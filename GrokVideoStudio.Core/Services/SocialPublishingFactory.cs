using GrokVideoStudio.Core.Models;
using GrokVideoStudio.Core.Services;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Factory for resolving the correct social publishing service by platform.
/// Uses DI-registered services resolved on demand.
/// </summary>
public interface ISocialPublishingFactory
{
    YouTubeUploadService? GetYouTubeService();
    FacebookUploadService? GetFacebookService();
    InstagramUploadService? GetInstagramService();
    TikTokUploadService? GetTikTokService();
}

public sealed class SocialPublishingFactory : ISocialPublishingFactory
{
    private readonly IServiceProvider _services;

    public SocialPublishingFactory(IServiceProvider services)
    {
        _services = services;
    }

    public YouTubeUploadService? GetYouTubeService() =>
        _services.GetService(typeof(YouTubeUploadService)) as YouTubeUploadService;

    public FacebookUploadService? GetFacebookService() =>
        _services.GetService(typeof(FacebookUploadService)) as FacebookUploadService;

    public InstagramUploadService? GetInstagramService() =>
        _services.GetService(typeof(InstagramUploadService)) as InstagramUploadService;

    public TikTokUploadService? GetTikTokService() =>
        _services.GetService(typeof(TikTokUploadService)) as TikTokUploadService;
}
