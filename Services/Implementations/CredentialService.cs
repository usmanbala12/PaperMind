using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using PaperMind.Services.Abstractions;

namespace PaperMind.Services.Implementations
{
    /// <summary>
    /// Secure credential storage using platform-specific secure storage:
    /// - Windows: DPAPI (Data Protection API)
    /// - macOS: Keychain via Security.framework
    /// - Linux: libsecret (Secret Service API)
    /// - Fallback: AES encryption with machine-derived key
    /// </summary>
    public sealed class CredentialService : ICredentialService
    {
        private readonly string _credentialFolder;
        private readonly ILoggingService _log;
        private readonly OSPlatform _platform;
        private const string KEYCHAIN_SERVICE = "PaperMind";

        public CredentialService(ILoggingService log)
        {
            _log = log;

            // Detect platform
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                _platform = OSPlatform.Windows;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                _platform = OSPlatform.OSX;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                _platform = OSPlatform.Linux;
            else
                _platform = OSPlatform.Create("Unknown");

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
                // Try platform-specific storage first
                if (_platform == OSPlatform.OSX)
                {
                    return GetCredentialMacOS(key);
                }
                else if (_platform == OSPlatform.Linux)
                {
                    return GetCredentialLinux(key);
                }
                else if (_platform == OSPlatform.Windows)
                {
                    // Try file-based DPAPI storage
                    var filePath = GetCredentialPath(key);
                    if (!File.Exists(filePath)) return null;

                    var encryptedData = File.ReadAllBytes(filePath);
                    var decryptedData = DecryptWindows(encryptedData);
                    return Encoding.UTF8.GetString(decryptedData);
                }
                else
                {
                    // Unknown platform - use file-based AES
                    var filePath = GetCredentialPath(key);
                    if (!File.Exists(filePath)) return null;

                    var encryptedData = File.ReadAllBytes(filePath);
                    var decryptedData = DecryptCrossPlatform(encryptedData);
                    return Encoding.UTF8.GetString(decryptedData);
                }
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
                // Use platform-specific storage
                if (_platform == OSPlatform.OSX)
                {
                    SetCredentialMacOS(key, value);
                }
                else if (_platform == OSPlatform.Linux)
                {
                    SetCredentialLinux(key, value);
                }
                else if (_platform == OSPlatform.Windows)
                {
                    // Use file-based DPAPI storage
                    var filePath = GetCredentialPath(key);
                    var plainData = Encoding.UTF8.GetBytes(value);
                    var encryptedData = EncryptWindows(plainData);
                    File.WriteAllBytes(filePath, encryptedData);
                }
                else
                {
                    // Unknown platform - use file-based AES
                    var filePath = GetCredentialPath(key);
                    var plainData = Encoding.UTF8.GetBytes(value);
                    var encryptedData = EncryptCrossPlatform(plainData);
                    File.WriteAllBytes(filePath, encryptedData);
                }

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

        #region macOS Keychain P/Invoke

        // macOS Security.framework P/Invoke declarations
        [DllImport("Security.framework/Security")]
        private static extern int SecKeychainAddGenericPassword(
            IntPtr keychain,
            uint serviceNameLength,
            string serviceName,
            uint accountNameLength,
            string accountName,
            uint passwordLength,
            byte[] passwordData,
            IntPtr itemRef);

        [DllImport("Security.framework/Security")]
        private static extern int SecKeychainFindGenericPassword(
            IntPtr keychain,
            uint serviceNameLength,
            string serviceName,
            uint accountNameLength,
            string accountName,
            out uint passwordLength,
            out IntPtr passwordData,
            out IntPtr itemRef);

        [DllImport("Security.framework/Security")]
        private static extern int SecKeychainItemFreeContent(
            IntPtr attrList,
            IntPtr data);

        [DllImport("Security.framework/Security")]
        private static extern int SecKeychainItemDelete(IntPtr itemRef);

        private string? GetCredentialMacOS(string key)
        {
            if (!OperatingSystem.IsMacOS())
            {
                _log.Warn("macOS Keychain is only available on macOS, falling back to file storage");
                return GetCredentialFallback(key);
            }

            try
            {
                var status = SecKeychainFindGenericPassword(
                    IntPtr.Zero,
                    (uint)KEYCHAIN_SERVICE.Length,
                    KEYCHAIN_SERVICE,
                    (uint)key.Length,
                    key,
                    out uint passwordLength,
                    out IntPtr passwordData,
                    out IntPtr itemRef);

                if (status == 0 && passwordData != IntPtr.Zero)
                {
                    try
                    {
                        var passwordBytes = new byte[passwordLength];
                        Marshal.Copy(passwordData, passwordBytes, 0, (int)passwordLength);
                        return Encoding.UTF8.GetString(passwordBytes);
                    }
                    finally
                    {
                        SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
                    }
                }

                // Not found (-25300 is errSecItemNotFound)
                return null;
            }
            catch (Exception ex)
            {
                _log.Warn($"Failed to access macOS Keychain, falling back to file storage: {ex.Message}");
                return GetCredentialFallback(key);
            }
        }

        private void SetCredentialMacOS(string key, string value)
        {
            if (!OperatingSystem.IsMacOS())
            {
                _log.Warn("macOS Keychain is only available on macOS, falling back to file storage");
                SetCredentialFallback(key, value);
                return;
            }

            try
            {
                var passwordBytes = Encoding.UTF8.GetBytes(value);

                // Try to find and delete existing entry first
                var findStatus = SecKeychainFindGenericPassword(
                    IntPtr.Zero,
                    (uint)KEYCHAIN_SERVICE.Length,
                    KEYCHAIN_SERVICE,
                    (uint)key.Length,
                    key,
                    out _,
                    out IntPtr existingData,
                    out IntPtr itemRef);

                if (findStatus == 0)
                {
                    SecKeychainItemDelete(itemRef);
                    if (existingData != IntPtr.Zero)
                    {
                        SecKeychainItemFreeContent(IntPtr.Zero, existingData);
                    }
                }

                // Add new entry
                var status = SecKeychainAddGenericPassword(
                    IntPtr.Zero,
                    (uint)KEYCHAIN_SERVICE.Length,
                    KEYCHAIN_SERVICE,
                    (uint)key.Length,
                    key,
                    (uint)passwordBytes.Length,
                    passwordBytes,
                    IntPtr.Zero);

                if (status != 0)
                {
                    _log.Warn($"Failed to store credential in macOS Keychain (status: {status}), falling back to file storage");
                    SetCredentialFallback(key, value);
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"Failed to access macOS Keychain, falling back to file storage: {ex.Message}");
                SetCredentialFallback(key, value);
            }
        }

        #endregion

        #region Linux libsecret P/Invoke

        // Linux libsecret P/Invoke declarations
        [DllImport("libsecret-1.so.0", CharSet = CharSet.Ansi)]
        private static extern IntPtr secret_password_store_sync(
            IntPtr schema,
            string? collection,
            string label,
            string password,
            IntPtr cancellable,
            out IntPtr error,
            string attribute1_key,
            string attribute1_value,
            IntPtr end);

        [DllImport("libsecret-1.so.0", CharSet = CharSet.Ansi)]
        private static extern IntPtr secret_password_lookup_sync(
            IntPtr schema,
            IntPtr cancellable,
            out IntPtr error,
            string attribute1_key,
            string attribute1_value,
            IntPtr end);

        [DllImport("libsecret-1.so.0")]
        private static extern void secret_password_free(IntPtr password);

        [DllImport("libsecret-1.so.0")]
        private static extern IntPtr secret_schema_new(
            string name,
            int flags,
            string attribute1,
            int attribute1_type,
            IntPtr end);

        private static IntPtr? _secretSchema;

        private IntPtr GetSecretSchema()
        {
            if (_secretSchema == null)
            {
                // Create schema: SECRET_SCHEMA_NONE = 0, SECRET_SCHEMA_ATTRIBUTE_STRING = 0
                _secretSchema = secret_schema_new("com.papermind.credentials", 0, "key", 0, IntPtr.Zero);
            }
            return _secretSchema.Value;
        }

        private string? GetCredentialLinux(string key)
        {
            if (!OperatingSystem.IsLinux())
            {
                _log.Warn("libsecret is only available on Linux, falling back to file storage");
                return GetCredentialFallback(key);
            }

            try
            {
                var schema = GetSecretSchema();
                var password = secret_password_lookup_sync(
                    schema,
                    IntPtr.Zero,
                    out IntPtr error,
                    "key",
                    key,
                    IntPtr.Zero);

                if (error != IntPtr.Zero)
                {
                    _log.Warn("Failed to access libsecret, falling back to file storage");
                    return GetCredentialFallback(key);
                }

                if (password == IntPtr.Zero)
                {
                    return null; // Not found
                }

                try
                {
                    return Marshal.PtrToStringAnsi(password);
                }
                finally
                {
                    secret_password_free(password);
                }
            }
            catch (DllNotFoundException)
            {
                _log.Warn("libsecret not found, falling back to file storage");
                return GetCredentialFallback(key);
            }
            catch (Exception ex)
            {
                _log.Warn($"Failed to access libsecret, falling back to file storage: {ex.Message}");
                return GetCredentialFallback(key);
            }
        }

        private void SetCredentialLinux(string key, string value)
        {
            if (!OperatingSystem.IsLinux())
            {
                _log.Warn("libsecret is only available on Linux, falling back to file storage");
                SetCredentialFallback(key, value);
                return;
            }

            try
            {
                var schema = GetSecretSchema();
                secret_password_store_sync(
                    schema,
                    null, // Default collection
                    $"PaperMind: {key}",
                    value,
                    IntPtr.Zero,
                    out IntPtr error,
                    "key",
                    key,
                    IntPtr.Zero);

                if (error != IntPtr.Zero)
                {
                    _log.Warn("Failed to store credential in libsecret, falling back to file storage");
                    SetCredentialFallback(key, value);
                }
            }
            catch (DllNotFoundException)
            {
                _log.Warn("libsecret not found, falling back to file storage");
                SetCredentialFallback(key, value);
            }
            catch (Exception ex)
            {
                _log.Warn($"Failed to access libsecret, falling back to file storage: {ex.Message}");
                SetCredentialFallback(key, value);
            }
        }

        #endregion

        #region Fallback Methods

        private string? GetCredentialFallback(string key)
        {
            var filePath = GetCredentialPath(key);
            if (!File.Exists(filePath)) return null;

            var encryptedData = File.ReadAllBytes(filePath);
            var decryptedData = DecryptCrossPlatform(encryptedData);
            return Encoding.UTF8.GetString(decryptedData);
        }

        private void SetCredentialFallback(string key, string value)
        {
            var filePath = GetCredentialPath(key);
            var plainData = Encoding.UTF8.GetBytes(value);
            var encryptedData = EncryptCrossPlatform(plainData);
            File.WriteAllBytes(filePath, encryptedData);
        }

        #endregion
    }
}
