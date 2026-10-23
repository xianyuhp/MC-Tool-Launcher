using System.IO;
using AuroraLauncher.Core.Mc;

namespace AuroraLauncher.Core.Mods;

/// <summary>
/// 前置模组安装：把缺失的依赖逐个下载到 mods 目录，并把每个文件的下载进度
/// 折算进整次安装的总进度，避免界面长时间停在 0% 让人以为卡死。
/// </summary>
public static class ModDependencyInstaller
{
    /// <summary>
    /// 安装前置模组。进度区间为 [start, start + span]，返回下载失败的数量。
    /// </summary>
    public static async Task<int> InstallAsync(IReadOnlyList<DependencyItem> dependencies, string modsDir,
        IProgress<string> log, IProgress<double> progress, double start, double span, CancellationToken ct)
    {
        if (dependencies.Count == 0) return 0;

        var failed = 0;
        for (var i = 0; i < dependencies.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var dependency = dependencies[i];

            // 与原模组相同的中文文件名前缀规则，保证文件名风格一致
            var fileName = ModNamePrefix.Apply(
                dependency.File.FileName,
                ModNamePrefix.Describe(dependency.Source, dependency.ProjectId, "", dependency.Name),
                Settings.Current.ModPrefixMode);
            var target = Path.Combine(modsDir, fileName);

            log.Report($"补齐前置 {i + 1}/{dependencies.Count}：{dependency.Name} → {fileName}");

            // 每个前置占用一段进度，文件内部的进度再映射到这一段里
            var sliceStart = start + span * i / dependencies.Count;
            var sliceSpan = span / dependencies.Count;
            var fileProgress = new Progress<double>(p => progress.Report(sliceStart + p * sliceSpan));

            try
            {
                await McApi.DownloadAsync(dependency.File.Url, target, dependency.File.Sha1, ct, fileProgress);
                log.Report("前置已安装：" + fileName);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                log.Report($"前置安装失败：{dependency.Name}（{ex.Message}）");
            }
        }

        progress.Report(start + span);
        return failed;
    }
}
