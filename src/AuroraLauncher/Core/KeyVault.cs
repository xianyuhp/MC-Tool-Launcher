using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AuroraLauncher.Core;

/// <summary>
/// 正式版内置凭据的解包：AES-256-CBC + 逐字节掩码 + 三段重排。
/// 打包逻辑见 tools/KeyPacker，两侧的密钥派生与掩码表必须保持一致。
/// </summary>
internal static class KeyVault
{
    private static readonly uint[] Parts = { 0x6D2B79F5u, 0x9E3779B9u, 0x85EBCA6Bu, 0xC2B2AE35u };

    // 头部长度字段的异或常量（必须是 int，否则会按 long 参与运算导致 8 字节写入/读取）
    private const int HeaderKeyA = unchecked((int)0x5A5A5A5A);
    private const int HeaderKeyB = unchecked((int)0xA5A5A5A5);

    public static (string MicrosoftClientId, string CurseForgeKey)? Read(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var data = buffer.ToArray();
        if (data.Length < 24) return null;

        var p0Len = BitConverter.ToInt32(data, 0) ^ HeaderKeyA;
        var p1Len = BitConverter.ToInt32(data, 4) ^ HeaderKeyB;
        var rest = data.Length - 8;
        var p2Len = rest - p0Len - p1Len;
        if (p0Len <= 0 || p1Len <= 0 || p2Len <= 0)
        {
            Logger.Warn($"KeyVault 头部异常：len={data.Length} p0={p0Len} p1={p1Len} p2={p2Len}");
            return null;
        }

        // 打包顺序是 p1、p2、p0（头部记 p0/p1 长度），这里按原位置还原
        var blob = new byte[rest];
        Array.Copy(data, 8, blob, p0Len, p1Len);
        Array.Copy(data, 8 + p1Len, blob, p0Len + p1Len, p2Len);
        Array.Copy(data, 8 + p1Len + p2Len, blob, 0, p0Len);

        for (var i = 0; i < blob.Length; i++) blob[i] ^= Mask(i);

        var iv = new byte[16];
        Array.Copy(blob, 0, iv, 0, 16);
        var cipher = new byte[blob.Length - 16];
        Array.Copy(blob, 16, cipher, 0, cipher.Length);

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = DeriveKey();
        aes.IV = iv;
        byte[] plain;
        using (var decryptor = aes.CreateDecryptor())
            plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        using var doc = JsonDocument.Parse(plain);
        var root = doc.RootElement;
        var clientId = root.TryGetProperty("m", out var m) ? (m.GetString() ?? "").Trim() : "";
        var curseForge = root.TryGetProperty("c", out var c) ? (c.GetString() ?? "").Trim() : "";
        return (clientId, curseForge);
    }

    private static byte[] DeriveKey()
    {
        var seed = "MCTL/vault/1:" +
                   string.Join(":", Parts.Select(p => (p ^ 0x5A5A5A5A).ToString("X8")));
        return SHA256.HashData(Encoding.UTF8.GetBytes(seed));
    }

    private static byte Mask(int index)
    {
        byte[] table = { 0x3B, 0x91, 0xE4, 0x57, 0x0C, 0xA8, 0x6D };
        return (byte)(table[index % table.Length] + index * 31);
    }
}
