using System;

namespace PaperMind.Services.Abstractions
{
    /// <summary>
    /// Service for securely storing and retrieving sensitive credentials like API keys.
    /// Uses Windows DPAPI on Windows, or AES encryption on other platforms.
    /// </summary>
    public interface ICredentialService
    {
        /// <summary>
        /// Retrieves a stored credential.
        /// </summary>
        /// <param name="key">The credential key/identifier</param>
        /// <returns>The decrypted credential value, or null if not found</returns>
        string? GetCredential(string key);

        /// <summary>
        /// Stores a credential securely.
        /// </summary>
        /// <param name="key">The credential key/identifier</param>
        /// <param name="value">The credential value to encrypt and store</param>
        void SetCredential(string key, string value);

        /// <summary>
        /// Deletes a stored credential.
        /// </summary>
        /// <param name="key">The credential key/identifier</param>
        void DeleteCredential(string key);
    }
}
