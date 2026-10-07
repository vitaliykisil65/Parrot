using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Parrot.Core.Ai;

/// <summary>
/// Talks to an Ollama server over its native API. Only the two calls Parrot needs: which models
/// are installed, and one non-streaming chat turn. Nothing here holds state, so a failed call can
/// simply be retried.
/// </summary>
public sealed class OllamaClient(HttpClient http, Uri baseUri)
{
    /// <summary>Cyrillic goes out as plain UTF-8 rather than escapes: half the bytes, and readable in a capture.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Names of the models the server has pulled, e.g. "qwen3:4b".</summary>
    public async Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync(new Uri(baseUri, "api/tags"), ct).ConfigureAwait(false);
        var tags = await ReadAsync<TagsResponse>(response, ct).ConfigureAwait(false);

        return tags.Models?.Select(m => m.Name).Where(n => !string.IsNullOrEmpty(n)).ToList() ?? [];
    }

    /// <summary>
    /// One chat turn. <paramref name="format"/> is a JSON schema the reply must follow; thinking
    /// is switched off because a grader that reasons out loud is many times slower on a CPU.
    /// </summary>
    public async Task<string> ChatAsync(
        string model,
        string system,
        string user,
        JsonNode? format = null,
        CancellationToken ct = default)
    {
        var request = new JsonObject
        {
            ["model"] = model,
            ["stream"] = false,
            ["think"] = false,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user },
            },
            ["options"] = new JsonObject { ["temperature"] = 0 },
        };

        if (format is not null)
            request["format"] = format.DeepClone();

        using var content = JsonContent.Create(request, options: JsonOptions);
        using var response = await http.PostAsync(new Uri(baseUri, "api/chat"), content, ct).ConfigureAwait(false);
        var chat = await ReadAsync<ChatResponse>(response, ct).ConfigureAwait(false);

        return chat.Message?.Content
            ?? throw new OllamaException(response.StatusCode, "The server returned no message.");
    }

    /// <summary>Loads a model into memory without generating anything: a chat with no messages.</summary>
    public async Task LoadAsync(string model, CancellationToken ct = default)
    {
        var request = new JsonObject { ["model"] = model, ["messages"] = new JsonArray(), ["stream"] = false };

        using var content = JsonContent.Create(request, options: JsonOptions);
        using var response = await http.PostAsync(new Uri(baseUri, "api/chat"), content, ct).ConfigureAwait(false);
        await ReadAsync<ChatResponse>(response, ct).ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            throw new OllamaException(response.StatusCode, await ReadErrorAsync(response, ct).ConfigureAwait(false));

        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
        return value ?? throw new OllamaException(response.StatusCode, "The server returned an empty body.");
    }

    /// <summary>
    /// Pulls the message out of Ollama's <c>{"error": "..."}</c> body. Falls back to the raw text,
    /// because a proxy in front of the server can answer with something that is not JSON at all.
    /// </summary>
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return response.ReasonPhrase ?? response.StatusCode.ToString();
        }

        if (string.IsNullOrWhiteSpace(body))
            return response.ReasonPhrase ?? response.StatusCode.ToString();

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String)
                return error.GetString() ?? body;
        }
        catch (JsonException)
        {
            // Not JSON — return the raw text.
        }

        return body.Length > 300 ? body[..300] : body;
    }

    private sealed record TagsResponse(List<TagModel>? Models);

    private sealed record TagModel(string Name);

    private sealed record ChatResponse(ChatMessage? Message);

    private sealed record ChatMessage(string? Role, string? Content);
}

/// <summary>An error reported by the Ollama server, with the HTTP status so callers can react.</summary>
public sealed class OllamaException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>The model named in the request is not pulled on the server.</summary>
    public bool IsModelMissing => StatusCode == HttpStatusCode.NotFound;
}

/// <summary>Where the Ollama server lives.</summary>
public static class OllamaAddress
{
    public static Uri Default { get; } = new("http://127.0.0.1:11434/");

    /// <summary>
    /// Turns what a person types into the server's root URL, always with a trailing slash:
    /// "192.168.0.146:11434" becomes "http://192.168.0.146:11434/". Empty means the default
    /// local server; anything unparseable gives null.
    /// </summary>
    public static Uri? Parse(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return Default;

        var text = address.Trim();

        if (!text.Contains("://", StringComparison.Ordinal))
            text = "http://" + text;

        if (!text.EndsWith('/'))
            text += "/";

        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri
            : null;
    }
}
