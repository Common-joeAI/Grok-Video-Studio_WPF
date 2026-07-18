using GrokVideoStudio.Core.Models;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Factory that resolves the correct <see cref="IVideoGenerationService"/> based on <see cref="VideoProvider"/>.
/// Uses dependency injection to receive all registered implementations of the service.
/// </summary>
/// <param name="services">The collection of available video generation services.</param>
public sealed class VideoGenerationFactory(IEnumerable<IVideoGenerationService> services) : IVideoGenerationFactory
{
    private readonly IEnumerable<IVideoGenerationService> _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <inheritdoc />
    public IVideoGenerationService GetService(VideoProvider provider)
    {
        return _services.FirstOrDefault(s => s.Provider == provider)
            ?? throw new NotSupportedException($"No IVideoGenerationService registered for provider: {provider}");
    }
}
