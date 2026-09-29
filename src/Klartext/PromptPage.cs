namespace Klartext;

public sealed class PromptPage : UserControl
{
    private readonly ActivityGate gate;
    private readonly Func<string> dictation;
    // Kompakter Einstieg; alle Textfelder wachsen weiterhin mit ihrem Inhalt.
    private readonly GardenTextBox original = WorkshopUi.Grow("Ausgangstext", 4);
    private readonly GardenTextBox goal = WorkshopUi.Grow("Aufgabe ergänzen", 3);
    private readonly GardenTextBox audience = WorkshopUi.Grow("Zielgruppe und Kontext", 3);
    private readonly GardenTextBox format = WorkshopUi.Grow("Gewünschtes Ausgabeformat", 3);
    private readonly GardenTextBox constraints = WorkshopUi.Grow("Verbindliche Vorgaben", 3);
    private readonly GardenTextBox draft = WorkshopUi.Grow("Mein Prompt", 6);
    private readonly GardenTextBox candidate = WorkshopUi.Grow("Ungeprüfter KI-Vorschlag", 12);
    private readonly GardenTextBox title = new() { Multiline = false, MaxLength = 120, AccessibleName = "Bibliothekstitel", PlaceholderText = "Titel für die Bibliothek" };
    private readonly GardenTextBox search = new() { Multiline = false, AccessibleName = "Bibliothek durchsuchen", PlaceholderText = "Bibliothek durchsuchen" };
    private readonly ListBox libraryList = new() { Height = 120, IntegralHeight = false, DisplayMember = "", AccessibleName = "Promptbibliothek" }; // Sprint E: sonst plant der Fluss 120 px, Windows zeichnet 94 (gemessen); die Differenz war die Lücke über der Versionsauswahl.
    private readonly GardenTextBox libraryPreview = WorkshopUi.Grow("Gespeicherter Prompt · Vorschau", 12);
    private readonly ComboBox versions = new() { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Gespeicherte Version" };
    private readonly Label info = WorkshopUi.Hint("Bereit. Manuelle Werkstatt funktioniert ohne KI-Modell.");
    private readonly Label findings = WorkshopUi.Hint("Noch nicht geprüft.");
    private readonly Label libraryHint = WorkshopUi.Hint("");
    private readonly GardenButton createFirstPrompt = new() { Text = "Zur Erstellung" };
    private readonly Label modelInfo = WorkshopUi.Hint("");
    private readonly Label workStatus = WorkshopUi.Hint("Noch kein Auftrag gestartet.");
    private readonly Label nextStep = WorkshopUi.Hint("");
    private readonly ComboBox reviewProvider = new() { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "KI für die Promptprüfung" };
    private bool OnlineReview => reviewProvider.SelectedIndex == 1;
    private readonly GardenButton generate = new() { Text = "Vorschlag erstellen" };
    private readonly GardenButton cancelAi = new() { Text = "Abbrechen", Enabled = false };
    private readonly GardenButton download = new() { Text = "KI einmalig herunterladen (2,50 GB)" };
    private readonly GardenButton verify = new() { Text = "Dateien erneut prüfen" };
    private readonly GardenButton adopt = new() { Text = "Diesen Vorschlag übernehmen", Enabled = false };
    private readonly GardenButton favorite = new() { Text = "Favorit umschalten" };
    private readonly GardenButton deleteEntry = new() { Text = "Eintrag löschen …" };
    // Sprint C (S11): Knöpfe mit Hauptaktionsrolle sind benannt, damit RefreshActions sie hervorheben kann.
    private readonly GardenButton importDictation = new() { Text = "Diktat übernehmen" };
    private readonly GardenButton newPrompt = new() { Text = "Neuer Prompt" };
    private readonly GardenButton adoptIdea = new() { Text = "So als Prompt übernehmen" };
    private readonly GardenButton build = new() { Text = "Aus Angaben bauen" };
    private readonly GardenButton copyPrompt = new() { Text = "Prompt kopieren" };
    private readonly GardenButton recheck = new() { Text = "Formal erneut prüfen" };
    private readonly GardenButton handoff = new() { Text = "Arbeitsauftrag vorbereiten …" };
    private readonly GardenButton saveVersion = new() { Text = "Speichern / neue Version" };
    private readonly GardenButton saveNew = new() { Text = "Als neuen Eintrag speichern" };
    private readonly GardenButton connection = new() { Text = "KI-Verbindung …" };
    private readonly GardenButton loadVersion = new() { Text = "Version laden" };
    // Freiwillige Bereiche starten zugeklappt; ihr Zustand gilt nur je Sitzung.
    private readonly GardenExpander saveBlock = new("In der Bibliothek speichern");
    private readonly GardenExpander connectionBlock = new("KI-Verbindung");
    private readonly GardenExpander helpBlock = new("Einrichtung / Fehlerhilfe");
    private readonly GardenExpander supplementsBlock = new("Ergänzungen · freiwillig");
    private readonly System.Windows.Forms.Timer workTimer = new() { Interval = 250 };
    private readonly System.Diagnostics.Stopwatch workClock = new();
    private PromptProgress currentProgress = new("Vorbereiten");
    private int workGeneration;
    private bool cancelling, modelInvalid, timedFormulation;
    private readonly Label candidateInfo = WorkshopUi.Hint("KI-Varianten werden nie automatisch übernommen.");
    private readonly Label reviewInfo = WorkshopUi.Hint("Die KI sucht einen wichtigen offenen Punkt. Du entscheidest, was du ergänzen möchtest.");
    private readonly Label reviewQuestion = WorkshopUi.Hint("");
    private readonly ComboBox reviewOptions = new() { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Mögliche Antworten · freiwillige Auswahl", Visible = false };
    private readonly GardenTextBox reviewAnswer = WorkshopUi.Grow("Meine Antwort auf die KI-Rückfrage", 3);
    private readonly CheckBox reviewSuggestion = new() { AutoSize = true, AccessibleName = "Freiwillige KI-Ergänzung auswählen", Visible = false };
    private PromptReview? review;
    private string? reviewSource;
    private readonly ProgressBar progress = new() { Height = 12, Visible = false, AccessibleName = "Fortschritt des aktuellen KI-Arbeitsschritts" };
    private readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 600 };
    private readonly string baseDraftPath;
    private readonly string freshDraftPath;
    private readonly GardenButton reloadDraft = new() { Text = "Erneut laden" };
    private readonly GardenButton freshDraft = new() { Text = "Leer neu anfangen" };
    private readonly FlowLayoutPanel recoveryRow = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Bottom, WrapContents = true, Visible = false, AccessibleName = "Ausweg bei nicht lesbarem Arbeitsentwurf" };
    private string draftPath;
    private PromptLibrary? library;
    private Guid? libraryId;
    private bool loading, damagedDraft, working;
    private readonly GardenPages views = new() { Dock = DockStyle.Fill, AccessibleName = "Werkstattinhalt" };
    private readonly GardenScrollPanel libraryWorkspace = new() { Dock = DockStyle.Fill, AccessibleName = "Fertige Prompts" };
    private readonly FlowLayoutPanel librarySheet = Column();
    private readonly GardenScrollPanel workspace = new() { Dock = DockStyle.Fill, AccessibleName = "Gemeinsame Promptwerkstatt" };
    private readonly FlowLayoutPanel sheet = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
    private readonly FlowLayoutPanel columns = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
    private readonly FlowLayoutPanel inputColumn = Column();
    private readonly FlowLayoutPanel outputColumn = Column();
    private readonly List<FlowLayoutPanel> sections = [];
    private bool arranging;
    private string? candidateSource;
    private readonly List<Control> editing = [];

    public PromptPage(ActivityGate gate, Func<string> dictation)
    {
        this.gate = gate; this.dictation = dictation; Dock = DockStyle.Fill; AutoScaleMode = AutoScaleMode.Dpi;
        baseDraftPath = Path.Combine(AppPaths.Root, "prompt-draft.bin");
        freshDraftPath = WorkshopUi.FreshPath(baseDraftPath);
        draftPath = baseDraftPath;
        Controls.Add(views);
        var creationTab = new TabPage("Erstellung"); var libraryTab = new TabPage("Bibliothek");
        views.TabPages.AddRange([creationTab, libraryTab]); creationTab.Controls.Add(workspace); libraryTab.Controls.Add(libraryWorkspace);
        libraryWorkspace.Controls.Add(librarySheet);
        workspace.Controls.Add(sheet); sheet.Controls.Add(columns);
        // Elementabstand zwischen innerer Leiste und erster Karte; skaliert wie die übrigen Innenabstände.
        sheet.Padding = new Padding(0, BrandTheme.ElementGap, 0, 0); librarySheet.Padding = new Padding(0, BrandTheme.ElementGap, 0, 0);
        columns.Controls.Add(inputColumn); columns.Controls.Add(outputColumn);
        workspace.ClientSizeChanged += (_, _) => ArrangeWorkspace();
        libraryWorkspace.ClientSizeChanged += (_, _) => ArrangeWorkspace();
        views.SelectedIndexChanged += (_, _) => ArrangeWorkspace();
        // Sprint C (8): Die Höhe folgt dem Text bei der aktuellen Breite; kurze Meldungen sind eine Zeile hoch.
        // Die Höhe wird in OnLayout vor dem Andocken gesetzt (Muster AudioToolsPage), nie aus SizeChanged heraus:
        // Eine Höhenänderung mitten im Andocken ließ die Oberkante auf dem alten Wert stehen (K33, Meldung über der Seite).
        info.Dock = DockStyle.Bottom; info.MaximumSize = new Size(0, 0); info.AutoSize = false; Controls.Add(info);
        info.TextChanged += (_, _) => PerformLayout();
        reloadDraft.Click += (_, _) =>
        {
            if (!working && !gate.Busy) ReloadDraft();
        };
        freshDraft.Click += (_, _) =>
        {
            if (working || gate.Busy) return;
            if (WorkshopUi.ConfirmFreshStart(this, Path.GetFileName(draftPath))) StartFreshDraft();
        };
        recoveryRow.Controls.Add(reloadDraft);
        recoveryRow.Controls.Add(freshDraft);
        Controls.Add(recoveryRow);
        Controls.Add(new GardenTabStrip(views) { AccessibleName = "Erstellung und Bibliothek" });
        importDictation.Click += (_, _) => ImportText(dictation());
        newPrompt.Click += (_, _) => NewPrompt();
        adoptIdea.Click += (_, _) => { if (CanEdit()) { draft.Text = original.Text; Check(); info.Text = "Text in „Mein Prompt“ übernommen. Dort bearbeiten, kopieren oder speichern."; ShowPrompt(); } };
        var flow = Section("Deine Idee", inputColumn);
        flow.Controls.Add(WorkshopUi.Row(importDictation, newPrompt));
        flow.Controls.Add(WorkshopUi.Hint("Text eingeben oder ein Diktat übernehmen. Dein Ausgangstext bleibt erhalten.")); flow.Controls.Add(original);
        flow.Controls.Add(adoptIdea);
        flow = Section("Ergänzungen · freiwillig", inputColumn);
        flow.Controls[0].Dispose();
        flow.Controls.Add(supplementsBlock);
        flow = supplementsBlock;
        flow.Controls.Add(WorkshopUi.Hint("Nur ausfüllen, was noch fehlt. Diese Angaben und angekreuzte Regeln kannst du in deinen Prompt einbauen."));
        AddField("Was soll das Ergebnis sein? (optional)", goal);
        AddField("Für wen / welcher Kontext? (optional)", audience);
        var choices = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Formatvorschläge" };
        choices.Items.AddRange(["Format selbst wählen …", "Kurzer Fließtext", "Stichpunkte", "Tabelle", "Schrittweise Anleitung"]); choices.SelectedIndex = 0;
        choices.SelectedIndexChanged += (_, _) => { if (choices.SelectedIndex > 0 && CanEdit()) format.Text = choices.SelectedItem!.ToString(); };
        flow.Controls.Add(choices); editing.Add(choices); BrandTheme.RelayWheel(choices); AddField("Gewünschtes Ausgabeformat (optional)", format); AddField("Was muss beachtet oder vermieden werden? (optional)", constraints);
        var suggestions = new[] { "Wenn wesentliche Angaben fehlen, stelle gezielte Rückfragen.", "Kennzeichne Annahmen und Unsicherheiten ausdrücklich.", "Erfinde keine Quellen oder Fakten." }
            .Select(text => new CheckBox { Text = text, AccessibleName = text, AutoSize = true }).ToArray();
        flow.Controls.Add(WorkshopUi.Hint("Zusatzregeln auswählen:"));
        foreach (var suggestion in suggestions) { flow.Controls.Add(suggestion); editing.Add(suggestion); }
        build.Click += (_, _) =>
        {
            if (!CanEdit()) return;
            string[] selected = suggestions.Where(s => s.Checked).Select(s => s.Text).ToArray();
            string composed = Document().Compose() + (selected.Length > 0 ? "\r\n\r\n" + string.Join("\r\n", selected) : "");
            if (composed.Length > 24000) { info.Text = "Zusammen mehr als 24.000 Zeichen. Bitte Angaben kürzen. Bisheriger Prompt bleibt erhalten."; return; }
            draft.Text = composed; Check(); info.Text = "Ausgangstext und Ergänzungen stehen jetzt gemeinsam in „Mein Prompt“."; ShowPrompt();
        };
        flow.Controls.Add(build);
        flow = Section("Mein Prompt", outputColumn);
        flow.Controls.Add(WorkshopUi.Hint("Deine aktuelle Fassung · frei bearbeitbar")); flow.Controls.Add(draft);
        copyPrompt.Click += async (_, _) => await CopyAsync();
        recheck.Click += (_, _) => Check();
        flow.Controls.Add(WorkshopUi.Row(copyPrompt, recheck));
        flow.Controls.Add(findings);
        handoff.Click += (_, _) =>
        {
            if (!CanEdit() || string.IsNullOrWhiteSpace(draft.Text)) { info.Text = "Zuerst deinen fertigen Prompt eintragen."; return; }
            try { using var dialog = new AgentHandoffDialog(draft.Text); dialog.ShowDialog(this); }
            catch { info.Text = "Auftragseinstellungen konnten nicht gelesen werden. Bestehende Daten bleiben erhalten."; }
        };
        flow.Controls.Add(handoff);
        saveVersion.Click += (_, _) => SaveLibrary(false);
        saveNew.Click += (_, _) => SaveLibrary(true);
        saveBlock.Controls.Add(title);
        saveBlock.Controls.Add(WorkshopUi.Row(saveVersion, saveNew));
        flow.Controls.Add(saveBlock);
        flow = Section("Mit der KI weiterarbeiten · freiwillig", outputColumn);
        reviewProvider.Items.AddRange(["Lokal · experimentell", "Claude · online über Console-Guthaben"]);
        reviewProvider.SelectedIndex = 0; editing.Add(reviewProvider); BrandTheme.RelayWheel(reviewProvider);
        try { if (ClaudeConnection.Load().Enabled) reviewProvider.SelectedIndex = 1; } catch { info.Text = "Claude-Einstellungen nicht lesbar. Bestehende Datei bleibt erhalten."; }
        connectionBlock.Controls.Add(reviewProvider);
        connection.Click += (_, _) =>
        {
            if (!CanEdit()) return;
            try { using var dialog = new ClaudeConnectionDialog(); if (dialog.ShowDialog(this) == DialogResult.OK) reviewProvider.SelectedIndex = ClaudeConnection.Load().Enabled ? 1 : 0; RefreshModelInfo(); RefreshActions(); }
            catch { info.Text = "Claude-Einstellungen nicht lesbar. Bestehende Datei bleibt erhalten; Zugriff gesperrt."; }
        };
        connectionBlock.Controls.Add(connection);
        flow.Controls.Add(connectionBlock);
        reviewProvider.SelectedIndexChanged += (_, _) => { RefreshModelInfo(); RefreshActions(); };
        flow.Controls.Add(nextStep);
        flow.Controls.Add(WorkshopUi.Row(generate, download, cancelAi));
        flow.Controls.Add(workStatus); flow.Controls.Add(progress);
        flow.Controls.Add(reviewInfo); flow.Controls.Add(reviewQuestion); flow.Controls.Add(reviewOptions); flow.Controls.Add(reviewAnswer); flow.Controls.Add(reviewSuggestion);
        reviewAnswer.MaxLength = 4000; reviewAnswer.Visible = false; reviewAnswer.PlaceholderText = "Antwort auswählen oder selbst schreiben. Du kannst die Frage auch offen lassen.";
        editing.AddRange([reviewOptions, reviewAnswer, reviewSuggestion]); BrandTheme.RelayWheel(reviewOptions);
        reviewOptions.SelectedIndexChanged += (_, _) => { if (!loading && reviewOptions.SelectedIndex >= 0) reviewAnswer.Text = reviewOptions.SelectedItem!.ToString(); };
        reviewAnswer.TextChanged += (_, _) => UpdateReviewCandidate(); reviewSuggestion.CheckedChanged += (_, _) => UpdateReviewCandidate();
        generate.Click += async (_, _) => await GenerateAsync();
        download.Click += async (_, _) => await DownloadAsync();
        cancelAi.Click += (_, _) => { if (working) { cancelling = true; gate.Cancel(); RefreshActions(); UpdateWorkStatus(); } };
        verify.Click += async (_, _) => await VerifyModelAsync();
        candidate.Visible = false; editing.Add(candidate); candidate.PlaceholderText = "Vorschau mit deinen gewählten Ergänzungen."; flow.Controls.Add(candidate); flow.Controls.Add(candidateInfo);
        flow.Controls.Add(adopt); adopt.Click += (_, _) =>
        {
            if (!CanEdit() || candidate.Text.Length == 0) return;
            if (candidateSource == null || candidateSource != draft.Text) { info.Text = "Der Entwurf hat sich geändert oder die App wurde neu gestartet. Erst eine neue Variante erzeugen."; return; }
            draft.Text = candidate.Text; candidateSource = null; info.Text = "Deine Ergänzungen übernommen. Du kannst weiter bearbeiten, erneut prüfen oder in der Bibliothek speichern."; Check();
            RefreshActions();
        };
        // Der frühere Umschaltknopf „Einrichtung / Fehlerhilfe …“ ist jetzt die Kopfzeile dieses Blocks.
        helpBlock.Controls.Add(modelInfo); helpBlock.Controls.Add(verify);
        flow.Controls.Add(helpBlock);
        flow = Section("Fertige Prompts", librarySheet);
        flow.Controls.Add(WorkshopUi.Hint("Gespeicherte Prompts suchen und wiederverwenden. Bis 200 Einträge mit je 20 Versionen; lokal mit deinem Windows-Konto geschützt."));
        flow.Controls.Add(search); flow.Controls.Add(libraryHint); flow.Controls.Add(createFirstPrompt);
        createFirstPrompt.Click += (_, _) => { views.SelectedIndex = 0; workspace.AutoScrollPosition = Point.Empty; original.Focus(); };
        flow.Controls.Add(libraryList); flow.Controls.Add(versions); BrandTheme.RelayWheel(versions);
        // Sprint C, Nachtrag 6b: Der Fluss rechnet sonst mit der vorgegebenen Höhe (33 px bei 150 %), Windows zeichnet die
        // Auswahlliste mit Indie Flower 14 pt aber 53 px hoch; die Vorschau rutschte 11 px unter ihre Unterkante.
        versions.AutoSize = true;
        libraryPreview.ReadOnly = true; flow.Controls.Add(libraryPreview);
        favorite.Click += (_, _) => ChangeFavorite();
        deleteEntry.Click += (_, _) => DeleteEntry();
        loadVersion.Click += (_, _) => LoadEntry();
        flow.Controls.Add(WorkshopUi.Row(loadVersion, favorite, deleteEntry));
        foreach (var box in new[] { original, goal, audience, format, constraints, draft, title }) { editing.Add(box); box.TextChanged += (_, _) => ScheduleSave(); }
        candidate.TextChanged += (_, _) => { candidate.Visible = !string.IsNullOrWhiteSpace(candidate.Text); ScheduleSave(); };
        search.TextChanged += (_, _) => RefreshLibrary();
        libraryList.SelectedIndexChanged += (_, _) =>
        {
            libraryPreview.Clear();
            versions.Items.Clear();
            if (libraryList.SelectedItem is PromptEntry entry) foreach (var version in entry.Versions) versions.Items.Add($"Version {version.Number} · {version.SavedAt:g}");
            if (versions.Items.Count > 0) versions.SelectedIndex = versions.Items.Count - 1;
        };
        versions.SelectedIndexChanged += (_, _) =>
        {
            libraryPreview.Text = libraryList.SelectedItem is PromptEntry entry && versions.SelectedIndex >= 0 ? entry.Versions[versions.SelectedIndex].Text : "";
        };
        try { library = new PromptLibrary(Path.Combine(AppPaths.Root, "prompt-library.bin")); RefreshLibrary(); }
        catch { info.Text = "Bibliothek nicht lesbar. Datei bleibt erhalten; Bibliotheksschreibzugriff gesperrt."; RefreshLibrary(); }
        LoadDraft();
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); Flush(); };
        workTimer.Tick += (_, _) => UpdateWorkStatus();
        gate.Changed += RefreshActions;
        RefreshModelInfo(); RefreshActions(); ArrangeWorkspace();
        void AddField(string label, Control c) { flow.Controls.Add(WorkshopUi.Hint(label)); flow.Controls.Add(c); }
        FlowLayoutPanel Section(string label, FlowLayoutPanel parent)
        {
            // Sprint C (T3): Karte mit weichen Ecken; Innenabstand und Kartenabstand aus dem Raster.
            var panel = new GardenCardFlow { AccessibleName = label };
            var heading = WorkshopUi.Hint(label); heading.Font = BrandTheme.Large; panel.Controls.Add(heading);
            parent.Controls.Add(panel); sections.Add(panel); return panel;
        }
    }
    private bool fittingInfo;
    private void FitInfo()
    {
        if (fittingInfo) return;
        fittingInfo = true;
        try
        {
            // Breite der Seite, nicht des Labels: Beim Andocken bekommt info genau diese Breite (Dock ignoriert Margin).
            int width = Math.Max(1, ClientSize.Width - Padding.Horizontal);
            int height = info.GetPreferredSize(new Size(width, 0)).Height;
            if (info.Height != height) info.Height = height;
        }
        finally { fittingInfo = false; }
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        // Höhe der Meldung vor dem Andocken festlegen, sonst bleibt ihre Oberkante auf dem alten Wert stehen.
        FitInfo();
        base.OnLayout(e);
    }
    private static FlowLayoutPanel Column() => new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
    private void ArrangeWorkspace()
    {
        if (arranging || sections.Count != 5) return;
        arranging = true;
        try
        {
            int gap = (int)(BrandTheme.CardGap * DeviceDpi / 96f);
            int width = Math.Max(260, workspace.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - gap * 2);
            bool wide = width >= 820 * DeviceDpi / 96f;
            // Im schmalen Fenster kommt das Ergebnis direkt nach der Idee, vor allen Extras.
            var promptSection = sections[2];
            var promptParent = wide ? outputColumn : inputColumn;
            if (promptSection.Parent != promptParent) promptParent.Controls.Add(promptSection);
            int promptIndex = wide ? 0 : 1;
            if (promptParent.Controls.GetChildIndex(promptSection) != promptIndex)
                promptParent.Controls.SetChildIndex(promptSection, promptIndex);
            sections[0].TabIndex = 0; promptSection.TabIndex = 1; sections[1].TabIndex = 2; sections[3].TabIndex = 3;
            columns.FlowDirection = wide ? FlowDirection.LeftToRight : FlowDirection.TopDown;
            int columnWidth = wide ? (width - gap) / 2 : width;
            inputColumn.Margin = new Padding(0, 0, wide ? gap : 0, 0);
            foreach (var section in sections)
            {
                int sectionWidth = section.Parent == librarySheet ? Math.Max(260, libraryWorkspace.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - gap * 2) : columnWidth;
                section.SuspendLayout();
                section.MinimumSize = new Size(sectionWidth, 0); section.MaximumSize = new Size(sectionWidth, 0); section.Width = sectionWidth;
                int inner = sectionWidth - section.Padding.Horizontal - 10;
                foreach (Control child in section.Controls)
                {
                    FitChild(child, inner);
                    // Sprint C (S11): Inhalte eines einklappbaren Blocks werden wie direkte Kinder behandelt.
                    if (child is GardenExpander expander)
                    {
                        expander.MinimumSize = new Size(inner, 0);
                        foreach (Control nested in expander.Controls) FitChild(nested, inner - expander.Padding.Horizontal);
                    }
                }
                section.ResumeLayout(true);
            }
        }
        finally { arranging = false; }
        static void FitChild(Control child, int inner)
        {
            if (child is Label or FlowLayoutPanel or CheckBox) child.MaximumSize = new Size(inner, 0);
            else if (child is not Button) child.Width = inner;
            if (child is FlowLayoutPanel row and not GardenExpander)
                foreach (var label in row.Controls.OfType<Label>()) label.MaximumSize = new Size(inner - 8, 0);
        }
    }
    private bool CanEdit() => !working && !gate.Busy && !damagedDraft;
    private void ShowPrompt()
    {
        ArrangeWorkspace(); workspace.PerformLayout(); draft.Focus();
        int top = workspace.PointToClient(draft.Parent!.PointToScreen(Point.Empty)).Y - workspace.AutoScrollPosition.Y;
        workspace.AutoScrollPosition = new Point(0, Math.Max(0, top));
    }
    private bool LibraryAvailable => !working && !gate.Busy && library != null;
    private PromptDocument Document() => new(original.Text, goal.Text, audience.Text, format.Text, constraints.Text, draft.Text, candidate.Text, title.Text, libraryId, review, reviewSource, reviewAnswer.Text, reviewSuggestion.Checked);
    private void ScheduleSave() { if (!loading && !damagedDraft) { saveTimer.Stop(); saveTimer.Start(); findings.Text = "Text geändert · bitte formal erneut prüfen. Inhaltliche Eignung bleibt ungeprüft."; RefreshActions(); } }
    public bool Flush()
    {
        saveTimer.Stop(); if (damagedDraft) return true; // Preserve unreadable file; never overwrite it.
        try { var doc = Document(); if (!doc.Valid) throw new InvalidDataException(); ProtectedFile.Write(draftPath, doc); return true; }
        catch { info.Text = "Entwurf nur im Fenster! Speichern fehlgeschlagen. Bitte kopieren; Beenden bleibt angehalten."; return false; }
    }
    private string DamagedDraftMessage()
    {
        if (draftPath == freshDraftPath) return $"Arbeitsentwurf nicht lesbar: weder {Path.GetFileName(baseDraftPath)} noch {Path.GetFileName(freshDraftPath)}. Beide Dateien bleiben unverändert liegen; sie werden weder gelöscht noch überschrieben. „Erneut laden“ liest erneut. Einen weiteren leeren Anfang biete ich nicht an, weil ein Lesefehler am Windows-Konto hängt und nicht an der einzelnen Datei. Dasselbe Windows-Konto verwenden.";
        return $"Arbeitsentwurf nicht lesbar. Die Datei {Path.GetFileName(draftPath)} bleibt unverändert liegen; sie wird weder gelöscht noch überschrieben. Du hast zwei Wege: „Erneut laden“ liest sie noch einmal, „Leer neu anfangen“ beginnt mit leerem Entwurf in einer zweiten Datei. Hilft beides nicht, dasselbe Windows-Konto verwenden.";
    }
    private void StartFreshDraft()
    {
        draftPath = freshDraftPath;
        damagedDraft = false;
        LoadDocument(new());
        bool saved = Flush();
        RefreshActions();
        if (saved) info.Text = $"Leer neu angefangen. Ab jetzt wird in {Path.GetFileName(freshDraftPath)} gesichert; {Path.GetFileName(baseDraftPath)} bleibt unverändert liegen.";
    }
    private void ReloadDraft()
    {
        LoadDraft();
        RefreshActions();
        if (damagedDraft) info.Text = DamagedDraftMessage() + " Erneutes Laden hat gerade nicht geholfen.";
        else info.Text = "Arbeitsentwurf erneut geladen. Du kannst weiterarbeiten.";
    }
    private void LoadDraft()
    {
        draftPath = File.Exists(freshDraftPath) ? freshDraftPath : baseDraftPath;
        try
        {
            damagedDraft = false;
            var doc = ProtectedFile.Read<PromptDocument>(draftPath);
            if (doc != null)
            {
                if (!doc.Valid) throw new InvalidDataException();
                LoadDocument(doc);
            }
        }
        catch
        {
            damagedDraft = true;
            info.Text = DamagedDraftMessage();
        }
    }
    private void LoadDocument(PromptDocument doc)
    {
        findings.Text = "Noch nicht geprüft.";
        loading = true;
        try
        {
            original.Text = doc.Original; goal.Text = doc.Goal; audience.Text = doc.Audience; format.Text = doc.Format; constraints.Text = doc.Constraints; draft.Text = doc.Draft; candidate.Text = doc.Candidate; title.Text = doc.Title; libraryId = doc.LibraryId; candidateSource = null;
            review = doc.Review; reviewSource = doc.ReviewSource; ShowReview(); reviewAnswer.Text = doc.ReviewAnswer; reviewSuggestion.Checked = doc.ReviewSuggestion;
            if (review != null && reviewSource == draft.Text && candidate.Text.Length > 0) candidateSource = reviewSource;
        }
        finally { loading = false; RefreshActions(); }
    }
    public void ImportText(string text)
    {
        if (damagedDraft)
        {
            info.Text = $"Text nicht übernommen: Der Arbeitsentwurf {Path.GetFileName(draftPath)} ist nicht lesbar und bleibt unverändert liegen. Dein Text steht weiter auf der Seite, von der du ihn schicken wolltest. Mit „Leer neu anfangen“ bekommst du hier einen leeren Entwurf und kannst ihn danach erneut übernehmen.";
            return;
        }
        if (!CanEdit() || string.IsNullOrWhiteSpace(text)) { info.Text = "Kein übernehmbarer Text vorhanden oder Vorgang läuft."; return; }
        if (text.Length > 24000) { info.Text = "Maximal 24.000 Zeichen. Bitte einen Ausschnitt übernehmen."; return; }
        if (!ConfirmReplace()) return;
        LoadDocument(new(Original: text)); Flush(); views.SelectedIndex = 0; workspace.AutoScrollPosition = Point.Empty; info.Text = "Text übernommen. So verwenden oder freiwillig ergänzen.";
    }
    private bool ConfirmReplace() => string.IsNullOrWhiteSpace(original.Text + goal.Text + audience.Text + format.Text + constraints.Text + draft.Text + candidate.Text + title.Text) || MessageBox.Show(this, "Aktuellen Arbeitsentwurf ersetzen? Ungespeicherte Änderungen sind danach weg. Bibliothekseinträge bleiben erhalten.", "Arbeitsentwurf ersetzen", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    private void NewPrompt() { if (CanEdit() && ConfirmReplace()) { LoadDocument(new()); Flush(); findings.Text = "Noch nicht geprüft."; } }
    private void Check()
    {
        string text = string.IsNullOrWhiteSpace(draft.Text) ? original.Text : draft.Text;
        findings.Text = "Formale Prüfung · Regeln " + PromptRules.Version + "\r\n" + string.Join("\r\n", PromptRules.Inspect(text).Select(f => $"{f.Level}: {f.Text}"));
    }
    private void RefreshLibrary()
    {
        libraryList.Items.Clear(); if (library != null) foreach (var entry in library.Search(search.Text)) libraryList.Items.Add(entry);
        bool empty = libraryList.Items.Count == 0;
        libraryHint.Visible = empty;
        libraryHint.Text = library == null ? "Die Bibliothek konnte nicht geladen werden. Deine vorhandenen Dateien bleiben erhalten." :
            string.IsNullOrWhiteSpace(search.Text) ? "Noch keine Prompts gespeichert. Erstelle deinen ersten Prompt und öffne dort „In der Bibliothek speichern“." :
            "Keine passenden Prompts gefunden. Ändere oder leere deine Suche.";
        createFirstPrompt.Visible = empty && library != null && string.IsNullOrWhiteSpace(search.Text);
    }
    private void SaveLibrary(bool asNew)
    {
        if (!CanEdit() || library == null) { info.Text = "Bibliothek derzeit nicht beschreibbar."; return; }
        try { libraryId = library.Save(asNew ? null : libraryId, title.Text, draft.Text, original.Text); Flush(); RefreshLibrary(); info.Text = "Prompt bewusst in der Bibliothek gespeichert. Frühere Versionen bleiben erhalten."; }
        catch (Exception ex) { info.Text = ex is InvalidDataException or InvalidOperationException ? ex.Message : "Bibliothek konnte nicht gespeichert werden. Entwurf bleibt erhalten."; }
    }
    private void LoadEntry()
    {
        if (!CanEdit() || libraryList.SelectedItem is not PromptEntry entry || versions.SelectedIndex < 0 || !ConfirmReplace()) return;
        var version = entry.Versions[versions.SelectedIndex];
        LoadDocument(new(Original: version.Original, Draft: version.Text, Title: entry.Title, LibraryId: entry.Id)); Flush(); Check(); views.SelectedIndex = 0;
        // Direkt auf den Anfang von „Mein Prompt“ springen; ScrollControlIntoView ließe ein hohes Feld wegen der Fokusregel stehen.
        PerformLayout(); workspace.PerformLayout();
        int draftTop = workspace.PointToClient(draft.Parent!.PointToScreen(draft.Location)).Y - workspace.AutoScrollPosition.Y;
        int gap = (int)Math.Round(BrandTheme.ElementGap * DeviceDpi / 96f);
        workspace.AutoScrollPosition = new Point(0, Math.Max(0, draftTop - gap));
        info.Text = $"Version {version.Number} geladen. Erneutes Speichern erzeugt eine zusätzliche Version.";
    }
    private void ChangeFavorite()
    {
        // Sprint E: derselbe Wächter wie DeleteEntry; LibraryAvailable prüft damagedDraft nicht, die Sprint-A-Zusage bleibt.
        if (!LibraryAvailable || library == null || libraryList.SelectedItem is not PromptEntry entry) return;
        try { library.Favorite(entry.Id); RefreshLibrary(); } catch { info.Text = "Favorit konnte nicht gespeichert werden."; }
    }
    private void DeleteEntry()
    {
        if (!LibraryAvailable || library == null || libraryList.SelectedItem is not PromptEntry entry) return; // library erneut geprüft, damit die Nullable-Analyse greift.
        if (MessageBox.Show(this, "Diesen Bibliothekseintrag mit allen Versionen endgültig löschen?", "Eintrag löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try { library.Delete(entry.Id); if (libraryId == entry.Id) { libraryId = null; Flush(); } RefreshLibrary(); info.Text = "Bibliothekseintrag gelöscht. Arbeitsentwurf bleibt erhalten."; } catch { info.Text = "Löschen fehlgeschlagen. Eintrag bleibt erhalten."; }
    }
    private async Task CopyAsync()
    {
        if (string.IsNullOrWhiteSpace(draft.Text)) return;
        using var lease = gate.TryEnter("Prompt kopieren"); if (lease == null) { info.Text = "Zuerst aktuellen Vorgang abschließen."; return; }
        try { info.Text = await UnicodeClipboard.CopyAsync(Handle, draft.Text, lease.Token) ? "Prompt kopiert · Strg+V zum Einfügen." : "Zwischenablage belegt. Prompt bleibt erhalten."; }
        catch (OperationCanceledException) { info.Text = "Kopieren abgebrochen. Prompt bleibt erhalten."; }
    }
    private async Task VerifyModelAsync() => await ModelJobAsync("KI-Datei prüfen", async (token, report) =>
    {
        modelInvalid = !await LocalPromptModel.VerifyAsync(LocalPromptModel.ModelPath, token, report);
        RefreshModelInfo();
        info.Text = VerificationMessage(modelInvalid, File.Exists(LocalPromptModel.RuntimePath));
    });
    // Sprint E (K3): Per Prüfwert geprüft wird nur die Modelldatei; die Programmdateien nur auf Vorhandensein.
    private static string VerificationMessage(bool modelInvalid, bool runtimePresent) =>
        modelInvalid ? "KI-Datei fehlt oder ist beschädigt. Bitte herunterladen." :
        runtimePresent ? "Modelldatei geprüft: Ihr Prüfwert stimmt. Die KI-Programmdateien sind vorhanden; ihren Prüfwert kontrolliert der Paketbau, nicht diese Prüfung. Mit „Prompt prüfen“ geht es weiter." :
        "KI-Programmdateien fehlen. Bitte das vollständige Sprachgarten-Paket verwenden.";
    private async Task DownloadAsync() => await ModelJobAsync("KI herunterladen", async (token, report) =>
    {
        await LocalPromptModel.DownloadAsync(new Progress<int>(), token, report);
        modelInvalid = false; RefreshModelInfo();
        info.Text = File.Exists(LocalPromptModel.RuntimePath) ? "Einrichtung abgeschlossen. Jetzt „Prompt prüfen“ wählen." : "Modell geladen. KI-Programmdateien fehlen noch; bitte das vollständige Paket verwenden.";
    });
    private async Task GenerateAsync()
    {
        if (!CanEdit()) return;
        string input = draft.Text;
        if (string.IsNullOrWhiteSpace(input) || input.Length > (OnlineReview ? 24000 : 3000)) { info.Text = "Zuerst „Mein Prompt“ ausfüllen. Lokal bis 3.000, Claude bis 24.000 Zeichen."; return; }
        await ModelJobAsync("Prompt prüfen", async (token, report) =>
        {
            PromptReviewResult response;
            if (OnlineReview)
            {
                var connection = ClaudeConnection.Load();
                using var http = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
                response = await new ClaudeReviewClient(http).ReviewAsync(input, connection, connection.Save, token, report);
            }
            else response = await new LocalPromptModel().ReviewAsync(input, token, progress: report);
            SetReview(response.Review, input);
            info.Text = review!.Questions.Length > 0 ? "Rückfrage bereit. Beantworte sie, wenn sie hilfreich ist; dann Ergänzungen übernehmen. Dein Prompt bleibt bis dahin unverändert." :
                review.Suggestions.Length > 0 ? "Freiwillige Ergänzung bereit. Bei Gefallen auswählen, Vorschau ansehen und übernehmen." :
                "Prüfbefunde bereit. Die KI hat keine konkrete Rückfrage oder Ergänzung formuliert. Lies die Befunde; das ist keine Garantie für einen perfekten Prompt.";
            Flush();
        });
    }
    private void SetReview(PromptReview value, string source)
    {
        if (!value.Valid) throw new InvalidDataException("Ungültige KI-Prüfung.");
        loading = true;
        try { review = value; reviewSource = source; candidateSource = null; candidate.Clear(); reviewAnswer.Clear(); reviewSuggestion.Checked = false; ShowReview(); }
        finally { loading = false; RefreshActions(); }
    }
    private void ShowReview()
    {
        reviewOptions.Items.Clear(); reviewOptions.SelectedIndex = -1;
        reviewAnswer.Visible = reviewQuestion.Visible = review?.Questions.Length > 0;
        reviewSuggestion.Visible = review?.Suggestions.Length > 0;
        reviewOptions.Visible = review?.Questions.FirstOrDefault()?.Options.Length > 0;
        reviewInfo.Text = review == null ? "Die KI sucht einen wichtigen offenen Punkt. Du entscheidest, was du ergänzen möchtest." : "KI-Prüfbefunde (bitte mit deinem Text vergleichen):\r\n" + review.Summary;
        if (review?.Questions.FirstOrDefault() is PromptQuestion q)
        { reviewQuestion.Text = q.Question + "\r\n" + q.Reason; reviewOptions.Items.AddRange(q.Options); }
        if (review?.Suggestions.FirstOrDefault() is PromptSuggestion s) reviewSuggestion.Text = s.Text + "\r\nWarum: " + s.Reason;
        candidateInfo.Text = review == null ? "Hier wird nichts automatisch umgeschrieben." :
            review.Questions.Length == 0 && review.Suggestions.Length == 0 ? "Kein konkreter Ergänzungsvorschlag in diesem Durchlauf. Das bedeutet nicht, dass dein Prompt perfekt ist." :
            "Nichts vorausgewählt. Deine Antwort oder ausgewählte Ergänzung erscheint zusammen mit dem unveränderten bisherigen Text als Vorschau. Erst mit Übernehmen wird daraus dein Prompt.";
        ArrangeWorkspace();
    }
    private void UpdateReviewCandidate()
    {
        if (loading || review == null || reviewSource != draft.Text) return;
        try
        {
            string text = review.Apply(draft.Text, reviewAnswer.Text, reviewSuggestion.Checked);
            candidateSource = text != draft.Text ? draft.Text : null; candidate.Text = text != draft.Text ? text : ""; ScheduleSave();
        }
        catch (InvalidDataException ex) { candidateSource = null; info.Text = ex.Message; RefreshActions(); }
    }
    private async Task ModelJobAsync(string label, Func<CancellationToken, IProgress<PromptProgress>, Task> action)
    {
        if (!CanEdit()) return;
        using var lease = gate.TryEnter(label); if (lease == null) { info.Text = "Ein anderer Vorgang läuft. Bitte zuerst abschließen."; return; }
        working = true; cancelling = false; timedFormulation = label is "Vorschlag erstellen" or "Prompt prüfen"; int run = ++workGeneration;
        currentProgress = new(label); progress.Value = 0; progress.Style = ProgressBarStyle.Marquee; progress.Visible = true;
        workClock.Restart(); workTimer.Start(); info.Text = "Die KI arbeitet. Alle Texte bleiben sichtbar; Bearbeiten ist nach Abschluss oder Abbruch wieder möglich.";
        RefreshActions(); UpdateWorkStatus(); string completion = "Abgeschlossen";
        var report = new Progress<PromptProgress>(p => { if (working && workGeneration == run && !IsDisposed) { currentProgress = p; UpdateWorkStatus(); } });
        try { await action(lease.Token, report); }
        catch (OperationCanceledException) { completion = "Abgebrochen"; info.Text = "Abgebrochen. Dein Entwurf bleibt erhalten." + (OnlineReview ? " Die Online-Anfrage kann trotzdem berechnet werden; Budget vorsorglich reserviert." : " Du kannst es erneut versuchen."); }
        catch (Exception ex) { completion = "Nicht abgeschlossen"; info.Text = ex is InvalidDataException or TimeoutException ? ex.Message : "KI-Prüfung fehlgeschlagen. Verbindung oder lokale Einrichtung prüfen. Entwurf bleibt unverändert."; }
        finally
        {
            working = false; workTimer.Stop(); workClock.Stop();
            progress.Style = ProgressBarStyle.Blocks; progress.Value = 0; progress.Visible = false;
            workStatus.Text = $"{completion} · {workClock.Elapsed.TotalSeconds:F0} Sekunden. {info.Text}";
            RefreshActions();
        }
    }
    private void UpdateWorkStatus()
    {
        if (!working) return;
        progress.Style = currentProgress.Percent.HasValue && !cancelling ? ProgressBarStyle.Blocks : ProgressBarStyle.Marquee;
        if (currentProgress.Percent is int percent) progress.Value = Math.Clamp(percent, 0, 100);
        workStatus.Text = cancelling ? $"Wird abgebrochen · {workClock.Elapsed.TotalSeconds:F0} Sekunden" :
            $"{currentProgress.Stage}{(currentProgress.Percent is int p ? $" · {p} % dieses Schritts" : " …")} · {workClock.Elapsed.TotalSeconds:F0} Sekunden" +
            (currentProgress.Percent == null ? timedFormulation ? "\nKeine verlässliche Prozentangabe. Die Prüfung kann abgebrochen werden; Zeitlimit 3 Minuten." : "\nDie Dauer hängt von Dateizugriff oder Verbindung ab. Abbrechen ist möglich." : "");
    }
    private void RefreshModelInfo()
    {
        if (OnlineReview) { modelInfo.Text = "Claude Sonnet 5 · mittel · gewählter Prompt wird online verarbeitet. Zugang und App-Budget unter KI-Verbindung."; return; }
        modelInfo.Text = !File.Exists(LocalPromptModel.RuntimePath) ? "KI-Programmdateien fehlen. Bitte das vollständige Paket verwenden." :
            File.Exists(LocalPromptModel.ModelPath) && !modelInvalid ? "KI ist enthalten. Kein Download nötig; die Datei wird beim Erstellen automatisch geprüft. Alles bleibt auf diesem Rechner." : "Einmalige Einrichtung nötig: 2,50 GB herunterladen, danach offline nutzbar.";
    }
    private void RefreshActions()
    {
        if (IsDisposed) return;
        bool available = !working && !gate.Busy && !damagedDraft;
        // Sprint C (S12): Während eines Auftrags bleiben Texte lesbar und markierbar, aber schreibgeschützt.
        // Nur ein beschädigter Entwurf sperrt die Felder ganz, wie in Sprint A.
        foreach (Control c in editing)
        {
            if (c is TextBoxBase box) { box.Enabled = !damagedDraft; box.ReadOnly = !available; }
            else c.Enabled = available;
        }
        foreach (var button in Buttons(views)) button.Enabled = available;
        favorite.Enabled = LibraryAvailable;
        deleteEntry.Enabled = LibraryAvailable;
        recoveryRow.Visible = damagedDraft;
        reloadDraft.Visible = damagedDraft;
        reloadDraft.Enabled = !working && !gate.Busy;
        freshDraft.Visible = damagedDraft && draftPath != freshDraftPath;
        freshDraft.Enabled = !working && !gate.Busy;
        bool hasModel = File.Exists(LocalPromptModel.ModelPath) && !modelInvalid;
        generate.Enabled = available && (OnlineReview || (hasModel && File.Exists(LocalPromptModel.RuntimePath))) && !string.IsNullOrWhiteSpace(draft.Text) && draft.Text.Length <= (OnlineReview ? 24000 : 3000);
        generate.Text = reviewSource == draft.Text ? "Erneut prüfen" : "Prompt prüfen";
        adopt.Text = "Ergänzungen in meinen Prompt übernehmen";
        reviewAnswer.Enabled = reviewOptions.Enabled = reviewSuggestion.Enabled = available && reviewSource == draft.Text;
        cancelAi.Enabled = working && !cancelling;
        bool showDownload = !OnlineReview && !hasModel;
        download.Visible = showDownload; download.Enabled = available && File.Exists(LocalPromptModel.RuntimePath); verify.Enabled = available && !OnlineReview;
        bool adoptable = available && candidate.Text.Length > 0 && candidateSource != null && candidateSource == draft.Text;
        adopt.Enabled = adoptable;
        // Heute stummer Ausstieg bei leerem Entwurf; jetzt sichtbar gesperrt.
        copyPrompt.Enabled = available && !string.IsNullOrWhiteSpace(draft.Text);
        adoptIdea.Enabled = available && !string.IsNullOrWhiteSpace(original.Text);
        connectionBlock.Detail = reviewProvider.SelectedItem?.ToString() ?? "";
        // Eine Quelle für den Hinweis und für jede Hervorhebung der Werkstatt.
        var step = NextStep(showDownload, adoptable);
        nextStep.Text = step.Hint;
        foreach (var button in WorkshopButtons()) button.Primary = step.Primary.Contains(button);
    }
    private sealed record WorkshopStep(string Hint, GardenButton[] Primary);
    /// <summary>Der nächste sinnvolle Schritt: Text für „nextStep“ und die eine hervorgehobene Schaltfläche je Abschnitt.</summary>
    private WorkshopStep NextStep(bool showDownload, bool adoptable)
    {
        string hint = damagedDraft ? $"Arbeitsentwurf {Path.GetFileName(draftPath)} nicht lesbar: Eingabefelder und „Version laden“ bleiben gesperrt, weil beide in den Entwurf schreiben würden. „Favorit umschalten“ und „Eintrag löschen …“ kannst du weiter benutzen. Mit „Leer neu anfangen“ bekommst du einen leeren, beschreibbaren Entwurf zurück." :
            string.IsNullOrWhiteSpace(draft.Text) ? "Fülle „Mein Prompt“ aus oder übernimm deine Idee. Danach kannst du deinen Prompt prüfen lassen." :
            !OnlineReview && draft.Text.Length > 3000 ? "Dein Prompt ist für die kleine KI zu lang. Bitte auf höchstens 3.000 Zeichen kürzen oder Claude auswählen." :
            review != null && reviewSource != draft.Text ? "Dein Prompt wurde geändert. Für passende Rückfragen erneut prüfen. Frühere Antworten bleiben sichtbar." :
            (OnlineReview ? "Claude prüft online · Console-Guthaben wird genutzt. " : "Lokale KI · experimentelle Qualität. ") + "Prompt prüfen → Frage beantworten oder Ergänzung auswählen → bewusst übernehmen. Speichern geht auch ohne KI.";
        // Entwurf beschädigt: Einziger Weg nach vorn ist der Ausweg; die Abschnitte sind gesperrt und bleiben ohne Hervorhebung.
        if (damagedDraft) return new(hint, [reloadDraft]);
        GardenButton idea = string.IsNullOrWhiteSpace(original.Text) ? importDictation : adoptIdea;
        GardenButton ai = working ? cancelAi : showDownload ? download : adoptable ? adopt : generate;
        // Reihenfolge der Abschnitte: Deine Idee, Ergänzungen, Mein Prompt, KI, Fertige Prompts.
        return new(hint, [idea, build, copyPrompt, ai, loadVersion]);
    }
    private GardenButton[] WorkshopButtons() => [importDictation, newPrompt, adoptIdea, build, copyPrompt, recheck, handoff, saveVersion, saveNew, connection, generate, download, cancelAi, adopt, verify, loadVersion, favorite, deleteEntry, reloadDraft, freshDraft];
    private static IEnumerable<Button> Buttons(Control parent)
    {
        foreach (Control child in parent.Controls) { if (child is Button button) yield return button; foreach (var nested in Buttons(child)) yield return nested; }
    }
    protected override void Dispose(bool disposing) { if (disposing) { gate.Changed -= RefreshActions; saveTimer.Dispose(); workTimer.Dispose(); } base.Dispose(disposing); }
}
