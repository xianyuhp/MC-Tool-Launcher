namespace MCTL.Core;

/// <summary>启动器全局的「当前选中版本」，供下载页决定资源保存路径（与 PCL 一致）。</summary>
public static class AppState
{
    public static string SelectedVersionId { get; private set; } = "";
    public static string SelectedGameDir { get; private set; } = "";

    public static event Action SelectedVersionChanged;

    public static void SetSelectedVersion(string id, string gameDir)
    {
        id ??= "";
        gameDir ??= "";
        if (SelectedVersionId == id && SelectedGameDir == gameDir) return;
        SelectedVersionId = id;
        SelectedGameDir = gameDir;
        SelectedVersionChanged?.Invoke();
    }
}
