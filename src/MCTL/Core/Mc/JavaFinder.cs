using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace MCTL.Core.Mc;

public record JavaInfo(string Path, int Major, string Version)
{
    public override string ToString() => $"Java {Major}  ({Version})  {Path}";
}

public static class JavaFinder
{
    private static List<JavaInfo> _cache;

    public static List<string> CandidatePaths()
    {
        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(Settings.Current.JavaPath)) result.Add(Settings.Current.JavaPath);

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome)) result.Add(Path.Combine(javaHome, "bin", "java.exe"));

        foreach (var key in new[]
                 {
                     @"SOFTWARE\JavaSoft\JDK", @"SOFTWARE\JavaSoft\Java Development Kit",
                     @"SOFTWARE\JavaSoft\Java Runtime Environment",
                     @"SOFTWARE\WOW6432Node\JavaSoft\JDK",
                     @"SOFTWARE\WOW6432Node\JavaSoft\Java Runtime Environment"
                 })
        {
            try
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(key);
                if (baseKey == null) continue;
                foreach (var sub in baseKey.GetSubKeyNames())
                {
                    using var versionKey = baseKey.OpenSubKey(sub);
                    var home = versionKey?.GetValue("JavaHome") as string;
                    if (!string.IsNullOrWhiteSpace(home)) result.Add(Path.Combine(home, "bin", "java.exe"));
                }
            }
            catch { }
        }

        var roots = new[]
        {
            @"C:\Program Files\Java", @"C:\Program Files\Eclipse Adoptium", @"C:\Program Files\Microsoft",
            @"C:\Program Files\BellSoft", @"C:\Program Files\Zulu", @"C:\Program Files\Amazon Corretto",
            @"C:\Program Files\Semeru", @"C:\Program Files (x86)\Java",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Eclipse Adoptium"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft")
        };
        foreach (var root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
                {
                    var exe = Path.Combine(dir, "bin", "java.exe");
                    if (File.Exists(exe)) result.Add(exe);
                }
            }
            catch { }
        }

        // PATH 中的 java
        try
        {
            var psi = new ProcessStartInfo("where.exe", "java.exe")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var path = line.Trim();
                    if (File.Exists(path)) result.Add(path);
                }
            }
        }
        catch { }

        // 扫描各固定磁盘根目录下常见的 JDK 目录（限深度，避免过慢）
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                foreach (var dir in SafeDirectories(drive.RootDirectory.FullName))
                {
                    var name = Path.GetFileName(dir).ToLowerInvariant();
                    if (!(name.Contains("java") || name.Contains("jdk") || name.Contains("jre") ||
                          name.Contains("adoptium") || name.Contains("zulu") || name.Contains("corretto") ||
                          name.Contains("graal") || name.Contains("semeru") || name.Contains("liberica") ||
                          name.Contains("temurin")))
                        continue;
                    foreach (var exe in FindJavaUnder(dir, 3)) result.Add(exe);
                }
            }
        }
        catch { }

        return result;
    }

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> FindJavaUnder(string root, int depth)
    {
        var result = new List<string>();
        try
        {
            var direct = Path.Combine(root, "bin", "java.exe");
            if (File.Exists(direct)) result.Add(direct);
            if (depth <= 0) return result;
            foreach (var dir in SafeDirectories(root))
                result.AddRange(FindJavaUnder(dir, depth - 1));
        }
        catch { }
        return result;
    }

    public static List<JavaInfo> FindAll(bool force = false)
    {
        if (_cache != null && !force) return _cache;
        var result = new List<JavaInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in CandidatePaths())
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
            if (!seen.Add(path)) continue;
            var info = Probe(path);
            if (info != null) result.Add(info);
        }
        _cache = result.OrderByDescending(j => j.Major).ToList();
        return _cache;
    }

    public static JavaInfo Pick(int requiredMajor)
    {
        var all = FindAll();
        // 优先选满足要求的最低主版本，兼容性更好；没有满足的就用最高版本
        return all.Where(j => j.Major >= requiredMajor).OrderBy(j => j.Major).FirstOrDefault()
               ?? all.OrderByDescending(j => j.Major).FirstOrDefault();
    }

    /// <summary>当前是否有满足该版本要求的 Java。</summary>
    public static JavaInfo PickCompatible(int requiredMajor) =>
        FindAll().Where(j => j.Major >= requiredMajor).OrderBy(j => j.Major).FirstOrDefault();

    /// <summary>读取版本 JSON（含 inheritsFrom 链）要求的 Java 主版本，读取失败时按版本号推断。</summary>
    public static int RequiredMajor(string gameDir, string versionId)
    {
        try
        {
            var current = versionId;
            for (var depth = 0; depth < 8 && !string.IsNullOrEmpty(current); depth++)
            {
                var path = Path.Combine(gameDir, "versions", current, current + ".json");
                if (!File.Exists(path)) break;

                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                if (root.TryGetProperty("javaVersion", out var jv) &&
                    jv.TryGetProperty("majorVersion", out var major))
                    return major.GetInt32();

                current = root.TryGetProperty("inheritsFrom", out var parent) ? parent.GetString() : null;
            }
        }
        catch { }

        return GuessMajor(versionId);
    }

    /// <summary>按原版版本号推断 Java 主版本：≤1.16 → 8，1.17 → 16，1.18~1.20.4 → 17，≥1.20.5 → 21。</summary>
    public static int GuessMajor(string versionId)
    {
        var match = System.Text.RegularExpressions.Regex.Match(versionId ?? "", @"^(\d+)\.(\d+)(?:\.(\d+))?");
        if (!match.Success) return 17;

        var major = int.Parse(match.Groups[1].Value);
        var minor = int.Parse(match.Groups[2].Value);
        var patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        if (major != 1) return 21;
        if (minor <= 16) return 8;
        if (minor == 17) return 16;
        if (minor <= 19) return 17;
        if (minor == 20) return patch >= 5 ? 21 : 17;
        return 21;
    }

    public static JavaInfo Probe(string javaPath)
    {
        try
        {
            var psi = new ProcessStartInfo(javaPath, "-version")
            {
                RedirectStandardError = true, RedirectStandardOutput = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            var text = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
            p.WaitForExit(8000);

            var major = 0;
            var version = "未知";
            var match = System.Text.RegularExpressions.Regex.Match(text, "version \"([^\"]+)\"");
            if (match.Success)
            {
                version = match.Groups[1].Value;
                var parts = version.Split('.', '_');
                if (parts[0] == "1" && parts.Length > 1) int.TryParse(parts[1], out major);
                else int.TryParse(parts[0], out major);
            }
            return major == 0 ? null : new JavaInfo(javaPath, major, version);
        }
        catch
        {
            return null;
        }
    }
}
