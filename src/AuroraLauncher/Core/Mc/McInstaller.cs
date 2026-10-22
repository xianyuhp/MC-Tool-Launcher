using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using AuroraLauncher.Core.Net;

namespace AuroraLauncher.Core.Mc;

public record LibraryFile(string Path, string Url, string Sha1);
public record LibraryEntry(string Name, LibraryFile Artifact, Dictionary<string, LibraryFile> Natives);

/// <summary>解析 version json 中的 libraries / rules / 参数。</summary>
public static class VersionJson
{
    public static bool RulesAllow(JsonElement element)
    {
        if (!element.TryGetProperty("rules", out var rules)) return true;
        bool allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            bool match = true;
            if (rule.TryGetProperty("os", out var os))
            {
                if (os.TryGetProperty("name", out var name) && !string.Equals(name.GetString(), "windows", StringComparison.OrdinalIgnoreCase))
                    match = false;
                if (os.TryGetProperty("arch", out var arch))
                {
                    var a = arch.GetString();
                    if (a is not ("x86_64" or "amd64" or "x86" or "i386")) match = false;
                }
            }
            if (rule.TryGetProperty("features", out var features))
                foreach (var f in features.EnumerateObject())
                    if (f.Value.ValueKind == JsonValueKind.True) match = false; // 未启用 demo / 自定义分辨率等特性

            if (!match) continue;
            allowed = rule.TryGetProperty("action", out var action) && action.GetString() == "allow";
        }
        return allowed;
    }

    public static string GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static LibraryFile ParseFile(JsonElement el)
    {
        var path = GetString(el, "path");
        var url = GetString(el, "url");
        if (path == null || url == null) return null;
        return new LibraryFile(path, url, GetString(el, "sha1") ?? "");
    }

    public static string MavenToPath(string name)
    {
        var ext = ".jar";
        if (name.Contains('@'))
        {
            var idx = name.LastIndexOf('@');
            ext = "." + name[(idx + 1)..];
            name = name[..idx];
        }
        var parts = name.Split(':');
        if (parts.Length < 3) return null;
        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : "";
        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}{ext}";
    }

    public static List<LibraryEntry> ParseLibraries(JsonElement root)
    {
        var result = new List<LibraryEntry>();
        if (!root.TryGetProperty("libraries", out var libs)) return result;

        foreach (var lib in libs.EnumerateArray())
        {
            if (!RulesAllow(lib)) continue;
            var name = GetString(lib, "name") ?? "";
            LibraryFile artifact = null;
            var natives = new Dictionary<string, LibraryFile>();
            var hasDownloads = lib.TryGetProperty("downloads", out var downloads);

            if (hasDownloads)
            {
                if (downloads.TryGetProperty("artifact", out var art))
                    artifact = ParseFile(art);
                if (downloads.TryGetProperty("classifiers", out var classifiers))
                {
                    foreach (var cls in classifiers.EnumerateObject())
                    {
                        if (!cls.Name.StartsWith("natives-windows")) continue;
                        var f = ParseFile(cls.Value);
                        if (f != null) natives[cls.Name] = f;
                    }
                }
            }

            // 只有完全没有 downloads 的库（Fabric 等 Maven 库）才按坐标拼接地址
            if (artifact == null && !hasDownloads && !string.IsNullOrEmpty(name))
            {
                var path = MavenToPath(name);
                if (path != null)
                {
                    var baseUrl = GetString(lib, "url") ?? "https://libraries.minecraft.net/";
                    artifact = new LibraryFile(path, baseUrl.TrimEnd('/') + "/" + path, "");
                }
            }

            // 只含 natives 的库（老版本 LWJGL）没有 artifact 时仍然要保留
            if (artifact != null || natives.Count > 0)
                result.Add(new LibraryEntry(name, artifact, natives));
        }
        return result;
    }
}

public static class McInstaller
{
    private static string ResolveDir(string gameDir) =>
        string.IsNullOrWhiteSpace(gameDir) ? Settings.Current.EffectiveGameDir : gameDir;

    /// <summary>安装原版 Minecraft（客户端 jar + 依赖库 + 资源文件 + natives）。</summary>
    public static async Task InstallVanillaAsync(string versionId, IProgress<string> log, IProgress<double> progress,
        CancellationToken ct, string gameDir = null)
    {
        gameDir = ResolveDir(gameDir);
        var entry = (await McApi.GetVersionsAsync()).FirstOrDefault(v => v.Id == versionId)
                    ?? throw new Exception("版本清单中不存在：" + versionId);

        log.Report("获取版本信息：" + versionId);
        progress.Report(0.02);
        var json = await McApi.GetVersionJsonAsync(entry);

        var versionDir = Path.Combine(gameDir, "versions", versionId);
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, versionId + ".json"), json);
        log.Report("版本 JSON 已保存");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 客户端 jar
        if (root.TryGetProperty("downloads", out var downloads) && downloads.TryGetProperty("client", out var client))
        {
            var url = VersionJson.GetString(client, "url");
            var sha1 = VersionJson.GetString(client, "sha1") ?? "";
            if (url != null)
            {
                log.Report("下载客户端 jar");
                var clientProgress = new Progress<double>(p => progress.Report(0.02 + p * 0.10));
                try
                {
                    await McApi.DownloadAsync(url, Path.Combine(versionDir, versionId + ".jar"), sha1, ct, clientProgress);
                }
                catch (Exception ex) when (!url.Contains("bmclapi", StringComparison.OrdinalIgnoreCase))
                {
                    log.Report("客户端下载失败，尝试镜像：" + ex.Message);
                    await McApi.DownloadAsync(Mirror.ToMirror(url), Path.Combine(versionDir, versionId + ".jar"), sha1, ct);
                }
            }
        }
        progress.Report(0.12);

        // 依赖库
        var libraries = VersionJson.ParseLibraries(root);
        var libFiles = new List<(string Url, string Path, string Sha1)>();
        foreach (var lib in libraries)
        {
            if (lib.Artifact != null)
                libFiles.Add((lib.Artifact.Url, Path.Combine(gameDir, "libraries", lib.Artifact.Path.Replace('/', '\\')), lib.Artifact.Sha1));
            foreach (var native in lib.Natives.Values)
                libFiles.Add((native.Url, Path.Combine(gameDir, "libraries", native.Path.Replace('/', '\\')), native.Sha1));
        }
        log.Report($"下载依赖库（{libFiles.Count} 个文件）");
        await McApi.DownloadManyAsync(libFiles, (n, t) => progress.Report(0.12 + 0.38 * n / Math.Max(t, 1)), ct);
        progress.Report(0.5);

        // 资源文件
        if (root.TryGetProperty("assetIndex", out var assetIndex))
        {
            var indexId = VersionJson.GetString(assetIndex, "id") ?? "legacy";
            var indexUrl = VersionJson.GetString(assetIndex, "url");
            var indexPath = Path.Combine(gameDir, "assets", "indexes", indexId + ".json");
            if (indexUrl != null)
            {
                await McApi.DownloadAsync(indexUrl, indexPath, VersionJson.GetString(assetIndex, "sha1") ?? "", ct,
                    Mirror.ToOfficial(indexUrl));
            }

            if (File.Exists(indexPath))
            {
                using var indexDoc = JsonDocument.Parse(File.ReadAllText(indexPath));
                if (indexDoc.RootElement.TryGetProperty("objects", out var objects))
                {
                    var assetFiles = new List<(string Url, string Path, string Sha1)>();
                    foreach (var obj in objects.EnumerateObject())
                    {
                        var hash = VersionJson.GetString(obj.Value, "hash");
                        if (string.IsNullOrEmpty(hash) || hash.Length < 2) continue;
                        var target = Path.Combine(gameDir, "assets", "objects", hash[..2], hash);
                        assetFiles.Add((Mirror.AssetObjectUrl(hash, McApi.UsingMirror), target, hash));
                    }
                    log.Report($"下载资源文件（{assetFiles.Count} 个对象，约数百 MB，请耐心等待）");
                    await McApi.DownloadManyAsync(assetFiles, (n, t) => progress.Report(0.5 + 0.45 * n / Math.Max(t, 1)), ct);
                }
            }
        }

        // natives 解压
        ExtractNatives(gameDir, versionDir, libraries, log, ct);
        progress.Report(1);
        log.Report("安装完成：" + versionId);
    }

    /// <summary>安装 Fabric 加载器（自动先装对应原版）。</summary>
    public static async Task InstallFabricAsync(string mcVersion, string loaderVersion, IProgress<string> log,
        IProgress<double> progress, CancellationToken ct, string gameDir = null)
    {
        gameDir = ResolveDir(gameDir);

        if (!File.Exists(Path.Combine(gameDir, "versions", mcVersion, mcVersion + ".jar")))
        {
            log.Report("未检测到原版 " + mcVersion + "，先安装原版");
            await InstallVanillaAsync(mcVersion, log, new Progress<double>(p => progress.Report(p * 0.7)), ct, gameDir);
        }

        log.Report($"获取 Fabric {loaderVersion} 配置");
        var profile = await McApi.GetFabricProfileAsync(mcVersion, loaderVersion);
        using var doc = JsonDocument.Parse(profile);
        var id = VersionJson.GetString(doc.RootElement, "id") ?? $"fabric-loader-{loaderVersion}-{mcVersion}";
        var versionDir = Path.Combine(gameDir, "versions", id);
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, id + ".json"), profile);

        var libraries = VersionJson.ParseLibraries(doc.RootElement);
        var files = libraries.Where(l => l.Artifact != null)
            .Select(l => (l.Artifact.Url, Path.Combine(gameDir, "libraries", l.Artifact.Path.Replace('/', '\\')), l.Artifact.Sha1))
            .ToList();
        log.Report($"下载 Fabric 依赖（{files.Count} 个文件）");
        await McApi.DownloadManyAsync(files, (n, t) => progress.Report(0.7 + 0.3 * n / Math.Max(t, 1)), ct);

        progress.Report(1);
        log.Report("Fabric 安装完成：" + id);
    }

    /// <summary>列出 Forge 可选版本（recommended / latest）。</summary>
    public static async Task<List<(string Value, string Label)>> GetForgeVersionsAsync(string mcVersion)
    {
        var promosJson = await McApi.Client.GetStringAsync(Mirror.ForgePromotions);
        using var promos = JsonDocument.Parse(promosJson);
        var root = promos.RootElement.GetProperty("promos");
        var list = new List<(string, string)>();
        string rv = null;
        if (root.TryGetProperty($"{mcVersion}-recommended", out var rec) && rec.GetString() is { } recommended)
        {
            rv = recommended;
            list.Add((rv, rv + "（推荐）"));
        }
        if (root.TryGetProperty($"{mcVersion}-latest", out var latest) && latest.GetString() is { } lv && lv != rv)
            list.Add((lv, lv + "（最新）"));
        return list;
    }

    /// <summary>列出 NeoForge 可选版本。</summary>
    public static async Task<List<(string Value, string Label)>> GetNeoForgeVersionsAsync(string mcVersion)
    {
        var parts = mcVersion.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            throw new Exception("NeoForge 不支持该版本：" + mcVersion);
        var prefix = $"{major}.{minor}.";

        string xml;
        try
        {
            // 官方 Maven 不通时会自动切换 ForgeCDN / BMCLAPI 镜像，并带指数退避重试
            xml = await ResilientHttp.GetStringAsync(
                $"{Mirror.NeoForgeMaven}/net/neoforged/neoforge/maven-metadata.xml", 30);
        }
        catch (DownloadUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new Exception("无法访问 NeoForge 仓库（网络可能被拦截），可改用 Forge。原始错误：" + ex.Message);
        }

        var doc = XDocument.Parse(xml);
        return doc.Descendants("version")
            .Select(x => x.Value.Trim())
            .Where(v => v.StartsWith(prefix, StringComparison.Ordinal))
            .Select(v => (v, Version.TryParse(v, out var p) ? p : new Version(0, 0)))
            .OrderByDescending(x => x.Item2)
            .Select(x => (x.v, x.v))
            .ToList();
    }

    /// <summary>使用官方安装器安装 Forge。</summary>
    public static async Task<string> InstallForgeAsync(string mcVersion, IProgress<string> log, IProgress<double> progress,
        CancellationToken ct, string gameDir = null, string forgeVersion = null)
    {
        gameDir = ResolveDir(gameDir);
        await EnsureVanillaAsync(mcVersion, gameDir, log, progress, ct);

        if (string.IsNullOrEmpty(forgeVersion))
        {
            log.Report("获取 Forge 版本信息");
            var candidates = await GetForgeVersionsAsync(mcVersion);
            if (candidates.Count == 0)
                throw new Exception($"Forge 没有适用于 {mcVersion} 的安装包（可尝试 Fabric 或更换版本）。");
            forgeVersion = candidates[0].Value;
        }

        var full = $"{mcVersion}-{forgeVersion}";
        var installerUrl = $"{Mirror.ForgeMaven}/net/minecraftforge/forge/{full}/forge-{full}-installer.jar";
        var installer = Path.Combine(gameDir, "installers", $"forge-{full}-installer.jar");

        log.Report($"下载 Forge 安装器 {full}");
        progress.Report(0.6);
        await McApi.DownloadAsync(installerUrl, installer, "", ct);

        EnsureLauncherProfiles(gameDir);
        log.Report("运行 Forge 安装器（首次可能较慢）");
        await RunInstallerAsync(installer, gameDir, log, ct);
        progress.Report(1);

        var versionId = $"{mcVersion}-forge-{forgeVersion}";
        log.Report("Forge 安装完成：" + versionId);
        return Directory.Exists(Path.Combine(gameDir, "versions", versionId)) ? versionId : full;
    }

    /// <summary>使用官方安装器安装 NeoForge（21.x 对应 MC 1.21.x 等）。</summary>
    public static async Task<string> InstallNeoForgeAsync(string mcVersion, IProgress<string> log, IProgress<double> progress,
        CancellationToken ct, string gameDir = null, string neoVersion = null)
    {
        gameDir = ResolveDir(gameDir);
        await EnsureVanillaAsync(mcVersion, gameDir, log, progress, ct);

        var version = neoVersion;
        if (string.IsNullOrEmpty(version))
        {
            log.Report("获取 NeoForge 版本列表");
            var versions = await GetNeoForgeVersionsAsync(mcVersion);
            if (versions.Count == 0)
                throw new Exception($"NeoForge 没有适用于 {mcVersion} 的版本（1.20.1 及更早请使用 Forge）。");
            version = versions[0].Value;
        }
        var installerUrl = $"{Mirror.NeoForgeMaven}/net/neoforged/neoforge/{version}/neoforge-{version}-installer.jar";
        var installer = Path.Combine(gameDir, "installers", $"neoforge-{version}-installer.jar");

        log.Report($"下载 NeoForge 安装器 {version}");
        progress.Report(0.6);
        try
        {
            await McApi.DownloadAsync(installerUrl, installer, "", ct);
        }
        catch (DownloadUnavailableException ex)
        {
            // 不自动下载缺失依赖：直接告诉用户缺什么、放到哪里、去哪里下
            throw new DownloadUnavailableException(installerUrl, ex.InnerException, $"可手动下载后放到：{installer}");
        }

        EnsureLauncherProfiles(gameDir);
        log.Report("运行 NeoForge 安装器（首次可能较慢）");
        await RunInstallerAsync(installer, gameDir, log, ct);
        progress.Report(1);

        var versionId = "neoforge-" + version;
        log.Report("NeoForge 安装完成：" + versionId);
        return Directory.Exists(Path.Combine(gameDir, "versions", versionId)) ? versionId : version;
    }

    private static async Task EnsureVanillaAsync(string mcVersion, string gameDir, IProgress<string> log,
        IProgress<double> progress, CancellationToken ct)
    {
        if (File.Exists(Path.Combine(gameDir, "versions", mcVersion, mcVersion + ".jar"))) return;
        log.Report("未检测到原版 " + mcVersion + "，先安装原版");
        await InstallVanillaAsync(mcVersion, log, new Progress<double>(p => progress.Report(p * 0.55)), ct, gameDir);
    }

    private static void EnsureLauncherProfiles(string gameDir)
    {
        var path = Path.Combine(gameDir, "launcher_profiles.json");
        if (File.Exists(path)) return;
        Directory.CreateDirectory(gameDir);
        File.WriteAllText(path,
            "{\"profiles\":{},\"settings\":{\"enableSnapshots\":false,\"enableAdvanced\":false,\"keepLauncherOpen\":false,\"showMenu\":false,\"showGameLog\":false,\"locale\":\"zh_CN\"},\"version\":3}");
    }

    private static async Task RunInstallerAsync(string installer, string gameDir, IProgress<string> log, CancellationToken ct)
    {
        var java = JavaFinder.Pick(17);
        if (java == null) throw new Exception("未找到 Java，无法运行安装器。请先在设置中指定 java.exe。");

        var psi = new ProcessStartInfo(java.Path)
        {
            WorkingDirectory = gameDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installer);
        psi.ArgumentList.Add("--installClient");

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log.Report(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log.Report(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new Exception($"安装器退出代码 {process.ExitCode}，请查看下方日志。");
    }

    private static void ExtractNatives(string gameDir, string versionDir, List<LibraryEntry> libraries,
        IProgress<string> log, CancellationToken ct)
    {
        var nativesDir = Path.Combine(versionDir, "natives");
        Directory.CreateDirectory(nativesDir);
        int count = 0;
        foreach (var lib in libraries)
        {
            foreach (var native in lib.Natives.Values)
            {
                var jar = Path.Combine(gameDir, "libraries", native.Path.Replace('/', '\\'));
                if (!File.Exists(jar)) continue;
                try
                {
                    using var archive = ZipFile.OpenRead(jar);
                    foreach (var entry in archive.Entries)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (entry.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                        var target = Path.Combine(nativesDir, entry.Name);
                        entry.ExtractToFile(target, true);
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    log.Report("解压 natives 失败：" + Path.GetFileName(jar) + " - " + ex.Message);
                }
            }
        }
        log.Report($"natives 解压完成（{count} 个 dll）");
    }
}
