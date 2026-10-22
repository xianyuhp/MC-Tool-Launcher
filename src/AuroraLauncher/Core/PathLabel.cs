using System.IO;

namespace AuroraLauncher.Core;

/// <summary>目录显示名：多个 .minecraft 目录时用「所在文件夹」区分，而不是全部显示 .minecraft。</summary>
public static class PathLabel
{
    public static string ForGameDir(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return "";
        var trimmed = dir.TrimEnd('\\', '/');
        try
        {
            var parent = Directory.GetParent(trimmed)?.Name;
            var leaf = Path.GetFileName(trimmed);
            if (!string.IsNullOrEmpty(parent)) return parent;
            return string.IsNullOrEmpty(leaf) ? trimmed : leaf;
        }
        catch
        {
            return trimmed;
        }
    }
}
