using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MCTL.Core;

public class AppEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Arguments { get; set; } = "";
}

/// <summary>类似 Steam 的“库”：添加任意 exe / 快捷方式并启动。</summary>
public static class AppLibrary
{
    private static readonly string FilePath = System.IO.Path.Combine(Settings.AppDir, "app-library.json");
    public static List<AppEntry> Items { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Items = JsonSerializer.Deserialize<List<AppEntry>>(File.ReadAllText(FilePath)) ?? new List<AppEntry>();
        }
        catch (Exception ex)
        {
            Logger.Warn("读取应用库失败：" + ex.Message);
            Items = new List<AppEntry>();
        }
    }

    public static void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Items, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Logger.Warn("保存应用库失败：" + ex.Message);
        }
    }

    public static AppEntry Add(string path)
    {
        var entry = new AppEntry
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(path),
            Path = path
        };
        Items.Add(entry);
        Save();
        return entry;
    }

    public static void Remove(AppEntry entry)
    {
        Items.Remove(entry);
        Save();
    }

    public static void Launch(AppEntry entry)
    {
        try
        {
            if (!File.Exists(entry.Path))
                throw new FileNotFoundException("文件不存在：" + entry.Path);

            var psi = new ProcessStartInfo(entry.Path)
            {
                UseShellExecute = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(entry.Path) ?? Settings.AppDir,
                Arguments = entry.Arguments ?? ""
            };
            Process.Start(psi);
            Logger.Info($"已启动外部程序：{entry.Name}（{entry.Path}）");
        }
        catch (Exception ex)
        {
            Logger.Error("启动外部程序失败：" + ex.Message);
            MessageBox.Show(ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>从 exe 中提取图标，失败返回 null。</summary>
    public static ImageSource GetIcon(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (ExtractIconEx(path, 0, out var large, out var small, 1) == 0) return null;
            var icon = large != IntPtr.Zero ? large : small;
            if (icon == IntPtr.Zero) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            if (large != IntPtr.Zero) DestroyIcon(large);
            if (small != IntPtr.Zero) DestroyIcon(small);
            return source;
        }
        catch
        {
            return null;
        }
    }
}
