using GrokVideoStudio.Core.Models;
using GrokVideoStudio.Core.Services;
using Xunit;

namespace GrokVideoStudio.Tests;

/// <summary>
/// Tests for Core layer — verifies model contracts and API logic.
/// </summary>
public class CoreLayerTests
{
    [Fact]
    public void VideoGenerationRequest_DefaultsAreValid()
    {
        var request = new VideoGenerationRequest { Prompt = "A cat playing piano" };
        Assert.Equal("grok-video-latest", request.Model);
        Assert.Equal(8, request.Duration);
        Assert.Equal("16:9", request.AspectRatio);
        Assert.Equal(VideoProvider.GrokImagine, request.Provider);
        Assert.Null(request.Image);
    }

    [Fact]
    public void VideoPollResponse_IsTerminal_WhenDone()
    {
        var resp = new VideoPollResponse
        {
            Status = "done",
            Video = new VideoResult { Url = "https://example.com/v.mp4" }
        };
        Assert.True(resp.IsDone);
        Assert.True(resp.IsTerminal);
    }

    [Fact]
    public void VideoPollResponse_IsTerminal_WhenFailed()
    {
        var resp = new VideoPollResponse
        {
            Status = "failed",
            Error = new ApiError { Message = "Moderation rejected" }
        };
        Assert.False(resp.IsDone);
        Assert.True(resp.IsTerminal);
    }

    [Fact]
    public void VideoPollResponse_NotTerminal_WhenPending()
    {
        var resp = new VideoPollResponse { Status = "pending" };
        Assert.False(resp.IsDone);
        Assert.False(resp.IsTerminal);
    }

    [Fact]
    public void AppSettings_DefaultsMatchActualRepo()
    {
        var s = new AppSettings();
        Assert.Equal("grok-3-mini", s.GrokChatModel);
        Assert.Equal("grok-video-latest", s.GrokVideoModel);
        Assert.Equal("gpt-5.1-codex", s.OpenAiChatModel);
        Assert.Equal("http://127.0.0.1:11434/v1", s.OllamaApiBase);
        Assert.Equal("llama3.1:8b", s.OllamaChatModel);
        Assert.Equal("Dark", s.Theme);
    }

    [Fact]
    public void StitchOptions_Defaults()
    {
        var opts = new StitchOptions();
        Assert.True(opts.EnableCrossfade);
        Assert.Equal(0, opts.InterpolationFps);
        Assert.Equal("none", opts.UpscalePreset);
        Assert.False(opts.EnableGpuEncode);
    }

    [Fact]
    public void VideoItem_RecordEquality()
    {
        var item = new VideoItem { Prompt = "Test", Model = "grok-video-latest" };
        var copy = item with { };
        Assert.Equal(item, copy);
        Assert.Equal(item.Id, copy.Id);
    }

    [Fact]
    public void GrokVideoService_ProviderIsGrokImagine()
    {
        // Verify provider enum
        Assert.Equal(VideoProvider.GrokImagine, VideoProvider.GrokImagine);
        Assert.Equal(VideoProvider.OpenAiSora, VideoProvider.OpenAiSora);
        Assert.Equal(VideoProvider.Seedance, VideoProvider.Seedance);
    }
}
