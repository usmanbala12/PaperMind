using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using PaperMind.Services.Abstractions;

namespace PaperMind.Services.Implementations
{
    /// <summary>
    /// Secure credential storage using Windows DPAPI or AES encryption.
    /// Credentials are encrypted and stored in the user's AppData folder.
    /// </summary>
    public sealed class CredentialService : ICredentialService
    {
        private readonly string _credentialFolder;
        private readonly ILoggingService _log;
        private readonly bool _isWindows;

        public CredentialService(ILoggingService log)
        {
            _log = log;
            _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            _credentialFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PaperMind",
                "credentials");

            Directory.CreateDirectory(_credentialFolder);
        }

        public string? GetCredential(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;

            try
            {
                var filePath = GetCredentialPath(key);
                if (!File.Exists(filePath)) return null;

                var encryptedData = File.ReadAllBytes(filePath);
                var decryptedData = _isWindows
                    ? DecryptWindows(encryptedData)
                    : DecryptCrossPlatform(encryptedData);

                return Encoding.UTF8.GetString(decryptedData);
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to retrieve credential '{key}'", ex);
                return null;
            }
        }

        public void SetCredential(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Credential key cannot be empty", nameof(key));

            if (value == null)
                throw new ArgumentNullException(nameof(value));

            try
            {
                var filePath = GetCredentialPath(key);
                var plainData = Encoding.UTF8.GetBytes(value);
                var encryptedData = _isWindows
                    ? EncryptWindows(plainData)
                    : EncryptCrossPlatform(plainData);

                File.WriteAllBytes(filePath, encryptedData);
                _log.Info($"Credential '{key}' stored securely");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to store credential '{key}'", ex);
                throw;
            }
        }

        public void DeleteCredential(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            try
            {
                var filePath = GetCredentialPath(key);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _log.Info($"Credential '{key}' deleted");
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to delete credential '{key}'", ex);
            }
        }

        private string GetCredentialPath(string key)
        {
            // Sanitize key to make it filename-safe
            var sanitized = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(_credentialFolder, $"{sanitized}.cred");
        }

        private byte[] EncryptWindows(byte[] data)
        {
            if (OperatingSystem.IsWindows())
            {
                // Use Windows DPAPI (Data Protection API)
                return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            }
            throw new PlatformNotSupportedException("Windows DPAPI is only supported on Windows.");
        }

        private byte[] DecryptWindows(byte[] encryptedData)
        {
            if (OperatingSystem.IsWindows())
            {
                // Use Windows DPAPI (Data Protection API)
                return ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
            }
            throw new PlatformNotSupportedException("Windows DPAPI is only supported on Windows.");
        }

        private byte[] EncryptCrossPlatform(byte[] data)
        {
            // Use AES-256 with a machine-derived key for cross-platform compatibility
            using var aes = Aes.Create();
            aes.Key = GetMachineKey();
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);

            // Prepend IV to encrypted data
            var result = new byte[aes.IV.Length + encrypted.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);

            return result;
        }

        private byte[] DecryptCrossPlatform(byte[] encryptedData)
        {
            // Extract IV and encrypted data
            using var aes = Aes.Create();
            aes.Key = GetMachineKey();

            var iv = new byte[16]; // AES IV is always 16 bytes
            var encrypted = new byte[encryptedData.Length - 16];
            Buffer.BlockCopy(encryptedData, 0, iv, 0, 16);
            Buffer.BlockCopy(encryptedData, 16, encrypted, 0, encrypted.Length);

            aes.IV = iv;
            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
        }

        private byte[] GetMachineKey()
        {
            // Derive a machine-specific key from machine name and user
            // This is not cryptographically perfect but provides reasonable protection
            var machineId = $"{Environment.MachineName}_{Environment.UserName}_PaperMind_v1";
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(machineId));
        }
    }
}
