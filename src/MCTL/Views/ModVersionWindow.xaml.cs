using System.Windows;
using System.Windows.Input;

namespace MCTL.Views;

/// <summary>Modrinth 依赖项（仅保留 required 类型）。</summary>
public record ModDependency(string ProjectId, string VersionId);

/// <summary>Modrinth 中的一个可下载文件（某个项目版本的主文件）。</summary>
public class ModFile
{
    public string Name { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Url { get; init; } = "";
    public string GameVersions { get; init; } = "";
    public string Loaders { get; init; } = "";
    public string Date { get; init; } = "";
    public string Size { get; init; } = "";
    /// <summary>文件 SHA1（用于下载校验，可为空）。</summary>
    public string Sha1 { get; init; } = "";
    /// <summary>该文件声明的强制前置项目（跨平台）。</summary>
    public List<(MCTL.Core.Mods.ModSource Source, string ProjectId)> RequiredProjects { get; init; } = new();
    /// <summary>强制前置依赖，下载后会自动补齐。</summary>
    public List<ModDependency> Required { get; init; } = new();

    /// <summary>列表第二行：加载器 + 支持的版本。</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Loaders)) parts.Add(Loaders);
            if (!string.IsNullOrWhiteSpace(GameVersions)) parts.Add(GameVersions);
            if (!string.IsNullOrWhiteSpace(FileName)) parts.Add(FileName);
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>展开选择具体的模组 / 插件版本。</summary>
public partial class ModVersionWindow : Window
{
    public ModFile SelectedFile { get; private set; }

    public ModVersionWindow(string projectName, string gameVersion, string loader,
        List<ModFile> files, string hint = null)
    {
        InitializeComponent();
        TitleText.Text = "选择版本 · " + projectName;

        var scope = new List<string>();
        if (!string.IsNullOrEmpty(gameVersion)) scope.Add("游戏版本 " + gameVersion);
        if (!string.IsNullOrEmpty(loader)) scope.Add("加载器 " + loader);
        HintText.Text = hint ?? (scope.Count == 0
            ? $"共 {files.Count} 个文件，选择要下载的版本"
            : $"已按 {string.Join(" · ", scope)} 筛选，共 {files.Count} 个文件；双击可直接下载");

        FileList.ItemsSource = files;
        if (files.Count > 0) FileList.SelectedIndex = 0;
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Confirm();

    private void Ok_Click(object sender, RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (FileList.SelectedItem is not ModFile file) return;
        SelectedFile = file;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
