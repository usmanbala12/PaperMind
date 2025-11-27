using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Styling;
using PaperMind.Services.Abstractions;

namespace PaperMind.Services.Implementations
{
    /// <summary>
    /// Cross-platform theme service that detects system dark mode preferences.
    /// </summary>
    public sealed class ThemeService : IThemeService
    {
        private readonly IConfigurationService _config;
        private readonly ILoggingService _log;
        private ThemeVariant _currentTheme;

        public ThemeService(IConfigurationService config, ILoggingService log)
        {
            _config = config;
            _log = log;

            // Load saved theme preference or detect system theme
            var savedTheme = _config.Get("AppTheme");
            if (!string.IsNullOrEmpty(savedTheme))
            {
                _currentTheme = ParseThemeVariant(savedTheme);
                _log.Info($"Loaded saved theme preference: {savedTheme}");
            }
            else
            {
                _currentTheme = ThemeVariant.Default;
                _log.Info("No saved theme preference, using system default");
            }
        }

        public ThemeVariant CurrentTheme => _currentTheme;

        public event EventHandler<ThemeVariant>? ThemeChanged;

        public void SetTheme(ThemeVariant theme)
        {
            if (_currentTheme == theme) return;

            _currentTheme = theme;
            
            // Save preference
            var themeString = theme == ThemeVariant.Dark ? "Dark" 
                            : theme == ThemeVariant.Light ? "Light" 
                            : "Default";
            
            _ = _config.SetAsync("AppTheme", themeString);
            _log.Info($"Theme changed to: {themeString}");

            // Notify listeners
            ThemeChanged?.Invoke(this, theme);
        }

        /// <summary>
        /// Detects the system's dark mode preference (for reference/future use).
        /// </summary>
        public static bool IsSystemDarkMode()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return IsWindowsDarkMode();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return IsLinuxDarkMode();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return IsMacOSDarkMode();
                }
            }
            catch
            {
                // If detection fails, default to light mode
            }

            return false;
        }

        private static bool IsWindowsDarkMode()
        {
            try
            {
                // Check registry for Windows dark mode setting
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                
                if (key != null)
                {
                    var value = key.GetValue("AppsUseLightTheme");
                    if (value is int intValue)
                    {
                        return intValue == 0; // 0 = Dark mode, 1 = Light mode
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return false;
        }

        private static bool IsLinuxDarkMode()
        {
            try
            {
                // Try GTK theme detection first
                var gtkTheme = Environment.GetEnvironmentVariable("GTK_THEME");
                if (!string.IsNullOrEmpty(gtkTheme) && gtkTheme.Contains("dark", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Try gsettings for GNOME (Fedora default)
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "gsettings",
                        Arguments = "get org.gnome.desktop.interface gtk-theme",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd().Trim().ToLowerInvariant();
                        process.WaitForExit();
                        
                        // Check if theme name contains 'dark'
                        return output.Contains("dark");
                    }
                }
                catch
                {
                    // gsettings might not be available
                }

                // Try another GNOME setting for color scheme
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "gsettings",
                        Arguments = "get org.gnome.desktop.interface color-scheme",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd().Trim().ToLowerInvariant();
                        process.WaitForExit();
                        
                        return output.Contains("dark") || output.Contains("prefer-dark");
                    }
                }
                catch
                {
                    // Ignore
                }
            }
            catch
            {
                // Ignore errors
            }

            return false;
        }

        private static bool IsMacOSDarkMode()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "defaults",
                    Arguments = "read -g AppleInterfaceStyle",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    
                    // If the command succeeds and returns "Dark", dark mode is enabled
                    return output.Equals("Dark", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // If the setting doesn't exist or command fails, light mode is assumed
            }

            return false;
        }

        private static ThemeVariant ParseThemeVariant(string theme)
        {
            return theme.ToLowerInvariant() switch
            {
                "dark" => ThemeVariant.Dark,
                "light" => ThemeVariant.Light,
                _ => ThemeVariant.Default
            };
        }
    }
}
