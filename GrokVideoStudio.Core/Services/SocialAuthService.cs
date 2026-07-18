using System.Collections.Concurrent;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;
using System.Net;
using System.Text.Json;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Disposable local HTTP listener that captures the OAuth2 redirect callback.
/// Opens a temporary port, waits for the browser to hit it with ?code=...,
/// returns a simple HTML page to the browser, and exposes the captured code.
/// </summary>
internal sealed class LocalOAuthServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;

    public int Port { get; }
    public string RedirectUri => $"http://localhost:{Port}/callback/";

    public LocalOAuthServer()
    {
        // Find a free port between 8400-8500
        for (int port = 8400; port <= 8500; port++)
        {
            try
            {
                _listener.Prefixes.Add($"http://localhost:{port}/callback/");
                _listener.Start();
                Port = port;
                return;
            }
            catch
            {
                _listener.Prefixes.Clear();
            }
        }
        throw new InvalidOperationException("Could not find a free port for OAuth callback listener.");
    }

    /// <summary>
    /// Waits for the OAuth redirect. Returns the authorization code, or null on timeout.
    /// </summary>
    public async Task<string?> WaitForCodeAsync(TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<string?>();

        _listenTask = Task.Run(async () =>
        {
            try
            {
                using var reg = _cts.Token.Register(() => tcs.TrySetResult(null));
                var context = await _listener.GetContextAsync().WaitAsync(timeout, _cts.Token);
                var query = context.Request.QueryString;

                var code = query["code"];
                var error = query["error"];

                // Respond to the browser with a nice page
                var html = error is not null
                    ? $"<html><body><h2>Authorization failed: {WebUtility.HtmlEncode(error)}</h2><p>You can close this window.</p></body></html>"
                    : "<html><body><h2>✅ Authorization successful!</h2><p>You can close this window and return to Grok Video Studio.</p></body></html>";

                context.Response.ContentType = "text/html";
                context.Response.ContentEncoding = System.Text.Encoding.UTF8;
                var buffer = System.Text.Encoding.UTF8.GetBytes(html);
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, _cts.Token);
                context.Response.Close();

                tcs.TrySetResult(code);
            }
            catch
            {
                tcs.TrySetResult(null);
            }
        }, _cts.Token);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        timeoutCts.CancelAfter(timeout);

        try
        {
            return await tcs.Task.WaitAsync(timeout);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        _cts.Dispose();
    }
}

/// <summary>
/// Result of an OAuth authentication attempt.
/// </summary>
public sealed record SocialAuthResult
{
    public bool Success { get; init; }
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Service for handling OAuth2 browser-based authentication for social platforms.
/// Each platform has an Authenticate method that opens the browser, listens for
/// the callback, and exchanges the code for an access token.
/// </summary>
public interface ISocialAuthService
{
    /// <summary>YouTube/Google OAuth using the client secrets JSON file.</summary>
    Task<SocialAuthResult> AuthenticateYouTubeAsync(string clientSecretsPath, CancellationToken ct = default);

    /// <summary>Facebook OAuth — opens browser, exchanges code for user + page tokens.</summary>
    Task<SocialAuthResult> AuthenticateFacebookAsync(string clientId, string clientSecret, CancellationToken ct = default);

    /// <summary>Instagram OAuth — opens browser, exchanges code for access token + user ID.</summary>
    Task<SocialAuthResult> AuthenticateInstagramAsync(string clientId, string clientSecret, CancellationToken ct = default);

    /// <summary>TikTok OAuth — opens browser, exchanges code for access token + open ID.</summary>
    Task<SocialAuthResult> AuthenticateTikTokAsync(string clientKey, string clientSecret, CancellationToken ct = default);
}

/// <summary>
/// OAuth2 authentication service for social platforms.
/// Uses a local HttpListener for the redirect callback and HttpClient for token exchange.
/// </summary>
public sealed class SocialAuthService : ISocialAuthService
{
    private readonly HttpClient _httpClient;

    public SocialAuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    private static void OpenBrowser(string url)
    {
        // Use Process.Start with UseShellExecute to open the default browser
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private const string GraphVersion = "v23.0";

    // ── YouTube/Google ──

    public async Task<SocialAuthResult> AuthenticateYouTubeAsync(string clientSecretsPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientSecretsPath) || !File.Exists(clientSecretsPath))
            return new SocialAuthResult
            {
                Success = false,
                ErrorMessage = "YouTube requires a client_secrets.json file. Download it from Google Cloud Console and set the path in Settings."
            };

        try
        {
            UserCredential credential;
            await using (var stream = File.OpenRead(clientSecretsPath))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    (await GoogleClientSecrets.FromStreamAsync(stream, ct)).Secrets,
                    [Google.Apis.YouTube.v3.YouTubeService.Scope.YoutubeUpload],
                    "user",
                    ct,
                    new FileDataStore(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "GrokVideoStudio", "youtube-tokens"), true)
                );
            }

            return new SocialAuthResult
            {
                Success = true,
                AccessToken = credential.Token.AccessToken,
                UserId = "youtube-user"
            };
        }
        catch (Exception ex)
        {
            return new SocialAuthResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    // ── Facebook ──

    public async Task<SocialAuthResult> AuthenticateFacebookAsync(
        string clientId, string clientSecret, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return new SocialAuthResult
            {
                Success = false,
                ErrorMessage = "Facebook Client ID and Client Secret are required. Register an app at developers.facebook.com."
            };

        using var oauthServer = new LocalOAuthServer();
        var redirectUri = oauthServer.RedirectUri;

        // Build auth URL with the scopes needed for video publishing
        var scopes = "pages_manage_videos,pages_read_engagement,pages_show_list";
        var authUrl = $"https://www.facebook.com/{GraphVersion}/dialog/oauth" +
                      $"?client_id={Uri.EscapeDataString(clientId)}" +
                      $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                      $"&scope={scopes}" +
                      $"&response_type=code";

        OpenBrowser(authUrl);

        var code = await oauthServer.WaitForCodeAsync(TimeSpan.FromMinutes(5));
        if (string.IsNullOrEmpty(code))
            return new SocialAuthResult { Success = false, ErrorMessage = "Authorization timed out or was cancelled." };

        // Exchange code for user access token
        var tokenUrl = $"https://graph.facebook.com/{GraphVersion}/oauth/access_token" +
                       $"?client_id={Uri.EscapeDataString(clientId)}" +
                       $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                       $"&client_secret={Uri.EscapeDataString(clientSecret)}" +
                       $"&code={Uri.EscapeDataString(code)}";

        var tokenResp = await _httpClient.GetStringAsync(tokenUrl, ct);
        var tokenData = JsonDocument.Parse(tokenResp).RootElement;

        if (tokenData.TryGetProperty("error", out _))
            return new SocialAuthResult { Success = false, ErrorMessage = "Token exchange failed." };

        var userToken = tokenData.GetProperty("access_token").GetString()!;

        // Exchange user token for a long-lived page access token
        var pagesUrl = $"https://graph.facebook.com/{GraphVersion}/me/accounts?access_token={Uri.EscapeDataString(userToken)}";
        var pagesResp = await _httpClient.GetStringAsync(pagesUrl, ct);
        var pagesData = JsonDocument.Parse(pagesResp).RootElement;

        if (!pagesData.TryGetProperty("data", out var dataArr) || dataArr.GetArrayLength() == 0)
            return new SocialAuthResult
            {
                Success = true,
                AccessToken = userToken,
                UserId = string.Empty,
                ErrorMessage = "Connected, but no Facebook Pages found. Token stored as user token."
            };

        // Use the first page's access token (long-lived)
        var firstPage = dataArr[0];
        var pageToken = firstPage.GetProperty("access_token").GetString()!;
        var pageId = firstPage.GetProperty("id").GetString()!;

        return new SocialAuthResult
        {
            Success = true,
            AccessToken = pageToken,
            UserId = pageId
        };
    }

    // ── Instagram ──

    public async Task<SocialAuthResult> AuthenticateInstagramAsync(
        string clientId, string clientSecret, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return new SocialAuthResult
            {
                Success = false,
                ErrorMessage = "Instagram Client ID and Client Secret are required. Register an app at developers.facebook.com."
            };

        using var oauthServer = new LocalOAuthServer();
        var redirectUri = oauthServer.RedirectUri;

        var scopes = "instagram_basic,instagram_content_publish";
        var authUrl = $"https://api.instagram.com/oauth/authorize" +
                      $"?client_id={Uri.EscapeDataString(clientId)}" +
                      $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                      $"&scope={scopes}" +
                      $"&response_type=code";

        OpenBrowser(authUrl);

        var code = await oauthServer.WaitForCodeAsync(TimeSpan.FromMinutes(5));
        if (string.IsNullOrEmpty(code))
            return new SocialAuthResult { Success = false, ErrorMessage = "Authorization timed out or was cancelled." };

        // Exchange code for short-lived access token
        var tokenReq = new
        {
            client_id = clientId,
            client_secret = clientSecret,
            grant_type = "authorization_code",
            redirect_uri = redirectUri,
            code = code
        };

        var content = new StringContent(JsonSerializer.Serialize(tokenReq), System.Text.Encoding.UTF8, "application/json");
        var tokenResp = await _httpClient.PostAsync("https://api.instagram.com/oauth/access_token", content, ct);
        var tokenRespStr = await tokenResp.Content.ReadAsStringAsync(ct);
        var tokenData = JsonDocument.Parse(tokenRespStr).RootElement;

        if (tokenData.TryGetProperty("error", out var err) || !tokenData.TryGetProperty("access_token", out _))
            return new SocialAuthResult
            {
                Success = false,
                ErrorMessage = err.TryGetProperty("message", out var msg) ? msg.GetString()! : "Token exchange failed."
            };

        var shortToken = tokenData.GetProperty("access_token").GetString()!;
        var igUserId = tokenData.TryGetProperty("user_id", out var uid) ? uid.GetInt64().ToString() : string.Empty;

        // Exchange for long-lived token
        var longLivedUrl = $"https://graph.instagram.com/access_token" +
                           $"?grant_type=ig_exchange_token" +
                           $"&client_secret={Uri.EscapeDataString(clientSecret)}" +
                           $"&access_token={Uri.EscapeDataString(shortToken)}";

        var longResp = await _httpClient.GetStringAsync(longLivedUrl, ct);
        var longData = JsonDocument.Parse(longResp).RootElement;

        var longToken = longData.TryGetProperty("access_token", out var lt) ? lt.GetString()! : shortToken;

        return new SocialAuthResult
        {
            Success = true,
            AccessToken = longToken,
            UserId = igUserId
        };
    }

    // ── TikTok ──

    public async Task<SocialAuthResult> AuthenticateTikTokAsync(
        string clientKey, string clientSecret, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientKey) || string.IsNullOrWhiteSpace(clientSecret))
            return new SocialAuthResult
            {
                Success = false,
                ErrorMessage = "TikTok Client Key and Client Secret are required. Register an app at developers.tiktok.com."
            };

        using var oauthServer = new LocalOAuthServer();
        var redirectUri = oauthServer.RedirectUri;

        var scopes = "video.publish,video.upload";
        var authUrl = $"https://www.tiktok.com/auth/v2/authorize/" +
                      $"?client_key={Uri.EscapeDataString(clientKey)}" +
                      $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                      $"&response_type=code" +
                      $"&scope={scopes}";

        OpenBrowser(authUrl);

        var code = await oauthServer.WaitForCodeAsync(TimeSpan.FromMinutes(5));
        if (string.IsNullOrEmpty(code))
            return new SocialAuthResult { Success = false, ErrorMessage = "Authorization timed out or was cancelled." };

        // Exchange code for access token
        var tokenReq = new
        {
            client_key = clientKey,
            client_secret = clientSecret,
            grant_type = "authorization_code",
            redirect_uri = redirectUri,
            code = code
        };

        var content = new StringContent(JsonSerializer.Serialize(tokenReq), System.Text.Encoding.UTF8, "application/json");
        var tokenResp = await _httpClient.PostAsync("https://open.tiktokapis.com/v2/oauth/token/", content, ct);
        var tokenRespStr = await tokenResp.Content.ReadAsStringAsync(ct);
        var tokenData = JsonDocument.Parse(tokenRespStr).RootElement;

        if (tokenData.TryGetProperty("error", out _))
            return new SocialAuthResult { Success = false, ErrorMessage = $"TikTok token exchange failed: {tokenRespStr}" };

        var accessToken = tokenData.GetProperty("access_token").GetString()!;
        var openId = tokenData.TryGetProperty("open_id", out var oid) ? oid.GetString()! : string.Empty;

        return new SocialAuthResult
        {
            Success = true,
            AccessToken = accessToken,
            UserId = openId
        };
    }
}
