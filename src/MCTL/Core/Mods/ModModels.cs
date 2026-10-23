namespace MCTL.Core.Mods;

/// <summary>模组来源平台。</summary>
public enum ModSource
{
    Modrinth,
    CurseForge
}

/// <summary>统一的模组条目（合并 Modrinth 与 CurseForge 后的数据结构）。</summary>
public class ModEntry
{
    public ModSource Source { get; init; }
    /// <summary>Modrinth project id / CurseForge mod id。</summary>
    public string ProjectId { get; init; } = "";
    public string Slug { get; init; } = "";
    public string EnglishName { get; set; } = "";
    public string Author { get; init; } = "";
    public string Summary { get; set; } = "";
    public string IconUrl { get; init; } = "";
    public long Downloads { get; init; }
    public string Loaders { get; init; } = "";
    public string GameVersions { get; init; } = "";
    /// <summary>本地映射表中的中文名（仅用于界面显示）。</summary>
    public string ChineseName { get; set; } = "";
    /// <summary>另一平台也有同款模组时记录其 ID，便于切换来源。</summary>
    public string AlternateId { get; set; } = "";
    public ModSource? AlternateSource { get; set; }

    public string SourceLabel => Source == ModSource.CurseForge ? "CurseForge" : "Modrinth";
    public string DisplayTitle => string.IsNullOrEmpty(ChineseName) ? EnglishName : $"{ChineseName} | {EnglishName}";
}

/// <summary>统一的模组文件（用于下载）。</summary>
public class ModFileEntry
{
    public ModSource Source { get; init; }
    public string ProjectId { get; init; } = "";
    public string FileId { get; init; } = "";
    public string FileName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Url { get; init; } = "";
    public string Sha1 { get; init; } = "";
    public string GameVersions { get; init; } = "";
    public string Loaders { get; init; } = "";
    public string Date { get; init; } = "";
    public string Size { get; init; } = "";
    /// <summary>该文件声明的强制前置项目（跨平台统一表示）。</summary>
    public List<(ModSource Source, string ProjectId)> RequiredProjects { get; init; } = new();
}

/// <summary>某个数据源本次请求的可用状态。</summary>
public class SourceStatus
{
    public ModSource Source { get; init; }
    public bool Available { get; set; } = true;
    public string Message { get; set; } = "";

    public string Label => Source == ModSource.CurseForge ? "CurseForge" : "Modrinth";
}

/// <summary>双源搜索结果。</summary>
public class ModSearchResult
{
    public List<ModEntry> Entries { get; } = new();
    public List<SourceStatus> Sources { get; } = new();

    /// <summary>某个源不可用时的提示文本（都可用时为空）。</summary>
    public string UnavailableHint =>
        string.Join("　", Sources.Where(s => !s.Available).Select(s => $"{s.Label} 暂不可用"));
}
