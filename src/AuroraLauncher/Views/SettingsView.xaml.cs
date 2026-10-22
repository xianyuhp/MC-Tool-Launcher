using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using AuroraLauncher.Core;
using AuroraLauncher.Core.Mc;

namespace AuroraLauncher.Views;

public partial class SettingsView : UserControl, IRefreshable
{
    private static readonly string[] Palette =
    {
        "#5B8CFF", "#8B5CF6", "#22C7A9", "#FF7A59", "#F2C14E", "#FF5C8A"
    };

    private bool _loading = true;
    private List<JavaInfo> _javas = new();

    public SettingsView()
    {
        InitializeComponent();
        BuildColorSwatches();
        LoadFromSettings();
        MusicService.Instance.PlaylistChanged += UpdateMusicStatus;
        Settings.Changed += OnExternalSettingsChanged;
        Unloaded += (_, _) =>
        {
            MusicService.Instance.PlaylistChanged -= UpdateMusicStatus;
            Settings.Changed -= OnExternalSettingsChanged;
        };
        Refresh();
    }

    /// <summary>标题栏切换主题时，同步设置页里的主题选中状态。</summary>
    private void OnExternalSettingsChanged()
    {
        if (_loading || ThemeLight == null) return;
        _loading = true;
        ThemeLight.IsChecked = Settings.Current.ThemeMode == "light";
        ThemeDark.IsChecked = Settings.Current.ThemeMode != "light";
        _loading = false;
    }

    public void Refresh()
    {
        LoadFromSettings();
        UpdateMusicStatus();
    }

    private void LoadFromSettings()
    {
        _loading = true;
        var s = Settings.Current;

        AcrylicChk.IsChecked = s.AcrylicEnabled;
        AcrylicSlider.Value = s.AcrylicOpacity;
        AcrylicValue.Text = s.AcrylicOpacity + "%";

        BackgroundPath.Text = string.IsNullOrWhiteSpace(s.BackgroundImage) ? "未设置" : s.BackgroundImage;
        BackgroundOpacitySlider.Value = s.BackgroundOpacity;
        BackgroundOpacityValue.Text = s.BackgroundOpacity + "%";
        BackgroundBlurSlider.Value = s.BackgroundBlur;
        BackgroundBlurValue.Text = s.BackgroundBlur + " px";

        ThemeLight.IsChecked = s.ThemeMode == "light";
        ThemeDark.IsChecked = s.ThemeMode != "light";
        CardOpacitySlider.Value = s.CardOpacity;
        CardOpacityValue.Text = s.CardOpacity + "%";
        SidebarOpacitySlider.Value = s.SidebarOpacity;
        SidebarOpacityValue.Text = s.SidebarOpacity + "%";
        TitleBarOpacitySlider.Value = s.TitleBarOpacity;
        TitleBarOpacityValue.Text = s.TitleBarOpacity + "%";
        ContentOpacitySlider.Value = s.ContentOpacity;
        ContentOpacityValue.Text = s.ContentOpacity + "%";

        MusicFolderText.Text = s.EffectiveMusicFolder;
        VolumeSlider.Value = s.MusicVolume;
        VolumeValue.Text = s.MusicVolume + "%";
        AutoPlayChk.IsChecked = s.MusicAutoPlay;
        RainbowChk.IsChecked = s.RainbowTitles;
        HomeLogChk.IsChecked = s.ShowHomeLog;
        Skin3DChk.IsChecked = s.ShowSkin3D;
        CurseForgeKeyBox.Text = s.CurseForgeApiKey;
        NameTableHint.Text = $"模组中文名表：{AuroraLauncher.Core.Mods.ChineseNameTable.Count} 条";

        FolderList.Items.Clear();
        foreach (var dir in s.GameDirs) FolderList.Items.Add(dir);
        FolderList.SelectedIndex = FolderList.Items.Count > 0 ? 0 : -1;

        MirrorAuto.IsChecked = s.DownloadMirror != "official";
        MirrorOfficial.IsChecked = s.DownloadMirror == "official";

        ModPageSizeSlider.Value = Math.Clamp(s.ModPageSize, 10, 25);
        ModPageSizeValue.Text = Math.Clamp(s.ModPageSize, 10, 25) + " 个";

        MultiThreadChk.IsChecked = s.MultiThreadDownload;
        ThreadsSlider.Value = Math.Clamp(s.DownloadThreads, 2, 16);
        ThreadsValue.Text = Math.Clamp(s.DownloadThreads, 2, 16) + " 线程";

        var gb = Math.Clamp(s.MemoryMb / 1024, 1, 32);
        MemorySlider.Value = gb;
        MemoryValue.Text = gb + " G";
        PlayerNameBox.Text = s.PlayerName;
        WidthBox.Text = s.WindowWidth.ToString();
        HeightBox.Text = s.WindowHeight.ToString();
        FullscreenChk.IsChecked = s.Fullscreen;
        IsolationChk.IsChecked = s.VersionIsolation;
        ChineseChk.IsChecked = s.ForceChinese;
        JavaAutoChk.IsChecked = s.JavaAutoSelect;

        foreach (var item in ModPrefixCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, s.ModPrefixMode, StringComparison.OrdinalIgnoreCase))
            {
                ModPrefixCombo.SelectedItem = item;
                break;
            }
        }
        if (ModPrefixCombo.SelectedItem == null) ModPrefixCombo.SelectedIndex = 0;

        LoadJavaList();
        _loading = false;
    }

    private void LoadJavaList()
    {
        _javas = JavaFinder.FindAll();
        JavaList.Items.Clear();
        foreach (var java in _javas) JavaList.Items.Add(java.ToString());
        JavaHint.Text = _javas.Count == 0 ? "未检测到 Java，请点击「手动选择 java.exe」。" : $"共检测到 {_javas.Count} 个 Java";
    }

    private void Save() => Settings.Save();

    /// <summary>模组文件名前缀：保存后立即对下一次下载生效（无需重启）。</summary>
    private void ModPrefix_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ModPrefixCombo.SelectedItem is not ComboBoxItem item) return;
        Settings.Current.ModPrefixMode = item.Tag as string ?? "none";
        Save();
    }

    // ---------------- 外观 ----------------

    private void Acrylic_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.AcrylicEnabled = AcrylicChk.IsChecked == true;
        Save();
    }

    private void AcrylicSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || AcrylicValue == null) return;
        Settings.Current.AcrylicOpacity = (int)AcrylicSlider.Value;
        AcrylicValue.Text = Settings.Current.AcrylicOpacity + "%";
        Save();
    }

    private void BackgroundOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || BackgroundOpacityValue == null) return;
        Settings.Current.BackgroundOpacity = (int)BackgroundOpacitySlider.Value;
        BackgroundOpacityValue.Text = Settings.Current.BackgroundOpacity + "%";
        Save();
    }

    private void BackgroundBlur_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || BackgroundBlurValue == null) return;
        Settings.Current.BackgroundBlur = (int)BackgroundBlurSlider.Value;
        BackgroundBlurValue.Text = Settings.Current.BackgroundBlur + " px";
        Save();
    }

    private void PickBackground_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择背景图片",
            Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
        };
        if (dialog.ShowDialog() != true) return;

        // 复制到 config/images，方便随配置一起备份
        try
        {
            Directory.CreateDirectory(Settings.ImagesDir);
            var target = Path.Combine(Settings.ImagesDir,
                $"background-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(dialog.FileName)}");
            File.Copy(dialog.FileName, target, true);
            Settings.Current.BackgroundImage = target;
            BackgroundPath.Text = target;
        }
        catch
        {
            Settings.Current.BackgroundImage = dialog.FileName;
            BackgroundPath.Text = dialog.FileName;
        }
        Save();
    }

    private void ClearBackground_Click(object sender, RoutedEventArgs e)
    {
        Settings.Current.BackgroundImage = "";
        BackgroundPath.Text = "未设置";
        Save();
    }

    private void Theme_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.ThemeMode = ThemeLight.IsChecked == true ? "light" : "dark";
        Save();
    }

    private void JavaAuto_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.JavaAutoSelect = JavaAutoChk.IsChecked == true;
        JavaHint.Text = Settings.Current.JavaAutoSelect
            ? "已启用自动选择：启动时会按版本要求挑选 Java。"
            : "已关闭自动选择：将使用下方手动选择的 Java。";
        Save();
    }

    private void Chinese_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.ForceChinese = ChineseChk.IsChecked == true;
        Save();
    }

    private void CardOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || CardOpacityValue == null) return;
        Settings.Current.CardOpacity = (int)CardOpacitySlider.Value;
        CardOpacityValue.Text = Settings.Current.CardOpacity + "%";
        Save();
    }

    private void SidebarOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || SidebarOpacityValue == null) return;
        Settings.Current.SidebarOpacity = (int)SidebarOpacitySlider.Value;
        SidebarOpacityValue.Text = Settings.Current.SidebarOpacity + "%";
        Save();
    }

    private void TitleBarOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || TitleBarOpacityValue == null) return;
        Settings.Current.TitleBarOpacity = (int)TitleBarOpacitySlider.Value;
        TitleBarOpacityValue.Text = Settings.Current.TitleBarOpacity + "%";
        Save();
    }

    private void ContentOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || ContentOpacityValue == null) return;
        Settings.Current.ContentOpacity = (int)ContentOpacitySlider.Value;
        ContentOpacityValue.Text = Settings.Current.ContentOpacity + "%";
        Save();
    }

    private void BuildColorSwatches()
    {
        ColorPanel.Children.Clear();
        foreach (var hex in Palette)
        {
            var selected = string.Equals(Settings.Current.AccentColor, hex, StringComparison.OrdinalIgnoreCase);
            var button = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(9),
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderThickness = new Thickness(selected ? 2 : 1),
                BorderBrush = new SolidColorBrush(selected ? Colors.White : Color.FromArgb(0x44, 255, 255, 255)),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = hex
            };
            button.MouseLeftButtonUp += (_, _) =>
            {
                Settings.Current.AccentColor = hex;
                Save();
                BuildColorSwatches();
            };
            ColorPanel.Children.Add(button);
        }
    }

    // ---------------- 音乐 ----------------

    private void PickMusicFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择音乐文件夹" };
        if (dialog.ShowDialog() != true) return;
        Settings.Current.MusicFolder = dialog.FolderName;
        MusicFolderText.Text = dialog.FolderName;
        Save();
        MusicService.Instance.Reload();
        UpdateMusicStatus();
    }

    private void OpenMusicFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Settings.Current.EffectiveMusicFolder;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "打开失败");
        }
    }

    private void RescanMusic_Click(object sender, RoutedEventArgs e)
    {
        MusicService.Instance.Reload();
        UpdateMusicStatus();
    }

    private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || VolumeValue == null) return;
        Settings.Current.MusicVolume = (int)VolumeSlider.Value;
        VolumeValue.Text = Settings.Current.MusicVolume + "%";
        MusicService.Instance.ApplyVolume();
        Save();
    }

    private void AutoPlay_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.MusicAutoPlay = AutoPlayChk.IsChecked == true;
        Save();
    }

    private void Rainbow_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.RainbowTitles = RainbowChk.IsChecked == true;
        Save();
    }

    /// <summary>主页运行日志开关（默认关闭）。</summary>
    private void HomeLog_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.ShowHomeLog = HomeLogChk.IsChecked == true;
        Save();
    }

    /// <summary>主页 3D 皮肤预览开关（默认开启）。</summary>
    private void Skin3D_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.ShowSkin3D = Skin3DChk.IsChecked == true;
        Save();
    }

    // ---------------- 模组数据源 ----------------

    private void SaveCurseForgeKey_Click(object sender, RoutedEventArgs e)
    {
        Settings.Current.CurseForgeApiKey = (CurseForgeKeyBox.Text ?? "").Trim();
        Save();
        MessageBox.Show(string.IsNullOrEmpty(Settings.Current.CurseForgeApiKey)
            ? "已清空 API Key，CurseForge 将使用 MCIM 镜像通道。"
            : "已保存 API Key，CurseForge 将优先使用官方接口。", "CurseForge");
    }

    /// <summary>打开开源版本的 API 凭据填写窗口（微软客户端 ID / CurseForge Key）。</summary>
    private void OpenApiKeys_Click(object sender, RoutedEventArgs e)
    {
        var window = new ApiKeysWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
        // 窗口里保存的 CurseForge Key 会同步到设置里，这里刷新输入框显示
        CurseForgeKeyBox.Text = Settings.Current.CurseForgeApiKey;
    }

    private async void UpdateNameTable_Click(object sender, RoutedEventArgs e)
    {
        NameTableHint.Text = "正在更新模组中文名表…";
        var ok = await AuroraLauncher.Core.Mods.ChineseNameTable.UpdateFromUrlAsync(
            AuroraLauncher.Core.Mods.ChineseNameTable.UpdateUrl);
        NameTableHint.Text = ok
            ? $"模组中文名表：{AuroraLauncher.Core.Mods.ChineseNameTable.Count} 条（已更新）"
            : "模组中文名表更新失败（可稍后重试）";
    }

    private void UpdateMusicStatus()
    {
        var music = MusicService.Instance;
        MusicStatus.Text = music.Playlist.Count == 0
            ? $"曲目：0 首（将音频放入 {Settings.Current.EffectiveMusicFolder} 后点「重新扫描」）"
            : $"曲目：{music.Playlist.Count} 首 · 当前：{music.CurrentTrack}";
    }

    // ---------------- 游戏 ----------------

    private void AddGameFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择 .minecraft 游戏目录" };
        if (dialog.ShowDialog() != true) return;
        Settings.Current.AddGameFolder(dialog.FolderName);
        Save();
        LoadFromSettings();
        Logger.Info("已添加游戏目录：" + dialog.FolderName);
    }

    private void RemoveGameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is not string dir) return;
        if (MessageBox.Show($"确定不再管理该目录吗？\n{dir}\n（不会删除目录中的文件）", "移除目录",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Settings.Current.RemoveGameFolder(dir);
        Save();
        LoadFromSettings();
    }

    private void SetPrimaryFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is not string dir) return;
        Settings.Current.SetPrimaryGameFolder(dir);
        Save();
        LoadFromSettings();
    }

    private void Mirror_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.DownloadMirror = MirrorOfficial.IsChecked == true ? "official" : "auto";
        McApi.ResetCache();
        Save();
    }

    private void ModPageSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || ModPageSizeValue == null) return;
        var size = (int)Math.Clamp(ModPageSizeSlider.Value, 10, 25);
        Settings.Current.ModPageSize = size;
        ModPageSizeValue.Text = size + " 个";
        Save();
    }

    private void MultiThread_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.MultiThreadDownload = MultiThreadChk.IsChecked == true;
        Save();
    }

    private void Threads_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || ThreadsValue == null) return;
        var threads = (int)Math.Clamp(ThreadsSlider.Value, 2, 16);
        Settings.Current.DownloadThreads = threads;
        ThreadsValue.Text = threads + " 线程";
        Save();
    }

    private void RescanJava_Click(object sender, RoutedEventArgs e)
    {
        LoadJavaList();
        JavaHint.Text = $"重新检测完成，共 {_javas.Count} 个 Java";
    }

    private void PickJava_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 java.exe",
            Filter = "java.exe|java.exe|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        var info = JavaFinder.Probe(dialog.FileName);
        if (info == null)
        {
            MessageBox.Show("无法识别该 java.exe 的版本，请确认路径正确。", "提示");
            return;
        }
        Settings.Current.JavaPath = dialog.FileName;
        Save();
        JavaHint.Text = "已指定：" + info;
    }

    private void JavaList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var index = JavaList.SelectedIndex;
        if (index < 0 || index >= _javas.Count) return;
        Settings.Current.JavaPath = _javas[index].Path;
        JavaHint.Text = "已选择：" + _javas[index];
        Save();
    }

    private void Memory_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || MemoryValue == null) return;
        var gb = (int)MemorySlider.Value;
        Settings.Current.MemoryMb = gb * 1024;
        MemoryValue.Text = gb + " G";
        Save();
    }

    private void PlayerName_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.PlayerName = string.IsNullOrWhiteSpace(PlayerNameBox.Text) ? "Steve" : PlayerNameBox.Text.Trim();
        Save();
    }

    private void Resolution_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        if (int.TryParse(WidthBox.Text, out var w) && w >= 640) Settings.Current.WindowWidth = w;
        if (int.TryParse(HeightBox.Text, out var h) && h >= 480) Settings.Current.WindowHeight = h;
        Save();
    }

    private void Fullscreen_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.Fullscreen = FullscreenChk.IsChecked == true;
        Save();
    }

    private void Isolation_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.VersionIsolation = IsolationChk.IsChecked == true;
        Save();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Save();
        MessageBox.Show("设置已保存。", "MC Tool Launcher");
    }
}
