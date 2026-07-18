using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrokVideoStudio.Core.Models;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Analyzes audio files to drive chained video generation.
/// Uses ffprobe for duration extraction and AI (Grok/OpenAI) for scene segmentation.
/// </summary>
public interface IAudioAnalysisService
{
    /// <summary>Get audio file duration in seconds using ffprobe.</summary>
    Task<double> GetDurationAsync(string audioPath, CancellationToken ct = default);

    /// <summary>Analyze the audio concept and generate a scene-by-scene plan.</summary>
    Task<AudioAnalysisResult> AnalyzeAsync(
        string audioPath,
        string concept,
        int clipDurationSeconds,
        string aiApiKey,
        string aiModel = "grok-3-mini",
        string aiProvider = "grok",
        CancellationToken ct = default);
}

/// <summary>
/// Result of audio analysis — contains the full scene plan for chained generation.
/// </summary>
public sealed record AudioAnalysisResult
{
    /// <summary>Total audio duration in seconds.</summary>
    public double TotalDuration { get; init; }

    /// <summary>Number of clips needed to cover the full audio.</summary>
    public int ClipCount { get; init; }

    /// <summary>Duration per clip in seconds.</summary>
    public int ClipDuration { get; init; }

    /// <summary>The scene-by-scene plan.</summary>
    public required IReadOnlyList<SceneSegment> Scenes { get; init; }
}

/// <summary>
/// A single scene segment in the chained generation plan.
/// </summary>
public sealed record SceneSegment
{
    /// <summary>1-based index.</summary>
    public int Index { get; init; }

    /// <summary>Start time in the final video (seconds).</summary>
    public double StartTime { get; init; }

    /// <summary>The video generation prompt for this scene.</summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>A brief mood/atmosphere description.</summary>
    public string Mood { get; init; } = string.Empty;
}

public sealed class AudioAnalysisService : IAudioAnalysisService
{
    private readonly HttpClient _httpClient;
    private readonly string _ffprobePath;

    public AudioAnalysisService(HttpClient httpClient, string ffprobePath = "ffprobe")
    {
        _httpClient = httpClient;
        _ffprobePath = ffprobePath;
    }

    public async Task<double> GetDurationAsync(string audioPath, CancellationToken ct = default)
    {
        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}");

        var psi = new ProcessStartInfo
        {
            FileName = _ffprobePath,
            Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{audioPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"ffprobe failed (exit {process.ExitCode}): {stderr}");
        }

        return double.TryParse(stdout.Trim(), out var duration) ? duration : 0;
    }

    public async Task<AudioAnalysisResult> AnalyzeAsync(
        string audioPath,
        string concept,
        int clipDurationSeconds,
        string aiApiKey,
        string aiModel = "grok-3-mini",
        string aiProvider = "grok",
        CancellationToken ct = default)
    {
        var totalDuration = await GetDurationAsync(audioPath, ct);
        if (totalDuration <= 0)
            throw new InvalidOperationException("Could not determine audio duration. Is ffprobe installed?");

        var clipCount = (int)Math.Ceiling(totalDuration / clipDurationSeconds);
        clipCount = Math.Max(1, clipCount);

        // Use AI to generate scene-by-scene prompts
        var scenes = await GenerateScenePlanAsync(
            concept, clipCount, clipDurationSeconds, totalDuration,
            aiApiKey, aiModel, aiProvider, ct);

        return new AudioAnalysisResult
        {
            TotalDuration = totalDuration,
            ClipCount = clipCount,
            ClipDuration = clipDurationSeconds,
            Scenes = scenes
        };
    }

    private async Task<IReadOnlyList<SceneSegment>> GenerateScenePlanAsync(
        string concept,
        int clipCount,
        int clipDuration,
        double totalDuration,
        string apiKey,
        string model,
        string provider,
        CancellationToken ct)
    {
        var systemPrompt =
            "You are a creative video director and AI video prompt engineer. " +
            $"The user has a {totalDuration:F1}-second audio track with the concept: \"{concept}\". " +
            $"You need to create a {clipCount}-scene video where each scene is {clipDuration} seconds long. " +
            "Each scene should flow naturally into the next as a continuous visual narrative. " +
            "For each scene, provide:\n" +
            "1. A detailed, cinematic video generation prompt (visual description only, no audio references)\n" +
            "2. A mood/atmosphere label (1-3 words)\n\n" +
            "Return ONLY a JSON array, no markdown, no explanation. " +
            "Each element: {\"prompt\": \"...\", \"mood\": \"...\"}";

        var url = provider == "openai"
            ? "https://api.openai.com/v1/chat/completions"
            : "https://api.x.ai/v1/chat/completions";

        var payload = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = $"Generate {clipCount} scenes for this video." }
            },
            max_tokens = 4096,
            temperature = 0.8
        };

        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var respJson = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(respJson);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "[]";

        // Parse the JSON array from the AI response
        // Strip markdown code fences if present
        content = content.Trim();
        if (content.StartsWith("```"))
        {
            var firstNewline = content.IndexOf('\n');
            if (firstNewline > 0) content = content[(firstNewline + 1)..];
            var lastFence = content.LastIndexOf("```");
            if (lastFence > 0) content = content[..lastFence];
            content = content.Trim();
        }

        var rawScenes = JsonSerializer.Deserialize<List<JsonElement>>(content) ?? [];
        var scenes = new List<SceneSegment>();

        for (int i = 0; i < rawScenes.Count; i++)
        {
            var s = rawScenes[i];
            scenes.Add(new SceneSegment
            {
                Index = i + 1,
                StartTime = i * clipDuration,
                Prompt = s.TryGetProperty("prompt", out var p) ? p.GetString() ?? "" : "",
                Mood = s.TryGetProperty("mood", out var m) ? m.GetString() ?? "" : ""
            });
        }

        // Fallback: if AI returned fewer scenes than needed, fill the rest with generic prompts
        while (scenes.Count < clipCount)
        {
            var idx = scenes.Count;
            scenes.Add(new SceneSegment
            {
                Index = idx + 1,
                StartTime = idx * clipDuration,
                Prompt = $"Cinematic continuation of: {concept} — scene {idx + 1}",
                Mood = "continuation"
            });
        }

        return scenes;
    }
}
