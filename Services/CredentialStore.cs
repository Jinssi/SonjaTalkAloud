using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sonja.ReadAloud.Services;

/// Stores the Azure Speech key encrypted for the current Windows account (DPAPI).
public sealed class CredentialStore
{
    private readonly string _path;

    public CredentialStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sonja.ReadAloud",
            "credentials.dat"))
    {
    }

    internal CredentialStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public SpeechCredentials? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var protectedBytes = File.ReadAllBytes(_path);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<SpeechCredentials>(bytes);
        }
        catch
        {
            return null;
        }
    }

    public void Save(SpeechCredentials credentials)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(credentials);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        var temporaryPath = _path + ".tmp";
        File.WriteAllBytes(temporaryPath, protectedBytes);
        File.Move(temporaryPath, _path, true);
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
