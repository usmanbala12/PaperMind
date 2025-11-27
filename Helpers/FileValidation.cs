using System;
using System.IO;

namespace PaperMind.Helpers
{
    /// <summary>
    /// Helper class for validating file formats.
    /// </summary>
    public static class FileValidation
    {
        /// <summary>
        /// Validates if a file is a valid PDF by checking magic bytes.
        /// </summary>
        /// <param name="filePath">Path to the file to validate</param>
        /// <returns>True if file appears to be a valid PDF</returns>
        public static bool IsValidPdf(string filePath)
        {
            if (!File.Exists(filePath))
                return false;

            try
            {
                // PDF files start with "%PDF-" (hex: 25 50 44 46 2D)
                using var stream = File.OpenRead(filePath);
                if (stream.Length < 5)
                    return false;

                var buffer = new byte[5];
                var bytesRead = stream.Read(buffer, 0, 5);

                if (bytesRead < 5)
                    return false;

                // Check for PDF magic number: %PDF-
                return buffer[0] == 0x25 &&  // %
                       buffer[1] == 0x50 &&  // P
                       buffer[2] == 0x44 &&  // D
                       buffer[3] == 0x46 &&  // F
                       buffer[4] == 0x2D;    // -
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a user-friendly error message for an invalid PDF.
        /// </summary>
        public static string GetInvalidPdfMessage(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            return $"File '{fileName}' does not appear to be a valid PDF. It may be corrupted or have the wrong extension.";
        }
    }
}
