# Fonts Directory

This directory contains TrueType fonts used by the OCR service for creating searchable PDF text layers with proper Unicode support (including diacritics).

## Required Fonts

The `GetEmbeddedTypeface()` method in `OcrService.cs` looks for fonts in this priority order:
1. `arial.ttf` or `Arial.ttf`
2. `liberation-sans.ttf`
3. Falls back to system fonts if none found

## Recommended: Liberation Sans

**Download Liberation Sans** (open-source, free to redistribute):

### Option 1: Direct Download
1. Visit: https://github.com/liberationfonts/liberation-fonts/releases
2. Download the latest `liberation-fonts-ttf-*.tar.gz`
3. Extract and copy `LiberationSans-Regular.ttf` to this directory
4. Rename it to `liberation-sans.ttf`

### Option 2: Quick Command (Windows)
```powershell
# Download using curl (if available)
curl -L -o liberation-sans.ttf "https://github.com/liberationfonts/liberation-sans/raw/main/LiberationSans-Regular.ttf"
```

### Option 3: Use System Arial (Windows only)
```powershell
# Copy Arial from Windows Fonts directory
Copy-Item "C:\Windows\Fonts\arial.ttf" -Destination ".\fonts\arial.ttf"
```

## Why This Matters

Without a proper Unicode font, the OCR searchable PDF text layer may not correctly render:
- Accented characters (é, ñ, ü, etc.)
- Non-Latin scripts
- Special symbols

The font file will be automatically bundled with your application during build.
