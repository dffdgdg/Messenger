#if ANDROID
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Core.Infrastructure.Helpers;

namespace Core.Services.Core.Auth;

public sealed class AndroidSecureStorageService : ISecureStorageService, IDisposable
{
    private const string KeyAlias = "messenger_secure_key";
    private const string KeystoreProvider = "AndroidKeyStore";
    private const string TransformationAes = "AES/GCM/NoPadding";
    private const int GcmTagLength = 128;
    private const int GcmIvLength = 12;

    private readonly string _storagePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public AndroidSecureStorageService()
    {
        _storagePath = Path.Combine(AppPaths.GetAppDataDirectory(), "SecureStorage");
        Directory.CreateDirectory(_storagePath);
        EnsureKeyExists();
    }

    // ── Android Keystore ──────────────────────────────────────────────────────

    private static void EnsureKeyExists()
    {
        var keyStore = KeyStore.GetInstance(KeystoreProvider)!;
        keyStore.Load(null);

        if (keyStore.ContainsAlias(KeyAlias)) return;

        var keyGenerator = KeyGenerator.GetInstance(
            KeyProperties.KeyAlgorithmAes,
            KeystoreProvider)!;

        var spec = new KeyGenParameterSpec.Builder(
                KeyAlias,
                KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)!
            .SetBlockModes(KeyProperties.BlockModeGcm)!
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!
            .SetKeySize(256)!
            .Build()!;

        keyGenerator.Init(spec);
        keyGenerator.GenerateKey();

        Debug.WriteLine("[AndroidSecureStorage] AES-256 key created in Keystore");
    }

    private static ISecretKey GetKey()
    {
        var keyStore = KeyStore.GetInstance(KeystoreProvider)!;
        keyStore.Load(null);
        return (ISecretKey)keyStore.GetKey(KeyAlias, null)!;
    }

    // ── Encrypt / Decrypt ─────────────────────────────────────────────────────

    private static byte[] Encrypt(string plainText)
    {
        var cipher = Cipher.GetInstance(TransformationAes)!;
        cipher.Init(CipherMode.EncryptMode, GetKey());

        var iv = cipher.GetIV()!; // GCM генерирует IV автоматически
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = cipher.DoFinal(plainBytes)!;

        // Формат: [iv (12 байт)][encrypted]
        var result = new byte[GcmIvLength + encrypted.Length];
        Array.Copy(iv, 0, result, 0, GcmIvLength);
        Array.Copy(encrypted, 0, result, GcmIvLength, encrypted.Length);

        return result;
    }

    private static string Decrypt(byte[] data)
    {
        if (data.Length <= GcmIvLength)
            throw new InvalidOperationException("Invalid encrypted data length");

        var iv = new byte[GcmIvLength];
        Array.Copy(data, 0, iv, 0, GcmIvLength);

        var encrypted = new byte[data.Length - GcmIvLength];
        Array.Copy(data, GcmIvLength, encrypted, 0, encrypted.Length);

        var cipher = Cipher.GetInstance(TransformationAes)!;
        var spec = new GCMParameterSpec(GcmTagLength, iv);
        cipher.Init(CipherMode.DecryptMode, GetKey(), spec);

        var decrypted = cipher.DoFinal(encrypted)!;
        return Encoding.UTF8.GetString(decrypted);
    }

    // ── ISecureStorageService ─────────────────────────────────────────────────

    public async Task SaveAsync<T>(string key, T value)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(value);
            var encrypted = Encrypt(json);
            await File.WriteAllBytesAsync(GetFilePath(key), encrypted);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AndroidSecureStorage] SaveAsync error: {ex.Message}");
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync();
        try
        {
            var filePath = GetFilePath(key);
            if (!File.Exists(filePath)) return default;

            var encrypted = await File.ReadAllBytesAsync(filePath);
            var json = Decrypt(encrypted);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AndroidSecureStorage] GetAsync error: {ex.Message}");
            // При ошибке расшифровки удаляем повреждённый файл
            await RemoveInternalAsync(key);
            return default;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveAsync(string key)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync();
        try { await RemoveInternalAsync(key); }
        finally { _lock.Release(); }
    }

    private Task RemoveInternalAsync(string key)
    {
        try
        {
            var path = GetFilePath(key);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AndroidSecureStorage] RemoveAsync error: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    public Task<bool> ContainsKeyAsync(string key)
    {
        ThrowIfDisposed();
        return Task.FromResult(File.Exists(GetFilePath(key)));
    }

    private string GetFilePath(string key)
    {
        var safeKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(key))
            .Replace('/', '_').Replace('+', '-').Replace('=', '.');
        return Path.Combine(_storagePath, $"{safeKey}.secure");
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, nameof(AndroidSecureStorageService));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
    }
}
#endif