using System.Text;
using System.Text.Json;
using AuroraLauncher.Core.Mc;

namespace AuroraLauncher.Core.Mods;

/// <summary>双源模组检索：并发请求 Modrinth 与 CurseForge，合并去重后返回。</summary>
public static class ModSearchService
{
    public static async Task<ModSearchResult> SearchAsync(string query, string projectType, string gameVersion,
        string loader, int page, int pageSize, CancellationToken ct = default)
    {
        var result = new ModSearchResult();
        var mrStatus = new SourceStatus { Source = ModSource.Modrinth };
        var cfStatus = new SourceStatus { Source = ModSource.CurseForge };
        result.Sources.Add(mrStatus);
        result.Sources.Add(cfStatus);

        // 中文关键词：若能匹配本地映射表，则同时用英文名检索
        var effectiveQuery = query ?? "";
        if (ContainsChinese(effectiveQuery))
        {
            var english = ChineseNameTable.EnglishForChinese(effectiveQuery).FirstOrDefault();
            if (!string.IsNullOrEmpty(english)) effectiveQuery = english;
        }

        var offset = Math.Max(0, page) * pageSize;
        var modrinthTask = LoadModrinthAsync(effectiveQuery, projectType, gameVersion, loader, offset, pageSize, ct);
        var useCurseForge = projectType is "mod" or "modpack";
        var curseTask = useCurseForge
            ? LoadCurseForgeAsync(effectiveQuery, projectType, gameVersion, loader, offset, pageSize, ct)
            : Task.FromResult((new List<ModEntry>(), true, ""));

        await Task.WhenAll(modrinthTask, curseTask);

        var (modrinth, mrOk, mrMessage) = modrinthTask.Result;
        var (curse, cfOk, cfMessage) = curseTask.Result;
        mrStatus.Available = mrOk;
        mrStatus.Message = mrMessage;
        cfStatus.Available = cfOk;
        cfStatus.Message = cfMessage;

        foreach (var entry in modrinth) Merge(result.Entries, entry);
        foreach (var entry in curse) Merge(result.Entries, entry);
        return result;
    }

    private static async Task<(List<ModEntry> Entries, bool Ok, string Message)> LoadModrinthAsync(string query,
        string projectType, string gameVersion, string loader, int offset, int pageSize, CancellationToken ct)
    {
        try
        {
            // 有关键词时按相关度排序，热门列表才按下载量
            var index = string.IsNullOrWhiteSpace(query) ? "downloads" : "relevance";
            using var doc = await McApi.SearchModrinthAsync(query, projectType, pageSize, index, offset,
                null, gameVersion, projectType == "mod" ? loader : null);
            var list = new List<ModEntry>();
            foreach (var hit in doc.RootElement.GetProperty("hits").EnumerateArray())
            {
                var name = hit.GetProperty("title").GetString() ?? "";
                var entry = new ModEntry
                {
                    Source = ModSource.Modrinth,
                    ProjectId = hit.GetProperty("project_id").GetString() ?? "",
                    Slug = hit.TryGetProperty("slug", out var slug) ? slug.GetString() ?? "" : "",
                    EnglishName = name,
                    Author = hit.TryGetProperty("author", out var author) ? author.GetString() ?? "" : "",
                    Summary = hit.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                    Downloads = hit.TryGetProperty("downloads", out var dl) && dl.TryGetInt64(out var count) ? count : 0,
                    IconUrl = hit.TryGetProperty("icon_url", out var icon) ? icon.GetString() ?? "" : "",
                    GameVersions = ReadArray(hit, "versions"),
                    Loaders = ReadArray(hit, "categories")
                };
                entry.ChineseName = ChineseNameTable.Lookup(ModSource.Modrinth, entry.ProjectId, entry.Slug, entry.EnglishName);
                list.Add(entry);
            }
            return (list, true, "");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warn("Modrinth 检索失败：" + ex.Message);
            return (new List<ModEntry>(), false, ex.Message);
        }
    }

    private static async Task<(List<ModEntry> Entries, bool Ok, string Message)> LoadCurseForgeAsync(string query,
        string projectType, string gameVersion, string loader, int offset, int pageSize, CancellationToken ct)
    {
        try
        {
            var list = await CurseForgeApi.SearchAsync(query, projectType, gameVersion, loader, offset, pageSize, ct);
            return (list, true, "");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warn("CurseForge 检索失败：" + ex.Message);
            return (new List<ModEntry>(), false, ex.Message);
        }
    }

    private static string ReadArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array) return "";
        return string.Join(", ", array.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x)).Take(8));
    }

    /// <summary>合并时去重：本地映射关联 → 中文名 → slug / 英文名（保守匹配）。</summary>
    private static void Merge(List<ModEntry> target, ModEntry entry)
    {
        var existing = target.FirstOrDefault(e => IsSameMod(e, entry));
        if (existing == null)
        {
            target.Add(entry);
            return;
        }

        // 优先保留 Modrinth 条目，同时记录另一平台以便切换
        if (existing.Source == ModSource.CurseForge && entry.Source == ModSource.Modrinth)
        {
            entry.AlternateId = existing.ProjectId;
            entry.AlternateSource = existing.Source;
            target.Remove(existing);
            target.Add(entry);
        }
        else
        {
            existing.AlternateId = entry.ProjectId;
            existing.AlternateSource = entry.Source;
        }
    }

    private static bool IsSameMod(ModEntry a, ModEntry b)
    {
        if (a.Source == b.Source) return false;

        var relation = ChineseNameTable.FindRelation(a.Source, a.ProjectId, a.Slug, a.EnglishName);
        if (relation != null &&
            ((!string.IsNullOrEmpty(relation.Id) && relation.Id.Equals(b.ProjectId, StringComparison.OrdinalIgnoreCase)) ||
             (!string.IsNullOrEmpty(relation.Slug) && relation.Slug.Equals(b.Slug, StringComparison.OrdinalIgnoreCase))))
            return true;

        if (!string.IsNullOrEmpty(a.ChineseName) && !string.IsNullOrEmpty(b.ChineseName) &&
            a.ChineseName.Equals(b.ChineseName, StringComparison.OrdinalIgnoreCase))
            return true;

        var slugA = Normalize(a.Slug);
        var slugB = Normalize(b.Slug);
        if (slugA.Length > 2 && slugA == slugB) return true;

        var nameA = Normalize(a.EnglishName);
        var nameB = Normalize(b.EnglishName);
        return nameA.Length > 2 && nameA == nameB;
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var builder = new StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
            if (char.IsLetterOrDigit(ch)) builder.Append(ch);
        return builder.ToString();
    }

    private static bool ContainsChinese(string text) =>
        !string.IsNullOrEmpty(text) && text.Any(c => c >= 0x4E00 && c <= 0x9FFF);

    /// <summary>获取某个模组在指定版本 / 加载器下的文件列表。</summary>
    public static async Task<List<ModFileEntry>> GetFilesAsync(ModEntry mod, string gameVersion, string loader,
        CancellationToken ct = default)
    {
        if (mod.Source == ModSource.CurseForge)
            return await CurseForgeApi.GetFilesAsync(mod.ProjectId, gameVersion, loader, ct);

        using var doc = await McApi.GetModrinthVersionsAsync(mod.ProjectId, gameVersion, loader);
        var files = new List<ModFileEntry>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return files;

        foreach (var version in doc.RootElement.EnumerateArray())
        {
            if (!version.TryGetProperty("files", out var versionFiles) || versionFiles.GetArrayLength() == 0) continue;
            JsonElement primary = default;
            var hasFile = false;
            foreach (var file in versionFiles.EnumerateArray())
            {
                if (!hasFile) { primary = file; hasFile = true; }
                if (file.TryGetProperty("primary", out var isPrimary) && isPrimary.GetBoolean()) { primary = file; break; }
            }
            if (!hasFile) continue;

            var sha1 = "";
            if (primary.TryGetProperty("hashes", out var hashes) && hashes.TryGetProperty("sha1", out var hash))
                sha1 = hash.GetString() ?? "";

            var size = primary.TryGetProperty("size", out var len) && len.TryGetInt64(out var bytes)
                ? (bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.0} MB" : $"{bytes / 1024.0:0} KB")
                : "";
            var date = version.TryGetProperty("date_published", out var published) &&
                       DateTime.TryParse(published.GetString(), out var parsed)
                ? parsed.ToLocalTime().ToString("yyyy-MM-dd")
                : "";

            files.Add(new ModFileEntry
            {
                Source = ModSource.Modrinth,
                ProjectId = mod.ProjectId,
                FileId = version.TryGetProperty("id", out var vid) ? vid.GetString() ?? "" : "",
                FileName = primary.GetProperty("filename").GetString() ?? "",
                DisplayName = version.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Url = primary.GetProperty("url").GetString() ?? "",
                Sha1 = sha1,
                GameVersions = version.TryGetProperty("game_versions", out var gv)
                    ? string.Join(", ", gv.EnumerateArray().Select(x => x.GetString()))
                    : "",
                Loaders = version.TryGetProperty("loaders", out var loaders)
                    ? string.Join(", ", loaders.EnumerateArray().Select(x => x.GetString()))
                    : "",
                Date = date,
                Size = size
            });
        }
        return files;
    }
}
