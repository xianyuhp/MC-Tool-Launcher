using System.Diagnostics;
using System.IO;
using System.Text;

namespace AuroraLauncher.Core.Mc;

/// <summary>已下载的服务端实例（位于启动器目录 server 下）。</summary>
public class ServerInstance
{
    public string Dir { get; init; } = "";
    public string Name { get; init; } = "";
    public string JarPath { get; init; } = "";
    public string Note { get; init; } = "";
    public bool HasJar => !string.IsNullOrEmpty(JarPath);
    /// <summary>Forge / NeoForge 安装器生成的启动脚本。</summary>
    public bool RunScriptExists { get; init; }
    public bool CanLaunch => HasJar || RunScriptExists;
    public override string ToString() => Name;
}

/// <summary>扫描与创建服务端实例：启动器目录/server/&lt;核心&gt;-&lt;版本&gt;/。</summary>
public static class ServerInstances
{
    public static string Root => Path.Combine(Settings.AppDir, "server");

    /// <summary>所有服务端保存根目录：默认目录 + 下载时选过的目录。</summary>
    public static IEnumerable<string> Roots =>
        new[] { Root }
            .Concat(Settings.Current.ServerRoots
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => d.TrimEnd('\\')))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    public static string DirFor(string coreLabel, string mc)
    {
        var core = coreLabel.Replace(" ", "").Replace("混合端", "").Trim();
        return Path.Combine(Root, core, mc);
    }

    public static List<ServerInstance> Scan()
    {
        var list = new List<ServerInstance>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Roots)
        {
            try
            {
                if (!Directory.Exists(root)) Directory.CreateDirectory(root);
                foreach (var coreDir in Directory.EnumerateDirectories(root))
                {
                    if (!seen.Add(coreDir)) continue;
                    var jars = Directory.GetFiles(coreDir, "*.jar", SearchOption.TopDirectoryOnly);
                    if (jars.Length > 0)
                    {
                        // 兼容旧版扁平结构：server/核心-版本/，以及自定义命名的实例
                        list.Add(Build(coreDir, Path.GetFileName(coreDir), jars));
                        continue;
                    }

                    foreach (var versionDir in Directory.EnumerateDirectories(coreDir))
                    {
                        if (!seen.Add(versionDir)) continue;
                        var versionJars = Directory.GetFiles(versionDir, "*.jar", SearchOption.TopDirectoryOnly);
                        var label = $"{Path.GetFileName(coreDir)} · {Path.GetFileName(versionDir)}";
                        list.Add(Build(versionDir, label, versionJars));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("扫描服务端目录失败（" + root + "）：" + ex.Message);
            }
        }
        return list.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static ServerInstance Build(string dir, string name, string[] jars)
    {
        var jar = PickJar(jars);
        var runBat = File.Exists(Path.Combine(dir, "run.bat"));
        return new ServerInstance
        {
            Dir = dir,
            Name = name,
            JarPath = jar ?? "",
            RunScriptExists = runBat,
            Note = runBat ? "run.bat（已安装）"
                : jar == null ? "未找到服务端 jar（可能还需运行安装器）" : Path.GetFileName(jar)
        };
    }

    /// <summary>挑选启动用的 jar：排除 installer，优先名称含 server / 常见核心名，其次体积最大。</summary>
    private static string PickJar(string[] jars)
    {
        var candidates = jars
            .Where(j => !Path.GetFileName(j).Contains("installer", StringComparison.OrdinalIgnoreCase) &&
                        !Path.GetFileName(j).Contains("sources", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0) return null;

        var preferred = candidates.FirstOrDefault(j =>
            Path.GetFileName(j).Contains("server", StringComparison.OrdinalIgnoreCase));
        preferred ??= candidates.FirstOrDefault(j =>
            new[] { "paper", "purpur", "arclight", "mohist", "fabric", "forge", "neoforge" }
                .Any(k => Path.GetFileName(j).Contains(k, StringComparison.OrdinalIgnoreCase)));
        preferred ??= candidates.OrderByDescending(f => new FileInfo(f).Length).First();
        return preferred;
    }
}

/// <summary>服务端进程管理：启动、控制台输入、定时重启。</summary>
public class ServerRunner
{
    private readonly object _gate = new();
    private Process _process;
    private bool _manualStop;
    private bool _scheduledRestart;
    private System.Timers.Timer _restartTimer;

    public ServerInstance Instance { get; }
    public int MemoryMb { get; set; } = 4096;
    public string JavaPath { get; set; } = "";
    /// <summary>定时重启间隔（分钟），0 表示关闭。</summary>
    public int RestartMinutes { get; set; }

    public event Action<string> Output;
    public event Action<bool> RunningChanged;

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _process != null && !_process.HasExited;
        }
    }

    public ServerRunner(ServerInstance instance) => Instance = instance;

    private void Emit(string line) => Output?.Invoke(line);

    public void Start()
    {
        if (IsRunning || !Instance.CanLaunch) return;

        try
        {
            // 自动同意 EULA（与启动器一体化启动）
            File.WriteAllText(Path.Combine(Instance.Dir, "eula.txt"), "eula=true" + Environment.NewLine);
            Directory.CreateDirectory(Path.Combine(Instance.Dir, "plugins"));

            ProcessStartInfo info;
            var runBat = Path.Combine(Instance.Dir, "run.bat");
            if (File.Exists(runBat))
            {
                info = new ProcessStartInfo("cmd.exe", "/c run.bat")
                {
                    WorkingDirectory = Instance.Dir,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                Emit($"检测到 run.bat，使用安装器生成的启动参数（内存 {MemoryMb} MB）。");
            }
            else
            {
                var java = string.IsNullOrWhiteSpace(JavaPath) || !File.Exists(JavaPath) ? "java" : JavaPath;
                var xms = Math.Max(512, MemoryMb / 2);
                var args = $"-Xms{xms}M -Xmx{MemoryMb}M -Dfile.encoding=UTF-8 -Djava.net.preferIPv4Stack=true " +
                           $"-jar \"{Instance.JarPath}\" nogui";
                info = new ProcessStartInfo(java, args)
                {
                    WorkingDirectory = Instance.Dir,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
            }

            _manualStop = false;
            _scheduledRestart = false;

            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) Emit(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) Emit(e.Data);
            };
            process.Exited += (_, _) => OnExited();

            lock (_gate) _process = process;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            Emit($"已启动服务端：{Instance.Name}（内存 {MemoryMb} MB）");
            Emit("首次启动可能自动下载依赖（如 NeoForge / 库文件），请耐心等待，不要关闭窗口。");
            StartRestartTimer();
            RunningChanged?.Invoke(true);
        }
        catch (Exception ex)
        {
            Emit("启动失败：" + ex.Message);
            Logger.Warn("服务端启动失败：" + ex.Message);
            RunningChanged?.Invoke(false);
        }
    }

    private void StartRestartTimer()
    {
        StopRestartTimer();
        if (RestartMinutes <= 0) return;

        Emit($"已设置定时重启：每 {RestartMinutes} 分钟");
        _restartTimer = new System.Timers.Timer(RestartMinutes * 60_000.0) { AutoReset = true };
        _restartTimer.Elapsed += (_, _) =>
        {
            if (!IsRunning) return;
            Emit("到达定时重启时间，正在停止服务端…");
            _scheduledRestart = true;
            Send("stop");
        };
        _restartTimer.Start();
    }

    private void StopRestartTimer()
    {
        _restartTimer?.Stop();
        _restartTimer?.Dispose();
        _restartTimer = null;
    }

    private void OnExited()
    {
        var restart = _scheduledRestart && !_manualStop;
        lock (_gate) _process = null;
        Emit("服务端进程已退出。");
        RunningChanged?.Invoke(false);

        if (restart)
        {
            _scheduledRestart = false;
            Emit("3 秒后重新启动服务端…");
            var timer = new System.Timers.Timer(3000) { AutoReset = false };
            timer.Elapsed += (_, _) =>
            {
                timer.Dispose();
                Start();
            };
            timer.Start();
        }
    }

    public void Send(string command)
    {
        lock (_gate)
        {
            if (_process == null || _process.HasExited) return;
            try
            {
                _process.StandardInput.WriteLine(command);
                _process.StandardInput.Flush();
                Emit("> " + command);
            }
            catch (Exception ex)
            {
                Emit("发送命令失败：" + ex.Message);
            }
        }
    }

    public async Task StopAsync(int waitSeconds = 15)
    {
        _manualStop = true;
        StopRestartTimer();
        Send("stop");

        var deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (!IsRunning) return;
            await Task.Delay(300);
        }

        try
        {
            lock (_gate)
            {
                if (_process is { HasExited: false }) _process.Kill(true);
            }
        }
        catch { }
    }
}
