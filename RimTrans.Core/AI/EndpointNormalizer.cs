using System;
using System.Text.RegularExpressions;

namespace RimTrans.Core.AI
{
    /// <summary>
    /// 端点规范化（对齐 RimTuber AIProviderManager.NormalizeEndpointUrl，全局规则强制）：
    /// 已含 completions 不动；含版本段（/v1 /v4 /v1beta /openai /api/paas…）补 /chat/completions；纯域名补 /v1/chat/completions。
    /// </summary>
    public static class EndpointNormalizer
    {
        public static string Normalize(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            url = url.Trim().TrimEnd('/');
            if (url.EndsWith("chat/completions", StringComparison.OrdinalIgnoreCase)
                || url.EndsWith("/completions", StringComparison.OrdinalIgnoreCase))
                return url;
            if (Regex.IsMatch(url, @"/v\d+[a-z]*(/|$)", RegexOptions.IgnoreCase)
                || url.EndsWith("/openai", StringComparison.OrdinalIgnoreCase)
                || url.Contains("/api/paas", StringComparison.OrdinalIgnoreCase))
                return url + "/chat/completions";
            return url + "/v1/chat/completions";
        }
    }
}
