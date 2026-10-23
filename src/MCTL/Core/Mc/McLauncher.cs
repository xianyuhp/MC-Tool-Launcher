using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MCTL.Core.Mc;

public class LaunchOptions
{
    public string VersionId { get; set; } = "";
    public string GameDir { get; set; } = "";
    public string PlayerName { get; set; } = "Steve";
    public int MemoryMb { get; set; } = 4096;
    public string JavaPath { get; set; } = "";
    public bool Fullscreen { get; set; }
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    /// <summary>正版登录的 Minecraft 访问令牌（为空则离线模式）。</summary>
    public string AccessToken { get; set; } = "";
    public string Uuid { get; set; } = "";
    public bool Online { get; set; }
}

public class GameSession
{
    public Process Process { get; init; }
    public List<string> Output { get; } = new();
    public string VersionId { get; init; } = "";
    public string GameDir { get; init; } = "";
    public DateTime StartedAt { get; } = DateTime.Now;

    public string GetPreCrashLog(int maxLines = 300)
    {
        lock (Output)
            return string.Join(Environment.NewLine, Output.TakeLast(maxLines));
    }
}

public static class McLauncher
{
    public static List<string> GetInstalledVersions(string gameDir = null)
    {
        gameDir = string.IsNullOrWhiteSpace(gameDir) ? Settings.Current.EffectiveGameDir : gameDir;
        var dir = Path.Combine(gameDir, "versions");
        if (!Directory.Exists(dir)) return new List<string>();
        return Directory.EnumerateDirectories(dir)
            .Where(d => File.Exists(Path.Combine(d, Path.GetFileName(d) + ".json")))
            .Select(d => Path.GetFileName(d))
            .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static GameSession Start(LaunchOptions options)
    {
        var rootDir = string.IsNullOrWhiteSpace(options.GameDir) ? Settings.Current.EffectiveGameDir : options.GameDir;
        // 版本隔离：mods / 存档 / 配置都放在 versions/<版本>/ 下
        var gameDir = rootDir;
        if (Settings.Current.VersionIsolation)
        {
            var isolated = Path.Combine(rootDir, "versions", options.VersionId);
            try { Directory.CreateDirectory(isolated); } catch { }
            if (Directory.Exists(isolated)) gameDir = isolated;
        }
        var documents = new List<JsonDocument>();
        var chain = new List<(string Id, string Dir, JsonElement Root)>();

        try
        {
            var currentId = options.VersionId;
            while (!string.IsNullOrEmpty(currentId))
            {
                var dir = Path.Combine(rootDir, "versions", currentId);
                var jsonPath = Path.Combine(dir, currentId + ".json");
                if (!File.Exists(jsonPath))
                    throw new Exception($"未找到版本 {currentId}，请先在下载页安装。");

                var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                documents.Add(doc);
                var root = doc.RootElement;
                chain.Add((currentId, dir, root));
                currentId = VersionJson.GetString(root, "inheritsFrom");
                if (chain.Count > 8) break;
            }

            // 主类、资源索引、客户端 jar
            var mainClass = chain.Select(c => VersionJson.GetString(c.Root, "mainClass")).FirstOrDefault(x => x != null);
            var assetsIndex = chain.Select(c => VersionJson.GetString(c.Root, "assets")).FirstOrDefault(x => x != null) ?? "legacy";
            var versionType = VersionJson.GetString(chain[0].Root, "type") ?? "release";
            var clientJar = "";
            foreach (var (id, dir, root) in chain)
            {
                if (root.TryGetProperty("downloads", out var d) && d.TryGetProperty("client", out _))
                    clientJar = Path.Combine(dir, id + ".jar");
            }
            if (mainClass == null) throw new Exception("版本 JSON 缺少 mainClass");

            // 依赖库（子版本优先）
            var libraries = new List<LibraryEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, _, root) in chain)
                foreach (var lib in VersionJson.ParseLibraries(root))
                {
                    var key = lib.Artifact?.Path ?? lib.Name;
                    if (seen.Add(key)) libraries.Add(lib);
                }

            var classpath = string.Join(';', libraries
                .Where(l => l.Artifact != null)
                .Select(l => Path.Combine(rootDir, "libraries", l.Artifact.Path.Replace('/', '\\')))
                .Append(clientJar)
                .Where(File.Exists));

            var nativesDir = Path.Combine(chain[0].Dir, "natives");

            // 参数：父版本在前，子版本在后
            var jvmArgs = new List<string>();
            var gameArgs = new List<string>();
            foreach (var (_, _, root) in Enumerable.Reverse(chain))
            {
                if (!root.TryGetProperty("arguments", out var arguments)) continue;
                if (arguments.TryGetProperty("jvm", out var jvm))
                    jvmArgs.AddRange(ReadArguments(jvm));
                if (arguments.TryGetProperty("game", out var game))
                    gameArgs.AddRange(ReadArguments(game));
            }
            if (gameArgs.Count == 0)
            {
                var legacy = chain.Select(c => VersionJson.GetString(c.Root, "minecraftArguments")).FirstOrDefault(x => x != null);
                if (legacy != null) gameArgs.AddRange(legacy.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }

            var uuid = options.Online && !string.IsNullOrWhiteSpace(options.Uuid)
                ? options.Uuid
                : OfflineUuid(options.PlayerName);
            var accessToken = options.Online && !string.IsNullOrWhiteSpace(options.AccessToken)
                ? options.AccessToken
                : "0";
            var replacements = new Dictionary<string, string>
            {
                ["${auth_player_name}"] = options.PlayerName,
                ["${version_name}"] = options.VersionId,
                ["${game_directory}"] = gameDir,
                ["${assets_root}"] = Path.Combine(rootDir, "assets"),
                ["${game_assets}"] = Path.Combine(rootDir, "assets", "virtual", "legacy"),
                ["${assets_index_name}"] = assetsIndex,
                ["${auth_uuid}"] = uuid,
                ["${auth_access_token}"] = accessToken,
                ["${auth_session}"] = accessToken,
                ["${user_type}"] = options.Online ? "msa" : "legacy",
                ["${version_type}"] = versionType,
                ["${clientid}"] = "aurora",
                ["${auth_xuid}"] = "0",
                ["${user_properties}"] = "{}",
                ["${launcher_name}"] = "MCTL",
                ["${launcher_version}"] = "1.0",
                ["${classpath}"] = classpath,
                ["${natives_directory}"] = nativesDir,
                ["${library_directory}"] = Path.Combine(rootDir, "libraries"),
                ["${classpath_separator}"] = ";",
                ["${resolution_width}"] = options.Width.ToString(),
                ["${resolution_height}"] = options.Height.ToString(),
            };

            string Replace(string value)
            {
                foreach (var (k, v) in replacements) value = value.Replace(k, v);
                return value;
            }

            var requiredJava = 0;
            foreach (var (_, _, root) in chain)
            {
                if (root.TryGetProperty("javaVersion", out var jv) &&
                    jv.TryGetProperty("majorVersion", out var major))
                {
                    requiredJava = major.GetInt32();
                    break;
                }
            }

            JavaInfo javaInfo;
            if (Settings.Current.JavaAutoSelect)
            {
                javaInfo = JavaFinder.Pick(requiredJava);
            }
            else
            {
                var configuredJava = !string.IsNullOrWhiteSpace(options.JavaPath)
                    ? JavaFinder.Probe(options.JavaPath)
                    : null;
                if (configuredJava != null && configuredJava.Major < requiredJava)
                    Logger.Warn($"所选 Java {configuredJava.Major} 低于该版本要求（{requiredJava}），已自动换用更高版本。");
                javaInfo = configuredJava != null && configuredJava.Major >= requiredJava
                    ? configuredJava
                    : JavaFinder.Pick(requiredJava);
            }
            var java = javaInfo?.Path;
            if (string.IsNullOrWhiteSpace(java) || !File.Exists(java))
                throw new Exception($"未找到可用的 Java（该版本需要 Java {requiredJava} 或更高），请在设置中指定 java.exe 路径。");

            EnsureChineseOptions(gameDir);

            var args = new List<string>
            {
                $"-Xmx{Math.Max(512, options.MemoryMb)}M",
                $"-Xms{Math.Min(512, Math.Max(256, options.MemoryMb / 4))}M",
                "-Dfile.encoding=UTF-8",
                "-Dlog4j2.formatMsgNoLookups=true",
                "-Dminecraft.launcher.brand=MCTL",
                "-Dminecraft.launcher.version=1.0"
            };
            args.AddRange(jvmArgs.Where(a => a != "-cp" && a != "${classpath}").Select(Replace));
            if (!args.Any(a => a.StartsWith("-Djava.library.path")))
                args.Add("-Djava.library.path=" + nativesDir);
            args.Add("-cp");
            args.Add(classpath);
            args.Add(mainClass);
            args.AddRange(gameArgs.Select(Replace));

            if (options.Fullscreen && !args.Contains("--fullscreen")) args.Add("--fullscreen");
            if (!args.Contains("--width")) { args.Add("--width"); args.Add(options.Width.ToString()); }
            if (!args.Contains("--height")) { args.Add("--height"); args.Add(options.Height.ToString()); }

            var psi = new ProcessStartInfo(java)
            {
                WorkingDirectory = gameDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var session = new GameSession
            {
                Process = process,
                VersionId = options.VersionId,
                GameDir = gameDir
            };

            void Handle(string line)
            {
                if (line == null) return;
                lock (session.Output)
                {
                    session.Output.Add(line);
                    if (session.Output.Count > 5000) session.Output.RemoveRange(0, 1000);
                }
                Logger.Write("MC", line);
            }

            process.OutputDataReceived += (_, e) => Handle(e.Data);
            process.ErrorDataReceived += (_, e) => Handle(e.Data);

            Logger.Info($"启动 {options.VersionId}，Java：{java}");
            Logger.Info("命令行：" + string.Join(' ', args.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a)));

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return session;
        }
        finally
        {
            foreach (var doc in documents) doc.Dispose();
        }
    }

    private static IEnumerable<string> ReadArguments(JsonElement array)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                yield return item.GetString()!;
            }
            else if (item.ValueKind == JsonValueKind.Object && VersionJson.RulesAllow(item) &&
                     item.TryGetProperty("value", out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                    yield return value.GetString()!;
                else if (value.ValueKind == JsonValueKind.Array)
                    foreach (var v in value.EnumerateArray())
                        if (v.ValueKind == JsonValueKind.String)
                            yield return v.GetString()!;
            }
        }
    }

    public static string OfflineUuid(string name)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    /// <summary>按设置把游戏语言固定为简体中文（默认开启）。</summary>
    private static void EnsureChineseOptions(string gameDir)
    {
        if (!Settings.Current.ForceChinese) return;
        try
        {
            var path = Path.Combine(gameDir, "options.txt");
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(gameDir);
                File.WriteAllText(path, "lang:zh_cn" + Environment.NewLine);
                return;
            }

            var lines = File.ReadAllLines(path).ToList();
            var index = lines.FindIndex(l => l.StartsWith("lang:", StringComparison.OrdinalIgnoreCase));
            if (index >= 0) lines[index] = "lang:zh_cn";
            else lines.Add("lang:zh_cn");
            File.WriteAllLines(path, lines);
        }
        catch (Exception ex)
        {
            Logger.Warn("写入中文语言设置失败：" + ex.Message);
        }
    }
}
