using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MCTL.Core;
using MCTL.Core.Auth;
using MCTL.Core.Mc;
using MCTL.Core.Mods;
using Microsoft.Win32;

namespace MCTL.Views;

public partial class HomeView : UserControl, IRefreshable
{
    private List<VersionEntry> _entries = new();
    private bool _running;
    private bool _initializing = true;
    private readonly List<InstalledMod> _allMods = new();
    private bool _applyingLog;
    private bool _applyingMode;
    private CancellationTokenSource _skinCts;
    private string _lastSkinKey;

    public HomeView()
    {
        InitializeComponent();
        ApplyCachedSkin();
        PlayerNameBox.Text = Settings.Current.PlayerName;
        MemorySlider.Value = Math.Clamp(Settings.Current.MemoryMb / 1024.0, 1, 32);
        FullscreenBox.IsChecked = Settings.Current.Fullscreen;
        _initializing = false;
        UpdateMemoryText();

        GameRunner.RunningChanged += OnRunningChanged;
        Logger.LineAdded += OnLogLine;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        UpdateAccountUi();
        Refresh();

        if (Settings.Current.IsLoggedIn) _ = RestoreAccountAsync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AccountService.Changed += OnAccountChanged;
        ScrollLog();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Logger.LineAdded -= OnLogLine;
        AccountService.Changed -= OnAccountChanged;
    }

    private void OnAccountChanged() => Dispatcher.BeginInvoke(() =>
    {
        UpdateAccountUi();
        Refresh();
    });

    private async Task RestoreAccountAsync()
    {
        var ok = await AccountService.TryRestoreAsync();
        _ = Dispatcher.BeginInvoke(() =>
        {
            UpdateAccountUi();
            if (!ok) StatusText.Text = "正版登录已过期，请重新登录（当前将使用离线模式）。";
        });
    }

    private void ScrollLog()
    {
        if (LogCard == null || LogCard.Visibility != Visibility.Visible) return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[^1]);
        });
    }

    private void OnLogLine(string line) => ScrollLog();


    public void Refresh()
    {
        var current = VersionList.SelectedItem as VersionEntry;
        var previousId = current?.Id;
        if (string.IsNullOrEmpty(previousId)) previousId = AppState.SelectedVersionId;
        var previousDir = !string.IsNullOrEmpty(AppState.SelectedGameDir) ? AppState.SelectedGameDir : current?.Dir;

        _entries = ScanVersions();
        ApplyVersionFilter(previousId, previousDir);

        BuildDirPanel();
        ApplyVersionModeUi();

        EmptyHint.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FolderHint.Text = "目录：" + string.Join(" · ", Settings.Current.GameDirs.Select(PathLabel.ForGameDir));
        ApplyLogVisibility(Settings.Current.ShowHomeLog);

        var java = Settings.Current.JavaAutoSelect
            ? JavaFinder.Pick(0)
            : JavaFinder.FindAll().FirstOrDefault(j =>
                  j.Path.Equals(Settings.Current.JavaPath, StringComparison.OrdinalIgnoreCase))
              ?? JavaFinder.Probe(Settings.Current.JavaPath)
              ?? JavaFinder.Pick(0);
        JavaAutoChk.IsChecked = Settings.Current.JavaAutoSelect;
        JavaText.Text = java == null
            ? "未检测到 Java，请点击下方下拉框手动选择"
            : $"{(Settings.Current.JavaAutoSelect ? "自动" : "手动")}：{java.Version}";
        JavaText.Foreground = java == null
            ? Brushes.OrangeRed
            : (Brush)FindResource("SubTextBrush");

        UpdateJavaCombo();

        PlayerNameBox.Text = Settings.Current.PlayerName;
        MemorySlider.Value = Math.Clamp(Settings.Current.MemoryMb / 1024.0, 1, 32);
        FullscreenBox.IsChecked = Settings.Current.Fullscreen;
        UpdateMemoryText();
        UpdateAccountUi();
        UpdateShowcase();
        SubGreeting.Text = _entries.Count == 0
            ? "尚未安装任何 Minecraft 版本。"
            : $"共 {_entries.Count} 个版本可选，祝游戏愉快。";
    }

    /// <summary>按显示模式扫描版本：综合 = 全部目录，单独 = 仅勾选目录。</summary>
    private List<VersionEntry> ScanVersions()
    {
        // 页面来回切换时避免反复读取磁盘与图标，2 秒内复用结果
        List<VersionEntry> all;
        if (_scanCache != null && (DateTime.Now - _scanAt).TotalSeconds < 2)
        {
            all = _scanCache;
        }
        else
        {
            all = McVersions.ScanAll();
            _scanCache = all;
            _scanAt = DateTime.Now;
        }

        if (Settings.Current.VersionDisplayMode != "selected") return all;
        var dirs = Settings.Current.VersionDisplayDirs;
        if (dirs.Count == 0) return all;
        return all.Where(entry => dirs.Contains(entry.Dir, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private DateTime _scanAt = DateTime.MinValue;
    private List<VersionEntry> _scanCache;

    private void RescanVersions()
    {
        _scanAt = DateTime.MinValue;
        var previousId = (VersionList.SelectedItem as VersionEntry)?.Id ?? AppState.SelectedVersionId;
        _entries = ScanVersions();
        ApplyVersionFilter(previousId, AppState.SelectedGameDir);
        EmptyHint.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SubGreeting.Text = _entries.Count == 0
            ? "该目录下没有可显示的版本。"
            : $"共 {_entries.Count} 个版本可选，祝游戏愉快。";
    }

    /// <summary>启动模块上的客户端名字按钮（只显示版本名）。</summary>
    private void UpdateShowcase()
    {
        if (VersionButton == null) return;
        VersionButton.Content = VersionList.SelectedItem is VersionEntry entry ? entry.Id : "选择版本";
    }

    private void ListSearch_Changed(object sender, TextChangedEventArgs e) =>
        ApplyVersionFilter((VersionList?.SelectedItem as VersionEntry)?.Id, AppState.SelectedGameDir);

    /// <summary>按版本号 / 加载器过滤游戏列表（例如 1.21、fabric、neoforge）。</summary>
    private void ApplyVersionFilter(string previousId, string previousDir)
    {
        if (VersionList == null) return;
        var keyword = (ListSearch?.Text ?? "").Trim();
        var shown = keyword.Length == 0
            ? _entries
            : _entries.Where(entry =>
                entry.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (entry.Subtitle ?? "").Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

        VersionList.Items.Clear();
        foreach (var entry in shown) VersionList.Items.Add(entry);

        VersionEntry restore = null;
        if (!string.IsNullOrEmpty(previousId))
        {
            restore = shown.FirstOrDefault(entry =>
                entry.Id == previousId &&
                (string.IsNullOrEmpty(previousDir) || entry.Dir.Equals(previousDir, StringComparison.OrdinalIgnoreCase)))
                ?? shown.FirstOrDefault(entry => entry.Id == previousId);
        }

        if (restore != null) VersionList.SelectedItem = restore;
        else if (shown.Count > 0) VersionList.SelectedIndex = 0;
        else VersionList.SelectedIndex = -1;
    }

    private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionList.SelectedItem is not VersionEntry entry) return;
        StatusText.Text = $"已选择：{entry.Id}";
        AppState.SetSelectedVersion(entry.Id, entry.Dir);
        Settings.Current.SelectedVersionId = entry.Id;
        Settings.Current.SelectedVersionDir = entry.Dir;
        Settings.Save();
        UpdateShowcase();
        if (ModPanel.Visibility == Visibility.Visible) LoadInstalledMods();
    }


    private void VersionButton_Click(object sender, RoutedEventArgs e)
    {
        if (VersionPanel.Visibility == Visibility.Visible)
        {
            CloseVersionPanel();
            return;
        }
        CloseModPanel();
        VersionPanel.Visibility = Visibility.Visible;
        UiAnim.ExpandIn(VersionPanel, 10, 200);
        UpdateRightPane();
        VersionList.Focus();
    }

    private void CloseVersionPanel_Click(object sender, RoutedEventArgs e) => CloseVersionPanel();

    private void CloseVersionPanel()
    {
        if (VersionPanel != null) VersionPanel.Visibility = Visibility.Collapsed;
        UpdateRightPane();
    }

    /// <summary>Esc 关闭浮层。</summary>
    public bool TryCloseOverlays()
    {
        var closed = false;
        if (VersionPanel.Visibility == Visibility.Visible)
        {
            CloseVersionPanel();
            closed = true;
        }
        if (ModPanel.Visibility == Visibility.Visible)
        {
            CloseModPanel();
            closed = true;
        }
        return closed;
    }

    private void BuildDirPanel()
    {
        if (DirPanel == null) return;
        DirPanel.Children.Clear();
        foreach (var dir in Settings.Current.GameDirs)
        {
            var check = new CheckBox
            {
                Content = PathLabel.ForGameDir(dir),
                ToolTip = dir,
                FontSize = 11,
                Margin = new Thickness(0, 0, 14, 4),
                IsChecked = Settings.Current.VersionDisplayDirs.Contains(dir, StringComparer.OrdinalIgnoreCase)
            };
            var captured = dir;
            check.Checked += (_, _) => ToggleDisplayDir(captured, true);
            check.Unchecked += (_, _) => ToggleDisplayDir(captured, false);
            DirPanel.Children.Add(check);
        }
    }

    private void ToggleDisplayDir(string dir, bool selected)
    {
        var list = Settings.Current.VersionDisplayDirs;
        list.RemoveAll(d => d.Equals(dir, StringComparison.OrdinalIgnoreCase));
        if (selected) list.Add(dir);
        Settings.Save();
        RescanVersions();
    }

    private void ApplyVersionModeUi()
    {
        _applyingMode = true;
        var selected = Settings.Current.VersionDisplayMode == "selected";
        ModeAll.IsChecked = !selected;
        ModeSelected.IsChecked = selected;
        DirPanel.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        _applyingMode = false;
    }

    private void VersionMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_applyingMode || ModeAll == null) return;
        Settings.Current.VersionDisplayMode = ModeSelected.IsChecked == true ? "selected" : "all";
        Settings.Save();
        ApplyVersionModeUi();
        RescanVersions();
    }

    /// <summary>新建一个 .minecraft 文件夹并注册为游戏目录。</summary>
    private void NewGameFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择要在其中创建 .minecraft 的位置" };
        if (dialog.ShowDialog() != true) return;

        var target = Path.Combine(dialog.FolderName, ".minecraft");
        try
        {
            // 一并写入 launcher_profiles.json 与各版本的隔离目录，Forge / NeoForge 安装器才能正常工作
            McInstaller.EnsureGameDirLayout(target);
        }
        catch (Exception ex)
        {
            MessageBox.Show("创建失败：" + ex.Message, "新建文件夹", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Settings.Current.AddGameFolder(target);
        Settings.Save();
        Refresh();
        StatusText.Text = "已创建并添加目录：" + target;
        Logger.Info("新建 .minecraft 目录：" + target);
    }

    /// <summary>添加已有的 .minecraft 目录（选到父目录时自动识别其中的 .minecraft）。</summary>
    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择已有的 .minecraft 游戏目录" };
        if (dialog.ShowDialog() != true) return;

        var path = dialog.FolderName;
        var nested = Path.Combine(path, ".minecraft");
        if (!Directory.Exists(Path.Combine(path, "versions")) && Directory.Exists(nested))
            path = nested;

        // 已有目录也补齐版本隔离结构（缺 launcher_profiles.json 时安装器会报错）
        try { McInstaller.EnsureGameDirLayout(path); } catch { }

        Settings.Current.AddGameFolder(path);
        Settings.Save();
        Refresh();
        StatusText.Text = "已添加目录：" + path;
        Logger.Info("已添加游戏目录：" + path);
    }

    private void OpenInstanceDir_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(InstancePaths.Ensure(InstancePaths.InstanceDir));

    private void GoDownload_Click(object sender, RoutedEventArgs e) => NavigateToDownload();

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _scanAt = DateTime.MinValue;
        Refresh();
    }


    private void ModManager_Click(object sender, RoutedEventArgs e)
    {
        if (ModPanel.Visibility == Visibility.Visible)
        {
            CloseModPanel();
            return;
        }
        CloseVersionPanel();
        ModPanel.Visibility = Visibility.Visible;
        UiAnim.ExpandIn(ModPanel, 8, 180);
        UpdateRightPane();
        LoadInstalledMods();
    }

    private void CloseModPanel_Click(object sender, RoutedEventArgs e) => CloseModPanel();

    private void CloseModPanel()
    {
        if (ModPanel == null) return;
        if (ModActionPanel != null) ModActionPanel.Visibility = Visibility.Collapsed;
        UiAnim.CollapseToCorner(ModPanel, () =>
        {
            ModPanel.Visibility = Visibility.Collapsed;
            UpdateRightPane();
        }, 18, -10, 150);
    }

    /// <summary>扫描当前版本的 mods 目录（后台线程，避免打开面板时卡顿）。</summary>
    private void LoadInstalledMods() => _ = LoadInstalledModsAsync();

    private async Task LoadInstalledModsAsync()
    {
        var versionId = AppState.SelectedVersionId;
        if (string.IsNullOrEmpty(versionId))
        {
            _allMods.Clear();
            ModList.Items.Clear();
            ModPanelTitle.Text = "模组管理 · 未选择版本";
            ModStatusText.Text = "请先选择要管理的版本。";
            UpdateModSelection();
            return;
        }

        ModStatusText.Text = "正在扫描 mods 目录…";
        var modsDir = InstancePaths.Ensure(InstancePaths.Mods);
        List<InstalledMod> scanned;
        try
        {
            scanned = await Task.Run(() => InstalledModService.Scan(modsDir));
        }
        catch (Exception ex)
        {
            Logger.Warn("扫描模组失败：" + ex.Message);
            ModStatusText.Text = "扫描失败：" + ex.Message;
            return;
        }

        if (versionId != AppState.SelectedVersionId) return;
        _allMods.Clear();
        _allMods.AddRange(scanned);
        ApplyModFilter();
        ModPanelTitle.Text = $"模组管理 · {versionId}（{_allMods.Count} 个）";
        ModStatusText.Text = modsDir;
    }

    private void ModRefresh_Click(object sender, RoutedEventArgs e) => LoadInstalledMods();

    private void ModSearch_Changed(object sender, TextChangedEventArgs e) => ApplyModFilter();

    private void ApplyModFilter()
    {
        if (ModList == null) return;
        var keyword = (ModSearchBox.Text ?? "").Trim();
        ModList.Items.Clear();
        foreach (var mod in _allMods)
        {
            if (keyword.Length > 0 &&
                !mod.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                !(mod.ModId ?? "").Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;
            ModList.Items.Add(mod);
        }
        UpdateModSelection();
    }

    private void ModList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateModSelection();


    private int _modDragAnchor = -1;
    private bool _modDragging;

    /// <summary>切换选取模式：开启后按住左键上下拖动即可连续多选（Ctrl / Shift 多选始终可用）。</summary>
    private void ModSelectMode_Changed(object sender, RoutedEventArgs e)
    {
        if (ModList == null) return;
        var on = ModSelectModeChk.IsChecked == true;
        ModList.SelectedItems.Clear();
        ModStatusText.Text = on
            ? "选取模式：按住左键上下拖动可连续选择，右键可批量操作"
            : "已退出选取模式（仍可用 Ctrl / Shift 多选）";
    }

    /// <summary>选取模式下按下左键：记住起点并接管鼠标，由拖动过程决定选中范围。</summary>
    private void ModList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ModSelectModeChk?.IsChecked != true) return;
        if (ItemsControl.ContainerFromElement(ModList, e.OriginalSource as DependencyObject) is not ListBoxItem item) return;
        if (item.DataContext is not InstalledMod) return;

        _modDragAnchor = ModList.Items.IndexOf(item.DataContext);
        ModList.SelectedItems.Clear();
        ModList.SelectedItems.Add(item.DataContext);
        _modDragging = true;
        ModList.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>拖动过程中按起点到当前项的范围实时选中。</summary>
    private void ModList_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_modDragging || e.LeftButton != MouseButtonState.Pressed) return;
        var element = ModList.InputHitTest(e.GetPosition(ModList)) as DependencyObject;
        if (ItemsControl.ContainerFromElement(ModList, element) is not ListBoxItem item) return;
        if (item.DataContext is not InstalledMod) return;

        var index = ModList.Items.IndexOf(item.DataContext);
        if (index < 0 || _modDragAnchor < 0) return;

        var from = Math.Min(_modDragAnchor, index);
        var to = Math.Max(_modDragAnchor, index);
        ModList.SelectedItems.Clear();
        for (var i = from; i <= to; i++) ModList.SelectedItems.Add(ModList.Items[i]);
    }

    /// <summary>松开左键结束框选。</summary>
    private void ModList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_modDragging) return;
        _modDragging = false;
        ModList.ReleaseMouseCapture();
    }

    /// <summary>右键时若点到的模组未被选中，先选中它，菜单操作才有对象。</summary>
    private void ModList_PreviewRightDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(ModList, e.OriginalSource as DependencyObject) is not ListBoxItem item) return;
        if (item.DataContext is not InstalledMod mod) return;
        if (!ModList.SelectedItems.Contains(mod)) ModList.SelectedItem = mod;
    }

    /// <summary>打开当前选中模组所在的文件夹。</summary>
    private void ModOpenSelectedFolder_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedMods();
        var dir = selected.Count > 0 ? Path.GetDirectoryName(selected[0].Path) : null;
        OpenFolder(string.IsNullOrEmpty(dir) ? InstancePaths.Ensure(InstancePaths.Mods) : dir);
    }

    private void UpdateModSelection()
    {
        if (ModSelectionText == null) return;
        var count = ModList?.SelectedItems.Count ?? 0;
        ModSelectionText.Text = $"已选择 {count} 个文件";
        ModActionHint.Text = $"已选择 {count} 个文件";
        ModActionPanel.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private List<InstalledMod> SelectedMods() => ModList.SelectedItems.OfType<InstalledMod>().ToList();

    private void ModClearSelection_Click(object sender, RoutedEventArgs e) => ModList.SelectedItems.Clear();

    private void ModEnable_Click(object sender, RoutedEventArgs e) => SetSelectedModsEnabled(true);

    private void ModDisable_Click(object sender, RoutedEventArgs e) => SetSelectedModsEnabled(false);

    private void SetSelectedModsEnabled(bool enabled)
    {
        var selected = SelectedMods();
        if (selected.Count == 0) return;
        foreach (var mod in selected)
        {
            try { InstalledModService.SetEnabled(mod, enabled); }
            catch (Exception ex) { Logger.Warn($"切换模组状态失败（{mod.FileName}）：{ex.Message}"); }
        }
        LoadInstalledMods();
        ModStatusText.Text = (enabled ? "已启用 " : "已禁用 ") + selected.Count + " 个模组";
    }

    private void ModDelete_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedMods();
        if (selected.Count == 0) return;
        if (MessageBox.Show($"确定删除选中的 {selected.Count} 个模组文件吗？此操作不可恢复。", "删除模组",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        foreach (var mod in selected)
        {
            try { InstalledModService.Delete(mod); }
            catch (Exception ex) { Logger.Warn($"删除模组失败（{mod.FileName}）：{ex.Message}"); }
        }
        LoadInstalledMods();
        ModStatusText.Text = "已删除 " + selected.Count + " 个模组文件";
    }

    private void ModOpenFolder_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(InstancePaths.Ensure(InstancePaths.Mods));

    private void ModInstallFromFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要安装的模组",
            Filter = "模组文件 (*.jar)|*.jar|所有文件|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true) return;

        var modsDir = InstancePaths.Ensure(InstancePaths.Mods);
        var count = 0;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                InstalledModService.InstallFromFile(file, modsDir);
                count++;
            }
            catch (Exception ex)
            {
                Logger.Warn("安装模组失败：" + ex.Message);
            }
        }
        LoadInstalledMods();
        ModStatusText.Text = $"已安装 {count} 个文件到 mods";
    }

    private void ModGoDownload_Click(object sender, RoutedEventArgs e) => NavigateToDownload();

    /// <summary>批量更新：按模组名检索双源，取最新兼容文件替换。</summary>
    private async void ModUpdate_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedMods();
        if (selected.Count == 0) return;

        var (mcVersion, loader) = McVersions.GetInstanceTag(AppState.SelectedGameDir, AppState.SelectedVersionId);
        if (string.IsNullOrEmpty(mcVersion))
        {
            MessageBox.Show("无法识别当前版本的原版号，暂时不能自动更新。", "更新模组");
            return;
        }

        var modsDir = InstancePaths.Ensure(InstancePaths.Mods);
        var updated = 0;
        foreach (var mod in selected)
        {
            try
            {
                ModStatusText.Text = $"正在检查 {mod.DisplayName} …";
                var result = await ModSearchService.SearchAsync(mod.DisplayName, "mod", mcVersion, loader, 0, 5);
                var match = result.Entries.FirstOrDefault(entry => IsSameMod(entry, mod));
                if (match == null)
                {
                    ModStatusText.Text = $"{mod.DisplayName}：两个平台都没有找到对应项目";
                    continue;
                }

                var files = await ModSearchService.GetFilesAsync(match, mcVersion, loader);
                var file = files.FirstOrDefault();
                if (file == null)
                {
                    ModStatusText.Text = $"{mod.DisplayName}：没有适配 {mcVersion} 的文件";
                    continue;
                }

                var target = Path.Combine(modsDir, file.FileName);
                ModStatusText.Text = $"正在更新 {mod.DisplayName} → {file.FileName}";
                await McApi.DownloadAsync(file.Url, target, file.Sha1, CancellationToken.None);
                if (!string.Equals(Path.GetFileName(mod.Path), file.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(mod.Path); } catch { }
                }
                updated++;
            }
            catch (Exception ex)
            {
                Logger.Warn($"更新模组失败（{mod.DisplayName}）：{ex.Message}");
                ModStatusText.Text = $"{mod.DisplayName}：更新失败（{ex.Message}）";
            }
        }

        LoadInstalledMods();
        ModStatusText.Text = $"已更新 {updated} / {selected.Count} 个模组";
    }

    private static bool IsSameMod(ModEntry entry, InstalledMod mod)
    {
        static string Norm(string text) =>
            new string((text ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        var entryName = Norm(entry.EnglishName);
        var modName = Norm(mod.Name);
        if (entryName.Length > 2 && entryName == modName) return true;

        var slug = Norm(entry.Slug);
        var modId = Norm(mod.ModId);
        return modId.Length > 2 && (slug == modId || entryName == modId);
    }


    private void QuickMods_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(InstancePaths.Ensure(InstancePaths.Mods));

    private void QuickResourcePacks_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(InstancePaths.Ensure(InstancePaths.ResourcePacks));

    private void QuickShaderPacks_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(InstancePaths.Ensure(InstancePaths.ShaderPacks));

    private void QuickSaves_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(InstancePaths.Ensure(InstancePaths.Saves));

    private void NavigateToDownload()
    {
        CloseModPanel();
        CloseVersionPanel();
        if (Window.GetWindow(this) is MainWindow window) window.NavigateTo("NavDownload");
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "打开失败");
        }
    }


    private void ApplyLogVisibility(bool show)
    {
        if (LogCard == null || LogToggle == null) return;
        _applyingLog = true;
        LogToggle.IsChecked = show;
        _applyingLog = false;
        UpdateRightPane();
        if (show) ScrollLog();
    }

    private void LogToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_applyingLog) return;
        var show = LogToggle.IsChecked == true;
        Settings.Current.ShowHomeLog = show;
        Settings.Save();
        ApplyLogVisibility(show);
    }


    private void MenuImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入整合包",
            Filter = "整合包 (*.mrpack;*.zip)|*.mrpack;*.zip"
        };
        if (dialog.ShowDialog() != true) return;

        if (!ModpackService.LooksLikeModpack(dialog.FileName))
        {
            MessageBox.Show("无法识别该压缩包格式。\n整合包需包含 modrinth.index.json 或 manifest.json。",
                "导入整合包", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var window = new ModpackImportWindow(dialog.FileName, _ => Refresh())
        {
            Owner = Window.GetWindow(this)
        };
        window.ShowDialog();
    }

    private void MenuExport_Click(object sender, RoutedEventArgs e)
    {
        if (VersionList.SelectedItem is not VersionEntry entry)
        {
            MessageBox.Show("请先选择一个版本。", "导出整合包");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出整合包",
            Filter = "Modrinth 整合包 (*.mrpack)|*.mrpack",
            FileName = entry.Id + ".mrpack"
        };
        if (dialog.ShowDialog() != true) return;

        _ = ExportAsync(entry, dialog.FileName);
    }

    private async Task ExportAsync(VersionEntry entry, string path)
    {
        StatusText.Text = "正在导出整合包…";
        try
        {
            var log = new Progress<string>(s => StatusText.Text = s);
            await ModpackService.ExportAsync(entry.Id, entry.Dir, path, log, CancellationToken.None);
            StatusText.Text = "整合包已导出：" + path;
            MessageBox.Show("导出完成：\n" + path, "导出整合包");
        }
        catch (Exception ex)
        {
            StatusText.Text = "导出失败：" + ex.Message;
            MessageBox.Show("导出失败：" + ex.Message, "导出整合包", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }


    private void UpdateMemoryText()
    {
        if (MemoryText == null) return;
        var gb = (int)Math.Round(MemorySlider.Value);
        MemoryText.Text = gb + " G";
        if (_initializing) return;
        Settings.Current.MemoryMb = gb * 1024;
    }

    private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateMemoryText();

    private void JavaAuto_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        Settings.Current.JavaAutoSelect = JavaAutoChk.IsChecked == true;
        Settings.Save();
        Refresh();
    }

    private List<JavaInfo> _homeJavas = new();
    private bool _updatingJavaCombo;

    private void UpdateJavaCombo()
    {
        if (JavaCombo == null) return;
        _updatingJavaCombo = true;
        _homeJavas = JavaFinder.FindAll();
        JavaCombo.Items.Clear();
        JavaCombo.Items.Add("自动选择 Java（推荐）");
        foreach (var java in _homeJavas)
            JavaCombo.Items.Add($"Java {java.Major} · {java.Path}");
        var index = 0;
        if (!Settings.Current.JavaAutoSelect && !string.IsNullOrEmpty(Settings.Current.JavaPath))
        {
            var found = _homeJavas.FindIndex(j =>
                j.Path.Equals(Settings.Current.JavaPath, StringComparison.OrdinalIgnoreCase));
            if (found >= 0) index = found + 1;
        }
        JavaCombo.SelectedIndex = index;
        _updatingJavaCombo = false;
    }

    private void JavaCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _updatingJavaCombo || JavaCombo.SelectedIndex < 0) return;
        if (JavaCombo.SelectedIndex == 0)
        {
            Settings.Current.JavaAutoSelect = true;
        }
        else
        {
            var index = JavaCombo.SelectedIndex - 1;
            if (index >= _homeJavas.Count) return;
            Settings.Current.JavaAutoSelect = false;
            Settings.Current.JavaPath = _homeJavas[index].Path;
        }
        Settings.Save();
        var java = Settings.Current.JavaAutoSelect ? JavaFinder.Pick(0) : JavaFinder.Probe(Settings.Current.JavaPath);
        JavaText.Text = java == null ? "未检测到 Java" :
            $"{(Settings.Current.JavaAutoSelect ? "自动" : "手动")}：{java.Version}";
    }


    private void UpdateAccountUi()
    {
        var settings = Settings.Current;
        var loggedIn = settings.IsLoggedIn;

        AccountName.Text = loggedIn ? settings.MsName : "未登录";
        AccountSub.Text = loggedIn ? "正版" : "离线模式";
        AccountBtn.Content = "微软登录";
        AccountBtn.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        LogoutMenu.IsEnabled = loggedIn;

        PlayerNameLabel.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        PlayerNameBox.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        PlayerNameBox.IsEnabled = true;
        if (loggedIn) PlayerNameBox.Text = settings.MsName;

        if (SkinAuthorText != null)
            SkinAuthorText.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;

        _ = LoadSkinsAsync();
    }

    /// <summary>启动时先用本地缓存（或内置贴图）建模，首帧就是正确皮肤，不再先显示默认皮肤。</summary>
    private void ApplyCachedSkin()
    {
        var loggedIn = Settings.Current.IsLoggedIn;
        var key = loggedIn ? Settings.Current.MsUuid : "default";
        var cached = SkinService.LoadCached(key) ?? (loggedIn ? null : SkinService.BundledDefault);
        if (cached == null) return;

        AccountHead.SetSkin(cached.Texture, cached.Slim);
        BodySkinView.SetSkin(cached.Texture, cached.Slim);
        SkinCardMode.Text = cached.Slim ? "纤细 Slim" : "经典 Classic";
    }

    /// <summary>加载正版皮肤纹理，同时更新 3D 头像与右侧 3D 小人。</summary>
    private async Task LoadSkinsAsync()
    {
        var settings = Settings.Current;
        var key = settings.IsLoggedIn ? settings.MsUuid + "|" + settings.MsName : "";
        if (key == _lastSkinKey) return;
        _lastSkinKey = key;

        _skinCts?.Cancel();
        var cts = new CancellationTokenSource();
        _skinCts = cts;

        BitmapSource texture = null;
        var slim = false;
        if (settings.IsLoggedIn)
        {
            try
            {
                var data = await SkinService.LoadTextureForAccountAsync(
                    settings.MsUuid, settings.MsUuid, settings.MsName, cts.Token);
                if (data != null)
                {
                    texture = data.Texture;
                    slim = data.Slim;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.Warn("加载 3D 皮肤失败：" + ex.Message);
            }
        }
        else
        {
            // 未登录：使用启动器内置的默认渲染皮肤
            try
            {
                var fallback = await SkinService.LoadDefaultSkinAsync(cts.Token);
                if (fallback != null)
                {
                    texture = fallback.Texture;
                    slim = fallback.Slim;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.Warn("加载默认皮肤失败：" + ex.Message);
            }
        }

        if (cts.IsCancellationRequested) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (cts.IsCancellationRequested) return;
            AccountHead.SetSkin(texture, slim);
            BodySkinView.SetSkin(texture, slim);
            SkinCardTitle.Text = settings.IsLoggedIn ? settings.MsName : "皮肤预览";
            SkinCardMode.Text = slim ? "纤细 Slim" : "经典 Classic";
            // 作者信息只给未登录玩家看，登录正版后自动隐藏
            SkinAuthorText.Visibility = settings.IsLoggedIn ? Visibility.Collapsed : Visibility.Visible;
            UpdateRightPane();
        });
    }

    /// <summary>右侧区域：3D 皮肤预览 / 运行日志，浮层打开时让位。</summary>
    private void UpdateRightPane()
    {
        if (SkinCard == null || LogCard == null) return;

        var panelOpen = VersionPanel.Visibility == Visibility.Visible ||
                        ModPanel.Visibility == Visibility.Visible;
        var logOn = LogToggle.IsChecked == true;

        LogCard.Visibility = logOn && !panelOpen ? Visibility.Visible : Visibility.Collapsed;
        var showSkin = Settings.Current.ShowSkin3D && !logOn && !panelOpen;
        SkinCard.Visibility = showSkin ? Visibility.Visible : Visibility.Collapsed;

        // 面板挡住 / 隐藏时停止行走动画，避免无谓的渲染开销
        BodySkinView.WalkAnimation = showSkin;
    }

    /// <summary>未登录时点击「微软登录」。</summary>
    private void Account_Click(object sender, RoutedEventArgs e)
    {
        if (Settings.Current.IsLoggedIn) return;

        var window = new LoginWindow { Owner = Window.GetWindow(this) };
        var ok = window.ShowDialog();
        UpdateAccountUi();
        if (ok == true && window.Account != null)
        {
            StatusText.Text = "已登录：" + window.Account.Name;
            PlayerNameBox.Text = window.Account.Name;
        }
    }

    /// <summary>右键皮肤预览 → 退出登录。</summary>
    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        if (!Settings.Current.IsLoggedIn) return;
        if (MessageBox.Show($"确定退出正版账号「{Settings.Current.MsName}」吗？", "退出登录",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        AccountService.Logout();
        UpdateAccountUi();
        PlayerNameBox.Text = Settings.Current.PlayerName;
        StatusText.Text = "已退出正版账号，当前为离线模式。";
    }


    private async void Launch_Click(object sender, RoutedEventArgs e) => await LaunchSelectedAsync();

    private async Task LaunchSelectedAsync()
    {
        if (_running) return;
        if (VersionList.SelectedItem is not VersionEntry entry)
        {
            MessageBox.Show("请先安装并选择一个 Minecraft 版本。", "提示");
            return;
        }

        Settings.Current.PlayerName = string.IsNullOrWhiteSpace(PlayerNameBox.Text) ? "Steve" : PlayerNameBox.Text.Trim();
        Settings.Current.MemoryMb = (int)Math.Round(MemorySlider.Value) * 1024;
        Settings.Current.Fullscreen = FullscreenBox.IsChecked == true;
        Settings.Save();

        // 自动选择 Java：没有兼容该版本的运行时 → 提示并可选自动下载
        var requiredJava = JavaFinder.RequiredMajor(entry.Dir, entry.Id);
        if (Settings.Current.JavaAutoSelect && JavaFinder.PickCompatible(requiredJava) == null)
        {
            var confirm = MessageBox.Show(
                $"未检测到适合该游戏版本的 Java（需要 Java {requiredJava}），是否自动下载并安装？\n\n" +
                "安装来源：Adoptium Temurin JRE（Windows x64）\n" +
                $"安装位置：{JavaInstaller.Root}",
                "缺少 Java 运行时", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                StatusText.Text = $"已取消启动：缺少 Java {requiredJava}。可在设置 → Java 运行时中手动指定路径。";
                return;
            }

            var dialog = new JavaDownloadWindow(requiredJava) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true || dialog.Installed == null)
            {
                StatusText.Text = "Java 安装未完成，已取消启动。";
                return;
            }

            Settings.Current.JavaPath = dialog.Installed.Path;
            Settings.Current.JavaAutoSelect = false;
            Settings.Save();
            JavaAutoChk.IsChecked = false;
            UpdateJavaCombo();
            JavaText.Text = $"手动：{dialog.Installed.Version}";
            StatusText.Text = $"已安装 Java {dialog.Installed.Major} 并选中，正在启动…";
        }

        var options = new LaunchOptions
        {
            VersionId = entry.Id,
            GameDir = entry.Dir,
            PlayerName = Settings.Current.PlayerName,
            MemoryMb = Settings.Current.MemoryMb,
            JavaPath = Settings.Current.JavaPath,
            Fullscreen = Settings.Current.Fullscreen,
            Width = Settings.Current.WindowWidth,
            Height = Settings.Current.WindowHeight
        };

        if (Settings.Current.IsLoggedIn)
        {
            try
            {
                var account = await AccountService.EnsureValidAsync();
                if (account != null)
                {
                    options.Online = true;
                    options.AccessToken = account.AccessToken;
                    options.Uuid = account.Uuid;
                    options.PlayerName = account.Name;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("正版登录续期失败，改用离线模式：" + ex.Message);
                StatusText.Text = "登录已过期，本次将以离线模式启动。";
            }
        }

        StatusText.Text = "正在启动 " + entry.Id + " …";
        await GameRunner.RunAsync(options);
        StatusText.Text = "游戏已退出。";
        UpdateShowcase();
    }

    private void OnRunningChanged(bool running)
    {
        _running = running;
        Dispatcher.BeginInvoke(() =>
        {
            LaunchButton.IsEnabled = !running;
            LaunchButton.Content = running ? "游戏运行中…" : "启 动 游 戏";
            KillButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void Kill_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("确定要结束当前游戏进程吗？", "结束游戏",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        GameRunner.StopGame();
        StatusText.Text = "已请求结束游戏进程。";
    }

    private void VersionList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VersionList.SelectedItem is VersionEntry)
        {
            CloseVersionPanel();
            _ = LaunchSelectedAsync();
        }
    }

    private void VersionList_PreviewRightDown(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(VersionList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item != null) item.IsSelected = true;
    }


    private void MenuLaunch_Click(object sender, RoutedEventArgs e) => _ = LaunchSelectedAsync();

    private void MenuIcon_Click(object sender, RoutedEventArgs e)
    {
        if (VersionList.SelectedItem is not VersionEntry entry) return;
        var dialog = new OpenFileDialog
        {
            Title = "选择版本图标",
            Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            McVersions.SetCustomIcon(entry.Dir, entry.Id, dialog.FileName);
            Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "设置图标失败");
        }
    }

    private void MenuResetIcon_Click(object sender, RoutedEventArgs e)
    {
        if (VersionList.SelectedItem is not VersionEntry entry) return;
        McVersions.ClearCustomIcon(entry.Dir, entry.Id);
        Refresh();
    }

    private void MenuRename_Click(object sender, RoutedEventArgs e)
    {
        if (VersionList.SelectedItem is not VersionEntry entry) return;
        var window = new PromptWindow("重命名版本", "将同时修改版本文件夹与 JSON 中的 id，不会影响存档。", entry.Id)
        {
            Owner = Window.GetWindow(this)
        };
        if (window.ShowDialog() != true) return;
        try
        {
            McVersions.Rename(entry.Dir, entry.Id, window.Value);
            Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "重命名失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (VersionList.SelectedItem is not VersionEntry entry) return;
        if (MessageBox.Show($"确定删除版本「{entry.Id}」吗？\n该操作会删除整个版本目录，且不可恢复。", "删除版本",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            McVersions.Delete(entry.Dir, entry.Id);
            Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "删除失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MenuOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (VersionList.SelectedItem is not VersionEntry entry) return;
        OpenFolder(Path.Combine(entry.Dir, "versions", entry.Id));
    }
}
