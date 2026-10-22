using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AuroraLauncher.Core.Auth;

/// <summary>
/// 正版 / 离线账号头像：会话服务器 → 皮肤纹理 → 裁剪头部（含帽子层）。
/// <para/>缓存位于 config/cache/avatars，按 UUID 命名，7 天过期。
/// <para/>对应 PCL 的「头像缓存 + 离线借皮」思路，但使用本项目自己的存储与实现。
/// </summary>
public static class AvatarService
{
    private const string SessionUrl = "https://sessionserver.mojang.com/session/minecraft/profile/";
    private const string NameToUuidUrl = "https://api.mojang.com/users/profiles/minecraft/";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);
    private static readonly HttpClient Client = CreateClient();
    private static readonly Dictionary<string, ImageSource> Memory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MCTL/1.0");
        return client;
    }

    private static string CacheDir => Path.Combine(Settings.ConfigDir, "cache", "avatars");

    /// <summary>默认头像（离线 / 请求失败时使用），不发起网络请求。</summary>
    public static ImageSource DefaultAvatar { get; } = BuildDefault();

    /// <summary>
    /// 获取头像。nameOrUuid 可以是玩家名或 32 位 UUID；返回 null 时调用方应保留默认头像。
    /// </summary>
    public static async Task<ImageSource> GetAvatarAsync(string nameOrUuid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nameOrUuid)) return null;
        try
        {
            var uuid = await ResolveUuidAsync(nameOrUuid, ct);
            if (string.IsNullOrWhiteSpace(uuid)) return null;

            // 内存缓存
            lock (Gate)
                if (Memory.TryGetValue(uuid, out var cached)) return cached;

            // 磁盘缓存（7 天内直接使用）
            Directory.CreateDirectory(CacheDir);
            var avatarPath = Path.Combine(CacheDir, uuid + ".png");
            var skinPath = Path.Combine(CacheDir, uuid + ".skin.png");
            if (File.Exists(avatarPath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(avatarPath) < CacheLifetime)
            {
                var image = LoadPng(avatarPath);
                if (image != null)
                {
                    lock (Gate) Memory[uuid] = image;
                    return image;
                }
            }

            var skinUrl = await GetSkinUrlAsync(uuid, ct);
            if (string.IsNullOrEmpty(skinUrl)) return ReadStale(avatarPath, uuid);

            var skinBytes = await Client.GetByteArrayAsync(skinUrl, ct);
            var skin = Decode(skinBytes);
            if (skin == null) return ReadStale(avatarPath, uuid);

            File.WriteAllBytes(skinPath, skinBytes);
            var avatar = CropHead(skin);
            if (avatar == null) return ReadStale(avatarPath, uuid);

            var encoded = Encode(avatar);
            if (encoded != null) File.WriteAllBytes(avatarPath, encoded);
            lock (Gate) Memory[uuid] = avatar;
            return avatar;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 会话服务器 / 纹理服务器在部分地区不可达：静默降级
            Logger.Warn("获取头像失败（将显示默认头像）：" + ex.Message);
            return null;
        }
    }

    /// <summary>解析玩家名 / UUID 为标准 32 位 UUID（无连字符）。</summary>
    public static async Task<string> ResolveUuidAsync(string nameOrUuid, CancellationToken ct = default)
    {
        var text = nameOrUuid.Trim().Replace("-", "");
        if (text.Length == 32 && text.All(Uri.IsHexDigit)) return text.ToLowerInvariant();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(8));
        var json = await Client.GetStringAsync(NameToUuidUrl + Uri.EscapeDataString(nameOrUuid.Trim()), cts.Token);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    private static async Task<string> GetSkinUrlAsync(string uuid, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var json = await Client.GetStringAsync(SessionUrl + uuid, cts.Token);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var property in properties.EnumerateArray())
        {
            if (!property.TryGetProperty("name", out var name) ||
                !string.Equals(name.GetString(), "textures", StringComparison.OrdinalIgnoreCase)) continue;
            if (!property.TryGetProperty("value", out var value)) continue;

            var decoded = System.Convert.FromBase64String(value.GetString() ?? "");
            using var textures = JsonDocument.Parse(decoded);
            if (textures.RootElement.TryGetProperty("textures", out var tex) &&
                tex.TryGetProperty("SKIN", out var skin) &&
                skin.TryGetProperty("url", out var url))
                return url.GetString();
        }
        return null;
    }

    /// <summary>裁剪 8×8 头部 + (40,8) 帽子层，放大到 128×128（最近邻，保持像素风）。</summary>
    private static BitmapSource CropHead(BitmapSource skin)
    {
        if (skin.PixelWidth < 16 || skin.PixelHeight < 16) return null;
        var head = new CroppedBitmap(skin, new Int32Rect(8, 8, 8, 8));

        CroppedBitmap hat = null;
        if (skin.PixelWidth >= 48 && skin.PixelHeight >= 16)
            hat = new CroppedBitmap(skin, new Int32Rect(40, 8, 8, 8));

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            var target = new Rect(0, 0, 128, 128);
            dc.DrawImage(head, target);
            if (hat != null) dc.DrawImage(hat, target);
        }

        var bitmap = new RenderTargetBitmap(128, 128, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static ImageSource ReadStale(string avatarPath, string uuid)
    {
        if (!File.Exists(avatarPath)) return null;
        var image = LoadPng(avatarPath);
        if (image != null) lock (Gate) Memory[uuid] = image;
        return image;
    }

    private static BitmapSource LoadPng(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Decode(ReadAll(stream));
        }
        catch
        {
            return null;
        }
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
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

    private static byte[] Encode(BitmapSource bitmap)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var memory = new MemoryStream();
            encoder.Save(memory);
            return memory.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>内置默认头像：8×8 像素风 Steve，放大 16 倍。</summary>
    private static ImageSource BuildDefault()
    {
        const int size = 8;
        var pixels = new byte[size * size * 4];
        var hair = new (byte, byte, byte)[] { (0x35, 0x2A, 0x1E), (0x3D, 0x30, 0x21), (0x2E, 0x24, 0x19) };
        var skin = (R: (byte)0xC6, G: (byte)0x9A, B: (byte)0x76);
        var eye = (R: (byte)0x3C, G: (byte)0x50, B: (byte)0x8A);
        var mouth = (R: (byte)0x8A, G: (byte)0x5C, B: (byte)0x45);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var index = (y * size + x) * 4;
                byte r = skin.R, g = skin.G, b = skin.B;
                if (y <= 1) (r, g, b) = hair[(x + y) % hair.Length];
                if (y == 3 && (x == 1 || x == 2 || x == 5 || x == 6)) (r, g, b) = eye;
                if (y == 5 && x >= 2 && x <= 5) (r, g, b) = mouth;
                if (y == 6 && (x == 0 || x == 7)) (r, g, b) = hair[0];
                pixels[index] = b;
                pixels[index + 1] = g;
                pixels[index + 2] = r;
                pixels[index + 3] = 255;
            }
        }

        var small = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
            dc.DrawImage(small, new Rect(0, 0, 128, 128));

        var bitmap = new RenderTargetBitmap(128, 128, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
