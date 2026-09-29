using NAudio.Wave;

namespace Klartext;

public sealed class ReadingPage : UserControl
{
    private readonly ActivityGate gate;
    private readonly LocalSpeech speech = new();
    private readonly GardenTextBox input = WorkshopUi.Grow("Text zum Vorlesen", 7);
    private readonly Label status = WorkshopUi.Hint("Stimmaufnahme auswählen und einmal vorbereiten. Alles bleibt auf diesem Rechner.");
    private readonly Label shortcut = WorkshopUi.Hint("Markierten Text mit Strg+Alt+V vorlesen. Erneutes Drücken stoppt das Vorlesen.");
    private readonly GardenButton play = new() { Text = "Vorlesen", Primary = true };
    private readonly GardenButton pause = new() { Text = "Pause" };
    private readonly GardenButton stop = new() { Text = "Stopp" };
    private readonly GardenButton prepare = new() { Text = "Stimme vorbereiten" };
    private readonly GardenButton choose = new() { Text = "Stimmaufnahme auswählen …" };
    private readonly GardenButton paste = new() { Text = "Zwischenablage einfügen" };
    private CancellationTokenSource? operation;
    private SpeechPlayer? player;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    private bool preparing, selecting;
    public bool Busy => operation != null;
    public bool VoiceReady => speech.Ready;
    public event Action? ShowRequested;

    public ReadingPage(ActivityGate gate)
    {
        this.gate = gate; Dock = DockStyle.Fill;
        input.MaxLength = 4000;
        var surface = new GardenScrollFlow { Dock = DockStyle.Fill };
        var textCard = new GardenCardFlow();
        textCard.Controls.Add(WorkshopUi.Hint("Text eingeben oder bewusst einfügen · bis 4000 Zeichen"));
        textCard.Controls.Add(input);
        textCard.Controls.Add(WorkshopUi.Row(paste, play, pause, stop));
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
                input.Text = text;
            } catch { Notice("Zwischenablage gerade nicht verfügbar. Bitte erneut versuchen."); }
        };
        play.Click += async (_, _) => await ReadAsync(input.Text);
        prepare.Click += async (_, _) => await PrepareAsync();
        stop.Click += (_, _) => Stop();
        pause.Click += (_, _) => { player?.TogglePause(); RefreshActions(); };
        choose.Click += (_, _) => ChooseVoice();
        gate.Changed += RefreshActions;
        timer.Tick += (_, _) => {
            if (player != null && Busy) status.Text = player.Paused ? "Pausiert · mit „Weiter“ fortsetzen" : player.Started ? "Wird lokal vorgelesen …" : "Erste Sprache wird vorbereitet …";
        };
        timer.Start(); RefreshActions();
    }
    public void SetHotkeyAvailable(bool available) => shortcut.Text = available
        ? "Markierten Text mit Strg+Alt+V vorlesen. Erneutes Drücken stoppt das Vorlesen. Falls keine Auswahl verfügbar ist: Strg+C, hier „Zwischenablage einfügen“ und „Vorlesen“."
        : "Strg+Alt+V ist belegt. Hier Text einfügen und „Vorlesen“ wählen. Nach Schließen der anderen Sprachgarten-Ausgabe neu starten, um den Hotkey zu aktivieren.";
    public void Notice(string text) { if (!IsDisposed) status.Text = text; }
    public void Stop() { operation?.Cancel(); player?.Stop(); }
    private void RefreshActions()
    {
        if (IsDisposed) return;
        bool free = !Busy && !gate.Busy;
        play.Enabled = free && LocalSpeech.ValidText(input.Text);
        input.ReadOnly = Busy; paste.Enabled = free; choose.Enabled = free; prepare.Enabled = free;
        stop.Enabled = Busy; pause.Enabled = player != null && Busy && !preparing;
        pause.Text = player?.Paused == true ? "Weiter" : "Pause";
    }
    private SpeechRuntime RequireRuntime() => SpeechRuntime.Load() ?? throw new IOException("Das lokale Vorlesepaket fehlt. Bitte die eingerichtete Sprachgarten-Ausgabe auf diesem Rechner verwenden.");
    public async Task PrepareAsync()
    {
        if (Busy) return;
        using var lease = gate.TryEnter("Vorlesestimme wird vorbereitet");
        if (lease == null) { Notice("Bitte den laufenden Vorgang zuerst abschließen."); return; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lease.Token);
        operation = cancellation; preparing = true; RefreshActions();
        try { Notice("Stimme wird einmalig vorbereitet …"); await speech.PrepareAsync(RequireRuntime(), cancellation.Token); Notice("Deine Stimme ist bereit · Text eingeben und vorlesen lassen."); }
        catch (OperationCanceledException) { Notice("Vorbereitung gestoppt."); }
        catch (Exception e) { Notice(e is IOException ? e.Message : "Stimme konnte nicht vorbereitet werden. Bitte Stimmaufnahme und lokales Vorlesepaket prüfen."); }
        finally { operation = null; preparing = false; RefreshActions(); }
    }
    public async Task ReadAsync(string text)
    {
        if (Busy || !LocalSpeech.ValidText(text)) { if (!Busy) Notice("Bitte 1 bis 4000 Zeichen zum Vorlesen eingeben."); return; }
        using var lease = gate.TryEnter("Vorlesen");
        if (lease == null) { Notice("Bitte den laufenden Vorgang zuerst abschließen."); return; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lease.Token);
        operation = cancellation; input.Text = text; preparing = true; RefreshActions();
        try
        {
            Notice(speech.Ready ? "Vorlesen startet …" : "Stimme wird einmalig vorbereitet …");
            await speech.PrepareAsync(RequireRuntime(), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            player = new SpeechPlayer(); preparing = false; RefreshActions();
            using var stopRegistration = cancellation.Token.Register(() => player?.Stop());
            await speech.GenerateAsync(text, bytes => player.AddAsync(bytes, cancellation.Token), cancellation.Token);
            await player.FinishAsync(cancellation.Token);
            Notice("Fertig vorgelesen · die Stimme bleibt bereit.");
        }
        catch (OperationCanceledException) { Notice("Vorlesen gestoppt."); }
        catch (Exception e) { Notice(e is IOException ? e.Message : "Vorlesen fehlgeschlagen. Bitte Audiogerät und Stimmvorlage prüfen und erneut versuchen."); }
        finally { player?.Dispose(); player = null; operation = null; preparing = false; RefreshActions(); }
    }
    private void ChooseVoice()
    {
        if (Busy || gate.Busy) return;
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
            speech.Reset(); Notice("Stimmvorlage lokal übernommen. Jetzt „Stimme vorbereiten“ wählen.");
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
            using var selectionLease = gate.TryEnter("Textmarkierung lesen");
            if (selectionLease == null) { Notice("Bitte den laufenden Vorgang zuerst abschließen."); return; }
            text = await SelectedText.ReadAsync();
            if (selectionLease.Token.IsCancellationRequested) return;
        }
        finally { selecting = false; }
        ShowRequested?.Invoke();
        if (text == null) { Notice("Keine unterstützte Textmarkierung gefunden. Bitte Text mit Strg+C kopieren und hier „Zwischenablage einfügen“ wählen."); return; }
        await ReadAsync(text);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); gate.Changed -= RefreshActions; Stop(); speech.Dispose(); player?.Dispose(); player = null; }
        base.Dispose(disposing);
    }
}
