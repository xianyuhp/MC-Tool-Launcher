using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MCTL.Core;

public record SteamGame(long AppId, string Name, string ImageUrl, string PriceText, string StoreUrl);
public record SteamEdition(string Title, string Price);

public record HistoricalLow(decimal? Usd, DateTime? Date, string Title)
{
    public string Display => Usd == null
        ? "史低：暂无数据"
        : $"史低：${Usd:0.00}" + (Date != null ? $"（{Date:yyyy-MM-dd}）" : "");
}

/// <summary>国区历史最低价（多数据源，优先实时接口，失败时按汇率折算）。</summary>
public record PriceHistory(decimal? Cny, decimal? Usd, DateTime? Date, string Source)
{
    public string Display => Cny == null
        ? "国区史低：暂无数据"
        : $"国区史低：¥{Cny:0.00}" + (Date != null ? $"（{Date:yyyy-MM-dd}）" : "") + $"　来源：{Source}";
}

public static class SteamService
{
    private static HttpClient Client => Mc.McApi.Client;
    private static bool _cnSearchBlocked;

    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);

    /// <summary>清除接口返回文本里的 HTML 标签（例如 &lt;span class="discount_original_price"&gt;）。</summary>
    public static string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var clean = HtmlTag.Replace(text, " ");
        clean = WebUtility.HtmlDecode(clean).Replace('\u00A0', ' ');
        return Regex.Replace(clean, "\\s{2,}", " ").Trim();
    }

    /// <summary>访问 SteamDB 需要浏览器 UA，单独使用一个客户端。</summary>
    private static readonly HttpClient BrowserClient = CreateBrowserClient();

    private static HttpClient CreateBrowserClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        return client;
    }

    private static async Task<string> GetTextAsync(string url, int seconds, CancellationToken ct)
    {
        Exception last = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
                return await Client.GetStringAsync(url, timeout.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                last = ex;
                try { await Task.Delay(400, ct); } catch { }
            }
        }
        throw last ?? new Exception("请求失败");
    }

    private static decimal? _usdCnyRate;
    private static DateTime _rateTime;

    /// <summary>美元兑人民币汇率（6 小时缓存，失败时用 7.2 兜底）。</summary>
    public static async Task<decimal> GetUsdCnyRateAsync(CancellationToken ct = default)
    {
        if (_usdCnyRate != null && (DateTime.UtcNow - _rateTime).TotalHours < 6) return _usdCnyRate.Value;
        try
        {
            using var doc = JsonDocument.Parse(await Client.GetStringAsync("https://open.er-api.com/v6/latest/USD", ct));
            _usdCnyRate = doc.RootElement.GetProperty("rates").GetProperty("CNY").GetDecimal();
            _rateTime = DateTime.UtcNow;
        }
        catch
        {
            _usdCnyRate = 7.2m;
        }
        return _usdCnyRate.Value;
    }

    /// <summary>搜索 Steam 商店（国区优先，失败自动回退美区；中文名会自动翻译后再搜）。</summary>
    public static async Task<List<SteamGame>> SearchAsync(string term, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term)) return new List<SteamGame>();

        var results = await TrySearchAsync(term, ct);

        // 中文游戏名：同时用翻译后的英文再搜一次并合并，提高命中率
        if (ContainsChinese(term))
        {
            var translated = await TranslateToEnglishAsync(term, ct);
            if (!string.IsNullOrWhiteSpace(translated) &&
                !translated.Equals(term, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"中文名搜索：{term} → {translated}");
                var more = await TrySearchAsync(translated, ct);
                foreach (var game in more)
                    if (results.All(x => x.AppId != game.AppId))
                        results.Add(game);
            }
        }
        return results;
    }

    private static bool ContainsChinese(string text) =>
        text.Any(c => c >= 0x4E00 && c <= 0x9FFF);

    /// <summary>当前正在折扣的热销游戏（Steam 首页特惠）。网络失败时自动回退本地缓存。</summary>
    public static async Task<List<SteamGame>> GetDiscountedTopAsync(CancellationToken ct = default)
    {
        LastLoadNote = "";
        Exception last = null;

        foreach (var (cc, lang, attempts) in new[] { ("cn", "schinese", 2), ("us", "english", 1) })
        {
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var url = $"https://store.steampowered.com/api/featuredcategories?cc={cc}&l={lang}";
                    using var doc = JsonDocument.Parse(await GetTextAsync(url, 10, ct));
                    if (!doc.RootElement.TryGetProperty("specials", out var specials) ||
                        !specials.TryGetProperty("items", out var items)) continue;

                    var list = new List<SteamGame>();
                    foreach (var item in items.EnumerateArray())
                    {
                        var id = item.GetProperty("id").GetInt64();
                        var name = CleanText(item.GetProperty("name").GetString() ?? "");
                        var image = item.TryGetProperty("header_image", out var header) ? header.GetString()
                            : item.TryGetProperty("large_capsule_image", out var large) ? large.GetString()
                            : item.TryGetProperty("small_capsule_image", out var small) ? small.GetString() : "";
                        var discount = item.TryGetProperty("discount_percent", out var d) ? d.GetInt32() : 0;
                        var final = item.TryGetProperty("final_price", out var f) ? f.GetInt64() : 0;
                        var currency = cc == "us" ? "USD" : "CNY";
                        var priceText = final > 0 ? $"{(discount > 0 ? "-" + discount + "%  " : "")}{FormatPrice(final, currency)}" : "免费";
                        list.Add(new SteamGame(id, name, image ?? "", priceText, $"https://store.steampowered.com/app/{id}/"));
                    }
                    if (list.Count > 0)
                    {
                        SaveFeaturedCache(list);
                        return list;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Logger.Warn($"Steam 特惠列表失败（cc={cc}，第 {attempt} 次）：{ex.Message}");
                }

                try { await Task.Delay(500 * attempt, ct); }
                catch (OperationCanceledException) { throw; }
            }
        }

        // 网络完全不可用时用上次成功的数据，避免板块空白
        var cached = LoadFeaturedCache(out var savedAt);
        if (cached.Count > 0)
        {
            LastLoadNote = $"无法连接 Steam 商店，显示 {savedAt:MM-dd HH:mm} 的缓存";
            Logger.Warn("Steam 特惠改用本地缓存：" + (last?.Message ?? "无数据"));
            return cached;
        }

        LastLoadNote = "无法连接 Steam 商店（网络被拦截或超时），请开加速器后点「刷新特惠」";
        Logger.Warn("Steam 特惠加载失败且没有缓存：" + (last?.Message ?? "无数据"));
        return new List<SteamGame>();
    }

    /// <summary>上次加载的提示（用了缓存时告诉界面）。</summary>
    public static string LastLoadNote { get; private set; } = "";

    private static readonly string FeaturedCachePath = Path.Combine(Settings.ConfigDir, "steam-featured.json");

    private sealed class FeaturedCache
    {
        public DateTime SavedAt { get; set; }
        public List<SteamGame> Games { get; set; } = new();
    }

    /// <summary>把折扣列表写到本地，供下次网络异常时兜底。</summary>
    private static void SaveFeaturedCache(List<SteamGame> games)
    {
        try
        {
            Directory.CreateDirectory(Settings.ConfigDir);
            var payload = new FeaturedCache { SavedAt = DateTime.Now, Games = games };
            File.WriteAllText(FeaturedCachePath, JsonSerializer.Serialize(payload));
        }
        catch (Exception ex)
        {
            Logger.Warn("保存 Steam 缓存失败：" + ex.Message);
        }
    }

    /// <summary>读取上次成功保存的折扣列表。</summary>
    private static List<SteamGame> LoadFeaturedCache(out DateTime savedAt)
    {
        savedAt = DateTime.MinValue;
        try
        {
            if (!File.Exists(FeaturedCachePath)) return new List<SteamGame>();
            var payload = JsonSerializer.Deserialize<FeaturedCache>(File.ReadAllText(FeaturedCachePath));
            if (payload?.Games == null || payload.Games.Count == 0) return new List<SteamGame>();
            savedAt = payload.SavedAt;
            return payload.Games;
        }
        catch (Exception ex)
        {
            Logger.Warn("读取 Steam 缓存失败：" + ex.Message);
            return new List<SteamGame>();
        }
    }

    /// <summary>获取某游戏的各版本/套餐与价格（类似 Steam 客户端右侧列表）。</summary>
    public static async Task<List<SteamEdition>> GetEditionsAsync(long appId, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&cc=cn&l=schinese";
            using var doc = JsonDocument.Parse(await GetTextAsync(url, 15, ct));
            var root = doc.RootElement.GetProperty(appId.ToString());
            if (!root.TryGetProperty("success", out var ok) || !ok.GetBoolean()) return new List<SteamEdition>();
            var data = root.GetProperty("data");

            var list = new List<SteamEdition>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (data.TryGetProperty("price_overview", out var po))
            {
                var final = po.GetProperty("final").GetInt64();
                var currency = po.TryGetProperty("currency", out var c) ? c.GetString() : "CNY";
                var discount = po.TryGetProperty("discount_percent", out var d) ? d.GetInt32() : 0;
                var text = FormatPrice(final, currency);
                if (discount > 0) text += $"  -{discount}%";
                list.Add(new SteamEdition("标准版", text));
                seen.Add("标准版");
            }

            if (data.TryGetProperty("package_groups", out var groups))
            {
                var gameName = CleanText(data.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "");
                foreach (var group in groups.EnumerateArray())
                {
                    if (!group.TryGetProperty("subs", out var subs)) continue;
                    foreach (var sub in subs.EnumerateArray())
                    {
                        var option = sub.TryGetProperty("option_text", out var ot) ? ot.GetString() ?? "" : "";
                        var price = sub.TryGetProperty("price_in_cents_with_discount", out var p) ? p.GetInt64() : 0;
                        var discount = sub.TryGetProperty("discount_pct", out var dp) ? dp.GetInt32() : 0;
                        if (string.IsNullOrWhiteSpace(option)) continue;

                        // 去掉 HTML 标签、“购买”前缀与价格尾部，仅保留版本名
                        var title = CleanText(option);
                        foreach (var prefix in new[] { "购买 ", "购买", "Buy ", "Purchase " })
                            if (title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                title = title[prefix.Length..].Trim();
                        var dash = title.LastIndexOf(" - ¥", StringComparison.Ordinal);
                        if (dash > 0) title = title[..dash].Trim();
                        var yen = title.LastIndexOf("¥", StringComparison.Ordinal);
                        if (yen > 0) title = title[..yen].Trim();
                        title = title.Trim().TrimEnd('-', '—', '·', ' ').Trim();
                        if (string.IsNullOrWhiteSpace(title)) title = "版本";
                        if (gameName.Length > 0 && title.Equals(gameName, StringComparison.OrdinalIgnoreCase))
                            title = "标准版";
                        if (!seen.Add(title)) continue;

                        var priceText = price > 0 ? $"¥{price / 100.0:0.00}" : "免费";
                        if (discount > 0) priceText += $"  -{discount}%";
                        list.Add(new SteamEdition(title, priceText));
                    }
                }
            }
            return list.Take(8).ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warn($"获取游戏版本列表失败（{appId}）：{ex.Message}");
            return new List<SteamEdition>();
        }
    }

    private static async Task<string> TranslateToEnglishAsync(string text, CancellationToken ct)
    {
        try
        {
            var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair=zh-CN|en";
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(12));
            using var doc = JsonDocument.Parse(await Client.GetStringAsync(url, cts.Token));
            var translated = doc.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString();
            return translated?.Trim();
        }
        catch (Exception ex)
        {
            Logger.Warn("中文名翻译失败：" + ex.Message);
            return null;
        }
    }

    private static async Task<List<SteamGame>> TrySearchAsync(string term, CancellationToken ct)
    {
        foreach (var (cc, lang) in new[] { ("cn", "schinese"), ("us", "english") })
        {
            if (cc == "cn" && _cnSearchBlocked) continue;
            try
            {
                var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(term)}&l={lang}&cc={cc}";
                using var doc = JsonDocument.Parse(await GetTextAsync(url, 12, ct));
                if (!doc.RootElement.TryGetProperty("items", out var items)) continue;

                var list = new List<SteamGame>();
                foreach (var item in items.EnumerateArray())
                {
                    var id = item.GetProperty("id").GetInt64();
                    var name = CleanText(item.GetProperty("name").GetString() ?? "");
                    var image = item.TryGetProperty("tiny_image", out var img) ? img.GetString() : "";
                    var priceText = "价格未知";
                    if (item.TryGetProperty("price", out var price) && price.TryGetProperty("final", out var final))
                    {
                        var currency = price.TryGetProperty("currency", out var c) ? c.GetString() : "CNY";
                        priceText = FormatPrice(final.GetInt64(), currency);
                    }
                    list.Add(new SteamGame(id, name, image ?? "", priceText,
                        $"https://store.steampowered.com/app/{id}/"));
                }
                if (list.Count > 0) return list;
            }
            catch (Exception ex)
            {
                if (cc == "cn") _cnSearchBlocked = true;
                Logger.Warn($"Steam 搜索失败（cc={cc}）：{ex.Message}");
            }
        }
        return new List<SteamGame>();
    }

    /// <summary>国区当前价格（含折扣）。</summary>
    public static async Task<string> GetPriceAsync(long appId, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&cc=cn&l=schinese";
            using var doc = JsonDocument.Parse(await GetTextAsync(url, 15, ct));
            var root = doc.RootElement.GetProperty(appId.ToString());
            if (!root.TryGetProperty("success", out var ok) || !ok.GetBoolean()) return "价格未知";
            var data = root.GetProperty("data");
            if (!data.TryGetProperty("price_overview", out var po)) return "免费 / 无价格";
            var final = po.GetProperty("final").GetInt64();
            var initial = po.GetProperty("initial").GetInt64();
            var discount = po.TryGetProperty("discount_percent", out var d) ? d.GetInt32() : 0;
            var currency = po.TryGetProperty("currency", out var c) ? c.GetString() : "CNY";
            // 记录本次观察到的国区价格，作为史低接口不可用时的兜底
            if (string.Equals(currency, "CNY", StringComparison.OrdinalIgnoreCase))
                SteamPriceHistory.Record(appId, final / 100.0m, discount);
            var text = FormatPrice(final, currency);
            if (discount > 0) text += $"  -{discount}%  （原价 {FormatPrice(initial, currency)}）";
            return text;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warn($"获取价格失败（{appId}）：{ex.Message}");
            return "价格获取失败";
        }
    }

    /// <summary>史低：通过 Steam AppID 映射 CheapShark 条目，取全站历史最低价（USD）。</summary>
    public static async Task<HistoricalLow> GetHistoricalLowAsync(long appId, CancellationToken ct = default)
    {
        try
        {
            // 第一步：Steam AppID -> CheapShark 内部 gameID
            var lookupUrl = $"https://www.cheapshark.com/api/1.0/games?steamAppID={appId}";
            using var lookup = JsonDocument.Parse(await Client.GetStringAsync(lookupUrl, ct));
            if (lookup.RootElement.ValueKind != JsonValueKind.Array || lookup.RootElement.GetArrayLength() == 0)
                return new HistoricalLow(null, null, "");

            var gameId = lookup.RootElement[0].TryGetProperty("gameID", out var gid) ? gid.GetString() : null;
            if (string.IsNullOrEmpty(gameId)) return new HistoricalLow(null, null, "");

            // 第二步：读取该条目的 cheapestPriceEver
            var url = $"https://www.cheapshark.com/api/1.0/games?id={gameId}";
            using var doc = JsonDocument.Parse(await Client.GetStringAsync(url, ct));
            var root = doc.RootElement;
            var title = root.TryGetProperty("info", out var info) && info.TryGetProperty("title", out var t)
                ? t.GetString() ?? ""
                : "";

            if (!root.TryGetProperty("cheapestPriceEver", out var cheap) ||
                !cheap.TryGetProperty("price", out var p) ||
                !decimal.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var usd))
                return new HistoricalLow(null, null, title);

            DateTime? date = null;
            if (cheap.TryGetProperty("date", out var d) && d.TryGetInt64(out var unix) && unix > 0)
                date = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;

            return new HistoricalLow(usd, date, title);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warn($"获取史低失败（{appId}）：{ex.Message}");
            return new HistoricalLow(null, null, "");
        }
    }

    /// <summary>国区史低：先尝试实时数据源（SteamDB），失败后按汇率折算 CheapShark 国际史低。</summary>
    public static async Task<PriceHistory> GetPriceHistoryAsync(long appId, CancellationToken ct = default)
    {
        // 1) SteamDB 实时价格历史（国区）
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://steamdb.info/api/GetPriceHistory/?appid={appId}&cc=cn");
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*");
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            request.Headers.Referrer = new Uri($"https://steamdb.info/app/{appId}/");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var response = await BrowserClient.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync(timeout.Token);
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("lowest", out var lowest) &&
                    lowest.TryGetProperty("price", out var price) &&
                    decimal.TryParse(price.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var cny))
                {
                    DateTime? date = null;
                    if (lowest.TryGetProperty("time", out var time) && time.TryGetInt64(out var unix) && unix > 0)
                        date = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
                    return new PriceHistory(cny, null, date, "SteamDB 实时");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 用户切换了别的游戏，属于正常取消，不写日志
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warn($"SteamDB 史低接口不可用：{ex.Message}");
        }

        // 2) CheapShark 国际史低 × 实时汇率
        var low = await GetHistoricalLowAsync(appId, ct);
        if (low.Usd is decimal usd)
        {
            var rate = await GetUsdCnyRateAsync(ct);
            return new PriceHistory(Math.Round(usd * rate, 2), usd, low.Date, "CheapShark 折算");
        }

        // 3) 在线史低接口都不可用时，用本地记录的最低价兜底（避免部分游戏完全不显示）
        var local = SteamPriceHistory.Get(appId);
        if (local != null && local.LowestCny > 0)
        {
            DateTime? date = DateTime.TryParse(local.Date, out var parsedDate) ? parsedDate : null;
            return new PriceHistory(local.LowestCny, null, date, "本地记录");
        }

        return new PriceHistory(null, null, null, "暂无");
    }

    private static string FormatPrice(long cents, string currency)
    {
        var symbol = currency switch
        {
            "CNY" => "¥",
            "USD" => "$",
            "EUR" => "€",
            _ => ""
        };
        return $"{symbol}{cents / 100.0:0.00}";
    }
}
