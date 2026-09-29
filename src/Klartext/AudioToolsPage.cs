using System.Diagnostics;
using System.Text.RegularExpressions;
using NAudio.Wave;

namespace Klartext;

public static class WorkshopUi
{
    public static TextBox Text(string name, int height = 90) => new() { AccessibleName = name, Name = name, Multiline = true, ScrollBars = ScrollBars.Vertical, Width = 700, Height = height, MaxLength = 24000 };
    /// <summary>Sprint C: wachsendes Feld ohne eigenen Scrollbalken, mindestens <paramref name="minLines"/> Zeilen hoch.</summary>
    public static GardenTextBox Grow(string name, int minLines) => new() { AccessibleName = name, Name = name, MinLines = minLines, Width = 700, MaxLength = 24000 };
    public static Label Hint(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(700, 0), Font = BrandTheme.Small, Margin = new Padding(3, 8, 3, 6) };
    public static FlowLayoutPanel Row(params Control[] items) { var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(710, 0) }; row.Controls.AddRange(items); return row; }
    public static GardenButton Button(string label, EventHandler click) { var b = new GardenButton { Text = label }; b.Click += click; return b; }
    public static string FreshPath(string path) => Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + "-neu" + Path.GetExtension(path));
    public static bool ConfirmFreshStart(IWin32Window owner, string fileName)
    {
        string question = $"Leer neu anfangen?\r\n\r\nDie nicht lesbare Datei {fileName} bleibt unverändert liegen; sie wird weder gelöscht noch überschrieben.\r\nDu beginnst hier mit einem leeren Text, der ab dann in einer zweiten Datei gesichert wird.";
        return MessageBox.Show(owner, question, "Leer neu anfangen", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }
    public static void Fit(FlowLayoutPanel panel)
    {
        // Sprint E (K8): Das Anpassen kann den Scrollbalken ein- oder ausblenden und damit ClientSizeChanged erneut auslösen.
        bool fitting = false, again = false;
        panel.ClientSizeChanged += (_, _) =>
        {
            // Sprint F (F8): Ein Aufruf während des Anpassens wird vorgemerkt und sofort nachgerechnet, höchstens 3 Runden, ohne Timer (Muster resizeDictation).
            if (fitting) { again = true; return; }
            fitting = true;
            try
            {
                int rounds = 0;
                do
                {
                    again = false;
                    // Sprint E: rechter Rand wie in der Werkstatt, Scrollbalkenbreite plus zweimal CardGap, dpi-skaliert.
                    int gap = (int)(BrandTheme.CardGap * panel.DeviceDpi / 96f);
                    int width = Math.Max(200, panel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - gap * 2);
                    foreach (Control child in panel.Controls)
                    {
                        // Sprint C: Karten bekommen die volle Breite, ihr Inhalt die Breite innerhalb der Karte.
                        if (child is GardenCardFlow card)
                        {
                            card.MinimumSize = new Size(width, 0); card.MaximumSize = new Size(width, 0);
                            int inner = Math.Max(150, width - card.Padding.Horizontal - 10);
                            foreach (Control nested in card.Controls)
                            {
                                if (nested is FlowLayoutPanel or Label) nested.MaximumSize = new Size(inner, 0);
                                else if (nested is not System.Windows.Forms.Button) nested.Width = inner;
                            }
                        }
                        else if (child is FlowLayoutPanel or Label) child.MaximumSize = new Size(width, 0);
                        else child.Width = width;
                    }
                }
                while (again && ++rounds < 3);
            }
            finally { fitting = false; }
        };
    }
}

public sealed class AudioToolsPage : UserControl
{
    private readonly bool laboratory;
    private readonly ActivityGate gate;
    private readonly Func<string?> device;
    private readonly AudioRecorder recorder = new();
    private readonly Stopwatch watch = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 150 };
    // Sprint C (T2c): 8 Zeilen für das Datei-Transkript, 3 für den Vergleichstext; darüber wachsen sie mit.
    private readonly GardenTextBox result = WorkshopUi.Grow("Datei-Transkript", 8);
    private readonly GardenTextBox expected = WorkshopUi.Grow("Vergleichstext", 3);
    // Sprint C (S10): benannte Knöpfe, damit RefreshActions sie nach Zustand sperren und hervorheben kann.
    private readonly GardenButton choose;
    private readonly GardenButton stop = new() { Text = "Aufnahme stoppen" };
    private readonly GardenButton cancelOperation = new() { Text = "Abbrechen / Wiedergabe stoppen" };
    private readonly GardenButton check = new() { Text = "Datei prüfen" };
    private readonly GardenButton transcribe = new() { Text = "Transkribieren / wiederholen" };
    private readonly GardenButton play = new() { Text = "Anhören" };
    private readonly GardenButton discardCopy = new() { Text = "Aufnahme / Arbeitskopie verwerfen" };
    private readonly GardenButton copyText = new() { Text = "Text kopieren" };
    private readonly GardenButton toWorkshop = new() { Text = "Zur Promptwerkstatt" };
    private readonly GardenButton clearText = new() { Text = "Text leeren" };
    private readonly GardenButton compare = new() { Text = "Texte vergleichen" };
    private readonly Label stepHint = WorkshopUi.Hint("");
    // Sprint C (S14): Statusbereich unten angedockt, nie verdeckt, so hoch wie die Meldung.
    private readonly Panel statusArea = new() { Dock = DockStyle.Bottom, AccessibleName = "Status" };
    private bool layingOutStatus;
    private readonly Label info = WorkshopUi.Hint("Bereit. Es wird nichts aufgenommen.");
    private readonly ProgressBar meter = new() { Width = 700, Height = 12 };
    private readonly string basePath;
    private readonly string freshPath;
    private readonly GardenButton reloadResult = new() { Text = "Erneut laden" };
    private readonly GardenButton freshResult = new() { Text = "Leer neu anfangen" };
    private readonly FlowLayoutPanel recoveryActions;
    private string recoveryPath;
    private ActivityGate.Lease? recordingLease;
    private WaveOutEvent? player;
    private AudioFileReader? playbackReader;
    private ActivityGate.Lease? playbackLease;
    private string? pending;
    private string? loadNote;
    private bool working, stopping, saveFailed, loading, damagedRecovery;
    public event Action<string>? ToWorkshop;
    public bool IsRecording => recordingLease != null;
    public TimeSpan RecordingDuration => watch.Elapsed;
    private bool InProgress => working || recordingLease != null || playbackLease != null;

    public AudioToolsPage(bool laboratory, ActivityGate gate, Func<string?> device)
    {
        this.laboratory = laboratory; this.gate = gate; this.device = device;
        Dock = DockStyle.Fill; AutoScaleMode = AutoScaleMode.Dpi;
        basePath = Path.Combine(AppPaths.Root, laboratory ? "labor-result.bin" : "import-result.bin");
        freshPath = WorkshopUi.FreshPath(basePath);
        recoveryPath = basePath;
        var flow = new GardenScrollFlow { Dock = DockStyle.Fill };
        Controls.Add(flow); Controls.Add(statusArea); WorkshopUi.Fit(flow);
        choose = new GardenButton { Text = laboratory ? "Testaufnahme starten" : "Audiodatei wählen …" };
        choose.Click += async (_, _) => { if (laboratory) StartRecording(); else await ChooseAsync(); };
        stop.Click += async (_, _) => await StopRecordingAsync();
        stop.Visible = laboratory;
        cancelOperation.Click += (_, _) => CancelOperation();
        check.Click += async (_, _) => await ProcessAsync(false);
        transcribe.Click += async (_, _) => await ProcessAsync(true);
        play.Click += (_, _) => Play();
        discardCopy.Click += (_, _) => Discard();
        copyText.Click += async (_, _) => await CopyAsync();
        toWorkshop.Click += (_, _) => SendToWorkshop();
        clearText.Click += (_, _) => ClearText();
        compare.Click += (_, _) => Compare();
        // Zwei Karten: Aufnahme oder Datei, dann der Text.
        var source = new GardenCardFlow { AccessibleName = laboratory ? "Testaufnahme" : "Audiodatei" };
        source.Controls.Add(stepHint);
        source.Controls.Add(WorkshopUi.Hint(laboratory
            ? "Bis 60 Sekunden · Mikrofon aus den Einstellungen. Aufnahme nur nach Klick; der Diktat-Hotkey pausiert dabei."
            : "WAV, MP3, M4A oder FLAC · bis 5 Minuten / 200 MB. Dein Original bleibt unverändert."));
        source.Controls.Add(WorkshopUi.Row(choose, stop, cancelOperation));
        if (laboratory) source.Controls.Add(meter);
        source.Controls.Add(WorkshopUi.Row(check, transcribe, play, discardCopy));
        var textCard = new GardenCardFlow { AccessibleName = "Text" };
        textCard.Controls.Add(new Label { Text = "3. Ergebnis nutzen", AutoSize = true, Font = BrandTheme.Large });
        result.PlaceholderText = "Hier erscheint dein erkannter Text. Danach kannst du ihn kopieren oder in der Promptwerkstatt weiterverwenden.";
        textCard.Controls.Add(WorkshopUi.Hint("Text bleibt bis zum bewussten Leeren lokal verschlüsselt erhalten. Datei-Ergebnisse werden nicht automatisch in die Zwischenablage geschrieben."));
        textCard.Controls.Add(result);
        textCard.Controls.Add(WorkshopUi.Row(copyText, toWorkshop, clearText));
        if (laboratory)
        {
            textCard.Controls.Add(WorkshopUi.Hint("Optional: erwarteten Testsatz eintragen. Der Vergleich misst Wortabweichungen; kein Urteil über deine Stimme."));
            textCard.Controls.Add(expected);
            textCard.Controls.Add(compare);
        }
        flow.Controls.Add(source); flow.Controls.Add(textCard);
        source.Controls.Add(WorkshopUi.Hint(laboratory
            ? "Die Aufnahme bleibt bis zum Verwerfen gespeichert. Der Vergleich bewertet die Erkennung, nicht deine Stimme."
            : "Erkennung auf Deutsch und lokal · keine Übersetzung, keine automatischen Ersetzungen."));
        info.AutoSize = false; info.MaximumSize = Size.Empty; statusArea.Controls.Add(info);
        reloadResult.Click += (_, _) =>
        {
            if (!InProgress) ReloadResult();
        };
        freshResult.Click += (_, _) =>
        {
            if (InProgress) return;
            if (WorkshopUi.ConfirmFreshStart(this, Path.GetFileName(recoveryPath))) StartFreshResult();
        };
        recoveryActions = WorkshopUi.Row(reloadResult, freshResult);
        recoveryActions.Visible = false;
        statusArea.Controls.Add(recoveryActions);
        statusArea.ClientSizeChanged += (_, _) => LayoutStatus();
        info.TextChanged += (_, _) => LayoutStatus();
        LoadResult();
        gate.Changed += RefreshActions;
        result.TextChanged += (_, _) => SaveResult();
        result.TextChanged += (_, _) => RefreshActions();
        expected.TextChanged += (_, _) => RefreshActions();
        recorder.UnexpectedStop += _ =>
        {
            if (!IsHandleCreated || IsDisposed) return;
            try { BeginInvoke(async () => await StopRecordingAsync(true)); } catch (InvalidOperationException) { /* Window closed while the device callback was queued. */ }
        };
        timer.Tick += async (_, _) =>
        {
            if (recordingLease != null && !stopping)
            {
                meter.Value = (int)Math.Clamp(recorder.Peak * 100, 0, 100);
                info.Text = $"● Mikrofon aktiv · {watch.Elapsed:mm\\:ss} / 01:00";
                if (watch.Elapsed.TotalSeconds >= 60 || recordingLease.Token.IsCancellationRequested) await StopRecordingAsync();
            }
            if (playbackLease != null && (playbackLease.Token.IsCancellationRequested || player?.PlaybackState != PlaybackState.Playing)) StopPlayback();
            // Nur bei Zustandswechsel neu setzen; der Takt fängt Wechsel auf, die sonst keinen Aufrufer haben.
            if (ActionKey() != lastActionKey) RefreshActions();
        };
        timer.Start();
    }
    /// <summary>Alle Eingaben, die RefreshActions liest; ändert sich keine, bleibt die Knopfleiste, wie sie ist.</summary>
    private readonly record struct ActionState(bool Working, bool Recording, bool Playing, bool Stopping, bool GateBusy, bool Damaged, bool FreshFile, bool HasCopy, bool HasText, bool AnyText, bool HasExpected);
    private ActionState? lastActionKey;
    private ActionState ActionKey() => new(working, recordingLease != null, playbackLease != null, stopping, gate.Busy, damagedRecovery, recoveryPath == freshPath, pending != null,
        !string.IsNullOrWhiteSpace(result.Text), result.Text.Length > 0, !string.IsNullOrWhiteSpace(expected.Text));
    private string DamagedMessage()
    {
        if (recoveryPath == freshPath) return $"Gesicherter Text nicht lesbar: weder {Path.GetFileName(basePath)} noch {Path.GetFileName(freshPath)}. Beide Dateien bleiben unverändert liegen; sie werden weder gelöscht noch überschrieben. „Erneut laden“ liest erneut. Einen weiteren leeren Anfang biete ich nicht an, weil ein Lesefehler am Windows-Konto hängt und nicht an der einzelnen Datei. Dasselbe Windows-Konto verwenden.";
        return $"Gesicherter Text nicht lesbar. Die Datei {Path.GetFileName(recoveryPath)} bleibt unverändert liegen; sie wird weder gelöscht noch überschrieben. Du hast zwei Wege: „Erneut laden“ liest sie noch einmal, „Leer neu anfangen“ beginnt mit leerem Text in einer zweiten Datei. Hilft beides nicht, dasselbe Windows-Konto verwenden.";
    }
    // LoadResult bleibt unverändert und ruft weiter diesen Namen; er führt jetzt die ganze Zustandsprüfung aus.
    private void RefreshRecoveryActions() => RefreshActions();
    // Sprint C (S10): Sperren nach Zustand und genau eine Hauptaktion je Seite; Auswege wie bisher.
    private void RefreshActions()
    {
        if (IsDisposed) return;
        lastActionKey = ActionKey();
        bool usable = !InProgress && !gate.Busy;
        recoveryActions.Visible = damagedRecovery;
        reloadResult.Visible = damagedRecovery;
        reloadResult.Enabled = usable;
        freshResult.Visible = damagedRecovery && recoveryPath != freshPath;
        freshResult.Enabled = usable;
        bool free = usable;
        bool hasCopy = pending != null;
        bool hasText = !string.IsNullOrWhiteSpace(result.Text);
        bool canCheck = free && !damagedRecovery && hasCopy;
        choose.Enabled = free && !damagedRecovery && !hasCopy && !hasText;
        stop.Enabled = recordingLease != null && !stopping;
        cancelOperation.Enabled = InProgress;
        check.Enabled = canCheck;
        transcribe.Enabled = canCheck && !hasText;
        play.Enabled = free && hasCopy;
        discardCopy.Enabled = free && hasCopy;
        copyText.Enabled = free && hasText;
        toWorkshop.Enabled = free && hasText;
        clearText.Enabled = free && !damagedRecovery && result.Text.Length > 0;
        compare.Enabled = hasText && !string.IsNullOrWhiteSpace(expected.Text);
        stop.Visible = recordingLease != null;
        cancelOperation.Visible = InProgress && recordingLease == null;
        check.Visible = play.Visible = discardCopy.Visible = hasCopy && recordingLease == null;
        transcribe.Visible = hasCopy && !hasText && recordingLease == null;
        meter.Visible = recordingLease != null;
        copyText.Visible = toWorkshop.Visible = clearText.Visible = hasText;
        stepHint.Text = damagedRecovery ? "Gesicherten Text wiederherstellen – die Auswege stehen unten." :
            recordingLease != null ? "1. Aufnahme läuft – anschließend Aufnahme stoppen." :
            InProgress ? "Bitte warten – der aktuelle Vorgang läuft." :
            hasText ? "3. Text ist da – kopieren oder zur Promptwerkstatt übernehmen." :
            hasCopy ? "2. Audio ist bereit – jetzt transkribieren oder zuerst anhören." :
            laboratory ? "1. Beginne mit einer kurzen Testaufnahme." : "1. Wähle die Audiodatei, aus der du Text machen möchtest.";
        GardenButton primary = recordingLease != null ? stop : InProgress ? cancelOperation : damagedRecovery ? reloadResult : hasText ? copyText : hasCopy ? transcribe : choose;
        foreach (var button in new[] { choose, stop, cancelOperation, check, transcribe, play, discardCopy, copyText, toWorkshop, clearText, compare, reloadResult, freshResult })
            button.Primary = button == primary;
        LayoutStatus();
    }
    private void LayoutStatus()
    {
        if (layingOutStatus) return;
        layingOutStatus = true;
        try
        {
            int gap = (int)Math.Round(BrandTheme.ElementGap * DeviceDpi / 96f);
            // Breite der Seite, nicht des Bereichs: Die Höhe muss vor dem Dock-Layout feststehen.
            int width = Math.Max(1, ClientSize.Width - statusArea.Padding.Horizontal);
            int infoHeight = info.GetPreferredSize(new Size(width, 0)).Height;
            info.SetBounds(0, gap, width, infoHeight);
            int bottom = gap + infoHeight;
            if (damagedRecovery)
            {
                recoveryActions.MaximumSize = new Size(width, 0);
                var size = recoveryActions.GetPreferredSize(new Size(width, 0));
                recoveryActions.Location = new Point(0, bottom);
                bottom += size.Height + recoveryActions.Margin.Vertical;
            }
            if (statusArea.Height != bottom + gap) statusArea.Height = bottom + gap;
        }
        finally { layingOutStatus = false; }
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        // Höhe des Statusbereichs vor dem Andocken festlegen, sonst bleibt seine Oberkante auf dem alten Wert stehen.
        LayoutStatus();
        base.OnLayout(e);
    }
    // Die sechs früher stummen Pfade nennen jetzt einen Grund, falls sie doch erreicht werden, etwa per Tastatur.
    private void CancelOperation()
    {
        if (InProgress) { gate.Cancel(); return; }
        info.Text = "Es läuft gerade kein Vorgang, der abgebrochen werden könnte.";
    }
    private void SendToWorkshop()
    {
        if (string.IsNullOrWhiteSpace(result.Text)) { info.Text = "Noch kein Text vorhanden, der in die Promptwerkstatt übernommen werden kann."; return; }
        ToWorkshop?.Invoke(result.Text);
    }
    private void Compare() => info.Text = string.IsNullOrWhiteSpace(expected.Text) || string.IsNullOrWhiteSpace(result.Text) ? "Erwarteten Text und Transkript eintragen." : $"Wortabweichung: {WordErrorRate(expected.Text, result.Text):P1}. Großschreibung und Satzzeichen ignoriert; Ergänzungen zählen mit.";
    private void StartFreshResult()
    {
        recoveryPath = freshPath;
        damagedRecovery = false;
        result.ReadOnly = false;
        loading = true;
        result.Clear();
        loading = false;
        SaveResult();
        if (!saveFailed) info.Text = $"Leer neu angefangen. Ab jetzt wird in {Path.GetFileName(freshPath)} gesichert; {Path.GetFileName(basePath)} bleibt unverändert liegen.";
        RefreshRecoveryActions();
    }
    private void ReloadResult()
    {
        LoadResult();
        if (damagedRecovery)
        {
            info.Text = DamagedMessage() + " Erneutes Laden hat gerade nicht geholfen.";
            return;
        }
        info.Text = "Gesicherter Text erneut geladen. Du kannst weiterarbeiten." + (loadNote == null ? "" : " " + loadNote);
    }
    private void LoadResult()
    {
        recoveryPath = File.Exists(freshPath) ? freshPath : basePath;
        loadNote = null;
        // Only decrypting the backup file may produce the locked state.
        try
        {
            loading = true;
            damagedRecovery = false;
            result.ReadOnly = false;
            result.Text = ProtectedFile.Read<string>(recoveryPath) ?? "";
            if (result.Text.Length > 24000) throw new InvalidDataException();
            info.Text = "Bereit. Es wird nichts aufgenommen.";
        }
        catch
        {
            damagedRecovery = true;
            result.ReadOnly = true;
            info.Text = DamagedMessage();
        }
        finally { loading = false; }
        // A failing recording folder is a separate, milder problem and never locks the page.
        try
        {
            pending = Directory.GetFiles(AppPaths.Audio, laboratory ? "labor-*.wav" : "import-*.*").Where(p => !p.EndsWith(".pcm.wav")).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (pending != null) loadNote = "Unterbrochene Arbeitskopie vorhanden. Prüfen, wiederholen oder verwerfen.";
        }
        catch
        {
            pending = null;
            loadNote = "Aufnahmeordner nicht lesbar. Arbeitskopien werden gerade nicht angezeigt; der gesicherte Text ist davon nicht betroffen.";
        }
        if (loadNote != null && !damagedRecovery) info.Text = loadNote;
        RefreshRecoveryActions();
    }
    private void SaveResult()
    {
        if (loading || damagedRecovery || result.ReadOnly) return;
        try { ProtectedFile.Write(recoveryPath, result.Text); saveFailed = false; }
        catch { saveFailed = true; info.Text = "Text nur im Fenster! Speichern fehlgeschlagen. Bitte kopieren und nicht beenden."; }
    }
    public bool Flush() { if (damagedRecovery) return true; if (!result.ReadOnly) SaveResult(); return !saveFailed; }
    private bool CanReplaceAudio()
    {
        if (damagedRecovery)
        {
            info.Text = DamagedMessage();
            return false;
        }
        if (InProgress)
        {
            info.Text = "Zuerst aktuellen Vorgang abschließen.";
            return false;
        }
        if (pending != null) { info.Text = "Eine Arbeitskopie ist noch vorhanden. Erst verwerfen oder transkribieren."; return false; }
        if (!string.IsNullOrWhiteSpace(result.Text)) { info.Text = "Vor dem nächsten Ergebnis Text sichern und bewusst leeren."; return false; }
        return true;
    }
    private ActivityGate.Lease? Acquire(string label)
    {
        var lease = gate.TryEnter(label);
        if (lease == null) info.Text = "Ein anderer Vorgang läuft. Bitte zuerst abschließen oder abbrechen.";
        return lease;
    }
    private async Task ChooseAsync()
    {
        if (!CanReplaceAudio()) return;
        using var dialog = new OpenFileDialog { Title = "Audiodatei auswählen", Filter = "Audio|*.wav;*.mp3;*.m4a;*.flac|Alle Dateien|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var lease = Acquire("Audiodatei importieren"); if (lease == null) return;
        working = true;
        string copy = Path.Combine(AppPaths.Audio, $"import-{Guid.NewGuid():N}{Path.GetExtension(dialog.FileName).ToLowerInvariant()}");
        try
        {
            var file = new FileInfo(dialog.FileName);
            if (file.Length is <= 0 or > 200_000_000) throw new InvalidDataException("Datei muss zwischen 1 Byte und 200 MB groß sein.");
            if (!new[] { ".wav", ".mp3", ".m4a", ".flac" }.Contains(file.Extension.ToLowerInvariant())) throw new InvalidDataException("Bitte WAV, MP3, M4A oder FLAC wählen.");
            info.Text = "Arbeitskopie wird erstellt …";
            using (var source = File.OpenRead(file.FullName))
            using (var target = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(target, lease.Token);
            lease.Token.ThrowIfCancellationRequested(); pending = copy;
            info.Text = "Arbeitskopie bereit. „Transkribieren“ starten. Original bleibt unverändert.";
        }
        catch (OperationCanceledException) { TryDelete(copy); info.Text = "Import abgebrochen."; }
        catch (Exception ex) { TryDelete(copy); info.Text = ex is InvalidDataException ? ex.Message : "Import fehlgeschlagen. Dateizugriff und freien Speicher prüfen."; }
        finally { working = false; }
    }
    private void StartRecording()
    {
        if (!CanReplaceAudio()) return;
        recordingLease = Acquire("Sprachlabor · Aufnahme"); if (recordingLease == null) return;
        pending = Path.Combine(AppPaths.Audio, $"labor-{Guid.NewGuid():N}.wav");
        try { recorder.Start(device(), pending); watch.Restart(); info.Text = "● Mikrofon aktiv – Aufnahme stoppen beendet den Zugriff."; }
        catch { recorder.Dispose(); TryDelete(pending); pending = null; recordingLease.Dispose(); recordingLease = null; info.Text = "Mikrofon nicht verfügbar. Gerät/Berechtigung in Windows prüfen."; }
        RefreshActions();
    }
    private async Task StopRecordingAsync(bool unexpected = false)
    {
        if (recordingLease == null || stopping) return;
        stopping = true;
        try { await recorder.StopAsync(); info.Text = unexpected ? "Gerät unterbrochen. Arbeitskopie bleibt zum Prüfen erhalten." : "Mikrofon freigegeben. Anhören, prüfen oder transkribieren."; }
        catch { info.Text = "Aufnahmefehler. Mikrofon freigegeben; vorhandene Arbeitskopie prüfen oder verwerfen."; }
        finally { watch.Stop(); meter.Value = 0; recordingLease.Dispose(); recordingLease = null; stopping = false; RefreshActions(); }
    }
    private async Task ProcessAsync(bool transcribe)
    {
        if (damagedRecovery)
        {
            info.Text = DamagedMessage();
            return;
        }
        if (pending == null || InProgress)
        {
            info.Text = "Zuerst eine abgeschlossene Aufnahme oder Datei bereitstellen.";
            return;
        }
        if (transcribe && !string.IsNullOrWhiteSpace(result.Text)) { info.Text = "Vorhandenen Text zuerst sichern und bewusst leeren."; return; }
        using var lease = Acquire(transcribe ? "Audiodatei transkribieren" : "Audiodatei prüfen"); if (lease == null) return;
        working = true; string pcm = pending + ".pcm.wav"; result.ReadOnly = true;
        try
        {
            info.Text = "Audio wird geprüft …";
            var stats = await Task.Run(() => AudioRecorder.ConvertToPcm(pending, pcm, lease.Token));
            lease.Token.ThrowIfCancellationRequested();
            info.Text = $"{stats.Seconds:F1} s · aktives Signal {stats.ActiveSeconds:F1} s · Spitze {stats.Peak:P0} · RMS {stats.Rms:F4}. " + (!stats.HasSignal ? "Zu kurz / zu still. Keine Transkription." : stats.Peak > .98 ? "Hoher Spitzenpegel: möglicherweise übersteuert." : "Messwerte vorhanden; Sprachqualität bitte selbst anhören.");
            if (!transcribe || !stats.HasSignal) return;
            if (!await ModelStore.VerifyAsync(AppPaths.Model, lease.Token)) throw new InvalidDataException("Sprachmodell fehlt oder ist beschädigt. In Einstellungen einrichten.");
            info.Text = "Lokal transkribieren … Abbrechen ist möglich.";
            var text = await new Transcriber().TranscribeAsync(AppPaths.Model, pcm, lease.Token);
            if (string.IsNullOrWhiteSpace(text.Text)) throw new InvalidDataException("Kein Sprachtext erkannt. Arbeitskopie bleibt erhalten.");
            if (text.Text.Length > 24000) throw new InvalidDataException("Ergebnis überschreitet die Textgrenze. Bitte kürzere Aufnahme verwenden.");
            result.Text = text.Text; // Keep visible even if saving fails.
            ProtectedFile.Write(recoveryPath, text.Text); saveFailed = false;
            if (!laboratory) { AppPaths.DeleteOwned(pending); pending = null; }
            info.Text = $"Transkript bereit · {text.Seconds:F1} Sekunden Verarbeitung. Kopieren oder zur Promptwerkstatt übernehmen.";
        }
        catch (OperationCanceledException) { info.Text = "Abgebrochen. Arbeitskopie bleibt für Wiederholung oder Verwerfen erhalten."; }
        catch (Exception ex) { info.Text = ex is InvalidDataException ? ex.Message : "Verarbeitung fehlgeschlagen. Arbeitskopie/Text bleibt erhalten. Codec, Modell, Speicher prüfen; WAV versuchen."; if (result.Text.Length > 0) saveFailed = true; }
        finally
        {
            TryDelete(pcm);
            working = false;
            result.ReadOnly = damagedRecovery;
        }
    }
    private void Play()
    {
        if (InProgress) { info.Text = "Zuerst aktuellen Vorgang abschließen."; return; }
        if (pending == null) { info.Text = "Keine Aufnahme oder Arbeitskopie zum Anhören vorhanden."; return; }
        playbackLease = Acquire("Sprachlabor · Wiedergabe"); if (playbackLease == null) return;
        try { playbackReader = new AudioFileReader(pending); player = new WaveOutEvent(); player.Init(playbackReader); player.Play(); info.Text = "Wiedergabe läuft. Abbrechen stoppt sie."; }
        catch { StopPlayback(); info.Text = "Wiedergabe fehlgeschlagen. Ausgabegerät/Codec prüfen."; }
        RefreshActions();
    }
    private void StopPlayback() { player?.Stop(); player?.Dispose(); player = null; playbackReader?.Dispose(); playbackReader = null; playbackLease?.Dispose(); playbackLease = null; if (!IsDisposed && !Disposing) RefreshActions(); }
    private void Discard()
    {
        if (InProgress) { info.Text = "Zuerst aktuellen Vorgang abschließen."; return; }
        if (pending == null) { info.Text = "Keine Aufnahme oder Arbeitskopie zum Verwerfen vorhanden."; return; }
        if (MessageBox.Show(this, "Arbeitskopie endgültig verwerfen? Deine Originaldatei bleibt erhalten.", "Verwerfen", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try { AppPaths.DeleteOwned(pending); pending = null; info.Text = "Arbeitskopie verworfen."; } catch { info.Text = "Löschen fehlgeschlagen. Erneut versuchen."; }
        RefreshActions();
    }
    private void ClearText()
    {
        if (damagedRecovery)
        {
            info.Text = DamagedMessage();
            return;
        }
        if (working) { info.Text = "Zuerst aktuellen Vorgang abschließen."; return; }
        if (result.Text.Length == 0) { info.Text = "Das Textfeld ist bereits leer."; return; }
        if (MessageBox.Show(this, "Diesen Text aus der Arbeitsansicht und Rettungsdatei löschen?", "Text leeren", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes) result.Clear();
    }
    private async Task CopyAsync()
    {
        if (working) { info.Text = "Zuerst aktuellen Vorgang abschließen."; return; }
        if (string.IsNullOrWhiteSpace(result.Text)) { info.Text = "Noch kein Text zum Kopieren vorhanden."; return; }
        using var lease = Acquire("Datei-Transkript kopieren"); if (lease == null) return;
        try { info.Text = await UnicodeClipboard.CopyAsync(Handle, result.Text, lease.Token) ? "Text kopiert · Strg+V zum Einfügen." : "Zwischenablage belegt. Text bleibt hier erhalten; erneut kopieren."; }
        catch (OperationCanceledException) { info.Text = "Kopieren abgebrochen. Text bleibt erhalten."; }
    }
    private static void TryDelete(string? path) { if (path != null) try { AppPaths.DeleteOwned(path); } catch (IOException) { } }
    public static double WordErrorRate(string expected, string actual)
    {
        static string[] Words(string s) => Regex.Matches(s.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value).Take(4000).ToArray();
        var a = Words(expected); var b = Words(actual); if (a.Length == 0) return b.Length == 0 ? 0 : 1;
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++) { var row = new int[b.Length + 1]; row[0] = i; for (int j = 1; j <= b.Length; j++) row[j] = Math.Min(Math.Min(previous[j] + 1, row[j - 1] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1)); previous = row; }
        return previous[b.Length] / (double)a.Length;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { gate.Changed -= RefreshActions; timer.Dispose(); StopPlayback(); recorder.Dispose(); recordingLease?.Dispose(); }
        base.Dispose(disposing);
    }
}
