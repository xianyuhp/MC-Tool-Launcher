using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AuroraLauncher.Core;

/// <summary>正版账号皮肤：拉取官方皮肤文件并渲染为高清头像 / 全身预览（参考 PCL 的显示方式）。</summary>
public static class SkinService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>皮肤渲染结果：Head 为方块头像，Body 为全身正面预览。</summary>
    public record SkinImages(ImageSource Head, ImageSource Body);

    /// <summary>原始皮肤纹理 + 是否为纤细（Slim / Alex）模型。</summary>
    public record SkinData(BitmapSource Texture, bool Slim);

    // 默认渲染皮肤：玩家标识经过异或混淆，运行时还原，避免明文出现在代码里
    private const string DefaultSkinIdMasked = "aW9ja2g7PGhqbWppbj9qPmJpPGhjaDlsaW5ja2s8bT4=";
    private const byte DefaultSkinMask = 0x5A;
    private static SkinData _defaultSkin;

    private static string DefaultSkinId => string.Concat(
        Convert.FromBase64String(DefaultSkinIdMasked).Select(b => (char)(b ^ DefaultSkinMask)));

    /// <summary>编译进程序的默认皮肤，首次启动（无缓存、无网络）也能立刻用正确贴图建模。</summary>
    public static SkinData BundledDefault
    {
        get
        {
            if (_bundled != null) return _bundled;
            try
            {
                using var stream = typeof(SkinService).Assembly
                    .GetManifestResourceStream("AuroraLauncher.Assets.default-skin.png");
                if (stream == null) return null;

                var frame = BitmapDecoder.Create(stream,
                    BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                frame.Freeze();
                _bundled = new SkinData(frame, true);
            }
            catch
            {
            }
            return _bundled;
        }
    }

    private static SkinData _bundled;

    /// <summary>未登录时使用的默认皮肤（带内存缓存），失败返回 null 由调用方回退到内置配色。</summary>
    public static async Task<SkinData> LoadDefaultSkinAsync(CancellationToken ct = default)
    {
        if (_defaultSkin != null) return _defaultSkin;

        // 先读磁盘缓存，保证启动首帧就是正确皮肤
        _defaultSkin = LoadCached(DefaultCacheKey);
        if (_defaultSkin != null) return _defaultSkin;

        SkinData data = null;
        try
        {
            data = await LoadTextureAsync(DefaultSkinId, null, ct);
        }
        catch (Exception)
        {
            // 网络不可用时继续走内置皮肤
        }

        // 网络失败时使用编译进程序的那份贴图
        data ??= BundledDefault;
        if (data != null)
        {
            _defaultSkin = data;
            SaveCache(DefaultCacheKey, data);
        }
        return data;
    }

    private const string DefaultCacheKey = "default";

    private static string SkinCacheDir => Path.Combine(Settings.ConfigDir, "cache", "skins");

    /// <summary>同步读取本地皮肤缓存，用于启动时立刻建模（避免先显示默认皮肤再替换）。</summary>
    public static SkinData LoadCached(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        try
        {
            var path = Path.Combine(SkinCacheDir, key + ".png");
            if (!File.Exists(path)) return null;

            var bytes = File.ReadAllBytes(path);
            var frame = BitmapDecoder.Create(new MemoryStream(bytes),
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            frame.Freeze();
            var slim = File.Exists(Path.Combine(SkinCacheDir, key + ".slim"));
            return new SkinData(frame, slim);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveCache(string key, SkinData data)
    {
        if (string.IsNullOrWhiteSpace(key) || data?.Texture == null) return;
        try
        {
            Directory.CreateDirectory(SkinCacheDir);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(data.Texture));
            using (var stream = File.Create(Path.Combine(SkinCacheDir, key + ".png")))
                encoder.Save(stream);

            var slimMarker = Path.Combine(SkinCacheDir, key + ".slim");
            if (data.Slim) File.WriteAllText(slimMarker, "1");
            else if (File.Exists(slimMarker)) File.Delete(slimMarker);
        }
        catch
        {
        }
    }

    /// <summary>按账号拉取皮肤并写入磁盘缓存；key 用于下次启动秒开。</summary>
    public static async Task<SkinData> LoadTextureForAccountAsync(string key, string uuid, string name,
        CancellationToken ct = default)
    {
        var data = await LoadTextureAsync(uuid, name, ct);
        if (data != null) SaveCache(key, data);
        return data;
    }

    /// <summary>获取皮肤纹理与模型形态，供 3D 控件贴图使用。</summary>
    public static async Task<SkinData> LoadTextureAsync(string uuid, string name, CancellationToken ct = default)
    {
        try
        {
            var id = NormalizeUuid(uuid);
            if (id == null && !string.IsNullOrWhiteSpace(name)) id = await ResolveUuidAsync(name, ct);
            if (id == null) return null;

            var (skinUrl, slim) = await GetSkinInfoAsync(id, ct);
            if (string.IsNullOrEmpty(skinUrl)) return null;

            var bytes = await Client.GetByteArrayAsync(skinUrl, ct);
            var frame = BitmapDecoder.Create(new MemoryStream(bytes),
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            if (frame.PixelWidth < 64 || frame.PixelHeight < 32) return null;
            frame.Freeze();
            return new SkinData(frame, slim);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>返回渲染好的皮肤图片；失败返回 null（调用方可回退到第三方头像接口）。</summary>
    public static async Task<SkinImages> LoadAsync(string uuid, string name, CancellationToken ct = default)
    {
        var frame = await LoadSkinAsync(uuid, name, ct);
        if (frame == null) return null;
        try
        {
            return new SkinImages(RenderHead(frame), RenderBody(frame));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>获取原始皮肤纹理（64×64 或 64×32），用于 3D 模型贴图。</summary>
    public static async Task<BitmapSource> LoadSkinAsync(string uuid, string name, CancellationToken ct = default)
    {
        try
        {
            var id = NormalizeUuid(uuid);
            if (id == null && !string.IsNullOrWhiteSpace(name)) id = await ResolveUuidAsync(name, ct);
            if (id == null) return null;

            var skinUrl = await GetSkinUrlAsync(id, ct);
            if (string.IsNullOrEmpty(skinUrl)) return null;

            var bytes = await Client.GetByteArrayAsync(skinUrl, ct);
            var frame = BitmapDecoder.Create(new MemoryStream(bytes),
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            return frame.PixelWidth >= 64 && frame.PixelHeight >= 32 ? frame : null;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeUuid(string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid)) return null;
        var clean = uuid.Replace("-", "").Trim();
        return clean.Length == 32 ? clean : null;
    }

    /// <summary>只有名字时通过 Mojang 接口换 UUID。</summary>
    private static async Task<string> ResolveUuidAsync(string name, CancellationToken ct)
    {
        var json = await Client.GetStringAsync(
            $"https://api.mojang.com/users/profiles/minecraft/{Uri.EscapeDataString(name)}", ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    /// <summary>从会话服务读取皮肤纹理地址与模型形态。</summary>
    private static async Task<(string Url, bool Slim)> GetSkinInfoAsync(string uuid, CancellationToken ct)
    {
        var json = await Client.GetStringAsync(
            $"https://sessionserver.mojang.com/session/minecraft/profile/{uuid}", ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("properties", out var props)) return (null, false);

        foreach (var property in props.EnumerateArray())
        {
            var value = property.TryGetProperty("value", out var v) ? v.GetString() : null;
            if (string.IsNullOrEmpty(value)) continue;
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                using var texture = JsonDocument.Parse(decoded);
                if (!texture.RootElement.TryGetProperty("textures", out var textures) ||
                    !textures.TryGetProperty("SKIN", out var skin) ||
                    !skin.TryGetProperty("url", out var url))
                    continue;

                var slim = skin.TryGetProperty("metadata", out var metadata) &&
                           metadata.TryGetProperty("model", out var model) &&
                           model.GetString() == "slim";
                return (url.GetString(), slim);
            }
            catch { }
        }
        return (null, false);
    }

    /// <summary>从会话服务读取皮肤纹理地址。</summary>
    private static async Task<string> GetSkinUrlAsync(string uuid, CancellationToken ct) =>
        (await GetSkinInfoAsync(uuid, ct)).Url;

    /// <summary>把皮肤 PNG 的头部（8,8）与帽子层（40,8）合成高清头像。</summary>
    private static ImageSource RenderHead(BitmapSource frame)
    {
        const int scale = 16;   // 8 像素 -> 128 像素，足够清晰
        const int size = 8 * scale;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(Crop(frame, 8, 8, 8, 8), new Rect(0, 0, size, size));
            dc.DrawImage(Crop(frame, 40, 8, 8, 8), new Rect(0, 0, size, size));
        }
        return Rasterize(visual, size, size);
    }

    /// <summary>按 16×32 的正面布局合成全身皮肤（支持 64×64 与 64×32 旧版皮肤）。</summary>
    private static ImageSource RenderBody(BitmapSource frame)
    {
        const int scale = 16;               // 每个皮肤像素放大到 16 像素
        const int unit = scale;             // 1 单位 = 1 像素
        const int width = 16 * unit;
        const int height = 32 * unit;
        var modern = frame.PixelHeight >= 64;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            // 腿（先画后面的层）
            var rightLeg = Crop(frame, 4, 20, 4, 12);
            var leftLeg = modern ? Crop(frame, 20, 52, 4, 12) : rightLeg;
            dc.DrawImage(rightLeg, new Rect(4 * unit, 20 * unit, 4 * unit, 12 * unit));
            dc.DrawImage(leftLeg, new Rect(8 * unit, 20 * unit, 4 * unit, 12 * unit));
            if (modern)
            {
                dc.DrawImage(Crop(frame, 4, 36, 4, 12), new Rect(4 * unit, 20 * unit, 4 * unit, 12 * unit));
                dc.DrawImage(Crop(frame, 4, 52, 4, 12), new Rect(8 * unit, 20 * unit, 4 * unit, 12 * unit));
            }

            // 身体
            dc.DrawImage(Crop(frame, 20, 20, 8, 12), new Rect(4 * unit, 8 * unit, 8 * unit, 12 * unit));
            if (modern)
                dc.DrawImage(Crop(frame, 20, 36, 8, 12), new Rect(4 * unit, 8 * unit, 8 * unit, 12 * unit));

            // 手臂
            var rightArm = Crop(frame, 44, 20, 4, 12);
            var leftArm = modern ? Crop(frame, 36, 52, 4, 12) : rightArm;
            dc.DrawImage(rightArm, new Rect(0, 8 * unit, 4 * unit, 12 * unit));
            dc.DrawImage(leftArm, new Rect(12 * unit, 8 * unit, 4 * unit, 12 * unit));
            if (modern)
            {
                dc.DrawImage(Crop(frame, 44, 36, 4, 12), new Rect(0, 8 * unit, 4 * unit, 12 * unit));
                dc.DrawImage(Crop(frame, 52, 52, 4, 12), new Rect(12 * unit, 8 * unit, 4 * unit, 12 * unit));
            }

            // 头
            dc.DrawImage(Crop(frame, 8, 8, 8, 8), new Rect(4 * unit, 0, 8 * unit, 8 * unit));
            dc.DrawImage(Crop(frame, 40, 8, 8, 8), new Rect(4 * unit, 0, 8 * unit, 8 * unit));
        }
        return Rasterize(visual, width, height);
    }

    private static CroppedBitmap Crop(BitmapSource frame, int x, int y, int w, int h) =>
        new(frame, new Int32Rect(x, y, w, h));

    private static ImageSource Rasterize(DrawingVisual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
