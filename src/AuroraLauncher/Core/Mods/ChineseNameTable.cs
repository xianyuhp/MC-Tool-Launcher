using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuroraLauncher.Core.Mods;

/// <summary>中文名映射表的一行。</summary>
public class NameEntry
{
    public string Platform { get; set; } = "";
    public string Id { get; set; } = "";
    public string Slug { get; set; } = "";
    public string English { get; set; } = "";
    public string Chinese { get; set; } = "";
    public string McmodId { get; set; } = "";
}

/// <summary>
/// 本地模组中文名映射表（config/modnames.json）。
/// 中文名只用于界面显示，绝不写入下载文件名。
/// </summary>
public static class ChineseNameTable
{
    private static readonly string FilePath = Path.Combine(Settings.ConfigDir, "modnames.json");
    private static readonly object Gate = new();
    private static List<NameEntry> _entries;
    private static readonly Dictionary<string, string> ById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> BySlug = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<string>> ByChinese = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> ByEnglish = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>远程映射表更新地址（预留入口，不自动高频请求）。</summary>
    public static string UpdateUrl { get; set; } =
        "https://raw.githubusercontent.com/AuroraLauncher/modnames/main/modnames.json";

    public static int Count
    {
        get { EnsureLoaded(); return _entries.Count; }
    }

    public static void Reload()
    {
        lock (Gate) _entries = null;
        EnsureLoaded();
    }

    private static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_entries != null) return;
            _entries = new List<NameEntry>();
            try
            {
                Directory.CreateDirectory(Settings.ConfigDir);
                if (!File.Exists(FilePath)) WriteSeed();
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var list = JsonSerializer.Deserialize<List<NameEntry>>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (list != null) _entries = list;
                }

                // 旧版本只带少量手写种子：直接换成内置的完整表
                if (_entries.Count < 500)
                {
                    WriteSeed();
                    var json = File.ReadAllText(FilePath);
                    var list = JsonSerializer.Deserialize<List<NameEntry>>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (list != null && list.Count > _entries.Count) _entries = list;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("读取模组中文名表失败：" + ex.Message);
            }
            BuildIndex();
        }
    }

    private static void BuildIndex()
    {
        ById.Clear();
        BySlug.Clear();
        ByChinese.Clear();
        ByEnglish.Clear();
        foreach (var entry in _entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Chinese)) continue;
            if (!string.IsNullOrWhiteSpace(entry.Id)) ById[Key(entry.Platform, entry.Id)] = entry.Chinese;
            if (!string.IsNullOrWhiteSpace(entry.Slug)) BySlug[Key(entry.Platform, entry.Slug)] = entry.Chinese;

            // 种子表只带 slug：用 slug 推导英文名，支持「中文 → 英文」搜索
            var english = string.IsNullOrWhiteSpace(entry.English)
                ? (entry.Slug ?? "").Replace('-', ' ')
                : entry.English;
            if (!string.IsNullOrWhiteSpace(english)) ByEnglish[english.Trim()] = entry.Chinese;

            if (!ByChinese.TryGetValue(entry.Chinese.Trim(), out var list))
                ByChinese[entry.Chinese.Trim()] = list = new List<string>();
            if (!string.IsNullOrWhiteSpace(english) &&
                !list.Contains(english, StringComparer.OrdinalIgnoreCase))
                list.Add(english);
        }
    }

    private static string Key(string platform, string value) => $"{platform?.ToLowerInvariant()}|{value}";

    /// <summary>查中文名：平台 + ID / slug，失败再退回英文名。</summary>
    public static string Lookup(ModSource source, string id, string slug, string english)
    {
        EnsureLoaded();
        var platform = source == ModSource.CurseForge ? "curseforge" : "modrinth";
        lock (Gate)
        {
            if (!string.IsNullOrWhiteSpace(id) && ById.TryGetValue(Key(platform, id), out var name)) return name;
            if (!string.IsNullOrWhiteSpace(slug) && BySlug.TryGetValue(Key(platform, slug), out name)) return name;
            if (!string.IsNullOrWhiteSpace(english) && ByEnglish.TryGetValue(english.Trim(), out name)) return name;
        }
        return "";
    }

    /// <summary>中文关键词 → 对应的英文名列表（用于中文搜索）。</summary>
    public static List<string> EnglishForChinese(string keyword)
    {
        EnsureLoaded();
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(keyword)) return result;
        var key = keyword.Trim();
        lock (Gate)
        {
            if (ByChinese.TryGetValue(key, out var exact))
                result.AddRange(exact.Where(x => !result.Contains(x, StringComparer.OrdinalIgnoreCase)));
            // 支持部分匹配，例如「机械动」→ 机械动力
            foreach (var pair in ByChinese)
            {
                if (pair.Key.Length < 2 || !pair.Key.Contains(key, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var english in pair.Value)
                    if (!result.Contains(english, StringComparer.OrdinalIgnoreCase)) result.Add(english);
            }
        }
        return result.Take(6).ToList();
    }

    /// <summary>同一模组在另一平台的关联条目（按相同 mcmodId 或中文名判断）。</summary>
    public static NameEntry FindRelation(ModSource source, string id, string slug, string english)
    {
        EnsureLoaded();
        var platform = source == ModSource.CurseForge ? "curseforge" : "modrinth";
        lock (Gate)
        {
            var self = _entries.FirstOrDefault(e =>
                e.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase) &&
                ((!string.IsNullOrEmpty(id) && e.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ||
                 (!string.IsNullOrEmpty(slug) && e.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase)) ||
                 (!string.IsNullOrEmpty(english) && e.English.Equals(english, StringComparison.OrdinalIgnoreCase))));
            if (self == null) return null;

            var chinese = self.Chinese?.Trim();
            var mcmod = self.McmodId?.Trim();
            return _entries.FirstOrDefault(e =>
                !e.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase) &&
                ((!string.IsNullOrEmpty(mcmod) && e.McmodId.Equals(mcmod, StringComparison.OrdinalIgnoreCase)) ||
                 (!string.IsNullOrEmpty(chinese) && e.Chinese.Trim().Equals(chinese, StringComparison.OrdinalIgnoreCase))));
        }
    }

    /// <summary>MCmod 搜索地址（供用户手动跳转补充）。</summary>
    public static string McmodSearchUrl(string name) =>
        "https://search.mcmod.cn/s?key=" + Uri.EscapeDataString(name ?? "");

    /// <summary>从远程覆盖本地映射表（预留入口）。</summary>
    public static async Task<bool> UpdateFromUrlAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            var json = await Mc.McApi.Client.GetStringAsync(url, ct);
            var list = JsonSerializer.Deserialize<List<NameEntry>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (list == null || list.Count == 0) return false;
            Directory.CreateDirectory(Settings.ConfigDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            }));
            Reload();
            Logger.Info($"模组中文名表已更新：{list.Count} 条");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn("更新模组中文名表失败：" + ex.Message);
            return false;
        }
    }

    private static void WriteSeed()
    {
        // 种子数据来自 PCL 公开的 MC 百科条目表（已转换为本项目的 JSON 结构）
        using var stream = typeof(ChineseNameTable).Assembly
            .GetManifestResourceStream("AuroraLauncher.Core.Mods.modnames.seed.json");
        if (stream == null)
        {
            Logger.Warn("未找到内置的模组中文名种子表");
            return;
        }
        using var reader = new StreamReader(stream);
        File.WriteAllText(FilePath, reader.ReadToEnd());
    }
}
