﻿using Mikoto.Helpers.Network;
using Mikoto.Translators.Interfaces;
using System.Net.Http;
using System.Text.Json;
using System.Windows;

/*
 * ChatGPT translator integration
 * Author: bychv
 * API version: v1
 */
namespace Mikoto.Translators.Implementations
{
    public class ChatGPTTranslator : ITranslator
    {
        private ChatGPTTranslator() { }
        public static readonly string SIGN_UP_URL = "https://api.deepseek.com";
        public static readonly string BILL_URL = "https://api.deepseek.com/user/balance";
        public static readonly string DOCUMENT_URL = "https://platform.openai.com/docs/introduction/overview";
        private string openai_model = "deepseek-chat";
        private string format = "text"; //json_object
        private int max_tokens = 4096;
        private float temperature = 1.3f;

        private string? apiKey; //deepseek翻译API的密钥
        private string? apiUrl; //deepseek翻译API的URL
        private string errorInfo = string.Empty; //错误信息

        public string TranslatorDisplayName { get { return Application.Current.Resources["ChatGPTTranslator"].ToString()!; } }

        public string GetLastError()
        {
            return errorInfo;
        }

        public async Task<string?> TranslateAsync(string sourceText, string desLang, string srcLang)
        {
            string q = sourceText;

            if (sourceText == "" || desLang == "" || srcLang == "")
            {
                errorInfo = "Param Missing";
                return null;
            }
            string retString;
            string jsonParam = $"{{\"model\": \"{openai_model}\",\"messages\": [{{\"role\": \"system\", \"content\": \"根据原文逐行将{srcLang}文本翻译成{desLang}文本输出,保留文本的原始格式\"}},{{\"role\": \"user\", \"content\": \"{q}\"}}], \"response_format\": {{\"type\": \"{format}\"}}, \"max_tokens\": {max_tokens}, \"temperature\": {temperature}}}";
            var hc = CommonHttpClient.Instance;
            var req = new StringContent(jsonParam, null, "application/json");
            hc.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            try
            {
                retString = await (await hc.PostAsync(apiUrl, req)).Content.ReadAsStringAsync();
            }
            catch (HttpRequestException ex)
            {
                errorInfo = ex.Message;
                return null;
            }
            catch (TaskCanceledException ex)
            {
                errorInfo = ex.Message;
                return null;
            }
            finally
            {
                req.Dispose();
            }

            ChatResponse oinfo;

            try
            {
                oinfo = JsonSerializer.Deserialize<ChatResponse>(retString, TranslatorCommon.JsonSerializerOptions);
            }
            catch
            {
                errorInfo = "JsonConvert Error";
                return null;
            }

            try
            {
                return oinfo.choices[0].message.content;
            }
            catch
            {
                try
                {
                    var err = JsonSerializer.Deserialize<ChatResErr>(retString, TranslatorCommon.JsonSerializerOptions);
                    errorInfo = err.error.message;
                    return null;
                }
                catch
                {
                    errorInfo = "Unknown error";
                    return null;
                }
            }
        }

        public static ITranslator TranslatorInit(params string[] param)
        {
            ChatGPTTranslator chatGPTTranslator = new()
            {
                apiKey = param.First(),
                apiUrl = param.Last(),
            };
            return chatGPTTranslator;
        }
    }

#pragma warning disable 0649
    public struct ChatResponse
    {
        public string id;
        public string _object;
        public int created;
        public string model;
        public ChatUsage usage;
        public ChatChoice[] choices;
    }

    public struct ChatUsage
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
    }

    public struct ChatChoice
    {
        public ChatMessage message;
        public string finish_reason;
        public int index;
    }

    public struct ChatMessage
    {
        public string role;
        public string content;
    }

    public struct ChatResErr
    {
        public ChatError error;
    }

    public struct ChatError
    {
        public string message;
        public string type;
        public object param;
        public object code;
    }
}
