using NAudio.Wave;

namespace Klartext;

public sealed class ReadingPage : UserControl
{
    private readonly ActivityGate gate;
    private readonly LocalSpeech speech;
    private readonly Func<(SpeechRuntime? Runtime, string? Error)> setup;
    private readonly Func<bool> confirmReplacement;
    private readonly Func<bool> confirmRestore;
    private readonly Func<Task<string?>> readSelection;
    private string? previousText, insertedText;
    private bool readingSelection;
    private readonly Label setupHint = WorkshopUi.Hint("");
    private CancellationTokenSource? warmupCancellation;
    private Task warmupTask = Task.CompletedTask;
    private Exception? warmupFailure;
    private bool claimingWarmup;
    public bool Warming => warmupCancellation != null;
    private readonly GardenTextBox input = WorkshopUi.Grow("Text zum Vorlesen", 7);
    private readonly Label status = WorkshopUi.Hint("Stimmaufnahme auswählen und einmal vorbereiten. Alles bleibt auf diesem Rechner.");
    private readonly Label shortcut = WorkshopUi.Hint("Markierten Text mit Strg+Alt+V vorlesen. Erneutes Drücken stoppt das Vorlesen.");
    private readonly GardenButton play = new() { Text = "Vorlesen", Primary = true };
    private readonly GardenButton pause = new() { Text = "Pause" };
    private readonly GardenButton stop = new() { Text = "Stopp" };
    private readonly GardenButton prepare = new() { Text = "Stimme vorbereiten" };
    private readonly GardenButton choose = new() { Text = "Stimmaufnahme auswählen …" };
    private readonly GardenButton paste = new() { Text = "Zwischenablage einfügen" };
    private readonly GardenButton restoreText = new() { Text = "Vorherigen Text zurückholen" };
    private readonly GardenButton help = new() { Text = "Hilfe zum Vorlesen" };
    private CancellationTokenSource? operation;
    private SpeechPlayer? player;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    private bool preparing, selecting;
    public bool Busy => operation != null;
    public bool VoiceReady => speech.Ready;
    public event Action? ShowRequested;

    public ReadingPage(ActivityGate gate) : this(gate, new LocalSpeech(), SpeechSetup.Check) { }
    internal ReadingPage(ActivityGate gate, LocalSpeech speech, Func<(SpeechRuntime? Runtime, string? Error)> setup, Func<bool>? confirmReplacement = null, Func<Task<string?>>? readSelection = null)
    {
        this.gate = gate; this.speech = speech; this.setup = setup; Dock = DockStyle.Fill;
        this.readSelection = readSelection ?? SelectedText.ReadAsync;
        this.confirmReplacement = confirmReplacement ?? (() => MessageBox.Show(this,
            "Dein eingegebener Vorlesetext wird ersetzt. Möchtest du das?\n\nMit Nein bleibt dein Text erhalten. Nach dem Ersetzen kannst du ihn in dieser Sitzung zurückholen.",
            "Vorlesetext ersetzen?", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
        confirmRestore = confirmReplacement ?? (() => MessageBox.Show(this,
            "Du hast den eingefügten Text inzwischen bearbeitet. Möchtest du diese Änderungen verwerfen und den vorherigen Text zurückholen?\n\nDie aktuelle Fassung kann danach nicht zurückgeholt werden. Mit Nein bleibt sie erhalten.",
            "Vorherigen Text zurückholen?", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
        input.MaxLength = 4000;
        var surface = new GardenScrollFlow { Dock = DockStyle.Fill };
        var textCard = new GardenCardFlow();
        textCard.Controls.Add(WorkshopUi.Hint("Text eingeben oder bewusst einfügen · bis 4000 Zeichen"));
        textCard.Controls.Add(setupHint);
        textCard.Controls.Add(input);
        textCard.Controls.Add(WorkshopUi.Row(paste, play, pause, stop));
        textCard.Controls.Add(WorkshopUi.Row(restoreText, help));
        textCard.Controls.Add(status);
        var voiceCard = new GardenCardFlow();
        voiceCard.Controls.Add(WorkshopUi.Hint("Meine lokale Vorlesestimme"));
        voiceCard.Controls.Add(WorkshopUi.Hint("Wähle 2–30 Sekunden deiner Stimme als WAV ohne Musik oder andere Stimmen. Sprachgarten behält nur diese bewusst gewählte Stimmvorlage; vorgelesene Texte und Ausgaben werden nicht gespeichert."));
        voiceCard.Controls.Add(WorkshopUi.Row(choose, prepare));
        voiceCard.Controls.Add(shortcut);
        surface.Controls.Add(textCard); surface.Controls.Add(voiceCard);
        WorkshopUi.Fit(surface); Controls.Add(surface);
        input.TextChanged += (_, _) => RefreshActions();
        paste.Click += (_, _) => {
            try {
                if (!Clipboard.ContainsText(TextDataFormat.UnicodeText)) { Notice("Die Zwischenablage enthält keinen Text."); return; }
                string text = Clipboard.GetText(TextDataFormat.UnicodeText);
                if (!LocalSpeech.ValidText(text)) { Notice("Bitte höchstens 4000 Zeichen kopieren. Dein bisheriger Text bleibt erhalten."); return; }
                PasteText(text);
            } catch { Notice("Zwischenablage gerade nicht verfügbar. Bitte erneut versuchen."); }
        };
        play.Click += async (_, _) => await ReadAsync(input.Text);
        prepare.Click += async (_, _) => await PrepareAsync();
        stop.Click += (_, _) => Stop();
        pause.Click += (_, _) => { player?.TogglePause(); RefreshActions(); };
        choose.Click += (_, _) => ChooseVoice();
        restoreText.Click += (_, _) => RestoreText();
        help.Click += (_, _) => { using var dialog = new ReadingHelpDialog(); dialog.ShowDialog(this); };
        gate.Changed += GateChanged;
        timer.Tick += (_, _) => {
            if (player != null && Busy) status.Text = (readingSelection ? "Textmarkierung · dein Eingabetext bleibt erhalten. " : "") + (player.Paused ? "Pausiert · mit „Weiter“ fortsetzen" : player.Started ? "Wird lokal vorgelesen …" : "Erste Sprache wird vorbereitet …");
        };
        timer.Start(); RefreshActions();
    }
    internal bool PasteText(string text)
    {
        if (Busy || gate.Busy || !LocalSpeech.ValidText(text)) return false;
        using var lease = Claim("Vorlesetext einfügen");
        if (lease == null) return false;
        string old = input.Text;
        if (text == old) return true;
        if (old.Length > 0 && !confirmReplacement()) { Notice("Dein Vorlesetext bleibt erhalten."); return false; }
        if (IsDisposed || lease.Token.IsCancellationRequested || input.Text != old) return false;
        previousText = old.Length > 0 ? old : null; insertedText = text;
        input.Text = text; Notice("Text eingefügt." + (previousText != null ? " Der vorherige Text kann in dieser Sitzung zurückgeholt werden." : ""));
        RefreshActions(); return true;
    }
    internal bool RestoreText()
    {
        if (Busy || gate.Busy || previousText == null) return false;
        using var lease = Claim("Vorlesetext zurückholen");
        if (lease == null) return false;
        string old = input.Text;
        if (old.Length > 0 && old != insertedText && !confirmRestore()) return false;
        if (IsDisposed || lease.Token.IsCancellationRequested || input.Text != old) return false;
        string restored = previousText; previousText = insertedText = null;
        input.Text = restored; Notice("Dein vorheriger Vorlesetext ist wieder da."); RefreshActions(); return true;
    }
    public void SetHotkeyAvailable(bool available) => shortcut.Text = available
        ? "Strg+Alt+V liest die Textmarkierung, ohne deinen Eingabetext zu ersetzen. Erneutes Drücken stoppt das Vorlesen. Falls keine Auswahl verfügbar ist: Strg+C, hier „Zwischenablage einfügen“ und „Vorlesen“."
        : "Strg+Alt+V ist belegt. Hier Text einfügen und „Vorlesen“ wählen. Nach Schließen der anderen Sprachgarten-Ausgabe neu starten, um den Hotkey zu aktivieren.";
    public void Notice(string text) { if (!IsDisposed) status.Text = text; }
    public void Stop() { warmupCancellation?.Cancel(); operation?.Cancel(); player?.Stop(); }
    private void GateChanged() { if (gate.Busy && !claimingWarmup) warmupCancellation?.Cancel(); RefreshActions(); }
    public Task WarmAsync()
    {
        if (IsDisposed || Busy || Warming || gate.Busy || setup().Error != null) return Task.CompletedTask;
        warmupFailure = null;
        var cancellation = new CancellationTokenSource(); warmupCancellation = cancellation;
        warmupTask = WarmCoreAsync(cancellation); return warmupTask;
    }
    private async Task WarmCoreAsync(CancellationTokenSource cancellation)
    {
        try { Notice("Stimme wird im Hintergrund vorbereitet · Diktieren ist möglich."); RefreshActions(); await speech.PrepareAsync(RequireRuntime(), cancellation.Token); if (!Busy) Notice("Deine Stimme ist bereit."); }
        catch (Exception e) { warmupFailure = e; if (!Busy && !IsDisposed) Notice(e is OperationCanceledException ? "Vorbereitung unterbrochen · startet beim nächsten Vorlesen." : FailureNotice(e)); }
        finally { warmupCancellation = null; cancellation.Dispose(); RefreshActions(); }
    }
    public async Task StopWarmupAsync()
    {
        var pendingWarmup = warmupTask; warmupCancellation?.Cancel(); await pendingWarmup;
    }
    private async Task EnsureVoiceAsync(CancellationToken token)
    {
        if (Warming)
        {
            var cancellation = warmupCancellation!;
            using var registration = token.Register(() => { try { cancellation.Cancel(); } catch (ObjectDisposedException) { } });
            await warmupTask; token.ThrowIfCancellationRequested();
            if (warmupFailure != null && warmupFailure is not OperationCanceledException) throw warmupFailure;
        }
        token.ThrowIfCancellationRequested();
        if (IsDisposed) throw new OperationCanceledException(token);
        await speech.PrepareAsync(RequireRuntime(), token);
    }
    private ActivityGate.Lease? Claim(string label)
    {
        claimingWarmup = true;
        try { return gate.TryEnter(label); }
        finally { claimingWarmup = false; }
    }
    private static string FailureNotice(Exception e) => e is IOException or TimeoutException ? e.Message : "Vorlesen fehlgeschlagen. Bitte Audiogerät, Stimmvorlage und docs/READING.md im App-Ordner prüfen.";
    private void RefreshActions()
    {
        if (IsDisposed) return;
        bool free = !Busy && !gate.Busy;
        var configuration = setup();
        setupHint.Text = configuration.Error ?? "Vorlesepaket und Stimmaufnahme sind eingerichtet · vollständig lokal.";
        play.Enabled = free && configuration.Error == null && LocalSpeech.ValidText(input.Text);
        input.ReadOnly = Busy || gate.Busy; paste.Enabled = free; choose.Enabled = free && !Warming; prepare.Enabled = free && !Warming && configuration.Error == null;
        restoreText.Visible = previousText != null; restoreText.Enabled = free && previousText != null;
        stop.Enabled = Busy || Warming; pause.Enabled = player != null && Busy && !preparing;
        pause.Text = player?.Paused == true ? "Weiter" : "Pause";
    }
    private SpeechRuntime RequireRuntime()
    {
        var configuration = setup();
        return configuration.Error == null && configuration.Runtime != null ? configuration.Runtime : throw new IOException(configuration.Error ?? "Vorlesepaket fehlt. Bitte docs/READING.md prüfen.");
    }
    public async Task PrepareAsync()
    {
        if (Busy) return;
        if (setup().Error is string setupError) { Notice(setupError); return; }
        using var lease = Claim("Vorlesestimme wird vorbereitet");
        if (lease == null) { Notice("Bitte den laufenden Vorgang zuerst abschließen."); return; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lease.Token);
        operation = cancellation; preparing = true; RefreshActions();
        try { Notice("Stimme wird einmalig vorbereitet …"); await EnsureVoiceAsync(cancellation.Token); Notice("Deine Stimme ist bereit · Text eingeben und vorlesen lassen."); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Notice("Vorbereitung gestoppt."); }
        catch (Exception e) { Notice(FailureNotice(e)); }
        finally { operation = null; preparing = false; RefreshActions(); }
    }
    public async Task ReadAsync(string text)
    {
        if (Busy || !LocalSpeech.ValidText(text)) { if (!Busy) Notice("Bitte 1 bis 4000 Zeichen zum Vorlesen eingeben."); return; }
        if (setup().Error is string setupError) { Notice(setupError); return; }
        using var lease = Claim("Vorlesen");
        if (lease == null) { Notice("Bitte den laufenden Vorgang zuerst abschließen."); return; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lease.Token);
        // The editor belongs to the user. Reading a selection must never replace it.
        operation = cancellation; readingSelection = text != input.Text; preparing = true; RefreshActions();
        try
        {
            Notice((readingSelection ? "Textmarkierung · dein Eingabetext bleibt erhalten. " : "") + (speech.Ready ? "Vorlesen startet …" : "Stimme wird einmalig vorbereitet …"));
            await EnsureVoiceAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            player = new SpeechPlayer(); preparing = false; RefreshActions();
            using var stopRegistration = cancellation.Token.Register(() => player?.Stop());
            await speech.GenerateAsync(text, bytes => player.AddAsync(bytes, cancellation.Token), cancellation.Token);
            await player.FinishAsync(cancellation.Token);
            Notice(readingSelection ? "Textmarkierung fertig vorgelesen · dein Eingabetext bleibt erhalten." : "Fertig vorgelesen · die Stimme bleibt bereit.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Notice("Vorlesen gestoppt."); }
        catch (Exception e) { Notice(FailureNotice(e)); }
        finally { player?.Dispose(); player = null; operation = null; preparing = false; readingSelection = false; RefreshActions(); }
    }
    private void ChooseVoice()
    {
        if (Busy || Warming || gate.Busy) return;
        using var dialog = new OpenFileDialog { Title = "Deine Stimmvorlage auswählen (2–30 Sekunden)", Filter = "WAV-Aufnahme|*.wav", Multiselect = false };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 12_000_000) throw new IOException("Bitte eine kürzere WAV-Aufnahme wählen (höchstens 30 Sekunden).");
            using (var wave = new WaveFileReader(dialog.FileName)) {
                if (wave.TotalTime.TotalSeconds is < 2 or > 30 || wave.WaveFormat.Channels is < 1 or > 2 ||
                    !((wave.WaveFormat.Encoding == WaveFormatEncoding.Pcm && wave.WaveFormat.BitsPerSample == 16) || (wave.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat && wave.WaveFormat.BitsPerSample == 32)))
                    throw new IOException("Bitte eine WAV-Aufnahme mit 2–30 Sekunden wählen (16-Bit PCM oder 32-Bit Float, Mono/Stereo).");
            }
            string path = SpeechRuntime.VoicePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(dialog.FileName, path + ".tmp", true);
            File.Move(path + ".tmp", path, true);
            speech.Reset(); RefreshActions(); Notice("Stimmvorlage lokal übernommen. Jetzt „Stimme vorbereiten“ wählen.");
        }
        catch (Exception e) { Notice(e is IOException ? e.Message : "Stimmvorlage konnte nicht übernommen werden. Die bisherige Vorlage bleibt erhalten."); }
    }
    public async Task ReadSelectionAsync()
    {
        if (Busy) { Stop(); return; }
        if (selecting) return;
        if (gate.Busy) { Notice("Bitte den laufenden Vorgang zuerst abschließen."); ShowRequested?.Invoke(); return; }
        string? text;
        selecting = true;
        try {
            using var selectionLease = Claim("Textmarkierung lesen");
            if (selectionLease == null) { Notice("Bitte den laufenden Vorgang zuerst abschließen."); return; }
            text = await readSelection();
            if (selectionLease.Token.IsCancellationRequested) return;
        }
        finally { selecting = false; }
        ShowRequested?.Invoke();
        if (text == null) { Notice("Keine unterstützte Textmarkierung gefunden. Bitte Text mit Strg+C kopieren und hier „Zwischenablage einfügen“ wählen."); return; }
        await ReadAsync(text);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); gate.Changed -= GateChanged; Stop(); speech.Dispose(); player?.Dispose(); player = null; }
        base.Dispose(disposing);
    }
}
