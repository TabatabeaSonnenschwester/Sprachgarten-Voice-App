using System.Text.Json;
using System.Text.RegularExpressions;

namespace Klartext;

/// <summary>UI-thread lease shared by extension jobs. Main dictation is consulted before acquisition.</summary>
public sealed class ActivityGate(Func<bool> mainAvailable)
{
    private Lease? current;
    public bool Busy => current != null;
    public string Label => current?.Label ?? "";
    public event Action? Changed;
    public Lease? TryEnter(string label)
    {
        if (Busy || !mainAvailable()) return null;
        current = new Lease(this, label); Changed?.Invoke(); return current;
    }
    public void Cancel() => current?.Cancellation.Cancel();
    public sealed class Lease : IDisposable
    {
        private ActivityGate? owner;
        internal string Label { get; }
        internal CancellationTokenSource Cancellation { get; } = new();
        public CancellationToken Token => Cancellation.Token;
        internal Lease(ActivityGate owner, string label) { this.owner = owner; Label = label; }
        public void Dispose()
        {
            if (owner == null) return;
            var gate = owner; owner = null; gate.current = null; Cancellation.Dispose(); gate.Changed?.Invoke();
        }
    }
}

public static class ProtectedFile
{
    public static T? Read<T>(string path)
    {
        if (!File.Exists(path)) return default;
        if (new FileInfo(path).Length > 8_000_000) throw new InvalidDataException("Datei zu groß.");
        return JsonSerializer.Deserialize<T>(WindowsUserProtection.Transform(File.ReadAllBytes(path), false))
            ?? throw new InvalidDataException("Datei ist leer.");
    }
    public static void Write<T>(string path, T value)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(value);
        if (json.Length > 7_000_000) throw new InvalidDataException("Speichergrenze erreicht.");
        var encrypted = WindowsUserProtection.Transform(json, true);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        try { File.WriteAllBytes(temp, encrypted); MoveReplacing(temp, path); }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }
    /// <summary>Sprint F (F1): Ersetzt die Zieldatei durch die Temp-Datei; nur das Umbenennen wird wiederholt, das Schreiben der Temp-Datei nicht.</summary>
    internal static void MoveReplacing(string temp, string path)
    {
        // Ein fremdes Programm kann die Zieldatei kurz offen halten; wer genau, ist nicht belegt.
        // Höchstens 5 Versuche, Pausen 20, 40, 80 und 160 ms (zusammen höchstens 300 ms). Scheitert auch der letzte, fliegt die ursprüngliche Ausnahme weiter.
        for (int attempt = 1; ; attempt++)
        {
            try { File.Move(temp, path, true); break; }
            catch (Exception e) when (attempt < 5 && (e is UnauthorizedAccessException || (e is IOException && e is not FileNotFoundException && e is not DirectoryNotFoundException && e is not PathTooLongException)))
            { Thread.Sleep(20 << (attempt - 1)); }
        }
    }
}

public sealed record PromptDocument(string Original = "", string Goal = "", string Audience = "", string Format = "", string Constraints = "", string Draft = "", string Candidate = "", string Title = "", Guid? LibraryId = null,
    PromptReview? Review = null, string? ReviewSource = null, string ReviewAnswer = "", bool ReviewSuggestion = false)
{
    public string Compose()
    {
        var parts = new List<string> { Original.Trim() };
        foreach (var (label, value) in new[] { ("Aufgabe", Goal), ("Zielgruppe / Kontext", Audience), ("Ausgabeformat", Format), ("Verbindliche Vorgaben", Constraints) })
            if (!string.IsNullOrWhiteSpace(value)) parts.Add($"{label}: {value.Trim()}");
        return string.Join("\r\n\r\n", parts.Where(p => p.Length > 0));
    }
    public bool Valid => new[] { Original, Goal, Audience, Format, Constraints, Draft, Candidate, Title }.All(s => s != null && s.Length <= 24000) && Title.Length <= 120
        && (Review == null || Review.Valid) && (ReviewSource == null || ReviewSource.Length <= 24000) && ReviewAnswer != null && ReviewAnswer.Length <= 4000;
}

public sealed record PromptFinding(string Level, string Text);
public static class PromptRules
{
    public const string Version = "2026-09-20.1";
    public static IReadOnlyList<PromptFinding> Inspect(string text)
    {
        var findings = new List<PromptFinding>();
        if (string.IsNullOrWhiteSpace(text)) return [new("Rot", "Noch kein Prompt vorhanden. Beschreibe zuerst die gewünschte Aufgabe.")];
        if (text.Length > 3000) findings.Add(new("Gelb", "Mehr als 3.000 Zeichen: manuell nutzbar; für die kleine lokale KI bitte kürzen."));
        if (Regex.IsMatch(text, @"\[(?:TEXT|EINFÜGEN|PLATZHALTER|TODO)[^\]]*\]", RegexOptions.IgnoreCase))
            findings.Add(new("Gelb", "Ein markierter Platzhalter ist noch offen. Ergänzen oder bewusst als Vorlage behalten."));
        if (Regex.IsMatch(text, @"\b(folgenden|beigefügten|angehängten)\b", RegexOptions.IgnoreCase))
            findings.Add(new("Gelb", "Du verweist auf Material. Ist es beim späteren Verwenden wirklich dabei? Diese App kann das nicht beurteilen."));
        if (findings.Count == 0) findings.Add(new("Hinweis", "Keine der begrenzten formalen Warnregeln ausgelöst. Inhaltliche Eignung ist damit NICHT geprüft."));
        findings.Add(new("Hinweis", "Optional klären: Was soll entstehen? Für wen? In welchem Format? Welche Grenzen? Bereits klare oder einfache Prompts brauchen keine zusätzlichen Felder."));
        return findings;
    }
    public static string CandidateWarning(string source, string candidate)
    {
        var numbers = Regex.Matches(source, @"\d+(?:[.,]\d+)?").Select(m => m.Value).ToHashSet();
        var generated = Regex.Matches(candidate, @"\d+(?:[.,]\d+)?").Select(m => m.Value).ToHashSet();
        string warning = "KI-Vorschlag ungeprüft: Vergleiche Bedeutung, Verneinungen und Vorgaben mit deinem Ausgangstext. Die KI kann Fehler machen.";
        if (!numbers.SetEquals(generated)) warning = "Achtung: Zahlen weichen von deinen Angaben ab! " + warning;
        var missing = MissingWords(source, candidate);
        if (missing.Length > 0) warning = "Bitte vergleichen – diese Wörter wurden nicht wiedergefunden: " + string.Join(", ", missing) + ". (Wortvergleich, keine Bedeutungsprüfung.) " + warning;
        return warning;
    }
    public static string[] MissingWords(string source, string candidate)
    {
        var filler = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "möchte", "irgendwie", "eigentlich", "schreiben", "lassen", "bitte", "einfach", "brauche", "unsere", "unseren", "hätte", "gerne", "wünsche", "damit", "dafür", "sollte", "könnte", "würde" };
        return Regex.Matches(source, @"\p{L}{5,}|\b(?:ohne|kein|nie)\b", RegexOptions.IgnoreCase).Select(m => m.Value).Where(word => !filler.Contains(word) && !candidate.Contains(word[..Math.Min(5, word.Length)], StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
    }
    public static bool NumbersChanged(string source, string candidate) => !Regex.Matches(source, @"\d+(?:[.,]\d+)?").Select(m => m.Value).ToHashSet().SetEquals(Regex.Matches(candidate, @"\d+(?:[.,]\d+)?").Select(m => m.Value));
}

public sealed record PromptVersion(int Number, DateTimeOffset SavedAt, string Text, string Original);
public sealed record PromptEntry(Guid Id, string Title, bool Favorite, PromptVersion[] Versions)
{
    public override string ToString() => $"{(Favorite ? "★ " : "")}{Title} · v{Versions[^1].Number}";
}
public sealed class PromptLibrary
{
    private readonly string path;
    private List<PromptEntry> entries;
    public IReadOnlyList<PromptEntry> Entries => entries.AsReadOnly();
    public PromptLibrary(string path)
    {
        this.path = path; entries = ProtectedFile.Read<List<PromptEntry>>(path) ?? [];
        if (entries.Count > 200 || entries.Any(e => e == null || string.IsNullOrWhiteSpace(e.Title) || e.Title.Length > 120 || e.Versions == null || e.Versions.Length is < 1 or > 20 || e.Versions.Any(v => v == null || v.Text == null || v.Original == null || v.Text.Length > 24000 || v.Original.Length > 24000)))
            throw new InvalidDataException("Ungültige Bibliothek. Bestehende Datei bleibt erhalten.");
    }
    public Guid Save(Guid? id, string title, string text, string original)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 120 || string.IsNullOrWhiteSpace(text) || text.Length > 24000 || original.Length > 24000) throw new InvalidDataException("Titel (bis 120 Zeichen) und Prompt (bis 24.000 Zeichen) erforderlich.");
        var existing = entries.SingleOrDefault(e => e.Id == id);
        if (existing == null && entries.Count >= 200) throw new InvalidOperationException("Maximal 200 Bibliothekseinträge. Zuerst einen nicht mehr benötigten Eintrag löschen.");
        if (existing?.Versions.Length >= 20) throw new InvalidOperationException("20 Versionen erreicht. Bitte als neuen Eintrag speichern.");
        var key = existing?.Id ?? Guid.NewGuid();
        var version = new PromptVersion((existing?.Versions.Last().Number ?? 0) + 1, DateTimeOffset.Now, text, original);
        var entry = new PromptEntry(key, title.Trim(), existing?.Favorite ?? false, [..existing?.Versions ?? [], version]);
        Persist(entries.Where(e => e.Id != key).Append(entry).ToList()); return key;
    }
    public void Favorite(Guid id) => Persist(entries.Select(e => e.Id == id ? e with { Favorite = !e.Favorite } : e).ToList());
    public void Delete(Guid id) => Persist(entries.Where(e => e.Id != id).ToList());
    private void Persist(List<PromptEntry> next) { ProtectedFile.Write(path, next); entries = next; }
    public IEnumerable<PromptEntry> Search(string query) => entries.Where(e => e.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Versions[^1].Text.Contains(query, StringComparison.OrdinalIgnoreCase)).OrderByDescending(e => e.Favorite).ThenBy(e => e.Title);
}
