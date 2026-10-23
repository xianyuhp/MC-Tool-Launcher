using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using MCTL.Core;
using MCTL.Views;

namespace MCTL;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = new();
    private string _lastMusicFolder;
    private string _appearanceKey;
    private bool _rainbowActive;
    private bool _loadingVolume;

    public MainWindow()
    {
        Settings.Load();
        ApiKeys.Load();
        AppLibrary.Load();
        Logger.Info("MC Tool Launcher (MCTL) 启动");

        InitializeComponent();
        // 还原上次退出前选择的版本（主页与下载页都会用到）
        AppState.SetSelectedVersion(Settings.Current.SelectedVersionId, Settings.Current.SelectedVersionDir);
        VersionTag.Text = "v" + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
        SourceInitialized += (_, _) => ApplyAppearance();
        Loaded += (_, _) =>
        {
            ApplyAppearance();
            InitMusic();
            PlayWindowFadeIn();
            if (PageHost.Content == null) Navigate("NavHome");
            Logger.Info($"API 凭据：微软={ApiKeys.MicrosoftReady} CurseForge={ApiKeys.CurseForgeReady} 需要配置={ApiKeys.NeedsSetup}");
            if (ApiKeys.NeedsSetup) _ = PromptApiKeysAsync();
        };
        Closed += (_, _) =>
        {
            Settings.Save();
            MusicService.Instance.Stop();
        };

        Settings.Changed += OnSettingsChanged;
        MusicService.Instance.TrackChanged += UpdateMusicUi;
        MusicService.Instance.StateChanged += UpdateMusicUi;

        // 拖入 .mrpack / .zip 整合包即可导入
        AllowDrop = true;
        DragOver += OnDragOver;
        Drop += OnFilesDropped;

        _loadingVolume = true;
        VolumeMini.Value = Settings.Current.MusicVolume;
        _loadingVolume = false;
        UpdateMusicUi();
        Navigate("NavHome");
    }


    public void ApplyAppearance()
    {
        var settings = Settings.Current;
        _appearanceKey = AppearanceKey(settings);

        ApplyTheme(settings);

        var accent = ParseColor(settings.AccentColor);
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush(accent);
        Application.Current.Resources["AccentSoftBrush"] =
            new SolidColorBrush(Color.FromArgb(0x3A, accent.R, accent.G, accent.B));

        // 背景图与玻璃面板
        var glass = settings.AcrylicEnabled;
        var intensity = Math.Clamp(settings.AcrylicOpacity / 100.0, 0, 1);
        var light = IsLightTheme(settings);

        var image = ImageCache.Load(settings.BackgroundImage);
        BgImage.Source = image;
        BgImage.Opacity = settings.BackgroundOpacity / 100.0;
        BgImage.Effect = settings.BackgroundBlur > 0
            ? new BlurEffect { Radius = settings.BackgroundBlur, KernelType = KernelType.Gaussian }
            : null;

        var baseColor = ParseColor(settings.AccentColor);
        if (glass)
        {
            // 玻璃面板：亚克力浓度直接控制面板不透明度（0% 近乎全透，100% 接近不透明）
            // 白天模式的底色更接近柔和的珍珠白，且最低不透明度更高，避免文字压在背景图上难辨认
            var c1 = MixColor(light ? Color.FromRgb(252, 253, 255) : Color.FromRgb(10, 16, 32), baseColor, light ? 0.10 : 0.22);
            var c2 = MixColor(light ? Color.FromRgb(240, 243, 249) : Color.FromRgb(10, 16, 32), baseColor, light ? 0.18 : 0.32);
            var maxAlpha = light ? 0xF4 : 0xDA;
            var minAlpha = light ? 0x8C : 0x18;
            byte Alpha(double k) => (byte)Math.Clamp(minAlpha + (maxAlpha - minAlpha) * k, 0, 255);
            FrostLayer.Background = new LinearGradientBrush(
                Color.FromArgb(Alpha(intensity), c1.R, c1.G, c1.B),
                Color.FromArgb(Alpha(intensity * 0.82), c2.R, c2.G, c2.B), 90);
        }
        else
        {
            // 关闭透明：完全不透明的纯色面板
            var solid = MixColor(light ? Color.FromRgb(246, 247, 250) : Color.FromRgb(12, 18, 32), baseColor, light ? 0.28 : 0.30);
            FrostLayer.Background = new SolidColorBrush(solid);
        }

        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        // 组件透明度：玻璃浓度越低，卡片也一起变得更透明
        var glassFactor = glass ? 0.45 + 0.55 * intensity : 1.0;
        var cardAlpha = (byte)Math.Clamp(0x26 * settings.CardOpacity / 100 * glassFactor, 0, 255);
        if (IsLightTheme(settings))
            cardAlpha = (byte)Math.Clamp(0xE8 * settings.CardOpacity / 100 * glassFactor, 0xA6, 0xF2);
        Application.Current.Resources["CardBrush"] = new SolidColorBrush(Color.FromArgb(cardAlpha, 255, 255, 255));
        var lineAlpha = (byte)Math.Clamp(0x22 * settings.CardOpacity / 100, 0, 255);
        Application.Current.Resources["LineBrush"] = IsLightTheme(settings)
            ? new SolidColorBrush(Color.FromArgb(lineAlpha, 0, 0, 0))
            : new SolidColorBrush(Color.FromArgb(lineAlpha, 255, 255, 255));

        var lightTheme = IsLightTheme(settings);
        // 白天模式的侧栏 / 标题栏用白色薄纱，避免叠成灰扑扑的一片导致文字对比度不足
        TitleBarLayer.Background = lightTheme
            ? new SolidColorBrush(Color.FromArgb(
                (byte)Math.Clamp(0x46 * settings.TitleBarOpacity / 100, 0, 255), 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(
                (byte)Math.Clamp(0x14 * settings.TitleBarOpacity / 100, 0, 255), 255, 255, 255));
        SidebarLayer.Background = lightTheme
            ? new SolidColorBrush(Color.FromArgb(
                (byte)Math.Clamp(0x52 * settings.SidebarOpacity / 100, 0, 255), 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(
                (byte)Math.Clamp(0x14 * settings.SidebarOpacity / 100, 0, 255), 255, 255, 255));
        PageHost.Opacity = Math.Clamp(settings.ContentOpacity / 100.0, 0.2, 1.0);

        AcrylicHelper.Apply(this, settings.AcrylicEnabled,
            settings.AcrylicOpacity, Color.FromRgb(14, 20, 34));
    }

    /// <summary>开源版本未内置 API 凭据时，启动后提示使用者自己申请并填写。</summary>
    private async Task PromptApiKeysAsync()
    {
        await Task.Delay(700);
        if (!ApiKeys.NeedsSetup) return;
        try
        {
            new ApiKeysWindow { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            Logger.Warn("打开 API 凭据窗口失败：" + ex.Message);
        }
    }

    private static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Color.FromRgb(0x5B, 0x8C, 0xFF); }
    }

    private static Color MixColor(Color background, Color accent, double ratio)
    {
        byte Mix(byte a, byte b) => (byte)Math.Clamp(a * (1 - ratio) + b * ratio, 0, 255);
        return Color.FromRgb(Mix(background.R, accent.R), Mix(background.G, accent.G), Mix(background.B, accent.B));
    }

    /// <summary>是否使用浅色主题。</summary>
    private static bool IsLightTheme(Settings settings) => settings.ThemeMode == "light";

    private void ApplyTheme(Settings settings)
    {
        var light = IsLightTheme(settings);
        ThemeBtn.Content = light ? "☀" : "🌙";
        var res = Application.Current.Resources;
        // 玻璃面板越透明，副文字越接近纯色，保证可读
        var contrast = settings.AcrylicEnabled
            ? Math.Clamp((100 - settings.AcrylicOpacity) / 70.0, 0, 1)
            : 0;
        var subAlpha = light
            ? (byte)Math.Clamp(0xBC + contrast * 0x43, 0xBC, 0xFF)
            : (byte)Math.Clamp(0x9F + contrast * 0x60, 0x9F, 0xFF);
        res["TextBrush"] = new SolidColorBrush(light ? Color.FromRgb(0x22, 0x26, 0x2E) : Colors.White);
        // 白天模式用偏冷的深灰蓝而不是纯黑，柔和但对比更高（玻璃面板下也不会发灰）
        res["SubTextBrush"] = new SolidColorBrush(light
            ? Color.FromArgb(subAlpha, 0x3C, 0x43, 0x4F)
            : Color.FromArgb(subAlpha, 255, 255, 255));
        res["LineBrush"] = new SolidColorBrush(light
            ? Color.FromArgb(0x22, 0, 0, 0)
            : Color.FromArgb(0x22, 255, 255, 255));
        res["NavHoverBrush"] = new SolidColorBrush(light
            ? Color.FromArgb(0x12, 0, 0, 0)
            : Color.FromArgb(0x1F, 255, 255, 255));
        res["ChipBrush"] = new SolidColorBrush(light
            ? Color.FromArgb(0x10, 0, 0, 0)
            : Color.FromArgb(0x1F, 255, 255, 255));
        res["ChipHoverBrush"] = new SolidColorBrush(light
            ? Color.FromArgb(0x1C, 0, 0, 0)
            : Color.FromArgb(0x2F, 255, 255, 255));
        res["ItemHoverBrush"] = new SolidColorBrush(light
            ? Color.FromArgb(0x10, 0, 0, 0)
            : Color.FromArgb(0x1F, 255, 255, 255));
        res["InputBrush"] = new SolidColorBrush(light
            ? Color.FromArgb(0x0F, 0, 0, 0)
            : Color.FromArgb(0x1F, 255, 255, 255));
        res["CardHoverBrush"] = new SolidColorBrush(light
            ? Color.FromArgb(0x1A, 0, 0, 0)
            : Color.FromArgb(0x26, 255, 255, 255));
        res["PopupBrush"] = new SolidColorBrush(light
            ? Color.FromRgb(0xF7, 0xF8, 0xFB)
            : Color.FromRgb(0x1A, 0x20, 0x30));
        res["MenuBrush"] = new SolidColorBrush(light
            ? Color.FromArgb(0xF2, 0xF7, 0xF8, 0xFB)
            : Color.FromArgb(0xF2, 0x18, 0x1D, 0x28));
        res["WindowBgBrush"] = new SolidColorBrush(light
            ? Color.FromArgb(0xF2, 247, 248, 251)
            : Color.FromArgb(0xF2, 0x18, 0x1D, 0x28));
        res["WindowBorderBrush"] = new SolidColorBrush(light
            ? Color.FromArgb(0x22, 0, 0, 0)
            : Color.FromArgb(0x33, 255, 255, 255));
        res["PriceBrush"] = new SolidColorBrush(light
            ? Color.FromRgb(0x1A, 0x1A, 0x1A)
            : Color.FromRgb(0xFF, 0xC4, 0x6B));
        res["HeadingBrush"] = new SolidColorBrush(light ? Color.FromRgb(0x1B, 0x1B, 0x1F) : Colors.White);
        if (_rainbowActive) ApplyRainbowBrush();
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        Settings.Current.ThemeMode = IsLightTheme(Settings.Current) ? "dark" : "light";
        Settings.Save();
        UpdateMusicUi();
    }

    private static string AppearanceKey(Settings s) =>
        $"{s.AcrylicEnabled}|{s.AcrylicOpacity}|{s.BackgroundImage}|{s.BackgroundOpacity}|{s.BackgroundBlur}|{s.AccentColor}" +
        $"|{s.CardOpacity}|{s.SidebarOpacity}|{s.TitleBarOpacity}|{s.ContentOpacity}|{s.ThemeMode}";

    private void OnSettingsChanged()
    {
        if (_appearanceKey != AppearanceKey(Settings.Current)) ApplyAppearance();
        MusicService.Instance.ApplyVolume();
        _loadingVolume = true;
        if (VolumeMini != null) VolumeMini.Value = Settings.Current.MusicVolume;
        _loadingVolume = false;
        if (_lastMusicFolder != Settings.Current.EffectiveMusicFolder)
        {
            _lastMusicFolder = Settings.Current.EffectiveMusicFolder;
            MusicService.Instance.Reload();
        }
        UpdateMusicUi();
    }


    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFilesDropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        var pack = files.FirstOrDefault(f =>
            f.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (pack == null)
        {
            MessageBox.Show("请拖入 .mrpack 或 .zip 整合包文件。", "导入整合包");
            return;
        }

        var window = new ModpackImportWindow(pack, name =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                NavHome.IsChecked = true;
                if (_pages.TryGetValue("NavHome", out var page) && page is IRefreshable refreshable)
                    refreshable.Refresh();
            });
        })
        {
            Owner = this
        };
        window.ShowDialog();
    }


    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || PageHost == null) return;
        Navigate(((RadioButton)sender).Name);
    }

    private void Navigate(string navName)
    {
        if (!_pages.TryGetValue(navName, out var page))
        {
            page = navName switch
            {
                "NavHome" => new HomeView(),
                "NavDownload" => new DownloadView(),
                "NavLibrary" => new LibraryView(),
                "NavSteam" => new SteamView(),
                "NavToolbox" => new ToolboxView(),
                "NavColor" => new ColorFontView(),
                "NavCommand" => new CommandView(),
                "NavServer" => new ServerHelperView(),
                "NavSettings" => new SettingsView(),
                _ => new HomeView()
            };
            _pages[navName] = page;
        }
        PageHost.Content = page;
        AnimatePageSwitch();
        if (page is IRefreshable refreshable) refreshable.Refresh();
    }

    private void PlayWindowFadeIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = ease
        });
    }

    /// <summary>页面切换的淡入 + 位移 + 轻微缩放。</summary>
    private void AnimatePageSwitch()
    {
        if (PageHost == null) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var group = new TransformGroup();
        var translate = new TranslateTransform(0, 22);
        var scale = new ScaleTransform(0.985, 0.985);
        group.Children.Add(scale);
        group.Children.Add(translate);
        PageHost.RenderTransform = group;
        PageHost.RenderTransformOrigin = new Point(0.5, 0.5);

        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = ease
        });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
    }


    private void InitMusic()
    {
        _lastMusicFolder = Settings.Current.EffectiveMusicFolder;
        MusicService.Instance.ApplyVolume();
        MusicService.Instance.Reload();
        if (Settings.Current.MusicAutoPlay && MusicService.Instance.Playlist.Count > 0)
            MusicService.Instance.PlayIndex(0);
        UpdateMusicUi();
    }

    private void UpdateMusicUi()
    {
        var track = MusicService.Instance.CurrentTrack;
        if (TrackText != null)
        {
            TrackText.Text = track;
            TrackText.ToolTip = track;
            UpdateMarquee(track);
        }
        if (BtnPlay != null)
            BtnPlay.Content = MusicService.Instance.IsPlaying ? "⏸" : "▶";
        UpdateRainbowState();
    }

    /// <summary>歌曲名过长时原地左右滚动。</summary>
    private void UpdateMarquee(string track)
    {
        if (TrackShift == null || TrackText == null) return;
        TrackShift.BeginAnimation(TranslateTransform.XProperty, null);
        TrackShift.X = 0;
        if (string.IsNullOrEmpty(track)) return;

        var typeface = new Typeface(TrackText.FontFamily, TrackText.FontStyle,
            TrackText.FontWeight, TrackText.FontStretch);
        var formatted = new FormattedText(track, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, TrackText.FontSize, Brushes.White, 1.0);
        var distance = formatted.WidthIncludingTrailingWhitespace - 166;
        if (distance <= 4) return;

        var seconds = Math.Max(5, distance / 16.0);
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(seconds),
            RepeatBehavior = RepeatBehavior.Forever
        };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-distance, KeyTime.FromPercent(0.45)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-distance, KeyTime.FromPercent(0.55)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        TrackShift.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private void VolumeMini_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingVolume || VolumeMini == null) return;
        Settings.Current.MusicVolume = (int)VolumeMini.Value;
        MusicService.Instance.ApplyVolume();
        Settings.Save();
    }


    /// <summary>播放音乐且开启开关时，标题文字改为循环流动的彩色渐变。</summary>
    private void UpdateRainbowState()
    {
        var enabled = Settings.Current.RainbowTitles && MusicService.Instance.IsPlaying;
        if (enabled == _rainbowActive) return;

        _rainbowActive = enabled;
        if (enabled)
        {
            ApplyRainbowBrush();
        }
        else
        {
            var light = IsLightTheme(Settings.Current);
            Application.Current.Resources["HeadingBrush"] =
                new SolidColorBrush(light ? Color.FromRgb(0x1B, 0x1B, 0x1F) : Colors.White);
        }
    }

    /// <summary>用循环平铺的渐变 + 位移动画，实现无缝滚动的彩虹字。</summary>
    private void ApplyRainbowBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            SpreadMethod = GradientSpreadMethod.Repeat
        };
        for (var i = 0; i <= 6; i++)
            brush.GradientStops.Add(new GradientStop(FromHsv(i * 60), i / 6.0));

        var shift = new TranslateTransform(0, 0);
        brush.RelativeTransform = shift;
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(4))
        {
            RepeatBehavior = RepeatBehavior.Forever
        });

        Application.Current.Resources["HeadingBrush"] = brush;
    }

    private static Color FromHsv(double hue, double saturation = 0.72, double value = 1.0)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - c;
        (double r, double g, double b) = hue switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => MusicService.Instance.Previous();
    private void Next_Click(object sender, RoutedEventArgs e) => MusicService.Instance.Next();
    private void PlayPause_Click(object sender, RoutedEventArgs e) => MusicService.Instance.PlayPause();


    /// <summary>Esc：关闭主页上的版本列表 / 模组管理浮层。</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (PageHost.Content is HomeView home && home.TryCloseOverlays())
            e.Handled = true;
    }

    /// <summary>供页面内按钮跳转（同步左侧导航选中状态）。</summary>
    public void NavigateTo(string navName)
    {
        if (FindName(navName) is RadioButton radio) radio.IsChecked = true;
        else Navigate(navName);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Max_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
