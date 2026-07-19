using System.IO;
using System.Windows;
using GrokVideoStudio.App.Services;
using GrokVideoStudio.App.ViewModels;
using GrokVideoStudio.App.Views;
using GrokVideoStudio.App.Views.Pages;
using GrokVideoStudio.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WpfUiApp = System.Windows.Application;

namespace GrokVideoStudio.App;

/// <summary>
/// Application entry point and DI container setup.
///
/// FULL FEATURE PARITY WPF rebuild of the Python/PySide6 Grok-Video-Studio.
/// Registers all multi-provider video services, prompt services, social publishing
/// services, FFmpeg stitch service, activity log, usage stats, download, and
/// thumbnail services.
/// </summary>
public partial class App : WpfUiApp
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // ── Infrastructure ──
                services.AddSingleton<ISecureSettingsService, DpapiSettingsService>();
                services.AddSingleton<IVideoStorageService, VideoStorageService>();
                services.AddSingleton<IActivityLogService, ActivityLogService>();
                services.AddSingleton<IUsageStatsService, UsageStatsService>();
                services.AddSingleton<IVideoThumbnailService, VideoThumbnailService>();
                services.AddSingleton<IStitchService>(sp => new FfmpegStitchService(sp.GetRequiredService<ISecureSettingsService>()));

                // ── Video generation services (multi-provider) ──
                services.AddHttpClient<GrokVideoService>();
                services.AddHttpClient<SoraVideoService>();
                services.AddHttpClient<SeedanceVideoService>();
                services.AddSingleton<IVideoGenerationService>(sp => sp.GetRequiredService<GrokVideoService>());
                services.AddSingleton<IVideoGenerationService>(sp => sp.GetRequiredService<SoraVideoService>());
                services.AddSingleton<IVideoGenerationService>(sp => sp.GetRequiredService<SeedanceVideoService>());
                services.AddSingleton<IVideoGenerationFactory, VideoGenerationFactory>();

                // ── Prompt generation services ──
                services.AddHttpClient<GrokPromptService>();
                services.AddHttpClient<OpenAiPromptService>();
                services.AddHttpClient<OllamaPromptService>();
                services.AddSingleton<IPromptGenerationService>(sp => sp.GetRequiredService<GrokPromptService>());

                // ── Download service (needs HttpClient) ──
                services.AddHttpClient<VideoDownloadService>();
                services.AddSingleton<IVideoDownloadService>(sp => sp.GetRequiredService<VideoDownloadService>());

                // ── Social publishing services ──
                services.AddHttpClient<YouTubeUploadService>();
                services.AddHttpClient<FacebookUploadService>();
                services.AddHttpClient<InstagramUploadService>();
                services.AddHttpClient<TikTokUploadService>();
                services.AddSingleton<FolderPublishService>();
                services.AddHttpClient<SocialAuthService>();
                services.AddSingleton<ISocialAuthService>(sp => sp.GetRequiredService<SocialAuthService>());
                services.AddSingleton<ISocialPublishingFactory, SocialPublishingFactory>();

                // ── Audio analysis & chained generation ──
                services.AddHttpClient<AudioAnalysisService>();
                services.AddSingleton<IAudioAnalysisService>(sp => sp.GetRequiredService<AudioAnalysisService>());
                services.AddSingleton<IChainedGenerationService>(sp => new ChainedGenerationService(
                    sp.GetRequiredService<IVideoGenerationFactory>(),
                    sp.GetRequiredService<IVideoDownloadService>(),
                    sp.GetRequiredService<IVideoStorageService>(),
                    sp.GetRequiredService<IActivityLogService>(),
                    sp.GetRequiredService<IBrandingService>()));

                // ── Branding ──
                services.AddSingleton<IBrandingService, BrandingService>();

                // ── Navigation ──
                services.AddSingleton<INavigationService, NavigationService>();

                // ── ViewModels ──
                services.AddTransient<MainViewModel>();
                services.AddTransient<GenerateViewModel>();
                services.AddTransient<HistoryViewModel>();
                services.AddTransient<VideoPlayerViewModel>();
                services.AddTransient<StitchViewModel>();
                services.AddTransient<PublishViewModel>();
                services.AddTransient<ActivityLogViewModel>();
                services.AddSingleton<ActivityTrackerViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<ChainGenerationViewModel>();
                services.AddTransient<BrandingViewModel>();

                // ── Main window ──
                services.AddSingleton<MainWindow>();
            })
            .ConfigureLogging(logging =>
            {
                logging.AddDebug();
                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .Build();
    }

    /// <summary>
    /// Global DI service provider — used by pages that WPF UI NavigationView
    /// creates via parameterless constructors (which can't use constructor injection).
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();
        Services = _host.Services;

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
