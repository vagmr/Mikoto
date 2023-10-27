﻿using System;
using System.Windows;
using System.Windows.Controls;
using TranslatorLibrary.Translator;
using TTSHelperLibrary.TTSGenerator;

namespace MisakaTranslator_WPF.SettingsPages.TTSPages
{
    /// <summary>
    /// AzureTransSettingsPage.xaml 的交互逻辑
    /// </summary>
    public partial class AzureTTSSettingsPage : Page
    {
        public AzureTTSSettingsPage()
        {
            InitializeComponent();
            AzureTTSSecretKeyBox.Text = Common.appSettings.AzureTTSSecretKey;
            AzureTTSLocationBox.Text = Common.appSettings.AzureTTSLocation;
            HttpProxyBox.Text = Common.appSettings.AzureTTSProxy;
            AzureTTS.ProxyString = Common.appSettings.AzureTTSProxy;
            TestDstVoice.Text = Common.appSettings.AzureTTSVoice;
        }

        private void ApplyBtn_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(AzureTTS.GetUrl_allpyAPI());
        }

        private void BillBtn_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(AzureTTS.GetUrl_bill());
        }

        private async void TransTestBtn_Click(object sender, RoutedEventArgs e)
        {
            Common.appSettings.AzureTTSSecretKey = AzureTTSSecretKeyBox.Text;
            Common.appSettings.AzureTTSLocation = AzureTTSLocationBox.Text;
            AzureTTS azureTTS = new AzureTTS();
            azureTTS.TTSInit(Common.appSettings.AzureTTSSecretKey, Common.appSettings.AzureTTSLocation);
            await azureTTS.TextToSpeechAsync(TestSrcText.Text, TestDstVoice.Text);
            if (azureTTS.ErrorMessage != string.Empty)
            {
                HandyControl.Controls.Growl.Error(azureTTS.ErrorMessage);
            }
        }

        private void HttpProxyBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string text = HttpProxyBox.Text.Trim();
            Common.appSettings.AzureTTSProxy = text;
            AzureTTS.ProxyString = text;
        }

        private void TestDstVoice_LostFocus(object sender, RoutedEventArgs e)
        {
            Common.appSettings.AzureTTSVoice = TestDstVoice.Text;
        }

        private void VoiceNameQuery_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(AzureTTS.GetUrl_VoiceList());
        }
    }
}