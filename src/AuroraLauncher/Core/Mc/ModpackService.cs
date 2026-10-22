using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AuroraLauncher.Core.Mc;

/// <summary>整合包导入 / 导出：支持 Modrinth .mrpack 与普通 zip（含 CurseForge manifest）。</summary>
public static class ModpackService
{
    /// <summary>粗略判断压缩包是否为可识别的整合包（modrinth.index.json / manifest.json）。</summary>
    public static bool LooksLikeModpack(string file)
    {
        try
        {
            if (file.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)) return true;
            if (!file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return false;
            using var zip = ZipFile.OpenRead(file);
            return zip.Entries.Any(e =>
                e.FullName.Equals("modrinth.index.json", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>导入整合包，返回新版本 ID（安装到游戏列表）。</summary>
    public static async Task<string> ImportAsync(string file, string gameDir,
        IProgress<string> log, IProgress<double> progress, CancellationToken ct)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        if (ext is not (".mrpack" or ".zip"))
            throw new Exception("仅支持 .mrpack 或 .zip 整合包。");

        var packName = SanitizeName(Path.GetFileNameWithoutExtension(file));
        var versionsDir = Path.Combine(gameDir, "versions");
        var target = Path.Combine(versionsDir, packName);
        for (var i = 2; Directory.Exists(target); i++)
        {
            packName = SanitizeName(Path.GetFileNameWithoutExtension(file)) + " (" + i + ")";
            target = Path.Combine(versionsDir, packName);
        }
        Directory.CreateDirectory(target);

        if (ext == ".mrpack")
            await ImportMrpackAsync(file, gameDir, target, packName, log, progress, ct);
        else
            await ImportZipAsync(file, gameDir, target, packName, log, progress, ct);

        progress.Report(1);
        log.Report("导入完成：" + packName);
        return packName;
    }

    private static async Task ImportMrpackAsync(string file, string gameDir, string target, string packName,
        IProgress<string> log, IProgress<double> progress, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(file);
        var indexEntry = zip.GetEntry("modrinth.index.json")
                         ?? throw new Exception("不是有效的 Modrinth 整合包（缺少 modrinth.index.json）。");

        JsonNode index;
        using (var stream = indexEntry.Open())
            index = JsonNode.Parse(stream) ?? throw new Exception("modrinth.index.json 解析失败。");

        var mc = index["dependencies"]?["minecraft"]?.GetValue<string>() ?? "";
        var loader = "";
        var loaderVersion = "";
        foreach (var (key, label) in new[]
                 {
                     ("fabric-loader", "Fabric"), ("forge", "Forge"),
                     ("neoforge", "NeoForge"), ("quilt-loader", "Quilt")
                 })
        {
            var value = index["dependencies"]?[key]?.GetValue<string>();
            if (string.IsNullOrEmpty(value)) continue;
            loader = label;
            loaderVersion = value;
            break;
        }

        var parent = FindParent(gameDir, mc, loader);
        var baseInstalled = false;
        if (parent == null)
        {
            parent = await InstallBaseAsync(gameDir, mc, loader, loaderVersion, log,
                new Progress<double>(p => progress.Report(p * 0.45)), ct);
            baseInstalled = parent != null;
        }
        if (parent == null)
            throw new Exception(
                $"未找到可继承的 {mc} {(loader.Length > 0 ? loader + " " : "")}版本，请先在下载页安装对应版本后再导入。");
        log.Report($"基础版本：{parent}（{mc}{(loader.Length > 0 ? " " + loader : "")}{(loaderVersion.Length > 0 ? " " + loaderVersion : "")}）");
        WriteVersionJson(target, packName, parent);

        var fileBase = baseInstalled ? 0.45 : 0.0;
        var fileSpan = 0.9 - fileBase;
        var files = index["files"] as JsonArray;
        var total = files?.Count ?? 0;
        var done = 0;
        var failed = 0;

        if (files != null)
        {
            foreach (var item in files)
            {
                ct.ThrowIfCancellationRequested();
                var path = item?["path"]?.GetValue<string>();
                var url = item?["downloads"]?.AsArray().FirstOrDefault()?.GetValue<string>();
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(url)) continue;

                var destination = Path.Combine(target, path.Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                try
                {
                    await McApi.DownloadAsync(url, destination, "", ct);
                }
                catch (Exception ex)
                {
                    failed++;
                    log.Report($"下载失败：{path}（{ex.Message}）");
                }
                done++;
                progress.Report(total == 0 ? fileBase : fileBase + (double)done / total * fileSpan);
            }
        }

        // overrides 覆盖文件
        var overrideCount = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue; // 目录条目
            var name = entry.FullName.Replace('/', '\\');
            if (!name.StartsWith("overrides\\", StringComparison.OrdinalIgnoreCase)) continue;
            var relative = name["overrides\\".Length..];
            if (relative.Length == 0 || relative.EndsWith("\\", StringComparison.Ordinal)) continue;
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
            overrideCount++;
        }
        log.Report($"已解压 overrides：{overrideCount} 个文件");
        if (failed > 0) log.Report($"有 {failed} 个文件下载失败（可能是网络限制），可稍后重新下载。");

        ExtractIcon(zip, target);
        progress.Report(0.95);
    }

    private static async Task ImportZipAsync(string file, string gameDir, string target, string packName,
        IProgress<string> log, IProgress<double> progress, CancellationToken ct)
    {
        string mc = null;
        string loader = null;
        string loaderVersion = null;
        using (var zip = ZipFile.OpenRead(file))
        {
            var manifest = zip.GetEntry("manifest.json");
            if (manifest != null)
            {
                try
                {
                    using var stream = manifest.Open();
                    var node = JsonNode.Parse(stream);
                    mc = node?["minecraft"]?["version"]?.GetValue<string>();
                    var loaderId = node?["minecraft"]?["modLoaders"]?.AsArray().FirstOrDefault()?["id"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(loaderId))
                    {
                        if (loaderId.StartsWith("forge", StringComparison.OrdinalIgnoreCase)) loader = "Forge";
                        else if (loaderId.StartsWith("neoforge", StringComparison.OrdinalIgnoreCase)) loader = "NeoForge";
                        else if (loaderId.StartsWith("fabric", StringComparison.OrdinalIgnoreCase)) loader = "Fabric";
                        var dash = loaderId.IndexOf('-');
                        if (dash > 0 && dash < loaderId.Length - 1) loaderVersion = loaderId[(dash + 1)..];
                    }
                }
                catch { }
            }

            var parent = FindParent(gameDir, mc, loader);
            var baseInstalled = false;
            if (parent == null && !string.IsNullOrWhiteSpace(mc))
            {
                parent = await InstallBaseAsync(gameDir, mc, loader, loaderVersion, log,
                    new Progress<double>(p => progress.Report(p * 0.45)), ct);
                baseInstalled = parent != null;
            }
            parent ??= FindAnyParent(gameDir);
            if (parent == null) throw new Exception("未找到可继承的游戏版本，请先安装一个 Minecraft 版本。");
            log.Report("基础版本：" + parent);
            WriteVersionJson(target, packName, parent);

            var fileBase = baseInstalled ? 0.45 : 0.0;
            var fileSpan = 0.95 - fileBase;
            var entries = zip.Entries.Where(e => !e.FullName.EndsWith('/')).ToList();
            var total = Math.Max(1, entries.Count);
            var done = 0;
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                var name = entry.FullName.Replace('/', '\\');
                // 跳过其它启动器的版本元数据
                if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                    name.StartsWith("versions\\", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) continue;

                var destination = Path.Combine(target, name);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                try { entry.ExtractToFile(destination, true); } catch { }
                done++;
                if (done % 20 == 0) progress.Report(fileBase + (double)done / total * fileSpan);
            }

            // 常见整合包把内容放在 overrides/ 下
            var overrides = Path.Combine(target, "overrides");
            if (Directory.Exists(overrides))
            {
                foreach (var dir in Directory.GetDirectories(overrides))
                {
                    var dest = Path.Combine(target, Path.GetFileName(dir));
                    if (Directory.Exists(dest)) continue;
                    Directory.CreateDirectory(dest);
                    foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(dir, f);
                        var d = Path.Combine(dest, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(d)!);
                        try { File.Copy(f, d, true); } catch { }
                    }
                }
            }

            ExtractIcon(zip, target);
        }
        await Task.CompletedTask;
        progress.Report(0.95);
    }

    /// <summary>导出为 Modrinth 兼容的 .mrpack（其它启动器 / PCL 可直接导入）。</summary>
    public static async Task ExportAsync(string versionId, string gameDir, string outputPath,
        IProgress<string> log, CancellationToken ct)
    {
        var versionDir = Path.Combine(gameDir, "versions", versionId);
        if (!Directory.Exists(versionDir)) throw new Exception("版本目录不存在。");

        var subtitle = McVersions.GetSubtitle(gameDir, versionId);
        var mc = subtitle.Split('·').FirstOrDefault()?.Trim() ?? "";
        var loader = "";
        var loaderVersion = "";
        if (subtitle.Contains("NeoForge")) { loader = "neoforge"; loaderVersion = subtitle.Split("NeoForge").Last().Trim(); }
        else if (subtitle.Contains("Forge")) { loader = "forge"; loaderVersion = subtitle.Split("Forge").Last().Trim(); }
        else if (subtitle.Contains("Fabric")) { loader = "fabric-loader"; loaderVersion = subtitle.Split("Fabric").Last().Trim(); }
        else if (subtitle.Contains("Quilt")) { loader = "quilt-loader"; loaderVersion = subtitle.Split("Quilt").Last().Trim(); }

        log.Report($"导出 {versionId}（{mc} {loader} {loaderVersion}）");

        var temp = Path.Combine(Path.GetTempPath(), "tal-modpack-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);
        try
        {
            var dependencies = new JsonObject { ["minecraft"] = mc };
            if (loader.Length > 0 && loaderVersion.Length > 0) dependencies[loader] = loaderVersion;

            var index = new JsonObject
            {
                ["formatVersion"] = 1,
                ["game"] = "minecraft",
                ["versionId"] = versionId,
                ["name"] = versionId,
                ["summary"] = "Exported by MCTL",
                ["files"] = new JsonArray(),
                ["dependencies"] = dependencies
            };
            await File.WriteAllTextAsync(Path.Combine(temp, "modrinth.index.json"), index.ToJsonString(), ct);

            // overrides：mods / config / 资源包 / 光影 / 关键配置
            var overrides = Path.Combine(temp, "overrides");
            Directory.CreateDirectory(overrides);
            foreach (var folder in new[] { "mods", "config", "resourcepacks", "shaderpacks", "defaultconfigs" })
            {
                var source = Path.Combine(versionDir, folder);
                if (!Directory.Exists(source)) continue;
                CopyDirectory(source, Path.Combine(overrides, folder), ct);
            }
            foreach (var file in new[] { "options.txt", "servers.dat" })
            {
                var source = Path.Combine(versionDir, file);
                if (File.Exists(source)) File.Copy(source, Path.Combine(overrides, file), true);
            }
            var icon = Path.Combine(versionDir, "icon.png");
            if (File.Exists(icon)) File.Copy(icon, Path.Combine(temp, "icon.png"), true);

            if (File.Exists(outputPath)) File.Delete(outputPath);
            await Task.Run(() => ZipFile.CreateFromDirectory(temp, outputPath, CompressionLevel.Fastest, false), ct);
            log.Report("已导出：" + outputPath);
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    private static void CopyDirectory(string source, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(source, file);
            var dest = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            try { File.Copy(file, dest, true); } catch { }
        }
    }

    private static void WriteVersionJson(string target, string id, string parent)
    {
        var json = new JsonObject
        {
            ["id"] = id,
            ["inheritsFrom"] = parent,
            ["type"] = "release",
            ["time"] = DateTime.UtcNow.ToString("o"),
            ["releaseTime"] = DateTime.UtcNow.ToString("o")
        };
        File.WriteAllText(Path.Combine(target, id + ".json"),
            json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void ExtractIcon(ZipArchive zip, string target)
    {
        var entry = zip.Entries.FirstOrDefault(e =>
            e.FullName.Equals("icon.png", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.EndsWith("/icon.png", StringComparison.OrdinalIgnoreCase));
        if (entry == null) return;
        try { entry.ExtractToFile(Path.Combine(target, "icon.png"), true); } catch { }
    }

    /// <summary>游戏列表里没有整合包所需的基础版本时，自动补齐原版 / 加载器，保证导入后能直接启动。</summary>
    private static async Task<string> InstallBaseAsync(string gameDir, string mc, string loader, string loaderVersion,
        IProgress<string> log, IProgress<double> progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mc)) return null;

        log.Report($"游戏列表中没有 {mc}{(string.IsNullOrEmpty(loader) ? "" : " " + loader)}，开始补齐基础客户端…");
        switch (loader)
        {
            case "Fabric":
                if (string.IsNullOrWhiteSpace(loaderVersion))
                {
                    log.Report("整合包未指定 Fabric 版本，先安装原版，导入后可在下载页补装 Fabric。");
                    await McInstaller.InstallVanillaAsync(mc, log, progress, ct, gameDir);
                    return mc;
                }
                await McInstaller.InstallFabricAsync(mc, loaderVersion, log, progress, ct, gameDir);
                return $"fabric-loader-{loaderVersion}-{mc}";

            case "Forge":
                return await McInstaller.InstallForgeAsync(mc, log, progress, ct, gameDir, loaderVersion);

            case "NeoForge":
                return await McInstaller.InstallNeoForgeAsync(mc, log, progress, ct, gameDir, loaderVersion);

            default:
                await McInstaller.InstallVanillaAsync(mc, log, progress, ct, gameDir);
                return mc;
        }
    }

    /// <summary>找与 基础版本 + 加载器 匹配的已安装版本。</summary>
    private static string FindParent(string gameDir, string mc, string loader)
    {
        if (string.IsNullOrWhiteSpace(mc)) return null;
        var versionsDir = Path.Combine(gameDir, "versions");
        if (!Directory.Exists(versionsDir)) return null;

        string fallback = null;
        foreach (var dir in Directory.GetDirectories(versionsDir))
        {
            var id = Path.GetFileName(dir);
            var subtitle = McVersions.GetSubtitle(gameDir, id);
            if (!subtitle.Contains(mc, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(loader)) return id;
            if (subtitle.Contains(loader, StringComparison.OrdinalIgnoreCase)) return id;
            fallback ??= id;
        }
        return fallback;
    }

    private static string FindAnyParent(string gameDir)
    {
        var versionsDir = Path.Combine(gameDir, "versions");
        if (!Directory.Exists(versionsDir)) return null;
        return Directory.GetDirectories(versionsDir)
            .Select(Path.GetFileName)
            .FirstOrDefault(id => File.Exists(Path.Combine(versionsDir, id, id + ".json")));
    }

    private static string SanitizeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim();
        return name.Length == 0 ? "整合包" : name;
    }
}
