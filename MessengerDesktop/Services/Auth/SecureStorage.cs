using System;
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

    public class SecureStorageService : ISecureStorageService
    {
        private readonly byte[] _key;
        private readonly string _storagePath;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public SecureStorageService()
        {
            _key = GenerateMachineKey();
            _storagePath = GetStoragePath();
            Directory.CreateDirectory(_storagePath);
        }

        private static byte[] GenerateMachineKey()
        {
            var machineData = $"{Environment.MachineName}{Environment.UserName}";
            return SHA256.HashData(Encoding.UTF8.GetBytes(machineData));
        }

        private static string GetStoragePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "MessengerDesktop", "SecureStorage");
        }

        public async Task SaveAsync<T>(string key, T value)
        {
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
                System.Diagnostics.Debug.WriteLine($"SecureStorage.SaveAsync error: {ex.Message}");
                throw;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<T?> GetAsync<T>(string key)
        {
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SecureStorage.GetAsync error: {ex.Message}");
                return default;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task RemoveAsync(string key)
        {
            await _lock.WaitAsync();
            try
            {
                var filePath = GetFilePath(key);
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SecureStorage.RemoveAsync error: {ex.Message}");
            }
            finally
            {
                _lock.Release();
            }
        }

        public Task<bool> ContainsKeyAsync(string key)
        {
            var filePath = GetFilePath(key);
            return Task.FromResult(File.Exists(filePath));
        }

        private string GetFilePath(string key)
            => Path.Combine(_storagePath, $"{key}.secure");

        private byte[] Encrypt(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();

            ms.Write(aes.IV, 0, aes.IV.Length);

            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(plainText);
            }

            return ms.ToArray();
        }

        private string Decrypt(byte[] cipherText)
        {
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
            using var sr = new StreamReader(cs);

            return sr.ReadToEnd();
        }
    }
}