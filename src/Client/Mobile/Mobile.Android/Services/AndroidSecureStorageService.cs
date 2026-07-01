using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Core.Services.Platform.Abstractions;

namespace Mobile.Android.Services;

public sealed class AndroidSecureStorageService : ISecureStorageService, IDisposable
{
    private readonly string _preferencesName;
    private readonly string _keyAlias;
    private readonly ISharedPreferences _preferences;
    private readonly KeyStore _keyStore;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    private const string AndroidKeyStore = "AndroidKeyStore";
    private const string KeyAlgorithm = KeyProperties.KeyAlgorithmAes;
    private const int KeySize = 256;
    private const string Transformation = "AES/GCM/NoPadding";
    private const string IvKey = "___iv___";

    public AndroidSecureStorageService()
    {
        _preferencesName = "com.companyname.mobile.secure_storage";
        _keyAlias = "com.companyname.mobile.storage_key";

        var context = Application.Context;
        _preferences = context.GetSharedPreferences(_preferencesName, FileCreationMode.Private)!;

        _keyStore = KeyStore.GetInstance(AndroidKeyStore)!;
        _keyStore.Load(null);

        CreateKeyIfNeeded();
    }

    private void CreateKeyIfNeeded()
    {
        if (_keyStore.ContainsAlias(_keyAlias)) return;

        var keyGenerator = KeyGenerator.GetInstance(KeyAlgorithm, AndroidKeyStore)!;
        var builder = new KeyGenParameterSpec.Builder(_keyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetKeySize(KeySize)
            .SetRandomizedEncryptionRequired(false)
            .Build();

        keyGenerator.Init(builder);
        keyGenerator.GenerateKey();
    }

    private IKey GetKey()
    {
        var entry = _keyStore.GetEntry(_keyAlias, null) as KeyStore.SecretKeyEntry;
        return entry?.SecretKey!;
    }

    private byte[] GenerateIv()
    {
        var iv = new byte[12];
        new Java.Security.SecureRandom().NextBytes(iv);
        return iv;
    }

    private byte[] Encrypt(string plainText, out byte[] iv)
    {
        var key = GetKey();
        iv = GenerateIv();

        var cipher = Cipher.GetInstance(Transformation)!;
        // Используем Init с IKey вместо SecretKey
        cipher.Init(CipherMode.EncryptMode, (Java.Security.IKey)key, new GCMParameterSpec(128, iv));

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        return cipher.DoFinal(plainBytes)!;
    }

    private string Decrypt(byte[] cipherText, byte[] iv)
    {
        var key = GetKey();
        var spec = new GCMParameterSpec(128, iv);

        var cipher = Cipher.GetInstance(Transformation)!;
        // Используем Init с IKey вместо SecretKey
        cipher.Init(CipherMode.DecryptMode, (Java.Security.IKey)key, spec);

        var decryptedBytes = cipher.DoFinal(cipherText)!;
        return Encoding.UTF8.GetString(decryptedBytes);
    }

    private void SaveIv(string key, byte[] iv)
    {
        var editor = _preferences.Edit()!;
        editor.PutString($"{key}{IvKey}", Convert.ToBase64String(iv));
        editor.Apply();
    }

    private byte[]? GetIv(string key)
    {
        var ivBase64 = _preferences.GetString($"{key}{IvKey}", null);
        return ivBase64 != null ? Convert.FromBase64String(ivBase64) : null;
    }

    private void RemoveIv(string key)
    {
        var editor = _preferences.Edit()!;
        editor.Remove($"{key}{IvKey}");
        editor.Apply();
    }

    public async Task SaveAsync<T>(string key, T value)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(value);
            var encrypted = Encrypt(json, out var iv);
            var encryptedBase64 = Convert.ToBase64String(encrypted);

            var editor = _preferences.Edit()!;
            editor.PutString(key, encryptedBase64);
            editor.Apply();

            SaveIv(key, iv);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AndroidSecureStorage.SaveAsync error: {ex.Message}");
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
            var encryptedBase64 = _preferences.GetString(key, null);
            if (encryptedBase64 == null) return default;

            var iv = GetIv(key);
            if (iv == null) return default;

            var encrypted = Convert.FromBase64String(encryptedBase64);
            var decrypted = Decrypt(encrypted, iv);
            return JsonSerializer.Deserialize<T>(decrypted);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AndroidSecureStorage.GetAsync error: {ex.Message}");
            await RemoveAsync(key);
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
        try
        {
            RemoveIv(key);
            var editor = _preferences.Edit()!;
            editor.Remove(key);
            editor.Apply();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AndroidSecureStorage.RemoveAsync error: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<bool> ContainsKeyAsync(string key)
    {
        ThrowIfDisposed();
        return Task.FromResult(_preferences.Contains(key));
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