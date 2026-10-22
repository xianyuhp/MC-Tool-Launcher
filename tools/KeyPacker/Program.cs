using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// 把 ApiKeys.local.json 打包成不可直接读取的 ApiKeys.local.bin：
// 紧凑 JSON → AES-256-CBC 加密 → 逐字节掩码 → 三段重排，头部长度字段再异或混淆。
// 与 Core/KeyVault.cs 的解码逻辑一一对应，改动任一侧都要同步另一侧。

if (args.Length < 2)
{
    Console.WriteLine("用法: KeyPacker <ApiKeys.local.json> <ApiKeys.local.bin>");
    return 1;
}

var inputPath = args[0];
var outputPath = args[1];
const int HeaderKeyA = unchecked((int)0x5A5A5A5A);
const int HeaderKeyB = unchecked((int)0xA5A5A5A5);
if (!File.Exists(inputPath))
{
    Console.WriteLine("找不到输入文件：" + inputPath);
    return 1;
}

using var doc = JsonDocument.Parse(File.ReadAllText(inputPath));
string Read(string name) =>
    doc.RootElement.TryGetProperty(name, out var value) ? (value.GetString() ?? "").Trim() : "";

var json = JsonSerializer.SerializeToUtf8Bytes(new
{
    m = Read("MicrosoftClientId"),
    c = Read("CurseForgeApiKey")
});

using var aes = Aes.Create();
aes.KeySize = 256;
aes.Mode = CipherMode.CBC;
aes.Padding = PaddingMode.PKCS7;
aes.Key = KeyVaultKeys.DeriveKey();
aes.GenerateIV();

byte[] cipher;
using (var encryptor = aes.CreateEncryptor())
    cipher = encryptor.TransformFinalBlock(json, 0, json.Length);

var blob = new byte[16 + cipher.Length];
Buffer.BlockCopy(aes.IV, 0, blob, 0, 16);
Buffer.BlockCopy(cipher, 0, blob, 16, cipher.Length);

for (var i = 0; i < blob.Length; i++) blob[i] ^= Mask(i);

var total = blob.Length;
var p0Len = total / 3;
var p1Len = (total - p0Len) / 2;
var p0 = blob[..p0Len];
var p1 = blob[p0Len..(p0Len + p1Len)];
var p2 = blob[(p0Len + p1Len)..];

using var output = new MemoryStream();
output.Write(BitConverter.GetBytes(p0Len ^ HeaderKeyA));
output.Write(BitConverter.GetBytes(p1Len ^ HeaderKeyB));
output.Write(p1);
output.Write(p2);
output.Write(p0);

File.WriteAllBytes(outputPath, output.ToArray());
Console.WriteLine($"packed {json.Length} bytes -> {output.Length} bytes : {outputPath}");
return 0;

static byte Mask(int index)
{
    byte[] table = { 0x3B, 0x91, 0xE4, 0x57, 0x0C, 0xA8, 0x6D };
    return (byte)(table[index % table.Length] + index * 31);
}

internal static class KeyVaultKeys
{
    private static readonly uint[] Parts = { 0x6D2B79F5u, 0x9E3779B9u, 0x85EBCA6Bu, 0xC2B2AE35u };

    public static byte[] DeriveKey()
    {
        var seed = "MCTL/vault/1:" +
                   string.Join(":", Parts.Select(p => (p ^ 0x5A5A5A5A).ToString("X8")));
        return SHA256.HashData(Encoding.UTF8.GetBytes(seed));
    }
}
