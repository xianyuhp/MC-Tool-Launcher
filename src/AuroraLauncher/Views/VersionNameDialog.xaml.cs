using System.IO;
using System.Windows;
using System.Windows.Input;
using AuroraLauncher.Core;
using AuroraLauncher.Core.Mc;
using Microsoft.Win32;

namespace AuroraLauncher.Views;

/// <summary>
/// 下载前统一弹窗：选择保存位置并自定义名称。
/// 游戏版本写入 &lt;目录&gt;/versions/&lt;名称&gt;，服务端核心写入 &lt;目录&gt;/&lt;名称&gt;。
/// </summary>
public partial class VersionNameDialog : Window
{
    private readonly List<string> _dirs;
    private readonly bool _serverMode;

    public string SelectedDir { get; private set; }
    public string VersionName { get; private set; } = "";

    public VersionNameDialog(string title, string suggestedName, string preferredDir, bool serverMode = false)
    {
        InitializeComponent();
        _serverMode = serverMode;

        TitleText.Text = (serverMode ? "下载 " : "安装 ") + title;
        if (serverMode)
        {
            LocationLabel.Text = "保存位置（服务端根目录）";
            NameLabel.Text = "实例名称（每个服务端单独一个文件夹，仍保存在上面选的目录里）";
            OkButton.Content = "开始下载";
        }

        _dirs = new List<string>(serverMode ? Settings.Current.ServerDirOptions : Settings.Current.GameDirs);
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
        foreach (var dir in _dirs)
            DirCombo.Items.Add(_serverMode ? dir : PathLabel.ForGameDir(dir));
        if (DirCombo.SelectedIndex < 0 && _dirs.Count > 0) DirCombo.SelectedIndex = 0;
    }

    private static string SanitizeDir(string dir) => string.IsNullOrWhiteSpace(dir) ? "" : dir.TrimEnd('\\', '/');

    private string CurrentDir =>
        DirCombo.SelectedIndex >= 0 && DirCombo.SelectedIndex < _dirs.Count ? _dirs[DirCombo.SelectedIndex] : "";

    /// <summary>最终写入的目录：游戏版本带 versions 层，服务端直接是 根目录/实例名。</summary>
    private string TargetPath(string dir, string name) =>
        _serverMode ? Path.Combine(dir, name) : Path.Combine(dir, "versions", name);

    private void UpdateHint()
    {
        var name = McInstaller.SanitizeVersionName(NameBox.Text);
        var dir = CurrentDir;
        PathHint.Text = name.Length == 0 || dir.Length == 0 ? "" : "将写入：" + TargetPath(dir, name);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = _serverMode ? "选择服务端保存根目录" : "选择 .minecraft 游戏目录"
        };
        if (dialog.ShowDialog() != true) return;

        var path = dialog.FolderName;
        if (!_serverMode)
        {
            var nested = Path.Combine(path, ".minecraft");
            if (!Directory.Exists(Path.Combine(path, "versions")) && Directory.Exists(nested)) path = nested;
        }

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
            ErrorText.Text = "请选择保存位置。";
            return;
        }

        var name = McInstaller.SanitizeVersionName(NameBox.Text);
        if (name.Length == 0)
        {
            ErrorText.Text = "名称不能为空，也不能只包含非法字符。";
            return;
        }

        if (_serverMode)
        {
            // 记住服务端根目录，下次默认用它
            Settings.Current.AddServerRoot(dir);
            Settings.Save();
            try
            {
                Directory.CreateDirectory(dir);
                Directory.CreateDirectory(TargetPath(dir, name));
            }
            catch (Exception ex)
            {
                ErrorText.Text = "无法创建目录：" + ex.Message;
                return;
            }
        }
        else
        {
            // 记住这个目录，下次默认用它；顺便补齐 launcher_profiles.json 与版本隔离目录
            Settings.Current.AddGameFolder(dir);
            Settings.Save();
            try { McInstaller.EnsureGameDirLayout(dir); } catch { }
        }

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
