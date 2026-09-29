using System.Diagnostics;

namespace Klartext;

public sealed class MainForm : Form
{
    private readonly SessionState state = new();
    private readonly AudioRecorder audio = new();
    private readonly Transcriber transcriber = new();
    private readonly HotkeyWindow hotkey = new();
    private readonly HotkeyWindow readingHotkey = new();
    private readonly ReadingPage reading;
    private readonly TabPage readingTab;
    private readonly NotifyIcon tray = new() { Icon = BrandTheme.Icon, Text = BrandTheme.AppName, Visible = true };
    private readonly StatusOverlay overlay = new();
    private readonly Label status = new() { AutoSize = true, Text = "Modell wird geprüft …", AccessibleName = "Status" };
    private readonly Label details = new() { AutoSize = true, MaximumSize = new Size(670, 0) };
    private readonly Label stateSummary = new() { AutoSize = true, Text = "● Bereit" };
    private readonly Label modelLabel = new() { AutoSize = true, Text = "Whisper Small wird geprüft …" };
    private readonly Label duration = new() { AutoSize = true, Text = "Maximal 5 Minuten pro Aufnahme · Verarbeitung auf der CPU" };
    private readonly ComboBox microphones = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420, AccessibleName = "Mikrofon" };
    // Sprint C: kein eigener Scrollbalken; die Karte wird so hoch wie der Inhalt, die Diktatseite scrollt.
    private readonly GardenTextBox transcript = new() { ReadOnly = true, AutoGrow = false, Dock = DockStyle.Fill, PlaceholderText = "Hier entsteht dein Text.\r\nDu kannst ihn direkt korrigieren.", AccessibleName = "Letztes Transkript" };
    private readonly GardenButton toggle = new() { Text = "Aufnahme starten", Primary = true };
    private readonly GardenButton cancel = new() { Text = "Abbrechen" };
    private readonly GardenButton retry = new() { Text = "Erneut transkribieren" };
    private readonly GardenButton discard = new() { Text = "Aufnahme verwerfen" };
    private readonly GardenButton copy = new() { Text = "Erneut kopieren" };
    private readonly GardenButton applyCorrection = new() { Text = "Korrektur übernehmen" };
    private readonly Label editHint = new() { Text = "Falsche Stelle markieren und richtig schreiben.\n„Korrektur übernehmen“ (Strg+Enter) fragt, ob Sprachgarten es sich merken soll.", AutoSize = true, MaximumSize = new Size(780, 0) };
    private readonly ListView ruleList = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill, AccessibleName = "Gespeicherte Ersetzungen" };
    private readonly GardenButton removeRule = new() { Text = "Markierte Regel löschen" };
    private readonly Label rulesHint = new() { AutoSize = true, MaximumSize = new Size(630, 0) };
    private readonly GardenButton reloadRecovery = new() { Text = "Ergebnis erneut laden", Visible = false };
    private readonly GardenButton modelDownload = new() { Text = "Modell herunterladen (488 MB)" };
    private readonly ProgressBar progress = new() { Width = 420, Height = 15, AccessibleName = "Download-Fortschritt Sprachmodell" };
    private readonly ProgressBar level = new() { Width = 420, Height = 10, AccessibleName = "Mikrofonpegel", AccessibleDescription = "Zeigt, ob das Mikrofon etwas hört" };
    private readonly GardenButton settingsButton = new() { Text = "Hotkey ändern …" };
    private readonly GardenButton refresh = new() { Text = "Mikrofone aktualisieren" };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };
    private readonly Stopwatch recordingTime = new();
    private CancellationTokenSource? operation;
    private UserSettings settings;
    private string? pending;
    private bool modelReady, exiting, copyBusy, loadingDevices, recoveryUnavailable;
    private bool modelBlocked, exitRequested;
    // Sprint E (c): bis zur ersten Modellprüfung zeigt die Karte „wird geprüft“ statt „fehlt“.
    private bool modelChecked;
    private bool settingTranscript, editingBusy, editsDirty, editSaveFailed;
    private string editBaseline = "";
    private string? recognitionSource;
    private ReplacementStore? replacements;
    private readonly bool startHidden;
    private DateTime noticeUntil;
    private string lastStatus = "Modell wird geprüft …";
    private Action? resizeDictation;
    private readonly ActivityGate activities;
    private readonly AudioToolsPage imports;
    private readonly AudioToolsPage laboratory;
    private readonly PromptPage workshop;
    private const string ImportsArea = "Audiodateien";
    private const string LaboratoryArea = "Sprachlabor";
    private const string WorkshopArea = "Promptwerkstatt";
    private readonly GardenTabs tabs;
    private readonly TabPage importsTab;
    private readonly TabPage laboratoryTab;
    private readonly TabPage workshopTab;

    public MainForm(bool startHidden)
    {
        this.startHidden = startHidden;
        activities = new(() => !exiting && !state.Busy && !copyBusy && !editingBusy);
        settings = UserSettings.Load(AppPaths.Settings);
        try { replacements = new ReplacementStore(Path.Combine(AppPaths.Root, "replacements.bin")); } catch { }
        SuspendLayout(); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Sprachgarten · Lokal diktieren · " + Application.ProductVersion; Name = "SprachgartenMain"; Icon = BrandTheme.Icon;
        ClientSize = new Size(900, 740); MinimumSize = new Size(800, 660);
        StartPosition = FormStartPosition.CenterScreen; Font = BrandTheme.Body;
        BackColor = BrandTheme.Cream;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 0, 24, 12), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(i == 2 ? SizeType.Percent : SizeType.AutoSize, i == 2 ? 100 : 0));
        var header = new GardenHeader { Dock = DockStyle.Fill, Height = 112, Margin = Padding.Empty };
        header.Controls.Add(new GardenImage(BrandTheme.Microphone) { Bounds = new Rectangle(6, 12, 45, 82), AccessibleName = "Blumenmikrofon", TabStop = false });
        header.Controls.Add(new GardenImage(BrandTheme.Wordmark) { Bounds = new Rectangle(65, 14, 315, 50), AccessibleName = "Sprachgarten", TabStop = false });
        header.Controls.Add(new Label { Text = "Lokal diktieren", AutoSize = true, Font = BrandTheme.Body, Location = new Point(65, 70) });
        stateSummary.Font = BrandTheme.Body; stateSummary.Location = new Point(575, 59); stateSummary.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        details.Font = BrandTheme.Small; details.Location = new Point(575, 87); details.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Size = new Size(852, 112); header.Controls.Add(stateSummary); header.Controls.Add(details); layout.Controls.Add(header, 0, 0);
        status.Font = BrandTheme.Small; status.MaximumSize = new Size(810, 0); status.Margin = new Padding(0, 0, 0, 8); layout.Controls.Add(status, 0, 1);
        tabs = new GardenTabs { Dock = DockStyle.Fill, Margin = Padding.Empty, AccessibleName = "Bereichsinhalt" };
        var dictationTab = new TabPage("Diktat") { Padding = new Padding(BrandTheme.PagePadding), UseVisualStyleBackColor = false };
        var settingsTab = new TabPage("Einstellungen") { Padding = new Padding(BrandTheme.PagePadding), UseVisualStyleBackColor = false };
        var correctionsTab = new TabPage("Ersetzungen") { Padding = new Padding(BrandTheme.PagePadding), UseVisualStyleBackColor = false };
        tabs.TabPages.Add(dictationTab); tabs.TabPages.Add(settingsTab); tabs.TabPages.Add(correctionsTab);
        imports = new AudioToolsPage(false, activities, () => settings.DeviceId);
        laboratory = new AudioToolsPage(true, activities, () => settings.DeviceId);
        workshop = new PromptPage(activities, () => transcript.Text);
        workshopTab = new TabPage(WorkshopArea) { Padding = new Padding(BrandTheme.PagePadding) };
        workshopTab.Controls.Add(workshop);
        // Named pages instead of the anonymous loop: exit blocking has to reach them (S13).
        importsTab = new TabPage(ImportsArea) { Padding = new Padding(BrandTheme.PagePadding) };
        importsTab.Controls.Add(imports);
        tabs.TabPages.Add(importsTab);
        laboratoryTab = new TabPage(LaboratoryArea) { Padding = new Padding(BrandTheme.PagePadding) };
        laboratoryTab.Controls.Add(laboratory);
        tabs.TabPages.Add(laboratoryTab);
        tabs.TabPages.Add(workshopTab);
        reading = new ReadingPage(activities);
        readingTab = new TabPage("Vorlesen") { Padding = new Padding(BrandTheme.PagePadding) };
        readingTab.Controls.Add(reading); tabs.TabPages.Add(readingTab);
        reading.ShowRequested += () => { tabs.SelectedTab = readingTab; Show(); WindowState = FormWindowState.Normal; Activate(); };
        imports.ToWorkshop += text => { tabs.SelectedTab = workshopTab; workshop.ImportText(text); };
        laboratory.ToWorkshop += text => { tabs.SelectedTab = workshopTab; workshop.ImportText(text); };
        activities.Changed += () => { if (!IsDisposed) UpdateUi(); };
        var dictationViewport = new GardenScrollPanel { Dock = DockStyle.Fill };
        var dictation = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 7 };
        dictation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 7; i++) dictation.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        toggle.Font = BrandTheme.Large; toggle.Padding = new Padding(23, 7, 23, 7); toggle.MinimumSize = new Size(310, 60);
        duration.Font = BrandTheme.Small; editHint.Font = BrandTheme.Small; transcript.Font = BrandTheme.Large;
        var transcriptFrame = new GardenCard { Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 7), MinimumSize = new Size(0, 185) };
        transcriptFrame.Controls.Add(transcript);
        dictation.Controls.Add(Row(toggle, cancel), 0, 0); dictation.Controls.Add(duration, 0, 1); dictation.Controls.Add(level, 0, 2);
        dictation.Controls.Add(transcriptFrame, 0, 3); dictation.Controls.Add(Row(applyCorrection, copy), 0, 4); dictation.Controls.Add(editHint, 0, 5);
        dictation.Controls.Add(Row(retry, discard, reloadRecovery), 0, 6);
        dictationViewport.Controls.Add(dictation); dictationTab.Controls.Add(dictationViewport);
        bool sizingDictation = false, resizeAgain = false;
        resizeDictation = () =>
        {
            if (sizingDictation) { resizeAgain = true; return; }
            sizingDictation = true;
            try
            {
                // Sprint C: Ein Scrollbalken der Seite verengt das Transkript, das dann höher wird; bis zu vier Runden, bis es steht.
                int rounds = 0;
                do
                {
                    resizeAgain = false;
                    int width = Math.Max(1, dictationViewport.ClientSize.Width);
                    int frameHeight = Math.Max(transcriptFrame.MinimumSize.Height, transcript.ContentHeight + transcriptFrame.Padding.Vertical);
                    int required = frameHeight + transcriptFrame.Margin.Vertical;
                    foreach (Control child in dictation.Controls)
                    {
                        if (child == transcriptFrame || (child == level && state.Current != DictationState.Recording)) continue;
                        required += child.GetPreferredSize(new Size(Math.Max(1, width - child.Margin.Horizontal), 0)).Height + child.Margin.Vertical;
                    }
                    // Base this on intrinsic rows, not the current percentage-row height (avoids resize feedback).
                    dictation.Height = Math.Max(dictationViewport.ClientSize.Height, required);
                }
                while (resizeAgain && ++rounds < 4);
            }
            finally { sizingDictation = false; }
        };
        dictationViewport.ClientSizeChanged += (_, _) => resizeDictation();
        transcript.ContentHeightChanged += (_, _) => resizeDictation?.Invoke();
        var correctionsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        correctionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        correctionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); correctionsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); correctionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        ruleList.Columns.Add("Bisher erkannt", 340); ruleList.Columns.Add("Immer ersetzen durch", 380);
        rulesHint.Font = BrandTheme.Small;
        correctionsLayout.Controls.Add(rulesHint); correctionsLayout.Controls.Add(ruleList); correctionsLayout.Controls.Add(Row(removeRule)); correctionsTab.Controls.Add(correctionsLayout);
        correctionsLayout.ClientSizeChanged += (_, _) => rulesHint.MaximumSize = new Size(Math.Max(200, correctionsLayout.ClientSize.Width - rulesHint.Margin.Horizontal), 0);
        RefreshRules();
        var preferences = BuildSettingsPage();
        settingsTab.Controls.Add(preferences); layout.Controls.Add(BuildTabArea(), 0, 2);
        var hide = new GardenButton { Text = "Im Hintergrund weiterlaufen", Font = BrandTheme.Small };
        var quit = new GardenButton { Text = "Beenden", Font = BrandTheme.Small };
        var footer = new GardenFooter { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 10, 65, 0), Margin = new Padding(0, 12, 0, 0), WrapContents = true };
        footer.Controls.AddRange([hide, quit]); layout.Controls.Add(footer, 0, 3);
        Controls.Add(layout);
        BrandTheme.Apply(this);
        ResumeLayout(true);
        toggle.Click += async (_, _) => await ToggleAsync();
        cancel.Click += async (_, _) => await CancelAsync();
        retry.Click += async (_, _) => await ProcessPendingAsync();
        discard.Click += (_, _) => Discard();
        copy.Click += async (_, _) => await CopyAgainAsync();
        applyCorrection.Click += async (_, _) => await CopyAgainAsync();
        transcript.TextChanged += (_, _) => TranscriptEdited();
        transcript.KeyDown += async (_, e) => { if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await CopyAgainAsync(); } };
        removeRule.Click += (_, _) => RemoveRule();
        ruleList.SelectedIndexChanged += (_, _) => UpdateUi();
        reloadRecovery.Click += (_, _) => ReloadRecovery();
        modelDownload.Click += async (_, _) => await DownloadModelAsync();
        refresh.Click += (_, _) => LoadDevices();
        settingsButton.Click += (_, _) => ChangeHotkey();
        microphones.SelectedIndexChanged += (_, _) => SaveMicrophone();
        hide.Click += (_, _) => Hide(); quit.Click += async (_, _) => await ExitAsync();
        hotkey.Pressed += async () => await ToggleAsync();
        readingHotkey.Pressed += async () => await reading.ReadSelectionAsync();
        // Sprint E (K6): NAudio stellt den Abbruch auf dem UI-Thread zu (RecordingStopped über den gemerkten SynchronizationContext);
        // die Prüfung auf Handle und Entsorgung genügt deshalb, ein try/catch um BeginInvoke wie in AudioToolsPage ist hier nicht nötig.
        audio.UnexpectedStop += ex => { if (IsHandleCreated && !IsDisposed) BeginInvoke(async () => await DeviceStoppedAsync(ex)); };
        var menu = new ContextMenuStrip();
        menu.Font = BrandTheme.Small;
        menu.Items.Add("Sprachgarten öffnen", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        menu.Items.Add("Aufnahme starten / stoppen", null, async (_, _) => await ToggleAsync());
        menu.Items.Add("Vorlesen öffnen", null, (_, _) => { tabs.SelectedTab = readingTab; Show(); WindowState = FormWindowState.Normal; Activate(); });
        menu.Items.Add("Abbrechen", null, async (_, _) => await CancelAsync());
        menu.Items.Add("Beenden", null, async (_, _) => await ExitAsync()); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        // T1: minimising stays in the taskbar; the X exits through OnFormClosing, not by hiding.
        Shown += async (_, _) => await InitializeAsync();
        timer.Tick += async (_, _) =>
        {
            if (state.Current == DictationState.Recording)
            {
                duration.Text = $"Aufnahme {recordingTime.Elapsed:mm\\:ss} / 05:00 · Mikrofon aktiv";
                overlay.SetStatus($"● Aufnahme  {recordingTime.Elapsed:mm\\:ss}  ·  {settings.EffectiveHotkey}", true);
                level.Value = (int)Math.Clamp(audio.Peak * 100, 0, 100);
                if (recordingTime.Elapsed.TotalMinutes >= 5) await ToggleAsync();
            }
            else if (laboratory.IsRecording) overlay.SetStatus($"● Sprachlabor · Aufnahme {laboratory.RecordingDuration:mm\\:ss} / 01:00", true);
            else if (DateTime.UtcNow > noticeUntil && !state.Busy) overlay.Hide();
        };
        timer.Start();
        UpdateUi();
    }
    private static FlowLayoutPanel Row(params Control[] children)
    {
        var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 4, 0, 4) };
        row.Controls.AddRange(children); return row;
    }
    // Sprint C (T2a): Zeile 2 des Layouts trägt die gezeichnete Leiste „Bereiche“ und darunter die Seiten ohne native Kopfzeile.
    private Panel BuildTabArea()
    {
        var area = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var strip = new GardenTabStrip(tabs) { AccessibleName = "Bereiche" };
        area.Controls.Add(tabs); area.Controls.Add(strip);
        return area;
    }
    // Sprint C (T2b, T3): eine einzige Scrollebene mit zwei Karten; der Hinweis bricht in der Kartenbreite um statt fest.
    private GardenScrollFlow BuildSettingsPage()
    {
        var preferences = new GardenScrollFlow { Dock = DockStyle.Fill };
        var microphoneCard = new GardenCardFlow { AccessibleName = "Mikrofon" };
        microphoneCard.Controls.Add(new Label { Text = "Mikrofon", AutoSize = true, Font = BrandTheme.Large });
        microphoneCard.Controls.Add(microphones); microphoneCard.Controls.Add(refresh); microphoneCard.Controls.Add(settingsButton);
        var recognitionCard = new GardenCardFlow { AccessibleName = "Spracherkennung" };
        recognitionCard.Controls.Add(new Label { Text = "Spracherkennung", AutoSize = true, Font = BrandTheme.Large });
        recognitionCard.Controls.Add(modelLabel); recognitionCard.Controls.Add(modelDownload); recognitionCard.Controls.Add(progress);
        recognitionCard.Controls.Add(new Label { Text = "Deutsch · keine Übersetzung · maximal 5 Minuten pro Diktat. Keine Audiohistorie. Fehleraufnahmen bleiben bis Wiederholung oder Verwerfen erhalten. Noch nicht kopierter Text wird für einen Neustart mit Windows-Benutzerschutz gesichert.", Font = BrandTheme.Small, AutoSize = true, Margin = new Padding(0, BrandTheme.ElementGap, 0, 0) });
        preferences.Controls.Add(microphoneCard); preferences.Controls.Add(recognitionCard);
        preferences.ClientSizeChanged += (_, _) => FitCards(preferences);
        BrandTheme.RelayWheel(microphones);
        return preferences;
    }
    // Karten füllen die Breite der Scrollfläche; Hinweise und Zeilen brechen innerhalb der Karte um.
    internal static void FitCards(ScrollableControl surface)
    {
        // Sprint E: rechter Rand wie in der Werkstatt, Scrollbalkenbreite plus zweimal CardGap, dpi-skaliert.
        int gap = (int)(BrandTheme.CardGap * surface.DeviceDpi / 96f);
        int width = Math.Max(200, surface.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - gap * 2);
        foreach (Control control in surface.Controls)
        {
            if (control is not GardenCardFlow card) continue;
            int cardWidth = width - card.Margin.Horizontal;
            card.MinimumSize = new Size(cardWidth, 0); card.MaximumSize = new Size(cardWidth, 0);
            int inner = cardWidth - card.Padding.Horizontal;
            foreach (Control child in card.Controls)
            {
                if (child is Label or FlowLayoutPanel) child.MaximumSize = new Size(Math.Max(100, inner - child.Margin.Horizontal), 0);
                else if (child is GardenTextBox) child.Width = Math.Max(100, inner - child.Margin.Horizontal);
            }
        }
    }
    private async Task InitializeAsync()
    {
        if (startHidden) Hide();
        LoadDevices();
        foreach (var file in Directory.GetFiles(AppPaths.Audio).Where(p => p.EndsWith(".pcm.wav") || p.EndsWith(".result")))
        { try { AppPaths.DeleteOwned(file); } catch (IOException) { } }
        pending = Directory.GetFiles(AppPaths.Audio, "recording-*.wav").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        state.HasPendingAudio = pending != null;
        LoadRecovery();
        bool registered = hotkey.TryRegister(settings.EffectiveHotkey);
        state.Move(DictationState.Setup); UpdateUi("Modell wird geprüft …");
        try { await RefreshModelAsync(CancellationToken.None); }
        catch { modelReady = false; modelChecked = true; }
        state.Move(pending == null ? DictationState.Ready : DictationState.Error);
        UpdateUi(pending != null ? "Unterbrochene Aufnahme gefunden – wiederholen oder verwerfen." : modelReady ? "Bereit" : ModelNotice());
        if (!registered) UpdateUi("Hotkey ist belegt. Bitte eine andere Kombination einstellen.");
        if (state.HasUncopiedText) UpdateUi(recoveryUnavailable ? "Gesichertes Ergebnis kann nicht entschlüsselt werden. Datei bleibt erhalten; erneut laden oder bewusst verwerfen." : RestoredTranscriptNotice);
        reading.SetHotkeyAvailable(readingHotkey.TryRegister(new HotkeySpec(3, 86)));
        if (File.Exists(SpeechRuntime.VoicePath)) _ = reading.PrepareAsync();
    }
    // S3: three outcomes instead of two. A locked or denied model file is not a missing one.
    private enum ModelAvailability { Ready, Missing, Blocked }
    private const string ModelBlockedText = "Das Sprachmodell ist momentan nicht lesbar. Vermutlich hält gerade ein anderes Programm die Datei. Bitte in einem Moment erneut versuchen; ein neuer Download ist nicht nötig.";
    private const string ModelMissingText = "Einmalig das Sprachmodell herunterladen.";
    // Sprint E (B1): In diesen Zuständen heißt der Knopf „Text kopieren und freigeben“ (UpdateUi). Benannt, weil zwei der drei Stellen im Prüflauf nicht erreichbar sind.
    private const string RestoredResultNotice = "Ergebnis wiederhergestellt. „Text kopieren und freigeben“ wählen oder verwerfen.";
    private const string RestoredTranscriptNotice = "Noch nicht kopiertes Transkript wiederhergestellt. „Text kopieren und freigeben“ wählen oder verwerfen.";
    private const string ClipboardBusyNotice = "Text erkannt, Zwischenablage belegt. Bitte „Text kopieren und freigeben“ verwenden.";
    // Sprint F (F7): „Ergebnis erneut laden“ mit einer leeren Sicherung.
    private const string RestoredEmptyNotice = "Gesichertes Ergebnis erneut geladen: Es ist leer, es gibt nichts zu kopieren.";
    // Sprint F (F5): Nur für UnauthorizedAccessException; eigene deutsche Meldungen im Diktatweg bleiben unverändert.
    private const string FileLockedNotice = "Eine Datei im lokalen Datenordner war gerade gesperrt. Die Aufnahme bleibt erhalten; ein schon erkannter Text steht nur im Fenster – bitte kopieren.";
    private static string FailureNotice(Exception ex) => ex is UnauthorizedAccessException ? FileLockedNotice : ex.Message;
    private string ModelNotice()
    {
        if (modelBlocked) return ModelBlockedText;
        return ModelMissingText;
    }
    private static async Task<ModelAvailability> ProbeModelAsync(CancellationToken token)
    {
        string path = AppPaths.Model;
        if (!File.Exists(path)) return ModelAvailability.Missing;
        try
        {
            // Cheap open probe first: only a readable file is worth hashing 488 MB for.
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (FileNotFoundException) { return ModelAvailability.Missing; }
        catch (DirectoryNotFoundException) { return ModelAvailability.Missing; }
        catch (IOException) { return ModelAvailability.Blocked; }
        catch (UnauthorizedAccessException) { return ModelAvailability.Blocked; }
        try
        {
            if (await ModelStore.VerifyAsync(path, token)) return ModelAvailability.Ready;
            return ModelAvailability.Missing;
        }
        catch (OperationCanceledException) { throw; }
        catch (FileNotFoundException) { return ModelAvailability.Missing; }
        catch (DirectoryNotFoundException) { return ModelAvailability.Missing; }
        catch (IOException) { return ModelAvailability.Blocked; }
        catch (UnauthorizedAccessException) { return ModelAvailability.Blocked; }
    }
    private async Task<ModelAvailability> RefreshModelAsync(CancellationToken token)
    {
        var availability = await ProbeModelAsync(token);
        modelChecked = true;
        modelReady = availability == ModelAvailability.Ready;
        modelBlocked = availability == ModelAvailability.Blocked;
        return availability;
    }
    private async Task RecheckModelAsync()
    {
        if (state.Busy) return;
        state.Move(DictationState.Setup);
        UpdateUi("Sprachmodell wird erneut geprüft …");
        var availability = ModelAvailability.Blocked;
        try { availability = await RefreshModelAsync(CancellationToken.None); }
        catch { modelReady = false; modelChecked = true; }
        state.Move(pending == null ? DictationState.Ready : DictationState.Error);
        if (availability == ModelAvailability.Ready) Notice("Sprachmodell ist wieder lesbar. Für die Aufnahme bitte erneut „Aufnahme starten“ drücken.");
        else Notice(ModelNotice());
    }
    private void LoadRecovery()
    {
        try
        {
            var recovered = RecoveryStore.Load(); recoveryUnavailable = false;
            // A successfully decrypted empty edit contains nothing to protect. Keep the file,
            // but do not create an impossible copy-before-recording requirement on restart.
            state.HasUncopiedText = !string.IsNullOrWhiteSpace(recovered);
            if (recovered != null) SetTranscript(recovered);
        }
        catch { state.HasUncopiedText = RecoveryStore.Exists; recoveryUnavailable = state.HasUncopiedText; }
        reloadRecovery.Visible = recoveryUnavailable;
    }
    private void ReloadRecovery()
    {
        LoadRecovery(); UpdateUi(recoveryUnavailable ? "Gesichertes Ergebnis kann nicht entschlüsselt werden. Datei bleibt erhalten; dasselbe Windows-Konto verwenden." : state.HasUncopiedText ? RestoredResultNotice : RestoredEmptyNotice);
    }
    private void LoadDevices()
    {
        if (state.Busy) return;
        loadingDevices = true;
        try
        {
            microphones.DataSource = null;
            var devices = AudioRecorder.Devices().ToList();
            devices.Insert(0, new Microphone("", "Windows-Standardmikrofon"));
            microphones.DataSource = devices;
            microphones.SelectedIndex = Math.Max(0, devices.FindIndex(d => d.Id == settings.DeviceId));
        }
        catch { UpdateUi("Mikrofone konnten nicht gelesen werden. Windows-Mikrofonzugriff prüfen."); }
        finally { loadingDevices = false; }
    }
    private void SaveMicrophone()
    {
        if (loadingDevices || microphones.SelectedItem is not Microphone mic || state.Busy) return;
        settings = settings with { DeviceId = string.IsNullOrEmpty(mic.Id) ? null : mic.Id };
        try { settings.Save(AppPaths.Settings); }
        catch { UpdateUi("Einstellungen konnten nicht gespeichert werden."); }
    }
    private async Task ToggleAsync()
    {
        if (activities.Busy) { Notice("Bitte warten: " + activities.Label); return; }
        if (exiting) return;
        if (state.Current == DictationState.Recording) { await StopAndProcessAsync(); return; }
        if (!state.CanStart || copyBusy || editingBusy)
        {
            Notice(StartBlockReason() ?? "Bitte warten, die laufende Verarbeitung wird noch abgeschlossen.");
            return;
        }
        if (!modelReady && modelBlocked) { await RecheckModelAsync(); return; }
        if (!modelReady) { Notice("Bitte zuerst das Modell einrichten."); return; }
        pending = Path.Combine(AppPaths.Audio, $"recording-{Guid.NewGuid():N}.wav");
        try
        {
            audio.Start(settings.DeviceId, pending);
            state.Move(DictationState.Recording); state.HasPendingAudio = true; recordingTime.Restart();
            UpdateUi("Aufnahme läuft"); overlay.SetStatus($"● Aufnahme · {settings.EffectiveHotkey} zum Stoppen", true);
        }
        catch
        {
            // S5: reach the reported state first; a failing cleanup must not skip it or leave ToggleAsync.
            string? orphan = pending;
            pending = null;
            state.HasPendingAudio = false;
            state.Move(DictationState.Error);
            try { audio.Dispose(); }
            catch { }
            try { AppPaths.DeleteOwned(orphan); }
            catch { }
            Notice("Mikrofon nicht verfügbar. Gerät und Windows-Mikrofonberechtigung prüfen.");
        }
    }
    private async Task StopAndProcessAsync()
    {
        state.Move(DictationState.Stopping); UpdateUi("Aufnahme wird beendet …"); recordingTime.Stop();
        try { await audio.StopAsync(); await ProcessPendingAsync(); }
        catch (Exception ex) { state.Move(DictationState.Error); Notice(FailureNotice(ex)); }
    }
    private async Task DeviceStoppedAsync(Exception? error)
    {
        if (state.Current != DictationState.Recording) return;
        state.Move(DictationState.Stopping); recordingTime.Stop();
        try { await audio.StopAsync(); } catch { }
        state.Move(DictationState.Error);
        Notice("Mikrofonaufnahme unterbrochen. Vorhandene Aufnahme wiederholen oder verwerfen.");
    }
    private async Task ProcessPendingAsync()
    {
        if (pending == null || state.Current is not (DictationState.Error or DictationState.Stopping) || !modelReady) return;
        state.Move(DictationState.Transcribing); UpdateUi("Wird lokal transkribiert …"); overlay.SetStatus("… Wird lokal transkribiert", false);
        operation = new CancellationTokenSource();
        string pcm = pending + ".pcm.wav";
        try
        {
            var stats = await Task.Run(() => AudioRecorder.ConvertToPcm(pending, pcm, operation.Token), operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (!stats.HasSignal) { state.Move(DictationState.Error); Notice("Keine ausreichend deutliche Aufnahme. Zwischenablage unverändert. Wiederholen oder verwerfen."); return; }
            var availability = await RefreshModelAsync(operation.Token);
            if (availability == ModelAvailability.Blocked) throw new InvalidDataException(ModelBlockedText + " Aufnahme bleibt erhalten.");
            if (availability == ModelAvailability.Missing) throw new InvalidDataException("Modell fehlt oder ist beschädigt. Bitte erneut herunterladen; Aufnahme bleibt erhalten.");
            var result = await transcriber.TranscribeAsync(AppPaths.Model, pcm, operation.Token);
            if (string.IsNullOrWhiteSpace(result.Text)) { state.Move(DictationState.Error); Notice("Keine Sprache erkannt. Zwischenablage unverändert. Aufnahme wiederholen oder verwerfen."); return; }
            string corrected = replacements == null ? result.Text : CorrectionEngine.Apply(result.Text, replacements.Rules);
            SetTranscript(corrected); recognitionSource = result.Text;
            state.HasUncopiedText = true;
            RecoveryStore.Save(corrected);
            AppPaths.DeleteOwned(pending); pending = null; state.HasPendingAudio = false;
            bool copied = await UnicodeClipboard.CopyAsync(hotkey.Handle, corrected, operation.Token);
            if (copied) { RecoveryStore.Clear(); state.HasUncopiedText = false; }
            state.Move(DictationState.Result);
            Notice(copied ? $"Text kopiert{(corrected != result.Text ? " · Ersetzungen angewendet" : "")} · {result.Seconds:F1} s Verarbeitung · Mit Strg+V einfügen" : ClipboardBusyNotice);
        }
        catch (OperationCanceledException) { state.Move(DictationState.Error); Notice(state.HasUncopiedText ? "Abgebrochen. Transkript bleibt zum Kopieren erhalten." : "Abgebrochen. Aufnahme bleibt für Wiederholen oder Verwerfen erhalten."); }
        catch (Exception ex) { state.Move(DictationState.Error); Notice(FailureNotice(ex)); }
        finally
        {
            try { AppPaths.DeleteOwned(pcm); } catch (IOException) { Notice("Temporäre Audiodatei noch gesperrt; Bereinigung beim Neustart."); }
            finally { operation.Dispose(); operation = null; UpdateUi(); }
        }
    }
    private async Task CancelAsync()
    {
        if (activities.Busy) { activities.Cancel(); return; }
        if (state.Current == DictationState.Recording)
        {
            state.Move(DictationState.Stopping); recordingTime.Stop(); UpdateUi("Aufnahme wird beendet …");
            try { await audio.StopAsync(); } catch { }
            state.Move(DictationState.Error); Notice("Aufnahme angehalten. Wiederholen oder bewusst verwerfen.");
        }
        else operation?.Cancel();
    }
    // S1: one source for the three discard labels; the block reason and the final notice read it too.
    private static string DiscardWording(bool text, bool audio, bool alreadyDone)
    {
        string subject = text && audio ? "Ergebnis und Aufnahme" : text ? "Ergebnis" : "Aufnahme";
        return alreadyDone ? subject + " verworfen" : subject + " verwerfen";
    }
    private string DiscardLabel() => DiscardWording(state.HasUncopiedText, pending != null, false);
    private void Discard()
    {
        if (state.Busy || copyBusy || editingBusy) return;
        bool hadText = state.HasUncopiedText;
        bool hadAudio = pending != null;
        string removed = DiscardWording(hadText, hadAudio, true);
        try
        {
            if (state.HasUncopiedText) { RecoveryStore.Clear(); state.HasUncopiedText = false; recoveryUnavailable = false; reloadRecovery.Visible = false; SetTranscript(""); }
            AppPaths.DeleteOwned(pending); pending = null;
            pending = Directory.GetFiles(AppPaths.Audio, "recording-*.wav").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            state.HasPendingAudio = pending != null;
            state.Move(pending == null ? DictationState.Ready : DictationState.Error);
            Notice(pending == null ? "Bereit · " + removed : removed + " · Weitere unterbrochene Aufnahme vorhanden.");
        }
        catch { Notice("Aufnahme konnte nicht gelöscht werden. Bitte erneut versuchen."); }
    }
    private async Task CopyAgainAsync()
    {
        if (activities.Busy || copyBusy || editingBusy || state.Busy || string.IsNullOrWhiteSpace(transcript.Text)) return;
        editingBusy = true; UpdateUi();
        try
        {
            bool withoutRule = false;
            if (editsDirty)
            {
                var suggestion = CorrectionEngine.SuggestWithOrigin(editBaseline, transcript.Text, recognitionSource, replacements?.Rules ?? []);
                withoutRule = suggestion == null;
                if (suggestion != null)
                {
                    using var dialog = new CorrectionDialog(suggestion, replacements != null, replacements?.Rules.Any(r => r.From == suggestion.From) == true);
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    if (dialog.Remember)
                    {
                        try { replacements!.Upsert(suggestion); RefreshRules(); }
                        catch { UpdateUi("Regel konnte nicht gespeichert werden. Korrektur bleibt erhalten; erneut übernehmen oder „Nur diesmal“ wählen."); return; }
                    }
                }
                RecoveryStore.Save(transcript.Text); editSaveFailed = false;
                editBaseline = transcript.Text; editsDirty = false;
            }
            copyBusy = true; UpdateUi();
            string snapshot = transcript.Text;
            bool copied = await UnicodeClipboard.CopyAsync(hotkey.Handle, snapshot);
            if (copied) { RecoveryStore.Clear(); state.HasUncopiedText = false; }
            Notice(copied ? (withoutRule ? "Korrektur nur diesmal kopiert · Keine eindeutige Einzelregel. Änderungen einzeln übernehmen." : "Text kopiert · Mit Strg+V einfügen") : "Zwischenablage weiterhin belegt. Text bleibt hier erhalten.");
        }
        catch { Notice("Kopie oder lokale Ergebnisbereinigung fehlgeschlagen. Text bleibt erhalten."); }
        finally { copyBusy = false; editingBusy = false; UpdateUi(); }
    }
    private void SetTranscript(string text)
    {
        settingTranscript = true;
        try { transcript.Text = text; editBaseline = transcript.Text; recognitionSource = null; editsDirty = false; editSaveFailed = false; }
        finally { settingTranscript = false; }
    }
    private void TranscriptEdited()
    {
        if (settingTranscript) return;
        editsDirty = transcript.Text != editBaseline;
        state.HasUncopiedText = true;
        try { RecoveryStore.Save(transcript.Text); editSaveFailed = false; state.HasUncopiedText = !string.IsNullOrWhiteSpace(transcript.Text); }
        catch { editSaveFailed = true; }
        UpdateUi(editSaveFailed ? "Korrektur nur im Fenster – Speichern fehlgeschlagen. Bitte nicht schließen; erneut übernehmen." : "Text geändert · „Korrektur übernehmen“ oder Strg+Enter drücken.");
    }
    private void RefreshRules()
    {
        ruleList.Items.Clear();
        if (replacements != null) foreach (var rule in replacements.Rules) ruleList.Items.Add(new ListViewItem([rule.From, rule.To]));
        rulesHint.Text = replacements == null ? "Regeln konnten nicht geladen werden. Die vorhandene Datei bleibt unverändert. Automatische Ersetzungen sind derzeit ausgesetzt." :
            replacements.Rules.Count == 0 ? "Noch keine Ersetzungen gespeichert. Korrigiere im Bereich Diktat eine falsch erkannte Stelle und wähle „Korrektur übernehmen“. Danach kannst du dir diese Ersetzung merken lassen." :
            $"{replacements.Rules.Count} von {ReplacementStore.MaxRules} Ersetzungen · nur nach deiner Bestätigung gespeichert. Exakte Schreibweise, ganze Wörter/Wortfolgen, keine Kettenersetzung. Die Regeln bleiben lokal und sind mit deinem Windows-Konto verschlüsselt.";
    }
    private void RemoveRule()
    {
        if (state.Busy || editingBusy || copyBusy || replacements == null || ruleList.SelectedItems.Count != 1) return;
        string from = ruleList.SelectedItems[0].Text;
        if (MessageBox.Show(this, "Die markierte Ersetzung künftig nicht mehr anwenden?", "Ersetzung löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try { replacements.Remove(from); RefreshRules(); UpdateUi("Ersetzung gelöscht. Bereits vorhandener Text bleibt unverändert."); }
        catch { UpdateUi("Ersetzung konnte nicht gelöscht werden. Bisherige Regel bleibt erhalten."); }
    }
    private async Task DownloadModelAsync()
    {
        if (state.Busy) return;
        state.Move(DictationState.Setup); operation = new(); UpdateUi("Modell wird heruntergeladen …");
        try
        {
            await ModelStore.DownloadAsync(AppPaths.InstalledModel, new Progress<int>(p => { progress.Value = p; modelLabel.Text = $"Sprachmodell: {p} % von 488 MB"; }), operation.Token);
            modelReady = true; modelBlocked = false; state.Move(pending == null ? DictationState.Ready : DictationState.Error); Notice("Modell geprüft. Offline-Erkennung ist eingerichtet.");
        }
        catch (OperationCanceledException) { state.Move(DictationState.Error); Notice("Download abgebrochen. Erneut herunterladen ist möglich."); }
        catch { state.Move(DictationState.Error); Notice("Modelldownload fehlgeschlagen. Internetverbindung und freien Speicher prüfen, dann erneut versuchen."); }
        finally { operation.Dispose(); operation = null; UpdateUi(); }
    }
    private void ChangeHotkey()
    {
        if (state.Busy) return;
        var built = CreateHotkeyDialog();
        using var dialog = built.Dialog;
        var hint = built.Hint;
        dialog.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape) { dialog.Close(); return; }
            var candidate = new HotkeySpec((uint)((e.Alt ? 1 : 0) | (e.Control ? 2 : 0) | (e.Shift ? 4 : 0)), (uint)e.KeyCode);
            if (!candidate.IsValid) return;
            if (!hotkey.TryRegister(candidate)) { hint.Text = "Diese Kombination ist bereits belegt.\nBitte eine andere Kombination drücken."; return; }
            var old = settings; settings = settings with { Hotkey = candidate };
            try { settings.Save(AppPaths.Settings); dialog.Close(); UpdateUi("Hotkey gespeichert."); }
            catch { settings = old; hotkey.TryRegister(old.EffectiveHotkey); hint.Text = "Speichern fehlgeschlagen. Bisheriger Hotkey bleibt aktiv."; }
        };
        dialog.ShowDialog(this);
    }
    // Sprint C (S17): Farben kommen aus BrandTheme.Apply und folgen damit dem Hochkontrastmodus.
    private static (Form Dialog, Label Hint) CreateHotkeyDialog()
    {
        var dialog = new Form { Text = "Tastenkombination ändern", ClientSize = new Size(540, 210), AutoScaleMode = AutoScaleMode.Dpi, Font = BrandTheme.Body, Icon = BrandTheme.Icon, StartPosition = FormStartPosition.CenterParent, KeyPreview = true, MinimizeBox = false, MaximizeBox = false };
        var hint = new Label { Text = "Gewünschte Kombination drücken:\nStrg oder Alt + Leertaste, A–Z oder F1–F11.\nEscape schließt dieses Fenster.", AutoSize = true, Location = new Point(20, 20) };
        dialog.Controls.Add(hint);
        BrandTheme.Apply(dialog);
        return (dialog, hint);
    }
    private void Notice(string text)
    {
        UpdateUi(text); overlay.SetStatus(text, false); noticeUntil = DateTime.UtcNow.AddSeconds(7);
    }
    private void UpdateUi(string? text = null)
    {
        if (text != null) lastStatus = text;
        string? block = StartBlockReason();
        status.Text = block == null ? lastStatus : block + (lastStatus != "Bereit" && lastStatus != block ? "\n" + lastStatus : "");
        status.AccessibleName = status.Text;
        status.Visible = status.Text != "Bereit";
        details.Text = hotkey.Registered?.ToString() ?? "Hotkey nicht registriert";
        stateSummary.Text = state.Current switch
        {
            DictationState.Recording => "● Aufnahme läuft", DictationState.Stopping => "● Aufnahme endet",
            DictationState.Transcribing => "● Wird transkribiert", DictationState.Setup => "● Einrichtung",
            DictationState.Error => "● Bitte prüfen", DictationState.Result => "● Ergebnis bereit", _ => "● Bereit"
        };
        stateSummary.ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : state.Current == DictationState.Recording ? BrandTheme.Recording : BrandTheme.Petrol;
        toggle.Text = state.Current == DictationState.Recording ? "Stoppen und transkribieren" : "Aufnahme starten";
        if (activities.Busy) stateSummary.Text = laboratory.IsRecording ? "● Laboraufnahme" : "● Zusatzfunktion läuft";
        else if (!state.Busy)
        {
            if (copyBusy || editingBusy) stateSummary.Text = "● Text wird gesichert";
            else if (state.HasUncopiedText) stateSummary.Text = "● Text wartet";
            else if (state.HasPendingAudio) stateSummary.Text = "● Aufnahme wartet";
            else if (!modelReady && modelBlocked) stateSummary.Text = "● Modell nicht lesbar";
            else if (!modelReady && !modelChecked) stateSummary.Text = "● Modell wird geprüft";
            else if (!modelReady) stateSummary.Text = "● Modell fehlt";
        }
        bool interactive = !activities.Busy && !state.Busy && !copyBusy && !editingBusy;
        // Blocked model keeps the button reachable: pressing it re-checks instead of recording (S3).
        toggle.Enabled = !activities.Busy && !copyBusy && !editingBusy && (modelReady || modelBlocked) && (state.CanStart || state.Current == DictationState.Recording);
        retry.Enabled = interactive && pending != null && modelReady && !state.HasUncopiedText;
        retry.Visible = pending != null;
        discard.Enabled = interactive && (pending != null || state.HasUncopiedText);
        discard.Visible = pending != null || state.HasUncopiedText;
        discard.Text = DiscardLabel();
        // Sprint E (N6): Bei der Einrichtung bricht „Abbrechen“ nur den Download ab; Start- und Nachprüfung haben keinen abbrechbaren Vorgang.
        cancel.Enabled = state.Current is DictationState.Recording or DictationState.Transcribing || (state.Current == DictationState.Setup && operation != null);
        cancel.Visible = cancel.Enabled;
        level.Visible = state.Current == DictationState.Recording;
        modelDownload.Enabled = interactive && !modelReady && !modelBlocked; settingsButton.Enabled = interactive; refresh.Enabled = interactive; microphones.Enabled = interactive;
        copy.Enabled = interactive && !string.IsNullOrWhiteSpace(transcript.Text) && !recoveryUnavailable;
        copy.Text = state.HasUncopiedText ? "Text kopieren und freigeben" : "Erneut kopieren";
        copy.Primary = state.HasUncopiedText;
        applyCorrection.Enabled = copy.Enabled && editsDirty;
        transcript.ReadOnly = !interactive || recoveryUnavailable || pending != null;
        removeRule.Enabled = interactive && ruleList.SelectedItems.Count == 1 && replacements != null;
        reloadRecovery.Enabled = interactive;
        tray.Text = "Sprachgarten · " + stateSummary.Text.TrimStart('●', ' ');
        if (activities.Busy) tray.Text = "Sprachgarten · " + activities.Label;
        if (state.Current != DictationState.Recording) { level.Value = 0; duration.Text = "Maximal 5 Minuten pro Aufnahme"; }
        if (modelReady) modelLabel.Text = "Whisper Small · SHA-256 geprüft · Offline verfügbar";
        else if (!modelChecked) modelLabel.Text = "Whisper Small wird geprüft …";
        else if (state.Current == DictationState.Setup) { }
        else if (modelBlocked) modelLabel.Text = "Whisper Small ist momentan nicht lesbar · kein neuer Download nötig";
        else modelLabel.Text = "Whisper Small fehlt oder ist ungültig · 488 MB Download";
        resizeDictation?.Invoke();
    }
    private string? StartBlockReason()
    {
        if (activities.Busy) return "Bitte warten: " + activities.Label;
        if (state.Busy || copyBusy || editingBusy) return null;
        if (state.HasUncopiedText)
            return recoveryUnavailable
                ? "Gesicherter Text konnte nicht geladen werden. Bitte „Ergebnis erneut laden“ wählen oder bewusst verwerfen. Neue Aufnahme bleibt gesperrt."
                : string.IsNullOrWhiteSpace(transcript.Text)
                    ? "Ein leeres Ergebnis wartet noch. Bitte „" + DiscardLabel() + "“ wählen, um die Aufnahme freizugeben."
                    : "Ein vorheriger Text wartet noch. Bitte „Text kopieren und freigeben“ wählen und den Text bei Bedarf mit Strg+V sichern. Danach ist eine neue Aufnahme möglich.";
        if (state.HasPendingAudio) return "Eine unterbrochene Aufnahme wartet. Bitte erneut transkribieren oder bewusst verwerfen, bevor du neu aufnimmst.";
        if (!modelReady && modelBlocked) return ModelBlockedText;
        if (!modelReady) return "Das Sprachmodell ist noch nicht verfügbar. Bitte unter „Einstellungen“ das Modell einrichten.";
        return null;
    }
    // S13/T1: a held exit has to be readable, so the window comes back if it was hidden or minimised.
    private void ShowForHold()
    {
        if (!IsHandleCreated) return;
        if (IsDisposed) return;
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }
    // S13: all three areas are always flushed; no short-circuiting || skips the later ones.
    private bool FlushAreas(string intro)
    {
        var unsaved = new List<string>();
        var pages = new List<TabPage>();
        if (!imports.Flush()) { unsaved.Add(ImportsArea); pages.Add(importsTab); }
        if (!laboratory.Flush()) { unsaved.Add(LaboratoryArea); pages.Add(laboratoryTab); }
        if (!workshop.Flush()) { unsaved.Add(WorkshopArea); pages.Add(workshopTab); }
        if (unsaved.Count == 0) return true;
        tabs.SelectedTab = pages[0];
        ShowForHold();
        UpdateUi(intro + " Nicht gesichert: " + string.Join(", ", unsaved) + ". Bitte im Register „" + unsaved[0] + "“ kopieren oder erneut speichern.");
        return false;
    }
    // S2: the recognised text is written before closing, but a damaged backup is never overwritten.
    private bool PersistUncopiedTextBeforeExit()
    {
        if (!editSaveFailed && !state.HasUncopiedText) return true;
        if (recoveryUnavailable) return true;
        // An empty field must never replace a stored result; the explicit discard stays the way out.
        if (string.IsNullOrWhiteSpace(transcript.Text) && RecoveryStore.Exists)
        {
            ShowForHold();
            UpdateUi("Beenden angehalten: Das leere Feld würde das gesicherte Ergebnis überschreiben. Bitte „" + DiscardLabel() + "“ wählen oder das Ergebnis erneut laden.");
            return false;
        }
        try
        {
            RecoveryStore.Save(transcript.Text);
            editSaveFailed = false;
            return true;
        }
        catch
        {
            ShowForHold();
            UpdateUi("Beenden angehalten: Der noch nicht kopierte Text konnte nicht gesichert werden. Bitte kopieren oder bewusst verwerfen.");
            return false;
        }
    }
    // T1: best effort on a Windows shutdown. Synchronous, no dialog, no cancelling.
    private void RescueBeforeShutdown()
    {
        try { imports.Flush(); }
        catch { }
        try { laboratory.Flush(); }
        catch { }
        try { workshop.Flush(); }
        catch { }
        if (recoveryUnavailable) return;
        if (!editSaveFailed && !state.HasUncopiedText) return;
        if (string.IsNullOrWhiteSpace(transcript.Text) && RecoveryStore.Exists) return;
        try { RecoveryStore.Save(transcript.Text); }
        catch { }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (exiting) return;
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            if (IsHandleCreated && !IsDisposed) BeginInvoke(async () => await ExitAsync());
            return;
        }
        RescueBeforeShutdown();
    }
    private async Task ExitAsync()
    {
        if (exiting) return;
        // A running copy or correction must say so instead of swallowing the exit request.
        if (editingBusy || copyBusy)
        {
            ShowForHold();
            UpdateUi("Beenden wartet: Kopieren oder Korrektur wird gerade abgeschlossen. Danach erneut Beenden wählen.");
            return;
        }
        if (exitRequested) return;
        exitRequested = true;
        try
        {
            if (!FlushAreas("Beenden angehalten: Ein Ergebnis ist noch nicht gesichert.")) return;
            if (!PersistUncopiedTextBeforeExit()) return;
            exiting = true; operation?.Cancel(); activities.Cancel();
            if (audio.IsRecording) { try { await audio.StopAsync(); } catch { } }
            // Let the processing finally release/kill its worker before disposing the UI.
            while (operation != null || activities.Busy) await Task.Delay(50);
            if (!FlushAreas("Beenden angehalten: Neues Ergebnis konnte nicht gesichert werden.")) { exiting = false; return; }
            timer.Stop(); tray.Visible = false; Close();
        }
        finally { if (!exiting) exitRequested = false; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { readingHotkey.Dispose(); reading.Dispose(); timer.Dispose(); audio.Dispose(); hotkey.Dispose(); tray.Dispose(); overlay.Dispose(); operation?.Dispose(); }
        base.Dispose(disposing);
    }
}

public sealed class StatusOverlay : Form
{
    private readonly Label label = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = BrandTheme.Body, Padding = new Padding(12) };
    public StatusOverlay()
    {
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; Size = new Size(600, 100);
        var colors = Palette(false, SystemInformation.HighContrast);
        BackColor = colors.Back; label.ForeColor = colors.Fore; Controls.Add(label);
    }
    // Sprint C (S17): reine Farbfunktion, damit der Hochkontrastzweig ohne Desktop prüfbar ist.
    internal static (Color Back, Color Fore) Palette(bool recording, bool highContrast) =>
        highContrast ? (SystemColors.Highlight, SystemColors.HighlightText) : (recording ? BrandTheme.Recording : BrandTheme.Petrol, Color.White);
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80 | 0x20; return p; } }
    public void SetStatus(string text, bool recording)
    {
        var colors = Palette(recording, SystemInformation.HighContrast);
        label.Text = text; BackColor = colors.Back; label.ForeColor = colors.Fore;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 20, area.Bottom - Height - 20);
        if (!Visible) Show();
    }
}
