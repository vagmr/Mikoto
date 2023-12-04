﻿using System.Windows;
using System.Windows.Controls;
using TranslatorLibrary.Translator;

namespace MisakaTranslator_WPF.SettingsPages.TranslatorPages
{
    /// <summary>
    /// DreyeTransSettingsPage.xaml 的交互逻辑
    /// </summary>
    public partial class DreyeTransSettingsPage : Page
    {
        public DreyeTransSettingsPage()
        {
            InitializeComponent();
            PathBox.Text = Common.appSettings.DreyePath;
        }

        private void ChoosePathBtn_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = Application.Current.Resources["DreyeTransSettingsPage_ChoosePathHint"].ToString();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (string.IsNullOrEmpty(dialog.SelectedPath))
                {
                    HandyControl.Controls.Growl.Error(Application.Current.Resources["FilePath_Null_Hint"].ToString());
                }
                else
                {
                    PathBox.Text = dialog.SelectedPath;
                    Common.appSettings.DreyePath = PathBox.Text;
                }
            }
        }

        private async void TransTestBtn_Click(object sender, RoutedEventArgs e)
        {
            DreyeTranslator Trans = new DreyeTranslator();
            Trans.TranslatorInit(Common.appSettings.DreyePath, "");
            string res = await Trans.TranslateAsync(TestSrcText.Text, "zh", TestSrcLang.Text);
            if (res != null)
            {
                HandyControl.Controls.MessageBox.Show(res, Application.Current.Resources["MessageBox_Result"].ToString());
            }
            else
            {
                HandyControl.Controls.Growl.Error(
                    $"译典通翻译{Application.Current.Resources["APITest_Error_Hint"]}\n{Trans.GetLastError()}");
            }
        }
    }
}