# Grok Video Studio — WPF Edition

A complete .NET 10 WPF rebuild of the [original Python/PySide6 Grok-Video-Studio](https://github.com/mysticalg/Grok-Video-Studio) — a desktop app for generating AI videos, stitching clips, and publishing to social platforms.

## What This Is

This is a from-scratch C# reimplementation with full feature parity to the original Python app. It uses modern .NET 10, WPF with Fluent Design, and clean architecture patterns.

| Original (Python) | This Rebuild (C# / WPF) |
|---|---|
| PySide6 UI | WPF + WPF UI (lepoco) 4.3.0 |
| Python 3.11+ | .NET 10 / C# 14 |
| requests / httpx | HttpClient via DI |
| Manual JSON settings | DPAPI-encrypted settings |
| Playwright (Python) | Playwright.NET (scaffolded) |
| SQLite (manual) | Microsoft.Data.Sqlite |

## Architecture

```
GrokVideoStudio.sln
├── GrokVideoStudio.Core/          # Domain logic, models, services (no UI deps)
│   ├── Models/                    # AppSettings, VideoItem, enums
│   └── Services/                  # API services, storage, DPAPI, FFmpeg, uploads
├── GrokVideoStudio.App/           # WPF application layer
│   ├── ViewModels/                # CommunityToolkit.Mvvm source generators
│   ├── Views/Pages/               # 7 pages: Generate, History, Player, Stitch, Publish, ActivityLog, Settings
│   ├── Helpers/                   # Value converters
│   └── Services/                  # Navigation, WPF thumbnail service
└── GrokVideoStudio.Tests/         # Unit tests for Core layer
```

## Features

- **Multi-provider video generation**: Grok Imagine, OpenAI Sora 2, Seedance 2.0
- **Multi-source prompt generation**: xAI Grok API, OpenAI API, local Ollama
- **Batch / variant generation** with queue execution
- **Continue-from-last-frame** and **image-to-video** support
- **FFmpeg stitch pipeline**: crossfade, interpolation (48/60fps), upscale (2x/1080p/1440p/4K), GPU encode, music mix
- **In-app video player** with seek, volume, mute
- **Thumbnail previews** in history gallery
- **Social publishing**: YouTube, Facebook, Instagram, TikTok (API-based upload)
- **Activity log** with real-time entries and export
- **Usage statistics** persisted to SQLite
- **DPAPI-encrypted settings** — all API keys stored securely
- **Dark Fluent Design** with Mica backdrop

## Prerequisites

- **.NET 10 SDK** — [download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Visual Studio 2022 17.14+** with ".NET desktop development" workload
- **FFmpeg** in your system PATH (for stitch, thumbnails, and continue-from-frame)

## Getting Started

```bash
# Clone
git clone https://github.com/Common-joeAI/Grok-Video-Studio_WPF.git
cd Grok-Video-Studio_WPF
git checkout wpf-rebuild

# Restore and build
dotnet restore GrokVideoStudio.sln
dotnet build GrokVideoStudio.sln

# Run
dotnet run --project GrokVideoStudio/GrokVideoStudio.App/GrokVideoStudio.App.csproj
```

Or open `GrokVideoStudio/GrokVideoStudio.sln` in Visual Studio and press F5.

## Configuration

1. Launch the app → navigate to **Settings**
2. Enter your **xAI API Key** (required for Grok Imagine Video)
3. Optionally add OpenAI, Seedance, and Ollama credentials
4. Set the FFmpeg path if it's not in your PATH
5. Click **Save** — settings are encrypted with DPAPI

## Key NuGet Packages

| Package | Version | Purpose |
|---|---|---|
| WPF-UI | 4.3.0 | Fluent Design controls, Mica, dark theme |
| CommunityToolkit.Mvvm | 8.4.2 | ObservableProperty, RelayCommand source generators |
| Microsoft.Extensions.Hosting | 10.0.0 | DI container, logging, configuration |
| Microsoft.Data.Sqlite | 10.0.0 | Usage statistics storage |

## Branch Structure

- `main` — original Python/PySide6 source (preserved from upstream fork)
- `wpf-rebuild` — the .NET 10 WPF rebuild (this branch)

## Roadmap

- [ ] Playwright.NET browser automation (CDP relay)
- [ ] AI Flow Trainer (record/replay browser workflows)
- [ ] MSI/EXE installer packaging
- [ ] Auto-update via GitHub Releases

## Credits

Based on the original [Grok-Video-Studio](https://github.com/mysticalg/Grok-Video-Studio) by [mysticalg](https://github.com/mysticalg).
