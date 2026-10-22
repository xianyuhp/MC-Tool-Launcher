using System.Diagnostics;
using System.IO;
using System.Windows;
using AuroraLauncher.Core;
using AuroraLauncher.Core.Mc;

namespace UiSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "core")
            return CoreCheck().GetAwaiter().GetResult();
        if (args.Length > 0 && args[0] == "skin")
            return SkinCheck();
        if (args.Length > 1 && args[0] == "mcmod")
            return McmodCheck(args[1]).GetAwaiter().GetResult();
        if (args.Length > 0 && args[0] == "cmd")
            return CommandCheck();
        if (args.Length > 0 && args[0] == "cmdnames")
            return CommandNamesCheck();
        if (args.Length > 0 && args[0] == "apikeys")
            return ApiKeysCheck();
        if (args.Length > 1 && args[0] == "neoversions")
            return NeoForgeVersionsCheck(args[1]).GetAwaiter().GetResult();
        if (args.Length > 2 && args[0] == "dl")
            return DownloadCheck(args[1], args[2]).GetAwaiter().GetResult();
        if (args.Length > 2 && args[0] == "importpack")
            return PackCheck(args[1], args[2]).GetAwaiter().GetResult();
        if (args.Length > 1 && args[0] == "install")
            return InstallCheck(args[1], args[2]).GetAwaiter().GetResult();
        if (args.Length > 2 && args[0] == "prefetch")
            return PrefetchCheck(args[1], args[2]).GetAwaiter().GetResult();
        if (args.Length > 1 && args[0] == "launch")
            return LaunchCheck(args[1], args[2], args.Length > 3 ? args[3] : null).GetAwaiter().GetResult();
        if (args.Length > 1 && args[0] == "render")
            return RenderCommand(args[1]);
        if (args.Length > 1 && args[0] == "rendertest")
            return RenderTextBoxTest(args[1]);
        if (args.Length > 1 && args[0] == "renderhome")
            return RenderHome(args[1]);
        if (args.Length > 2 && args[0] == "renderpage")
            return RenderPage(args[1], args[2]);
        if (args.Length > 0 && args[0] == "modtest")
            return ModServiceCheck();
        if (args.Length > 0 && args[0] == "prefix")
            return PrefixCheck();
        if (args.Length > 0 && args[0] == "skin3d")
            return Skin3DCheck();
        if (args.Length > 0 && args[0] == "defaultskin")
            return DefaultSkinCheck();
        if (args.Length > 0 && args[0] == "skincache")
            return SkinCacheCheck().GetAwaiter().GetResult();
        if (args.Length > 0 && args[0] == "netcheck")
            return NetworkCheck().GetAwaiter().GetResult();
        if (args.Length > 1 && args[0] == "javadl")
            return JavaInstallCheck(int.Parse(args[1])).GetAwaiter().GetResult();
        if (args.Length > 1 && args[0] == "modsearch")
            return ModSearchCheck(args[1]).GetAwaiter().GetResult();
        if (args.Length > 1 && args[0] == "avatar")
            return AvatarCheck(args[1]).GetAwaiter().GetResult();

        var failures = 0;
        try
        {
            Settings.Load();
            AppLibrary.Load();
            var app = new AuroraLauncher.App();
            app.InitializeComponent();

            Check("HomeView", () =>
            {
                _ = new AuroraLauncher.Views.HomeView();
                Console.WriteLine("  memory after HomeView = " + Settings.Current.MemoryMb);
            });
            Check("DownloadView", () => _ = new AuroraLauncher.Views.DownloadView());
            Check("LibraryView", () => _ = new AuroraLauncher.Views.LibraryView());
            Check("SteamView", () => _ = new AuroraLauncher.Views.SteamView());
            Check("ToolboxView", () => _ = new AuroraLauncher.Views.ToolboxView());
            Check("ServerHelperView", () => _ = new AuroraLauncher.Views.ServerHelperView());
            Check("ColorFontView", () => _ = new AuroraLauncher.Views.ColorFontView());
            Check("CommandView", () => _ = new AuroraLauncher.Views.CommandView());
            Check("ModpackImportWindow", () =>
            {
                var w = new AuroraLauncher.Views.ModpackImportWindow("test.mrpack", null);
                w.Close();
            });
            Check("SettingsView", () => _ = new AuroraLauncher.Views.SettingsView());
            Check("LoginWindow", () => new AuroraLauncher.Views.LoginWindow().Close());
            Check("LoaderPickerWindow", () =>
            {
                var w = new AuroraLauncher.Views.LoaderPickerWindow("t", "h", new[] { ("1.0", "1.0") });
                w.Close();
            });
            Check("DownloadDetailWindow", () =>
            {
                var w = new AuroraLauncher.Views.DownloadDetailWindow(new DownloadContext("t"), AppContext.BaseDirectory);
                w.Close();
            });
            Check("PromptWindow", () => new AuroraLauncher.Views.PromptWindow("t", "h").Close());
            Check("CrashWindow", () =>
            {
                var temp = Path.Combine(Path.GetTempPath(), "aurora-crash-test");
                Directory.CreateDirectory(temp);
                var session = new GameSession { Process = new Process(), VersionId = "test", GameDir = temp };
                session.Output.Add("test line");
                var w = new AuroraLauncher.Views.CrashWindow(session, 1);
                w.Close();
            });
            Check("AppLibrary", () =>
            {
                var entry = AppLibrary.Add(Path.Combine(Environment.SystemDirectory, "notepad.exe"));
                if (!AppLibrary.Items.Any(x => x.Path == entry.Path)) throw new Exception("条目未加入");
                AppLibrary.Remove(entry);
            });
            Check("MainWindow", () => _ = new AuroraLauncher.MainWindow());
        }
        catch (Exception ex)
        {
            Console.WriteLine("FATAL " + ex);
            failures++;
        }

        Console.WriteLine(failures == 0 ? "UI SMOKE OK" : $"UI SMOKE FAILED ({failures})");
        return failures;

        void Check(string name, Action action)
        {
            try
            {
                action();
                Console.WriteLine("PASS " + name);
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>检查皮肤渲染：64×64 现代皮肤与 64×32 旧版皮肤都应成功生成图片。</summary>
    private static int SkinCheck()
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var renderHead = typeof(SkinService).GetMethod("RenderHead", flags);
        var renderBody = typeof(SkinService).GetMethod("RenderBody", flags);
        if (renderHead == null || renderBody == null)
        {
            Console.WriteLine("SKIN CHECK FAILED: 方法未找到");
            return 1;
        }

        var failures = 0;
        foreach (var height in new[] { 64, 32 })
        {
            var width = 64;
            var pixels = new byte[width * height * 4];
            new Random(7).NextBytes(pixels);
            var bmp = System.Windows.Media.Imaging.BitmapSource.Create(width, height, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4);
            try
            {
                var head = (System.Windows.Media.ImageSource)renderHead.Invoke(null, new object[] { bmp });
                var body = (System.Windows.Media.ImageSource)renderBody.Invoke(null, new object[] { bmp });
                Console.WriteLine($"skin {width}x{height}: head={head?.Width}x{head?.Height} body={body?.Width}x{body?.Height}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"skin {width}x{height} FAILED: {ex.InnerException?.Message ?? ex.Message}");
                failures++;
            }
        }
        Console.WriteLine(failures == 0 ? "SKIN CHECK OK" : "SKIN CHECK FAILED");
        return failures;
    }

    /// <summary>头像链路自测：名称 → UUID → 会话资料 → 皮肤 → 裁剪头像。</summary>
    private static async Task<int> AvatarCheck(string name)
    {
        Settings.Load();
        var image = await AuroraLauncher.Core.Auth.AvatarService.GetAvatarAsync(name);
        Console.WriteLine(image == null
            ? "avatar=null（回退默认头像）"
            : $"avatar={image.Width}x{image.Height}");
        Console.WriteLine("cache=" + Path.Combine(Settings.ConfigDir, "cache", "avatars"));
        return image != null ? 0 : 1;
    }

    /// <summary>
    /// 3D 皮肤控件自测：用四象限色块皮肤渲染头部，正对相机采样四个位置，
    /// 校验 UV 没有上下翻转 / 左右镜像。
    /// </summary>
    private static int Skin3DCheck()
    {
        var app = new AuroraLauncher.App();
        app.InitializeComponent();

        var pixels = new byte[64 * 64 * 4];
        void Fill(int x, int y, int w, int h, byte r, byte g, byte b, byte a = 255)
        {
            for (var py = y; py < y + h; py++)
            for (var px = x; px < x + w; px++)
            {
                var i = (py * 64 + px) * 4;
                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                pixels[i + 3] = a;
            }
        }

        Fill(0, 0, 64, 64, 40, 40, 40);
        // 帽子层填成不透明深色，测试里不叠加帽子层
        Fill(32, 0, 32, 16, 10, 10, 10);
        Fill(8, 8, 4, 4, 255, 0, 0);       // 正面左上：红
        Fill(12, 8, 4, 4, 0, 255, 0);      // 正面右上：绿
        Fill(8, 12, 4, 4, 0, 0, 255);      // 正面左下：蓝
        Fill(12, 12, 4, 4, 255, 255, 0);   // 正面右下：黄
        Fill(24, 8, 8, 8, 0, 255, 255);    // 背面：青
        Fill(8, 0, 8, 8, 255, 255, 255);   // 顶面：白
        Fill(16, 0, 8, 8, 255, 128, 0);    // 底面：橙
        Fill(0, 8, 8, 8, 128, 0, 0);       // 右侧：暗红
        Fill(16, 8, 8, 8, 0, 255, 0);      // 左侧：绿

        var skin = System.Windows.Media.Imaging.BitmapSource.Create(64, 64, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null, pixels, 64 * 4);

        var view = new AuroraLauncher.Controls.Skin3DView
        {
            Width = 320,
            Height = 320,
            HeadOnly = true,
            WalkAnimation = false,
            ViewYaw = 0,
            ViewPitch = 0
        };
        view.SetSkin(skin, false);
        Console.WriteLine("mesh: " + view.DebugMeshInfo());

        var host = new System.Windows.Controls.Border
        {
            Child = view,
            Width = 320,
            Height = 320,
            Background = System.Windows.Media.Brushes.Black
        };
        host.Measure(new System.Windows.Size(320, 320));
        host.Arrange(new System.Windows.Rect(0, 0, 320, 320));
        host.UpdateLayout();

        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(320, 320, 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(host);

        var stride = 320 * 4;
        var buffer = new byte[stride * 320];
        bmp.CopyPixels(new System.Windows.Int32Rect(0, 0, 320, 320), buffer, stride, 0);

        int minX = 320, minY = 320, maxX = -1, maxY = -1;
        for (var y = 0; y < 320; y++)
        for (var x = 0; x < 320; x++)
        {
            var i = y * stride + x * 4;
            if (buffer[i] + buffer[i + 1] + buffer[i + 2] < 40) continue;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        if (maxX < 0)
        {
            Console.WriteLine("SKIN3D FAILED: 没有渲染出任何像素");
            return 1;
        }

        Console.WriteLine($"head bbox = ({minX},{minY})-({maxX},{maxY})");

        (int R, int G, int B) Sample(double fx, double fy)
        {
            var x = (int)(minX + (maxX - minX) * fx);
            var y = (int)(minY + (maxY - minY) * fy);
            var i = y * stride + x * 4;
            return (buffer[i + 2], buffer[i + 1], buffer[i]);
        }

        var tl = Sample(0.28, 0.28);
        var tr = Sample(0.72, 0.28);
        var bl = Sample(0.28, 0.72);
        var br = Sample(0.72, 0.72);
        Console.WriteLine($"TL={tl} TR={tr} BL={bl} BR={br}");

        var failures = 0;
        void Check((int R, int G, int B) color, string name, Func<(int R, int G, int B), bool> test)
        {
            var ok = test(color);
            if (!ok) failures++;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name} {color}");
        }

        Check(tl, "左上应为红", c => c.R > c.G + 40 && c.R > c.B + 40);
        Check(tr, "右上应为绿", c => c.G > c.R + 40 && c.G > c.B + 40);
        Check(bl, "左下应为蓝", c => c.B > c.R + 40 && c.B > c.G + 40);
        Check(br, "右下应为黄", c => c.R > c.B + 40 && c.G > c.B + 40);

        // 清晰度：红色与绿色块的交界应只有很窄的过渡带（线性拉伸整张贴图时会糊成一大片）
        var midY = (int)(minY + (maxY - minY) * 0.35);
        var centerX = (minX + maxX) / 2;
        var blended = 0;
        var profile = new List<string>();
        for (var x = centerX - 40; x <= centerX + 40; x += 4)
        {
            var i = midY * stride + x * 4;
            profile.Add($"{buffer[i + 2]}/{buffer[i + 1]}");
        }
        Console.WriteLine("profile(R/G): " + string.Join(" ", profile));

        for (var x = centerX - 30; x <= centerX + 30; x++)
        {
            var i = midY * stride + x * 4;
            var r = buffer[i + 2];
            var g = buffer[i + 1];
            var b = buffer[i];
            if (r > 40 && g > 40 && b < 60 && Math.Abs(r - g) < 150) blended++;
        }
        Console.WriteLine($"blendWidth={blended}px");
        Check((blended, 0, 0), "红绿交界应锐利（<=8px）", c => c.Item1 <= 8);

        // 全身模式：确认身体 / 腿部都渲染出来，且头部在上方
        var body = new AuroraLauncher.Controls.Skin3DView
        {
            Width = 320,
            Height = 240,
            HeadOnly = false,
            WalkAnimation = false,
            ViewYaw = 0,
            ViewPitch = 0
        };
        body.SetSkin(skin, false);
        var bodyHost = new System.Windows.Controls.Border
        {
            Child = body,
            Width = 320,
            Height = 240,
            Background = System.Windows.Media.Brushes.Black
        };
        bodyHost.Measure(new System.Windows.Size(320, 240));
        bodyHost.Arrange(new System.Windows.Rect(0, 0, 320, 240));
        bodyHost.UpdateLayout();
        var bodyBmp = new System.Windows.Media.Imaging.RenderTargetBitmap(320, 240, 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bodyBmp.Render(bodyHost);
        var bodyStride = 320 * 4;
        var bodyBuffer = new byte[bodyStride * 240];
        bodyBmp.CopyPixels(new System.Windows.Int32Rect(0, 0, 320, 240), bodyBuffer, bodyStride, 0);

        int bMinY = 240, bMaxY = -1, opaque = 0;
        for (var y = 0; y < 240; y++)
        for (var x = 0; x < 320; x++)
        {
            var i = y * bodyStride + x * 4;
            if (bodyBuffer[i] + bodyBuffer[i + 1] + bodyBuffer[i + 2] < 40) continue;
            opaque++;
            if (y < bMinY) bMinY = y;
            if (y > bMaxY) bMaxY = y;
        }
        Console.WriteLine($"body opaque={opaque} yRange=({bMinY}-{bMaxY})");

        var height = bMaxY - bMinY;
        Check((opaque, height, 0), "全身像素应覆盖大部分画面", c => c.Item1 > 3000 && c.Item2 > 140);
        Console.WriteLine(failures == 0 ? "SKIN3D OK" : $"SKIN3D FAILED ({failures})");
        return failures;
    }

    /// <summary>皮肤磁盘缓存自测：拉取后写入缓存，再次启动可同步读到同一张皮肤。</summary>
    private static async Task<int> SkinCacheCheck()
    {
        AuroraLauncher.Core.Settings.Load();
        var dir = Path.Combine(AuroraLauncher.Core.Settings.ConfigDir, "cache", "skins");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);

        var fresh = await AuroraLauncher.Core.SkinService.LoadDefaultSkinAsync();
        if (fresh == null)
        {
            Console.WriteLine("SKINCACHE FAILED：未取到默认皮肤");
            return 1;
        }

        var bundled = AuroraLauncher.Core.SkinService.BundledDefault;
        Console.WriteLine(bundled == null
            ? "bundled=null"
            : $"bundled={bundled.Texture.PixelWidth}x{bundled.Texture.PixelHeight} slim={bundled.Slim}");

        var cached = AuroraLauncher.Core.SkinService.LoadCached("default");
        Console.WriteLine($"fresh={fresh.Texture.PixelWidth}x{fresh.Texture.PixelHeight} slim={fresh.Slim}");
        Console.WriteLine($"cached={(cached == null ? "null" : cached.Texture.PixelWidth + "x" + cached.Texture.PixelHeight)} slim={cached?.Slim}");
        Console.WriteLine(Path.Combine(dir, "default.png") + " exists=" + File.Exists(Path.Combine(dir, "default.png")));

        if (cached == null || cached.Texture.PixelWidth != fresh.Texture.PixelWidth || cached.Slim != fresh.Slim)
        {
            Console.WriteLine("SKINCACHE FAILED");
            return 1;
        }

        Console.WriteLine("SKINCACHE OK");
        return 0;
    }

    /// <summary>
    /// 网络容错自测：NeoForge 官方 Maven 在当前网络不可达，
    /// 验证会自动切换 ForgeCDN / BMCLAPI 镜像并下载成功。
    /// </summary>
    private static async Task<int> NetworkCheck()
    {
        AuroraLauncher.Core.Settings.Load();
        const string official =
            "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";

        Console.WriteLine("候选源：");
        foreach (var candidate in AuroraLauncher.Core.Net.ResilientHttp.Candidates(official))
            Console.WriteLine("  " + candidate);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var xml = await AuroraLauncher.Core.Net.ResilientHttp.GetStringAsync(official, 12);
        watch.Stop();
        var count = System.Text.RegularExpressions.Regex.Matches(xml, "<version>").Count;
        Console.WriteLine($"元数据：{xml.Length} 字符 / {count} 个版本，用时 {watch.Elapsed.TotalSeconds:0.0}s");
        if (count < 10)
        {
            Console.WriteLine("NETCHECK FAILED：元数据内容异常");
            return 1;
        }

        var target = Path.Combine(Path.GetTempPath(), "neoforge-metadata-test.xml");
        watch.Restart();
        await AuroraLauncher.Core.Mc.McApi.DownloadAsync(official, target, "", CancellationToken.None);
        watch.Stop();
        var size = new FileInfo(target).Length;
        Console.WriteLine($"下载：{size} 字节，用时 {watch.Elapsed.TotalSeconds:0.0}s");
        File.Delete(target);

        try
        {
            await AuroraLauncher.Core.Net.ResilientHttp.GetStringAsync(
                "https://maven.neoforged.net/releases/net/neoforged/neoforge/0.0.0-does-not-exist.jar", 8);
            Console.WriteLine("NETCHECK FAILED：不存在的文件竟然成功了");
            return 1;
        }
        catch (AuroraLauncher.Core.Net.DownloadUnavailableException ex)
        {
            Console.WriteLine("失败提示：\n" + ex.Message);
            if (ex.OfficialPage != "https://neoforged.net/")
            {
                Console.WriteLine("NETCHECK FAILED：官网链接不正确");
                return 1;
            }
        }

        Console.WriteLine("NETCHECK OK");
        return 0;
    }

    /// <summary>默认皮肤自测：解码混淆标识并确认能从官方拿到纹理。</summary>
    private static int DefaultSkinCheck()
    {
        var data = AuroraLauncher.Core.SkinService.LoadDefaultSkinAsync().GetAwaiter().GetResult();
        if (data == null)
        {
            Console.WriteLine("DEFAULT SKIN FAILED");
            return 1;
        }
        Console.WriteLine($"DEFAULT SKIN OK {data.Texture.PixelWidth}x{data.Texture.PixelHeight} slim={data.Slim}");
        return 0;
    }

    /// <summary>自动安装 Java 自测：Adoptium API → 下载 → 校验 → 解压 → Probe。</summary>
    private static async Task<int> JavaInstallCheck(int major)
    {
        Settings.Load();
        var last = 0;
        var progress = new Progress<(double Percent, string Message)>(p =>
        {
            if ((int)p.Percent / 20 == last) return;
            last = (int)p.Percent / 20;
            Console.WriteLine($"  {p.Percent:0}% {p.Message}");
        });
        try
        {
            var info = await JavaInstaller.InstallAsync(major, progress, CancellationToken.None);
            Console.WriteLine($"JAVA OK {info}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("JAVA FAILED " + ex);
            return 1;
        }
    }

    /// <summary>模组文件名前缀：三种模式 + 非法字符清洗 + 查不到原样返回。</summary>
    private static int PrefixCheck()
    {
        var sodium = new AuroraLauncher.Core.Mods.ModNameInfo("Sodium", "钠");
        var empty = new AuroraLauncher.Core.Mods.ModNameInfo("", "");
        var cases = new (string Actual, string Expected)[]
        {
            (AuroraLauncher.Core.Mods.ModNamePrefix.Apply("sodium-0.5.11.jar", sodium, "chinese"), "[钠] sodium-0.5.11.jar"),
            (AuroraLauncher.Core.Mods.ModNamePrefix.Apply("sodium-0.5.11.jar", sodium, "english"), "[Sodium] sodium-0.5.11.jar"),
            (AuroraLauncher.Core.Mods.ModNamePrefix.Apply("sodium-0.5.11.jar", sodium, "none"), "sodium-0.5.11.jar"),
            (AuroraLauncher.Core.Mods.ModNamePrefix.Apply("unknown.jar", empty, "chinese"), "unknown.jar"),
            (AuroraLauncher.Core.Mods.ModNamePrefix.Apply("x.jar", new AuroraLauncher.Core.Mods.ModNameInfo("A/B:C*D", "A/B:C*D"), "chinese"), "[A_B_C_D] x.jar")
        };

        var failures = 0;
        foreach (var (actual, expected) in cases)
        {
            var ok = actual == expected;
            if (!ok) failures++;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {actual}" + (ok ? "" : $"（期望 {expected}）"));
        }
        Console.WriteLine(failures == 0 ? "PREFIX OK" : $"PREFIX FAILED ({failures})");
        return failures;
    }

    /// <summary>本地模组扫描 / 启停 / 删除自测。</summary>
    private static int ModServiceCheck()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tal-modtest");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);

        var jar = Path.Combine(dir, "example-mod.jar");
        using (var zip = System.IO.Compression.ZipFile.Open(jar, System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("fabric.mod.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("{\"id\":\"examplemod\",\"name\":\"Example Mod\",\"version\":\"1.2.3\",\"description\":\"测试模组\"}");
        }

        var mods = AuroraLauncher.Core.Mods.InstalledModService.Scan(dir);
        Console.WriteLine($"scan={mods.Count} name={mods.FirstOrDefault()?.Name} id={mods.FirstOrDefault()?.ModId} enabled={mods.FirstOrDefault()?.Enabled}");
        if (mods.Count != 1 || mods[0].ModId != "examplemod" || mods[0].Name != "Example Mod") return 1;

        var mod = mods[0];
        AuroraLauncher.Core.Mods.InstalledModService.SetEnabled(mod, false);
        var disabled = AuroraLauncher.Core.Mods.InstalledModService.Scan(dir);
        Console.WriteLine($"disabled name={disabled.FirstOrDefault()?.FileName} enabled={disabled.FirstOrDefault()?.Enabled}");
        if (disabled.Count != 1 || disabled[0].Enabled) return 1;

        AuroraLauncher.Core.Mods.InstalledModService.SetEnabled(disabled[0], true);
        var enabled = AuroraLauncher.Core.Mods.InstalledModService.Scan(dir);
        Console.WriteLine($"enabled name={enabled.FirstOrDefault()?.FileName} enabled={enabled.FirstOrDefault()?.Enabled}");
        if (enabled.Count != 1 || !enabled[0].Enabled) return 1;

        AuroraLauncher.Core.Mods.InstalledModService.Delete(enabled[0]);
        Console.WriteLine(AuroraLauncher.Core.Mods.InstalledModService.Scan(dir).Count == 0
            ? "MOD SERVICE OK" : "MOD SERVICE FAILED");
        return 0;
    }

    /// <summary>双源检索自测：打印各源状态与合并后的条目。</summary>
    private static async Task<int> ModSearchCheck(string query)
    {
        Settings.Load();
        var result = await AuroraLauncher.Core.Mods.ModSearchService.SearchAsync(query, "mod", null, "fabric", 0, 5);
        foreach (var source in result.Sources)
            Console.WriteLine($"source {source.Label}: available={source.Available} {source.Message}");
        foreach (var entry in result.Entries)
            Console.WriteLine($"  [{entry.SourceLabel}] {entry.DisplayTitle} / slug={entry.Slug} / dl={entry.Downloads} / alt={entry.AlternateSource}");
        Console.WriteLine("中文名表：" + AuroraLauncher.Core.Mods.ChineseNameTable.Count + " 条");
        return result.Entries.Count > 0 ? 0 : 1;
    }

    /// <summary>渲染任意页面到 PNG：download / download-vanilla / color。</summary>
    private static int RenderPage(string view, string path)
    {
        Settings.Load();
        AppLibrary.Load();
        var app = new AuroraLauncher.App();
        app.InitializeComponent();

        System.Windows.FrameworkElement page = view switch
        {
            "download" or "download-vanilla" => new AuroraLauncher.Views.DownloadView(),
            "color" or "color-gradient" => new AuroraLauncher.Views.ColorFontView(),
            "settings" => new AuroraLauncher.Views.SettingsView(),
            "toolbox" => new AuroraLauncher.Views.ToolboxView(),
            _ => new AuroraLauncher.Views.HomeView()
        };

        if (view == "color-gradient" && page is AuroraLauncher.Views.ColorFontView colorView)
        {
            if (colorView.FindName("ModeGradient") is System.Windows.Controls.RadioButton gradient)
                gradient.IsChecked = true;
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var stops = (List<string>)typeof(AuroraLauncher.Views.ColorFontView).GetField("_stops", flags)?.GetValue(colorView);
            stops?.Insert(1, "e");
            typeof(AuroraLauncher.Views.ColorFontView).GetMethod("BuildPalettes", flags)?.Invoke(colorView, null);
        }

        if (view == "download-vanilla" && page is AuroraLauncher.Views.DownloadView download)
        {
            if (download.FindName("NavVanilla") is System.Windows.Controls.RadioButton nav) nav.IsChecked = true;
            if (download.FindName("ModPanel") is FrameworkElement mod) mod.Visibility = Visibility.Collapsed;
            if (download.FindName("VanillaPanel") is FrameworkElement vanilla) vanilla.Visibility = Visibility.Visible;
        }

        if (view == "home-mod" && page is AuroraLauncher.Views.HomeView home)
        {
            if (home.FindName("VersionPanel") is FrameworkElement version) version.Visibility = Visibility.Collapsed;
            if (home.FindName("ModPanel") is FrameworkElement mod) mod.Visibility = Visibility.Visible;
        }

        var host = new System.Windows.Controls.Border
        {
            Child = page,
            Width = 1220,
            Height = 780,
            Background = System.Windows.Media.Brushes.Black
        };
        host.Measure(new Size(1220, 780));
        host.Arrange(new Rect(0, 0, 1220, 780));
        host.UpdateLayout();

        foreach (var name in new[] { "NavMod", "NavVanilla", "ModPanel", "VanillaPanel", "ModList", "VersionList", "ModFilterHint", "FolderPanel",
                     "AvatarBorder", "AccountName", "AccountSub", "AccountBtn",
                     "AccountHead", "SkinCard", "BodySkinView", "LogCard", "SkinAuthorText" })
        {
            if (page.FindName(name) is not FrameworkElement element || element.ActualWidth <= 0) continue;
            var origin = element.TranslatePoint(new Point(0, 0), host);
            Console.WriteLine($"  {name}: x={origin.X:0} y={origin.Y:0} w={element.ActualWidth:0} h={element.ActualHeight:0} vis={element.Visibility}");
        }

        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(1220, 780, 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(host);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using (var fs = File.Create(path)) encoder.Save(fs);
        Console.WriteLine("rendered -> " + path);
        return 0;
    }

    /// <summary>渲染主页（右侧版本面板展开状态）。</summary>
    private static int RenderHome(string path)
    {
        Settings.Load();
        AppLibrary.Load();
        var app = new AuroraLauncher.App();
        app.InitializeComponent();

        var view = new AuroraLauncher.Views.HomeView();
        if (view.FindName("VersionPanel") is System.Windows.FrameworkElement panel)
            panel.Visibility = Visibility.Visible;

        var host = new System.Windows.Controls.Border
        {
            Child = view,
            Width = 1220,
            Height = 780,
            Background = System.Windows.Media.Brushes.Black
        };
        host.Measure(new Size(1220, 780));
        host.Arrange(new Rect(0, 0, 1220, 780));
        host.UpdateLayout();

        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(1220, 780, 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(host);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using (var fs = File.Create(path)) encoder.Save(fs);
        Console.WriteLine("rendered -> " + path);
        return 0;
    }

    /// <summary>对照实验：同一 Input 样式在不同高度 / 是否位于 ScrollViewer 内的文字渲染。</summary>
    private static int RenderTextBoxTest(string path)
    {
        Settings.Load();
        var app = new AuroraLauncher.App();
        app.InitializeComponent();

        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
        var inputs = new List<System.Windows.Controls.TextBox>();
        var variants = new (double Height, string Padding)[]
        {
            (32, "10,7"), (32, "10,4"), (32, "10,2"), (32, "10,0"),
            (26, "10,4"), (26, "10,0"), (34, "10,4"), (36, "10,4"), (28, "10,2")
        };
        foreach (var (height, padding) in variants)
        {
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"h={height} pad={padding}",
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 10
            });
            var box = new System.Windows.Controls.TextBox
            {
                Style = (Style)app.Resources["Input"],
                Height = height,
                Padding = ParsePadding(padding),
                FontSize = 11,
                Text = "Steve64",
                Margin = new Thickness(0, 2, 0, 6)
            };
            panel.Children.Add(box);
            inputs.Add(box);
        }

        var scroller = new System.Windows.Controls.ScrollViewer
        {
            Height = 120,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var inner = new System.Windows.Controls.StackPanel();
        inner.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "inside ScrollViewer height=32",
            Foreground = System.Windows.Media.Brushes.Gray,
            FontSize = 10
        });
        var inside = new System.Windows.Controls.TextBox
        {
            Style = (Style)app.Resources["Input"],
            Height = 32,
            FontSize = 11,
            Text = "InsideText"
        };
        inner.Children.Add(inside);
        scroller.Content = inner;
        panel.Children.Add(scroller);

        var host = new System.Windows.Controls.Border
        {
            Child = panel,
            Width = 420,
            Height = 480,
            Background = System.Windows.Media.Brushes.Black
        };
        host.Measure(new Size(420, 480));
        host.Arrange(new Rect(0, 0, 420, 480));
        host.UpdateLayout();

        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(840, 960, 192, 192,
            System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(host);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using (var fs = File.Create(path)) encoder.Save(fs);
        Console.WriteLine("rendered -> " + path);
        return 0;

        static Thickness ParsePadding(string value)
        {
            var parts = value.Split(',');
            return new Thickness(double.Parse(parts[0]), double.Parse(parts[1]),
                double.Parse(parts[0]), double.Parse(parts[1]));
        }
    }
    private static int RenderCommand(string path)
    {
        Settings.Load();
        AppLibrary.Load();
        var app = new AuroraLauncher.App();
        app.InitializeComponent();

        var view = new AuroraLauncher.Views.CommandView();
        var host = new System.Windows.Controls.Border
        {
            Child = view,
            Width = 1220,
            Height = 780,
            Background = System.Windows.Media.Brushes.Black
        };
        host.Measure(new Size(1220, 780));
        host.Arrange(new Rect(0, 0, 1220, 780));
        host.UpdateLayout();

        Set("GiveTarget", "Steve");
        Set("GiveCount", "64");
        Set("GiveName", "Excalibur");
        Set("SummonPos", "100 64 100");
        Set("SummonHealth", "40");
        host.UpdateLayout();
        foreach (var name in new[] { "GiveTarget", "GiveCount", "GiveName", "GiveLore", "GiveOutput", "SummonPos", "ItemSearch", "ItemList" })
        {
            if (view.FindName(name) is not FrameworkElement element) continue;
            var origin = element.TranslatePoint(new Point(0, 0), host);
            Console.WriteLine($"  {name}: x={origin.X:0} y={origin.Y:0} w={element.ActualWidth:0} h={element.ActualHeight:0}");
        }

        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(2440, 1560, 192, 192,
            System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(host);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using (var fs = File.Create(path)) encoder.Save(fs);
        Console.WriteLine("rendered -> " + path);
        return 0;

        void Set(string name, string text)
        {
            if (view.FindName(name) is System.Windows.Controls.TextBox box) box.Text = text;
            else Console.WriteLine("missing control " + name);
        }
    }

    private static async Task<int> DownloadCheck(string url, string path)
    {
        Settings.Load();
        Console.WriteLine($"多线程下载：{Settings.Current.MultiThreadDownload}（{Settings.Current.DownloadThreads} 线程）");
        try
        {
            var watch = Stopwatch.StartNew();
            var progress = new Progress<double>(p =>
            {
                if (watch.ElapsedMilliseconds % 1000 < 40) Console.WriteLine($"  {p * 100:0.0}%");
            });
            await McApi.DownloadAsync(url, path, "", CancellationToken.None, progress);
            watch.Stop();
            var info = new FileInfo(path);
            Console.WriteLine($"DL OK bytes={info.Length} elapsed={watch.Elapsed.TotalSeconds:0.0}s");
            return info.Length > 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("DL FAILED " + ex.Message);
            return 1;
        }
    }

    /// <summary>检查 NeoForge 版本列表能否按 MC 版本正确列出（1.21.1 → 21.1.x，1.20.1 → 1.20.1-47.x）。</summary>
    private static async Task<int> NeoForgeVersionsCheck(string mcVersion)
    {
        try
        {
            var list = await McInstaller.GetNeoForgeVersionsAsync(mcVersion);
            Console.WriteLine($"mc={mcVersion} count={list.Count}");
            foreach (var (value, _) in list.Take(5)) Console.WriteLine("  " + value);
            var ok = list.Count > 0;
            Console.WriteLine(ok ? "NEOVERSIONS OK" : "NEOVERSIONS FAIL");
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("NEOVERSIONS FAILED: " + ex.Message);
            return 1;
        }
    }

    /// <summary>开源版本 API 凭据：默认应为「未配置」，保存后可读回，窗口能正常构造。</summary>
    private static int ApiKeysCheck()
    {
        Settings.Load();
        Settings.Current.CurseForgeApiKey = "";
        AuroraLauncher.Core.ApiKeys.Load();

        Console.WriteLine($"msReady={AuroraLauncher.Core.ApiKeys.MicrosoftReady} " +
                          $"cfReady={AuroraLauncher.Core.ApiKeys.CurseForgeReady} " +
                          $"needsSetup={AuroraLauncher.Core.ApiKeys.NeedsSetup}");
        Console.WriteLine("path=" + AuroraLauncher.Core.ApiKeys.FilePath);
        Console.WriteLine("resources=" + string.Join(",", typeof(AuroraLauncher.Core.ApiKeys).Assembly
            .GetManifestResourceNames().Where(n => n.Contains("ApiKeys", StringComparison.OrdinalIgnoreCase))));
        // 初始状态取决于构建方式：开源版为 True；带内置凭据的正式版为 False
        var initial = AuroraLauncher.Core.ApiKeys.NeedsSetup;
        Console.WriteLine("builtInKeys=" + (!initial));

        var app = new AuroraLauncher.App();
        app.InitializeComponent();
        new AuroraLauncher.Views.ApiKeysWindow().Close();

        AuroraLauncher.Core.ApiKeys.Save("test-client-id", "test-key");
        AuroraLauncher.Core.ApiKeys.Load();
        var text = File.ReadAllText(AuroraLauncher.Core.ApiKeys.FilePath);
        var saved = AuroraLauncher.Core.ApiKeys.MicrosoftClientId == "test-client-id" &&
                    AuroraLauncher.Core.ApiKeys.CurseForgeKey == "test-key" &&
                    text.Contains("test-key", StringComparison.Ordinal);
        Console.WriteLine($"afterSave ms={AuroraLauncher.Core.ApiKeys.MicrosoftClientId} " +
                          $"cf={AuroraLauncher.Core.ApiKeys.CurseForgeKey} needsSetup={AuroraLauncher.Core.ApiKeys.NeedsSetup}");

        File.Delete(AuroraLauncher.Core.ApiKeys.FilePath);
        AuroraLauncher.Core.ApiKeys.Load();
        // 删掉配置文件后应回到构建时的默认状态（开源版=需要配置，正式版=内置凭据）
        var restored = AuroraLauncher.Core.ApiKeys.NeedsSetup == initial;

        var ok = saved && restored;
        Console.WriteLine(ok ? "APIKEYS OK" : "APIKEYS FAIL");
        return ok ? 0 : 1;
    }

    private static int CommandNamesCheck()
    {
        var items = AuroraLauncher.Core.Commands.GameData.Items;
        var mobs = AuroraLauncher.Core.Commands.GameData.Mobs;
        var missing = new List<string>();
        foreach (var id in items)
            if (AuroraLauncher.Core.Commands.GameData.ItemName(id).Length == 0) missing.Add("item:" + id);
        foreach (var id in mobs)
            if (AuroraLauncher.Core.Commands.GameData.MobName(id).Length == 0) missing.Add("mob:" + id);

        Console.WriteLine($"items={items.Length} mobs={mobs.Length} missing={missing.Count}");
        foreach (var id in missing) Console.WriteLine("  missing " + id);
        Console.WriteLine("sample: diamond_sword=" + AuroraLauncher.Core.Commands.GameData.ItemName("diamond_sword")
                          + " creeper=" + AuroraLauncher.Core.Commands.GameData.MobName("creeper"));
        Console.WriteLine(missing.Count == 0 ? "CMDNAMES OK" : "CMDNAMES FAIL");
        return missing.Count == 0 ? 0 : 1;
    }

    private static int CommandCheck()
    {
        var give = new AuroraLauncher.Core.Commands.GiveOptions
        {
            Target = "@p",
            ItemId = "minecraft:diamond_sword",
            Count = 1,
            Name = "神剑",
            Lore = "第一行\n第二行",
            Unbreakable = true
        };
        give.Enchantments.Add(new AuroraLauncher.Core.Commands.EnchantEntry("sharpness", 5));
        give.Enchantments.Add(new AuroraLauncher.Core.Commands.EnchantEntry("unbreaking", 3));
        give.Attributes.Add(new AuroraLauncher.Core.Commands.AttributeEntry("generic.armor", 20));
        give.Attributes.Add(new AuroraLauncher.Core.Commands.AttributeEntry("generic.attack_damage", 10));

        var summon = new AuroraLauncher.Core.Commands.SummonOptions
        {
            Target = "~ ~ ~",
            EntityId = "minecraft:zombie",
            Name = "守卫",
            Health = 40,
            Persistence = true,
            MainHand = "minecraft:diamond_sword",
            Head = "minecraft:diamond_helmet",
            Feet = "minecraft:diamond_boots"
        };
        summon.Attributes.Add(new AuroraLauncher.Core.Commands.AttributeEntry("generic.armor", 30));
        summon.Attributes.Add(new AuroraLauncher.Core.Commands.AttributeEntry("generic.attack_damage", 15));

        foreach (var (version, label, _) in AuroraLauncher.Core.Commands.CommandVersionExtensions.All)
        {
            Console.WriteLine($"== {label}");
            Console.WriteLine("GIVE  " + AuroraLauncher.Core.Commands.CommandSyntax.BuildGive(version, give));
            Console.WriteLine("SUMMON " + AuroraLauncher.Core.Commands.CommandSyntax.BuildSummon(version, summon));

            var effect = AuroraLauncher.Core.Commands.CommandCatalog.Find("effect");
            Console.WriteLine("EFFECT " + effect.Build(version, new Dictionary<string, string>
            {
                ["action"] = "give", ["target"] = "@p", ["effect"] = "minecraft:speed",
                ["seconds"] = "30", ["amplifier"] = "1"
            }));
            var title = AuroraLauncher.Core.Commands.CommandCatalog.Find("title");
            Console.WriteLine("TITLE " + title.Build(version, new Dictionary<string, string>
            {
                ["target"] = "@a", ["action"] = "title", ["text"] = "欢迎"
            }));
        }
        return 0;
    }

    private static async Task<int> McmodCheck(string name)
    {
        var chinese = await McmodService.GetChineseNameAsync(name);
        Console.WriteLine($"{name} -> {chinese ?? "(未找到)"}");
        return string.IsNullOrEmpty(chinese) ? 1 : 0;
    }

    private static async Task<int> PackCheck(string file, string gameDir)
    {
        Settings.Current.GameDir = gameDir;
        var log = new Progress<string>(s => Console.WriteLine("  " + s));
        var progress = new Progress<double>(_ => { });
        try
        {
            var id = await ModpackService.ImportAsync(file, gameDir, log, progress, CancellationToken.None);
            Console.WriteLine("IMPORT OK id=" + id);
            var dir = Path.Combine(gameDir, "versions", id);
            Console.WriteLine("files=" + Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length);

            var exported = Path.Combine(Path.GetTempPath(), id + "-export.mrpack");
            await ModpackService.ExportAsync(id, gameDir, exported, log, CancellationToken.None);
            Console.WriteLine("EXPORT OK size=" + new FileInfo(exported).Length);
            using var zip = System.IO.Compression.ZipFile.OpenRead(exported);
            Console.WriteLine("entries=" + zip.Entries.Count + " -> " +
                              string.Join(", ", zip.Entries.Take(6).Select(x => x.FullName)));
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("PACK FAILED: " + ex.Message);
            return 1;
        }
    }

    private static async Task<int> PrefetchCheck(string installer, string gameDir)
    {
        var log = new Progress<string>(s => Console.WriteLine("  " + s));
        try
        {
            await McInstaller.PrefetchInstallerLibrariesAsync(installer, gameDir, log, CancellationToken.None);
            var libsDir = Path.Combine(gameDir, "libraries");
            var jars = Directory.Exists(libsDir)
                ? Directory.GetFiles(libsDir, "*.jar", SearchOption.AllDirectories).Length
                : 0;
            Console.WriteLine($"PREFETCH RESULT: jars={jars}");
            return jars > 20 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("PREFETCH FAILED: " + ex);
            return 1;
        }
    }

    private static async Task<int> InstallCheck(string version, string gameDir)
    {
        Settings.Current.GameDir = gameDir;
        Console.WriteLine($"安装 {version} -> {gameDir}");
        var lastReport = DateTime.Now;
        var log = new Progress<string>(s =>
        {
            Console.WriteLine("  " + s);
            lastReport = DateTime.Now;
        });
        var progress = new Progress<double>(p =>
        {
            if ((DateTime.Now - lastReport).TotalSeconds > 5)
            {
                Console.WriteLine($"  progress {p * 100:0.0}%");
                lastReport = DateTime.Now;
            }
        });

        try
        {
            var sw = Stopwatch.StartNew();
            await McInstaller.InstallVanillaAsync(version, log, progress, CancellationToken.None);
            sw.Stop();
            var dir = Path.Combine(gameDir, "versions", version);
            var jar = Path.Combine(dir, version + ".jar");
            var natives = Directory.Exists(Path.Combine(dir, "natives")) ? Directory.GetFiles(Path.Combine(dir, "natives")).Length : 0;
            var libs = Directory.Exists(Path.Combine(gameDir, "libraries")) ? Directory.GetFiles(Path.Combine(gameDir, "libraries"), "*.jar", SearchOption.AllDirectories).Length : 0;
            var assets = Directory.Exists(Path.Combine(gameDir, "assets", "objects")) ? Directory.GetFiles(Path.Combine(gameDir, "assets", "objects"), "*", SearchOption.AllDirectories).Length : 0;
            Console.WriteLine($"INSTALL RESULT: jar={File.Exists(jar)} natives={natives} libs={libs} assets={assets} elapsed={sw.Elapsed.TotalSeconds:0.0}s");
            return File.Exists(jar) && libs > 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("INSTALL FAILED: " + ex);
            return 1;
        }
    }

    private static async Task<int> LaunchCheck(string version, string gameDir, string java)
    {
        Settings.Current.GameDir = gameDir;
        Console.WriteLine($"启动 {version}（{gameDir}）");
        try
        {
            var session = McLauncher.Start(new LaunchOptions
            {
                VersionId = version,
                PlayerName = "TestPlayer",
                MemoryMb = 1024,
                JavaPath = java,
                Width = 854,
                Height = 480
            });
            await Task.Delay(12000);
            if (!session.Process.HasExited)
            {
                Console.WriteLine("进程仍在运行，强制结束（说明启动流程成功）");
                session.Process.Kill(true);
            }
            Console.WriteLine($"退出代码：{session.Process.ExitCode}");
            Console.WriteLine("---- 进程输出（最后 40 行） ----");
            foreach (var line in session.Output.TakeLast(40)) Console.WriteLine("  " + line);
            return session.Output.Count > 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("LAUNCH FAILED: " + ex);
            return 1;
        }
    }

    private static async Task<int> CoreCheck()
    {
        int failures = 0;

        void Check(bool ok, string message)
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + message);
            if (!ok) failures++;
        }

        try
        {
            var versions = await McApi.GetVersionsAsync();
            Check(versions.Count > 100, $"Mojang 版本清单：{versions.Count} 个，最新正式版 {versions.First(v => v.Type == "release").Id}，镜像={McApi.UsingMirror}");

            var fabric = await McApi.GetFabricLoadersAsync(versions.First(v => v.Type == "release").Id);
            Check(fabric.Count > 0, $"Fabric loader 列表：{fabric.Count} 个，最新 {fabric.FirstOrDefault()?.Version}");
        }
        catch (Exception ex)
        {
            Check(false, "Mojang/Fabric 接口异常：" + ex.Message);
        }

        try
        {
            var rate = await SteamService.GetUsdCnyRateAsync();
            Check(rate > 1 && rate < 15, $"USD → CNY 汇率：{rate:0.000}");
        }
        catch (Exception ex)
        {
            Check(false, "汇率接口异常：" + ex.Message);
        }

        try
        {
            var code = await AuroraLauncher.Core.Auth.MicrosoftAuth.RequestDeviceCodeAsync(CancellationToken.None);
            Check(!string.IsNullOrEmpty(code.UserCode), $"微软设备代码获取成功：{code.UserCode}（{code.ExpiresIn}s）");
        }
        catch (Exception ex)
        {
            Check(false, "微软登录接口异常：" + ex.Message);
        }

        try
        {
            var promos = await McApi.Client.GetStringAsync(Mirror.ForgePromotions);
            Check(promos.Contains("1.21.1"), "Forge 版本清单可访问");
        }
        catch (Exception ex)
        {
            Check(false, "Forge 接口异常：" + ex.Message);
        }

        try
        {
            using var popular = await McApi.SearchModrinthAsync("", "shader", 5, "downloads");
            var popCount = popular.RootElement.GetProperty("hits").GetArrayLength();
            Check(popCount > 0, $"Modrinth 热门光影：{popCount} 个");
        }
        catch (Exception ex)
        {
            Check(false, "Modrinth 热门接口异常：" + ex.Message);
        }

        try
        {
            var s = Settings.Current;
            var a = Path.Combine(Path.GetTempPath(), "aurora-mc-second");
            var b = Path.Combine(Path.GetTempPath(), "aurora-mc-third");
            s.AddGameFolder(a);
            s.AddGameFolder(b);
            Check(s.GameDirs.Count >= 2 && s.GameDirs.Contains(b), $"多目录管理：{s.GameDirs.Count} 个目录");
            s.RemoveGameFolder(b);
            Check(s.GameDirs.Count >= 1, "移除目录后恢复");
        }
        catch (Exception ex)
        {
            Check(false, "多目录管理异常：" + ex.Message);
        }

        try
        {
            var temp = Path.Combine(Path.GetTempPath(), "aurora-version-test");
            var verDir = Path.Combine(temp, "versions", "1.20.1-forge-47.4.10");
            Directory.CreateDirectory(verDir);
            File.WriteAllText(Path.Combine(verDir, "1.20.1-forge-47.4.10.json"),
                "{\"id\":\"1.20.1-forge-47.4.10\",\"inheritsFrom\":\"1.20.1\"}");
            Settings.Current.GameDir = temp;
            var entries = McVersions.ScanAll();
            Check(entries.Count == 1 && entries[0].Icon != null, $"版本扫描与图标：{entries.Count} 个，图标={(entries[0]?.Icon != null)}");
            McVersions.Rename(temp, "1.20.1-forge-47.4.10", "我的整合包");
            Check(Directory.Exists(Path.Combine(temp, "versions", "我的整合包")), "版本重命名成功");
            McVersions.Delete(temp, "我的整合包");
            Check(!Directory.Exists(Path.Combine(temp, "versions", "我的整合包")), "版本删除成功");
            Check(McVersions.GetSubtitle(temp, "1.20.1-forge-47.4.10").Contains("Forge"), "副标题解析加载器成功");
            Directory.Delete(temp, true);
        }
        catch (Exception ex)
        {
            Check(false, "版本管理异常：" + ex.Message);
        }

        try
        {
            var games = await SteamService.SearchAsync("portal");
            Check(games.Count > 0, $"Steam 搜索：{games.Count} 个结果，首个 = {games.FirstOrDefault()?.Name}");
            if (games.Count > 0)
            {
                var price = await SteamService.GetPriceAsync(games[0].AppId);
                Check(!string.IsNullOrEmpty(price), $"Steam 国区价格：{games[0].Name} = {price}");
                var low = await SteamService.GetHistoricalLowAsync(games[0].AppId);
                Check(low.Usd != null, $"CheapShark 史低：{low.Display}");
                var history = await SteamService.GetPriceHistoryAsync(games[0].AppId);
                Check(history.Cny != null, $"国区史低：" + history.Display);
            }

            var cn = await SteamService.SearchAsync("传送门");
            Check(cn.Count > 0, $"中文名搜索「传送门」：{cn.Count} 个结果，首个 = {cn.FirstOrDefault()?.Name}");

            var discounted = await SteamService.GetDiscountedTopAsync();
            Check(discounted.Count > 0, $"折扣热销：{discounted.Count} 个，首个 = {discounted.FirstOrDefault()?.Name}");
            var editions = await SteamService.GetEditionsAsync(620);
            Check(editions.Count > 0, $"版本套餐：{editions.Count} 个，首个 = {editions.FirstOrDefault()?.Title} / {editions.FirstOrDefault()?.Price}");
        }
        catch (Exception ex)
        {
            Check(false, "Steam 接口异常：" + ex.Message);
        }

        try
        {
            using var doc = await McApi.SearchModrinthAsync("sodium", "mod", 5);
            var count = doc.RootElement.GetProperty("hits").GetArrayLength();
            Check(count > 0, $"Modrinth 搜索：{count} 个结果");
        }
        catch (Exception ex)
        {
            Check(false, "Modrinth 接口异常：" + ex.Message);
        }

        try
        {
            var javas = JavaFinder.FindAll();
            Check(javas.Count > 0, $"Java 检测：{javas.Count} 个 → {javas.FirstOrDefault()}");
        }
        catch (Exception ex)
        {
            Check(false, "Java 检测异常：" + ex.Message);
        }

        Console.WriteLine(failures == 0 ? "CORE OK" : $"CORE FAILED ({failures})");
        return failures;
    }
}
