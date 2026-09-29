using System.Text.Json;
using System.Text.Json.Serialization;

namespace Klartext;

public sealed record PromptQuestion(string Question, string Reason, string[] Options);
public sealed record PromptSuggestion(string Text, string Reason);
public sealed record PromptReview(string Summary, PromptQuestion[] Questions, PromptSuggestion[] Suggestions)
{
    private static bool TextValid(string? text, int limit) => !string.IsNullOrWhiteSpace(text) && text.Length <= limit && !text.Contains("<think>", StringComparison.OrdinalIgnoreCase);
    public bool Valid => TextValid(Summary, 1200)
        && Questions is { Length: <= 1 } && Suggestions is { Length: <= 1 }
        && !(Questions.Length > 0 && Suggestions.Length > 0)
        && Questions.All(q => q != null && TextValid(q.Question, 1500) && TextValid(q.Reason, 1200) && q.Options is { Length: <= 3 } && q.Options.All(o => TextValid(o, 1000)))
        && Suggestions.All(s => s != null && TextValid(s.Text, 2000) && TextValid(s.Reason, 1200));
    public static PromptReview Parse(string json)
    {
        var review = JsonSerializer.Deserialize<PromptReview>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        });
        if (review is not { Valid: true }) throw new InvalidDataException("Die KI hat keine verwendbare Prüfung geliefert. Dein Prompt bleibt erhalten; erneut versuchen oder selbst bearbeiten.");
        return review;
    }
    // This is explicit user-confirmed composition, never a model rewrite or semantic guarantee.
    public string Apply(string source, string answer, bool includeSuggestion)
    {
        if (!Valid) throw new InvalidDataException("Ungültige KI-Prüfung.");
        if (source.Length > 24000 || answer.Length > 4000) throw new InvalidDataException("Text ist zu lang.");
        // Sprint E (K2): Überschrift je Herkunft; Frage und Vorschlag treten nie zusammen auf (Valid). Einem Vorschlag wird kein Vorrang zugeschrieben.
        string? addition = null;
        if (Questions.Length == 1 && !string.IsNullOrWhiteSpace(answer))
            addition = $"Von mir bestätigte Klärung (bei Widersprüchen gilt diese aktuelle Entscheidung):\r\nKlärung zu: {Questions[0].Question}\r\nMeine Antwort: {answer.Trim()}";
        else if (includeSuggestion && Suggestions.Length == 1)
            addition = "Von mir ausgewählte KI-Ergänzung:\r\nKI-Vorschlag: " + Suggestions[0].Text;
        if (addition == null) return source;
        string result = source + "\r\n\r\n" + addition;
        if (result.Length > 24000) throw new InvalidDataException("Zusammen mehr als 24.000 Zeichen. Bisheriger Prompt bleibt erhalten.");
        return result;
    }
}

public sealed record PromptReviewResult(PromptReview Review, double Seconds);

public static class PromptReviewProtocol
{
    private static readonly JsonSerializerOptions Unicode = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static string ReviewData(string text) => JsonSerializer.Serialize(new { draft = text }, Unicode);
    public static PromptReview ParseFeedback(string json, string input)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        string[] keys = ["befund", "rueckfrage", "ergaenzung"];
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 3 || root.EnumerateObject().Any(p => !keys.Contains(p.Name)))
            throw new InvalidDataException("Die KI-Prüfung hat ein unerwartetes Format geliefert. Dein Prompt bleibt erhalten.");
        string Read(string key, int max)
        {
            if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Unvollständige KI-Prüfung.");
            string text = value.GetString()!.Trim();
            if (text.Length > max) throw new InvalidDataException("Die KI-Antwort ist zu lang. Dein Prompt bleibt erhalten.");
            return text;
        }
        string finding = Read("befund", 1200), question = Read("rueckfrage", 1500), addition = Read("ergaenzung", 2000);
        if (question.Length > 0 && (!question.Contains('?') || input.Contains(question, StringComparison.Ordinal)))
            throw new InvalidDataException("Die KI hat keine brauchbare Rückfrage formuliert. Dein Prompt bleibt erhalten; erneut prüfen oder selbst bearbeiten.");
        // The model gives advice, not a replacement prompt. Only explicit user confirmation composes an addition.
        var review = question.Length > 0 ? new PromptReview(finding, [new(question, "Beantworte die Frage nur, wenn sie zu deinem Entwurf passt. Bereits geklärte Punkte musst du nicht erneut beantworten.", [])], []) :
            addition.Length > 0 ? new PromptReview(finding, [], [new(addition, "Freiwillige KI-Idee. Prüfe vor der Übernahme, ob sie deine Absicht trifft.")]) : new PromptReview(finding, [], []);
        if (!review.Valid) throw new InvalidDataException("Die KI hat keine verwendbare Prüfung geliefert. Dein Prompt bleibt erhalten.");
        return review;
    }
}