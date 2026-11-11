using System.Text.Json;
using AlleyWatch.Worker.Security;
using Google.Apis.Util.Store;

namespace AlleyWatch.Worker.Notification;

/// <summary>
/// Google.Apis.Auth's IDataStore, backed by DPAPI-protected files (one per key) instead of the default
/// FileDataStore's plaintext JSON. Plugging into IDataStore means the Google auth library's own
/// automatic-refresh machinery keeps working unchanged — this only changes how the refresh token is
/// stored at rest, not how it's used.
/// </summary>
public sealed class GmailTokenStore : IDataStore
{
    private readonly string _directory;

    public GmailTokenStore(string directory)
    {
        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
    }

    public Task StoreAsync<T>(string key, T value)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value);
        var protectedBytes = DpapiProtector.Protect(json);
        File.WriteAllBytes(PathFor(key), protectedBytes);
        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return Task.FromResult(default(T)!);
        }

        var protectedBytes = File.ReadAllBytes(path);
        var json = DpapiProtector.Unprotect(protectedBytes);
        return Task.FromResult(JsonSerializer.Deserialize<T>(json)!);
    }

    public Task ClearAsync()
    {
        if (Directory.Exists(_directory))
        {
            foreach (var file in Directory.GetFiles(_directory))
            {
                File.Delete(file);
            }
        }
        return Task.CompletedTask;
    }

    private string PathFor(string key) => Path.Combine(_directory, $"{SanitizeKey(key)}.bin");

    private static string SanitizeKey(string key)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            key = key.Replace(invalidChar, '_');
        }
        return key;
    }
}
