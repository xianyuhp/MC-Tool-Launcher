using System.IO;

namespace AuroraLauncher.Core;

/// <summary>当前选中实例（游戏版本）的目录：版本隔离时位于 versions/&lt;id&gt; 下。</summary>
public static class InstancePaths
{
    public static string Root =>
        !string.IsNullOrEmpty(AppState.SelectedGameDir) ? AppState.SelectedGameDir : Settings.Current.EffectiveGameDir;

    public static string VersionId => AppState.SelectedVersionId;

    /// <summary>实例目录：开启版本隔离时为 versions/&lt;版本&gt;，否则为游戏根目录。</summary>
    public static string InstanceDir
    {
        get
        {
            var id = AppState.SelectedVersionId;
            if (!string.IsNullOrEmpty(id) && Settings.Current.VersionIsolation)
                return Path.Combine(Root, "versions", id);
            return Root;
        }
    }

    public static string Mods => Path.Combine(InstanceDir, "mods");
    public static string ResourcePacks => Path.Combine(InstanceDir, "resourcepacks");
    public static string ShaderPacks => Path.Combine(InstanceDir, "shaderpacks");
    public static string Saves => Path.Combine(InstanceDir, "saves");
    public static string CrashReports => Path.Combine(InstanceDir, "crash-reports");

    /// <summary>确保目录存在并返回。</summary>
    public static string Ensure(string dir)
    {
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }
}
