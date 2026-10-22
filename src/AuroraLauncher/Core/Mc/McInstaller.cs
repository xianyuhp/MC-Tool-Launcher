using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>安装原版 Minecraft（客户端 jar + 依赖库 + 资源文件 + natives），返回版本 ID。</summary>
    public static async Task<string> InstallVanillaAsync(string versionId, IProgress<string> log, IProgress<double> progress,
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
        return versionId;
    }

    /// <summary>安装 Fabric 加载器（自动先装对应原版）。</summary>
    public static async Task<string> InstallFabricAsync(string mcVersion, string loaderVersion, IProgress<string> log,
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
        return id;
    }

    /// <summary>把生成的名字清理成合法的版本目录名。</summary>
    public static string SanitizeVersionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim().Where(c => !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 || cleaned == "." || cleaned == ".." ? "" : cleaned;
    }

    /// <summary>安装完成后按用户指定的名字重命名版本目录、版本 JSON 与客户端 jar。</summary>
    public static string RenameVersion(string gameDir, string oldId, string newName)
    {
        var newId = SanitizeVersionName(newName);
        if (newId.Length == 0 || newId == oldId) return oldId;

        var versionsDir = Path.Combine(gameDir, "versions");
        var source = Path.Combine(versionsDir, oldId);
        var destination = Path.Combine(versionsDir, newId);
        if (!Directory.Exists(source)) return oldId;
        if (Directory.Exists(destination)) throw new Exception($"版本名称「{newId}」已存在，请换一个。");

        Directory.Move(source, destination);

        var oldJson = Path.Combine(destination, oldId + ".json");
        var newJson = Path.Combine(destination, newId + ".json");
        if (File.Exists(oldJson))
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(oldJson))!.AsObject();
                node["id"] = newId;
                File.WriteAllText(newJson, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                if (!string.Equals(oldJson, newJson, StringComparison.OrdinalIgnoreCase)) File.Delete(oldJson);
            }
            catch
            {
                // 改写失败就把 json 原样改名，至少保证结构完整
                if (!string.Equals(oldJson, newJson, StringComparison.OrdinalIgnoreCase)) File.Move(oldJson, newJson);
            }
        }

        var oldJar = Path.Combine(destination, oldId + ".jar");
        if (File.Exists(oldJar)) File.Move(oldJar, Path.Combine(destination, newId + ".jar"));

        // 其它版本里 inheritsFrom 指向旧名字的，一并改掉，否则加载器版本会启动失败
        foreach (var json in Directory.GetFiles(versionsDir, "*.json", SearchOption.AllDirectories))
        {
            if (json.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(json)) as JsonObject;
                if (node?["inheritsFrom"]?.GetValue<string>() != oldId) continue;
                node["inheritsFrom"] = newId;
                File.WriteAllText(json, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
        return newId;
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

    /// <summary>
    /// 列出 NeoForge 可选版本。
    /// NeoForge 的版本号跟 Minecraft 不是一套：1.21.1 → 21.1.x，1.20.4 → 20.4.x，26.3 → 26.3.x；
    /// 1.20.1 及更早走旧构件 net/neoforged/forge，版本形如 1.20.1-47.1.x。
    /// </summary>
    public static async Task<List<(string Value, string Label)>> GetNeoForgeVersionsAsync(string mcVersion)
    {
        var parts = mcVersion.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            throw new Exception("NeoForge 不支持该版本：" + mcVersion);

        // 新版构件（1.20.2+）
        var prefixes = new List<string>();
        if (major == 1)
            prefixes.Add($"{parts[1]}.{(parts.Length > 2 ? parts[2] : "0")}.");
        else
            prefixes.Add($"{major}.{minor}.");

        var versions = new List<string>();
        var errors = new List<string>();

        try
        {
            // 官方 Maven 不通时会自动切换 ForgeCDN / BMCLAPI 镜像，并带指数退避重试
            var xml = await ResilientHttp.GetStringAsync(
                $"{Mirror.NeoForgeMaven}/net/neoforged/neoforge/maven-metadata.xml", 30);
            versions.AddRange(FilterVersions(xml, prefixes));
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        if (versions.Count == 0)
        {
            // 旧构件：1.20.1 等
            try
            {
                var legacy = await ResilientHttp.GetStringAsync(
                    $"{Mirror.NeoForgeMaven}/net/neoforged/forge/maven-metadata.xml", 30);
                versions.AddRange(FilterVersions(legacy, new[] { mcVersion + "-" }));
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }

        if (versions.Count == 0 && errors.Count > 0)
            throw new Exception("无法访问 NeoForge 仓库（网络可能被拦截），可改用 Forge。原始错误：" + errors[0]);

        return versions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(v => (v, Version.TryParse(v, out var parsed) ? parsed : new Version(0, 0)))
            .OrderByDescending(x => x.Item2)
            .ThenByDescending(x => x.v, StringComparer.OrdinalIgnoreCase)
            .Select(x => (x.v, x.v))
            .ToList();
    }

    /// <summary>从 maven-metadata.xml 里挑出符合前缀的版本号。</summary>
    private static IEnumerable<string> FilterVersions(string xml, IEnumerable<string> prefixes)
    {
        var list = prefixes.ToList();
        var doc = XDocument.Parse(xml);
        return doc.Descendants("version")
            .Select(x => x.Value.Trim())
            .Where(v => v.Length > 0 && list.Any(p => v.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
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
        await RunInstallerAsync(installer, gameDir, mcVersion, log, ct);
        progress.Report(1);

        var versionId = $"{mcVersion}-forge-{forgeVersion}";
        versionId = FindInstalledVersion(gameDir, new[] { versionId, full }, forgeVersion);
        log.Report("Forge 安装完成：" + versionId);
        return versionId;
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
        // 1.20.1 等旧版本使用 net/neoforged/forge 构件，版本号形如 1.20.1-47.1.x
        var legacy = version.StartsWith(mcVersion + "-", StringComparison.OrdinalIgnoreCase);
        if (!legacy && IsLegacyNeoForgeTarget(mcVersion))
        {
            // 整合包里通常只写 47.1.x，需要补上 MC 前缀才能对上旧构件
            version = mcVersion + "-" + version;
            legacy = true;
        }
        var artifact = legacy ? "forge" : "neoforge";
        var installerUrl = $"{Mirror.NeoForgeMaven}/net/neoforged/{artifact}/{version}/{artifact}-{version}-installer.jar";
        var installer = Path.Combine(gameDir, "installers", $"{artifact}-{version}-installer.jar");

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
        await RunInstallerAsync(installer, gameDir, mcVersion, log, ct);
        progress.Report(1);

        // 安装器建出来的版本目录名不固定（neoforge-<版本>、<MC>-neoforge-<版本> 等），按候选顺序找一遍
        var candidates = legacy
            ? new[]
            {
                $"{mcVersion}-neoforge-{version[(mcVersion.Length + 1)..]}",
                $"{mcVersion}-forge-{version[(mcVersion.Length + 1)..]}",
                "neoforge-" + version
            }
            : new[] { "neoforge-" + version, "neoforge-" + version + "-" + mcVersion, "neoforge-" + mcVersion + "-" + version };
        var versionId = FindInstalledVersion(gameDir, candidates, version);
        log.Report("NeoForge 安装完成：" + versionId);
        return versionId;
    }

    /// <summary>安装器生成的版本目录名不固定：先按候选名找，再在 versions 里按加载器 + 版本号扫一遍。</summary>
    private static string FindInstalledVersion(string gameDir, IEnumerable<string> candidates, string versionHint)
    {
        var list = candidates.Where(c => !string.IsNullOrEmpty(c)).ToList();
        var versionsDir = Path.Combine(gameDir, "versions");
        foreach (var id in list)
            if (Directory.Exists(Path.Combine(versionsDir, id))) return id;

        if (Directory.Exists(versionsDir) && !string.IsNullOrEmpty(versionHint))
        {
            var match = Directory.GetDirectories(versionsDir)
                .Select(Path.GetFileName)
                .Where(id => id.Contains("forge", StringComparison.OrdinalIgnoreCase) &&
                             id.Contains(versionHint, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(id => id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (match != null) return match;
        }
        return list.FirstOrDefault() ?? versionHint;
    }

    /// <summary>NeoForge 只有 1.20.2+ 才有 net/neoforged/neoforge 构件，1.20.1 及更早走 net/neoforged/forge。</summary>
    private static bool IsLegacyNeoForgeTarget(string mcVersion)
    {
        var parts = mcVersion.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            return false;
        if (major != 1) return major < 20; // 26.x 等新版不是 legacy
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0;
        return minor < 20 || (minor == 20 && patch <= 1);
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

    /// <summary>
    /// 补齐游戏目录的标准结构：launcher_profiles.json、versions 以及每个版本的隔离目录。
    /// 新建 .minecraft 时调用，避免安装器报「没有 launcher profile」、版本隔离目录也不存在。
    /// </summary>
    public static void EnsureGameDirLayout(string gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir)) return;
        var versionsDir = Path.Combine(gameDir, "versions");
        Directory.CreateDirectory(versionsDir);
        foreach (var folder in new[] { "mods", "resourcepacks", "shaderpacks", "saves", "config", "logs" })
            Directory.CreateDirectory(Path.Combine(gameDir, folder));
        EnsureLauncherProfiles(gameDir);

        // 已安装的版本补齐自己的隔离目录（开版本隔离后 mods / 存档都写进这里）
        foreach (var versionDir in Directory.EnumerateDirectories(versionsDir))
        {
            var id = Path.GetFileName(versionDir);
            if (!File.Exists(Path.Combine(versionDir, id + ".json"))) continue;
            foreach (var folder in new[] { "mods", "resourcepacks", "shaderpacks", "saves", "config", "crash-reports" })
                Directory.CreateDirectory(Path.Combine(versionDir, folder));
        }
    }

    /// <summary>
    /// 运行 Forge / NeoForge 安装器。
    /// 先带 <c>--installClient &lt;目录&gt;</c> 安装（否则 NeoForge 会装到 %APPDATA%\.minecraft），
    /// 旧版安装器不接受目录参数时自动去掉目录在工作目录内重试一次。
    /// </summary>
    private static async Task RunInstallerAsync(string installer, string gameDir, string mcVersion,
        IProgress<string> log, CancellationToken ct)
    {
        // NeoForge / Forge 安装器同样需要匹配的 Java（1.20.5+ 需要 Java 21）
        var required = JavaFinder.GuessMajor(mcVersion);
        var java = JavaFinder.PickCompatible(required) ?? JavaFinder.Pick(required);
        if (java == null) throw new Exception("未找到 Java，无法运行安装器。请先在设置中指定 java.exe。");
        log.Report($"使用 Java {java.Major} 运行安装器");

        // 安装器只会连官方 maven.neoforged.net，DNS 被污染时每个依赖都连接超时；
        // 先用我们自己的镜像容错下载好，安装器检查到文件已存在就会跳过下载。
        await PrefetchInstallerLibrariesAsync(installer, gameDir, log, ct);

        var (code, tail) = await RunInstallerOnceAsync(java.Path, installer, gameDir, true, log, ct);
        if (code == 0) return;

        // 旧版安装器可能把目录参数当成非法参数：去掉目录再试一次
        if (tail.Contains("--installClient", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("Unknown option", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("unrecognized", StringComparison.OrdinalIgnoreCase))
        {
            log.Report("该安装器不接受目录参数，改为在工作目录内重试…");
            (code, tail) = await RunInstallerOnceAsync(java.Path, installer, gameDir, false, log, ct);
            if (code == 0) return;
        }

        // 安装器的详细异常只写在自身日志里，取出来给用户看
        var detail = ReadInstallerLogTail(gameDir, installer);
        if (detail.Length > 0) tail += Environment.NewLine + detail;

        Logger.Warn($"安装器退出代码 {code}：{tail}");
        throw new Exception($"安装器退出代码 {code}。" +
                            (tail.Length > 0
                                ? Environment.NewLine + "安装器最后输出：" + Environment.NewLine + tail
                                : "请确认 Java 版本与网络是否正常。"));
    }

    /// <summary>
    /// 预下载 Forge / NeoForge 安装器自带 json 里声明的全部依赖到 &lt;gameDir&gt;/libraries。
    /// 官方安装器固定访问 maven.neoforged.net，该域名解析到错误 IP 时每个依赖都会 SocketTimeout；
    /// 我们走镜像（forgecdn / BMCLAPI）先放好文件，安装器校验 SHA1 通过后就不会再联网下载。
    /// </summary>
    public static async Task PrefetchInstallerLibrariesAsync(string installer, string gameDir,
        IProgress<string> log, CancellationToken ct)
    {
        var files = new List<(string Url, string Path, string Sha1)>();
        try
        {
            using var zip = ZipFile.OpenRead(installer);
            foreach (var name in new[] { "version.json", "install_profile.json" })
            {
                var entry = zip.GetEntry(name);
                if (entry == null) continue;
                using var stream = entry.Open();
                using var doc = JsonDocument.Parse(stream);
                if (!doc.RootElement.TryGetProperty("libraries", out var libraries)) continue;
                foreach (var lib in libraries.EnumerateArray())
                {
                    if (!lib.TryGetProperty("downloads", out var downloads)) continue;
                    if (!downloads.TryGetProperty("artifact", out var artifact)) continue;
                    var file = VersionJson.ParseFile(artifact);
                    if (file == null) continue;
                    files.Add((file.Url, Path.Combine(gameDir, "libraries", file.Path.Replace('/', '\\')), file.Sha1));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("读取安装器依赖清单失败：" + ex.Message);
            return;
        }

        files = files
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (files.Count == 0) return;

        log.Report($"预先补齐安装器依赖（{files.Count} 个文件，走镜像下载）");
        var failed = 0;
        string firstError = null;
        using var gate = new SemaphoreSlim(8);
        var tasks = files.Select(async file =>
        {
            await gate.WaitAsync(ct);
            try
            {
                await McApi.DownloadAsync(file.Url, file.Path, file.Sha1, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                firstError ??= Path.GetFileName(file.Path) + "：" + ex.Message;
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);

        if (failed > 0)
            log.Report($"有 {failed} 个依赖未能提前下载（安装器会自己再试）：{firstError}");
        else
            log.Report("安装器依赖已就绪，开始安装");
    }

    /// <summary>读取安装器自己写的日志末尾（其中的 Exception 才是真正的失败原因）。</summary>
    private static string ReadInstallerLogTail(string gameDir, string installer)
    {
        var candidates = new[]
        {
            Path.Combine(gameDir, Path.GetFileName(installer) + ".log"),
            Path.Combine(gameDir, "installer.log")
        };
        foreach (var path in candidates)
        {
            try
            {
                if (!File.Exists(path)) continue;
                var lines = File.ReadLines(path).TakeLast(200).ToList();
                var important = lines.Where(l =>
                    l.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                    l.Contains("Caused by", StringComparison.OrdinalIgnoreCase) ||
                    l.Contains("There was an error", StringComparison.OrdinalIgnoreCase)).TakeLast(6).ToList();
                var tail = important.Count > 0 ? important : lines.TakeLast(8).ToList();
                return $"安装器日志（{path}）：" + Environment.NewLine + string.Join(Environment.NewLine, tail);
            }
            catch { }
        }
        return "";
    }

    private static async Task<(int Code, string Tail)> RunInstallerOnceAsync(string java, string installer,
        string gameDir, bool withTargetPath, IProgress<string> log, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(java)
        {
            // 工作目录设置为游戏目录：旧版安装器会把客户端装进这里
            WorkingDirectory = gameDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installer);
        psi.ArgumentList.Add("--installClient");
        if (withTargetPath) psi.ArgumentList.Add(gameDir);

        var tail = new Queue<string>();
        void OnLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            log.Report(line);
            lock (tail)
            {
                tail.Enqueue(line);
                while (tail.Count > 25) tail.Dequeue();
            }
        }

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);

        string detail;
        lock (tail) detail = string.Join(Environment.NewLine, tail);
        return (process.ExitCode, detail);
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
