using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MessengerDesktop.Services
{
    public interface ISecureStorageService
    {
        Task SaveAsync<T>(string key, T value);
        Task<T?> GetAsync<T>(string key);
        Task RemoveAsync(string key);
        bool ContainsKey(string key);
    }

    public class SecureStorageService : ISecureStorageService
    {
        private readonly byte[] _key;
        private readonly string _storagePath;

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
            try
            {
                var json = JsonSerializer.Serialize(value);
                var encrypted = Encrypt(json);
                var filePath = Path.Combine(_storagePath, $"{key}.secure");
                await File.WriteAllBytesAsync(filePath, encrypted);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SecureStorage Save error: {ex.Message}");
            }
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            try
            {
                var filePath = Path.Combine(_storagePath, $"{key}.secure");
                if (!File.Exists(filePath)) return default;

                var encrypted = await File.ReadAllBytesAsync(filePath);
                var decrypted = Decrypt(encrypted);
                return JsonSerializer.Deserialize<T>(decrypted);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SecureStorage Load error: {ex.Message}");
                return default;
            }
        }

        public async Task RemoveAsync(string key)
        {
            try
            {
                var filePath = Path.Combine(_storagePath, $"{key}.secure");
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SecureStorage Remove error: {ex.Message}");
            }
        }

        public bool ContainsKey(string key)
        {
            var filePath = Path.Combine(_storagePath, $"{key}.secure");
            return File.Exists(filePath);
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