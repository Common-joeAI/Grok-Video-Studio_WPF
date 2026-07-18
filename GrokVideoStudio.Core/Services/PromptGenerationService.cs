using GrokVideoStudio.Core.Models;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Prompt generation service — generates video prompts from a concept using
/// various AI providers (Grok, OpenAI, Ollama).
/// Matches the actual repo's multi-source prompt generation.
/// </summary>
public interface IPromptGenerationService
{
    PromptSource Source { get; }

    /// <summary>Generate a video prompt from a concept/seed text.</summary>
    Task<string> GeneratePromptAsync(string concept, string apiKey, CancellationToken ct = default);
}

/// <summary>
/// Grok API prompt generation (grok-3-mini chat completions).
/// </summary>
public sealed class GrokPromptService : IPromptGenerationService
{
    private const string BaseUrl = "https://api.x.ai/v1/chat/completions";
    private readonly HttpClient _httpClient;

    public PromptSource Source => PromptSource.GrokApi;

    public GrokPromptService(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<string> GeneratePromptAsync(string concept, string apiKey, CancellationToken ct = default)
    {
        var payload = new
        {
            model = "grok-3-mini",
            messages = new[]
            {
                new { role = "system", content = "You are a video prompt engineer. Generate a detailed, cinematic video generation prompt based on the user's concept. Return only the prompt text, no explanations." },
                new { role = "user", content = concept }
            },
            max_tokens = 1024,
            temperature = 0.7
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var resp = await _httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var respJson = await resp.Content.ReadAsStringAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(respJson);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? concept;
    }
}

/// <summary>
/// OpenAI API prompt generation.
/// </summary>
public sealed class OpenAiPromptService : IPromptGenerationService
{
    private const string BaseUrl = "https://api.openai.com/v1/chat/completions";
    private readonly HttpClient _httpClient;

    public PromptSource Source => PromptSource.OpenAiApi;

    public OpenAiPromptService(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<string> GeneratePromptAsync(string concept, string apiKey, CancellationToken ct = default)
    {
        var payload = new
        {
            model = "gpt-5.1-codex",
            messages = new[]
            {
                new { role = "system", content = "You are a video prompt engineer. Generate a detailed, cinematic video generation prompt. Return only the prompt text." },
                new { role = "user", content = concept }
            },
            max_tokens = 1024,
            temperature = 0.7
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var resp = await _httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var respJson = await resp.Content.ReadAsStringAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(respJson);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? concept;
    }
}

/// <summary>
/// Local Ollama prompt generation (no API key needed).
/// </summary>
public sealed class OllamaPromptService : IPromptGenerationService
{
    public PromptSource Source => PromptSource.Ollama;
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public OllamaPromptService(HttpClient httpClient, string baseUrl = "http://127.0.0.1:11434/v1")
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl;
    }

    public async Task<string> GeneratePromptAsync(string concept, string apiKey, CancellationToken ct = default)
    {
        var payload = new
        {
            model = "llama3.1:8b",
            messages = new[]
            {
                new { role = "system", content = "You are a video prompt engineer. Generate a detailed, cinematic video generation prompt. Return only the prompt text." },
                new { role = "user", content = concept }
            },
            max_tokens = 768,
            temperature = 0.7
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions");
        req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var resp = await _httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var respJson = await resp.Content.ReadAsStringAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(respJson);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? concept;
    }
}
