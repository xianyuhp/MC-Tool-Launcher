using MCTL.Core;

namespace MCTL.Core.Mc;

/// <summary>下载源：默认优先 BMCLAPI 镜像（与 PCL 一致），失败回退官方。</summary>
public static class Mirror
{
    public const string Bmclapi = "https://bmclapi2.bangbang93.com";
    public const string MojangManifest = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";
    public const string FabricMeta = "https://meta.fabricmc.net";
    public const string ForgePromotions = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    public const string ForgeMaven = "https://maven.minecraftforge.net";
    public const string NeoForgeMaven = "https://maven.neoforged.net/releases";

    public static bool PreferMirror =>
        !string.Equals(Settings.Current.DownloadMirror, "official", StringComparison.OrdinalIgnoreCase);

    public static string ManifestUrl(bool useMirror) =>
        useMirror ? $"{Bmclapi}/mc/game/version_manifest_v2.json" : MojangManifest;

    public static string VersionJsonUrl(string id) => $"{Bmclapi}/version/{id}/json";

    public static string AssetObjectUrl(string hash, bool useMirror) => useMirror
        ? $"{Bmclapi}/assets/{hash[..2]}/{hash}"
        : $"https://resources.download.minecraft.net/{hash[..2]}/{hash}";

    public static string FabricLoadersUrl(bool useMirror, string mc) => useMirror
        ? $"{Bmclapi}/fabric-meta/v2/versions/loader/{mc}"
        : $"{FabricMeta}/v2/versions/loader/{mc}";

    public static string FabricProfileUrl(bool useMirror, string mc, string loader) => useMirror
        ? $"{Bmclapi}/fabric-meta/v2/versions/loader/{mc}/{loader}/profile/json"
        : $"{FabricMeta}/v2/versions/loader/{mc}/{loader}/profile/json";

    /// <summary>把官方库地址转换为镜像地址（仅处理 libraries.minecraft.net / resources）。</summary>
    public static string ToMirror(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        if (url.StartsWith("https://libraries.minecraft.net/", StringComparison.OrdinalIgnoreCase))
            return Bmclapi + "/maven/" + url["https://libraries.minecraft.net/".Length..];
        if (url.StartsWith("https://resources.download.minecraft.net/", StringComparison.OrdinalIgnoreCase))
            return Bmclapi + "/assets/" + url["https://resources.download.minecraft.net/".Length..];
        return url;
    }

    /// <summary>把镜像地址还原成官方地址，用于失败回退。</summary>
    public static string ToOfficial(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.StartsWith(Bmclapi + "/maven/", StringComparison.OrdinalIgnoreCase))
            return "https://libraries.minecraft.net/" + url[(Bmclapi + "/maven/").Length..];
        if (url.StartsWith(Bmclapi + "/assets/", StringComparison.OrdinalIgnoreCase))
            return "https://resources.download.minecraft.net/" + url[(Bmclapi + "/assets/").Length..];
        return null;
    }
}
