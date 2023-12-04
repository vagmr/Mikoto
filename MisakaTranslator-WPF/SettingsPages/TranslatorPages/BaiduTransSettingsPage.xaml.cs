﻿using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using TranslatorLibrary.Translator;

namespace MisakaTranslator_WPF.SettingsPages.TranslatorPages
{
    /// <summary>
    /// BaiduTransSettingsPage.xaml 的交互逻辑
    /// </summary>
    public partial class BaiduTransSettingsPage : Page
    {
        public BaiduTransSettingsPage()
        {
            InitializeComponent();
            BDTransAppIDBox.Text = Common.appSettings.BDappID;
            BDTransSecretKeyBox.Text = Common.appSettings.BDsecretKey;
        }

        private async void AuthTestBtn_Click(object sender, RoutedEventArgs e)
        {
            Common.appSettings.BDappID = BDTransAppIDBox.Text;
            Common.appSettings.BDsecretKey = BDTransSecretKeyBox.Text;

            if (BDTransAppIDBox.Text.Length == 24)
            {
                HandyControl.Controls.Growl.Error($"百度翻译{Application.Current.Resources["APITest_Error_Hint"]}\nDo not use ai.baidu.com endpoint.");
                return;
            }

            ITranslator BDTrans = new BaiduTranslator();
            BDTrans.TranslatorInit(BDTransAppIDBox.Text, BDTransSecretKeyBox.Text);

            if (await BDTrans.TranslateAsync("apple", "zh", "en") != null)
            {
                HandyControl.Controls.Growl.Success($"百度翻译{Application.Current.Resources["APITest_Success_Hint"]}");
            }
            else
            {
                HandyControl.Controls.Growl.Error($"百度翻译{Application.Current.Resources["APITest_Error_Hint"]}\n{BDTrans.GetLastError()}");
            }
        }

        private void ApplyBtn_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo(BaiduTranslator.GetUrl_API()) { UseShellExecute = true });
        }

        private void DocBtn_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo(BaiduTranslator.GetUrl_Doc()) { UseShellExecute = true });
        }

        private void BillBtn_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo(BaiduTranslator.GetUrl_Bill()) { UseShellExecute = true });
        }

        private async void TransTestBtn_Click(object sender, RoutedEventArgs e)
        {
            ITranslator BDTrans = new BaiduTranslator();
            BDTrans.TranslatorInit(Common.appSettings.BDappID, Common.appSettings.BDsecretKey);
            string res = await BDTrans.TranslateAsync(TestSrcText.Text, TestDstLang.Text, TestSrcLang.Text);

            if (res != null)
            {
                HandyControl.Controls.MessageBox.Show(res, Application.Current.Resources["MessageBox_Result"].ToString());
            }
            else
            {
                HandyControl.Controls.Growl.Error(
                    $"百度翻译{Application.Current.Resources["APITest_Error_Hint"]}\n{BDTrans.GetLastError()}");
            }
        }
    }
}