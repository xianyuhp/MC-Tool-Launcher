using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MCTL.Core.Mc;

public class VersionEntry
{
    public string Dir { get; init; } = "";
    public string Id { get; init; } = "";
    public string FolderLabel { get; init; } = "";
    /// <summary>副标题：基础版本 + 加载器，例如「1.21.1 · NeoForge 21.1.80」。</summary>
    public string Subtitle { get; set; } = "";
    public ImageSource Icon { get; set; }
    public bool CustomIcon { get; set; }

    public override string ToString() => Id;
}

/// <summary>已安装版本的扫描、图标、重命名与删除。</summary>
public static class McVersions
{
    private static readonly Dictionary<string, ImageSource> BadgeCache = new();

    public static List<VersionEntry> ScanAll()
    {
        var result = new List<VersionEntry>();
        var dirs = Settings.Current.GameDirs;
        foreach (var dir in dirs)
        {
            var versionsDir = Path.Combine(dir, "versions");
            if (!Directory.Exists(versionsDir)) continue;
            foreach (var folder in Directory.EnumerateDirectories(versionsDir))
            {
                var id = Path.GetFileName(folder);
                var jsonPath = Path.Combine(folder, id + ".json");
                if (!File.Exists(jsonPath))
                {
                    // 兼容文件夹名与 json 名不一致的情况
                    var candidate = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
                        .FirstOrDefault(f => !Path.GetFileName(f).StartsWith("manifest", StringComparison.OrdinalIgnoreCase));
                    if (candidate == null) continue;
                    jsonPath = candidate;
                }
                var entry = new VersionEntry
                {
                    Dir = dir,
                    Id = id,
                    FolderLabel = Path.GetFileName(dir.TrimEnd('\\'))
                };
                entry.Icon = LoadIcon(dir, id, out var custom);
                entry.CustomIcon = custom;
                entry.Subtitle = GetSubtitle(dir, id, jsonPath);
                if (dirs.Count > 1) entry.Subtitle += " · " + entry.FolderLabel;
                result.Add(entry);
            }
        }
        return result
            .OrderBy(e => e.FolderLabel, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string FindIconFile(string gameDir, string id)
    {
        var folder = Path.Combine(gameDir, "versions", id);
        if (!Directory.Exists(folder)) return null;
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp" })
        {
            var file = Path.Combine(folder, "icon" + ext);
            if (File.Exists(file)) return file;
        }
        return null;
    }

    public static ImageSource LoadIcon(string gameDir, string id, out bool custom)
    {
        custom = false;
        var file = FindIconFile(gameDir, id);
        if (file != null)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(file);
                bitmap.EndInit();
                bitmap.Freeze();
                custom = true;
                return bitmap;
            }
            catch { }
        }
        return Badge(id);
    }

    public static void SetCustomIcon(string gameDir, string id, string imagePath)
    {
        var folder = Path.Combine(gameDir, "versions", id);
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.GetFiles(folder, "icon.*")) File.Delete(old);
        File.Copy(imagePath, Path.Combine(folder, "icon" + Path.GetExtension(imagePath)), true);
    }

    public static void ClearCustomIcon(string gameDir, string id)
    {
        var folder = Path.Combine(gameDir, "versions", id);
        if (!Directory.Exists(folder)) return;
        foreach (var old in Directory.GetFiles(folder, "icon.*")) File.Delete(old);
    }

    /// <summary>按加载器类型生成与 PCL 类似的版本图标（Forge 铁砧 / NeoForge 狐狸 / Fabric 织布 / 原版镐）。</summary>
    public static ImageSource Badge(string id)
    {
        var lower = id.ToLowerInvariant();
        string kind;
        Color c1, c2;
        if (lower.Contains("neoforge")) { kind = "neoforge"; c1 = Color.FromRgb(0xF2, 0x8B, 0x4B); c2 = Color.FromRgb(0xB8, 0x4A, 0x2A); }
        else if (lower.Contains("forge")) { kind = "forge"; c1 = Color.FromRgb(0xF2, 0xB3, 0x3D); c2 = Color.FromRgb(0xC7, 0x78, 0x2C); }
        else if (lower.Contains("fabric")) { kind = "fabric"; c1 = Color.FromRgb(0xD8, 0xBD, 0x92); c2 = Color.FromRgb(0x8D, 0x6E, 0x4F); }
        else if (lower.Contains("quilt")) { kind = "quilt"; c1 = Color.FromRgb(0xC0, 0x84, 0xFC); c2 = Color.FromRgb(0x7C, 0x3A, 0xED); }
        else if (lower.Contains("optifine")) { kind = "pickaxe"; c1 = Color.FromRgb(0x7E, 0xD9, 0x57); c2 = Color.FromRgb(0x3F, 0x8F, 0x2F); }
        else { kind = "pickaxe"; c1 = Color.FromRgb(0x66, 0xC0, 0x6A); c2 = Color.FromRgb(0x2E, 0x7D, 0x32); }

        var key = kind + c1 + c2;
        if (BadgeCache.TryGetValue(key, out var cached)) return cached;

        const int size = 64;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var background = new LinearGradientBrush(c1, c2, new Point(0, 0), new Point(1, 1));
            dc.DrawRoundedRectangle(background, null, new Rect(1, 1, size - 2, size - 2), 16, 16);

            var white = new SolidColorBrush(Color.FromArgb(0xF2, 255, 255, 255));
            dc.PushTransform(new ScaleTransform(size / 64.0, size / 64.0));
            DrawBadgeIcon(dc, kind, white, background);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        BadgeCache[key] = bitmap;
        return bitmap;
    }

    /// <summary>用矢量图形绘制加载器标志，避免 emoji 字体缺字。</summary>
    private static void DrawBadgeIcon(DrawingContext dc, string kind, Brush white, Brush background)
    {
        switch (kind)
        {
            case "forge":
                // 铁砧
                dc.DrawGeometry(white, null, Geometry.Parse(
                    "M11,18 L53,18 L47,27 L39,27 L37,36 L45,41 L45,47 L19,47 L19,41 L27,36 L25,27 L17,27 Z"));
                break;
            case "neoforge":
            {
                // 狐狸头
                dc.DrawGeometry(white, null, Geometry.Parse(
                    "M12,12 L27,21 L37,21 L52,12 L49,33 L32,52 L15,33 Z"));
                dc.DrawEllipse(background, null, new Point(25.5, 29), 2.6, 2.6);
                dc.DrawEllipse(background, null, new Point(38.5, 29), 2.6, 2.6);
                dc.DrawGeometry(background, null, Geometry.Parse("M32,33 L36,38 L28,38 Z"));
                break;
            }
            case "fabric":
                // 织布线轴
                dc.DrawRoundedRectangle(white, null, new Rect(14, 13, 36, 8), 4, 4);
                dc.DrawRoundedRectangle(white, null, new Rect(14, 43, 36, 8), 4, 4);
                dc.DrawRectangle(white, null, new Rect(22, 21, 5, 22));
                dc.DrawRectangle(white, null, new Rect(37, 21, 5, 22));
                break;
            case "quilt":
                // 缝线方块
                dc.DrawGeometry(white, null, Geometry.Parse(
                    "M16,16 L48,16 L48,26 L34,26 L34,48 L16,48 Z"));
                dc.DrawRectangle(background, null, new Rect(22, 30, 5, 4));
                dc.DrawRectangle(background, null, new Rect(22, 38, 5, 4));
                break;
            default:
                // 原版镐
                dc.DrawGeometry(white, null, Geometry.Parse(
                    "M12,25 Q32,9 52,25 L45,31 Q32,19 19,31 Z"));
                dc.DrawRectangle(white, null, new Rect(28, 27, 7, 24));
                break;
        }
    }

    /// <summary>解析基础版本与加载器信息（用于版本列表副标题）。</summary>
    public static string GetSubtitle(string gameDir, string id) =>
        GetSubtitle(gameDir, id, Path.Combine(gameDir, "versions", id, id + ".json"));

    /// <summary>解析客户端版本信息：原版版本号 + 加载器（例如 26.3 + fabric）。</summary>
    public static (string McVersion, string Loader) GetInstanceTag(string gameDir, string id)
    {
        var subtitle = GetSubtitle(gameDir, id) ?? "";
        var parts = subtitle.Split('·', 2);
        var mc = parts.Length > 0 ? parts[0].Trim() : "";
        var loader = "";
        if (parts.Length > 1)
        {
            var rest = parts[1].Trim();
            if (rest.StartsWith("NeoForge", StringComparison.OrdinalIgnoreCase)) loader = "neoforge";
            else if (rest.StartsWith("Forge", StringComparison.OrdinalIgnoreCase)) loader = "forge";
            else if (rest.StartsWith("Fabric", StringComparison.OrdinalIgnoreCase)) loader = "fabric";
            else if (rest.StartsWith("Quilt", StringComparison.OrdinalIgnoreCase)) loader = "quilt";
        }
        if (mc.Length == 0) mc = DetectMcVersion(id) ?? "";
        return (mc, loader);
    }

    public static string GetSubtitle(string gameDir, string id, string jsonPath)
    {
        string baseVersion = null;
        string jsonText = null;
        var current = id;
        var currentJson = jsonPath;

        // 顺着 inheritsFrom 找到真正的基础版本
        for (var depth = 0; depth < 6; depth++)
        {
            if (!File.Exists(currentJson)) break;
            try { jsonText = File.ReadAllText(currentJson); }
            catch { break; }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
                var parent = VersionJson.GetString(doc.RootElement, "inheritsFrom");
                if (string.IsNullOrEmpty(parent))
                {
                    var clientVersion = VersionJson.GetString(doc.RootElement, "clientVersion");
                    if (!string.IsNullOrEmpty(clientVersion)) baseVersion = clientVersion;
                    else if (string.IsNullOrEmpty(baseVersion)) baseVersion = current;
                    break;
                }
                current = parent;
                baseVersion = parent;
                currentJson = Path.Combine(gameDir, "versions", current, current + ".json");
            }
            catch { break; }
        }

        baseVersion = DetectMcVersion(baseVersion) ?? DetectMcVersion(id) ?? baseVersion ?? id;

        var lower = id.ToLowerInvariant();
        string loader = null;
        string loaderVersion = null;

        if (lower.Contains("neoforge"))
        {
            loader = "NeoForge";
            loaderVersion = ExtractAfter(id, "neoforge-");
        }
        else if (lower.Contains("forge"))
        {
            loader = "Forge";
            loaderVersion = ExtractAfter(id, "forge-");
        }
        else if (lower.Contains("fabric"))
        {
            loader = "Fabric";
            loaderVersion = ExtractAfter(id, "fabric-loader-") ?? ExtractAfter(id, "fabric-");
        }
        else if (lower.Contains("quilt"))
        {
            loader = "Quilt";
            loaderVersion = ExtractAfter(id, "quilt-");
        }

        // id 里没有加载器信息时，按 libraries 判断（兼容自定义命名的版本）
        if (loader == null && !string.IsNullOrEmpty(jsonText))
        {
            if (jsonText.Contains("net.neoforged", StringComparison.OrdinalIgnoreCase)) loader = "NeoForge";
            else if (jsonText.Contains("net.minecraftforge", StringComparison.OrdinalIgnoreCase)) loader = "Forge";
            else if (jsonText.Contains("net.fabricmc", StringComparison.OrdinalIgnoreCase)) loader = "Fabric";
            else if (jsonText.Contains("org.quiltmc", StringComparison.OrdinalIgnoreCase)) loader = "Quilt";
        }

        loader ??= "原版";
        if (loaderVersion != null && loaderVersion.Equals(baseVersion, StringComparison.OrdinalIgnoreCase))
            loaderVersion = null;

        return $"{baseVersion} · {loader}" + (string.IsNullOrEmpty(loaderVersion) ? "" : " " + loaderVersion);
    }

    /// <summary>从字符串中提取形如 1.21.1 的 Minecraft 版本号。</summary>
    private static string DetectMcVersion(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\b([12]\d?\.[0-9]{1,2}(?:\.[0-9]{1,2})?)\b");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string ExtractAfter(string text, string marker)
    {
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        var rest = text[(index + marker.Length)..];
        var end = rest.IndexOfAny(new[] { '-', '+' });
        var value = (end > 0 ? rest[..end] : rest).Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>重命名版本（文件夹、json 文件名、id 字段，以及其它版本里的 inheritsFrom）。</summary>
    public static void Rename(string gameDir, string oldId, string newId)
    {
        newId = (newId ?? "").Trim();
        if (string.IsNullOrEmpty(newId)) throw new Exception("名称不能为空。");
        if (oldId.Equals(newId, StringComparison.Ordinal)) return;
        if (newId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new Exception("名称包含非法字符。");

        var root = Path.Combine(gameDir, "versions");
        var oldDir = Path.Combine(root, oldId);
        var newDir = Path.Combine(root, newId);
        if (!Directory.Exists(oldDir)) throw new Exception("版本目录不存在。");
        if (Directory.Exists(newDir)) throw new Exception("已存在同名版本。");

        Directory.Move(oldDir, newDir);

        var oldJson = Path.Combine(newDir, oldId + ".json");
        var newJson = Path.Combine(newDir, newId + ".json");
        if (File.Exists(oldJson))
        {
            var node = JsonNode.Parse(File.ReadAllText(oldJson));
            node["id"] = newId;
            File.WriteAllText(newJson, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            File.Delete(oldJson);
        }

        foreach (var json in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(json));
                if (node?["inheritsFrom"]?.GetValue<string>() == oldId)
                {
                    node["inheritsFrom"] = newId;
                    File.WriteAllText(json, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch { }
        }
        Logger.Info($"版本已重命名：{oldId} -> {newId}");
    }

    public static void Delete(string gameDir, string id)
    {
        var folder = Path.Combine(gameDir, "versions", id);
        if (!Directory.Exists(folder)) return;
        Directory.Delete(folder, true);
        Logger.Info("已删除版本：" + id);
    }
}
