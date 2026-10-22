using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace AuroraLauncher.Core;

/// <summary>从 MCmod（mcmod.cn）检索模组的中文名，供「中文名 | 英文名」显示。</summary>
public static class McmodService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly Dictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Missing = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastRequest = DateTime.MinValue;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        return client;
    }

    /// <summary>返回 MCmod 收录的中文名；找不到返回 null。</summary>
    public static async Task<string> GetChineseNameAsync(string englishName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(englishName)) return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(englishName, out var cached)) return cached;
            if (Missing.Contains(englishName)) return null;
        }

        var requestOk = false;
        try
        {
            var url = "https://search.mcmod.cn/s?key=" + Uri.EscapeDataString(englishName);
            string html;
            await Gate.WaitAsync(ct);
            try
            {
                var wait = 250 - (DateTime.UtcNow - _lastRequest).TotalMilliseconds;
                if (wait > 0) await Task.Delay((int)wait, ct);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(12));
                html = await Client.GetStringAsync(url, cts.Token);
                _lastRequest = DateTime.UtcNow;
                requestOk = true;
            }
            finally
            {
                Gate.Release();
            }

            var matches = Regex.Matches(html,
                "<a[^>]+href=\"https?://www\\.mcmod\\.cn/class/[^\"]+\"[^>]*>(.*?)</a>",
                RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                var title = WebUtility.HtmlDecode(Regex.Replace(match.Groups[1].Value, "<[^>]+>", "")).Trim();
                title = Regex.Replace(title, @"\s*[-|].*(MC百科|mcmod).*$", "", RegexOptions.IgnoreCase).Trim();
                if (title.Length == 0) continue;

                var pair = Regex.Match(title, @"^\s*(?:\[[^\]]*\]\s*)?(.+?)\s*\((.*?)\)\s*$");
                if (!pair.Success)
                {
                    // 没有括号英文名时无法核对，跳过（避免显示错误的中文名）
                    continue;
                }

                var chinese = pair.Groups[1].Value.Trim();
                var english = pair.Groups[2].Value.Trim();
                if (!ContainsChinese(chinese) || chinese.Equals(english, StringComparison.OrdinalIgnoreCase))
                    continue;

                // 只有英文名匹配（忽略大小写、空格与标点）时才采用，避免张冠李戴
                if (IsSameName(english, englishName))
                {
                    Cache[englishName] = chinese;
                    return chinese;
                }
            }
        }
        catch
        {
            // 网络 / 站点限制时静默失败，由调用方回退到英文原名
        }
        if (requestOk) lock (Cache) Missing.Add(englishName);
        return null;
    }

    private static string NormalizeName(string text) =>
        new string((text ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static bool IsSameName(string candidate, string query)
    {
        var a = NormalizeName(candidate);
        var b = NormalizeName(query);
        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b) return true;
        if (a.Length < 3 || b.Length < 3) return false;
        return a.Contains(b) || b.Contains(a);
    }

    private static bool ContainsChinese(string text) =>
        !string.IsNullOrEmpty(text) && text.Any(c => c >= 0x4E00 && c <= 0x9FFF);
}
