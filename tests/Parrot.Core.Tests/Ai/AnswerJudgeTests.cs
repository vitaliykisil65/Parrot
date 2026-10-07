using System.Net;
using System.Text;
using Parrot.Core.Ai;
using Parrot.Core.Models;
using Parrot.Core.Settings;

namespace Parrot.Core.Tests.Ai;

public class AnswerJudgeTests : IDisposable
{
    private readonly string _settingsFile = Path.Combine(Path.GetTempPath(), $"parrot-ai-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_settingsFile);

    private static Card Dishwasher() => new() { Id = 7, Front = "dishwasher", Back = "посудомийка" };

    private (AnswerJudge Judge, StubHandler Handler) Build(StubHandler handler, bool enabled = true)
    {
        var settings = new SettingsService(_settingsFile);
        settings.Save(new AppSettings { AiCheckEnabled = enabled, AiAddress = "192.168.0.146:11434", AiModel = "qwen3:4b" });

        return (new AnswerJudge(settings, new HttpClient(handler)), handler);
    }

    private static StubHandler Reply(string content) =>
        StubHandler.Json(HttpStatusCode.OK, $$"""{"message":{"role":"assistant","content":{{System.Text.Json.JsonSerializer.Serialize(content)}}},"done":true}""");

    [Fact]
    public async Task Accepts_what_the_model_accepts()
    {
        var (judge, _) = Build(Reply("""{"correct": true}"""));

        var result = await judge.JudgeAsync(Dishwasher(), TranslationDirection.FrontToBack, "машина для миття посуду");

        Assert.Equal(AiVerdict.Accepted, result.Verdict);
    }

    [Fact]
    public async Task Rejects_what_the_model_rejects()
    {
        var (judge, _) = Build(Reply("""{"correct": false}"""));

        var result = await judge.JudgeAsync(Dishwasher(), TranslationDirection.FrontToBack, "пральна машина");

        Assert.Equal(AiVerdict.Rejected, result.Verdict);
    }

    [Fact]
    public async Task Sends_the_card_and_the_answer_to_the_chat_endpoint()
    {
        var (judge, handler) = Build(Reply("""{"correct": true}"""));

        await judge.JudgeAsync(Dishwasher(), TranslationDirection.FrontToBack, "посудомийна машина");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://192.168.0.146:11434/api/chat", request.Uri);
        Assert.Contains("\"model\":\"qwen3:4b\"", request.Body);
        Assert.Contains("\"think\":false", request.Body);
        Assert.Contains("dishwasher", request.Body);
        Assert.Contains("посудомийна машина", request.Body);
    }

    [Fact]
    public async Task Asks_about_the_same_answer_only_once()
    {
        var (judge, handler) = Build(Reply("""{"correct": false}"""));

        await judge.JudgeAsync(Dishwasher(), TranslationDirection.FrontToBack, "пральна машина");
        var again = await judge.JudgeAsync(Dishwasher(), TranslationDirection.FrontToBack, "  Пральна машина ");

        Assert.Equal(AiVerdict.Rejected, again.Verdict);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Stays_silent_when_switched_off()
    {
        var (judge, handler) = Build(Reply("""{"correct": true}"""), enabled: false);

        var result = await judge.JudgeAsync(Dishwasher(), TranslationDirection.FrontToBack, "посудомийна машина");

        Assert.Equal(AiVerdict.Unavailable, result.Verdict);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_server_error_gives_no_verdict()
    {
        var (judge, _) = Build(StubHandler.Json(HttpStatusCode.NotFound, """{"error":"model 'qwen3:4b' not found"}"""));

        var result = await judge.JudgeAsync(Dishwasher(), TranslationDirection.FrontToBack, "посудомийна машина");

        Assert.Equal(AiVerdict.Unavailable, result.Verdict);
        Assert.Equal("model 'qwen3:4b' not found", result.Error);
    }

    [Fact]
    public async Task An_unreachable_server_gives_no_verdict()
    {
        var (judge, _) = Build(StubHandler.Throws(new HttpRequestException("No route to host")));

        var result = await judge.JudgeAsync(Dishwasher(), TranslationDirection.FrontToBack, "посудомийна машина");

        Assert.Equal(AiVerdict.Unavailable, result.Verdict);
    }

    [Fact]
    public async Task Test_reports_a_missing_model()
    {
        var (judge, _) = Build(StubHandler.Json(HttpStatusCode.OK, """{"models":[{"name":"gemma3:4b"}]}"""));

        var status = await judge.TestAsync();

        Assert.Equal(AiConnectionState.ModelMissing, status.State);
        Assert.Equal("gemma3:4b", status.Detail);
    }

    [Fact]
    public async Task Test_finds_the_model()
    {
        var (judge, handler) = Build(StubHandler.Json(HttpStatusCode.OK, """{"models":[{"name":"qwen3:4b"}]}"""));

        var status = await judge.TestAsync();

        Assert.Equal(AiConnectionState.Ok, status.State);
        Assert.Equal("http://192.168.0.146:11434/api/tags", handler.Requests[0].Uri);
    }

    [Fact]
    public void The_question_names_the_side_that_was_shown()
    {
        var card = new Card { Front = "dishwasher", Back = "посудомийка; посудомийна машина", Example = "Load the dishwasher." };

        var forward = AnswerJudge.BuildQuestion(card, TranslationDirection.FrontToBack, "машина для миття посуду");
        var backward = AnswerJudge.BuildQuestion(card, TranslationDirection.BackToFront, "dish washer");

        Assert.Contains("Shown word: dishwasher", forward);
        Assert.Contains("Translations on the card: посудомийка; посудомийна машина", forward);
        Assert.Contains("Example of use: Load the dishwasher.", forward);
        Assert.Contains("Shown word: посудомийка; посудомийна машина", backward);
        Assert.Contains("Translations on the card: dishwasher", backward);
    }

    [Theory]
    [InlineData("""{"correct": true}""", true)]
    [InlineData("""{"correct":false}""", false)]
    [InlineData("```json\n{\"correct\": true}\n```", true)]
    [InlineData("""{"correct": "true"}""", true)]
    [InlineData("true", true)]
    [InlineData("No", false)]
    [InlineData("I think so", null)]
    [InlineData("""{"answer": true}""", null)]
    public void Reads_the_verdict(string reply, bool? expected) =>
        Assert.Equal(expected, AnswerJudge.ParseVerdict(reply));

    [Theory]
    [InlineData(null, "http://127.0.0.1:11434/")]
    [InlineData("", "http://127.0.0.1:11434/")]
    [InlineData("192.168.0.146:11434", "http://192.168.0.146:11434/")]
    [InlineData("https://ai.example.com/ollama", "https://ai.example.com/ollama/")]
    public void Understands_the_address_a_person_types(string? typed, string expected) =>
        Assert.Equal(expected, OllamaAddress.Parse(typed)?.ToString());

    [Fact]
    public void Rejects_an_address_that_is_not_http() =>
        Assert.Null(OllamaAddress.Parse("ftp://server"));
}

public class AddAcceptedAnswerTests
{
    [Fact]
    public void Appends_a_new_variant()
    {
        var card = new Card { Back = "посудомийка" };

        Assert.True(card.AddAcceptedAnswer("  машина для миття посуду "));
        Assert.Equal("посудомийка; машина для миття посуду", card.Back);
    }

    [Theory]
    [InlineData("Посудомийка")]
    [InlineData("посудомийка.")]
    [InlineData("")]
    [InlineData("одне; друге")]
    public void Leaves_the_card_alone_for_known_or_unusable_answers(string answer)
    {
        var card = new Card { Back = "посудомийка" };

        Assert.False(card.AddAcceptedAnswer(answer));
        Assert.Equal("посудомийка", card.Back);
    }
}

/// <summary>Answers every request with one canned response and remembers what was asked.</summary>
internal sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(string Uri, string Body)> Requests { get; } = [];

    public static StubHandler Json(HttpStatusCode status, string body) =>
        new(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    public static StubHandler Throws(Exception exception) => new(() => throw exception);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.RequestUri!.ToString(), body));
        return respond();
    }
}
