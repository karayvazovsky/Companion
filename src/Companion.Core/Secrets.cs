using System.Security.Cryptography;
using System.Text;

namespace Companion.Core;

public interface ISecretProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

/// <summary>Windows DPAPI, CurrentUser scope: only this Windows account can decrypt.</summary>
public sealed class DpapiProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "Companion.v1"u8.ToArray();

    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}

public sealed class SecretStore(string path, ISecretProtector protector, ILog log)
{
    /// <summary>True only when a key file exists and decrypts; an unreadable key counts as missing.</summary>
    public bool HasKey => Load(logFailure: false) is not null;

    public string? LoadApiKey() => Load(logFailure: true);

    private string? Load(bool logFailure)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return Encoding.UTF8.GetString(protector.Unprotect(File.ReadAllBytes(path)));
        }
        catch (CryptographicException ex)
        {
            if (logFailure) log.Error("Could not decrypt the stored API key.", ex);
            return null;
        }
    }

    public void SaveApiKey(string apiKey)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, protector.Protect(Encoding.UTF8.GetBytes(apiKey.Trim())));
        File.Move(temp, path, overwrite: true);
    }

    public void Clear()
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
