using System.IO;
using System.Linq;

namespace AuroraLauncher.Core.Mods;

/// <summary>模组的英文名 / 中文名。任一项为空时对应模式不生效。</summary>
public record ModNameInfo(string English, string Chinese);

/// <summary>
/// 下载后的模组文件名前缀（参考 PCL：默认加中文前缀，可在设置里关闭）。
/// 只作用于新下载的文件，不重命名已有文件。
/// </summary>
public static class ModNamePrefix
{
    public const string Chinese = "chinese";
    public const string English = "english";
    public const string None = "none";

    /// <summary>
    /// chinese → "[中文名] 原文件名"；english → "[英文名] 原文件名"；none 或查不到 → 原文件名。
    /// </summary>
    public static string Apply(string originalFileName, ModNameInfo info, string prefixMode)
    {
        if (string.IsNullOrWhiteSpace(originalFileName) || info == null) return originalFileName;

        var mode = (prefixMode ?? None).Trim().ToLowerInvariant();
        if (mode != Chinese && mode != English) return originalFileName;

        var name = mode == Chinese ? info.Chinese : info.English;
        if (string.IsNullOrWhiteSpace(name)) return originalFileName;

        var safe = Sanitize(name);
        if (safe.Length == 0) return originalFileName;

        return $"[{safe}] {originalFileName}";
    }

    /// <summary>把文件名非法字符替换为下划线，并去掉首尾空白与结尾的点。</summary>
    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var buffer = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
            buffer[i] = invalid.Contains(name[i]) ? '_' : name[i];
        return new string(buffer).Trim().TrimEnd('.');
    }

    /// <summary>按当前中文名表查询中英文名。</summary>
    public static ModNameInfo Describe(ModSource source, string id, string slug, string displayName)
    {
        var chinese = ChineseNameTable.Lookup(source, id, slug, displayName);
        if (string.IsNullOrWhiteSpace(chinese) && ContainsChinese(displayName))
            chinese = displayName.Trim();

        var english = !string.IsNullOrWhiteSpace(displayName) && IsAscii(displayName)
            ? displayName.Trim()
            : slug;
        return new ModNameInfo(english, chinese);
    }

    private static bool ContainsChinese(string text) =>
        !string.IsNullOrEmpty(text) && text.Any(c => c >= 0x4E00 && c <= 0x9FFF);

    private static bool IsAscii(string text) =>
        !string.IsNullOrEmpty(text) && text.All(c => c < 128);
}
