using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using AuroraLauncher.Core.Mc;

namespace AuroraLauncher.Core.Mods;

/// <summary>CurseForge 客户端：官方接口需 API Key，失败或无 Key 时改用 MCIM 社区镜像。</summary>
public static class CurseForgeApi
{
    private const string OfficialBase = "https://api.curseforge.com/v1";
    private const string MirrorBase = "https://mod.mcimirror.top/curseforge/v1";
    private const int MinecraftGameId = 432;

    /// <summary>
    /// 内置 CurseForge API Key（与 PCL 相同的做法：Base64 存放，避免明文直接出现在源码中）。
    /// 设置里填写了自定义 Key 时优先使用自定义 Key。
    /// </summary>
    private const string EmbeddedKeyBase64 =
        "JDJhJDEwJDdXV1YxV0VMY3dYOFhwN2Q2OE1Icy41Z3JUeVpWYTZTeW55ZVN5TWgxcnNFODluSWEwOXpP";

    private static string ApiKey
    {
        get
        {
            var custom = Settings.Current.CurseForgeApiKey;
            if (!string.IsNullOrWhiteSpace(custom)) return custom.Trim();
            try
            {
                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(EmbeddedKeyBase64));
            }
            catch
            {
                return "";
            }
        }
    }

    public static bool LastUsedMirror { get; private set; }

    /// <summary>当前是否有可用通道（内置 Key 或自定义 Key）。</summary>
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    private static async Task<JsonDocument> GetJsonAsync(string pathAndQuery, CancellationToken ct)
    {
        var key = ApiKey;
        Exception last = null;

        var attempts = string.IsNullOrWhiteSpace(key)
            ? new[] { (MirrorBase, false) }
            : new[] { (OfficialBase, true), (MirrorBase, false) };

        foreach (var (baseUrl, useKey) in attempts)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + pathAndQuery);
                if (useKey) request.Headers.TryAddWithoutValidation("x-api-key", key);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                request.Headers.UserAgent.ParseAdd("MCTL/1.0");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(20));
                using var response = await McApi.Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cts.Token);
                LastUsedMirror = !useKey || baseUrl == MirrorBase;
                return JsonDocument.Parse(json);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }
        throw last ?? new Exception("CurseForge 接口不可用");
    }

    private static int LoaderType(string loader) => loader?.ToLowerInvariant() switch
    {
        "forge" => 1,
        "fabric" => 4,
        "quilt" => 5,
        "neoforge" => 6,
        _ => 0
    };

    /// <summary>classId：模组 6，整合包 4471（资源包/光影仍走 Modrinth）。</summary>
    public static int ClassIdFor(string projectType) => projectType switch
    {
        "modpack" => 4471,
        _ => 6
    };

    public static async Task<List<ModEntry>> SearchAsync(string query, string projectType, string gameVersion,
        string loader, int index, int pageSize, CancellationToken ct)
    {
        var parts = new List<string>
        {
            $"gameId={MinecraftGameId}",
            $"classId={ClassIdFor(projectType)}",
            "sortField=2",
            "sortOrder=desc",
            $"pageSize={Math.Clamp(pageSize, 1, 50)}",
            $"index={Math.Max(0, index)}"
        };
        if (!string.IsNullOrWhiteSpace(query)) parts.Add($"searchFilter={Uri.EscapeDataString(query)}");
        if (!string.IsNullOrWhiteSpace(gameVersion)) parts.Add($"gameVersion={Uri.EscapeDataString(gameVersion)}");
        var loaderType = LoaderType(loader);
        if (loaderType > 0) parts.Add($"modLoaderType={loaderType}");

        using var doc = await GetJsonAsync("/mods/search?" + string.Join("&", parts), ct);
        var result = new List<ModEntry>();
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in data.EnumerateArray())
        {
            var entry = new ModEntry
            {
                Source = ModSource.CurseForge,
                ProjectId = item.TryGetProperty("id", out var id) ? id.ToString() : "",
                Slug = item.TryGetProperty("slug", out var slug) ? slug.GetString() ?? "" : "",
                EnglishName = item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Summary = item.TryGetProperty("summary", out var summary) ? summary.GetString() ?? "" : "",
                Downloads = item.TryGetProperty("downloadCount", out var dl) && dl.TryGetInt64(out var count) ? count : 0,
                IconUrl = ReadIcon(item),
                Author = ReadAuthors(item),
                GameVersions = ReadIndexes(item, "gameVersion"),
                Loaders = ReadIndexes(item, "modLoader", true)
            };
            if (entry.ProjectId.Length == 0) continue;
            entry.ChineseName = ChineseNameTable.Lookup(ModSource.CurseForge, entry.ProjectId, entry.Slug, entry.EnglishName);
            result.Add(entry);
        }
        return result;
    }

    private static string ReadIcon(JsonElement item)
    {
        if (!item.TryGetProperty("logo", out var logo) || logo.ValueKind != JsonValueKind.Object) return "";
        return logo.TryGetProperty("thumbnailUrl", out var url) ? url.GetString() ?? "" : "";
    }

    private static string ReadAuthors(JsonElement item)
    {
        if (!item.TryGetProperty("authors", out var authors) || authors.ValueKind != JsonValueKind.Array) return "";
        var names = authors.EnumerateArray()
            .Select(a => a.TryGetProperty("name", out var n) ? n.GetString() : null)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Take(2);
        return string.Join(", ", names);
    }

    private static string ReadIndexes(JsonElement item, string field, bool loader = false)
    {
        if (!item.TryGetProperty("latestFilesIndexes", out var indexes) || indexes.ValueKind != JsonValueKind.Array)
            return "";
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var index in indexes.EnumerateArray())
        {
            if (!index.TryGetProperty(field, out var value)) continue;
            var text = loader ? LoaderName(value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0)
                              : value.GetString();
            if (!string.IsNullOrWhiteSpace(text)) values.Add(text);
        }
        return string.Join(", ", values.Take(6));
    }

    private static string LoaderName(int type) => type switch
    {
        1 => "forge",
        4 => "fabric",
        5 => "quilt",
        6 => "neoforge",
        _ => ""
    };

    public static async Task<(string Name, string Slug)> GetProjectAsync(string modId, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"/mods/{modId}", ct);
        if (!doc.RootElement.TryGetProperty("data", out var data)) return ("", "");
        var name = data.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var slug = data.TryGetProperty("slug", out var s) ? s.GetString() ?? "" : "";
        return (name, slug);
    }

    public static async Task<List<ModFileEntry>> GetFilesAsync(string modId, string gameVersion, string loader,
        CancellationToken ct)
    {
        var parts = new List<string> { "pageSize=50" };
        if (!string.IsNullOrWhiteSpace(gameVersion)) parts.Add($"gameVersion={Uri.EscapeDataString(gameVersion)}");
        var loaderType = LoaderType(loader);
        if (loaderType > 0) parts.Add($"modLoaderType={loaderType}");

        using var doc = await GetJsonAsync($"/mods/{modId}/files?" + string.Join("&", parts), ct);
        var files = new List<ModFileEntry>();
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return files;

        foreach (var file in data.EnumerateArray())
        {
            var fileId = file.TryGetProperty("id", out var id) ? id.ToString() : "";
            var fileName = file.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : "";
            if (fileName.Length == 0) continue;
            var downloadUrl = file.TryGetProperty("downloadUrl", out var url) ? url.GetString() : null;
            if (string.IsNullOrWhiteSpace(downloadUrl)) downloadUrl = MediaUrl(fileId, fileName);

            var sha1 = "";
            if (file.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Array)
            {
                foreach (var hash in hashes.EnumerateArray())
                {
                    if (hash.TryGetProperty("algo", out var algo) && algo.TryGetInt32(out var value) && value == 1 &&
                        hash.TryGetProperty("value", out var hashValue))
                    {
                        sha1 = hashValue.GetString() ?? "";
                        break;
                    }
                }
            }

            var gameVersions = "";
            if (file.TryGetProperty("gameVersions", out var gv) && gv.ValueKind == JsonValueKind.Array)
                gameVersions = string.Join(", ", gv.EnumerateArray().Select(x => x.GetString()).Where(x => x != null));

            var date = "";
            if (file.TryGetProperty("fileDate", out var fd) && DateTime.TryParse(fd.GetString(), out var parsed))
                date = parsed.ToLocalTime().ToString("yyyy-MM-dd");

            var size = file.TryGetProperty("fileLength", out var len) && len.TryGetInt64(out var bytes)
                ? FormatSize(bytes)
                : "";

            // relationType = 3 表示 RequiredDependency
            var required = new List<(ModSource, string)>();
            if (file.TryGetProperty("dependencies", out var dependencies) &&
                dependencies.ValueKind == JsonValueKind.Array)
            {
                foreach (var dependency in dependencies.EnumerateArray())
                {
                    if (!dependency.TryGetProperty("relationType", out var relation) ||
                        !relation.TryGetInt32(out var type) || type != 3) continue;
                    if (!dependency.TryGetProperty("modId", out var modIdValue)) continue;
                    var requiredId = modIdValue.ToString();
                    if (!string.IsNullOrWhiteSpace(requiredId))
                        required.Add((ModSource.CurseForge, requiredId));
                }
            }

            files.Add(new ModFileEntry
            {
                Source = ModSource.CurseForge,
                ProjectId = modId,
                FileId = fileId,
                FileName = fileName,
                DisplayName = file.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? fileName : fileName,
                Url = downloadUrl ?? "",
                Sha1 = sha1,
                GameVersions = gameVersions,
                Loaders = "",
                Date = date,
                Size = size,
                RequiredProjects = required
            });
        }

        return files
            .Where(f => f.Url.Length > 0)
            .OrderByDescending(f => f.Date, StringComparer.Ordinal)
            .Take(40)
            .ToList();
    }

    private static string MediaUrl(string fileId, string fileName)
    {
        if (!long.TryParse(fileId, out var id)) return null;
        return $"https://mediafilez.forgecdn.net/files/{id / 1000}/{id % 1000}/{Uri.EscapeDataString(fileName)}";
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB",
        >= 1024 => (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB",
        _ => bytes + " B"
    };
}
