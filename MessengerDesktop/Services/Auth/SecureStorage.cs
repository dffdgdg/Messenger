using MessengerDesktop.Services.Api;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Auth
{
    public interface ISecureStorageService
    {
        Task SaveAsync<T>(string key, T value);
        Task<T?> GetAsync<T>(string key);
        Task RemoveAsync(string key);
        Task<bool> ContainsKeyAsync(string key);
    }

    public class SecureStorageService : ISecureStorageService, IDisposable
    {
        private readonly byte[] _key;
        private readonly string _storagePath;
        private readonly SemaphoreSlim _lock = new(1, 1);

        private const string SaltFileName = ".salt";
        private const int Pbkdf2Iterations = 100_000;

        private bool _disposed;

        public SecureStorageService()
        {
            _storagePath = GetStoragePath();
            Directory.CreateDirectory(_storagePath);
            _key = GetOrCreateKey();
        }

        private byte[] GetOrCreateKey()
        {
            var saltPath = Path.Combine(_storagePath, SaltFileName);
            byte[] salt;

            if (File.Exists(saltPath))
            {
                salt = File.ReadAllBytes(saltPath);

                if (salt.Length != 32)
                {
                    salt = RandomNumberGenerator.GetBytes(32);
                    File.WriteAllBytes(saltPath, salt);
                    SetHiddenAttribute(saltPath);
                }
            }
            else
            {
                salt = RandomNumberGenerator.GetBytes(32);
                File.WriteAllBytes(saltPath, salt);
                SetHiddenAttribute(saltPath);
            }

            var machineData = $"{Environment.MachineName}{Environment.UserName}{Environment.OSVersion}";
            var baseKey = Encoding.UTF8.GetBytes(machineData);

            using var pbkdf2 = new Rfc2898DeriveBytes(baseKey, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
            return pbkdf2.GetBytes(32);
        }

        private static void SetHiddenAttribute(string filePath)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    File.SetAttributes(filePath, FileAttributes.Hidden);
                }
            }
            catch
            {
                // Ignore - not critical
            }
        }

        private static string GetStoragePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "MessengerDesktop", "SecureStorage");
        }

        public async Task SaveAsync<T>(string key, T value)
        {
            ThrowIfDisposed();

            await _lock.WaitAsync();
            try
            {
                var json = JsonSerializer.Serialize(value);
                var encrypted = Encrypt(json);
                var filePath = GetFilePath(key);
                await File.WriteAllBytesAsync(filePath, encrypted);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecureStorage.SaveAsync error: {ex.Message}");
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
                if (!File.Exists(filePath))
                    return default;

                var encrypted = await File.ReadAllBytesAsync(filePath);
                var decrypted = Decrypt(encrypted);
                return JsonSerializer.Deserialize<T>(decrypted);
            }
            catch (CryptographicException ex)
            {
                Debug.WriteLine($"SecureStorage.GetAsync decryption error: {ex.Message}");
                await RemoveInternalAsync(key);
                return default;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecureStorage.GetAsync error: {ex.Message}");
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
                await RemoveInternalAsync(key);
            }
            finally
            {
                _lock.Release();
            }
        }

        private Task RemoveInternalAsync(string key)
        {
            try
            {
                var filePath = GetFilePath(key);
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SecureStorage.RemoveAsync error: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        public Task<bool> ContainsKeyAsync(string key)
        {
            ThrowIfDisposed();

            var filePath = GetFilePath(key);
            return Task.FromResult(File.Exists(filePath));
        }

        private string GetFilePath(string key)
        {
            var safeKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(key))
                .Replace('/', '_')
                .Replace('+', '-')
                .Replace('=', '.');
            return Path.Combine(_storagePath, $"{safeKey}.secure");
        }

        private byte[] Encrypt(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();

            ms.Write(aes.IV, 0, aes.IV.Length);

            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs, Encoding.UTF8))
            {
                sw.Write(plainText);
            }

            return ms.ToArray();
        }

        private string Decrypt(byte[] cipherText)
        {
            if (cipherText.Length < 16)
                throw new CryptographicException("Invalid cipher text length");

            using var aes = Aes.Create();

            var iv = new byte[16];
            Array.Copy(cipherText, 0, iv, 0, iv.Length);

            var actualCipherText = new byte[cipherText.Length - iv.Length];
            Array.Copy(cipherText, iv.Length, actualCipherText, 0, actualCipherText.Length);

            aes.Key = _key;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(actualCipherText);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);

            return sr.ReadToEnd();
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, nameof(SecureStorageService));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _lock.Dispose();

            if (_key != null)
            {
                Array.Clear(_key, 0, _key.Length);
            }

            GC.SuppressFinalize(this);
        }
    }
}