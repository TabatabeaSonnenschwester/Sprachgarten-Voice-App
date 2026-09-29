using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Klartext;

public sealed record ReplacementRule(string From, string To)
{
    public const int MaxLength = 160;
    public bool IsValid => !string.IsNullOrWhiteSpace(From) && !string.IsNullOrWhiteSpace(To) &&
        From != To && From.Length <= MaxLength && To.Length <= MaxLength &&
        !From.Any(char.IsControl) && !To.Any(char.IsControl);
}

public static class CorrectionEngine
{
    private sealed record AppliedSpan(int Start, int Length, string Source);
    private const string WordChars = @"\p{L}\p{M}\p{N}\p{Pc}";
    private static bool Word(char c) => char.IsLetterOrDigit(c) || char.IsSurrogate(c) ||
        char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.ConnectorPunctuation;

    // One pass over the original recognition: replacements never feed another rule.
    public static string Apply(string text, IReadOnlyList<ReplacementRule> rules)
        => ApplyTracked(text, rules).Text;

    private static (string Text, List<AppliedSpan> Spans) ApplyTracked(string text, IReadOnlyList<ReplacementRule> rules)
    {
        if (rules.Count == 0 || text.Length == 0) return (text, []);
        var ordered = rules.OrderByDescending(r => r.From.Length).ToArray();
        string pattern = string.Join("|", ordered.Select(r =>
            (Word(r.From[0]) ? $"(?<![{WordChars}])" : "") + Regex.Escape(r.From) +
            (Word(r.From[^1]) ? $"(?![{WordChars}])" : "")));
        var lookup = ordered.ToDictionary(r => r.From, r => r.To, StringComparer.Ordinal);
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        var output = new StringBuilder(); var spans = new List<AppliedSpan>(); int cursor = 0;
        foreach (Match match in regex.Matches(text))
        {
            output.Append(text, cursor, match.Index - cursor);
            string replacement = lookup[match.Value];
            spans.Add(new AppliedSpan(output.Length, replacement.Length, match.Value));
            output.Append(replacement); cursor = match.Index + match.Length;
        }
        output.Append(text, cursor, text.Length - cursor);
        return (output.ToString(), spans);
    }

    // Expand a single local edit to complete words. A disjoint edit is not a safe learning proposal.
    public static ReplacementRule? Suggest(string before, string after)
        => SuggestEdit(before, after)?.Rule;

    public static ReplacementRule? SuggestWithOrigin(string before, string after, string? recognition, IReadOnlyList<ReplacementRule> rules)
    {
        var edit = SuggestEdit(before, after);
        if (edit == null) return null;
        if (recognition != null)
        {
            var tracked = ApplyTracked(recognition, rules);
            if (tracked.Text == before)
            {
                var overlapping = tracked.Spans.Where(s => edit.Value.Start < s.Start + s.Length && edit.Value.Start + edit.Value.Rule.From.Length > s.Start).ToArray();
                if (overlapping.Length == 1 && edit.Value.Start >= overlapping[0].Start && edit.Value.Start + edit.Value.Rule.From.Length <= overlapping[0].Start + overlapping[0].Length)
                {
                    var span = overlapping[0];
                    var refined = new ReplacementRule(span.Source, after.Substring(span.Start, span.Length + after.Length - before.Length));
                    return refined.IsValid ? refined : null;
                }
                if (overlapping.Length > 0) return null;
                return edit.Value.Rule;
            }
        }
        // Without reliable provenance, do not offer a new rule that silently creates a non-working chain.
        if (rules.Any(r => Apply(r.To, [edit.Value.Rule]) != r.To)) return null;
        return edit.Value.Rule;
    }

    private static (ReplacementRule Rule, int Start)? SuggestEdit(string before, string after)
    {
        if (before == after) return null;
        int start = 0;
        while (start < before.Length && start < after.Length && before[start] == after[start]) start++;
        int oldEnd = before.Length, newEnd = after.Length;
        while (oldEnd > start && newEnd > start && before[oldEnd - 1] == after[newEnd - 1]) { oldEnd--; newEnd--; }
        // Pure insertions/deletions do not imply an always-replace rule.
        if (oldEnd == start && (start == 0 || !Word(before[start - 1])) && (start == before.Length || !Word(before[start]))) return null;
        if (newEnd == start && (start == 0 || !Word(after[start - 1])) && (start == after.Length || !Word(after[start]))) return null;
        while (start > 0 && Word(before[start - 1])) start--;
        while (oldEnd < before.Length && Word(before[oldEnd])) oldEnd++;
        while (newEnd < after.Length && Word(after[newEnd])) newEnd++;
        var rule = new ReplacementRule(before[start..oldEnd], after[start..newEnd]);
        if (!rule.IsValid) return null;
        // Unchanged complete words between changed words signal more than one edit.
        var a = Regex.Matches(rule.From, @"[\p{L}\p{M}\p{N}\p{Pc}]+").Select(m => m.Value).ToArray();
        var b = Regex.Matches(rule.To, @"[\p{L}\p{M}\p{N}\p{Pc}]+").Select(m => m.Value).ToArray();
        if (a.Length > 2 && b.Length > 2 && a.Skip(1).SkipLast(1).Intersect(b.Skip(1).SkipLast(1), StringComparer.Ordinal).Any()) return null;
        return (rule, start);
    }
}

public sealed class ReplacementStore
{
    public const int MaxRules = 250;
    private readonly string path;
    private List<ReplacementRule> rules;
    public IReadOnlyList<ReplacementRule> Rules => rules.AsReadOnly();
    public ReplacementStore(string path)
    {
        this.path = path;
        rules = File.Exists(path) ? JsonSerializer.Deserialize<List<ReplacementRule>>(WindowsUserProtection.Transform(File.ReadAllBytes(path), false))
            ?? throw new InvalidDataException("Korrekturdatei ist ungültig.") : [];
        Validate(rules);
    }
    private static void Validate(List<ReplacementRule> candidates)
    {
        if (candidates.Count > MaxRules || candidates.Any(r => r == null || !r.IsValid) ||
            candidates.Select(r => r.From).Distinct(StringComparer.Ordinal).Count() != candidates.Count)
            throw new InvalidDataException("Korrekturdatei enthält ungültige Regeln.");
    }
    public void Upsert(ReplacementRule rule)
    {
        var next = rules.Where(r => r.From != rule.From).Append(rule).ToList();
        Commit(next);
    }
    public void Remove(string from) => Commit(rules.Where(r => r.From != from).ToList());
    private void Commit(List<ReplacementRule> next)
    {
        Validate(next);
        byte[] bytes = WindowsUserProtection.Transform(JsonSerializer.SerializeToUtf8Bytes(next), true);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string staging = path + ".tmp";
        try
        {
            using (var stream = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            File.Move(staging, path, true);
            rules = next; // Do not activate a rule unless persistence succeeded.
        }
        finally { try { if (File.Exists(staging)) File.Delete(staging); } catch (IOException) { } }
    }
}

public sealed class CorrectionDialog : Form
{
    public bool Remember { get; private set; }
    public CorrectionDialog(ReplacementRule rule, bool canRemember, bool replacesExisting)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Diese Korrektur immer anwenden?"; ClientSize = new Size(730, 440); Icon = BrandTheme.Icon;
        StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; Font = BrandTheme.Body;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "Bisher erkannt", AutoSize = true });
        layout.Controls.Add(new TextBox { Text = rule.From, ReadOnly = true, Multiline = true, Height = 50, Dock = DockStyle.Top, AccessibleName = "Bisher erkannt", ScrollBars = ScrollBars.Vertical });
        layout.Controls.Add(new Label { Text = "Deine Korrektur", AutoSize = true });
        layout.Controls.Add(new TextBox { Text = rule.To, ReadOnly = true, Multiline = true, Height = 50, Dock = DockStyle.Top, AccessibleName = "Deine Korrektur", ScrollBars = ScrollBars.Vertical });
        layout.Controls.Add(new Label { Text = (canRemember ? "Künftig exakt so ersetzen – Groß-/Kleinschreibung beachten.\nNur ganze Wörter oder Wortfolgen; lokal gespeichert." : "Regeldatei nicht verfügbar. Korrektur ist nur diesmal möglich.") +
            (replacesExisting ? "\nDie bisherige Regel für diesen Ausdruck wird ersetzt." : ""), AutoSize = true, MaximumSize = new Size(570, 0), Margin = new Padding(0, 12, 0, 12) });
        var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        // Sprint C (S8): Hervorhebung und Enter fallen zusammen; Enter erzeugt nie aus Versehen eine dauerhafte Regel.
        var once = new GardenButton { Text = "Nur diesmal", DialogResult = DialogResult.OK, Primary = true };
        var always = new GardenButton { Text = "Immer ersetzen", Enabled = canRemember };
        always.Click += (_, _) => { Remember = true; DialogResult = DialogResult.OK; };
        var back = new GardenButton { Text = "Weiter bearbeiten", DialogResult = DialogResult.Cancel };
        row.Controls.AddRange([once, always, back]); layout.Controls.Add(row); Controls.Add(layout);
        BrandTheme.Apply(this);
        AcceptButton = once; CancelButton = back;
        ResumeLayout(true);
        // Sprint C: Höhe nach Inhalt statt fester 440 logischer Pixel; gemessen nach der Skalierung bei der aktuellen Breite.
        int needed = layout.GetPreferredSize(new Size(ClientSize.Width, 0)).Height;
        if (needed > 0 && needed < ClientSize.Height) ClientSize = new Size(ClientSize.Width, needed);
    }
}
