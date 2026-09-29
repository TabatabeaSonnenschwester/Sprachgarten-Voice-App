using System.Diagnostics;

namespace Klartext;

public sealed record AgentModel(string Provider, string Id, string Label, string Reason, string[] Efforts)
{
    public override string ToString() => Label;
}
public sealed class AgentPreferences
{
    public string Provider { get; set; } = "Codex";
    public string Model { get; set; } = "gpt-5.6-sol";
    public string Effort { get; set; } = "medium";
    public string CodexPath { get; set; } = "";
    public string ClaudePath { get; set; } = "";
    public string Folder { get; set; } = "";
    public static AgentPreferences Load() => ProtectedFile.Read<AgentPreferences>(Path.Combine(AppPaths.Root, "agent-preferences.bin")) ?? new();
    public void Save() => ProtectedFile.Write(Path.Combine(AppPaths.Root, "agent-preferences.bin"), this);
}
public static class AgentHandoff
{
    public static readonly AgentModel[] Models =
    [
        new("Codex", "gpt-5.6-sol", "GPT-5.6 Sol", "Für klar abgegrenzte Umsetzung und alltägliche Arbeit; zunächst mittel, bei schwieriger Fehlersuche hoch.", ["low", "medium", "high", "xhigh"]),
        new("Codex", "gpt-6-astra", "GPT-6 Astra", "Für komplexe Vorhaben, viele Abhängigkeiten und anspruchsvolle Analyse; hoch als Ausgangspunkt, mehr Aufwand nur bei erkennbarem Nutzen.", ["low", "medium", "high", "xhigh"]),
        new("Claude Code", "claude-sonnet-5", "Claude Sonnet 5", "Für normale Umsetzung mit gutem Verhältnis von Leistung und Verbrauch; mittel für klare Aufgaben, hoch für anspruchsvolle Aufgaben.", ["low", "medium", "high"]),
        new("Claude Code", "claude-opus-5", "Claude Opus 5", "Für schwierige Abwägungen und umfangreiche Änderungen; hoch als Ausgangspunkt. Mehr Verbrauch als Sonnet.", ["low", "medium", "high"])
    ];
    public static (string Model, string Effort, string Reason) Recommend(string provider, bool complex) =>
        provider == "Codex" ? (complex ? "gpt-6-astra" : "gpt-5.6-sol", complex ? "high" : "medium", complex ? "Mehrere Abhängigkeiten: Astra mit hohem Denkaufwand als Ausgangspunkt. Ergebnis und Tests bleiben entscheidend." : "Klarer Auftrag: Sol mit mittlerem Denkaufwand hält Aufwand und Bearbeitungszeit im Rahmen.") :
        (complex ? "claude-opus-5" : "claude-sonnet-5", complex ? "high" : "medium", complex ? "Komplexe Abwägungen: Opus mit hohem Denkaufwand. Höherer Verbrauch ist hier eher gerechtfertigt." : "Klarer Auftrag: Sonnet mit mittlerem Denkaufwand als wirtschaftlicher Startpunkt.");
    public static string? FindExecutable(string provider)
    {
        string name = provider == "Codex" ? "codex.exe" : "claude.exe";
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try { string path = Path.Combine(folder.Trim('"'), name); if (Path.IsPathFullyQualified(path) && File.Exists(path)) return path; } catch { }
        }
        if (provider == "Claude Code")
        {
            string candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
    public static ProcessStartInfo CreateStart(string exe, string folder, AgentModel model, string effort, string prompt)
    {
        if (!Models.Contains(model) || !model.Efforts.Contains(effort)) throw new InvalidDataException("Diese Modell-/Denkaufwand-Kombination wird nicht angeboten.");
        if (!Path.IsPathFullyQualified(exe) || !File.Exists(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Bitte die installierte Agenten-EXE auswählen. Keine Skript-/Befehlszeile eingeben.");
        if (!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder)) throw new InvalidDataException("Bitte einen vorhandenen Arbeitsordner auswählen.");
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 24000 || prompt.Contains('\0')) throw new InvalidDataException("Auftrag muss 1 bis 24.000 Zeichen enthalten.");
        var start = new ProcessStartInfo(exe) { WorkingDirectory = folder, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal };
        start.ArgumentList.Add("--model"); start.ArgumentList.Add(model.Id);
        if (model.Provider == "Codex")
        {
            start.ArgumentList.Add("-c"); start.ArgumentList.Add($"model_reasoning_effort=\"{effort}\"");
            start.ArgumentList.Add("--sandbox"); start.ArgumentList.Add("workspace-write");
            start.ArgumentList.Add("--ask-for-approval"); start.ArgumentList.Add("on-request");
            start.ArgumentList.Add("--cd"); start.ArgumentList.Add(folder);
        }
        else { start.ArgumentList.Add("--effort"); start.ArgumentList.Add(effort); start.ArgumentList.Add("--permission-mode"); start.ArgumentList.Add("default"); }
        // No shell interpolation. The prompt is exactly one argument, including quotes/newlines/metacharacters.
        start.ArgumentList.Add("--"); start.ArgumentList.Add(prompt);
        if (exe.Length + start.ArgumentList.Sum(a => a.Length * 2 + 3) > 30000) throw new InvalidDataException("Dieser Auftrag ist für die Windows-Übergabe zu lang. Bitte kürzen oder im Agenten manuell einfügen.");
        return start;
    }
    public static async Task ValidateInstalledAsync(string exe, AgentModel model, string effort)
    {
        if (model.Provider == "Claude Code" && Environment.GetEnvironmentVariable("CLAUDE_CODE_EFFORT_LEVEL") is { Length: > 0 } inherited && inherited != "auto" && inherited != effort)
            throw new InvalidDataException("Eine vorhandene CLAUDE_CODE_EFFORT_LEVEL-Umgebungsvariable würde deine Auswahl überschreiben. Bitte deren Einstellung zuerst bewusst abgleichen.");
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--version");
        using var process = Process.Start(start) ?? throw new InvalidDataException("Agentenprogramm startet nicht.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token); var errors = process.StandardError.ReadToEndAsync(deadline.Token);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { try { process.Kill(true); } catch { } throw new InvalidDataException("Agenten-Version konnte nicht rechtzeitig geprüft werden."); }
        string version = await output; await errors;
        if (process.ExitCode != 0) throw new InvalidDataException("Agenten-Version konnte nicht geprüft werden.");
        if (model.Provider == "Codex" && !version.Contains("codex", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Diese EXE meldet sich in ihrer Versionsausgabe nicht als Codex. Das ist ein Namensvergleich, keine Echtheitsprüfung; es wurde kein Auftrag gestartet.");
        if (model.Provider == "Claude Code")
        {
            var match = System.Text.RegularExpressions.Regex.Match(version, @"\b(\d+\.\d+\.\d+)\b");
            var minimum = model.Id == "claude-opus-5" ? new Version(2, 1, 219) : new Version(2, 1, 197);
            if (!version.Contains("Claude", StringComparison.OrdinalIgnoreCase) || !match.Success || !Version.TryParse(match.Value, out var installed) || installed < minimum)
                throw new InvalidDataException($"Bitte Claude Code mindestens {minimum} für dieses Modell installieren/aktualisieren. Es wurde kein Auftrag gestartet.");
        }
    }
}

public sealed class AgentHandoffDialog : Form
{
    private readonly ComboBox provider = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, AccessibleName = "Auftrag ausführen mit" };
    private readonly ComboBox model = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, AccessibleName = "Ausführungsmodell" };
    private readonly ComboBox effort = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, AccessibleName = "Denkaufwand" };
    public AgentHandoffDialog(string prompt)
    {
        // Sprint C (S9): skaliert wie der Korrekturdialog; Escape bricht ab. Kein AcceptButton: Enter darf keinen Agenten starten.
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Arbeitsauftrag vorbereiten"; Font = BrandTheme.Body; Size = new(820, 780); MinimumSize = new(620, 600); StartPosition = FormStartPosition.CenterParent;
        var prefs = AgentPreferences.Load();
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(14) }; Controls.Add(flow);
        var preview = WorkshopUi.Text("Fertiger Auftrag · unveränderte Vorschau", 170); preview.ReadOnly = true; preview.Text = prompt; preview.Width = 730; flow.Controls.Add(preview);
        flow.Controls.Add(WorkshopUi.Hint("Ausführung wählen · unabhängig von Claude als Promptprüfer"));
        provider.Items.AddRange(["Codex", "Claude Code"]);
        flow.Controls.Add(WorkshopUi.Hint("Agent")); flow.Controls.Add(provider);
        flow.Controls.Add(WorkshopUi.Hint("Modell")); flow.Controls.Add(model);
        flow.Controls.Add(WorkshopUi.Hint("Denkaufwand: low = niedrig · medium = mittel · high = hoch · xhigh = sehr hoch")); flow.Controls.Add(effort);
        var explanation = WorkshopUi.Hint(""); explanation.MaximumSize = new(700, 0); flow.Controls.Add(explanation);
        var complexity = new ComboBox { Width = 420, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Aufgabenkomplexität für Empfehlung" };
        complexity.Items.AddRange(["Klar abgegrenzter Auftrag", "Komplex · mehrere Abhängigkeiten / schwierige Analyse"]); complexity.SelectedIndex = 0;
        var recommendation = WorkshopUi.Hint(""); recommendation.MaximumSize = new(700, 0); flow.Controls.Add(recommendation);
        flow.Controls.Add(WorkshopUi.Row(complexity, WorkshopUi.Button("Empfehlung übernehmen", (_, _) => ApplyRecommendation())));
        void UpdateRecommendation() { var r = AgentHandoff.Recommend(provider.Text, complexity.SelectedIndex == 1); recommendation.Text = "Empfehlung: " + r.Reason + " Du kannst Modell und Aufwand unabhängig ändern. Gleiche Stufennamen sind zwischen Modellen nicht direkt vergleichbar."; }
        void ApplyRecommendation() { var r = AgentHandoff.Recommend(provider.Text, complexity.SelectedIndex == 1); model.SelectedItem = AgentHandoff.Models.First(m => m.Id == r.Model); effort.SelectedItem = r.Effort; UpdateRecommendation(); }
        var executable = new TextBox { Width = 560, AccessibleName = "Agentenprogramm EXE" };
        var folder = new TextBox { Width = 560, AccessibleName = "Arbeitsordner", Text = prefs.Folder };
        flow.Controls.Add(WorkshopUi.Hint("Installiertes Agentenprogramm (.exe)"));
        flow.Controls.Add(WorkshopUi.Row(executable, WorkshopUi.Button("Auswählen …", (_, _) => { using var d = new OpenFileDialog { Filter = "Agentenprogramm|*.exe" }; if (d.ShowDialog(this) == DialogResult.OK) executable.Text = d.FileName; })));
        flow.Controls.Add(WorkshopUi.Hint("Arbeitsordner des Auftrags"));
        flow.Controls.Add(WorkshopUi.Row(folder, WorkshopUi.Button("Ordner …", (_, _) => { using var d = new FolderBrowserDialog(); if (d.ShowDialog(this) == DialogResult.OK) folder.Text = d.SelectedPath; })));
        var note = WorkshopUi.Hint("Der Agent öffnet sich in einem eigenen interaktiven Fenster. Seine Anmeldung, Abrechnung und Projektregeln gelten; der Prüf-API-Schlüssel wird nicht übergeben. Ein Ordner ist keine vollständige Zugriffssperre. Freigaben bearbeitest du im Agenten. Modellverfügbarkeit hängt vom Konto ab; die angebotenen Kombinationen sind ein dokumentierter Katalog, keine live geprüfte Kontoliste."); note.MaximumSize = new(700, 0); flow.Controls.Add(note);
        var consent = new CheckBox { AutoSize = false, MaximumSize = new(700, 0), Text = "Diesen angezeigten Auftrag jetzt mit dieser Auswahl starten (Agent kann im Arbeitsordner arbeiten)", AccessibleName = "Arbeitsauftrag ausdrücklich starten" }; flow.Controls.Add(consent);
        // Sprint E (K4): Die Zustimmung nennt das Programm, das startet, mit vollem Pfad.
        string consentBase = consent.Text;
        void UpdateConsent() => consent.Text = consentBase + " Programm: " + (string.IsNullOrWhiteSpace(executable.Text) ? "noch keines ausgewählt" : executable.Text);
        var status = WorkshopUi.Hint(""); status.MaximumSize = new(700, 0);
        bool launching = false;
        flow.Controls.Add(WorkshopUi.Button("Arbeitsauftrag starten", async (_, _) =>
        {
            if (launching) return;
            if (!consent.Checked) { status.Text = "Bitte zuerst den angezeigten Auftrag und die Auswahl bestätigen."; return; }
            launching = true; flow.Enabled = false;
            try
            {
                if (model.SelectedItem is not AgentModel selected) throw new InvalidDataException("Modell auswählen.");
                var start = AgentHandoff.CreateStart(executable.Text, folder.Text, selected, effort.Text, prompt);
                status.Text = "Installierte Agentenversion prüfen …";
                await AgentHandoff.ValidateInstalledAsync(executable.Text, selected, effort.Text);
                if (IsDisposed) return;
                prefs.Provider = provider.Text; prefs.Model = selected.Id; prefs.Effort = effort.Text; prefs.Folder = folder.Text;
                if (provider.Text == "Codex") prefs.CodexPath = executable.Text; else prefs.ClaudePath = executable.Text;
                prefs.Save();
                using var process = Process.Start(start) ?? throw new InvalidDataException("Agentenprozess konnte nicht gestartet werden.");
                consent.Checked = false;
                status.Text = "Agentenprozess gestartet. Bitte im neuen Fenster Anmeldung, Modell, Denkaufwand und Start prüfen. Das ist noch kein Nachweis einer erfolgreichen Ausführung.";
            }
            catch (InvalidDataException ex) { status.Text = ex.Message; }
            catch { status.Text = "Start oder Speichern fehlgeschlagen. Installation/Arbeitsordner prüfen. Der Prompt bleibt erhalten."; }
            finally { launching = false; if (!IsDisposed) flow.Enabled = true; }
        }));
        // Im selben Fluss wie die übrigen Aktionen; während des Starts mit dem ganzen Fluss gesperrt.
        var cancel = new GardenButton { Text = "Abbrechen", DialogResult = DialogResult.Cancel };
        flow.Controls.Add(cancel); flow.Controls.Add(status);
        provider.SelectedIndexChanged += (_, _) =>
        {
            model.Items.Clear(); model.Items.AddRange(AgentHandoff.Models.Where(m => m.Provider == provider.Text).Cast<object>().ToArray()); model.SelectedIndex = 0;
            executable.Text = (provider.Text == "Codex" ? prefs.CodexPath : prefs.ClaudePath) is { Length: > 0 } saved ? saved : AgentHandoff.FindExecutable(provider.Text) ?? "";
            consent.Checked = false; UpdateRecommendation();
        };
        model.SelectedIndexChanged += (_, _) => { if (model.SelectedItem is AgentModel selected) { effort.Items.Clear(); effort.Items.AddRange(selected.Efforts); effort.SelectedItem = "medium"; explanation.Text = selected.Reason; consent.Checked = false; } };
        effort.SelectedIndexChanged += (_, _) => consent.Checked = false;
        folder.TextChanged += (_, _) => consent.Checked = false; executable.TextChanged += (_, _) => { consent.Checked = false; UpdateConsent(); };
        complexity.SelectedIndexChanged += (_, _) => UpdateRecommendation();
        provider.SelectedItem = prefs.Provider == "Claude Code" ? "Claude Code" : "Codex";
        var existing = AgentHandoff.Models.FirstOrDefault(m => m.Provider == provider.Text && m.Id == prefs.Model); if (existing != null) model.SelectedItem = existing;
        if (effort.Items.Contains(prefs.Effort)) effort.SelectedItem = prefs.Effort;
        UpdateConsent();
        BrandTheme.Apply(this);
        WorkshopDialogLayout.Attach(flow);
        WrapCheckBoxText(consent, flow);
        CancelButton = cancel; AcceptButton = null;
        ResumeLayout(true);
    }
    protected override void OnLoad(EventArgs e) { base.OnLoad(e); BrandTheme.FitToWorkingArea(this); }
    // Sprint E (7b): Ankreuzfelder mit langem Text brechen um. AutoSize mit MaximumSize schnitt den Text in einer Zeile ab
    // (Zustimmungszeile ohne Pfad, Verbindungsdialog „… und Consol“). Breite aus dem Fluss (MaximumSize, gesetzt von
    // WorkshopDialogLayout), Höhe aus dem umbrochenen Text. Gesetzt im Layout-Ereignis des Flusses, also vor dem Anordnen
    // (Muster FitInfo, Sprint C), mit Wiedereintrittsschutz. Text, Name und Häkchen bleiben unberührt; gezeichnet wird wie bisher.
    internal static void WrapCheckBoxText(CheckBox box, FlowLayoutPanel flow)
    {
        box.AutoSize = false;
        bool fitting = false;
        void Fit()
        {
            if (fitting) return;
            fitting = true;
            try
            {
                int width = box.MaximumSize.Width > 0 ? box.MaximumSize.Width : Math.Max(1, flow.ClientSize.Width - flow.Padding.Horizontal - box.Margin.Horizontal);
                // Randbedarf (Kästchen, Abstand, Innenrand) laut WinForms: bevorzugte Einzeilengröße minus einzeilige Textgröße.
                var singleLine = TextRenderer.MeasureText(box.Text, box.Font); var preferred = box.GetPreferredSize(Size.Empty);
                int textWidth = Math.Max(1, width - Math.Max(0, preferred.Width - singleLine.Width));
                int height = TextRenderer.MeasureText(box.Text, box.Font, new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak).Height + Math.Max(0, preferred.Height - singleLine.Height);
                if (box.Width != width || box.Height != height) box.Size = new(width, height);
            }
            finally { fitting = false; }
        }
        flow.Layout += (_, _) => Fit();
        flow.ClientSizeChanged += (_, _) => Fit();
        box.TextChanged += (_, _) => Fit();
        box.FontChanged += (_, _) => Fit();
        box.SizeChanged += (_, _) => Fit();
        Fit();
    }
}
