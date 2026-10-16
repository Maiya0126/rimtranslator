using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace RimTrans.Core.AI
{
    public class AiSettings
    {
        public string Provider = "Player2";
        public string Endpoint = "http://127.0.0.1:4315/v1/chat/completions";
        public string ApiKey = "";
        public string Model = "Default";
        public const string Player2ClientId = "019e12ba-6062-79ae-8c76-de13bea9af7a";
        public const string Player2BaseUrl = "http://localhost:4315";
    }

    public class AiException : Exception
    {
        public bool IsEndpoint405;
        public AiException(string message, bool is405 = false) : base(message) { IsEndpoint405 = is405; }
    }

    /// <summary>
    /// OpenAI 兼容 Chat 客户端 + Player2 本地服务探测/登录（对齐 RimTuber AIProviderManager 流程）。
    /// </summary>
    public class AiClient
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };
        private readonly AiSettings _settings;
        private string? _player2Key;

        public AiClient(AiSettings settings) => _settings = settings;

        public async Task<bool> TestConnectionAsync()
        {
            if (_settings.Provider == "Player2")
            {
                string? key = await TryPlayer2LoginAsync();
                if (key == null)
                    throw new AiException("Player2 桌面程序未启动或未监听 4315 端口。请先启动并登录 Player2 桌面端，再点测试连接。");
                _player2Key = key;
                var reply = await ChatAsync("Reply with exactly: OK");
                return reply.Trim().Length > 0;
            }
            var r = await ChatAsync("Reply with exactly: OK");
            return r.Trim().Length > 0;
        }

        private static async Task<string?> TryPlayer2LoginAsync()
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var health = await Http.GetAsync(AiSettings.Player2BaseUrl + "/v1/health", cts.Token);
                if (!health.IsSuccessStatusCode) return null;
            }
            catch { return null; }

            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var login = new HttpRequestMessage(HttpMethod.Post,
                    $"{AiSettings.Player2BaseUrl}/v1/login/web/{AiSettings.Player2ClientId}")
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
                using var resp = await Http.SendAsync(login, cts.Token);
                if (!resp.IsSuccessStatusCode) return null;
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
                return doc.RootElement.TryGetProperty("p2Key", out var k) ? k.GetString() : null;
            }
            catch { return null; }
        }

        public async Task<string> ChatAsync(string userPrompt, string? systemPrompt = null, int maxTokens = 4000)
        {
            string endpoint = EndpointNormalizer.Normalize(_settings.Endpoint);
            var body = new
            {
                model = string.IsNullOrEmpty(_settings.Model) ? "Default" : _settings.Model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt ?? "You are a helpful assistant." },
                    new { role = "user", content = userPrompt },
                },
                max_tokens = maxTokens,
                temperature = 0.3,
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            if (_settings.Provider == "Player2")
            {
                if (_player2Key == null)
                {
                    string? key = await TryPlayer2LoginAsync()
                        ?? throw new AiException("Player2 桌面程序未启动或未监听 4315 端口。请先启动并登录 Player2 桌面端。");
                    _player2Key = key;
                }
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _player2Key);
                req.Headers.Add("player2-game-key", AiSettings.Player2ClientId);
            }
            else if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
            }
            if (_settings.Provider == "OpenRouter")
            {
                req.Headers.Add("HTTP-Referer", "https://github.com/rimtranslator");
                req.Headers.Add("X-Title", "RimTranslator");
            }

            using var resp2 = await Http.SendAsync(req);
            if (resp2.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
                throw new AiException("端点缺少路径，已自动补全，请重试", is405: true);
            string text = await resp2.Content.ReadAsStringAsync();
            if (!resp2.IsSuccessStatusCode)
                throw new AiException($"HTTP {(int)resp2.StatusCode}: {Truncate(text, 300)}");
            try
            {
                using var doc = JsonDocument.Parse(text);
                return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            }
            catch (Exception ex)
            {
                throw new AiException("响应解析失败: " + Truncate(text, 300) + " / " + ex.Message);
            }
        }

        private static string Truncate(string s, int len) => s.Length <= len ? s : s[..len] + "…";
    }
}
