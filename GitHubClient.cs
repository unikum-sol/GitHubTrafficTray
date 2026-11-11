using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GitHubTrafficTray;

public sealed class GitHubApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public GitHubApiException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

public sealed class GitHubClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;

    public RateLimitInfo? RateLimit { get; private set; }

    public GitHubClient(string token)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri("https://api.github.com/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GitHubTrafficTray/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public Task<GhUser> GetAuthenticatedUserAsync(CancellationToken ct) => GetAsync<GhUser>("user", ct);

    public async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        var (body, _) = await GetRawAsync(url, ct);
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
               ?? throw new InvalidOperationException($"Leere Antwort von {url}");
    }

    public async Task<List<T>> GetAllPagesAsync<T>(string url, CancellationToken ct)
    {
        var all = new List<T>();
        string? next = url;
        int guard = 0;
        while (next is not null && guard++ < 100)
        {
            var (body, nextLink) = await GetRawAsync(next, ct);
            var page = JsonSerializer.Deserialize<List<T>>(body, JsonOptions);
            if (page is not null) all.AddRange(page);
            next = nextLink;
        }
        return all;
    }

    private async Task<(string Body, string? Next)> GetRawAsync(string url, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, ct);
        UpdateRateLimit(resp);
        string body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new GitHubApiException(resp.StatusCode, ExtractMessage(body));
        return (body, GetNextLink(resp));
    }

    private void UpdateRateLimit(HttpResponseMessage resp)
    {
        if (TryGetInt(resp, "x-ratelimit-limit", out int limit) &&
            TryGetInt(resp, "x-ratelimit-remaining", out int remaining) &&
            resp.Headers.TryGetValues("x-ratelimit-reset", out var resetValues) &&
            long.TryParse(resetValues.FirstOrDefault(), out long resetEpoch))
        {
            RateLimit = new RateLimitInfo
            {
                Limit = limit,
                Remaining = remaining,
                ResetLocal = DateTimeOffset.FromUnixTimeSeconds(resetEpoch).LocalDateTime
            };
        }
    }

    private static bool TryGetInt(HttpResponseMessage resp, string header, out int value)
    {
        value = 0;
        return resp.Headers.TryGetValues(header, out var values) && int.TryParse(values.FirstOrDefault(), out value);
    }

    private static string? GetNextLink(HttpResponseMessage resp)
    {
        if (!resp.Headers.TryGetValues("Link", out var values)) return null;
        foreach (var header in values)
        {
            foreach (var part in header.Split(','))
            {
                var segments = part.Split(';');
                if (segments.Length < 2) continue;
                bool isNext = segments.Skip(1).Any(s => s.Trim().Equals("rel=\"next\"", StringComparison.OrdinalIgnoreCase));
                if (isNext) return segments[0].Trim().TrimStart('<').TrimEnd('>');
            }
        }
        return null;
    }

    private static string ExtractMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "";
        }
        catch
        {
            // kein JSON
        }
        return body.Length > 200 ? body[..200] : body;
    }

    public void Dispose() => _http.Dispose();
}
