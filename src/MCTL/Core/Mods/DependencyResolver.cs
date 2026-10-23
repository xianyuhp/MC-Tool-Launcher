using System.IO;
using System.Text.Json;
using MCTL.Core.Mc;

namespace MCTL.Core.Mods;

/// <summary>缺失的前置模组（已解析到可下载文件）。</summary>
public class DependencyItem
{
    public ModSource Source { get; init; }
    public string ProjectId { get; init; } = "";
    public string Name { get; init; } = "";
    public ModFileEntry File { get; init; }
}

/// <summary>检测并补齐前置模组：读取文件的 required 依赖，与本地 mods 目录比对。</summary>
public static class DependencyResolver
{
    /// <summary>返回缺失（未安装）的前置模组列表。</summary>
    public static async Task<List<DependencyItem>> FindMissingAsync(
        IEnumerable<(ModSource Source, string ProjectId)> required,
        string gameVersion, string loader, string modsDir, CancellationToken ct = default)
    {
        var missing = new List<DependencyItem>();
        if (required == null) return missing;

        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in InstalledModService.Scan(modsDir))
        {
            AddKey(installed, mod.ModId);
            AddKey(installed, mod.Name);
            AddKey(installed, Path.GetFileNameWithoutExtension(mod.FileName));
        }

        foreach (var (source, projectId) in required.Distinct())
        {
            if (string.IsNullOrWhiteSpace(projectId)) continue;
            try
            {
                var (name, slug) = source == ModSource.CurseForge
                    ? await CurseForgeApi.GetProjectAsync(projectId, ct)
                    : await GetModrinthProjectAsync(projectId, ct);

                if (installed.Contains(Normalize(name)) ||
                    installed.Contains(Normalize(slug)) ||
                    installed.Contains(Normalize(projectId)))
                    continue;

                var files = await ModSearchService.GetFilesAsync(
                    new ModEntry { Source = source, ProjectId = projectId }, gameVersion, loader, ct);
                var file = files.FirstOrDefault();
                if (file == null) continue;

                missing.Add(new DependencyItem
                {
                    Source = source,
                    ProjectId = projectId,
                    Name = string.IsNullOrWhiteSpace(name) ? slug : name,
                    File = file
                });
            }
            catch (Exception ex)
            {
                Logger.Warn($"解析前置模组 {projectId} 失败：" + ex.Message);
            }
        }
        return missing;
    }

    private static async Task<(string Name, string Slug)> GetModrinthProjectAsync(string id, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        var json = await McApi.Client.GetStringAsync($"https://api.modrinth.com/v2/project/{id}", cts.Token);
        using var doc = JsonDocument.Parse(json);
        var name = doc.RootElement.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "";
        var slug = doc.RootElement.TryGetProperty("slug", out var slugValue) ? slugValue.GetString() ?? "" : "";
        return (name, slug);
    }

    private static void AddKey(HashSet<string> set, string value)
    {
        var key = Normalize(value);
        if (key.Length >= 2) set.Add(key);
    }

    private static string Normalize(string text) =>
        new string((text ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
