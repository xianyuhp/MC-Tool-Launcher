using System.Net.Http;
using System.Text.Json;
using AuroraLauncher.Core.Net;

namespace AuroraLauncher.Core.Mc;

/// <summary>一个可下载的服务端核心构建。</summary>
public record ServerBuild(string Value, string Label, string Url, string FileName, string Note);

/// <summary>服主小帮手：各类服务端核心（原版 / 插件端 / 混合端）的版本查询与下载地址。</summary>
public static class ServerCores
{
    public static readonly (string Value, string Label, string Facet)[] Cores =
    {
        ("vanilla", "原版 Vanilla", ""),
        ("paper", "Paper", "paper"),
        ("purpur", "Purpur", "purpur"),
        ("fabric", "Fabric", "fabric"),
        ("forge", "Forge", "forge"),
        ("neoforge", "NeoForge", "neoforge"),
        ("arclight", "Arclight 混合端", "forge"),
        ("mohist", "Mohist 混合端", "forge")
    };

    /// <summary>插件端类型（同时用于 Modrinth 的插件加载器筛选）。</summary>
    public static readonly (string Value, string Label)[] PluginServers =
    {
        ("", "全部服务端"),
        ("paper", "Paper"),
        ("spigot", "Spigot"),
        ("bukkit", "Bukkit"),
        ("purpur", "Purpur"),
        ("folia", "Folia"),
        ("velocity", "Velocity"),
        ("bungeecord", "BungeeCord"),
        ("waterfall", "Waterfall"),
        ("fabric", "Fabric"),
        ("forge", "Forge"),
        ("neoforge", "NeoForge")
    };

    /// <summary>Modrinth 插件分类。</summary>
    public static readonly (string Tag, string Label)[] PluginCategories =
    {
        ("optimization", "优化"), ("management", "管理"), ("utility", "实用"),
        ("adventure", "冒险"), ("game-mechanics", "机制"), ("economy", "经济"),
        ("minigame", "小游戏"), ("mobs", "生物"), ("worldgen", "世界生成"),
        ("decoration", "装饰"), ("storage", "存储"), ("transportation", "运输"),
        ("technology", "科技"), ("magic", "魔法"), ("social", "社交"),
        ("library", "前置库")
    };

    public static (string Value, string Label, string Facet) CoreInfo(string core) =>
        Cores.FirstOrDefault(c => c.Value == core, Cores[0]);

    /// <summary>各核心当前支持的 Minecraft 版本（优先取各自官网 / 官方 API）。</summary>
    public static async Task<List<string>> GetSupportedVersionsAsync(string core)
    {
        try
        {
            switch (core)
            {
                case "paper":
                    return SortDesc(await GetNamesAsync("https://api.papermc.io/v2/projects/paper", "versions"));
                case "purpur":
                    return SortDesc(await GetNamesAsync("https://api.purpurmc.org/v2/purpur", "versions"));
                case "arclight":
                    return SortDesc(await GetArclightVersionsAsync());
                case "mohist":
                    return SortDesc(await GetNamesAsync("https://mohistmc.com/api/v2/projects/mohist", "versions"));
                case "forge":
                    return SortDesc(await GetForgeVersionsAsync());
                case "neoforge":
                    return SortDesc(await GetNeoForgeVersionsAsync());
                default:
                    return (await McApi.GetVersionsAsync())
                        .Where(v => v.Type == "release").Select(v => v.Id).ToList();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"{core} 版本列表获取失败，回退到原版清单：{ex.Message}");
            try
            {
                return (await McApi.GetVersionsAsync()).Where(v => v.Type == "release").Select(v => v.Id).ToList();
            }
            catch
            {
                return new List<string>();
            }
        }
    }

    private static async Task<List<string>> GetNamesAsync(string url, string property)
    {
        using var doc = JsonDocument.Parse(await GetTextAsync(url));
        var list = new List<string>();
        if (!doc.RootElement.TryGetProperty(property, out var array)) return list;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value)) list.Add(value);
            }
            else if (item.ValueKind == JsonValueKind.Object &&
                     item.TryGetProperty("name", out var name) && name.GetString() is { } text)
            {
                list.Add(text);
            }
        }
        return list;
    }

    /// <summary>Arclight 官网（arclight.izzel.io）使用的官方文件接口。</summary>
    private const string ArclightFiles = "https://files.hypoglycemia.icu/v1/files/arclight";

    private static async Task<List<string>> GetArclightVersionsAsync()
    {
        try
        {
            return await GetNamesAsync(ArclightFiles + "/minecraft", "files");
        }
        catch
        {
            // 官网接口不可用时使用官网页面上列出的版本
            return new List<string>
            {
                "1.21.1", "1.21", "1.20.4", "1.20.2", "1.20.1", "1.20",
                "1.19.4", "1.19.2", "1.18.2", "1.17.1", "1.16.5"
            };
        }
    }

    private static async Task<List<string>> GetForgeVersionsAsync()
    {
        using var doc = JsonDocument.Parse(await GetTextAsync(Mirror.ForgePromotions));
        if (!doc.RootElement.TryGetProperty("promos", out var promos)) return new List<string>();

        var versions = new HashSet<string>();
        foreach (var promo in promos.EnumerateObject())
        {
            var key = promo.Name;
            var index = key.LastIndexOf('-');
            if (index <= 0) continue;
            versions.Add(key[..index]);
        }
        return versions.ToList();
    }

    private static async Task<List<string>> GetNeoForgeVersionsAsync()
    {
        var xml = await GetTextAsync($"{Mirror.NeoForgeMaven}/net/neoforged/neoforge/maven-metadata.xml");
        var manifest = (await McApi.GetVersionsAsync())
            .Where(v => v.Type == "release").Select(v => v.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new HashSet<string>();
        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(xml, "<version>([^<]+)</version>"))
        {
            var parts = match.Groups[1].Value.Split('.');
            if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || major < 20) continue;
            var mc = parts[1] == "0" ? $"1.{major}" : $"1.{major}.{parts[1]}";
            if (manifest.Contains(mc)) result.Add(mc);
        }
        return result.ToList();
    }

    /// <summary>版本号倒序（1.21.1 > 1.21 > 1.9）。</summary>
    private static List<string> SortDesc(IEnumerable<string> versions) =>
        versions.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(v => v, new McVersionComparer())
            .ToList();

    private sealed class McVersionComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            var a = (x ?? "").Split('.');
            var b = (y ?? "").Split('.');
            for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                var av = i < a.Length && int.TryParse(a[i], out var ai) ? ai : 0;
                var bv = i < b.Length && int.TryParse(b[i], out var bi) ? bi : 0;
                if (av != bv) return av.CompareTo(bv);
            }
            return string.CompareOrdinal(x, y);
        }
    }

    public static async Task<List<ServerBuild>> GetBuildsAsync(string core, string mc)
    {
        return core switch
        {
            "vanilla" => await GetVanillaAsync(mc),
            "paper" => await GetPaperAsync(mc),
            "purpur" => await GetPurpurAsync(mc),
            "fabric" => await GetFabricAsync(mc),
            "forge" => await GetForgeAsync(mc),
            "neoforge" => await GetNeoForgeAsync(mc),
            "arclight" => await GetArclightAsync(mc),
            "mohist" => await GetMohistAsync(mc),
            _ => new List<ServerBuild>()
        };
    }

    private static async Task<string> GetTextAsync(string url, int seconds = 30)
    {
        // 带重试与镜像替换：NeoForge / Forge Maven 不通时自动改用 ForgeCDN、BMCLAPI
        return await ResilientHttp.GetStringAsync(url, seconds);
    }

    private static async Task<List<ServerBuild>> GetVanillaAsync(string mc)
    {
        var versions = await McApi.GetVersionsAsync();
        var version = versions.FirstOrDefault(v => v.Id == mc)
                      ?? throw new Exception($"版本清单中没有 {mc}。");
        using var doc = JsonDocument.Parse(await McApi.GetVersionJsonAsync(version));
        if (!doc.RootElement.TryGetProperty("downloads", out var downloads) ||
            !downloads.TryGetProperty("server", out var server))
            throw new Exception($"{mc} 没有官方服务端（过旧版本请使用第三方核心）。");

        var url = server.GetProperty("url").GetString();
        return new List<ServerBuild>
        {
            new("server", $"{mc} 官方服务端", url, $"server-{mc}.jar",
                "官方 Vanilla 服务端：把 eula.txt 中的 eula 改为 true 后运行")
        };
    }

    private static async Task<List<ServerBuild>> GetPaperAsync(string mc)
    {
        var json = await GetTextAsync($"https://api.papermc.io/v2/projects/paper/versions/{mc}/builds");
        using var doc = JsonDocument.Parse(json);
        var result = new List<ServerBuild>();
        if (!doc.RootElement.TryGetProperty("builds", out var builds)) return result;

        foreach (var build in builds.EnumerateArray().TakeLast(10).Reverse())
        {
            if (!build.TryGetProperty("build", out var number)) continue;
            var id = number.GetInt32();
            var channel = build.TryGetProperty("channel", out var ch) ? ch.GetString() : "default";
            result.Add(new ServerBuild(
                id.ToString(),
                $"Build {id}（{channel}）",
                $"https://api.papermc.io/v2/projects/paper/versions/{mc}/builds/{id}/downloads/paper-{mc}-{id}.jar",
                $"paper-{mc}-{id}.jar",
                "Paper 插件服务端：支持 Bukkit / Spigot / Paper 插件"));
        }
        if (result.Count == 0) throw new Exception($"Paper 没有适用于 {mc} 的构建。");
        return result;
    }

    private static async Task<List<ServerBuild>> GetPurpurAsync(string mc)
    {
        // 先确认该版本存在
        await GetTextAsync($"https://api.purpurmc.org/v2/purpur/{mc}/latest", 20);
        return new List<ServerBuild>
        {
            new("latest", $"{mc} 最新构建", $"https://api.purpurmc.org/v2/purpur/{mc}/latest/download",
                $"purpur-{mc}-latest.jar", "Purpur 插件服务端：Paper 的高性能分支，兼容插件")
        };
    }

    private static async Task<List<ServerBuild>> GetFabricAsync(string mc)
    {
        var loaders = await McApi.GetFabricLoadersAsync(mc);
        if (loaders.Count == 0) throw new Exception($"Fabric 不支持 {mc}。");

        // Fabric 的服务端启动器地址需要带上安装器版本，缺这一段会 404（之前所有版本都下载失败的原因）
        var installer = await GetFabricInstallerVersionAsync();
        return loaders.Take(8).Select(l => new ServerBuild(
            l.Version,
            l.Version + (l.Stable ? "（稳定）" : ""),
            $"{Mirror.FabricMeta}/v2/versions/loader/{mc}/{l.Version}/{installer}/server/jar",
            $"fabric-server-{mc}-{l.Version}.jar",
            "Fabric 服务端启动器：首次运行会自动下载原版服务端并生成 eula.txt，模组放 mods 文件夹")).ToList();
    }

    private static string _fabricInstallerVersion;

    /// <summary>取 Fabric 安装器版本（服务端启动器 URL 的必需参数），失败时用已知稳定版本兜底。</summary>
    private static async Task<string> GetFabricInstallerVersionAsync()
    {
        if (!string.IsNullOrEmpty(_fabricInstallerVersion)) return _fabricInstallerVersion;
        try
        {
            var json = await GetTextAsync($"{Mirror.FabricMeta}/v2/versions/installer", 20);
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("version", out var version)) continue;
                var stable = item.TryGetProperty("stable", out var flag) && flag.GetBoolean();
                if (!stable) continue;
                _fabricInstallerVersion = version.GetString();
                break;
            }
            if (string.IsNullOrEmpty(_fabricInstallerVersion) && doc.RootElement.GetArrayLength() > 0)
                _fabricInstallerVersion = doc.RootElement[0].GetProperty("version").GetString();
        }
        catch (Exception ex)
        {
            Logger.Warn("获取 Fabric 安装器版本失败：" + ex.Message);
        }
        return _fabricInstallerVersion ?? "1.1.2";
    }

    private static async Task<List<ServerBuild>> GetForgeAsync(string mc)
    {
        var versions = await McInstaller.GetForgeVersionsAsync(mc);
        if (versions.Count == 0) throw new Exception($"Forge 没有适用于 {mc} 的服务端。");
        return versions.Select(v => new ServerBuild(
            v.Value,
            v.Label,
            $"{Mirror.ForgeMaven}/net/minecraftforge/forge/{mc}-{v.Value}/forge-{mc}-{v.Value}-installer.jar",
            $"forge-{mc}-{v.Value}-installer.jar",
            "安装器：运行后选择 Install server，或在命令行执行 java -jar xxx-installer.jar --installServer")).ToList();
    }

    private static async Task<List<ServerBuild>> GetNeoForgeAsync(string mc)
    {
        var versions = await McInstaller.GetNeoForgeVersionsAsync(mc);
        if (versions.Count == 0)
            throw new Exception($"NeoForge 没有适用于 {mc} 的服务端（1.20.1 及更早请用 Forge）。");
        return versions.Select(v => new ServerBuild(
            v.Value,
            v.Label,
            $"{Mirror.NeoForgeMaven}/net/neoforged/neoforge/{v.Value}/neoforge-{v.Value}-installer.jar",
            $"neoforge-{v.Value}-installer.jar",
            "安装器：运行后选择 Install server，或执行 java -jar xxx-installer.jar --installServer")).ToList();
    }

    private static async Task<List<ServerBuild>> GetArclightAsync(string mc)
    {
        using var loadersDoc = JsonDocument.Parse(
            await GetTextAsync($"{ArclightFiles}/minecraft/{mc}/loaders"));
        if (!loadersDoc.RootElement.TryGetProperty("files", out var loaders))
            throw new Exception($"Arclight 官网没有 {mc} 的服务端。");

        var result = new List<ServerBuild>();
        foreach (var loader in loaders.EnumerateArray())
        {
            var loaderName = loader.TryGetProperty("name", out var ln) ? ln.GetString() ?? "" : "";
            if (loaderName.Length == 0) continue;

            using var doc = JsonDocument.Parse(
                await GetTextAsync($"{ArclightFiles}/minecraft/{mc}/loaders/{loaderName}/versions-snapshot"));
            if (!doc.RootElement.TryGetProperty("files", out var files)) continue;

            foreach (var file in files.EnumerateArray())
            {
                var name = file.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (name.Length == 0) continue;

                // 官方站提供 permlink（对象地址）；统一走可访问的官方 API 域名
                var link = file.TryGetProperty("permlink", out var pl) ? pl.GetString() : null;
                if (!string.IsNullOrEmpty(link))
                {
                    var uri = new Uri(link);
                    link = "https://files.hypoglycemia.icu" + uri.PathAndQuery;
                }
                else
                {
                    link = file.TryGetProperty("link", out var l) ? l.GetString() : null;
                    link ??= $"{ArclightFiles}/minecraft/{mc}/loaders/{loaderName}/{name}";
                }

                var stable = !name.Contains("SNAPSHOT", StringComparison.OrdinalIgnoreCase);
                result.Add(new ServerBuild(
                    $"{loaderName}/{name}",
                    $"{loaderName} · {name}{(stable ? "" : "（快照）")}",
                    link,
                    $"arclight-{loaderName}-{mc}-{name}.jar",
                    "Arclight 混合端（Forge/NeoForge + Bukkit 插件）：下载后直接 java -jar 运行"));
            }
            if (result.Count >= 12) break;
        }
        if (result.Count == 0) throw new Exception($"Arclight 官网没有找到 {mc} 的文件。");
        return result;
    }

    private static async Task<List<ServerBuild>> GetMohistAsync(string mc)
    {
        var json = await GetTextAsync($"https://mohistmc.com/api/v2/projects/mohist/{mc}/builds");
        using var doc = JsonDocument.Parse(json);
        var result = new List<ServerBuild>();

        if (!doc.RootElement.TryGetProperty("builds", out var builds) ||
            builds.ValueKind != JsonValueKind.Array)
            throw new Exception($"Mohist 官网没有适用于 {mc} 的构建。");

        foreach (var build in builds.EnumerateArray().TakeLast(8).Reverse())
        {
            var number = build.TryGetProperty("number", out var n) ? n.GetInt32() : 0;
            var url = build.TryGetProperty("url", out var u) ? u.GetString() : null;
            url ??= build.TryGetProperty("downloadUrl", out var du) ? du.GetString() : null;
            url ??= $"https://mohistmc.com/api/v2/projects/mohist/{mc}/builds/{number}/download";

            var forge = build.TryGetProperty("forgeVersion", out var fv) ? fv.GetString() : null;
            var label = number > 0 ? $"Build {number}" : "最新构建";
            if (!string.IsNullOrEmpty(forge)) label += $"（Forge {forge}）";

            result.Add(new ServerBuild(label, label, url, $"mohist-{mc}-{number}.jar",
                "Mohist 混合端（Forge + Bukkit 插件）：下载后直接运行"));
        }

        if (result.Count == 0) throw new Exception($"Mohist 没有适用于 {mc} 的构建（可尝试 Arclight）。");
        return result;
    }
}
