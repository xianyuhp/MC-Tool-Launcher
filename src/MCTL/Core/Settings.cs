using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCTL.Core;

/// <summary>启动器设置。配置集中放在 exe 同目录的 config 文件夹内。</summary>
public class Settings
{
    public static readonly string AppDir = AppContext.BaseDirectory.TrimEnd('\\');
    public static readonly string ConfigDir = Path.Combine(AppDir, "config");
    public static readonly string AudioDir = Path.Combine(ConfigDir, "audio");
    public static readonly string ImagesDir = Path.Combine(ConfigDir, "images");
    private static readonly string SettingsPath = Path.Combine(ConfigDir, "settings.json");
    private static readonly string LegacySettingsPath = Path.Combine(AppDir, "aurora.settings.json");

    public static Settings Current { get; private set; } = new();
    public static event Action Changed;

    // 外观
    public bool AcrylicEnabled { get; set; } = true;
    public int AcrylicOpacity { get; set; } = 68;
    public string BackgroundImage { get; set; } = "";
    public int BackgroundOpacity { get; set; } = 55;
    public int BackgroundBlur { get; set; } = 0;
    public string AccentColor { get; set; } = "#5B8CFF";
    /// <summary>主题：light / dark。</summary>
    public string ThemeMode { get; set; } = "dark";
    /// <summary>卡片不透明度 0-100。</summary>
    public int CardOpacity { get; set; } = 100;
    /// <summary>侧边栏不透明度 0-100。</summary>
    public int SidebarOpacity { get; set; } = 100;
    /// <summary>标题栏不透明度 0-100。</summary>
    public int TitleBarOpacity { get; set; } = 100;
    /// <summary>页面内容不透明度 0-100。</summary>
    public int ContentOpacity { get; set; } = 100;

    // 音乐
    public string MusicFolder { get; set; } = "";
    public int MusicVolume { get; set; } = 60;
    public bool MusicAutoPlay { get; set; } = true;
    /// <summary>播放音乐时标题文字使用彩色渐变（默认关闭）。</summary>
    public bool RainbowTitles { get; set; } = false;

    // 游戏
    public string GameDir { get; set; } = "";
    public List<string> GameFolders { get; set; } = new();
    public string JavaPath { get; set; } = "";
    /// <summary>自动选择最合适的 Java（忽略手动路径）。</summary>
    public bool JavaAutoSelect { get; set; } = true;
    /// <summary>启动游戏时强制中文界面（写入 options.txt 的 lang:zh_cn）。</summary>
    public bool ForceChinese { get; set; } = true;
    /// <summary>版本隔离：每个版本的 mods / 存档 / 配置独立（默认开启）。</summary>
    public bool VersionIsolation { get; set; } = true;
    public int MemoryMb { get; set; } = 4096;
    public string PlayerName { get; set; } = "Steve";
    /// <summary>离线模式下借用的正版皮肤名称 / UUID（仅用于启动器头像显示）。</summary>
    public string OfflineAvatarName { get; set; } = "";
    public bool Fullscreen { get; set; } = false;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;
    public string DownloadMirror { get; set; } = "auto";
    /// <summary>CurseForge 官方 API Key（留空则使用 MCIM 镜像通道）。</summary>
    public string CurseForgeApiKey { get; set; } = "";
    /// <summary>版本列表显示模式：all=综合（全部目录），selected=单独（仅勾选目录）。</summary>
    public string VersionDisplayMode { get; set; } = "all";
    /// <summary>单独模式下勾选的游戏目录。</summary>
    public List<string> VersionDisplayDirs { get; set; } = new();
    /// <summary>模组 / 插件列表每页展示数量（默认 15，上限 25）。</summary>
    public int ModPageSize { get; set; } = 15;
    /// <summary>下载模组的文件名前缀：chinese / english / none（默认中文，参考 PCL）。</summary>
    public string ModPrefixMode { get; set; } = "chinese";
    /// <summary>主页是否显示运行日志（默认关闭，关闭后主页内容自动铺满）。</summary>
    public bool ShowHomeLog { get; set; } = false;
    /// <summary>主页右侧 3D 皮肤预览（默认开启）。</summary>
    public bool ShowSkin3D { get; set; } = true;
    /// <summary>退出前选中的版本（下次启动自动还原）。</summary>
    public string SelectedVersionId { get; set; } = "";
    public string SelectedVersionDir { get; set; } = "";
    /// <summary>多线程分段下载（类似 Motrix），默认开启。</summary>
    public bool MultiThreadDownload { get; set; } = true;
    /// <summary>分段下载线程数（2-16）。</summary>
    public int DownloadThreads { get; set; } = 8;
    /// <summary>服务端默认保存根目录（留空则用启动器目录下的 server）。</summary>
    public string ServerRoot { get; set; } = "";
    /// <summary>用过的服务端保存根目录，供下载时下拉选择。</summary>
    public List<string> ServerRoots { get; set; } = new();

    // 微软账号
    public string MsRefreshToken { get; set; } = "";
    public string MsAccessToken { get; set; } = "";
    public string MsName { get; set; } = "";
    public string MsUuid { get; set; } = "";
    public long MsExpiresAt { get; set; }

    [JsonIgnore]
    public bool IsLoggedIn => !string.IsNullOrEmpty(MsRefreshToken) && !string.IsNullOrEmpty(MsName);

    [JsonIgnore]
    public string EffectiveGameDir => GameDirs[0];

    [JsonIgnore]
    public List<string> GameDirs
    {
        get
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(GameDir)) list.Add(GameDir.TrimEnd('\\'));
            foreach (var dir in GameFolders)
                if (!string.IsNullOrWhiteSpace(dir))
                    list.Add(dir.TrimEnd('\\'));
            if (list.Count == 0) list.Add(Path.Combine(AppDir, ".minecraft"));
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    [JsonIgnore]
    public string EffectiveMusicFolder =>
        string.IsNullOrWhiteSpace(MusicFolder) ? AudioDir : MusicFolder;

    public void AddGameFolder(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return;
        dir = dir.TrimEnd('\\');
        if (GameDirs.Any(d => d.Equals(dir, StringComparison.OrdinalIgnoreCase))) return;
        if (string.IsNullOrWhiteSpace(GameDir) && GameFolders.Count == 0) GameDir = dir;
        else GameFolders.Add(dir);
    }

    /// <summary>服务端默认保存根目录：没设置时用启动器目录下的 server。</summary>
    [JsonIgnore]
    public string EffectiveServerRoot =>
        string.IsNullOrWhiteSpace(ServerRoot) ? Path.Combine(AppDir, "server") : ServerRoot.TrimEnd('\\');

    /// <summary>服务端保存根目录列表：默认目录 + 用户选过的目录。</summary>
    [JsonIgnore]
    public List<string> ServerDirOptions
    {
        get
        {
            var list = new List<string> { EffectiveServerRoot };
            foreach (var dir in ServerRoots)
                if (!string.IsNullOrWhiteSpace(dir)) list.Add(dir.TrimEnd('\\'));
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public void AddServerRoot(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return;
        dir = dir.TrimEnd('\\');
        ServerRoot = dir;
        if (ServerRoots.Any(d => d.Equals(dir, StringComparison.OrdinalIgnoreCase))) return;
        ServerRoots.Add(dir);
    }

    public void RemoveGameFolder(string dir)
    {
        GameFolders.RemoveAll(d => d.Equals(dir, StringComparison.OrdinalIgnoreCase));
        if (GameDir.Equals(dir, StringComparison.OrdinalIgnoreCase))
        {
            GameDir = GameFolders.FirstOrDefault() ?? "";
            if (GameFolders.Count > 0) GameFolders.RemoveAt(0);
        }
    }

    public void SetPrimaryGameFolder(string dir)
    {
        var dirs = GameDirs;
        var target = dirs.FirstOrDefault(d => d.Equals(dir, StringComparison.OrdinalIgnoreCase));
        if (target == null) return;
        dirs.Remove(target);
        dirs.Insert(0, target);
        GameDir = dirs[0];
        GameFolders = dirs.Skip(1).ToList();
    }

    public static void Load()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            Directory.CreateDirectory(AudioDir);
            Directory.CreateDirectory(ImagesDir);

            string path = File.Exists(SettingsPath) ? SettingsPath
                : File.Exists(LegacySettingsPath) ? LegacySettingsPath
                : null;

            if (path != null)
                Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();

            if (path == LegacySettingsPath)
            {
                Current.MusicFolder = "";
                Save();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("读取设置失败，使用默认值：" + ex.Message);
            Current = new Settings();
        }
        Current.GameFolders ??= new List<string>();
        Current.VersionDisplayDirs ??= new List<string>();
        Directory.CreateDirectory(Current.EffectiveMusicFolder);
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(ImagesDir);
        try { Directory.CreateDirectory(Current.EffectiveGameDir); } catch { }
        if (!File.Exists(SettingsPath)) Save();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Logger.Warn("保存设置失败：" + ex.Message);
        }
        Changed?.Invoke();
    }
}
