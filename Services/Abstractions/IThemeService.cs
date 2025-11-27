using System;
using Avalonia.Styling;

namespace PaperMind.Services.Abstractions
{
    /// <summary>
    /// Service for managing application theme (Light/Dark mode).
    /// </summary>
    public interface IThemeService
    {
        /// <summary>
        /// Gets the current theme variant.
        /// </summary>
        ThemeVariant CurrentTheme { get; }

        /// <summary>
        /// Sets the application theme variant.
        /// </summary>
        /// <param name="theme">The theme to apply. Use ThemeVariant.Default for system theme.</param>
        void SetTheme(ThemeVariant theme);

        /// <summary>
        /// Event raised when the theme changes.
        /// </summary>
        event EventHandler<ThemeVariant>? ThemeChanged;
    }
}
