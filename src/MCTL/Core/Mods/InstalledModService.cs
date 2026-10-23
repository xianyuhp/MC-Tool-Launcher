using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MCTL.Core.Mods;

/// <summary>已安装的模组（本地 mods 文件夹中的一个 jar）。</summary>
public class InstalledMod
{
    public string Path { get; set; } = "";
    public string FileName => System.IO.Path.GetFileName(Path);
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "";
    public string ModId { get; set; } = "";
    public ImageSource Icon { get; set; }
    public long Size { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? FileName : Name;
    public string StatusLabel => Enabled ? "已启用" : "已禁用";
}

/// <summary>本地模组扫描、启停、删除与安装。</summary>
public static class InstalledModService
{
    /// <summary>扫描 mods 目录（.jar 与 .jar.disabled）。</summary>
    public static List<InstalledMod> Scan(string modsDir)
    {
        var result = new List<InstalledMod>();
        if (!Directory.Exists(modsDir)) return result;

        foreach (var path in Directory.EnumerateFiles(modsDir, "*.jar*", SearchOption.TopDirectoryOnly))
        {
            var name = System.IO.Path.GetFileName(path);
            var disabled = name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            if (!disabled && !name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) continue;

            var mod = new InstalledMod
            {
                Path = path,
                Enabled = !disabled,
                Name = System.IO.Path.GetFileNameWithoutExtension(System.IO.Path.GetFileNameWithoutExtension(name)),
                Size = new FileInfo(path).Length
            };
            try { ReadMetadata(mod); } catch { }
            result.Add(mod);
        }
        return result.OrderByDescending(m => m.Enabled).ThenBy(m => m.DisplayName).ToList();
    }

    private static void ReadMetadata(InstalledMod mod)
    {
        using var archive = ZipFile.OpenRead(mod.Path);

        // Fabric / Quilt
        var fabric = archive.GetEntry("fabric.mod.json");
        if (fabric != null)
        {
            using var stream = fabric.Open();
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (root.TryGetProperty("id", out var id)) mod.ModId = id.GetString() ?? "";
            if (root.TryGetProperty("name", out var name)) mod.Name = name.GetString() ?? mod.Name;
            if (root.TryGetProperty("version", out var version)) mod.Version = version.GetString() ?? "";
            if (root.TryGetProperty("description", out var desc)) mod.Description = desc.GetString() ?? "";
            if (root.TryGetProperty("icon", out var icon) && icon.ValueKind == JsonValueKind.String)
                mod.Icon = LoadIcon(archive, icon.GetString());
            return;
        }

        // Forge / NeoForge
        var toml = archive.GetEntry("META-INF/neoforge.mods.toml") ?? archive.GetEntry("META-INF/mods.toml");
        if (toml != null)
        {
            using var reader = new StreamReader(toml.Open());
            var text = reader.ReadToEnd();
            mod.ModId = Match(text, "modId") ?? "";
            mod.Name = Match(text, "displayName") ?? mod.Name;
            mod.Version = Match(text, "version") ?? "";
            mod.Description = MatchBlock(text, "description") ?? "";
            mod.Icon = LoadIcon(archive, "logo.png") ?? LoadIcon(archive, "icon.png");
            return;
        }

        // 旧版 mcmod.info
        var info = archive.GetEntry("mcmod.info");
        if (info != null)
        {
            using var reader = new StreamReader(info.Open());
            var text = reader.ReadToEnd();
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                    ? doc.RootElement[0]
                    : doc.RootElement;
                if (root.TryGetProperty("modid", out var id)) mod.ModId = id.GetString() ?? "";
                if (root.TryGetProperty("name", out var name)) mod.Name = name.GetString() ?? mod.Name;
                if (root.TryGetProperty("version", out var version)) mod.Version = version.GetString() ?? "";
                if (root.TryGetProperty("description", out var desc)) mod.Description = desc.GetString() ?? "";
                if (root.TryGetProperty("logoFile", out var logo)) mod.Icon = LoadIcon(archive, logo.GetString());
            }
            catch { }
        }
    }

    private static string Match(string text, string key)
    {
        var match = Regex.Match(text, $@"(?m)^\s*{key}\s*=\s*""([^""]*)""");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string MatchBlock(string text, string key)
    {
        var block = Regex.Match(text, $@"(?s){key}\s*=\s*'''(.*?)'''");
        if (block.Success) return block.Groups[1].Value.Trim();
        var single = Match(text, key);
        return string.IsNullOrWhiteSpace(single) ? null : single;
    }

    private static ImageSource LoadIcon(ZipArchive archive, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName)) return null;
        var entry = archive.GetEntry(entryName.TrimStart('/'));
        if (entry == null) return null;
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(memory.ToArray());
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>启用 / 禁用：改后缀 .jar ↔ .jar.disabled。</summary>
    public static InstalledMod SetEnabled(InstalledMod mod, bool enabled)
    {
        var target = enabled
            ? (mod.Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? mod.Path[..^".disabled".Length] : mod.Path)
            : (mod.Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? mod.Path : mod.Path + ".disabled");
        if (target != mod.Path && File.Exists(mod.Path))
        {
            if (File.Exists(target)) File.Delete(target);
            File.Move(mod.Path, target);
        }
        mod.Path = target;
        mod.Enabled = enabled;
        return mod;
    }

    public static void Delete(InstalledMod mod)
    {
        if (File.Exists(mod.Path)) File.Delete(mod.Path);
    }

    /// <summary>把外部 jar 复制进 mods 目录（重名自动加序号）。</summary>
    public static string InstallFromFile(string source, string modsDir)
    {
        Directory.CreateDirectory(modsDir);
        var target = Path.Combine(modsDir, Path.GetFileName(source));
        var index = 1;
        while (File.Exists(target))
        {
            var name = Path.GetFileNameWithoutExtension(source);
            target = Path.Combine(modsDir, $"{name} ({index++}){Path.GetExtension(source)}");
        }
        File.Copy(source, target);
        return target;
    }
}
