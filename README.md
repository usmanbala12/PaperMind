# PaperMind

![PaperMind](Assets/logo.png)

A powerful, intelligent document processing application that transforms scanned PDFs into searchable, intelligently-named documents with seamless cloud integration. Built with modern .NET technologies and cross-platform support.

---

## 🎯 Overview

PaperMind is a desktop application designed to automate the tedious tasks of document processing. It uses OCR (Optical Character Recognition) to make scanned PDFs searchable, leverages advanced LLMs (Large Language Models) to generate meaningful filenames from document content, and provides automated cloud storage integration for seamless document management.

### Key Features

- **📄 OCR Processing**: Convert scanned PDFs into searchable documents with embedded text layers
- **🤖 AI-Powered Renaming**: Use LLMs (OpenAI, Anthropic) to generate intelligent, context-aware filenames
- **☁️ Cloud Integration**: Automatic upload to Dropbox, Google Drive, or OneDrive
- **⚙️ Flexible Pipelines**: Create custom processing workflows with combinable steps
- **📊 Progress Tracking**: Real-time progress monitoring with throughput metrics and time estimates
- **🔄 Job Management**: Background processing with pause/resume capabilities and error recovery
- **👁️ Folder Watching**: Automatically process new documents as they arrive
- **📝 Comprehensive Logging**: Detailed logs with filtering and search capabilities
- **🎨 Modern UI**: Built with Avalonia for a beautiful cross-platform experience

---

## 🏗️ Architecture & Solution Justification

### Design Philosophy

PaperMind is architected around several core principles:

#### 1. **Separation of Concerns**

The application follows a clean architecture pattern with distinct layers:

- **Models**: Pure data classes representing domain entities (`ProcessingJob`, `AppConfig`, `LogEntry`)
- **Services**: Business logic abstracted behind interfaces (`IOcrService`, `ILlmService`, `IStorageService`)
- **ViewModels**: MVVM pattern for UI state management using ReactiveUI
- **Views**: Declarative AXAML markup for cross-platform UI

This separation ensures testability, maintainability, and flexibility for future enhancements.

#### 2. **Dependency Injection**

All services are registered and resolved through Microsoft's DI container, enabling:
- Loose coupling between components
- Easy testing through interface mocking
- Centralized lifecycle management
- Configuration-driven behavior

#### 3. **Provider-Agnostic Design**

**LLM Service**: Supports multiple LLM providers (OpenAI, Anthropic) through a unified interface, allowing users to switch providers without code changes. The service handles provider-specific API differences internally.

**Storage Service**: Factory pattern for cloud storage services enables seamless switching between:
- Local file system
- Dropbox
- Google Drive
- OneDrive

Each provider implements `IStorageService`, ensuring consistent behavior regardless of the underlying platform.

#### 4. **Pipeline-Based Processing**

Jobs are composed of configurable processing steps:

1. **OCR Step** (`OcrStep`): Converts scanned PDFs to searchable documents
2. **LLM Rename Step** (`LlmRenameStep`): Generates intelligent filenames
3. **Upload Step** (`UploadStep`): Uploads to cloud storage

Each step is:
- **Independent**: Can be used standalone or combined
- **Configurable**: Per-step configuration for maximum flexibility
- **Extensible**: New steps can be added without modifying existing code

This approach follows the **Open/Closed Principle** - open for extension, closed for modification.

#### 5. **Robust Error Handling & Recovery**

- **Per-File Error Tracking**: Failed files don't stop the entire job
- **Resume Capability**: Jobs track processed files and can resume from failures
- **Retry Logic**: Configurable retry attempts for transient failures
- **Graceful Degradation**: Fallback naming strategies when LLM fails

#### 6. **Performance Optimization**

- **Parallel Processing**: Configurable concurrent OCR and LLM operations using `System.Threading.Tasks.Dataflow`
- **Resource Management**: Thread-safe counters and proper disposal patterns
- **Throttled Updates**: Database updates are batched to prevent I/O bottlenecks
- **Lazy Loading**: Tesseract engines are created per-thread to avoid contention

#### 7. **Cross-Platform Support**

Built with:
- **.NET 9**: Modern, high-performance runtime
- **Avalonia UI**: Native cross-platform UI (Windows, macOS, Linux)
- **SQLite**: Embedded database for job persistence
- **SkiaSharp**: Cross-platform 2D graphics for PDF rendering

### Technology Choices

| Technology | Justification |
|------------|---------------|
| **Tesseract 5.0** | Industry-standard open-source OCR engine with excellent accuracy and multi-language support |
| **PdfPig** | Robust PDF manipulation library with good Unicode support |
| **SkiaSharp** | Hardware-accelerated 2D graphics for efficient PDF rendering |
| **ReactiveUI** | Reactive MVVM framework for responsive, declarative UI logic |
| **SQLite** | Zero-configuration embedded database perfect for desktop applications |
| **System.Threading.Tasks.Dataflow** | High-performance pipeline processing with built-in parallelism |

### Security Considerations

- **Secure Credential Storage**: API keys stored using Windows `ProtectedData` encryption
- **No Hardcoded Secrets**: All sensitive data externalized to configuration
- **OAuth Flows**: Standard OAuth2 for cloud provider authentication
- **Principle of Least Privilege**: Services only access what they need

---

## 🚀 Getting Started

### Prerequisites

- **.NET 9 SDK** or later
- **Windows, macOS, or Linux**
- Internet connection for LLM and cloud storage features

### Installation

1. **Clone the repository**:
   ```bash
   git clone https://github.com/yourusername/PaperMind.git
   cd PaperMind
   ```

2. **Restore dependencies**:
   ```bash
   dotnet restore
   ```

3. **Build the application**:
   ```bash
   dotnet build
   ```

4. **Run the application**:
   ```bash
   dotnet run
   ```

### First-Time Setup

On first launch, PaperMind will automatically:
- Download required Tesseract language data files (`eng.traineddata`, `osd.traineddata`)
- Create the local database for job storage
- Initialize default configuration

---

## 📖 How to Use

### Basic Workflow

1. **Configure Settings**
   - Navigate to the **Settings** tab
   - Set your preferred OCR language (default: English)
   - Choose OCR quality (Fast, Balanced, High)
   - Configure LLM provider (OpenAI or Anthropic) and API key
   - Set up cloud storage provider and credentials (optional)

2. **Create a Job**
   - Click **"New Job"** in the Jobs tab
   - Select **Input Folder** containing PDFs to process
   - Select **Output Folder** for processed files
   - Choose processing steps:
     - ✅ OCR to Searchable PDF
     - ✅ LLM Rename
     - ✅ Upload to Cloud
   - Configure per-step settings (if needed)

3. **Run the Job**
   - Click **"Start Job"**
   - Monitor real-time progress:
     - Files processed / Total files
     - Current file being processed
     - Throughput (files/minute)
     - Estimated time remaining
   - View detailed logs in the **Logs** tab

4. **Review Results**
   - Processed files appear in the output folder
   - Searchable PDFs have embedded text layers
   - Files are renamed with meaningful, context-aware names
   - Cloud uploads are confirmed with links

### Advanced Features

#### Job Triggers

- **Manual**: Start jobs on-demand with a button click
- **Watch**: Automatically process new files added to the input folder

#### Pipeline Customization

Create custom workflows by combining steps:

**Example: Minimal OCR Pipeline**
```
1. OCR to Searchable PDF
   - Language: English
   - Quality: Balanced
```

**Example: Full Automation Pipeline**
```
1. OCR to Searchable PDF
   - Language: English
   - Quality: High
2. LLM Rename
   - Provider: OpenAI
   - Model: gpt-4o
   - API Key: [your-key]
3. Upload to Cloud
   - Provider: Google Drive
   - Folder ID: [target-folder-id]
```

#### Error Recovery

If a job fails:
1. Review errors in the **Job Details** panel
2. Fix problematic files or configuration
3. Click **"Resume Failed"** to reprocess only failed files
4. Successfully processed files are automatically skipped

#### Duplicate & Modify

- **Duplicate Job**: Clone an existing job with all settings
- **Edit Job**: Modify job configuration before running
- **Restart Job**: Re-run a completed job from scratch

### LLM Configuration

#### OpenAI Setup

1. Get API key from [OpenAI Platform](https://platform.openai.com/api-keys)
2. In Settings, select **LLM Provider: OpenAI**
3. Enter your API key
4. Select model (e.g., `gpt-4o-mini` for cost-effectiveness)

#### Anthropic Setup

1. Get API key from [Anthropic Console](https://console.anthropic.com/)
2. In Settings, select **LLM Provider: Anthropic**
3. Enter your API key
4. Select model (e.g., `claude-3-5-sonnet-20241022`)

### Cloud Storage Setup

#### Google Drive

1. Create OAuth credentials in [Google Cloud Console](https://console.cloud.google.com/)
2. Download `credentials.json`
3. In Settings, select **Storage Provider: Google Drive**
4. Click **"Authenticate"** and follow OAuth flow
5. Specify target **Folder ID** in job step configuration

#### Dropbox

1. Create app in [Dropbox App Console](https://www.dropbox.com/developers/apps)
2. Get **App Key** and **App Secret**
3. In Settings, select **Storage Provider: Dropbox**
4. Click **"Authenticate"** and authorize the app
5. Specify target **Folder Path** in job step configuration

#### OneDrive

1. Register app in [Azure Portal](https://portal.azure.com/)
2. Configure Microsoft Graph API permissions
3. In Settings, select **Storage Provider: OneDrive**
4. Click **"Authenticate"** and sign in
5. Specify target **Folder Path** in job step configuration

---

## 🔧 Configuration

### Application Settings

Settings are persisted in the SQLite database and include:

- **OCR Settings**:
  - `OcrLanguage`: Tesseract language code (default: `eng`)
  - `OcrQuality`: Fast, Balanced, or High
  - `MaxConcurrentOcr`: Parallel OCR threads (1-10)

- **LLM Settings**:
  - `LlmProvider`: OpenAI or Anthropic
  - `ApiKey`: Securely stored API key
  - `MaxConcurrentLlm`: Parallel LLM requests (1-20)
  - `FallbackNamingStrategy`: Behavior when LLM fails

- **Storage Settings**:
  - `StorageProvider`: Local, Dropbox, Google Drive, or OneDrive
  - `StorageConfig`: Provider-specific configuration

- **Logging**:
  - `LogLevel`: Trace, Debug, Information, Warning, Error, Critical

### Job Configuration

Each job stores:

- Input/output folder paths
- Selected processing steps
- Per-step configuration (JSON serialized)
- Trigger type (Manual or Watch)
- Error tracking and processed file history

---

## 📊 Performance Tuning

### OCR Performance

- **Fast**: Lower DPI (150), faster processing, reduced accuracy
- **Balanced**: Medium DPI (200), good balance
- **High**: High DPI (300), best accuracy, slower processing

Adjust `MaxConcurrentOcr` based on CPU cores (recommended: half of logical cores).

### LLM Performance

- **Batch Processing**: Multiple files sent to LLM in a single request
- **Model Selection**: Smaller models (e.g., `gpt-4o-mini`) are faster and cheaper
- **Concurrency**: Adjust `MaxConcurrentLlm` for API rate limits

### Database Performance

- Progress updates are throttled (max 1 per second) to prevent I/O bottlenecks
- Bulk operations use transactions for efficiency

---

## 🐛 Troubleshooting

### OCR Errors

**Problem**: "Tesseract data file not found"

**Solution**: Ensure `tessdata` folder contains language files. The application auto-downloads on first run, but you can manually download from [Tesseract Data](https://github.com/tesseract-ocr/tessdata_fast).

### LLM Errors

**Problem**: "API key invalid"

**Solution**: Verify API key in Settings, ensure credits/quota available.

**Problem**: "Rate limit exceeded"

**Solution**: Reduce `MaxConcurrentLlm` or implement exponential backoff.

### Cloud Upload Errors

**Problem**: "Authentication failed"

**Solution**: Re-authenticate via Settings → [Provider] → "Authenticate".

**Problem**: "Folder not found"

**Solution**: Verify folder ID/path in job step configuration.

---

## 🤝 Contributing

Contributions are welcome! Please follow these guidelines:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## 🙏 Acknowledgments

- **Tesseract OCR** - Google's powerful OCR engine
- **Avalonia UI** - Modern cross-platform UI framework
- **OpenAI & Anthropic** - Advanced language models
- **PdfPig** - Robust PDF processing library

---

## 📧 Support

For issues, questions, or feature requests, please [open an issue](https://github.com/yourusername/PaperMind/issues) on GitHub.

---

**Made with ❤️ for document organization enthusiasts**
