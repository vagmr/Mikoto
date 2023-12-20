﻿using ArtificialTransHelperLibrary;
using FontAwesome.WPF;
using HandyControl.Controls;
using KeyboardMouseHookLibrary;
using MecabHelperLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using TextHookLibrary;
using TextRepairLibrary;
using TranslatorLibrary;
using TranslatorLibrary.Translator;
using TransOptimizationLibrary;
using TTSHelperLibrary;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MisakaTranslator_WPF
{
    /// <summary>
    /// TranslateWindow.xaml 的交互逻辑
    /// </summary>
    public partial class TranslateWindow
    {
        public DispatcherTimer dtimer;//定时器 定时将窗口置顶

        private ArtificialTransHelper _artificialTransHelper;

        private MecabHelper _mecabHelper;
        private BeforeTransHandle _beforeTransHandle;
        private AfterTransHandle _afterTransHandle;
        private ITranslator _translator1; //第一翻译源
        private ITranslator _translator2; //第二翻译源

        private string _currentsrcText; //当前源文本内容

        public string SourceTextFont; //源文本区域字体
        public int SourceTextFontSize; //源文本区域字体大小

        private Queue<string> _gameTextHistory; //历史文本
        private static KeyboardMouseHook hook; //全局键盘鼠标钩子
        public volatile bool IsOCRingFlag; //线程锁:判断是否正在OCR线程中，保证同时只有一组在跑OCR
        public bool IsNotPausedFlag; //是否处在暂停状态（专用于OCR）,为真可以翻译

        private bool _enableShowSource; //是否显示原文

        private readonly object _saveTransResultLock = new(); // 读写数据库和_gameTextHistory的线程锁

        private ITTS _TTS;

        private HWND winHandle;//窗口句柄，用于设置活动窗口，以达到全屏状态下总在最前的目的
        private TransWinSettingsWindow transWinSettingsWindow;

        //Effect 疑似有内存泄露 https://github.com/dotnet/wpf/issues/6782 use frozen
        private readonly DropShadowEffect dropShadowEffect = new();

        private readonly ObservableCollection<UIElement> _sourceTextCollection;


        public TranslateWindow()
        {
            InitializeComponent();

            _enableShowSource = true;

            _gameTextHistory = new Queue<string>();

            Topmost = true;
            UI_Init();
            IsOCRingFlag = false;


            _mecabHelper = new MecabHelper(Common.appSettings.Mecab_DicPath);
            if (!_mecabHelper.EnableMecab && Common.appSettings.Mecab_DicPath != string.Empty)
            {
                Growl.InfoGlobal(Application.Current.Resources["TranslateWin_NoMeCab_Hint"].ToString());
            }

            TTS_Init();

            IsNotPausedFlag = true;
            if (Common.appSettings.HttpProxy != "")
            {
                TranslatorCommon.SetHttpProxiedClient(Common.appSettings.HttpProxy);
            }
            _translator1 = TranslatorAuto(Common.appSettings.FirstTranslator);
            _translator2 = TranslatorAuto(Common.appSettings.SecondTranslator);

            _beforeTransHandle = new BeforeTransHandle(Convert.ToString(Common.GameID), Common.UsingSrcLang, Common.UsingDstLang);
            _afterTransHandle = new AfterTransHandle(_beforeTransHandle);

            _artificialTransHelper = new ArtificialTransHelper(Convert.ToString(Common.GameID));

            if (Common.TransMode == TransMode.Hook)
            {
                Common.textHooker.MeetHookAddressMessageReceived += ProcessAndDisplayTranslation;
            }
            else if (Common.TransMode == TransMode.Ocr)
            {
                MouseKeyboardHook_Init();
            }

            Dispatcher.BeginInvoke(() =>
            {
                transWinSettingsWindow = new TransWinSettingsWindow(this);
            });

            _sourceTextCollection = new ObservableCollection<UIElement>();
            DataContext = _sourceTextCollection;
        }

        private void TTS_Init()
        {
            DispatcherOperation dispatcherOperation = null;
            switch (Common.appSettings.SelectedTTS)
            {
                case TTSMode.Local:
                    var localTTS = new LocalTTS();
                    if (!string.IsNullOrWhiteSpace(Common.appSettings.LocalTTSVoice))
                    {
                        dispatcherOperation = Dispatcher.BeginInvoke(() =>
                            {
                                localTTS.SetTTSVoice(Common.appSettings.LocalTTSVoice);
                                localTTS.SetVolume(Common.appSettings.LoaclTTSVolume);
                                localTTS.SetRate(Common.appSettings.LocaTTSRate);
                                _TTS = localTTS;
                            });
                    }
                    else
                    {
                        _TTS = null;
                    }
                    break;
                case TTSMode.Azure:
                    if (!string.IsNullOrWhiteSpace(Common.appSettings.AzureTTSVoice)
                        && !string.IsNullOrWhiteSpace(Common.appSettings.AzureTTSSecretKey)
                        && !string.IsNullOrWhiteSpace(Common.appSettings.AzureTTSLocation)
                        )
                    {
                        var azureTTS = new AzureTTS
                        {
                            ProxyString = Common.appSettings.AzureTTSProxy
                        };
                        azureTTS.TTSInit(Common.appSettings.AzureTTSSecretKey, Common.appSettings.AzureTTSLocation, Common.appSettings.AzureTTSVoice);
                        _TTS = azureTTS;
                    }
                    else
                    {
                        _TTS = null;
                    }
                    break;
            }
            Dispatcher.BeginInvoke(() =>
            {
                dispatcherOperation?.Wait();
                if (_TTS == null)
                {
                    Growl.InfoGlobal(Application.Current.Resources["TranslateWin_NoTTS_Hint"].ToString());
                }
            });
        }

        /// <summary>
        /// 键盘鼠标钩子初始化
        /// </summary>
        private void MouseKeyboardHook_Init()
        {
            if (hook == null)
            {
                hook = new KeyboardMouseHook();
                bool r = false;

                if (Common.UsingHotKey.IsMouse)
                {
                    hook.OnMouseActivity += Hook_OnMouseActivity;
                    if (Common.UsingHotKey.MouseButton == System.Windows.Forms.MouseButtons.Left)
                    {
                        r = hook.Start(true, 1);
                    }
                    else if (Common.UsingHotKey.MouseButton == System.Windows.Forms.MouseButtons.Right)
                    {
                        r = hook.Start(true, 2);
                    }
                }
                else
                {
                    hook.onKeyboardActivity += Hook_OnKeyBoardActivity;
                    int keycode = (int)Common.UsingHotKey.KeyCode;
                    r = hook.Start(false, keycode);
                }

                if (!r)
                {
                    Growl.ErrorGlobal(Application.Current.Resources["Hook_Error_Hint"].ToString());
                }
            }


        }

        /// <summary>
        /// UI方面的初始化
        /// </summary>
        private void UI_Init()
        {

            _enableShowSource = Common.appSettings.TF_ShowSourceText;
            if (_enableShowSource)
            {
                ShowSourceButton.SetValue(Awesome.ContentProperty, FontAwesomeIcon.Eye);
            }
            else
            {
                ShowSourceButton.SetValue(Awesome.ContentProperty, FontAwesomeIcon.EyeSlash);
            }

            SourceTextFontSize = (int)Common.appSettings.TF_SrcTextSize;
            FirstTransText.FontSize = Common.appSettings.TF_FirstTransTextSize;
            SecondTransText.FontSize = Common.appSettings.TF_SecondTransTextSize;

            SourceTextFont = Common.appSettings.TF_SrcTextFont;
            FirstTransText.FontFamily = new FontFamily(Common.appSettings.TF_FirstTransTextFont);
            SecondTransText.FontFamily = new FontFamily(Common.appSettings.TF_SecondTransTextFont);

            FirstTransText.Stroke = Common.appSettings.TF_FirstWhiteStrokeIsChecked ? Brushes.White : Brushes.Black;
            SecondTransText.Stroke = Common.appSettings.TF_SecondWhiteStrokeIsChecked ? Brushes.White : Brushes.Black;

            BrushConverter brushConverter = new();
            FirstTransText.Fill = (Brush)brushConverter.ConvertFromString(Common.appSettings.TF_FirstTransTextColor);
            SecondTransText.Fill = (Brush)brushConverter.ConvertFromString(Common.appSettings.TF_SecondTransTextColor);

            this.Background = (Brush)brushConverter.ConvertFromString(Common.appSettings.TF_BackColor);

            if (int.Parse(Common.appSettings.TF_LocX) != -1 && int.Parse(Common.appSettings.TF_SizeW) != 0)
            {
                this.Left = int.Parse(Common.appSettings.TF_LocX);
                this.Top = int.Parse(Common.appSettings.TF_LocY);
                this.Width = int.Parse(Common.appSettings.TF_SizeW);
                this.Height = int.Parse(Common.appSettings.TF_SizeH);
            }

            dropShadowEffect.Opacity = 1;
            dropShadowEffect.ShadowDepth = 0;
            dropShadowEffect.BlurRadius = 6;
        }

        /// <summary>
        /// 根据翻译器名称自动返回翻译器类实例(包括初始化)
        /// </summary>
        /// <param name="translatorName"></param>
        /// <returns></returns>
        public static ITranslator TranslatorAuto(string translatorName)
        {
            ITranslator translator;
            switch (translatorName)
            {
                case "BaiduTranslator":
                    translator = new BaiduTranslator();
                    translator.TranslatorInit(Common.appSettings.BDappID, Common.appSettings.BDsecretKey);
                    return translator;
                case "TencentOldTranslator":
                    translator = new TencentOldTranslator();
                    translator.TranslatorInit(Common.appSettings.TXOSecretId, Common.appSettings.TXOSecretKey);
                    return translator;
                case "CaiyunTranslator":
                    translator = new CaiyunTranslator();
                    translator.TranslatorInit(Common.appSettings.CaiyunToken);
                    return translator;
                case "XiaoniuTranslator":
                    translator = new XiaoniuTranslator();
                    translator.TranslatorInit(Common.appSettings.xiaoniuApiKey);
                    return translator;
                case "IBMTranslator":
                    translator = new IBMTranslator();
                    translator.TranslatorInit(Common.appSettings.IBMApiKey, Common.appSettings.IBMURL);
                    return translator;
                case "YandexTranslator":
                    translator = new YandexTranslator();
                    translator.TranslatorInit(Common.appSettings.YandexApiKey);
                    return translator;
                case "YoudaoZhiyun":
                    translator = new YoudaoZhiyun();
                    translator.TranslatorInit(Common.appSettings.YDZYAppId, Common.appSettings.YDZYAppSecret);
                    return translator;
                case "GoogleCNTranslator":
                    translator = new GoogleCNTranslator();
                    translator.TranslatorInit();
                    return translator;
                case "JBeijingTranslator":
                    translator = new JBeijingTranslator();
                    translator.TranslatorInit(Common.appSettings.JBJCTDllPath);
                    return translator;
                case "KingsoftFastAITTranslator":
                    translator = new KingsoftFastAITTranslator();
                    translator.TranslatorInit(Common.appSettings.KingsoftFastAITPath);
                    return translator;
                case "DreyeTranslator":
                    translator = new DreyeTranslator();
                    translator.TranslatorInit(Common.appSettings.DreyePath);
                    return translator;
                case "DeepLTranslator":
                    translator = new DeepLTranslator();
                    translator.TranslatorInit(Common.appSettings.DeepLsecretKey, Common.appSettings.DeepLsecretKey);
                    return translator;
                case "ChatGPTTranslator":
                    translator = new ChatGPTTranslator();
                    translator.TranslatorInit(Common.appSettings.ChatGPTapiKey, Common.appSettings.ChatGPTapiUrl);
                    return translator;
                case "AzureTranslator":
                    translator = new AzureTranslator();
                    translator.TranslatorInit(Common.appSettings.AzureSecretKey, Common.appSettings.AzureLocation);
                    return translator;
                case "ArtificialTranslator":
                    translator = new ArtificialTranslator();
                    translator.TranslatorInit(Common.appSettings.ArtificialPatchPath);
                    return translator;
                default:
                    return null;
            }
        }

        /// <summary>
        /// 键盘点击事件
        /// </summary>
        void Hook_OnKeyBoardActivity(object sender)
        {
            TranslateEventOcr();
        }

        /// <summary>
        /// 鼠标点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Hook_OnMouseActivity(object sender, System.Drawing.Point e)
        {
            if (Common.isAllWindowCap && Environment.ProcessId != FindWindowInfo.GetProcessIDByHWND(FindWindowInfo.GetWindowHWND(e))
                || Common.OCRWinHwnd == FindWindowInfo.GetWindowHWND(e))
            {
                TranslateEventOcr();
            }
        }

        /// <summary>
        /// OCR事件
        /// </summary>
        /// <param name="isRenew">是否是重新获取翻译</param>
        private async void TranslateEventOcr(bool isRenew = false)
        {
            if (!IsNotPausedFlag && IsOCRingFlag)
                return;

            IsOCRingFlag = true;

            string srcText = null;
            for (int i = 0; i < 3; i++)
            {
                // 重新OCR不需要等待
                if (!isRenew)
                    await Task.Delay(Common.UsingOCRDelay);

                srcText = await Common.ocr.OCRProcessAsync();

                if (!string.IsNullOrEmpty(srcText))
                    break;
            }

            if (!string.IsNullOrEmpty(srcText))
            {
                if (Common.appSettings.OCRsource == "BaiduFanyiOCR" || Common.appSettings.OCRsource == "TencentOCR")
                    Application.Current.Dispatcher.Invoke(() => { FirstTransText.Text = srcText; });
                else
                {
                    if (!Common.appSettings.EachRowTrans) // 不启用分行翻译
                        if (Common.UsingSrcLang != "zh" && Common.UsingSrcLang != "ja")
                            srcText = srcText.Replace(Environment.NewLine, " ").Replace("\n", " ");
                        else
                            srcText = new string(srcText.Where(p => !char.IsWhiteSpace(p)).ToArray());

                    TranslateText(srcText, isRenew);
                }
            }
            else if (!string.IsNullOrEmpty(Common.ocr.GetLastError()))
                Growl.WarningGlobal(Common.appSettings.OCRsource + " Error: " + Common.ocr.GetLastError());
            else
                Growl.WarningGlobal(Application.Current.Resources["TranslateWindow_OCREmpty"].ToString());

            IsOCRingFlag = false;
        }
        DictResWindow _dictResWindow;
        private void DictArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            System.Windows.Controls.TextBox textBox = sender as System.Windows.Controls.TextBox;
            if (!string.IsNullOrWhiteSpace(textBox.SelectedText))
            {
                dtimer.Stop();
                _dictResWindow ??= new DictResWindow(_TTS);
                _dictResWindow.Search(textBox.SelectedText);
                _dictResWindow.Show();
                dtimer.Start();
            }
        }

        /// <summary>
        /// Hook模式下调用的事件
        /// </summary>
        public void ProcessAndDisplayTranslation(object sender, SolvedDataReceivedEventArgs e)
        {
            //1.得到原句
            string source = e.Data.Data;
            if (source == null && e.Data.HookFunc == "Clipboard")
            {
                return;
            }
            //2.进行去重
            string repairedText = TextRepair.RepairFun_Auto(Common.UsingRepairFunc, source);

            if (!Common.appSettings.EachRowTrans) // 不启用分行翻译
            {
                if (Common.UsingSrcLang == "zh" || Common.UsingSrcLang == "ja")
                {
                    repairedText = new string(repairedText.Where(p => !char.IsWhiteSpace(p)).ToArray()).Replace("<br>", "").Replace("</br>", "");
                }
                else
                {
                    repairedText = repairedText.Replace(Environment.NewLine, " ").Replace("\n", " ").Replace("<br>", " ").Replace("</br>", " ");
                }
            }

            TranslateText(repairedText);
        }

        /// <summary>
        /// 翻译
        /// </summary>
        /// <param name="repairedText">原文</param>
        /// <param name="isRenew">是否是重新获取翻译</param>
        private void TranslateText(string repairedText, bool isRenew = false)
        {
            //补充:如果去重之后的文本长度超过指定值（默认100），直接不翻译、不显示
            //补充2：如果去重后文本长度为0，则不翻译不显示
            if (repairedText.Length != 0 && repairedText.Length <= Common.appSettings.TransLimitNums)
            {

                _currentsrcText = repairedText;

                // 3. 更新原文
                UpdateSource(repairedText);

                // 分别获取两个翻译结果
                _ = TranslateApiSubmitASync(repairedText, 1, isRenew);
                _ = TranslateApiSubmitASync(repairedText, 2, isRenew);
            }
        }

        /// <summary>
        /// 更新原文
        /// 注意执行过程中调用了StackPanel等UI组件，必须交回主线程才能执行。
        /// </summary>
        /// <param name="repairedText">原文</param>
        private void UpdateSource(string repairedText)
        {
            // 使用BeginInvoke，在更新原文时可以去获取翻译
            Application.Current.Dispatcher.BeginInvoke(() =>
               {
                   _sourceTextCollection.Clear();
                   if (_enableShowSource)
                   {
                       //3.分词
                       if (Common.UsingSrcLang == "ja"
                           && _mecabHelper.EnableMecab
                           && (Common.appSettings.TF_EnablePhoneticNotation || Common.appSettings.TF_EnableColorful))
                       {
                           var mwi = _mecabHelper.SentenceHandle(repairedText);
                           //分词后结果显示
                           foreach (MecabWordInfo v in mwi)
                           {
                               StackPanel stackPanel = new()
                               {
                                   Orientation = Orientation.Vertical,
                                   Margin = new Thickness(5, 0, 0, 5)
                               };

                               System.Windows.Controls.TextBox textBox = new()
                               {
                                   IsReadOnly = true,
                                   BorderBrush = new SolidColorBrush(Colors.Transparent),
                                   Padding = new Thickness(0),
                                   Text = v.Word,
                                   Margin = new Thickness(0, 0, 0, 0),
                                   FontSize = SourceTextFontSize,
                                   Background = Brushes.Transparent,
                                   HorizontalAlignment = HorizontalAlignment.Center,
                               };
                               textBox.PreviewMouseLeftButtonUp += DictArea_MouseLeftButtonUp;
                               if (!string.IsNullOrEmpty(SourceTextFont))
                               {
                                   FontFamily fontFamily = new(SourceTextFont);
                                   textBox.FontFamily = fontFamily;
                               }
                               if (Common.appSettings.TF_EnableDropShadow)
                               {
                                   //加入原文的阴影
                                   textBox.Effect = (Effect)dropShadowEffect.GetCurrentValueAsFrozen();
                               }
                               if (Common.appSettings.TF_EnableColorful)
                               {
                                   textBox.TextAlignment = TextAlignment.Center;
                                   //根据不同词性跟字体上色
                                   textBox.Foreground = v.PartOfSpeech switch
                                   {
                                       "補助記号" or "空白" => Brushes.White,
                                       "動詞" => Brushes.YellowGreen,
                                       "形容詞" => Brushes.Orange,
                                       "判定詞" => Brushes.Yellow,
                                       "助動詞" => Brushes.LightGreen,
                                       "名詞" => Brushes.SkyBlue,
                                       "副詞" => Brushes.BlueViolet,
                                       "助詞" => Brushes.Wheat,
                                       "連体詞" => Brushes.Pink,
                                       "接続詞" => Brushes.Brown,
                                       "感動詞" => Brushes.Red,
                                       "指示詞" => Brushes.Plum,
                                       "代名詞" => Brushes.Olive,
                                       "接頭辞" => Brushes.LightGreen,
                                       "接尾辞" => Brushes.LightGoldenrodYellow,
                                       "形状詞" => Brushes.IndianRed,
                                       _ => Brushes.White,
                                   };
                               }
                               else
                               {
                                   textBox.Foreground = Brushes.White;
                               }

                               if (Common.appSettings.TF_EnablePhoneticNotation)
                               {
                                   // 假名或注释等的上标标签
                                   TextBlock NotationTextBlock = new();
                                   if (!string.IsNullOrEmpty(SourceTextFont))
                                   {
                                       FontFamily fontFamily = new(SourceTextFont);
                                       NotationTextBlock.FontFamily = fontFamily;
                                   }
                                   //选择平假名或者片假名
                                   NotationTextBlock.Text = Common.appSettings.TF_PhoneticNotationType switch
                                   {
                                       PhoneticNotationType.Hiragana => v.Hiragana,
                                       PhoneticNotationType.Katakana => v.Katakana,
                                       PhoneticNotationType.Romaji => v.Romaji,
                                       _ => v.Hiragana,
                                   };
                                   NotationTextBlock.Margin = new Thickness(0, 0, 0, 2);
                                   NotationTextBlock.HorizontalAlignment = HorizontalAlignment.Center;
                                   if (Common.appSettings.TF_EnableDropShadow)
                                   {
                                       //加入注音的阴影
                                       NotationTextBlock.Effect = (Effect)dropShadowEffect.GetCurrentValueAsFrozen();
                                   }
                                   if (SourceTextFontSize - 6.5 > 0)
                                   {
                                       NotationTextBlock.FontSize = SourceTextFontSize - 6.5;
                                       if (Common.appSettings.TF_EnableSuperBold)
                                       {
                                           NotationTextBlock.FontWeight = FontWeights.Bold;
                                           //注音加粗
                                       }
                                   }
                                   else
                                   {
                                       NotationTextBlock.FontSize = 1;
                                   }
                                   NotationTextBlock.Background = Brushes.Transparent;
                                   NotationTextBlock.Foreground = Brushes.White;
                                   stackPanel.Children.Add(NotationTextBlock);
                                   stackPanel.Children.Add(textBox);
                                   _sourceTextCollection.Add(stackPanel);
                               }
                               else
                               {
                                   _sourceTextCollection.Add(textBox);
                               }
                           }
                       }
                       else
                       {
                           System.Windows.Controls.TextBox textBox = new()
                           {
                               TextAlignment = TextAlignment.Left,
                               IsReadOnly = true,
                               Background = new SolidColorBrush(Colors.Transparent),
                               BorderBrush = new SolidColorBrush(Colors.Transparent),
                               Padding = new Thickness(0),
                               Margin = new Thickness(10, 0, 0, 10),
                               HorizontalAlignment = HorizontalAlignment.Left,
                           };
                           if (!string.IsNullOrEmpty(SourceTextFont))
                           {
                               FontFamily fontFamily = new(SourceTextFont);
                               textBox.FontFamily = fontFamily;
                           }
                           textBox.Text = repairedText;
                           textBox.TextWrapping = TextWrapping.Wrap;
                           textBox.FontSize = SourceTextFontSize;
                           textBox.Background = Brushes.Transparent;
                           textBox.PreviewMouseLeftButtonUp += DictArea_MouseLeftButtonUp;
                           if (Common.appSettings.TF_EnableDropShadow)
                           {
                               textBox.Effect = (Effect)dropShadowEffect.GetCurrentValueAsFrozen(); ;
                           }
                           textBox.Foreground = Brushes.White;
                           _sourceTextCollection.Add(textBox);
                       }
                   }
               });
        }

        /// <summary>
        /// 提交原文到翻译器，获取翻译结果并显示
        /// </summary>
        /// <param name="repairedText">原文</param>
        /// <param name="tranResultIndex">翻译框序号</param>
        /// <param name="isRenew">是否是重新获取翻译</param>
        private async Task TranslateApiSubmitASync(string repairedText, int tranResultIndex, bool isRenew = false)
        {
            //4.翻译前预处理 
            string beforeString = _beforeTransHandle.AutoHandle(repairedText);

            //5.提交翻译 
            string transRes = string.Empty;
            if (tranResultIndex == 1)
            {
                if (_translator1 != null)
                {
                    transRes = await _translator1.TranslateAsync(beforeString, Common.UsingDstLang, Common.UsingSrcLang);
                    if (transRes == null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Growl.WarningGlobal(_translator1.GetType().Name + ": " + _translator1.GetLastError());
                        });
                        return;
                    }
                }
            }
            else if (tranResultIndex == 2)
            {
                if (_translator2 != null)
                {
                    transRes = await _translator2.TranslateAsync(beforeString, Common.UsingDstLang, Common.UsingSrcLang);
                    if (transRes == null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Growl.WarningGlobal(_translator2.GetType().Name + ": " + _translator2.GetLastError());
                        });
                        return;
                    }
                }
            }

            //6.翻译后处理 
            string afterString = _afterTransHandle.AutoHandle(transRes);

            //7.翻译结果显示到窗口上 
            switch (tranResultIndex)
            {
                case 1:
                    _ = Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        FirstTransText.Text = afterString;
                        if (Common.appSettings.TF_EnableDropShadow)
                        {
                            FirstTransText.Effect = (Effect)dropShadowEffect.GetCurrentValueAsFrozen(); ;
                        }
                        else
                        {
                            FirstTransText.Effect = null;
                        }
                        //添加第一翻译源的阴影
                    }, DispatcherPriority.Send);
                    break;
                case 2:
                    _ = Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        SecondTransText.Text = afterString;
                        if (Common.appSettings.TF_EnableDropShadow)
                        {
                            SecondTransText.Effect = (Effect)dropShadowEffect.GetCurrentValueAsFrozen(); ;
                        }
                        else
                        {
                            SecondTransText.Effect = null;
                        }
                        //添加第二翻译源的阴影
                    }, DispatcherPriority.Send);
                    break;
            }

            if (!isRenew)
            {
                lock (_saveTransResultLock)
                {
                    //8.翻译结果记录到队列
                    // todo: 这是比较粗暴地添加历史记录，可以优化（时间排序等）
                    if (_gameTextHistory.Count > 10)
                    {
                        _gameTextHistory.Dequeue();
                    }
                    _gameTextHistory.Enqueue(repairedText + Environment.NewLine + afterString);

                    //9.翻译原句和结果记录到数据库 
                    if (Common.appSettings.ATon)
                    {
                        bool addRes = _artificialTransHelper.AddTrans(repairedText, afterString);
                        if (addRes == false)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                HandyControl.Data.GrowlInfo growlInfo = new()
                                {
                                    Message = Application.Current.Resources["ArtificialTransAdd_Error_Hint"].ToString(),
                                    WaitTime = 2
                                };
                                Growl.InfoGlobal(growlInfo);
                            });
                        }
                    }
                }
            }
        }


        private void ChangeSize_Item_Click(object sender, RoutedEventArgs e)
        {
            if ((bool)(sender as ToggleButton).IsChecked)
            {
                this.BorderThickness = new(3);
                this.ResizeMode = ResizeMode.CanResizeWithGrip;
                Growl.InfoGlobal(Application.Current.Resources["TranslateWin_DragBox_Hint"].ToString());
            }
            else
            {
                this.BorderThickness = new(0);
                this.ResizeMode = ResizeMode.CanResize;
            }
        }

        private void Exit_Item_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }


        private void Pause_Item_Click(object sender, RoutedEventArgs e)
        {
            if (Common.TransMode == TransMode.Hook)
            {
                if (Common.textHooker.Pause)
                {
                    PauseButton.SetValue(Awesome.ContentProperty, FontAwesomeIcon.Pause);
                }
                else
                {
                    PauseButton.SetValue(Awesome.ContentProperty, FontAwesomeIcon.Play);
                }
                Common.textHooker.Pause = !Common.textHooker.Pause;
            }
            else
            {
                if (IsNotPausedFlag)
                {
                    PauseButton.SetValue(Awesome.ContentProperty, FontAwesomeIcon.Play);
                }
                else
                {
                    PauseButton.SetValue(Awesome.ContentProperty, FontAwesomeIcon.Pause);
                }
                IsNotPausedFlag = !IsNotPausedFlag;
            }

        }

        private void ShowSource_Item_Click(object sender, RoutedEventArgs e)
        {
            _enableShowSource = !_enableShowSource;
            if (_enableShowSource)
            {
                ShowSourceButton.SetValue(Awesome.ContentProperty, FontAwesomeIcon.Eye);
            }
            else
            {
                ShowSourceButton.SetValue(Awesome.ContentProperty, FontAwesomeIcon.EyeSlash);
            }
            Common.appSettings.TF_ShowSourceText = _enableShowSource;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Common.appSettings.TF_LocX = Convert.ToString((int)this.Left);
            Common.appSettings.TF_LocY = Convert.ToString((int)this.Top);
            Common.appSettings.TF_SizeW = Convert.ToString((int)this.ActualWidth);
            Common.appSettings.TF_SizeH = Convert.ToString((int)this.ActualHeight);

            if (hook != null)
            {
                hook.Stop();
                hook = null;
            }

            if (Common.textHooker != null)
            {
                Common.textHooker.MeetHookAddressMessageReceived -= ProcessAndDisplayTranslation;
                Common.textHooker.StopHook();
                Common.textHooker = null;
            }

            dtimer.Stop();

            _mecabHelper.Dispose();

            try
            {
                //System.InvalidOperationException:“关闭 Window 之后，无法设置 Visibility，也无法调用 Show、ShowDialogor 或 WindowInteropHelper.EnsureHandle。”
                Common.mainWin.Visibility = Visibility.Visible;
            }
            catch (InvalidOperationException)
            { }
        }

        private void Settings_Item_Click(object sender, RoutedEventArgs e)
        {
            dtimer.Stop();
            transWinSettingsWindow.WindowState = WindowState.Normal;
            transWinSettingsWindow.Show();
        }

        private void History_Item_Click(object sender, RoutedEventArgs e)
        {
            var textbox = new HandyControl.Controls.TextBox();
            string his = string.Empty;
            string[] history = _gameTextHistory.ToArray();
            for (int i = history.Length - 1; i > 0; i--)
            {
                his += history[i] + "\n";
                his += "==================================\n";
            }
            textbox.Text = his;
            textbox.FontSize = 15;
            textbox.TextWrapping = TextWrapping.Wrap;
            textbox.TextAlignment = TextAlignment.Left;
            textbox.HorizontalScrollBarVisibility = ScrollBarVisibility.Visible;
            var window = new PopupWindow
            {
                PopupElement = textbox,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                BorderThickness = new Thickness(0, 0, 0, 0),
                MaxWidth = 600,
                MaxHeight = 300,
                MinWidth = 600,
                MinHeight = 300,
                Owner = this,
                Title = Application.Current.Resources["TranslateWin_History_Title"].ToString()
            };
            dtimer.Stop();
            window.Topmost = true;
            window.ShowDialog();
            dtimer.Start();
        }

        private void AddNoun_Item_Click(object sender, RoutedEventArgs e)
        {
            dtimer.Stop();
            AddOptWindow win = new(_currentsrcText);
            win.ShowDialog();
            dtimer.Start();
        }

        private void RenewOCR_Item_Click(object sender, RoutedEventArgs e)
        {
            if (Common.TransMode == TransMode.Ocr)
            {
                TranslateEventOcr(true);
            }
            else
            {
                _ = TranslateApiSubmitASync(_currentsrcText, 1, true);
                _ = TranslateApiSubmitASync(_currentsrcText, 2, true);
            }
        }

        private void Min_Item_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Lock_Item_Click(object sender, RoutedEventArgs e)
        {
            if (!(bool)(sender as ToggleButton).IsChecked)
            {
                this.Background = new SolidColorBrush(Colors.Transparent);
            }
            else
            {
                BrushConverter brushConverter = new();
                this.Background = (Brush)brushConverter.ConvertFromString(Common.appSettings.TF_BackColor);
            }
        }

        private void TTS_Item_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_currentsrcText))
                _TTS.SpeakAsync(_currentsrcText);
        }

        private void TransWin_Loaded(object sender, RoutedEventArgs e)
        {
            winHandle = (HWND)new WindowInteropHelper(this).Handle;//记录翻译窗口句柄

            dtimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            dtimer.Tick += TickWindowTopMost;
            dtimer.Start();

            Common.mainWin.Visibility = Visibility.Collapsed;
        }

        void TickWindowTopMost(object sender, EventArgs e)
        {
            if (this.WindowState != WindowState.Minimized)
            {
                //定时刷新窗口到顶层
                PInvoke.SetWindowPos(winHandle, HWND.HWND_TOPMOST, 0, 0, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE);
            }
        }

        private void ArtificialTransAdd_Item_Click(object sender, RoutedEventArgs e)
        {
            dtimer.Stop();
            ArtificialTransAddWindow win = new(_currentsrcText, FirstTransText.Text, SecondTransText.Text);
            win.ShowDialog();
            dtimer.Start();
        }
    }
}
