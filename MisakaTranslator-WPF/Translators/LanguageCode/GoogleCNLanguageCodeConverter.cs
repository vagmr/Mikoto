﻿using System.Globalization;

namespace MisakaTranslator
{
    public class GoogleCNLanguageCodeConverter : ILanguageCodeConverter
    {
        public static string GetLanguageCode(CultureInfo cultureInfo)
        {
            return cultureInfo.TwoLetterISOLanguageName switch
            {
                "zh" => "zh-CN",
                "zh-Hant" => "zh-TW",
                _ => cultureInfo.TwoLetterISOLanguageName,
            };
        }
    }
}
