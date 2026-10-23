using System.IO;
using System.Text.RegularExpressions;

namespace MCTL.Core.Diagnostics;

public enum CrashSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>一条崩溃原因分析。</summary>
public sealed class CrashFinding
{
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public CrashSeverity Severity { get; init; }
}

/// <summary>崩溃分析结果：一句话原因、建议、逐条依据与疑似模组。</summary>
public sealed class CrashAnalysis
{
    public string Summary { get; set; } = "";
    public string Suggestion { get; set; } = "";
    public List<CrashFinding> Findings { get; } = new();
    public List<string> SuspectMods { get; } = new();

    /// <summary>转成崩溃窗口里直接展示的文本。</summary>
    public string ToDisplayText()
    {
        var lines = new List<string>();
        lines.Add("【可能的原因】" + (Summary.Length > 0 ? Summary : "暂时无法确定，请查看下方日志"));
        if (Suggestion.Length > 0) lines.Add("【建议】" + Suggestion);

        if (Findings.Count > 0)
        {
            lines.Add("");
            lines.Add("【分析依据】");
            foreach (var finding in Findings)
            {
                var level = finding.Severity switch
                {
                    CrashSeverity.Critical => "严重",
                    CrashSeverity.Warning => "警告",
                    _ => "提示"
                };
                lines.Add($"[{level}] {finding.Title}");
                if (finding.Detail.Length > 0) lines.Add("    " + finding.Detail);
            }
        }

        if (SuspectMods.Count > 0)
        {
            lines.Add("");
            lines.Add("【疑似模组】" + string.Join("、", SuspectMods));
        }
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// 崩溃分析：把崩溃报告 / latest.log / 进程输出里的关键信息翻译成人话，
/// 并尽量定位到具体模组，避免只给用户一堆乱码日志。
/// </summary>
public static class CrashAnalyzer
{
    public static CrashAnalysis Analyze(int exitCode, string crashReport, string gameLog, string preCrash,
        string modsDir)
    {
        var text = string.Join("\n", crashReport, gameLog, preCrash);
        var result = new CrashAnalysis();

        // 1. 先看退出代码，这类错误日志里往往没有可用信息
        ApplyExitCode(exitCode, result);

        // 2. 再按日志里的典型关键字补充分析
        ApplyTextRules(text, result);

        // 3. 找出被崩溃报告点名的模组，找不到就列最近改动的模组
        CollectSuspectMods(crashReport, text, modsDir, result);

        if (result.Summary.Length == 0)
        {
            result.Summary = "未匹配到常见崩溃特征，可能是偶发的内存/驱动问题";
            result.Suggestion = "先尝试重启游戏；仍反复崩溃可关闭光影或逐个禁用最近安装的模组。";
        }
        return result;
    }

    /// <summary>退出代码对应的问题。</summary>
    private static void ApplyExitCode(int exitCode, CrashAnalysis result)
    {
        switch ((uint)exitCode)
        {
            case 0xC0000005:
                result.Summary = "游戏进程发生内存访问冲突（0xC0000005），多数与显卡驱动、光影或原生模组有关";
                result.Suggestion = "更新显卡驱动；暂时关闭光影；若最近装了带原生库的模组，先禁用它们。";
                result.Findings.Add(new CrashFinding
                {
                    Title = "内存访问冲突",
                    Detail = "这类崩溃 Java 层通常没有堆栈，常见于 OpenGL / 光影 / 原生库模组。",
                    Severity = CrashSeverity.Critical
                });
                break;
            case 0xC0000409:
                result.Summary = "游戏进程被系统强制终止（0xC0000409），通常是原生代码或驱动异常";
                result.Suggestion = "关闭光影与超频设置，更新显卡驱动后重试。";
                result.Findings.Add(new CrashFinding { Title = "系统强制终止进程", Severity = CrashSeverity.Critical });
                break;
            case 0xC00000FD:
                result.Summary = "栈溢出（0xC00000FD），常见于模组递归调用或内存参数异常";
                result.Suggestion = "检查最近安装的模组；适当降低内存分配后重试。";
                result.Findings.Add(new CrashFinding { Title = "栈溢出", Severity = CrashSeverity.Critical });
                break;
        }
    }

    /// <summary>日志关键字规则。</summary>
    private static void ApplyTextRules(string text, CrashAnalysis result)
    {
        if (text.Length == 0) return;

        void Add(string title, string detail, CrashSeverity severity, string summary, string suggestion)
        {
            result.Findings.Add(new CrashFinding { Title = title, Detail = detail, Severity = severity });
            if (result.Summary.Length == 0)
            {
                result.Summary = summary;
                result.Suggestion = suggestion;
            }
        }

        if (Contains(text, "OutOfMemoryError"))
            Add("内存不足", "Java 堆内存耗尽，通常是分配的内存太小或模组 + 光影占用过高。",
                CrashSeverity.Critical, "游戏内存不足（OutOfMemoryError）",
                "在主页把内存调大（建议 4G 以上），并降低渲染距离 / 关闭部分光影。");

        if (Contains(text, "UnsupportedClassVersionError") || Contains(text, "class file version"))
            Add("Java 版本不匹配", "模组或加载器要求的 Java 版本高于当前使用的 Java。",
                CrashSeverity.Critical, "Java 版本与游戏 / 模组不匹配",
                "在设置里改用与游戏版本匹配的 Java（1.20.5 及以上用 Java 21）。");

        if (Contains(text, "Missing or unsupported mandatory dependencies") ||
            Contains(text, "Mod requirements are not met") ||
            Contains(text, "Missing dependencies"))
            Add("缺少前置模组", "加载器提示缺少必需的依赖模组。",
                CrashSeverity.Critical, "缺少前置模组，游戏在加载阶段退出",
                "按日志里列出的模组名补齐前置；在下载页重新下载该模组时勾选自动补齐。");

        if (Contains(text, "Duplicate mods") || Contains(text, "duplicate mod"))
            Add("模组重复", "mods 目录里存在同一模组的多个版本。",
                CrashSeverity.Critical, "mods 目录存在重复模组",
                "删除旧版本，只保留一个版本的该模组。");

        if (Contains(text, "Mixin apply failed") || Contains(text, "MixinApplyError") ||
            Contains(text, "MixinTransformerError"))
            Add("Mixin 注入失败", "模组修改游戏代码时失败，常见于模组与游戏版本 / 加载器不匹配。",
                CrashSeverity.Critical, "模组 Mixin 注入失败，通常是版本不兼容",
                "确认模组适配当前游戏版本与加载器；更新或移除报错的模组。");

        if (Contains(text, "NoSuchMethodError") || Contains(text, "NoSuchFieldError") ||
            Contains(text, "AbstractMethodError"))
            Add("模组版本不兼容", "调用了不存在的游戏方法，说明模组与当前游戏版本不匹配。",
                CrashSeverity.Warning, "有模组与当前游戏版本不兼容",
                "更新报错模组到适配当前版本的版本，或改用兼容的加载器版本。");

        if (Contains(text, "NoClassDefFoundError") || Contains(text, "ClassNotFoundException"))
            Add("缺少类 / 依赖库", "模组依赖的类不存在，可能是前置没装或依赖库损坏。",
                CrashSeverity.Warning, "缺少依赖类，可能是前置未安装完整",
                "补齐前置模组，必要时删除并重新下载相关模组。");

        if (Contains(text, "Pixel format not accelerated") || Contains(text, "GLFW error") ||
            Contains(text, "Failed to create window"))
            Add("显卡 / 驱动问题", "游戏无法创建 OpenGL 窗口，通常是显卡驱动过旧或远程桌面环境。",
                CrashSeverity.Critical, "显卡驱动或 OpenGL 环境异常",
                "更新显卡驱动；避免在远程桌面 / 虚拟机里启动游戏。");

        if (Contains(text, "AuthenticationException") || Contains(text, "SSLHandshakeException") ||
            Contains(text, "Failed to verify username"))
            Add("正版验证失败", "连接微软 / Mojang 验证服务器失败，通常是网络问题。",
                CrashSeverity.Warning, "正版验证联网失败",
                "检查网络或代理；也可以先用离线模式进入游戏。");
    }

    /// <summary>从崩溃报告里提取被点名的模组，找不到就列出最近改动的模组。</summary>
    private static void CollectSuspectMods(string crashReport, string allText, string modsDir, CrashAnalysis result)
    {
        var suspects = new List<string>();

        // Forge / NeoForge 崩溃报告里的 Suspected Mods / Mod File 段落
        foreach (Match match in Regex.Matches(crashReport,
                     @"(?im)^\s*(?:Suspected Mods?|Suspect)\s*[:：]\s*(.+)$"))
        {
            var value = match.Groups[1].Value.Trim();
            if (value.Length == 0 || value.Equals("None", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var part in value.Split(','))
            {
                var name = part.Trim();
                if (name.Length == 0) continue;
                var paren = name.IndexOf('(');
                if (paren > 0) name = name[..paren].Trim();
                if (name.Length > 0 && !suspects.Contains(name, StringComparer.OrdinalIgnoreCase))
                    suspects.Add(name);
            }
        }

        // 崩溃报告 / 日志里出现的 mods\xxx.jar 路径
        foreach (Match match in Regex.Matches(allText, @"(?:mods[\\/])([^\\/\s""']+\.jar)",
                     RegexOptions.IgnoreCase))
        {
            var file = match.Groups[1].Value;
            if (!suspects.Contains(file, StringComparer.OrdinalIgnoreCase)) suspects.Add(file);
        }

        // 崩溃报告里没有点名时，用最近安装 / 更新过的模组作为参考
        if (suspects.Count == 0 && Directory.Exists(modsDir))
        {
            try
            {
                var recent = Directory.EnumerateFiles(modsDir, "*.jar")
                    .Select(path => new FileInfo(path))
                    .Where(info => (DateTime.Now - info.LastWriteTime).TotalDays < 3)
                    .OrderByDescending(info => info.LastWriteTime)
                    .Take(3)
                    .Select(info => info.Name)
                    .ToList();
                if (recent.Count > 0)
                {
                    suspects.AddRange(recent);
                    result.Findings.Add(new CrashFinding
                    {
                        Title = "最近安装 / 更新过的模组",
                        Detail = "崩溃报告没有明确点名，以下模组是近 3 天内改动过的，可优先排查。",
                        Severity = CrashSeverity.Info
                    });
                }
            }
            catch { }
        }

        foreach (var suspect in suspects.Take(8)) result.SuspectMods.Add(suspect);
    }

    private static bool Contains(string text, string keyword) =>
        text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
}
