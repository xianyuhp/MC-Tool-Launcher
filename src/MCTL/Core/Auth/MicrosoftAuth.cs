using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace MCTL.Core.Auth;

public record MsDeviceCode(string UserCode, string VerificationUri, string VerificationUriComplete,
    string DeviceCode, int Interval, int ExpiresIn);

public record MinecraftAccount(string Name, string Uuid, string AccessToken, string RefreshToken, long ExpiresAt)
{
    public bool IsExpired => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > ExpiresAt - 60_000;
}

/// <summary>微软正版登录：设备代码流 + Xbox Live / XSTS / Minecraft Services 链路。</summary>
public static class MicrosoftAuth
{
    // 客户端 ID 由使用者自行申请（Azure 应用注册，设备代码流无需密钥），开源版本不内置。
    // 填写位置：config/api-keys.json 的 MicrosoftClientId，或环境变量 MCTL_MS_CLIENT_ID。
    private static string ClientId => ApiKeys.MicrosoftClientId;
    private const string Scope = "XboxLive.signin offline_access";
    private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(45) };

    public static async Task<MsDeviceCode> RequestDeviceCodeAsync(CancellationToken ct)
    {
        if (!ApiKeys.MicrosoftReady)
            throw new Exception("尚未配置微软客户端 ID。\n请在「设置 → API 凭据」中填写自己申请的应用 ID，" +
                                $"或编辑：{ApiKeys.FilePath}");

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = Scope
        });
        using var response = await Client.PostAsync(DeviceCodeUrl, form, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new Exception("获取登录代码失败：" + text);

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        return new MsDeviceCode(
            root.GetProperty("user_code").GetString(),
            root.GetProperty("verification_uri").GetString(),
            root.TryGetProperty("verification_uri_complete", out var vc) ? vc.GetString() : root.GetProperty("verification_uri").GetString(),
            root.GetProperty("device_code").GetString(),
            root.TryGetProperty("interval", out var i) ? i.GetInt32() : 5,
            root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 900);
    }

    /// <summary>轮询设备代码登录结果，成功后继续完成 Xbox/Minecraft 认证。</summary>
    public static async Task<MinecraftAccount> WaitForLoginAsync(MsDeviceCode code, IProgress<string> status,
        CancellationToken ct)
    {
        var interval = Math.Max(2, code.Interval);
        var deadline = DateTime.UtcNow.AddSeconds(code.ExpiresIn);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            status?.Report("等待浏览器中完成登录…");

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = ClientId,
                ["device_code"] = code.DeviceCode
            });
            using var response = await Client.PostAsync(TokenUrl, form, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                var msAccess = root.GetProperty("access_token").GetString();
                var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : "";
                return await AuthenticateMinecraftAsync(msAccess, refresh, ct);
            }

            using var errDoc = JsonDocument.Parse(text);
            var error = errDoc.RootElement.TryGetProperty("error", out var er) ? er.GetString() : "unknown";
            switch (error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += 5;
                    continue;
                case "expired_token":
                    throw new Exception("登录代码已过期，请重新登录。");
                case "authorization_declined":
                    throw new Exception("登录已被取消。");
                default:
                    var desc = errDoc.RootElement.TryGetProperty("error_description", out var d) ? d.GetString() : text;
                    throw new Exception("登录失败：" + desc);
            }
        }
        throw new Exception("登录超时，请重试。");
    }

    /// <summary>使用 refresh_token 续期并重新完成 Xbox / Minecraft 认证。</summary>
    public static async Task<MinecraftAccount> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["refresh_token"] = refreshToken,
            ["scope"] = Scope
        });
        using var response = await Client.PostAsync(TokenUrl, form, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new Exception("刷新登录状态失败，请重新登录。");

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var msAccess = root.GetProperty("access_token").GetString();
        var newRefresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : refreshToken;
        return await AuthenticateMinecraftAsync(msAccess, newRefresh, ct);
    }

    private static async Task<MinecraftAccount> AuthenticateMinecraftAsync(string msAccessToken, string refreshToken,
        CancellationToken ct)
    {
        // 1) Xbox Live
        var xblBody = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = "d=" + msAccessToken
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };
        using var xblDoc = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate", xblBody, ct);
        var xblRoot = xblDoc.RootElement;
        var xblToken = xblRoot.GetProperty("Token").GetString();
        var uhs = xblRoot.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString();

        // 2) XSTS
        var xstsBody = new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xblToken } },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };
        using var xstsRequest = new HttpRequestMessage(HttpMethod.Post, "https://xsts.auth.xboxlive.com/xsts/authorize")
        {
            Content = new StringContent(JsonSerializer.Serialize(xstsBody), Encoding.UTF8, "application/json")
        };
        using var xstsResponse = await Client.SendAsync(xstsRequest, ct);
        var xstsText = await xstsResponse.Content.ReadAsStringAsync(ct);
        if (!xstsResponse.IsSuccessStatusCode)
        {
            var code = TryReadXErr(xstsText);
            throw new Exception(code switch
            {
                "2148916233" => "该微软账号尚未创建 Xbox 档案，请先登录 xbox.com 创建。",
                "2148916235" => "该账号所在地区不支持 Xbox Live。",
                "2148916238" => "未成年账号需要先加入家庭组。",
                _ => "Xbox 认证失败：" + xstsText
            });
        }
        using var xstsDoc = JsonDocument.Parse(xstsText);
        var xstsRoot = xstsDoc.RootElement;
        var xstsToken = xstsRoot.GetProperty("Token").GetString();
        var uhs2 = xstsRoot.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString()
                   ?? uhs;

        // 3) Minecraft Services
        var mcBody = new { identityToken = $"XBL3.0 x={uhs2};{xstsToken}" };
        using var mcDoc = await PostJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", mcBody, ct);
        var mcRoot = mcDoc.RootElement;
        var accessToken = mcRoot.GetProperty("access_token").GetString();
        var expiresIn = mcRoot.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 86400;

        // 4) 玩家档案
        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        profileRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var profileResponse = await Client.SendAsync(profileRequest, ct);
        var profileText = await profileResponse.Content.ReadAsStringAsync(ct);
        if (!profileResponse.IsSuccessStatusCode)
            throw new Exception("该微软账号未拥有 Minecraft（Java 版），无法用于正版登录。");

        using var profileDoc = JsonDocument.Parse(profileText);
        var profile = profileDoc.RootElement;
        var name = profile.GetProperty("name").GetString();
        var uuid = profile.GetProperty("id").GetString();

        return new MinecraftAccount(name, uuid, accessToken, refreshToken,
            DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToUnixTimeMilliseconds());
    }

    private static async Task<JsonDocument> PostJsonAsync(string url, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await Client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new Exception("认证失败：" + text);
        return JsonDocument.Parse(text);
    }

    private static string TryReadXErr(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("XErr", out var x) ? x.GetInt64().ToString() : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>账号管理：保存 / 续期 / 退出。</summary>
public static class AccountService
{
    public static MinecraftAccount CurrentAccount { get; private set; }

    /// <summary>登录状态变化（登录、续期、退出）。</summary>
    public static event Action Changed;

    public static async Task<bool> TryRestoreAsync()
    {
        var s = Settings.Current;
        if (string.IsNullOrEmpty(s.MsRefreshToken)) return false;

        try
        {
            var account = await MicrosoftAuth.RefreshAsync(s.MsRefreshToken);
            Apply(account);
            Logger.Info($"已恢复正版账号：{account.Name}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn("恢复正版账号失败：" + ex.Message);
            return false;
        }
    }

    public static async Task<MinecraftAccount> EnsureValidAsync()
    {
        var s = Settings.Current;
        if (CurrentAccount == null && !string.IsNullOrEmpty(s.MsRefreshToken))
        {
            var account = await MicrosoftAuth.RefreshAsync(s.MsRefreshToken);
            Apply(account);
        }
        return CurrentAccount;
    }

    public static void Apply(MinecraftAccount account)
    {
        CurrentAccount = account;
        Settings.Current.MsAccessToken = account.AccessToken;
        Settings.Current.MsRefreshToken = account.RefreshToken;
        Settings.Current.MsName = account.Name;
        Settings.Current.MsUuid = account.Uuid;
        Settings.Current.MsExpiresAt = account.ExpiresAt;
        Settings.Save();
        Changed?.Invoke();
    }

    public static void Logout()
    {
        CurrentAccount = null;
        Settings.Current.MsAccessToken = "";
        Settings.Current.MsRefreshToken = "";
        Settings.Current.MsName = "";
        Settings.Current.MsUuid = "";
        Settings.Current.MsExpiresAt = 0;
        Settings.Save();
        Changed?.Invoke();
    }
}
