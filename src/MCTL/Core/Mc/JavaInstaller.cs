using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace MCTL.Core.Mc;

/// <summary>
/// 自动下载并安装 Java：使用 Adoptium Temurin JRE（Windows x64），
/// 解压到 启动器目录\java\&lt;版本&gt;，并注册到 Java 列表。
/// </summary>
public static class JavaInstaller
{
    private const string Api =
        "https://api.adoptium.net/v3/assets/latest/{0}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse";

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static string Root => Path.Combine(Settings.AppDir, "java");

    public static async Task<JavaInfo> InstallAsync(int major,
        IProgress<(double Percent, string Message)> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Root);

        progress.Report((1, $"正在查询 Java {major}（Adoptium Temurin JRE）…"));
        using var doc = JsonDocument.Parse(await Client.GetStringAsync(string.Format(Api, major), ct));
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            throw new Exception($"没有找到适用于 Windows x64 的 Java {major} 安装包。");

        var asset = doc.RootElement[0];
        var release = asset.TryGetProperty("release_name", out var rn) ? rn.GetString() : null;
        release = string.IsNullOrWhiteSpace(release) ? $"temurin-{major}" : release;
        var package = asset.GetProperty("binary").GetProperty("package");
        var link = package.GetProperty("link").GetString();
        var checksum = package.TryGetProperty("checksum", out var c) ? c.GetString() : null;
        if (string.IsNullOrWhiteSpace(link)) throw new Exception("安装包下载地址为空。");

        var folder = Path.Combine(Root, release);
        var archive = Path.Combine(Root, release + ".zip");

        progress.Report((2, $"下载 {release}…"));
        var download = new Progress<double>(p =>
            progress.Report((2 + p * 88, $"下载 {release}… {p * 100:0.0}%")));
        await McApi.DownloadAsync(link, archive, null, ct, download);

        if (!string.IsNullOrWhiteSpace(checksum))
        {
            progress.Report((91, "校验安装包…"));
            var actual = await Sha256Async(archive, ct);
            if (!actual.Equals(checksum, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Java 安装包校验失败（SHA-256 不匹配），已中止。");
        }

        progress.Report((93, "解压运行时…"));
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        ZipFile.ExtractToDirectory(archive, folder);
        try { File.Delete(archive); } catch { }

        var java = FindJavaExe(folder);
        if (java == null) throw new Exception("解压后没有找到 bin\\java.exe。");

        progress.Report((98, "验证运行时…"));
        var info = JavaFinder.Probe(java) ?? throw new Exception("新安装的 Java 无法运行。");
        JavaFinder.FindAll(true);
        progress.Report((100, $"已安装 Java {info.Major}"));
        return info;
    }

    private static string FindJavaExe(string root)
    {
        var direct = Path.Combine(root, "bin", "java.exe");
        if (File.Exists(direct)) return direct;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            {
                var exe = Path.Combine(dir, "bin", "java.exe");
                if (File.Exists(exe)) return exe;
            }
        }
        catch { }
        return null;
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash);
    }
}
