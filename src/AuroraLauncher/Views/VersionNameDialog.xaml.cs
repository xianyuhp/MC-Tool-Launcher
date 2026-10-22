using System.IO;
using System.Windows;
using System.Windows.Input;
using AuroraLauncher.Core;
using AuroraLauncher.Core.Mc;
using Microsoft.Win32;

namespace AuroraLauncher.Views;

/// <summary>安装游戏版本前：选择游戏目录并给版本起名（类似保存模组时改文件名）。</summary>
public partial class VersionNameDialog : Window
{
    private readonly List<string> _dirs;

    public string SelectedDir { get; private set; }
    public string VersionName { get; private set; } = "";

    public VersionNameDialog(string title, string suggestedName, string preferredDir)
    {
        InitializeComponent();
        TitleText.Text = "安装 " + title;

        _dirs = new List<string>(Settings.Current.GameDirs);
        var preferred = SanitizeDir(preferredDir);
        if (preferred.Length > 0 && !_dirs.Contains(preferred, StringComparer.OrdinalIgnoreCase))
            _dirs.Insert(0, preferred);

        dirsRefresh();
        NameBox.Text = suggestedName;
        var index = _dirs.FindIndex(d => preferred.Length > 0 &&
                                         d.Equals(preferred, StringComparison.OrdinalIgnoreCase));
        DirCombo.SelectedIndex = index >= 0 ? index : 0;

        UpdateHint();
        NameBox.TextChanged += (_, _) => UpdateHint();
        DirCombo.SelectionChanged += (_, _) => UpdateHint();
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void dirsRefresh()
    {
        DirCombo.Items.Clear();
        foreach (var dir in _dirs) DirCombo.Items.Add(PathLabel.ForGameDir(dir));
        if (DirCombo.SelectedIndex < 0 && _dirs.Count > 0) DirCombo.SelectedIndex = 0;
    }

    private static string SanitizeDir(string dir) => string.IsNullOrWhiteSpace(dir) ? "" : dir.TrimEnd('\\', '/');

    private string CurrentDir =>
        DirCombo.SelectedIndex >= 0 && DirCombo.SelectedIndex < _dirs.Count ? _dirs[DirCombo.SelectedIndex] : "";

    private void UpdateHint()
    {
        var name = McInstaller.SanitizeVersionName(NameBox.Text);
        var dir = CurrentDir;
        PathHint.Text = name.Length == 0 || dir.Length == 0
            ? ""
            : "将写入：" + Path.Combine(dir, "versions", name);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择 .minecraft 游戏目录" };
        if (dialog.ShowDialog() != true) return;

        var path = dialog.FolderName;
        var nested = Path.Combine(path, ".minecraft");
        if (!Directory.Exists(Path.Combine(path, "versions")) && Directory.Exists(nested)) path = nested;

        var existing = _dirs.FindIndex(d => d.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (existing < 0)
        {
            _dirs.Add(path);
            dirsRefresh();
            existing = _dirs.Count - 1;
        }
        DirCombo.SelectedIndex = existing;
        UpdateHint();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var dir = CurrentDir;
        if (dir.Length == 0)
        {
            ErrorText.Text = "请选择游戏目录。";
            return;
        }

        var name = McInstaller.SanitizeVersionName(NameBox.Text);
        if (name.Length == 0)
        {
            ErrorText.Text = "版本名称不能为空，也不能只包含非法字符。";
            return;
        }

        // 记住这个目录，下次默认用它；顺便补齐 launcher_profiles.json 与版本隔离目录
        Settings.Current.AddGameFolder(dir);
        Settings.Save();
        try { McInstaller.EnsureGameDirLayout(dir); } catch { }

        SelectedDir = dir;
        VersionName = name;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
