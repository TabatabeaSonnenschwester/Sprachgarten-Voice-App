using System.Text;
using Klartext;

internal static class CorrectionTests
{
    internal static void Run(Action<string, bool, object?> record)
    {
        void Check(string name, bool pass) => record("corrections-" + name, pass, null);
        Check("word-suggestion", CorrectionEngine.Suggest("Das Hauß ist groß.", "Das Haus ist groß.") == new ReplacementRule("Hauß", "Haus"));
        Check("missing-letter-suggestion", CorrectionEngine.Suggest("Hallo Ale", "Hallo Alex") == new ReplacementRule("Ale", "Alex"));
        Check("extra-letter-suggestion", CorrectionEngine.Suggest("Hallo Aalex", "Hallo Alex") == new ReplacementRule("Aalex", "Alex"));
        Check("phrase-suggestion", CorrectionEngine.Suggest("Wir nutzen Klar Text.", "Wir nutzen Klartext.") == new ReplacementRule("Klar Text", "Klartext"));
        Check("disjoint-edit-no-rule", CorrectionEngine.Suggest("Dahs schöne Hauß", "Das schöne Haus") == null);
        Check("unchanged-no-rule", CorrectionEngine.Suggest("Hallo", "Hallo") == null);
        Check("empty-no-rule", CorrectionEngine.Suggest("", "Hallo") == null && CorrectionEngine.Suggest("Hallo", "") == null);
        Check("whitespace-control-rules-invalid", !new ReplacementRule(" ", "x").IsValid && !new ReplacementRule("x", "a\nb").IsValid);
        Check("phrase-limit", CorrectionEngine.Suggest(new string('a', 161), new string('b', 161)) == null);
        var rules = new[] { new ReplacementRule("Hauß", "Haus"), new ReplacementRule("Klar Text", "Klartext") };
        Check("word-boundaries-and-case", CorrectionEngine.Apply("Hauß Haußboot hauß (Hauß)!", rules) == "Haus Haußboot hauß (Haus)!");
        Check("umlaut-combining-boundaries", CorrectionEngine.Apply("Haußä Hauß\u0308 _Hauß Hauß2", rules) == "Haußä Hauß\u0308 _Hauß Hauß2");
        Check("unicode-crlf-preserved", CorrectionEngine.Apply("ÄÖÜ 😀\r\nKlar Text: Hauß €", rules) == "ÄÖÜ 😀\r\nKlartext: Haus €");
        Check("literal-regex-and-replacement", CorrectionEngine.Apply("C++ (a.b) $1", [new("C++", "C#"), new("(a.b)", "$2\\Ordner")]) == "C# $2\\Ordner $1");
        Check("no-cascade", CorrectionEngine.Apply("A B", [new("A", "B"), new("B", "C")]) == "B C");
        Check("no-cycle", CorrectionEngine.Apply("A B", [new("A", "B"), new("B", "A")]) == "B A");
        Check("longest-phrase-first", CorrectionEngine.Apply("Klar Text Klar", [new("Klar", "kurz"), new("Klar Text", "Klartext")]) == "Klartext kurz");
        var initial = new[] { new ReplacementRule("Klar Text", "Klartext") };
        string raw = "Wir nutzen Klar Text.";
        var refined = CorrectionEngine.SuggestWithOrigin(CorrectionEngine.Apply(raw, initial), "Wir nutzen KLARTEXT.", raw, initial);
        Check("refinement-targets-original-source", refined == new ReplacementRule("Klar Text", "KLARTEXT") && CorrectionEngine.Apply(raw, [refined!]) == "Wir nutzen KLARTEXT.");
        var phraseRules = new[] { new ReplacementRule("falsch", "guten Morgen") };
        var phraseRefined = CorrectionEngine.SuggestWithOrigin("Einen guten Morgen!", "Einen guten Abend!", "Einen falsch!", phraseRules);
        Check("partial-refinement-in-phrase", phraseRefined == new ReplacementRule("falsch", "guten Abend"));
        Check("recovered-text-no-unsafe-chain", CorrectionEngine.SuggestWithOrigin("Klartext", "KLARTEXT", null, initial) == null);
        Check("recovered-partial-phrase-no-unsafe-chain", CorrectionEngine.SuggestWithOrigin("Einen guten Morgen!", "Einen guten Abend!", null, phraseRules) == null);
        Check("known-original-word-not-suppressed", CorrectionEngine.SuggestWithOrigin("Morgen falsch", "Abend falsch", "Morgen falsch", [new("anderen Fehler", "guten Morgen")]) == new ReplacementRule("Morgen", "Abend"));
        string root = Path.Combine(AppPaths.Root, "corrections-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "replacements.bin");
        try
        {
            var store = new ReplacementStore(path);
            Check("first-start-empty", store.Rules.Count == 0);
            store.Upsert(new("Klar Text", "Klartext"));
            Check("encrypted-persistence", !Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("Klartext"));
            store = new ReplacementStore(path);
            Check("restart-applies-confirmed-rule", CorrectionEngine.Apply("Klar Text", store.Rules) == "Klartext");
            store.Upsert(new("Klar Text", "KLARTEXT"));
            Check("replace-existing-source", new ReplacementStore(path).Rules is { Count: 1 } && store.Rules[0].To == "KLARTEXT");
            byte[] previous = File.ReadAllBytes(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                bool rejected = false; try { store.Upsert(new("Fehler", "Richtig")); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { rejected = true; }
                Check("failed-write-not-active", rejected && store.Rules.Count == 1);
            }
            Check("failed-write-preserves-file", previous.SequenceEqual(File.ReadAllBytes(path)));
            store.Remove("Klar Text"); Check("delete-persists-restart", new ReplacementStore(path).Rules.Count == 0);
            File.WriteAllText(path, "broken");
            bool corrupt = false; try { _ = new ReplacementStore(path); } catch { corrupt = true; }
            Check("corrupt-file-preserved", corrupt && File.ReadAllText(path) == "broken");
            // Same protected store used by UI edits: no second rule pass when recovering.
            string originalRoot = AppPaths.Root;
            try
            {
                AppPaths.Root = root;
                RecoveryStore.Save("Korrigiert: Äöß 😀\r\nZeile zwei");
                var state = new SessionState { HasUncopiedText = true };
                Check("edited-result-recovery-and-record-guard", !state.CanStart && RecoveryStore.Load() == "Korrigiert: Äöß 😀\r\nZeile zwei");
                RecoveryStore.Clear();
            }
            finally { AppPaths.Root = originalRoot; }
        }
        finally
        {
            // Only test-owned flat files in the freshly generated directory.
            foreach (string file in Directory.GetFiles(root)) File.Delete(file);
            Directory.Delete(root);
        }
    }
}
