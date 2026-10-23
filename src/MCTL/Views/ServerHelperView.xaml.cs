using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MCTL.Core;
using MCTL.Core.Mc;
using MCTL.Core.Net;

namespace MCTL.Views;

/// <summary>开服：服务端核心下载、Modrinth 插件、可视化配置与后台启动。</summary>
public partial class ServerHelperView : UserControl, IRefreshable
{
    private static readonly HttpClient IconClient = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static int PageSize => Math.Clamp(Settings.Current.ModPageSize, 5, 25);

    private List<McVersion> _versions = new();
    private string _core = "vanilla";
    /// <summary>服务端保存根目录（下载时可选，弹窗里自定义实例名）。</summary>
    private string _serverRoot = "";
    /// <summary>本次下载 / 安装的服务端实例目录。</summary>
    private string _serverDir = "";
    private string _serverMc = "";
    private bool _customServerDir;
    private CancellationTokenSource _cts;
    private bool _busy;

    private readonly List<ModItem> _plugins = new();
    private int _pluginPage;
    private int _pluginRequest;
    private string _pluginSearch = "";
    private string _pluginCategory;
    private string _pluginServer = "";
    private string _pluginVersion = "";
    private bool _suppressPluginFilter;
    private int _pluginTotal;

    private List<ServerInstance> _instances = new();
    private ServerRunner _runner;
    private ServerConsoleWindow _consoleWindow;
    private List<JavaInfo> _javas = new();
    private string _propertyPath = "";
    private readonly Dictionary<string, Func<string>> _propertyGetters = new();

    private static readonly (string Key, string Label, string Kind, string[] Options)[] PropertySchema =
    {
        ("motd", "服务器描述", "text", null),
        ("server-port", "端口", "number", null),
        ("max-players", "最大玩家数", "number", null),
        ("gamemode", "游戏模式", "enum", new[] { "survival", "creative", "adventure", "spectator" }),
        ("difficulty", "难度", "enum", new[] { "peaceful", "easy", "normal", "hard" }),
        ("online-mode", "正版验证", "bool", null),
        ("pvp", "允许 PVP", "bool", null),
        ("level-name", "世界名称", "text", null),
        ("level-seed", "世界种子", "text", null),
        ("view-distance", "视距", "number", null),
        ("simulation-distance", "模拟距离", "number", null),
        ("spawn-protection", "出生点保护", "number", null),
        ("white-list", "白名单", "bool", null),
        ("enforce-whitelist", "强制白名单", "bool", null),
        ("allow-flight", "允许飞行", "bool", null),
        ("allow-nether", "允许下界", "bool", null),
        ("hardcore", "极限模式", "bool", null),
        ("enable-command-block", "命令方块", "bool", null),
        ("max-world-size", "世界边界", "number", null),
        ("enable-status", "显示在线状态", "bool", null),
        ("hide-online-players", "隐藏玩家列表", "bool", null),
        ("enforce-secure-profile", "强制安全配置", "bool", null),
        ("player-idle-timeout", "挂机踢出（分钟）", "number", null),
        ("prevent-proxy-connections", "防止代理连接", "bool", null),
        ("network-compression-threshold", "网络压缩阈值", "number", null),
        ("max-tick-time", "最大 tick 时间", "number", null),
        ("enable-query", "启用查询", "bool", null),
        ("query.port", "查询端口", "number", null),
        ("enable-rcon", "启用 RCON", "bool", null),
        ("rcon.port", "RCON 端口", "number", null),
        ("rcon.password", "RCON 密码", "text", null),
        ("resource-pack", "资源包地址", "text", null),
        ("resource-pack-sha1", "资源包 SHA1", "text", null),
        ("require-resource-pack", "强制资源包", "bool", null),
        ("generate-structures", "生成结构", "bool", null),
        ("spawn-monsters", "生成怪物", "bool", null),
        ("spawn-animals", "生成动物", "bool", null),
        ("spawn-npcs", "生成 NPC", "bool", null),
        ("force-gamemode", "强制游戏模式", "bool", null),
        ("op-permission-level", "OP 权限等级", "enum", new[] { "1", "2", "3", "4" }),
        ("function-permission-level", "函数权限等级", "enum", new[] { "1", "2", "3", "4" }),
        ("log-ips", "记录玩家 IP", "bool", null),
        ("use-native-transport", "原生传输优化", "bool", null),
        ("sync-chunk-writes", "同步区块写入", "bool", null),
        ("broadcast-console-to-ops", "同步控制台给 OP", "bool", null),
        ("rate-limit", "数据包速率限制", "number", null)
    };

    public ServerHelperView()
    {
        InitializeComponent();
        BuildCoreChips();
        BuildPluginFilters();
        _serverDir = ServerInstances.Root;
        ServerDirText.Text = _serverDir;
    }

    public void Refresh()
    {
        if (_versions.Count == 0) _ = LoadVersionsAsync();
        if (_plugins.Count == 0) _ = LoadPluginsAsync(true);
        if (RunTab.Visibility == Visibility.Visible)
        {
            BuildServerJavaCombo();
            RefreshInstances();
        }
    }

    private void ServerTab_Changed(object sender, RoutedEventArgs e)
    {
        if (DownloadTab == null || RunTab == null) return;
        var run = TabRun.IsChecked == true;
        DownloadTab.Visibility = run ? Visibility.Collapsed : Visibility.Visible;
        RunTab.Visibility = run ? Visibility.Visible : Visibility.Collapsed;
        UiAnim.ExpandIn(run ? RunTab : DownloadTab, 8, 200);
        if (run)
        {
            BuildServerJavaCombo();
            RefreshInstances();
        }
    }


    private void BuildCoreChips()
    {
        CorePanel.Children.Clear();
        foreach (var (value, label, _) in ServerCores.Cores)
        {
            var chip = new ToggleButton
            {
                Style = (Style)FindResource("ChipToggle"),
                Content = label,
                IsChecked = value == _core,
                FontSize = 11
            };
            var captured = value;
            chip.Checked += (_, _) =>
            {
                foreach (var child in CorePanel.Children.OfType<ToggleButton>())
                    if (!ReferenceEquals(child, chip)) child.IsChecked = false;
                if (_core == captured && CoreVersionCombo.Items.Count > 0) return;
                _core = captured;
                _ = LoadCoreVersionsAsync();
            };
            chip.Unchecked += (_, _) =>
            {
                if (CorePanel.Children.OfType<ToggleButton>().All(t => t.IsChecked != true))
                    chip.IsChecked = true;
            };
            CorePanel.Children.Add(chip);
        }
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            StatusText.Text = "正在获取 Minecraft 版本清单…";
            _versions = await McApi.GetVersionsAsync(true);
            var releases = _versions.Where(v => v.Type == "release").Select(v => v.Id).ToList();

            _suppressPluginFilter = true;
            PluginVersionCombo.Items.Clear();
            PluginVersionCombo.Items.Add("全部版本");
            foreach (var id in releases.Take(120)) PluginVersionCombo.Items.Add(id);
            PluginVersionCombo.SelectedIndex = 0;
            _suppressPluginFilter = false;

            StatusText.Text = $"版本清单已加载（{(McApi.UsingMirror ? "镜像" : "官方")}）。";
            await LoadCoreVersionsAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = "版本清单获取失败：" + ex.Message;
            Logger.Warn("开服版本清单失败：" + ex.Message);
        }
    }

    /// <summary>根据所选核心加载它支持的版本（Arclight / Mohist 等来自各自官网）。</summary>
    private async Task LoadCoreVersionsAsync()
    {
        var core = _core;
        CoreVersionHint.Text = $"正在获取 {CoreLabel(core)} 支持的版本…";
        try
        {
            var versions = await ServerCores.GetSupportedVersionsAsync(core);
            if (core != _core) return; // 期间切换了核心

            CoreVersionCombo.Items.Clear();
            foreach (var version in versions) CoreVersionCombo.Items.Add(version);
            if (CoreVersionCombo.Items.Count > 0) CoreVersionCombo.SelectedIndex = 0;

            var source = core is "arclight" or "mohist" ? "官网" : "官方接口";
            CoreVersionHint.Text = versions.Count == 0
                ? $"{CoreLabel(core)} 暂未返回可用版本"
                : $"{CoreLabel(core)} 支持 {versions.Count} 个版本（{source}）· 下载源优先官方";
            if (TabDownload.IsChecked == true) StatusText.Text = CoreVersionHint.Text;
            UpdateDefaultServerDir();
        }
        catch (Exception ex)
        {
            CoreVersionHint.Text = $"获取 {CoreLabel(core)} 版本失败：{ex.Message}";
            Logger.Warn("核心版本获取失败：" + ex.Message);
        }
    }

    private void CoreVersion_Changed(object sender, SelectionChangedEventArgs e) => UpdateDefaultServerDir();

    /// <summary>默认根目录：启动器目录/server/；用户选过根目录后一直用它。</summary>
    private void UpdateDefaultServerDir()
    {
        if (_customServerDir) return;
        _serverRoot = Settings.Current.EffectiveServerRoot;
        if (ServerDirText != null) ServerDirText.Text = _serverRoot;
    }

    private void PickServerDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择服务端保存根目录",
            InitialDirectory = Directory.Exists(_serverRoot) ? _serverRoot : Settings.Current.EffectiveServerRoot
        };
        if (dialog.ShowDialog() != true) return;
        _serverRoot = dialog.FolderName;
        _customServerDir = true;
        ServerDirText.Text = _serverRoot;
    }

    private void OpenServerDir_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(!string.IsNullOrEmpty(_serverDir) ? _serverDir : _serverRoot);

    private void OpenServerRoot_Click(object sender, RoutedEventArgs e) => OpenFolder(ServerInstances.Root);

    private void OpenPluginsDir_Click(object sender, RoutedEventArgs e)
    {
        var dir = SelectedInstance() is { } instance
            ? Path.Combine(instance.Dir, "plugins")
            : Path.Combine(_serverDir, "plugins");
        OpenFolder(dir);
    }

    private static void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", dir)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "打开失败");
        }
    }

    private string CoreLabel(string core) => ServerCores.CoreInfo(core).Label;

    /// <summary>默认实例名，例如 Paper-1.21.1 / NeoForge-26.3。</summary>
    private string SuggestServerName(string mc)
    {
        var name = CoreLabel(_core) switch
        {
            "原版 Vanilla" => "Vanilla",
            "Arclight 混合端" => "Arclight",
            "Mohist 混合端" => "Mohist",
            var other => other.Replace(" ", "")
        };
        return McInstaller.SanitizeVersionName($"{name}-{mc}");
    }

    private async void DownloadServer_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var mc = CoreVersionCombo.SelectedItem as string;
        if (string.IsNullOrEmpty(mc))
        {
            MessageBox.Show("请先选择游戏版本。", "提示");
            return;
        }

        try
        {
            UpdateDefaultServerDir();
            StatusText.Text = $"正在获取 {CoreLabel(_core)} 的可用构建…";
            var builds = await ServerCores.GetBuildsAsync(_core, mc);
            if (builds.Count == 0)
            {
                StatusText.Text = "没有可用的构建。";
                return;
            }

            var build = builds[0];
            if (builds.Count > 1)
            {
                var picker = new LoaderPickerWindow($"{CoreLabel(_core)} · {mc}", "选择要下载的服务端构建",
                    builds.Select(b => (b.Value, b.Label)))
                {
                    Owner = Window.GetWindow(this)
                };
                if (picker.ShowDialog() != true) return;
                build = builds.FirstOrDefault(b => b.Value == picker.SelectedVersion) ?? builds[0];
            }

            // 与其他下载一致：先选保存位置，再自定义实例文件夹名
            var dialog = new VersionNameDialog($"{CoreLabel(_core)} {mc}", SuggestServerName(mc),
                string.IsNullOrEmpty(_serverRoot) ? Settings.Current.EffectiveServerRoot : _serverRoot,
                serverMode: true)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() != true) return;

            _serverRoot = dialog.SelectedDir;
            _customServerDir = true;
            _serverDir = Path.Combine(_serverRoot, dialog.VersionName);
            _serverMc = mc;
            ServerDirText.Text = _serverDir;
            Directory.CreateDirectory(_serverDir);
            await DownloadBuildAsync(build);
            RefreshInstances();
        }
        catch (Exception ex)
        {
            StatusText.Text = "获取构建失败：" + ex.Message;
            MessageBox.Show(ex.Message, "下载服务端", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task DownloadBuildAsync(ServerBuild build)
    {
        _cts = new CancellationTokenSource();
        SetBusy(true);
        Bar.Value = 0;
        PercentText.Text = "0%";
        InstallLog.Clear();
        StatusText.Text = "准备下载…";
        ServerProgressHost.Visibility = Visibility.Visible;

        var completed = false;
        try
        {
            var target = Path.Combine(_serverDir, build.FileName);
            AppendLog($"下载 {build.Label}");
            if (!string.IsNullOrWhiteSpace(build.Note)) AppendLog(build.Note);

            var progress = new Progress<double>(p =>
            {
                Bar.Value = p;
                PercentText.Text = $"{p:0}%";
            });
            await McApi.DownloadAsync(build.Url, target, "", _cts.Token, progress);
            AppendLog("已保存到 " + target);

            // 校验文件：jar 必须是 zip 结构，避免把网页 / 错误内容当成服务端
            if (build.FileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
                !LooksLikeJar(target))
            {
                try { File.Delete(target); } catch { }
                throw new Exception("下载到的文件不是有效的 jar（可能是镜像 / 链接失效或被网络拦截），请重试或更换核心。");
            }

            if (build.FileName.Contains("installer", StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = "正在运行服务端安装器（首次安装需要几分钟）…";
                AppendLog("检测到安装器，自动执行 --installServer …");
                var ok = await RunInstallerAsync(target);
                if (!ok)
                {
                    if (_core == "neoforge" ||
                        build.FileName.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(Window.GetWindow(this),
                            "NeoForge 服务端安装器未能完成。启动器已用镜像预先下载安装器依赖，\n" +
                            "若仍失败，通常是安装器自身联网被拦截（maven.neoforged.net）。\n\n" +
                            "可查看下方日志的原始错误，或到 https://neoforged.net/ 手动下载安装器后重试，也可改用 Forge 核心。",
                            "NeoForge 安装未完成", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    throw new Exception("服务端安装器未成功完成，请打开后台日志查看原因。");
                }
                AppendLog("安装器执行完成，已生成服务端文件。");
            }

            WriteStartScript(Path.GetDirectoryName(target), Path.GetFileName(target));
            StatusText.Text = "下载完成：" + target;
            completed = true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消下载。";
            AppendLog("已取消下载。");
        }
        catch (Exception ex)
        {
            StatusText.Text = "下载失败：" + ex.Message;
            AppendLog("下载失败：" + ex.Message);
            Logger.Warn("服务端下载失败：" + ex.Message);
        }
        finally
        {
            if (!completed)
            {
                Bar.Value = 0;
                PercentText.Text = "0%";
            }
            ServerProgressHost.Visibility = Visibility.Collapsed;
            SetBusy(false);
            _cts.Dispose();
            _cts = null;
        }
    }

    private static bool LooksLikeJar(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            if (fs.Length < 4) return false;
            var header = new byte[4];
            fs.ReadExactly(header);
            return header[0] == 0x50 && header[1] == 0x4B;
        }
        catch
        {
            return false;
        }
    }

    private Process _installerProcess;

    /// <summary>运行 Forge / NeoForge 安装器（--installServer），输出实时写入日志。</summary>
    private async Task<bool> RunInstallerAsync(string installerPath)
    {
        // 与服务端版本匹配的 Java（1.20.5+ 需要 Java 21）
        var required = JavaFinder.GuessMajor(_serverMc);
        var java = JavaFinder.PickCompatible(required)?.Path;
        if (string.IsNullOrEmpty(java))
            java = File.Exists(Settings.Current.JavaPath) ? Settings.Current.JavaPath : "java";

        // 安装器只会连官方 maven.neoforged.net，先用镜像把依赖放到 libraries，避免连接超时
        try
        {
            StatusText.Text = "正在补齐安装器依赖（走镜像）…";
            await McInstaller.PrefetchInstallerLibrariesAsync(installerPath, _serverDir,
                new Progress<string>(AppendLog), _cts?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppendLog("依赖预下载失败：" + ex.Message);
        }

        var info = new ProcessStartInfo(java, $"-jar \"{installerPath}\" --installServer")
        {
            WorkingDirectory = _serverDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        AppendLog("使用 Java：" + java);

        try
        {
            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            _installerProcess = process;

            // 安装器输出非常密集：合并刷新，避免界面卡顿
            var pending = new List<string>();
            var lastFlush = DateTime.UtcNow;
            void Collect(string line)
            {
                lock (pending)
                {
                    pending.Add(line);
                    if (pending.Count > 400) pending.RemoveRange(0, pending.Count - 400);
                    if ((DateTime.UtcNow - lastFlush).TotalMilliseconds < 250) return;
                    lastFlush = DateTime.UtcNow;
                    var text = string.Join(Environment.NewLine, pending);
                    pending.Clear();
                    Dispatcher.BeginInvoke(() => AppendLog(text));
                }
            }

            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Collect(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Collect(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            while (!process.HasExited)
            {
                if (_cts?.IsCancellationRequested == true)
                {
                    try { process.Kill(true); } catch { }
                    throw new OperationCanceledException();
                }
                await Task.Delay(300);
            }

            lock (pending)
            {
                if (pending.Count > 0)
                {
                    var text = string.Join(Environment.NewLine, pending);
                    pending.Clear();
                    Dispatcher.BeginInvoke(() => AppendLog(text));
                }
            }
            return process.ExitCode == 0 || File.Exists(Path.Combine(_serverDir, "run.bat"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppendLog("安装器启动失败：" + ex.Message);
            return false;
        }
        finally
        {
            _installerProcess = null;
        }
    }

    private void WriteStartScript(string dir, string jarName)
    {
        try
        {
            var java = File.Exists(Settings.Current.JavaPath) ? Settings.Current.JavaPath : "java";
            var memory = Math.Max(1024, Settings.Current.MemoryMb);
            var content =
                "@echo off\r\n" +
                "cd /d \"%~dp0\"\r\n" +
                $"\"{java}\" -Xms{Math.Max(512, memory / 2)}M -Xmx{memory}M -jar \"{jarName}\" nogui\r\n" +
                "pause\r\n";
            File.WriteAllText(Path.Combine(dir, "start.bat"), content);
        }
        catch { }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        DownloadServerBtn.IsEnabled = !busy;
        CancelBtn.IsEnabled = busy;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        try { _installerProcess?.Kill(true); } catch { }
    }

    private void AppendLog(string message)
    {
        InstallLog.AppendText(message + Environment.NewLine);
        InstallLog.ScrollToEnd();
    }


    private ServerInstance SelectedInstance() => InstanceList?.SelectedItem as ServerInstance;

    private void RefreshInstances_Click(object sender, RoutedEventArgs e) => RefreshInstances();


    private void View_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void View_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var core = files.FirstOrDefault(f =>
            f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (core == null)
        {
            MessageBox.Show("请拖入服务端核心（.jar 或 .zip）。", "开服");
            return;
        }
        await ImportDroppedCoreAsync(core);
    }

    /// <summary>把拖入的核心解压 / 复制成新的服务端实例，并生成启动脚本。</summary>
    private async Task ImportDroppedCoreAsync(string file)
    {
        try
        {
            var name = DetectCoreName(file);
            var dir = Path.Combine(ServerInstances.Root, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(dir);

            StatusText.Text = "正在导入服务端核心…";
            await Task.Run(() =>
            {
                if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    System.IO.Compression.ZipFile.ExtractToDirectory(file, dir, true);
                else
                    File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), true);
            });

            var jar = Directory.GetFiles(dir, "*.jar", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => !Path.GetFileName(f).Contains("installer", StringComparison.OrdinalIgnoreCase));
            if (jar != null) WriteStartScript(dir, Path.GetFileName(jar));

            RefreshInstances();
            var target = _instances.FirstOrDefault(i => i.Dir.Equals(dir, StringComparison.OrdinalIgnoreCase));
            if (target != null) InstanceList.SelectedItem = target;
            if (TabRun != null) TabRun.IsChecked = true;
            StatusText.Text = jar == null
                ? "已导入核心，但没有找到可启动的 jar，请检查压缩包内容。"
                : $"已导入服务端核心：{name}（可点击「启动服务端」一键开服）";
            AppendLog($"拖入导入服务端：{file} → {dir}");
        }
        catch (Exception ex)
        {
            StatusText.Text = "导入失败：" + ex.Message;
            MessageBox.Show("导入服务端核心失败：" + ex.Message, "开服", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string DetectCoreName(string file)
    {
        var name = Path.GetFileName(file).ToLowerInvariant();
        if (name.Contains("neoforge")) return "NeoForge";
        if (name.Contains("forge")) return "Forge";
        if (name.Contains("fabric")) return "Fabric";
        if (name.Contains("quilt")) return "Quilt";
        if (name.Contains("arclight")) return "Arclight";
        if (name.Contains("mohist")) return "Mohist";
        if (name.Contains("paper")) return "Paper";
        if (name.Contains("purpur")) return "Purpur";
        if (name.Contains("spigot")) return "Spigot";
        if (name.Contains("bukkit")) return "Bukkit";
        if (name.Contains("velocity")) return "Velocity";
        if (name.Contains("bungee")) return "BungeeCord";
        return "Server";
    }

    private void RefreshInstances()
    {
        var selected = SelectedInstance()?.Dir;
        _instances = ServerInstances.Scan();
        InstanceList.Items.Clear();
        foreach (var instance in _instances) InstanceList.Items.Add(instance);

        if (_instances.Count == 0)
        {
            RunHint.Text = "还没有服务端。请先在「下载 / 更新核心」页下载一个服务端核心。";
            PropertyPanel.Children.Clear();
            _propertyPath = "";
            return;
        }

        var index = _instances.FindIndex(i => i.Dir.Equals(selected, StringComparison.OrdinalIgnoreCase));
        InstanceList.SelectedIndex = index >= 0 ? index : 0;
    }

    /// <summary>右键时先选中鼠标下的实例，避免菜单操作到别的实例。</summary>
    private void InstanceList_PreviewRightDown(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(InstanceList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item != null) item.IsSelected = true;
    }

    /// <summary>删除选中的服务端核心版本（整个实例文件夹）。</summary>
    private void DeleteInstance_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInstance() is not { } instance) return;

        if (_runner != null && _runner.IsRunning &&
            _runner.Instance.Dir.Equals(instance.Dir, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("该服务端正在运行，请先停止后再删除。", "删除核心版本",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(Window.GetWindow(this),
            $"确定删除核心版本「{instance.Name}」吗？\n目录：{instance.Dir}\n\n" +
            "会删除整个实例文件夹（含存档、配置与服务端文件），不可恢复。",
            "删除核心版本", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            if (Directory.Exists(instance.Dir)) Directory.Delete(instance.Dir, true);
            Logger.Info("已删除服务端核心版本：" + instance.Dir);
            if (_serverDir.Equals(instance.Dir, StringComparison.OrdinalIgnoreCase)) _serverDir = "";
            RefreshInstances();
            RunHint.Text = "已删除核心版本：" + instance.Name;
        }
        catch (Exception ex)
        {
            Logger.Warn("删除服务端核心版本失败：" + ex.Message);
            MessageBox.Show("删除失败：" + ex.Message, "删除核心版本",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Instance_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedInstance() is not { } instance) return;
        _serverDir = instance.Dir;
        ServerDirText.Text = _serverDir;
        LoadProperties(instance);
        RunHint.Text = instance.CanLaunch
            ? $"将使用 {instance.Note} 启动；首次启动会自动写入 eula.txt。"
            : "该文件夹里没有可启动的 jar：Forge/NeoForge 需要先运行 installer 安装服务端。";
    }

    private void BuildServerJavaCombo()
    {
        try { _javas = JavaFinder.FindAll(true); }
        catch { _javas = new List<JavaInfo>(); }

        ServerJavaCombo.Items.Clear();
        ServerJavaCombo.Items.Add("自动（系统默认 Java）");
        foreach (var java in _javas) ServerJavaCombo.Items.Add($"Java {java.Major} · {java.Path}");

        var configured = _javas.FindIndex(j => j.Path.Equals(Settings.Current.JavaPath, StringComparison.OrdinalIgnoreCase));
        ServerJavaCombo.SelectedIndex = configured >= 0 ? configured + 1 : 0;

        var gb = Math.Clamp(Settings.Current.MemoryMb / 1024, 1, 32);
        ServerMemorySlider.Value = gb;
        ServerMemoryText.Text = gb + " G";
    }

    private void ServerMemory_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ServerMemoryText == null) return;
        ServerMemoryText.Text = (int)ServerMemorySlider.Value + " G";
    }

    private void OpenInstanceDir_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInstance() is { } instance) OpenFolder(instance.Dir);
    }

    private ServerRunner CreateRunner(ServerInstance instance)
    {
        if (_runner != null && _runner.Instance.Dir.Equals(instance.Dir, StringComparison.OrdinalIgnoreCase))
            return _runner;

        _runner = new ServerRunner(instance);
        return _runner;
    }

    private void StartServer_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInstance() is not { } instance)
        {
            MessageBox.Show("请先选择一个服务端实例。", "提示");
            return;
        }
        if (!instance.CanLaunch)
        {
            MessageBox.Show("该实例没有可启动的服务端 jar。\n如果使用 Forge / NeoForge，请先运行 installer 并选择 Install server。",
                "无法启动", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SaveProperties(silent: true);
        var runner = CreateRunner(instance);
        runner.MemoryMb = (int)ServerMemorySlider.Value * 1024;
        runner.RestartMinutes = int.TryParse(RestartBox.Text, out var minutes) ? Math.Max(0, minutes) : 0;
        runner.JavaPath = ServerJavaCombo.SelectedIndex > 0
            ? _javas[ServerJavaCombo.SelectedIndex - 1].Path
            : Settings.Current.JavaPath;

        OpenConsole();
    }

    private async void StopServer_Click(object sender, RoutedEventArgs e)
    {
        if (_runner == null) return;
        await _runner.StopAsync();
        RunHint.Text = "服务端已停止。";
    }

    private void OpenConsole_Click(object sender, RoutedEventArgs e) => OpenConsole();

    private void OpenConsole()
    {
        if (_consoleWindow != null)
        {
            _consoleWindow.Activate();
            return;
        }
        if (SelectedInstance() is not { } instance)
        {
            MessageBox.Show("请先选择一个服务端实例。", "提示");
            return;
        }
        if (!instance.CanLaunch)
        {
            MessageBox.Show("该实例还不能启动：请先完成服务端安装（Forge / NeoForge 安装器）。",
                "无法启动", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var runner = CreateRunner(instance);
        runner.MemoryMb = (int)ServerMemorySlider.Value * 1024;
        runner.RestartMinutes = int.TryParse(RestartBox.Text, out var minutes) ? Math.Max(0, minutes) : 0;
        runner.JavaPath = ServerJavaCombo.SelectedIndex > 0
            ? _javas[ServerJavaCombo.SelectedIndex - 1].Path
            : Settings.Current.JavaPath;

        _consoleWindow = new ServerConsoleWindow(runner) { Owner = Window.GetWindow(this) };
        _consoleWindow.Closed += (_, _) => _consoleWindow = null;
        _consoleWindow.Show();
    }


    /// <summary>缺失配置项时使用的默认值（与原版服务端默认一致）。</summary>
    private static readonly Dictionary<string, string> PropertyDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["online-mode"] = "true",
        ["pvp"] = "true",
        ["level-name"] = "world",
        ["spawn-protection"] = "0",
        ["allow-nether"] = "true",
        ["spawn-monsters"] = "true",
        ["spawn-animals"] = "true",
        ["spawn-npcs"] = "true"
    };

    private void LoadProperties(ServerInstance instance)
    {
        PropertyPanel.Children.Clear();
        _propertyGetters.Clear();
        _propertyPath = Path.Combine(instance.Dir, "server.properties");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(_propertyPath))
            {
                foreach (var line in File.ReadAllLines(_propertyPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                    var index = trimmed.IndexOf('=');
                    if (index <= 0) continue;
                    values[trimmed[..index].Trim()] = trimmed[(index + 1)..].Trim();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("读取 server.properties 失败：" + ex.Message);
        }

        foreach (var (key, label, kind, options) in PropertySchema)
        {
            values.TryGetValue(key, out var value);
            if (string.IsNullOrEmpty(value)) value = PropertyDefaults.GetValueOrDefault(key, "");
            PropertyPanel.Children.Add(BuildPropertyRow(key, label, kind, options, value ?? ""));
        }

        // 未知的配置项也允许编辑，保证配置文件不会被忽略
        var known = PropertySchema.Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values.Where(v => !known.Contains(v.Key)))
            PropertyPanel.Children.Add(BuildPropertyRow(key, key, "text", null, value));
    }

    private FrameworkElement BuildPropertyRow(string key, string label, string kind, string[] options, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        // 名称列按内容自适应，避免长名称压到输入框上
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 132 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });

        var text = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Margin = new Thickness(0, 0, 12, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = key,
            Foreground = (Brush)FindResource("SubTextBrush")
        };
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        if (kind == "bool")
        {
            var combo = new ComboBox { FontSize = 11, MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Left };
            combo.Items.Add("是");
            combo.Items.Add("否");
            combo.SelectedIndex = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            Grid.SetColumn(combo, 1);
            grid.Children.Add(combo);
            _propertyGetters[key] = () => combo.SelectedIndex == 0 ? "true" : "false";
        }
        else if (kind == "enum" && options != null)
        {
            var combo = new ComboBox { FontSize = 11, MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var option in options) combo.Items.Add(option);
            combo.SelectedItem = options.Contains(value) ? value : options[0];
            Grid.SetColumn(combo, 1);
            grid.Children.Add(combo);
            _propertyGetters[key] = () => combo.SelectedItem as string ?? options[0];
        }
        else
        {
            var box = new TextBox
            {
                Text = value,
                FontSize = 11,
                Height = 32,
                Style = (Style)FindResource("Input"),
                TextAlignment = TextAlignment.Left
            };
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
            _propertyGetters[key] = () => box.Text.Trim();
        }
        return grid;
    }

    private void SaveProperties_Click(object sender, RoutedEventArgs e) => SaveProperties(silent: false);

    private void SaveProperties(bool silent)
    {
        if (_propertyPath.Length == 0 || _propertyGetters.Count == 0)
        {
            if (!silent) MessageBox.Show("请先选择一个服务端实例。", "提示");
            return;
        }

        try
        {
            var lines = new List<string>();
            var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(_propertyPath))
            {
                foreach (var line in File.ReadAllLines(_propertyPath))
                {
                    var trimmed = line.Trim();
                    var index = trimmed.IndexOf('=');
                    if (trimmed.Length > 0 && !trimmed.StartsWith('#') && index > 0)
                    {
                        var key = trimmed[..index].Trim();
                        if (_propertyGetters.TryGetValue(key, out var getter))
                        {
                            lines.Add($"{key}={getter()}");
                            handled.Add(key);
                            continue;
                        }
                    }
                    lines.Add(line);
                }
            }
            foreach (var (key, getter) in _propertyGetters)
                if (!handled.Contains(key)) lines.Add($"{key}={getter()}");

            File.WriteAllLines(_propertyPath, lines);
            if (!silent) MessageBox.Show("服务器配置已保存。", "开服");
            Logger.Info("已保存 server.properties：" + _propertyPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show("保存失败：" + ex.Message, "开服", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }


    private void BuildPluginFilters()
    {
        _suppressPluginFilter = true;
        foreach (var (_, label) in ServerCores.PluginServers) PluginServerCombo.Items.Add(label);
        PluginServerCombo.SelectedIndex = 0;

        var all = new RadioButton
        {
            Style = (Style)FindResource("Chip"),
            GroupName = "plugincategory",
            Content = "全部",
            IsChecked = true,
            Margin = new Thickness(0, 0, 6, 6),
            FontSize = 11
        };
        all.Checked += (_, _) =>
        {
            _pluginCategory = null;
            _ = LoadPluginsAsync(true);
        };
        PluginCategoryPanel.Children.Add(all);

        foreach (var (tag, label) in ServerCores.PluginCategories)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Chip"),
                GroupName = "plugincategory",
                Content = label,
                Margin = new Thickness(0, 0, 6, 6),
                FontSize = 11
            };
            var captured = tag;
            chip.Checked += (_, _) =>
            {
                _pluginCategory = captured;
                _ = LoadPluginsAsync(true);
            };
            PluginCategoryPanel.Children.Add(chip);
        }
        _suppressPluginFilter = false;
    }

    private void PluginFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPluginFilter || !IsLoaded) return;
        var serverIndex = PluginServerCombo.SelectedIndex;
        _pluginServer = serverIndex > 0 && serverIndex < ServerCores.PluginServers.Length
            ? ServerCores.PluginServers[serverIndex].Value
            : "";
        _pluginVersion = PluginVersionCombo.SelectedIndex <= 0
            ? ""
            : PluginVersionCombo.SelectedItem as string ?? "";
        _ = LoadPluginsAsync(true);
    }

    private async void PluginSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await LoadPluginsAsync(true, true);
    }

    private async void SearchPlugins_Click(object sender, RoutedEventArgs e) => await LoadPluginsAsync(true, true);

    private async void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_pluginPage <= 0) return;
        _pluginPage--;
        await LoadPluginsAsync(false);
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        _pluginPage++;
        await LoadPluginsAsync(false);
    }

    private async Task LoadPluginsAsync(bool resetPage, bool searchMode = false)
    {
        if (searchMode) _pluginSearch = (PluginSearch.Text ?? "").Trim();
        if (resetPage) _pluginPage = 0;
        var requestId = ++_pluginRequest;

        try
        {
            PluginHeader.Text = "加载中…";
            PrevPageBtn.IsEnabled = false;
            NextPageBtn.IsEnabled = false;
            var index = _pluginSearch.Length == 0 ? "downloads" : "relevance";
            using var doc = await McApi.SearchModrinthAsync(_pluginSearch, "plugin", PageSize, index,
                _pluginPage * PageSize, _pluginCategory, _pluginVersion,
                _pluginServer.Length > 0 ? _pluginServer : null);
            if (requestId != _pluginRequest) return;

            _plugins.Clear();
            PluginList.Items.Clear();
            foreach (var hit in doc.RootElement.GetProperty("hits").EnumerateArray())
            {
                var raw = hit.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                var name = hit.GetProperty("title").GetString() ?? "";
                var item = new ModItem
                {
                    ProjectId = hit.GetProperty("project_id").GetString() ?? "",
                    OriginalTitle = name,
                    Title = name,
                    Tag = "插件",
                    Description = raw.Length > 70 ? raw[..70] + "…" : raw,
                    OriginalDescription = raw,
                    Downloads = hit.TryGetProperty("downloads", out var dl) ? "↓" + FormatCount(dl.GetInt64()) : "",
                    IconUrl = hit.TryGetProperty("icon_url", out var icon) ? icon.GetString() ?? "" : ""
                };
                item.Icon = PlaceholderIcon(item.Title);
                _plugins.Add(item);
                PluginList.Items.Add(item);
                if (item.IconUrl.Length > 0) _ = LoadIconAsync(item);
            }
            if (_plugins.Count > 0) PluginList.SelectedIndex = 0;

            _pluginTotal = doc.RootElement.TryGetProperty("total_hits", out var total) ? total.GetInt32() : _plugins.Count;
            var pages = Math.Max(1, (int)Math.Ceiling(_pluginTotal / (double)PageSize));
            PageText.Text = $"{_pluginPage + 1}/{pages}";
            PrevPageBtn.IsEnabled = _pluginPage > 0;
            NextPageBtn.IsEnabled = _pluginPage + 1 < pages;

            var scope = new List<string>();
            if (_pluginServer.Length > 0)
                scope.Add(ServerCores.PluginServers.First(s => s.Value == _pluginServer).Label);
            if (_pluginVersion.Length > 0) scope.Add(_pluginVersion);
            if (_pluginCategory != null)
                scope.Add(ServerCores.PluginCategories.First(c => c.Tag == _pluginCategory).Label);
            var prefix = _pluginSearch.Length > 0 ? $"「{_pluginSearch}」" : "热门插件";
            PluginHeader.Text = $"{prefix}{(scope.Count > 0 ? " · " + string.Join(" · ", scope) : "")} · 共 {_pluginTotal} 个";
        }
        catch (Exception ex)
        {
            if (requestId != _pluginRequest) return;
            PluginHeader.Text = "加载失败：" + ex.Message;
            Logger.Warn("插件加载失败：" + ex.Message);
        }
    }

    private async void PluginList_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        await DownloadSelectedPluginAsync();

    private async void DownloadPlugin_Click(object sender, RoutedEventArgs e) =>
        await DownloadSelectedPluginAsync();

    private async Task DownloadSelectedPluginAsync()
    {
        if (_busy) return;
        if (PluginList.SelectedItem is not ModItem item)
        {
            MessageBox.Show("请先选择一个插件。", "提示");
            return;
        }

        var gameVersion = _pluginVersion;
        if (string.IsNullOrEmpty(gameVersion))
        {
            var options = _versions.Where(v => v.Type == "release").Take(60)
                .Select(v => (v.Id, v.Id)).ToList();
            if (options.Count == 0)
            {
                MessageBox.Show("请先选择游戏版本。", "提示");
                return;
            }
            var picker = new LoaderPickerWindow("选择游戏版本", $"为「{item.Title}」选择服务端对应的游戏版本", options)
            {
                Owner = Window.GetWindow(this)
            };
            if (picker.ShowDialog() != true) return;
            gameVersion = picker.SelectedVersion;
        }

        List<ModFile> files;
        string hint = null;
        try
        {
            StatusText.Text = $"正在获取「{item.Title}」可用版本…";
            using var doc = await McApi.GetModrinthVersionsAsync(item.ProjectId, gameVersion,
                _pluginServer.Length > 0 ? _pluginServer : null);
            files = ParseModFiles(doc.RootElement);
            if (files.Count == 0)
            {
                using var fallback = await McApi.GetModrinthVersionsAsync(item.ProjectId, null, null);
                files = ParseModFiles(fallback.RootElement);
                if (files.Count == 0) throw new Exception("该插件没有可下载的文件。");
                hint = "没有匹配当前服务端/版本的文件，已显示全部版本。";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "获取插件版本失败：" + ex.Message;
            MessageBox.Show(ex.Message, item.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var chooser = new ModVersionWindow(item.Title, gameVersion, _pluginServer, files, hint)
        {
            Owner = Window.GetWindow(this)
        };
        if (chooser.ShowDialog() != true || chooser.SelectedFile == null) return;

        var file = chooser.SelectedFile;
        var baseDir = SelectedInstance()?.Dir ?? _serverDir;
        var pluginsDir = Path.Combine(baseDir, "plugins");
        Directory.CreateDirectory(pluginsDir);
        var target = Path.Combine(pluginsDir, file.FileName);

        await DownloadFileAsync(item.Title, file, target);
        await InstallDependenciesAsync(file, gameVersion, pluginsDir, 0);
    }

    /// <summary>自动安装缺少的强制前置插件（Modrinth 的 required 依赖）。</summary>
    private async Task InstallDependenciesAsync(ModFile file, string gameVersion, string pluginsDir, int depth)
    {
        if (depth > 3 || file.Required.Count == 0) return;

        foreach (var dependency in file.Required)
        {
            try
            {
                ModFile required = null;
                if (!string.IsNullOrEmpty(dependency.VersionId))
                {
                    using var versionDoc = await McApi.GetModrinthVersionAsync(dependency.VersionId);
                    required = ParseModFile(versionDoc.RootElement);
                }
                if (required == null && !string.IsNullOrEmpty(dependency.ProjectId))
                {
                    using var doc = await McApi.GetModrinthVersionsAsync(dependency.ProjectId, gameVersion,
                        _pluginServer.Length > 0 ? _pluginServer : null);
                    required = ParseModFiles(doc.RootElement).FirstOrDefault();
                }
                if (required == null) continue;

                var target = Path.Combine(pluginsDir, required.FileName);
                if (File.Exists(target))
                {
                    AppendLog("前置已存在：" + required.FileName);
                    continue;
                }

                AppendLog("自动安装前置：" + required.Name);
                var progress = new Progress<double>(_ => { });
                await McApi.DownloadAsync(required.Url, target, "", CancellationToken.None, progress);
                await InstallDependenciesAsync(required, gameVersion, pluginsDir, depth + 1);
            }
            catch (Exception ex)
            {
                AppendLog("前置安装失败：" + ex.Message);
                Logger.Warn("前置插件安装失败：" + ex.Message);
            }
        }
    }

    private async Task DownloadFileAsync(string title, ModFile file, string target)
    {
        _cts = new CancellationTokenSource();
        SetBusy(true);
        Bar.Value = 0;
        PercentText.Text = "0%";
        InstallLog.Clear();
        StatusText.Text = "准备下载…";
        ServerProgressHost.Visibility = Visibility.Visible;

        var completed = false;
        try
        {
            AppendLog($"下载 {file.FileName}（{file.Loaders}）");
            var progress = new Progress<double>(p =>
            {
                Bar.Value = p;
                PercentText.Text = $"{p:0}%";
            });
            await McApi.DownloadAsync(file.Url, target, "", _cts.Token, progress);
            AppendLog("已保存到 " + target);
            StatusText.Text = "下载完成：" + target;
            completed = true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消下载。";
            AppendLog("已取消下载。");
        }
        catch (DownloadUnavailableException ex)
        {
            // 官方源与镜像都不通：列出缺少的文件并给出官网，不自动下载缺失库
            StatusText.Text = "缺少文件，已尝试官方源与镜像";
            AppendLog(ex.Message);
            Logger.Warn($"{title} 缺少文件：" + ex.Message);
            var open = MessageBox.Show(Window.GetWindow(this),
                ex.Message + "\n\n是否打开官方下载页手动下载？",
                "缺少文件", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (open == MessageBoxResult.Yes && !string.IsNullOrEmpty(ex.OfficialPage))
                Process.Start(new System.Diagnostics.ProcessStartInfo(ex.OfficialPage) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = "下载失败：" + ex.Message;
            AppendLog("下载失败：" + ex.Message);
            Logger.Warn($"{title} 下载失败：" + ex.Message);
        }
        finally
        {
            if (!completed)
            {
                Bar.Value = 0;
                PercentText.Text = "0%";
            }
            ServerProgressHost.Visibility = Visibility.Collapsed;
            SetBusy(false);
            _cts.Dispose();
            _cts = null;
        }
    }


    private static ModFile ParseModFile(JsonElement version)
    {
        if (!version.TryGetProperty("files", out var files) || files.GetArrayLength() == 0) return null;

        JsonElement primary = default;
        var hasFile = false;
        foreach (var f in files.EnumerateArray())
        {
            if (!hasFile) { primary = f; hasFile = true; }
            if (f.TryGetProperty("primary", out var p) && p.GetBoolean()) { primary = f; break; }
        }
        if (!hasFile) return null;

        var name = version.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var number = version.TryGetProperty("version_number", out var vn) ? vn.GetString() ?? "" : "";
        var gameVersions = version.TryGetProperty("game_versions", out var gv)
            ? string.Join(", ", gv.EnumerateArray().Select(x => x.GetString()))
            : "";
        var loaders = version.TryGetProperty("loaders", out var ld)
            ? string.Join(", ", ld.EnumerateArray().Select(x => x.GetString()))
            : "";
        var date = version.TryGetProperty("date_published", out var d) &&
                   DateTime.TryParse(d.GetString(), out var dt)
            ? dt.ToLocalTime().ToString("yyyy-MM-dd")
            : "";
        var size = primary.TryGetProperty("size", out var s)
            ? (s.GetInt64() / 1024.0 / 1024.0).ToString("0.0") + " MB"
            : "";

        var required = new List<ModDependency>();
        if (version.TryGetProperty("dependencies", out var dependencies))
        {
            foreach (var dependency in dependencies.EnumerateArray())
            {
                var type = dependency.TryGetProperty("dependency_type", out var dt2) ? dt2.GetString() : "";
                if (!string.Equals(type, "required", StringComparison.OrdinalIgnoreCase)) continue;
                var projectId = dependency.TryGetProperty("project_id", out var pid) ? pid.GetString() : null;
                var versionId = dependency.TryGetProperty("version_id", out var vid) ? vid.GetString() : null;
                if (!string.IsNullOrEmpty(projectId)) required.Add(new ModDependency(projectId, versionId));
            }
        }

        return new ModFile
        {
            Name = string.IsNullOrWhiteSpace(name) ? number : name,
            FileName = primary.GetProperty("filename").GetString() ?? "",
            Url = primary.GetProperty("url").GetString() ?? "",
            GameVersions = gameVersions,
            Loaders = loaders,
            Date = date,
            Size = size,
            Required = required
        };
    }

    private static List<ModFile> ParseModFiles(JsonElement versions)
    {
        var list = new List<ModFile>();
        if (versions.ValueKind != JsonValueKind.Array) return list;
        foreach (var version in versions.EnumerateArray())
        {
            var file = ParseModFile(version);
            if (file != null) list.Add(file);
        }
        return list;
    }

    private static string FormatCount(long value) => value switch
    {
        >= 100_000_000 => (value / 100_000_000.0).ToString("0.0") + "亿",
        >= 10_000 => (value / 10_000.0).ToString("0.0") + "万",
        _ => value.ToString("N0")
    };

    private static readonly Dictionary<string, ImageSource> PlaceholderCache = new();

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
            var formatted = new FormattedText(letter, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 30, Brushes.White, 1.0);
            dc.DrawText(formatted, new Point((size - formatted.Width) / 2, (size - formatted.Height) / 2));
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        PlaceholderCache[key] = bitmap;
        return bitmap;
    }

    private static async Task LoadIconAsync(ModItem item)
    {
        foreach (var url in new[]
                 {
                     "https://wsrv.nl/?url=" + Uri.EscapeDataString(item.IconUrl) + "&output=png",
                     item.IconUrl
                 })
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
}
