using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MCTL.Core;

/// <summary>物品 / 刷怪蛋图标：优先从本机客户端 jar 提取，其次在线 CDN，最后彩色占位图。</summary>
public static class GameIconService
{
    private static readonly Dictionary<string, BitmapSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ZipArchive> Jars = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();
    private static bool _jarScanned;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(4) };

    public static async Task<ImageSource> GetItemAsync(string itemId, bool allowNetwork = true) =>
        await GetAsync(itemId, false, allowNetwork);

    public static async Task<ImageSource> GetSpawnEggAsync(string mobId, bool allowNetwork = true) =>
        await GetAsync(mobId + "_spawn_egg", true, allowNetwork);

    private static async Task<ImageSource> GetAsync(string rawId, bool egg, bool allowNetwork)
    {
        var id = (rawId ?? "").Replace("minecraft:", "").Trim().ToLowerInvariant();
        if (id.Length == 0) return null;
        var cacheKey = id + (allowNetwork ? "" : "|local");

        lock (Gate)
            if (Cache.TryGetValue(cacheKey, out var cached)) return cached;

        // 1) 本机客户端 jar（离线可用，图标与本机版本一致）
        foreach (var entry in new[]
                 {
                     $"assets/minecraft/textures/item/{id}.png",
                     $"assets/minecraft/textures/block/{id}.png"
                 })
        {
            var bytes = ReadFromLocalJars(entry);
            if (bytes == null) continue;
            var bitmap = Decode(bytes);
            if (bitmap != null)
            {
                lock (Gate)
                {
                    Cache[id] = bitmap;
                    Cache[cacheKey] = bitmap;
                }
                return bitmap;
            }
        }

        // 列表批量加载时不访问网络，避免大量超时导致卡顿；选中项再单独联网取图标
        if (!allowNetwork)
        {
            var localFallback = Placeholder(id, egg);
            lock (Gate) Cache[cacheKey] = localFallback;
            return localFallback;
        }

        // 2) 在线 CDN（jsDelivr 镜像的 Minecraft 资源）
        foreach (var branch in new[] { "1.20.1", "1.21.1" })
        {
            foreach (var folder in new[] { "item", "block" })
            {
                var url = $"https://cdn.jsdelivr.net/gh/InventivetalentDev/minecraft-assets@{branch}" +
                          $"/assets/minecraft/textures/{folder}/{id}.png";
                try
                {
                    var bytes = await Client.GetByteArrayAsync(url);
                    var bitmap = Decode(bytes);
                    if (bitmap == null) continue;
                    lock (Gate) Cache[id] = bitmap;
                    return bitmap;
                }
                catch { }
            }
        }

        var placeholder = Placeholder(id, egg);
        lock (Gate) Cache[id] = placeholder;
        return placeholder;
    }

    private static IEnumerable<string> CandidateJars()
    {
        if (!_jarScanned)
        {
            _jarScanned = true;
            try
            {
                foreach (var root in Settings.Current.GameDirs)
                {
                    var versions = Path.Combine(root, "versions");
                    if (!Directory.Exists(versions)) continue;
                    foreach (var dir in Directory.GetDirectories(versions))
                    {
                        foreach (var jar in Directory.GetFiles(dir, "*.jar", SearchOption.TopDirectoryOnly))
                        {
                            try
                            {
                                if (new FileInfo(jar).Length < 4L * 1024 * 1024) continue;
                                var name = Path.GetFileName(jar);
                                if (name.Contains("installer", StringComparison.OrdinalIgnoreCase)) continue;
                                lock (Gate) Jars[jar] = null;
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("扫描客户端 jar 失败：" + ex.Message);
            }
        }

        lock (Gate) return Jars.Keys.ToList();
    }

    private static byte[] ReadFromLocalJars(string entryPath)
    {
        foreach (var jar in CandidateJars())
        {
            try
            {
                ZipArchive archive;
                lock (Gate)
                {
                    if (!Jars.TryGetValue(jar, out archive) || archive == null)
                    {
                        archive = ZipFile.OpenRead(jar);
                        Jars[jar] = archive;
                    }
                }
                var entry = archive.GetEntry(entryPath);
                if (entry == null) continue;
                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }
            catch { }
        }
        return null;
    }

    private static BitmapSource Decode(byte[] bytes)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource Placeholder(string id, bool egg)
    {
        const int size = 64;
        var colors = new[]
        {
            (Color.FromRgb(0x5B, 0x8C, 0xFF), Color.FromRgb(0x2F, 0x5B, 0xD0)),
            (Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0x5B, 0x34, 0xC0)),
            (Color.FromRgb(0x22, 0xC7, 0xA9), Color.FromRgb(0x10, 0x7A, 0x66)),
            (Color.FromRgb(0xFF, 0x7A, 0x59), Color.FromRgb(0xC0, 0x45, 0x2B)),
            (Color.FromRgb(0xF2, 0xC1, 0x4E), Color.FromRgb(0xB8, 0x8A, 0x1E))
        };
        var hash = 0;
        foreach (var c in id) hash = (hash * 31 + c) & 0x7fffffff;
        var (c1, c2) = colors[hash % colors.Length];
        var letter = egg ? "蛋" : (id.Length > 0 ? id[..1].ToUpperInvariant() : "?");

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new LinearGradientBrush(c1, c2, new Point(0, 0), new Point(1, 1));
            dc.DrawRoundedRectangle(brush, null, new Rect(1, 1, size - 2, size - 2), 14, 14);
            var typeface = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal,
                FontWeights.Bold, FontStretches.Normal);
            var text = new FormattedText(letter, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, egg ? 26 : 30, Brushes.White, 1.0);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
