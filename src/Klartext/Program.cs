namespace Klartext;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--moonshine-selftest") return MoonshineDiagnostics.RunAsync(args[1],args[2]).GetAwaiter().GetResult();
        if (args.Length == 2 && args[0] == "--moonshine-window-check") return MoonshineDiagnostics.WindowCheck(args[1]);
        if (args.Length == 1 && args[0] == "--read-selection") return SelectedText.Run();
        if (args.Length == 5 && args[0] == "--prompt-host") return LocalPromptModel.RunHostAsync(args[1], args[2], args[3], args[4]).GetAwaiter().GetResult();
        if (args.Length == 2 && args[0] == "--verify-prompt-review")
        {
            try
            {
                const string input = "Fasse den folgenden Text in fünf Stichpunkten zusammen.";
                var result = new LocalPromptModel().ReviewAsync(input, CancellationToken.None, seedOverride: 42).GetAwaiter().GetResult();
                File.WriteAllText(Path.GetFullPath(args[1]), System.Text.Json.JsonSerializer.Serialize(result));
                return result.Review.Questions.Length == 1 && result.Review.Questions[0].Question.Contains("Text", StringComparison.OrdinalIgnoreCase) && result.Review.Suggestions.Length == 0 ? 0 : 4;
            }
            catch { return 2; }
        }
        if (args.Length == 2 && args[0] == "--verify-prompt")
        {
            // Explicit package diagnostic: synthetic data only, never reads a user draft or uses the microphone.
            try
            {
                const string input = "Erstelle einen Workshop über Küchenkräuter für Erwachsene ohne Vorkenntnisse. Dauer 45 Minuten. Ausgabe als Tabelle mit Zeit und Aktivität.";
                var result = new LocalPromptModel().ComposeAsync(input, CancellationToken.None, seedOverride: 42).GetAwaiter().GetResult();
                File.WriteAllText(Path.GetFullPath(args[1]), System.Text.Json.JsonSerializer.Serialize(result));
                return !result.KeptOriginal && result.Text.Contains("45") && result.Text.Contains("Erwachsene") && result.Text.Contains("Tabelle") ? 0 : 4;
            }
            catch { return 2; }
        }
        if (args.Length == 5 && args[0] == "--worker" && !Transcriber.WaitForParentJob(args[4])) return 3;
        if (args.Length is 4 or 5 && args[0] == "--worker")
            return Transcriber.RunWorkerAsync(args[1], args[2], args[3]).GetAwaiter().GetResult();
        int index = Array.IndexOf(args, "--data-dir");
        if (index >= 0 && args.Length > index + 1) AppPaths.Root = Path.GetFullPath(args[index + 1]);
        ApplicationConfiguration.Initialize();
        // Safety net for async-void handlers: German wording, only the exception type, no repeat storms.
        string lastFailure = "";
        DateTime lastFailureAt = DateTime.MinValue;
        Application.ThreadException += (_, e) =>
        {
            string kind = e.Exception.GetType().Name;
            if (kind == lastFailure && DateTime.UtcNow - lastFailureAt < TimeSpan.FromSeconds(10)) return;
            lastFailure = kind;
            lastFailureAt = DateTime.UtcNow;
            MessageBox.Show("In Sprachgarten ist etwas unerwartet schiefgegangen. Dein Text und deine Aufnahme bleiben erhalten. Bitte den letzten Schritt wiederholen; hilft das nicht, Sprachgarten neu starten.\n" + kind,
                "Sprachgarten", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        // Explicit test profiles may coexist; the normal user profile retains its existing single-instance name.
        string suffix = index >= 0 ? "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(AppPaths.Root).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()))) : "";
        using var mutex = new Mutex(true, "Local\\Klartext.Dictation.v1" + suffix, out bool created);
        if (!created) return 0;
        try
        {
            AppPaths.Initialize();
            using var main = new MainForm(args.Contains("--tray"));
            Application.Run(main);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Sprachgarten konnte nicht starten. Bitte das vollständige Paket entpacken und Schreibrechte im App-Datenordner prüfen.\n" + ex.GetType().Name,
                "Sprachgarten", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally { BrandTheme.Release(); mutex.ReleaseMutex(); }
    }
}
