using System;
using System.IO;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;

using AIChat.Utils;
using AIChat.Core;
using ChillAIMod;
using System.ComponentModel;
using System.Diagnostics;

namespace AIChatConsoleApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Log.Info("Console version of AIChat started.");
            string memoryFilePath = Path.Combine("ChillAIMod", "memory.txt");

            while (true)
            {
                string prompt = Console.ReadLine();
                var requestContext = new LLMRequestContext
                {
                    ApiUrl = "http://127.0.0.1:11434/api/chat",
                    // ApiKey = "",
                    ModelName = "llama3",
                    SystemPrompt = AIConsole.DefaultPersona,
                    UserPrompt = prompt,
                    UseLocalOllama = true,
                    LogApiRequestBody = false,
                    ThinkMode = ThinkMode.Default,
                    HierarchicalMemory = null,
                    LogHeader = "AIChatConsoleApp",
                    FixApiPathForThinkMode = true
                };

                await AIConsole.SendLLMRequest(
                    requestContext,
                    onSuccess: (response) =>
                    {
                        Log.Debug($"请求成功，响应内容: {response}");
                        // AIConsole.ProcessStandardResponse(response, requestContext.UseLocalOllama);

                        AIConsole.ProcessJsonResponse(response);
                    },
                    onFailure: (error, code) =>
                    {
                        Log.Error($"请求失败，错误: {error}，响应码: {code}");
                    }
                );
            }
        }
    }

    class AIConsole
    {
        public const string DefaultPersona = @"
            角色：Satone（さとね），热爱写小说的日系女孩，语气温柔、活泼，有想象力。
            场景：和用户视频通话，一起共事，回复要贴合女孩的语气。

            【强制规则（必须100%遵守）】
            1. 仅返回JSON，无任何额外文字、换行、注释；
            2. JSON字段必须完整，且非空（Voice/Subtitle 禁止为空字符串）；
            3. Voice字段必须是日语（平假名/片假名/汉字），Subtitle是对应的中文翻译；
            4. Emotion只能选：Happy/Confused/Sad/Fun/Agree/Drink/Wave/Think；

            【JSON模板（必须严格照做）】
            {
                ""Emotion"": ""[情绪]"",
                ""Voice"": ""[日语回复]"",
                ""Subtitle"": ""[中文字幕]""
            }

            【示例】
            用户问「你好呀」，回复：
            {
                ""Emotion"": ""Happy"",
                ""Voice"": ""こんにちは～私はさとねです✨"",
                ""Subtitle"": ""你好呀～我是Satone✨""
            }
            用户问「什么情况」，回复：
            {
                ""Emotion"": ""Confused"",
                ""Voice"": ""どうしたの？何か問題があるの？"",
                ""Subtitle"": ""怎么啦？是有什么问题吗？""
            }
        ";

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static void ProcessStandardResponse(string response, bool isOllama)
        {
            string fullResponse = isOllama 
                ? ResponseParser.ExtractContentFromOllama(response) 
                : ResponseParser.ExtractContentRegex(response);
            Log.Debug($"[AIChat Console]: [Full]: {fullResponse}");
            LLMStandardResponse parsedResponse = LLMUtils.ParseStandardResponse(fullResponse);
            Log.Message($"[AIChat Console]: [result]: {parsedResponse.Success}");
            Log.Message($"[AIChat Console]: [Emotion]: {parsedResponse.EmotionTag}");
            Log.Message($"[AIChat Console]: [Voice]: {parsedResponse.VoiceText}");
            Log.Message($"[AIChat Console]: [Subtitle]: {parsedResponse.SubtitleText}");
        }

        public static void ProcessJsonResponse(string response)
        {
            try
            {
                dynamic fullResponse = Newtonsoft.Json.JsonConvert.DeserializeObject(response);
                
                string nestedJson = fullResponse.message.content;
                
                dynamic aiReply = Newtonsoft.Json.JsonConvert.DeserializeObject(nestedJson);
                
                bool isCommand = aiReply.IsCommand;
                string emotion = aiReply.Emotion;
                string voice = aiReply.Voice;
                string subtitle = aiReply.Subtitle;

                Log.Message($"isCommand: {isCommand}");
                Log.Message($"emotion: {emotion}");
                Log.Message($"voice: {voice}");
                Log.Message($"subtitle: {subtitle}");
            }
            catch (Exception ex)
            {
                Log.Error($"JSON解析失败:{ex.Message}");
            }
        }

        public static async Task SendLLMRequest(LLMRequestContext requestContext, Action<string> onSuccess, Action<string, long> onFailure)
        {
            try
            {
                string jsonBody = LLMUtils.BuildRequestBody(requestContext);
                string apiUrl = LLMUtils.GetApiUrlForThinkMode(requestContext);

                var requestContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                {
                    Content = requestContent
                };

                if (!requestContext.UseLocalOllama && !string.IsNullOrEmpty(requestContext.ApiKey))
                {
                    requestMessage.Headers.Add("Authorization", $"Bearer {requestContext.ApiKey}");
                }

                Log.Error($"[{requestContext.LogHeader}] 正在等待 LLM API 响应...");
                var startTime = DateTime.UtcNow;

                var response = await _httpClient.SendAsync(requestMessage);

                double elapsedSeconds = (DateTime.UtcNow - startTime).TotalSeconds;
                Log.Error($"[{requestContext.LogHeader}] LLM 响应完成，耗时: {elapsedSeconds:F2} 秒");

                if (response.IsSuccessStatusCode)
                {
                    string rawResponse = await response.Content.ReadAsStringAsync();
                    onSuccess?.Invoke(rawResponse);
                }
                else
                {
                    string errorMsg = $"HTTP 请求失败: {response.ReasonPhrase}";
                    long responseCode = (long)response.StatusCode;
                    Log.Error($"[{requestContext.LogHeader}] {errorMsg} (响应码: {responseCode})");
                    onFailure?.Invoke(errorMsg, responseCode);
                }
            }
            catch (TaskCanceledException ex)
            {
                string errorMsg = $"请求超时: {ex.Message}";
                Log.Error($"[{requestContext.LogHeader}] {errorMsg}");
                onFailure?.Invoke(errorMsg, -2);
            }
            catch (Exception ex)
            {
                string errorMsg = $"请求异常: {ex.Message}";
                Log.Error($"[{requestContext.LogHeader}] {errorMsg}");
                onFailure?.Invoke(errorMsg, -1);
            }
        }
    }
}