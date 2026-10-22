using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AuroraLauncher.Core;
using AuroraLauncher.Core.Mc;
using AuroraLauncher.Core.Mods;
using AuroraLauncher.Core.Net;

namespace AuroraLauncher.Views;

public class ModItem : INotifyPropertyChanged
{
    public string ProjectId { get; init; } = "";
    /// <summary>来源平台（Modrinth / CurseForge）。</summary>
    public AuroraLauncher.Core.Mods.ModSource Source { get; init; }
    public string Slug { get; init; } = "";
    public string AlternateId { get; init; } = "";
    public AuroraLauncher.Core.Mods.ModSource? AlternateSource { get; init; }

    private string _title = "";
    /// <summary>显示标题：中文名 | 英文名。</summary>
    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        }
    }
    /// <summary>英文原名。</summary>
    public string OriginalTitle { get; init; } = "";
    /// <summary>类型标签，例如 Mod / 整合包 / 资源包 / 光影 / 插件。</summary>
    public string Tag { get; set; } = "";

    private string _description = "";
    public string Description
    {
        get => _description;
        set
        {
            _description = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
        }
    }
    /// <summary>原文描述（用于悬停提示）。</summary>
    public string OriginalDescription { get; set; } = "";
    public string Downloads { get; init; } = "";
    public string IconUrl { get; init; } = "";

    private ImageSource _icon;
    public ImageSource Icon
    {
        get => _icon;
        set { _icon = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon))); }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public override string ToString() => Title;
}

public partial class DownloadView : UserControl, IRefreshable
{
    private List<McVersion> _allVersions = new();
    private List<McVersion> _shownVersions = new();
    private readonly List<ModItem> _mods = new();
    private CancellationTokenSource _cts;
    private string _downloadTargetDir = "";
    private bool _busy;
    private string _installDir;

    private DownloadContext _context = new("下载中心");
    private int _page;                      // 从 0 开始
    /// <summary>每页展示数量：默认 15，可在设置中调到 25。</summary>
    private static int PageSize => Math.Clamp(Settings.Current.ModPageSize, 5, 25);
    private bool _popularMode = true;
    private string _searchText = "";
    private string _category;
    private string _sortIndex = "downloads";
    private int _modRequest;
    private string _modGameVersion = "";
    private string _modLoader = "";
    /// <summary>左侧分类：mod / modpack / vanilla / shader / resourcepack / texturepack。</summary>
    private string _navKind = "mod";
    private bool _instanceFilterApplied;
    private bool _suppressModFilter;
    private static readonly HttpClient IconClient = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly HttpClient TranslateClient = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly Dictionary<string, string> TranslationCache = new();

    private static readonly (string Value, string Label)[] LoaderOptions =
    {
        ("", "全部加载器"), ("fabric", "Fabric"), ("forge", "Forge"),
        ("neoforge", "NeoForge"), ("quilt", "Quilt")
    };

    private static readonly Dictionary<string, (string Tag, string Label)[]> Categories = new()
    {
        ["mod"] = new[]
        {
            ("optimization", "优化"), ("adventure", "冒险"), ("technology", "科技"), ("magic", "魔法"),
            ("decoration", "装饰"), ("utility", "实用"), ("worldgen", "世界生成"), ("equipment", "装备"),
            ("food", "食物"), ("transportation", "运输")
        },
        ["modpack"] = new[]
        {
            ("adventure", "冒险"), ("challenge", "挑战"), ("technology", "科技"), ("magic", "魔法"),
            ("skyblock", "空岛"), ("optimization", "优化"), ("quests", "任务"), ("multiplayer", "多人")
        },
        ["resourcepack"] = new[]
        {
            ("16x", "16x"), ("32x", "32x"), ("64x", "64x"), ("128x", "128x"), ("256x", "256x"),
            ("realistic", "写实"), ("simplistic", "简约"), ("cartoon", "卡通"), ("medieval", "中世纪")
        },
        ["shader"] = new[]
        {
            ("realistic", "写实"), ("semi-realistic", "半写实"), ("cartoon", "卡通"),
            ("fantasy", "幻想"), ("vanilla-like", "原版风")
        },
        ["texturepack"] = new[]
        {
            ("16x", "16x"), ("32x", "32x"), ("64x", "64x"), ("128x", "128x"), ("256x", "256x"),
            ("512x", "512x"), ("realistic", "写实"), ("simplistic", "简约"), ("themed", "主题")
        }
    };

    public DownloadView()
    {
        InitializeComponent();
        VersionSearchHint.Visibility = Visibility.Visible;
        // 主页切换版本后，下载保存位置同步变化（与 PCL 一致：资源跟随所选版本）
        AppState.SelectedVersionChanged += OnSelectedVersionChanged;
        _context.PropertyChanged += Context_PropertyChanged;
        _context.Log.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            InstallLog.AppendText(_context.Log.Count > 0 ? _context.Log[^1] + Environment.NewLine : "");
            InstallLog.ScrollToEnd();
        });
    }

    public void Refresh()
    {
        BuildFolderChips();
        BuildSortChips();
        BuildCategoryChips();
        BuildModFilterCombos();
        ApplyInstanceFilter();
        if (_allVersions.Count == 0) _ = LoadVersionsAsync();
        if (ModPanel.Visibility == Visibility.Visible && _mods.Count == 0) _ = LoadModsAsync(true);
    }

    // ---------------- 左侧分类 ----------------

    /// <summary>左侧分类切换：原版显示安装面板，其余显示 Modrinth 内容。</summary>
    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || VanillaPanel == null || ModPanel == null) return;
        _navKind = ((RadioButton)sender).Name switch
        {
            "NavModpack" => "modpack",
            "NavVanilla" => "vanilla",
            "NavShader" => "shader",
            "NavResourcePack" => "resourcepack",
            "NavTexturePack" => "texturepack",
            _ => "mod"
        };

        var vanilla = _navKind == "vanilla";
        VanillaPanel.Visibility = vanilla ? Visibility.Visible : Visibility.Collapsed;
        ModPanel.Visibility = vanilla ? Visibility.Collapsed : Visibility.Visible;
        HeaderHint.Text = vanilla
            ? "原版 / Fabric / Forge / NeoForge 安装，支持镜像加速与多目录。"
            : $"{TypeLabel(_navKind)}：来自 Modrinth 的热门与搜索，默认按当前客户端版本筛选。";
        if (vanilla)
        {
            if (_allVersions.Count == 0) _ = LoadVersionsAsync();
            return;
        }

        ModLoaderCombo.IsEnabled = _navKind == "mod";
        _page = 0;
        BuildCategoryChips();
        ApplyInstanceFilter();
        UpdateModFilterHint();
        _ = LoadModsAsync(true);
    }

    private void BuildModFilterCombos()
    {
        if (ModLoaderCombo == null) return;
        if (ModLoaderCombo.Items.Count == 0)
        {
            _suppressModFilter = true;
            foreach (var (_, label) in LoaderOptions) ModLoaderCombo.Items.Add(label);
            ModLoaderCombo.SelectedIndex = 0;
            _suppressModFilter = false;
        }
        UpdateModFilterHint();
    }

    /// <summary>用已获取的版本清单填充「游戏版本」筛选下拉框。</summary>
    private void FillModVersionCombo()
    {
        if (ModGameVersionCombo == null) return;
        _suppressModFilter = true;
        ModGameVersionCombo.Items.Clear();
        ModGameVersionCombo.Items.Add("全部版本");
        foreach (var v in _allVersions.Where(v => v.Type is "release" or "snapshot").Take(80))
            ModGameVersionCombo.Items.Add(v.Id);

        // 保留用户当前选择的版本
        var index = string.IsNullOrEmpty(_modGameVersion) ? 0 : ModGameVersionCombo.Items.IndexOf(_modGameVersion);
        ModGameVersionCombo.SelectedIndex = index >= 0 ? index : 0;
        _suppressModFilter = false;
        UpdateModFilterHint();
    }

    private void UpdateModFilterHint()
    {
        if (ModFilterHint == null) return;
        var instance = InstanceTagText();
        if (_navKind == "modpack")
        {
            ModFilterHint.Text = instance.Length > 0
                ? $"{instance} · 下载后直接解析并安装进游戏列表"
                : "下载后直接解析并安装进游戏列表";
            return;
        }
        if (_navKind != "mod")
        {
            var save = DescribeTargetDir();
            ModFilterHint.Text = instance.Length > 0
                ? $"{instance} · 保存到：{save}"
                : $"保存到：{save}";
            return;
        }
        var parts = new List<string>();
        if (_modGameVersion.Length > 0) parts.Add(_modGameVersion);
        if (_modLoader.Length > 0) parts.Add(_modLoader);
        var filter = parts.Count == 0 ? "未筛选" : "筛选：" + string.Join(" · ", parts);
        ModFilterHint.Text = instance.Length > 0
            ? $"{instance} · {filter} · 保存到：{DescribeTargetDir()}"
            : $"{filter} · 保存到：{DescribeTargetDir()}";
    }

    /// <summary>主页所选客户端的描述，例如「当前客户端：26.3 · fabric」。</summary>
    private string InstanceTagText()
    {
        if (string.IsNullOrEmpty(AppState.SelectedVersionId)) return "";
        var (mc, loader) = McVersions.GetInstanceTag(AppState.SelectedGameDir, AppState.SelectedVersionId);
        var detail = string.Join(" · ", new[] { mc, loader }.Where(x => !string.IsNullOrEmpty(x)));
        return detail.Length == 0 ? "" : "当前客户端：" + detail;
    }

    /// <summary>主页切换版本后，重新按新客户端筛选并刷新列表。</summary>
    private void OnSelectedVersionChanged() => Dispatcher.BeginInvoke(() =>
    {
        _instanceFilterApplied = false;
        ApplyInstanceFilter();
        UpdateModFilterHint();
        if (IsLoaded && ModPanel.Visibility == Visibility.Visible) _ = LoadModsAsync(true);
    });

    /// <summary>优先使用主页所选客户端（原版版本 + 加载器）作为筛选条件，与 PCL 一致。</summary>
    private void ApplyInstanceFilter()
    {
        if (_instanceFilterApplied) return;
        if (string.IsNullOrEmpty(AppState.SelectedVersionId)) return;
        var (mc, loader) = McVersions.GetInstanceTag(AppState.SelectedGameDir, AppState.SelectedVersionId);
        if (string.IsNullOrEmpty(mc)) return;

        _instanceFilterApplied = true;
        SelectModGameVersion(mc);
        if (loader.Length > 0)
        {
            var index = Array.FindIndex(LoaderOptions, o => o.Value == loader);
            if (index > 0)
            {
                _suppressModFilter = true;
                ModLoaderCombo.SelectedIndex = index;
                _suppressModFilter = false;
                _modLoader = loader;
            }
        }
    }

    /// <summary>下载保存目录（相对游戏根目录，便于在界面上显示）。</summary>
    private string DescribeTargetDir()
    {
        var root = ResolveGameRoot();
        var dir = ResolveTargetDir(CurrentProjectType(), _modGameVersion);
        if (dir.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var relative = dir[root.Length..].TrimStart('\\', '/');
            return relative.Length == 0 ? "." : relative;
        }
        return dir;
    }

    private string ResolveGameRoot() =>
        !string.IsNullOrEmpty(AppState.SelectedGameDir)
            ? AppState.SelectedGameDir
            : _installDir ?? Settings.Current.EffectiveGameDir;

    private void ModFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressModFilter || ModGameVersionCombo == null || !IsLoaded) return;
        _modGameVersion = ModGameVersionCombo.SelectedIndex <= 0
            ? ""
            : ModGameVersionCombo.SelectedItem as string ?? "";
        UpdateModFilterHint();
        _ = LoadModsAsync(true);
    }

    private void ModLoader_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressModFilter || ModLoaderCombo == null || !IsLoaded) return;
        var index = ModLoaderCombo.SelectedIndex;
        _modLoader = index > 0 && index < LoaderOptions.Length ? LoaderOptions[index].Value : "";
        UpdateModFilterHint();
        _ = LoadModsAsync(true);
    }

    /// <summary>把筛选框同步到指定游戏版本（下载流程选完版本后调用）。</summary>
    private void SelectModGameVersion(string version)
    {
        if (ModGameVersionCombo == null || string.IsNullOrEmpty(version)) return;

        var index = ModGameVersionCombo.Items.IndexOf(version);
        if (index < 0)
        {
            // 列表尚未初始化时插入到下标 1 会越界，这里保证集合至少有「全部版本」
            if (ModGameVersionCombo.Items.Count == 0) ModGameVersionCombo.Items.Add("全部版本");
            var insertAt = Math.Min(1, ModGameVersionCombo.Items.Count);
            ModGameVersionCombo.Items.Insert(insertAt, version);
            index = insertAt;
        }

        _suppressModFilter = true;
        ModGameVersionCombo.SelectedIndex = index;
        _suppressModFilter = false;
        _modGameVersion = version;
        UpdateModFilterHint();
    }

    private void BuildSortChips()
    {
        if (SortPanel == null || SortPanel.Children.Count > 0) return;
        var options = new (string Index, string Label)[]
        {
            ("downloads", "按下载量"), ("newest", "最新"), ("follows", "关注最多"), ("relevance", "相关度")
        };
        foreach (var (index, label) in options)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Chip"),
                GroupName = "sort",
                Content = label,
                IsChecked = index == _sortIndex,
                Margin = new Thickness(0, 0, 6, 6),
                FontSize = 11
            };
            var captured = index;
            chip.Checked += (_, _) =>
            {
                _sortIndex = captured;
                _ = LoadModsAsync(true);
            };
            SortPanel.Children.Add(chip);
        }
    }

    private void Context_PropertyChanged(object sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        switch (e.PropertyName)
        {
            case nameof(DownloadContext.Progress):
                Bar.Value = _context.Progress;
                PercentText.Text = $"{_context.Progress:0}%";
                break;
            case nameof(DownloadContext.Status):
                StatusText.Text = _context.Status;
                break;
        }
    });

    private void BuildFolderChips()
    {
        if (FolderPanel == null) return;
        var dirs = Settings.Current.GameDirs;
        if (_installDir == null || !dirs.Contains(_installDir, StringComparer.OrdinalIgnoreCase))
        {
            // 默认用主页当前选中的目录，避免装到别的 .minecraft 里
            var selected = InstancePaths.Root;
            _installDir = dirs.Contains(selected, StringComparer.OrdinalIgnoreCase) ? selected : dirs[0];
            if (!Directory.Exists(_installDir))
            {
                try { Directory.CreateDirectory(_installDir); } catch { }
            }
        }

        FolderPanel.Children.Clear();
        foreach (var dir in dirs)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Chip"),
                GroupName = "folders",
                // 用所在文件夹名区分不同的 .minecraft 目录
                Content = PathLabel.ForGameDir(dir),
                ToolTip = dir,
                IsChecked = dir.Equals(_installDir, StringComparison.OrdinalIgnoreCase),
                Margin = new Thickness(0, 0, 6, 0)
            };
            var captured = dir;
            chip.Checked += (_, _) =>
            {
                _installDir = captured;
                TargetText.Text = "安装到：" + captured;
            };
            FolderPanel.Children.Add(chip);
        }
        TargetText.Text = "安装到：" + _installDir;
    }

    private void AppendLog(string message) => _context.Report(message);

    // ---------------- 版本列表 ----------------

    private async Task LoadVersionsAsync()
    {
        try
        {
            StatusText.Text = "正在获取 Minecraft 版本清单…";
            _allVersions = await McApi.GetVersionsAsync(true);
            ApplyVersionFilter();
            FillModVersionCombo();
            StatusText.Text = $"版本清单已加载（{(McApi.UsingMirror ? "镜像" : "官方")}，{_allVersions.Count} 个版本）。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "版本清单获取失败：" + ex.Message;
            Logger.Error("版本清单获取失败：" + ex);
        }
    }

    private void VersionFilter_Changed(object sender, RoutedEventArgs e) => ApplyVersionFilter();

    /// <summary>点击分类标签展开对应的版本列表；再次点击同一个标签收起。</summary>
    private void VersionCategory_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked || VersionListHost == null) return;
        var all = new[] { VerRelease, VerSnapshot, VerOld, VerApril };
        if (clicked.IsChecked == true)
        {
            foreach (var other in all)
                if (!ReferenceEquals(other, clicked)) other.IsChecked = false;
            VersionListHost.Visibility = Visibility.Visible;
            UiAnim.ExpandIn(VersionListHost, 8, 200);
        }
        else if (all.All(t => t.IsChecked != true))
        {
            VersionListHost.Visibility = Visibility.Collapsed;
        }
        ApplyVersionFilter();
    }

    private void VersionSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (VersionSearchHint != null)
            VersionSearchHint.Visibility = string.IsNullOrEmpty(VersionSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyVersionFilter();
    }

    /// <summary>愚人节版本 ID（Steam 之外的 Minecraft 彩蛋版本）。</summary>
    private static readonly HashSet<string> AprilFools = new(StringComparer.OrdinalIgnoreCase)
    {
        "15w14a", "1.RV-Pre1", "3D Shareware v1.34", "20w14infinite",
        "22w13oneBlockAtATime", "23w13a_or_b", "24w14potato", "25w14craftmine"
    };

    private void ApplyVersionFilter()
    {
        if (VersionList == null) return;
        if (_allVersions.Count == 0)
        {
            VersionHeader.Text = "正在获取版本清单…";
            return;
        }

        var keyword = (VersionSearch.Text ?? "").Trim();
        IEnumerable<McVersion> query;
        string label;
        if (VerSnapshot.IsChecked == true)
        {
            query = _allVersions.Where(v => v.Type == "snapshot" && !AprilFools.Contains(v.Id));
            label = "预览版";
        }
        else if (VerOld.IsChecked == true)
        {
            query = _allVersions.Where(v => v.Type is "old_beta" or "old_alpha");
            label = "远古版";
        }
        else if (VerApril.IsChecked == true)
        {
            query = _allVersions.Where(v => AprilFools.Contains(v.Id));
            label = "愚人节版";
        }
        else
        {
            query = _allVersions.Where(v => v.Type == "release");
            label = "正式版";
        }
        if (keyword.Length > 0) query = query.Where(v => v.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        _shownVersions = query.Take(500).ToList();
        var selected = VersionList.SelectedItem as string;
        VersionList.Items.Clear();
        foreach (var v in _shownVersions)
            VersionList.Items.Add($"{v.Id,-18}  {TypeName(v.Type),-6}  {v.ReleaseTime:yyyy-MM-dd}");
        if (selected != null && VersionList.Items.Contains(selected)) VersionList.SelectedItem = selected;
        else if (VersionList.Items.Count > 0) VersionList.SelectedIndex = 0;
        VersionHeader.Text = $"{label} · 共 {_shownVersions.Count} 个版本";
    }

    private static string TypeName(string type) => type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        "old_beta" => "远古 Beta",
        "old_alpha" => "远古 Alpha",
        _ => type
    };

    private string SelectedVersionId()
    {
        var index = VersionList.SelectedIndex;
        return index >= 0 && index < _shownVersions.Count ? _shownVersions[index].Id : null;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        InstallVanillaBtn.IsEnabled = !busy;
        InstallFabricBtn.IsEnabled = !busy;
        InstallForgeBtn.IsEnabled = !busy;
        InstallNeoForgeBtn.IsEnabled = !busy;
        CancelBtn.IsEnabled = busy;
    }

    private async Task<bool> InstallAsync(string title,
        Func<IProgress<string>, IProgress<double>, CancellationToken, Task> action, string doneMessage = null)
    {
        if (_busy) return false;
        _cts = new CancellationTokenSource();
        SetBusy(true);

        var context = new DownloadContext(title);
        _context.PropertyChanged -= Context_PropertyChanged;
        _context = context;
        _context.PropertyChanged += Context_PropertyChanged;
        Bar.Value = 0;
        PercentText.Text = "0%";
        StatusText.Text = "准备中…";
        InstallLog.Clear();
        ProgressHost.Visibility = Visibility.Visible;

        var completed = false;
        try
        {
            var log = new Progress<string>(s => _context.Report(s));
            var progress = new Progress<double>(p => _context.ReportProgress(p));
            Logger.Info("开始安装：" + title);
            await action(log, progress, _cts.Token);
            _context.Complete(doneMessage ?? (title + " 完成，可在「主页」启动。"));
            completed = true;
        }
        catch (OperationCanceledException)
        {
            _context.Report("已取消安装。");
        }
        catch (Exception ex)
        {
            _context.Fail(ex.Message);
            Logger.Error($"{title} 失败：" + ex);
            MessageBox.Show("安装失败：" + ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (!completed)
            {
                // 取消/失败后重置进度显示，避免残留进度条
                Bar.Value = 0;
                PercentText.Text = "0%";
                _context.ReportProgress(0);
                _context.ReportFile("");
            }
            // 只有下载过程中才显示进度条
            ProgressHost.Visibility = Visibility.Collapsed;
            SetBusy(false);
            _cts.Dispose();
            _cts = null;
        }
        return completed;
    }

    /// <summary>
    /// 安装游戏版本：先选安装目录并给版本起名，安装完成后把版本目录改成用户输入的名字（仍在同一目录里）。
    /// </summary>
    private async Task InstallVersionAsync(string title, string suggestedName,
        Func<string, IProgress<string>, IProgress<double>, CancellationToken, Task<string>> install)
    {
        var dialog = new VersionNameDialog(title, suggestedName, _installDir ?? InstancePaths.Root)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;

        var dir = dialog.SelectedDir;
        _installDir = dir;
        BuildFolderChips();

        string producedId = null;
        var ok = await InstallAsync(title, async (log, progress, ct) =>
            producedId = await install(dir, log, progress, ct));
        if (!ok || string.IsNullOrEmpty(producedId)) return;

        var finalId = producedId;
        try
        {
            finalId = McInstaller.RenameVersion(dir, producedId, dialog.VersionName);
        }
        catch (Exception ex)
        {
            MessageBox.Show("版本已安装，但改名失败：" + ex.Message, "版本名称", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // 让主页版本列表立刻能看到刚装好的版本（单独显示模式下也要把目录勾上）
        Settings.Current.AddGameFolder(dir);
        if (Settings.Current.VersionDisplayMode == "selected")
        {
            var list = Settings.Current.VersionDisplayDirs;
            if (!list.Contains(dir, StringComparer.OrdinalIgnoreCase)) list.Add(dir);
        }
        Settings.Current.SelectedVersionId = finalId;
        Settings.Current.SelectedVersionDir = dir;
        Settings.Save();
        AppState.SetSelectedVersion(finalId, dir);

        StatusText.Text = $"已安装 {finalId} → {Path.Combine(dir, "versions", finalId)}";
        Logger.Info($"版本安装完成：{finalId}（{dir}）");
    }

    private async void InstallVanilla_Click(object sender, RoutedEventArgs e)
    {
        var id = SelectedVersionId();
        if (id == null) { MessageBox.Show("请先在左侧选择一个版本。", "提示"); return; }
        await InstallVersionAsync("原版 " + id, id,
            (dir, log, progress, ct) => McInstaller.InstallVanillaAsync(id, log, progress, ct, dir));
    }

    private async void InstallFabric_Click(object sender, RoutedEventArgs e)
    {
        var id = SelectedVersionId();
        if (id == null) { MessageBox.Show("请先在左侧选择一个版本。", "提示"); return; }

        try
        {
            StatusText.Text = "正在获取 Fabric 版本列表…";
            var loaders = await McApi.GetFabricLoadersAsync(id);
            if (loaders.Count == 0) throw new Exception($"Fabric 不支持 {id}。");

            var picker = new LoaderPickerWindow($"Fabric · {id}", "选择要安装的 Fabric Loader 版本",
                loaders.Select(l => (l.Version, l.Version + (l.Stable ? "（稳定）" : ""))))
            {
                Owner = Window.GetWindow(this)
            };
            if (picker.ShowDialog() != true) return;

            var loader = picker.SelectedVersion;
            await InstallVersionAsync($"Fabric {loader} · {id}", $"fabric-loader-{loader}-{id}",
                (dir, log, progress, ct) => McInstaller.InstallFabricAsync(id, loader, log, progress, ct, dir));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fabric", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void InstallForge_Click(object sender, RoutedEventArgs e)
    {
        var id = SelectedVersionId();
        if (id == null) { MessageBox.Show("请先在左侧选择一个版本。", "提示"); return; }

        try
        {
            StatusText.Text = "正在获取 Forge 版本列表…";
            var versions = await McInstaller.GetForgeVersionsAsync(id);
            if (versions.Count == 0) throw new Exception($"Forge 没有适用于 {id} 的版本。");

            var picker = new LoaderPickerWindow($"Forge · {id}", "选择要安装的 Forge 版本", versions)
            {
                Owner = Window.GetWindow(this)
            };
            if (picker.ShowDialog() != true) return;

            await InstallVersionAsync($"Forge {picker.SelectedVersion} · {id}", $"{id}-forge-{picker.SelectedVersion}",
                (dir, log, progress, ct) => McInstaller.InstallForgeAsync(id, log, progress, ct, dir, picker.SelectedVersion));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Forge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void InstallNeoForge_Click(object sender, RoutedEventArgs e)
    {
        var id = SelectedVersionId();
        if (id == null) { MessageBox.Show("请先在左侧选择一个版本。", "提示"); return; }

        try
        {
            StatusText.Text = "正在获取 NeoForge 版本列表…";
            var versions = await McInstaller.GetNeoForgeVersionsAsync(id);
            if (versions.Count == 0) throw new Exception($"NeoForge 没有适用于 {id} 的版本（1.20.1 及更早请用 Forge）。");

            var picker = new LoaderPickerWindow($"NeoForge · {id}", "选择要安装的 NeoForge 版本", versions)
            {
                Owner = Window.GetWindow(this)
            };
            if (picker.ShowDialog() != true) return;

            var version = picker.SelectedVersion;
            // 1.20.1 等旧构件版本号带 MC 前缀，安装器生成的目录也带
            var suggested = version.StartsWith(id + "-", StringComparison.OrdinalIgnoreCase)
                ? $"{id}-neoforge-{version[(id.Length + 1)..]}"
                : "neoforge-" + version;
            await InstallVersionAsync($"NeoForge {version} · {id}", suggested,
                (dir, log, progress, ct) => McInstaller.InstallNeoForgeAsync(id, log, progress, ct, dir, version));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "NeoForge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    /// <summary>按 ESC 取消正在进行的下载 / 安装。</summary>
    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _busy)
        {
            _cts?.Cancel();
            e.Handled = true;
        }
    }

    // ---------------- 拖拽安装 ----------------

    private void View_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>拖入 .jar 直接装进当前版本 mods；拖入整合包则走导入流程。</summary>
    private void View_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var modsDir = AuroraLauncher.Core.InstancePaths.Ensure(AuroraLauncher.Core.InstancePaths.Mods);

        foreach (var file in files.Take(12))
        {
            if (file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var target = AuroraLauncher.Core.Mods.InstalledModService.InstallFromFile(file, modsDir);
                    Logger.Info("拖入安装模组：" + target);
                    ShowTransientStatus($"已安装到 mods：{Path.GetFileName(target)}", 4);
                }
                catch (Exception ex)
                {
                    Logger.Warn("拖入安装模组失败：" + ex.Message);
                    MessageBox.Show("安装失败：" + ex.Message, "模组", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                continue;
            }

            if (file.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (!AuroraLauncher.Core.Mc.ModpackService.LooksLikeModpack(file))
                {
                    MessageBox.Show("无法识别该压缩包格式。\n整合包需包含 modrinth.index.json 或 manifest.json。",
                        "整合包", MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }
                var window = new ModpackImportWindow(file, _ => { }) { Owner = Window.GetWindow(this) };
                window.ShowDialog();
            }
        }
    }

    private void Mcmod_Click(object sender, RoutedEventArgs e)
    {
        var query = (ModSearch.Text ?? "").Trim();
        if (query.Length == 0) query = _searchText;
        if (query.Length == 0)
        {
            MessageBox.Show("请先在搜索框输入模组名。", "提示");
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                AuroraLauncher.Core.Mods.ChineseNameTable.McmodSearchUrl(query)) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "打开失败");
        }
    }

    /// <summary>选择某个游戏版本只用于安装游戏本体，不再影响模组下载的筛选。</summary>
    private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _busy) return;
        var id = SelectedVersionId();
        if (string.IsNullOrEmpty(id)) return;
        StatusText.Text = "已选择版本：" + id;
    }

    private void VersionList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => _ = ChooseLoaderAndInstallAsync();

    /// <summary>双击版本后选择安装的加载器（原版 / Fabric / Forge / NeoForge）。</summary>
    private async Task ChooseLoaderAndInstallAsync()
    {
        var id = SelectedVersionId();
        if (id == null) return;

        var chooser = new LoaderPickerWindow($"安装 {id}", "选择要安装的加载器（也可只装原版）",
            new[]
            {
                ("none", "仅原版（不装加载器）"),
                ("fabric", "Fabric"),
                ("forge", "Forge"),
                ("neoforge", "NeoForge")
            })
        {
            Owner = Window.GetWindow(this)
        };
        if (chooser.ShowDialog() != true) return;

        try
        {
            switch (chooser.SelectedVersion)
            {
                case "none":
                    await InstallVersionAsync("原版 " + id, id,
                        (dir, log, progress, ct) => McInstaller.InstallVanillaAsync(id, log, progress, ct, dir));
                    break;

                case "fabric":
                {
                    var loaders = await McApi.GetFabricLoadersAsync(id);
                    if (loaders.Count == 0) throw new Exception($"Fabric 不支持 {id}。");
                    var picker = new LoaderPickerWindow($"Fabric · {id}", "选择 Fabric Loader 版本",
                        loaders.Select(l => (l.Version, l.Version + (l.Stable ? "（稳定）" : ""))))
                    { Owner = Window.GetWindow(this) };
                    if (picker.ShowDialog() != true) return;
                    var loader = picker.SelectedVersion;
                    await InstallVersionAsync($"Fabric {loader} · {id}", $"fabric-loader-{loader}-{id}",
                        (dir, log, progress, ct) => McInstaller.InstallFabricAsync(id, loader, log, progress, ct, dir));
                    break;
                }

                case "forge":
                {
                    var versions = await McInstaller.GetForgeVersionsAsync(id);
                    var picker = new LoaderPickerWindow($"Forge · {id}", "选择 Forge 版本", versions)
                    { Owner = Window.GetWindow(this) };
                    if (picker.ShowDialog() != true) return;
                    await InstallVersionAsync($"Forge {picker.SelectedVersion} · {id}", $"{id}-forge-{picker.SelectedVersion}",
                        (dir, log, progress, ct) => McInstaller.InstallForgeAsync(id, log, progress, ct, dir, picker.SelectedVersion));
                    break;
                }

                case "neoforge":
                {
                    var versions = await McInstaller.GetNeoForgeVersionsAsync(id);
                    var picker = new LoaderPickerWindow($"NeoForge · {id}", "选择 NeoForge 版本", versions)
                    { Owner = Window.GetWindow(this) };
                    if (picker.ShowDialog() != true) return;
                    var version = picker.SelectedVersion;
                    var suggested = version.StartsWith(id + "-", StringComparison.OrdinalIgnoreCase)
                        ? $"{id}-neoforge-{version[(id.Length + 1)..]}"
                        : "neoforge-" + version;
                    await InstallVersionAsync($"NeoForge {version} · {id}", suggested,
                        (dir, log, progress, ct) => McInstaller.InstallNeoForgeAsync(id, log, progress, ct, dir, version));
                    break;
                }
            }
        }
        catch (DownloadUnavailableException ex)
        {
            // 不弹「报错」：明确告知缺少什么，并提供官网入口让用户手动下载
            var open = MessageBox.Show(Window.GetWindow(this),
                "网络暂时无法连接下载源（官方源与镜像都已尝试）。\n\n" + ex.Message +
                "\n\n是否打开官方下载页手动下载？",
                "缺少文件", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (open == MessageBoxResult.Yes && !string.IsNullOrEmpty(ex.OfficialPage))
                Process.Start(new ProcessStartInfo(ex.OfficialPage) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "安装", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Detail_Click(object sender, RoutedEventArgs e)
    {
        var window = new DownloadDetailWindow(_context, _installDir ?? Settings.Current.EffectiveGameDir,
            () => _cts?.Cancel())
        {
            Owner = Window.GetWindow(this)
        };
        window.Show();
    }

    // ---------------- Modrinth ----------------

    private string CurrentProjectType() => _navKind switch
    {
        "modpack" => "modpack",
        "shader" => "shader",
        "resourcepack" => "resourcepack",
        "texturepack" => "resourcepack",
        _ => "mod"
    };

    private static string TypeLabel(string type) => type switch
    {
        "modpack" => "整合包",
        "resourcepack" => "资源包",
        "texturepack" => "材质包",
        "shader" => "光影",
        "vanilla" => "原版",
        _ => "Mod"
    };

    private void BuildCategoryChips()
    {
        if (CategoryPanel == null) return;
        var type = _navKind;
        CategoryPanel.Children.Clear();
        _category = null;

        var all = new RadioButton
        {
            Style = (Style)FindResource("Chip"),
            GroupName = "category",
            Content = "全部",
            IsChecked = true,
            Margin = new Thickness(0, 0, 6, 6)
        };
        all.Checked += (_, _) =>
        {
            _category = null;
            _ = LoadModsAsync(true);
        };
        CategoryPanel.Children.Add(all);

        if (!Categories.TryGetValue(type, out var tags)) return;
        foreach (var (tag, label) in tags)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Chip"),
                GroupName = "category",
                Content = label,
                Margin = new Thickness(0, 0, 6, 6)
            };
            var captured = tag;
            chip.Checked += (_, _) =>
            {
                _category = captured;
                _ = LoadModsAsync(true);
            };
            CategoryPanel.Children.Add(chip);
        }
    }

    private async void ModSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await LoadModsAsync(true, true);
    }

    private async Task LoadModsAsync(bool resetPage, bool? searchMode = null)
    {
        if (_busy) return;

        if (searchMode == true)
        {
            _searchText = (ModSearch.Text ?? "").Trim();
            _popularMode = _searchText.Length == 0;
        }
        else if (searchMode == false)
        {
            _popularMode = true;
            _searchText = "";
        }

        if (resetPage) _page = 0;
        var requestId = ++_modRequest;
        var type = CurrentProjectType();

        try
        {
            ModHeader.Text = "加载中…";
            PrevPageBtn.IsEnabled = false;
            NextPageBtn.IsEnabled = false;

            // 并发请求 Modrinth 与 CurseForge，合并去重
            var result = await AuroraLauncher.Core.Mods.ModSearchService.SearchAsync(_searchText, type,
                _modGameVersion, type == "mod" ? _modLoader : null, _page, PageSize);
            if (requestId != _modRequest) return;

            _mods.Clear();
            ModList.Items.Clear();
            foreach (var entry in result.Entries)
            {
                var item = new ModItem
                {
                    ProjectId = entry.ProjectId,
                    Source = entry.Source,
                    Slug = entry.Slug,
                    AlternateId = entry.AlternateId,
                    AlternateSource = entry.AlternateSource,
                    OriginalTitle = entry.EnglishName,
                    Title = entry.DisplayTitle,
                    Tag = $"{TypeLabel(_navKind)} · {entry.SourceLabel}",
                    Description = Shorten(entry.Summary),
                    OriginalDescription = entry.Summary,
                    Downloads = entry.Downloads > 0 ? "↓" + FormatCount(entry.Downloads) : "",
                    IconUrl = entry.IconUrl
                };
                item.Icon = PlaceholderIcon(item.Title);
                _mods.Add(item);
                ModList.Items.Add(item);
                if (item.IconUrl.Length > 0) _ = LoadIconAsync(item);
                if (rawContainsEnglish(entry.Summary)) _ = TranslateDescriptionAsync(item);
            }
            if (_mods.Count > 0) ModList.SelectedIndex = 0;

            PageText.Text = $"{_page + 1}";
            PrevPageBtn.IsEnabled = _page > 0;
            NextPageBtn.IsEnabled = _mods.Count >= PageSize;

            var prefix = _popularMode ? $"热门{TypeLabel(_navKind)}" : $"「{_searchText}」的搜索结果";
            var hint = result.UnavailableHint;
            var sourceInfo = hint.Length > 0 ? hint : "Modrinth + CurseForge";
            ModHeader.Text = $"{prefix} · {sourceInfo} · 本页 {_mods.Count} 个";

            McmodBtn.Visibility = _mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_mods.Count == 0 && hint.Length > 0) ShowTransientStatus(hint);
        }
        catch (Exception ex)
        {
            if (requestId != _modRequest) return;
            ModHeader.Text = "加载失败：" + ex.Message;
            Logger.Warn("模组检索失败：" + ex.Message);
        }
    }

    private static bool rawContainsEnglish(string text) =>
        !string.IsNullOrWhiteSpace(text) && !ContainsChinese(text);

    private static string Shorten(string text, int max = 70) =>
        string.IsNullOrEmpty(text) ? "" : text.Length > max ? text[..max] + "…" : text;

    private System.Windows.Threading.DispatcherTimer _statusTimer;

    /// <summary>显示一条会自动淡出的状态提示（默认 3 秒后渐隐）。</summary>
    private void ShowTransientStatus(string message, int seconds = 3)
    {
        StatusText.BeginAnimation(UIElement.OpacityProperty, null);
        StatusText.Opacity = 1;
        StatusText.Text = message;
        _statusTimer?.Stop();
        _statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(450));
            fade.Completed += (_, _) =>
            {
                StatusText.BeginAnimation(UIElement.OpacityProperty, null);
                StatusText.Opacity = 1;
                StatusText.Text = "";
            };
            StatusText.BeginAnimation(UIElement.OpacityProperty, fade);
        };
        _statusTimer.Start();
    }

    private static readonly Dictionary<string, ImageSource> PlaceholderCache = new();

    /// <summary>图标加载失败时使用的彩色占位图（保证列表始终有图标）。</summary>
    private static ImageSource PlaceholderIcon(string title)
    {
        var colors = new[]
        {
            (Color.FromRgb(0x5B, 0x8C, 0xFF), Color.FromRgb(0x2F, 0x5B, 0xD0)),
            (Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0x5B, 0x34, 0xC0)),
            (Color.FromRgb(0x22, 0xC7, 0xA9), Color.FromRgb(0x10, 0x7A, 0x66)),
            (Color.FromRgb(0xFF, 0x7A, 0x59), Color.FromRgb(0xC0, 0x45, 0x2B)),
            (Color.FromRgb(0xF2, 0xC1, 0x4E), Color.FromRgb(0xB8, 0x8A, 0x1E))
        };
        var hash = 0;
        foreach (var c in title) hash = (hash * 31 + c) & 0x7fffffff;
        var (c1, c2) = colors[hash % colors.Length];
        var letter = string.IsNullOrEmpty(title) ? "?" : title[..1].ToUpperInvariant();
        var key = letter + c1;
        if (PlaceholderCache.TryGetValue(key, out var cached)) return cached;

        const int size = 64;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new LinearGradientBrush(c1, c2, new Point(0, 0), new Point(1, 1));
            dc.DrawRoundedRectangle(brush, null, new Rect(1, 1, size - 2, size - 2), 14, 14);
            var typeface = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal,
                FontWeights.Bold, FontStretches.Normal);
            var formatted = new FormattedText(letter, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, 30, Brushes.White, 1.0);
            dc.DrawText(formatted, new Point((size - formatted.Width) / 2, (size - formatted.Height) / 2));
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        PlaceholderCache[key] = bitmap;
        return bitmap;
    }

    private static bool ContainsChinese(string text) =>
        text.Any(c => c >= 0x4E00 && c <= 0x9FFF);

    /// <summary>把英文简介翻译成中文（参考 MCmod 的中文显示方式）。</summary>
    private static async Task TranslateDescriptionAsync(ModItem item)
    {
        var original = item.OriginalDescription;
        if (string.IsNullOrWhiteSpace(original)) return;
        if (TranslationCache.TryGetValue(original, out var cached))
        {
            item.Description = cached;
            return;
        }

        try
        {
            var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(original)}&langpair=en|zh-CN";
            using var doc = JsonDocument.Parse(await TranslateClient.GetStringAsync(url));
            var translated = doc.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString();
            if (string.IsNullOrWhiteSpace(translated) ||
                translated.Equals(original, StringComparison.OrdinalIgnoreCase)) return;

            var shortText = translated.Length > 70 ? translated[..70] + "…" : translated;
            TranslationCache[original] = shortText;
            item.Description = shortText;
        }
        catch { }
    }

    /// <summary>使用 MCmod 收录的中文名显示为「中文名 | 英文名」；找不到时保留英文原名（与 PCL 一致）。</summary>
    private static async Task TranslateTitleAsync(ModItem item)
    {
        var original = item.OriginalTitle;
        if (string.IsNullOrWhiteSpace(original)) return;

        var key = "T:" + original;
        if (TranslationCache.TryGetValue(key, out var cached))
        {
            item.Title = $"{cached} | {original}";
            return;
        }

        try
        {
            var mcmodName = await McmodService.GetChineseNameAsync(original);
            if (!string.IsNullOrWhiteSpace(mcmodName))
            {
                TranslationCache[key] = mcmodName;
                item.Title = $"{mcmodName} | {original}";
            }
        }
        catch { }
    }

    private static string CategoryName(string type, string tag)
    {
        if (Categories.TryGetValue(type, out var tags))
            foreach (var (t, label) in tags)
                if (t == tag) return label;
        return tag;
    }

    private static string FormatCount(long value) => value switch
    {
        >= 100_000_000 => (value / 100_000_000.0).ToString("0.0") + "亿",
        >= 10_000 => (value / 10_000.0).ToString("0.0") + "万",
        _ => value.ToString("N0")
    };

    private static async Task LoadIconAsync(ModItem item)
    {
        // Modrinth 图标是 WebP，WPF 无法直接解码；用 wsrv.nl 转成 PNG，失败再试原图
        var urls = new List<string>
        {
            "https://wsrv.nl/?url=" + Uri.EscapeDataString(item.IconUrl) + "&output=png",
            item.IconUrl
        };
        foreach (var url in urls)
        {
            try
            {
                var bytes = await IconClient.GetByteArrayAsync(url);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new MemoryStream(bytes);
                bitmap.EndInit();
                bitmap.Freeze();
                item.Icon = bitmap;
                return;
            }
            catch { }
        }
    }

    private async void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_page <= 0) return;
        _page--;
        await LoadModsAsync(false);
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        _page++;
        await LoadModsAsync(false);
    }

    private async void SearchMods_Click(object sender, RoutedEventArgs e) => await LoadModsAsync(true, true);

    private async void ModList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => await DownloadSelectedModAsync();

    private async void DownloadMod_Click(object sender, RoutedEventArgs e) => await DownloadSelectedModAsync();

    private async Task DownloadSelectedModAsync()
    {
        if (_busy) return;
        if (ModList.SelectedItem is not ModItem item)
        {
            MessageBox.Show("请先选择一个资源。", "提示");
            return;
        }

        var type = CurrentProjectType();
        var gameVersion = _modGameVersion;
        if (string.IsNullOrEmpty(gameVersion))
        {
            // 没有手动筛选时，优先用主页所选客户端的原版版本
            var (instanceMc, _) = McVersions.GetInstanceTag(AppState.SelectedGameDir, AppState.SelectedVersionId);
            gameVersion = instanceMc;
        }

        // 第一步：仍然没有版本时，先让玩家选择
        if (string.IsNullOrEmpty(gameVersion))
        {
            StatusText.Text = "请选择要下载的游戏版本…";
            var options = await GetGameVersionOptionsAsync();
            if (options.Count == 0)
            {
                MessageBox.Show("没有可用的游戏版本。", "提示");
                return;
            }
            var picker = new LoaderPickerWindow("选择游戏版本", $"为「{item.Title}」选择对应的游戏版本", options)
            {
                Owner = Window.GetWindow(this)
            };
            if (picker.ShowDialog() != true) return;
            gameVersion = picker.SelectedVersion;
            SelectModGameVersion(gameVersion);
        }

        // 只有模组才按加载器筛选（光影 / 资源包 / 材质包 / 整合包没有加载器概念）
        var loader = type == "mod" && _modLoader.Length > 0 ? _modLoader : null;

        // 第二步：按游戏版本 + 加载器获取文件，再展开选择具体模组版本
        List<ModFile> files;
        string hint = null;
        try
        {
            ShowTransientStatus($"正在获取「{item.Title}」可用版本…");
            if (item.Source == AuroraLauncher.Core.Mods.ModSource.CurseForge)
            {
                var entries = await AuroraLauncher.Core.Mods.CurseForgeApi.GetFilesAsync(item.ProjectId, gameVersion, loader, CancellationToken.None);
                if (entries.Count == 0)
                {
                    entries = await AuroraLauncher.Core.Mods.CurseForgeApi.GetFilesAsync(item.ProjectId, null, null, CancellationToken.None);
                    if (entries.Count == 0) throw new Exception("该项目没有可下载的文件。");
                    hint = $"{gameVersion}{(loader != null ? " · " + loader : "")} 没有匹配文件，已显示全部版本。";
                }
                files = entries.Select(entry => new ModFile
                {
                    Name = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.FileName : entry.DisplayName,
                    FileName = entry.FileName,
                    Url = entry.Url,
                    GameVersions = entry.GameVersions,
                    Loaders = entry.Loaders,
                    Date = entry.Date,
                    Size = entry.Size,
                    Sha1 = entry.Sha1,
                    RequiredProjects = entry.RequiredProjects
                }).ToList();
            }
            else
            {
                using var doc = await McApi.GetModrinthVersionsAsync(item.ProjectId, gameVersion, loader);
                files = ParseModFiles(doc.RootElement);
                if (files.Count == 0)
                {
                    using var fallback = await McApi.GetModrinthVersionsAsync(item.ProjectId, null, null);
                    files = ParseModFiles(fallback.RootElement);
                    if (files.Count == 0) throw new Exception("该项目没有可下载的文件。");
                    hint = $"{gameVersion}{(loader != null ? " · " + loader : "")} 没有匹配文件，已显示全部版本。";
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("获取模组版本失败：" + ex.Message);
            MessageBox.Show("获取版本失败：" + ex.Message, item.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var chooser = new ModVersionWindow(item.Title, gameVersion, loader, files, hint)
        {
            Owner = Window.GetWindow(this)
        };
        if (chooser.ShowDialog() != true || chooser.SelectedFile == null) return;
        var file = chooser.SelectedFile;

        // 整合包：下载后直接解析成游戏列表里的客户端，不在版本目录里留下一个文件夹
        if (type == "modpack")
        {
            await InstallModpackAsync(item, file);
            return;
        }

        // 第三步：确认保存位置（默认就是该版本对应的 mods / resourcepacks 等目录）并下载
        var targetDir = ResolveTargetDir(type, gameVersion);
        try { Directory.CreateDirectory(targetDir); } catch { }

        var saveName = type == "mod"
            ? ModNamePrefix.Apply(
                file.FileName,
                ModNamePrefix.Describe(item.Source, item.ProjectId, item.Slug, item.OriginalTitle),
                Settings.Current.ModPrefixMode)
            : file.FileName;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = $"确认保存位置（{TypeLabel(_navKind)}）",
            FileName = saveName,
            InitialDirectory = targetDir,
            Filter = BuildSaveFilter(file.FileName),
            AddExtension = false,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true) return;

        var target = dialog.FileName;
        _downloadTargetDir = Path.GetDirectoryName(target) ?? targetDir;

        // 前置模组检测：Modrinth 的 required 依赖 + CurseForge 的 RequiredDependency
        var missing = new List<AuroraLauncher.Core.Mods.DependencyItem>();
        if (file.RequiredProjects.Count > 0)
        {
            try
            {
                ShowTransientStatus("正在检查前置模组…");
                missing = await AuroraLauncher.Core.Mods.DependencyResolver.FindMissingAsync(
                    file.RequiredProjects, gameVersion, loader,
                    AuroraLauncher.Core.InstancePaths.Mods);
            }
            catch (Exception ex)
            {
                Logger.Warn("检查前置模组失败：" + ex.Message);
            }

            if (missing.Count > 0)
            {
                var names = string.Join(Environment.NewLine, missing.Select(m => "· " + m.Name));
                if (MessageBox.Show(
                        $"「{item.Title}」需要以下前置模组，当前版本的 mods 文件夹里没有：{Environment.NewLine}{Environment.NewLine}" +
                        $"{names}{Environment.NewLine}{Environment.NewLine}是否一键安装/补齐这些前置模组？",
                        "缺少前置模组", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    missing.Clear();
            }
        }

        await InstallAsync("下载 " + item.Title, async (log, progress, ct) =>
        {
            var installDir = Path.GetDirectoryName(target) ?? targetDir;
            foreach (var dependency in missing)
            {
                var dependencyName = ModNamePrefix.Apply(
                    dependency.File.FileName,
                    ModNamePrefix.Describe(dependency.Source, dependency.ProjectId, "", dependency.Name),
                    Settings.Current.ModPrefixMode);
                var dependencyTarget = Path.Combine(installDir, dependencyName);
                log.Report($"补齐前置：{dependency.Name} → {dependencyName}");
                await McApi.DownloadAsync(dependency.File.Url, dependencyTarget, dependency.File.Sha1, ct);
                log.Report("前置已安装：" + dependencyTarget);
            }

            log.Report($"下载 {file.FileName}（{file.Loaders}）" + (file.Sha1.Length > 0 ? "，校验 SHA1" : ""));
            _context.ReportFile(target);
            progress.Report(0);
            await McApi.DownloadAsync(file.Url, target, file.Sha1, ct, progress);
            log.Report("已保存到 " + target);
        }, $"「{item.Title}」已保存到 {target}");
    }

    /// <summary>整合包：下载到临时目录后解析安装成一个完整客户端，直接进入游戏列表（不在版本目录里留文件夹）。</summary>
    private async Task InstallModpackAsync(ModItem item, ModFile file)
    {
        // 跟随下载页当前选择的目录（与主页选中目录一致），不要装到别的 .minecraft
        var gameDir = _installDir ?? ResolveGameRoot();
        var tempDir = Path.Combine(Path.GetTempPath(), "mctl-modpacks");
        try { Directory.CreateDirectory(tempDir); } catch { }
        var tempFile = Path.Combine(tempDir, SanitizeFileName(file.FileName));
        string installed = null;

        await InstallAsync("安装整合包 " + item.Title, async (log, progress, ct) =>
        {
            log.Report($"下载整合包：{file.FileName}");
            var download = new Progress<double>(p => progress.Report(p * 0.35));
            // cdn.modrinth.com 不通时，用官方 API 的按哈希下载接口换一条线路
            var alternate = file.Url.Contains("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase) && file.Sha1.Length == 40
                ? $"https://api.modrinth.com/v2/version_file/{file.Sha1}/download"
                : null;
            await McApi.DownloadAsync(file.Url, tempFile, file.Sha1, ct, download, alternate);

            log.Report("解析整合包，安装进游戏列表…");
            var install = new Progress<double>(p => progress.Report(0.35 + p * 0.65));
            installed = await ModpackService.ImportAsync(tempFile, gameDir, log, install, ct);
            log.Report("已加入游戏列表：" + installed);
        }, $"「{item.Title}」已安装到游戏列表，可在主页启动。");

        try { File.Delete(tempFile); } catch { }
        if (!string.IsNullOrEmpty(installed))
        {
            Settings.Current.AddGameFolder(gameDir);
            Settings.Save();
            AppState.SetSelectedVersion(installed, gameDir);
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "modpack.mrpack" : name;
    }

    /// <summary>保存对话框的过滤器：按下载文件扩展名生成。</summary>
    private static string BuildSaveFilter(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) return "所有文件 (*.*)|*.*";
        var label = ext.TrimStart('.').ToUpperInvariant();
        return $"{label} 文件 (*{ext})|*{ext}|所有文件 (*.*)|*.*";
    }

    /// <summary>解析 Modrinth 版本数组，取每个版本的主文件。</summary>
    private static List<ModFile> ParseModFiles(JsonElement versions)
    {
        var list = new List<ModFile>();
        if (versions.ValueKind != JsonValueKind.Array) return list;

        foreach (var v in versions.EnumerateArray())
        {
            if (!v.TryGetProperty("files", out var files) || files.GetArrayLength() == 0) continue;

            JsonElement primary = default;
            var hasFile = false;
            foreach (var f in files.EnumerateArray())
            {
                if (!hasFile) { primary = f; hasFile = true; }
                if (f.TryGetProperty("primary", out var p) && p.GetBoolean()) { primary = f; break; }
            }
            if (!hasFile) continue;

            var name = v.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var number = v.TryGetProperty("version_number", out var vn) ? vn.GetString() ?? "" : "";
            var gameVersions = v.TryGetProperty("game_versions", out var gv)
                ? string.Join(", ", gv.EnumerateArray().Select(x => x.GetString()))
                : "";
            var loaders = v.TryGetProperty("loaders", out var ld)
                ? string.Join(", ", ld.EnumerateArray().Select(x => x.GetString()))
                : "";
            var date = v.TryGetProperty("date_published", out var d) &&
                       DateTime.TryParse(d.GetString(), out var dt)
                ? dt.ToLocalTime().ToString("yyyy-MM-dd")
                : "";
            var size = primary.TryGetProperty("size", out var s) ? FormatSize(s.GetInt64()) : "";
            var sha1 = primary.TryGetProperty("hashes", out var hashesObj) &&
                       hashesObj.TryGetProperty("sha1", out var sha1Obj)
                ? sha1Obj.GetString() ?? ""
                : "";

            // 强制前置（dependency_type = required），用于缺失检测
            var requiredProjects = new List<(AuroraLauncher.Core.Mods.ModSource, string)>();
            if (v.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
            {
                foreach (var dep in deps.EnumerateArray())
                {
                    var dependencyType = dep.TryGetProperty("dependency_type", out var typeValue) ? typeValue.GetString() : "required";
                    if (!string.Equals(dependencyType, "required", StringComparison.OrdinalIgnoreCase)) continue;
                    var requiredId = dep.TryGetProperty("project_id", out var pid) ? pid.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(requiredId))
                        requiredProjects.Add((AuroraLauncher.Core.Mods.ModSource.Modrinth, requiredId));
                }
            }

            list.Add(new ModFile
            {
                Name = string.IsNullOrWhiteSpace(name) ? number : name,
                FileName = primary.GetProperty("filename").GetString() ?? "",
                Url = primary.GetProperty("url").GetString() ?? "",
                GameVersions = gameVersions,
                Loaders = loaders,
                Date = date,
                Size = size,
                Sha1 = sha1,
                RequiredProjects = requiredProjects
            });
        }
        return list;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB",
        >= 1024 => (bytes / 1024.0).ToString("0") + " KB",
        _ => bytes + " B"
    };

    /// <summary>资源保存目录：跟随主页所选版本（版本隔离时写入 versions/&lt;版本&gt;/），与 PCL 一致。</summary>
    private string ResolveTargetDir(string type, string gameVersion)
    {
        var root = ResolveGameRoot();
        var selectedId = AppState.SelectedVersionId;
        string baseDir;

        if (!string.IsNullOrEmpty(selectedId) && Settings.Current.VersionIsolation)
        {
            // 主页选中的版本 = 当前实例，资源直接装进该版本的隔离目录
            baseDir = Path.Combine(root, "versions", selectedId);
        }
        else
        {
            var versionDir = string.IsNullOrEmpty(gameVersion) ? null : Path.Combine(root, "versions", gameVersion);
            baseDir = versionDir != null && Directory.Exists(versionDir) ? versionDir : root;
        }
        try { Directory.CreateDirectory(baseDir); } catch { }

        var folder = type switch
        {
            "resourcepack" => "resourcepacks",
            "shader" => "shaderpacks",
            _ => "mods"
        };
        var target = Path.Combine(baseDir, folder);
        try { Directory.CreateDirectory(target); } catch { }
        return target;
    }

    /// <summary>可选的游戏版本：已安装版本优先，其次正式版清单。</summary>
    private async Task<List<(string Value, string Label)>> GetGameVersionOptionsAsync()
    {
        var result = new List<(string Value, string Label)>();
        try
        {
            foreach (var entry in McVersions.ScanAll())
                if (result.All(r => r.Value != entry.Id)) result.Add((entry.Id, entry.Id + "（已安装）"));
        }
        catch { }

        if (_allVersions.Count == 0)
        {
            try { _allVersions = await McApi.GetVersionsAsync(true); }
            catch { }
        }
        foreach (var v in _allVersions.Where(v => v.Type == "release").Take(60))
            if (result.All(r => r.Value != v.Id)) result.Add((v.Id, v.Id));
        return result;
    }

    private void OpenMods_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = ResolveTargetDir(CurrentProjectType(), _modGameVersion);
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "打开失败");
        }
    }
}
