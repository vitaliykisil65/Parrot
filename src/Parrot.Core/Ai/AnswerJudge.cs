using System.Text.Json;
using System.Text.Json.Nodes;
using Parrot.Core.Answers;
using Parrot.Core.Models;
using Parrot.Core.Settings;

namespace Parrot.Core.Ai;

public enum AiVerdict
{
    /// <summary>The model says the answer means the same as the card's.</summary>
    Accepted,

    /// <summary>The model says the answer is wrong.</summary>
    Rejected,

    /// <summary>No verdict: switched off, server unreachable, too slow or a reply that made no sense.</summary>
    Unavailable,
}

public readonly record struct AiJudgement(AiVerdict Verdict, string? Error = null)
{
    public static AiJudgement Unavailable(string error) => new(AiVerdict.Unavailable, error);
}

public enum AiConnectionState { Ok, Disabled, BadAddress, Unreachable, ModelMissing }

public readonly record struct AiConnectionStatus(AiConnectionState State, string? Detail = null);

/// <summary>
/// A second opinion from a language model, asked only after <see cref="AnswerChecker"/> has
/// rejected an answer. The checker can only compare against what is written on the card; the
/// model knows that "машина для миття посуду" is a dishwasher too. It never overrules a match —
/// it can only turn a miss into a hit — and any failure leaves the miss as it was.
/// </summary>
public sealed class AnswerJudge(SettingsService settings, HttpClient http)
{
    /// <summary>
    /// How long the user may be kept waiting. A cold model on a small CPU takes a while to load,
    /// which <see cref="WarmUpAsync"/> is there to hide.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    internal const string SystemPrompt =
        """
        You grade answers in a vocabulary flashcard app. The learner was shown a word or phrase
        and typed its translation. The card lists the translations its author expected, but the
        list is not complete. Decide whether the learner's answer is also a correct translation
        of the shown word in the same sense.

        Accept: synonyms, other common ways to say the same thing, a different word order, and
        grammatical forms that keep the meaning.
        Reject: a different or clearly broader or narrower meaning, another sense of the word than
        the one the card means, a wrong word, and misspellings that turn it into another word.

        Reply with JSON only.
        """;

    private static readonly JsonNode Schema = JsonNode.Parse(
        """
        {"type":"object","properties":{"correct":{"type":"boolean"}},"required":["correct"]}
        """)!;

    /// <summary>
    /// Verdicts already given this session. A card comes back with the same wrong answer often
    /// enough, and a remembered "no" costs nothing.
    /// </summary>
    private readonly Dictionary<(long CardId, TranslationDirection Direction, string Answer), AiVerdict> _verdicts = [];

    public bool IsEnabled =>
        settings.Current.AiCheckEnabled
        && !string.IsNullOrWhiteSpace(settings.Current.AiModel)
        && OllamaAddress.Parse(settings.Current.AiAddress) is not null;

    public async Task<AiJudgement> JudgeAsync(
        Card card,
        TranslationDirection direction,
        string? answer,
        CancellationToken ct = default)
    {
        if (!IsEnabled || OllamaAddress.Parse(settings.Current.AiAddress) is not { } address)
            return AiJudgement.Unavailable("AI check is off.");

        var typed = answer?.Trim() ?? "";
        if (AnswerNormalizer.Normalize(typed).Length == 0)
            return new AiJudgement(AiVerdict.Rejected);

        var key = (card.Id, direction, AnswerNormalizer.Normalize(typed));
        if (_verdicts.TryGetValue(key, out var known))
            return new AiJudgement(known);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        try
        {
            var client = new OllamaClient(http, address);
            var reply = await client.ChatAsync(
                settings.Current.AiModel.Trim(),
                SystemPrompt,
                BuildQuestion(card, direction, typed),
                Schema,
                timeout.Token).ConfigureAwait(false);

            if (ParseVerdict(reply) is not { } correct)
                return AiJudgement.Unavailable($"Unexpected reply: {Shorten(reply)}");

            var verdict = correct ? AiVerdict.Accepted : AiVerdict.Rejected;
            _verdicts[key] = verdict;
            return new AiJudgement(verdict);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return AiJudgement.Unavailable($"No reply within {Timeout.TotalSeconds:0} s.");
        }
        catch (Exception ex) when (ex is HttpRequestException or OllamaException or JsonException or NotSupportedException)
        {
            return AiJudgement.Unavailable(ex.Message);
        }
    }

    /// <summary>
    /// Loads the model into memory ahead of time, so the answer can be graded quickly once typed.
    /// Called when a card appears; failures are ignored — the real check will report them.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken ct = default)
    {
        if (!IsEnabled || OllamaAddress.Parse(settings.Current.AiAddress) is not { } address)
            return;

        try
        {
            await new OllamaClient(http, address)
                .LoadAsync(settings.Current.AiModel.Trim(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OllamaException or JsonException or OperationCanceledException)
        {
        }
    }

    /// <summary>Checks the server answers and has the configured model, for the settings page.</summary>
    public async Task<AiConnectionStatus> TestAsync(CancellationToken ct = default)
    {
        var current = settings.Current;

        if (OllamaAddress.Parse(current.AiAddress) is not { } address)
            return new AiConnectionStatus(AiConnectionState.BadAddress);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var models = await new OllamaClient(http, address).GetModelsAsync(timeout.Token).ConfigureAwait(false);
            var model = current.AiModel.Trim();

            return HasModel(models, model)
                ? new AiConnectionStatus(AiConnectionState.Ok, model)
                : new AiConnectionStatus(AiConnectionState.ModelMissing, string.Join(", ", models));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new AiConnectionStatus(AiConnectionState.Unreachable, "timeout");
        }
        catch (Exception ex) when (ex is HttpRequestException or OllamaException or JsonException)
        {
            return new AiConnectionStatus(AiConnectionState.Unreachable, ex.Message);
        }
    }

    /// <summary>
    /// Ollama lists "qwen3:4b" but also accepts "qwen3" for "qwen3:latest"; both spellings of a
    /// pulled model should count as present.
    /// </summary>
    public static bool HasModel(IEnumerable<string> models, string model) =>
        models.Any(m => string.Equals(m, model, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(m, model + ":latest", StringComparison.OrdinalIgnoreCase));

    public static string BuildQuestion(Card card, TranslationDirection direction, string answer)
    {
        var (shown, expected) = direction == TranslationDirection.BackToFront
            ? (card.Back, SplitVariants(card.Front))
            : (card.Front, card.AcceptedAnswers.ToList());

        var lines = new List<string>
        {
            $"Shown word: {shown}",
            $"Translations on the card: {string.Join("; ", expected)}",
        };

        // The example pins down which sense of a word with several meanings the card is about.
        if (!string.IsNullOrWhiteSpace(card.Example))
            lines.Add($"Example of use: {card.Example.Trim()}");

        lines.Add($"Learner's answer: {answer}");
        lines.Add("""Is the learner's answer correct? Reply {"correct": true} or {"correct": false}.""");

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Reads {"correct": true}. Small models sometimes wrap it in a code fence or just say
    /// "true" despite the schema, so those count too; anything else is no verdict at all.
    /// </summary>
    public static bool? ParseVerdict(string reply)
    {
        var text = reply.Trim().Trim('`').Trim();
        if (text.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            text = text[4..].Trim();

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("correct", out var correct))
                root = correct;

            return root.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(root.GetString(), out var value) => value,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return text.ToLowerInvariant() switch
            {
                "true" or "yes" => true,
                "false" or "no" => false,
                _ => null,
            };
        }
    }

    private static List<string> SplitVariants(string value) =>
        [.. value.Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static string Shorten(string text) => text.Length > 80 ? text[..80] + "…" : text;
}
