using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AuroraLauncher.Core;

/// <summary>
/// API 凭据（微软客户端 ID / CurseForge API Key）。
/// 公开仓库不内置任何官方凭据，构建出来的是开源版本：
/// 请把自己的凭据填进 <c>config/api-keys.json</c>，或通过环境变量
/// <c>MCTL_MS_CLIENT_ID</c> / <c>MCTL_CURSEFORGE_KEY</c> 提供。
/// </summary>
public static class ApiKeys
{
    // 首次访问时自动加载，避免调用方忘记先 Load()
    static ApiKeys() => Load();

    public static string MicrosoftClientId { get; private set; } = "";
    public static string CurseForgeKey { get; private set; } = "";

    /// <summary>凭据文件位置（与 settings.json 同目录，已在 .gitignore 中忽略）。</summary>
    public static string FilePath => Path.Combine(Settings.ConfigDir, "api-keys.json");

    public static bool MicrosoftReady => MicrosoftClientId.Length > 0;

    /// <summary>CurseForge Key 也可以只在「设置」里填写，两者任一有值即可。</summary>
    public static bool CurseForgeReady =>
        CurseForgeKey.Length > 0 || !string.IsNullOrWhiteSpace(Settings.Current.CurseForgeApiKey);

    /// <summary>缺少凭据（开源版本），启动时需要提示使用者自己申请。</summary>
    public static bool NeedsSetup => !MicrosoftReady || !CurseForgeReady;

    /// <summary>读取凭据：环境变量 &gt; config/api-keys.json &gt; 构建时内置（正式版）。</summary>
    public static void Load()
    {
        MicrosoftClientId = Env("MCTL_MS_CLIENT_ID");
        CurseForgeKey = Env("MCTL_CURSEFORGE_KEY");

        try
        {
            if (File.Exists(FilePath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                var root = doc.RootElement;
                if (MicrosoftClientId.Length == 0 && root.TryGetProperty("MicrosoftClientId", out var ms))
                    MicrosoftClientId = (ms.GetString() ?? "").Trim();
                if (CurseForgeKey.Length == 0 && root.TryGetProperty("CurseForgeApiKey", out var cf))
                    CurseForgeKey = (cf.GetString() ?? "").Trim();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("读取 api-keys.json 失败：" + ex.Message);
        }

        LoadBuiltIn();
    }

    /// <summary>正式版构建时把加密后的凭据编入程序，这里解出来作为默认值。</summary>
    private static void LoadBuiltIn()
    {
        if (MicrosoftReady && CurseForgeReady) return;
        try
        {
            using var stream = typeof(ApiKeys).Assembly
                .GetManifestResourceStream("AuroraLauncher.ApiKeys.local.bin");
            if (stream == null) return;

            var keys = KeyVault.Read(stream);
            if (keys == null)
            {
                Logger.Warn("内置 API 凭据解码失败，将按开源版本处理");
                return;
            }

            if (MicrosoftClientId.Length == 0) MicrosoftClientId = keys.Value.MicrosoftClientId;
            if (CurseForgeKey.Length == 0) CurseForgeKey = keys.Value.CurseForgeKey;
        }
        catch (Exception ex)
        {
            Logger.Warn("读取内置 API 凭据失败：" + ex.Message);
        }
    }

    /// <summary>保存凭据到 api-keys.json，并立即在当前进程生效。</summary>
    public static void Save(string microsoftClientId, string curseForgeKey)
    {
        MicrosoftClientId = (microsoftClientId ?? "").Trim();
        CurseForgeKey = (curseForgeKey ?? "").Trim();

        Directory.CreateDirectory(Settings.ConfigDir);
        var json = new JsonObject
        {
            ["_说明"] = "这里保存你自己的 API 凭据，请勿提交到公开仓库；已在 .gitignore 中忽略。",
            ["MicrosoftClientId"] = MicrosoftClientId,
            ["CurseForgeApiKey"] = CurseForgeKey
        };
        File.WriteAllText(FilePath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>首次运行时写出一份空白模板，方便直接编辑。</summary>
    public static void EnsureTemplate()
    {
        if (File.Exists(FilePath)) return;
        Save(MicrosoftClientId, CurseForgeKey);
    }

    private static string Env(string name) =>
        (Environment.GetEnvironmentVariable(name) ?? "").Trim();
}
