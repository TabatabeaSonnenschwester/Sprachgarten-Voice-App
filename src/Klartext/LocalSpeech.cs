using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NAudio.Wave;

namespace Klartext;

public sealed record SpeechRuntime(string Python, string Models)
{
    public static string VoicePath => Path.Combine(AppPaths.Root, "voice", "reference.wav");
    public static SpeechRuntime? Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "speech-runtime.json");
        if (!File.Exists(path)) return null;
        var value = JsonSerializer.Deserialize<SpeechRuntime>(File.ReadAllText(path));
        return value != null && File.Exists(value.Python) && File.Exists(Path.Combine(value.Models, "model.safetensors")) ? value : null;
    }
}

public sealed class LocalSpeech : IDisposable
{
    private readonly Func<ProcessStartInfo>? workerFactory;
    private readonly TimeSpan prepareTimeout, idleTimeout, cancelTimeout;
    public LocalSpeech() : this(null, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(3)) { }
    internal LocalSpeech(Func<ProcessStartInfo>? workerFactory, TimeSpan prepareTimeout, TimeSpan idleTimeout, TimeSpan cancelTimeout)
    { this.workerFactory = workerFactory; this.prepareTimeout = prepareTimeout; this.idleTimeout = idleTimeout; this.cancelTimeout = cancelTimeout; }
    private Process? process;
    private WorkerJob? job;
    private readonly SemaphoreSlim writes = new(1);
    public bool Ready { get; private set; }
    public int? WorkerId => process is { HasExited: false } ? process.Id : null;
    public static bool ValidText(string text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 4000 && !text.Contains('\0');
    private async Task SendAsync(Process target, object command)
    {
        await writes.WaitAsync();
        try { await target.StandardInput.WriteLineAsync(JsonSerializer.Serialize(command)); await target.StandardInput.FlushAsync(); }
        finally { writes.Release(); }
    }
    private async Task<JsonDocument> ReadAsync(CancellationToken token)
    {
        string? line = await process!.StandardOutput.ReadLineAsync(token);
        if (line == null || line.Length > 131072) throw new IOException("Die lokale Vorlesestimme hat nicht geantwortet.");
        return JsonDocument.Parse(line);
    }
    public async Task PrepareAsync(SpeechRuntime runtime, CancellationToken token)
    {
        if (Ready && WorkerId != null) return;
        DisposeWorker();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(prepareTimeout);
        try
        {
            token.ThrowIfCancellationRequested();
            if (workerFactory == null) SpeechSetup.Validate(runtime);
            string config = Path.Combine(AppPaths.Root, "voice", "german-local.yaml");
            Directory.CreateDirectory(Path.GetDirectoryName(config)!);
            var start = new ProcessStartInfo(runtime.Python) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add("-u"); start.ArgumentList.Add("-B");
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Speech", "speech_worker.py"));
            start.Environment["HF_HUB_OFFLINE"] = "1";
            start.Environment["HF_HUB_DISABLE_TELEMETRY"] = "1";
            if (workerFactory != null) start = workerFactory();
            job = new WorkerJob(); process = Process.Start(start) ?? throw new IOException("Vorlesestimme konnte nicht starten.");
            job.Add(process);
            // Drain diagnostics without storing or displaying private library output.
            process.ErrorDataReceived += (_, _) => { }; process.BeginErrorReadLine();
            await SendAsync(process, new { type = "init", models = Path.GetFullPath(runtime.Models), voice = SpeechRuntime.VoicePath, config });
            using var reply = await ReadAsync(timeout.Token);
            if (reply.RootElement.GetProperty("type").GetString() != "ready" || reply.RootElement.GetProperty("sampleRate").GetInt32() != 24000)
                throw new IOException("Stimme konnte nicht vorbereitet werden. Bitte eine klare WAV-Aufnahme (2–30 Sekunden) wählen und das lokale Vorlesepaket prüfen.");
            Ready = true;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { DisposeWorker(); throw new TimeoutException("Die Vorlesestimme antwortet bei der Vorbereitung nicht. Bitte erneut versuchen."); }
        catch { DisposeWorker(); throw; }
    }
    public async Task GenerateAsync(string text, Func<byte[], Task> audio, CancellationToken token)
    {
        if (!Ready || !ValidText(text)) throw new InvalidOperationException("Bitte 1 bis 4000 Zeichen zum Vorlesen eingeben.");
        token.ThrowIfCancellationRequested();
        string id = Guid.NewGuid().ToString("N");
        var worker = process!;
        using var abort = new CancellationTokenSource();
        await SendAsync(worker, new { type = "speak", id, text });
        Task cancelSend = Task.CompletedTask;
        using var registration = token.Register(() => {
            abort.CancelAfter(cancelTimeout);
            cancelSend = SendCancelAsync(worker, id);
        });
        try
        {
            while (true)
            {
                // Start a fresh watchdog only while waiting for the worker, never during playback backpressure.
                using var message = await ReadWithTimeoutAsync(abort.Token);
                var root = message.RootElement;
                if (!root.TryGetProperty("id", out var messageId) || messageId.GetString() != id) throw new IOException("Vorlese-Antwort passt nicht zum Auftrag.");
                string? kind = root.GetProperty("type").GetString();
                if (kind == "done") { token.ThrowIfCancellationRequested(); return; }
                if (kind != "audio") throw new IOException("Die lokale Spracherzeugung ist fehlgeschlagen. Bitte erneut versuchen.");
                if (token.IsCancellationRequested) continue;
                byte[] bytes = Convert.FromBase64String(root.GetProperty("data").GetString()!);
                if (bytes.Length == 0 || bytes.Length % 4 != 0 || bytes.Length > 96000) throw new IOException("Ungültige Audiodaten.");
                for (int i = 0; i < bytes.Length; i += 4)
                    if (!float.IsFinite(BitConverter.ToSingle(bytes, i))) throw new IOException("Ungültige Audiodaten.");
                try { await audio(bytes); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { if (abort.IsCancellationRequested) DisposeWorker(); throw; }
        catch (TimeoutException) when (token.IsCancellationRequested)
        { DisposeWorker(); throw new OperationCanceledException(token); }
        catch { DisposeWorker(); throw; }
        finally { registration.Dispose(); await cancelSend; }
    }
    private async Task<JsonDocument> ReadWithTimeoutAsync(CancellationToken abort)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(abort);
        idle.CancelAfter(idleTimeout);
        try { return await ReadAsync(idle.Token); }
        catch (OperationCanceledException) when (!abort.IsCancellationRequested)
        { throw new TimeoutException("Die Vorlesestimme antwortet nicht mehr. Dein Text bleibt erhalten. Bitte erneut versuchen."); }
    }
    private async Task SendCancelAsync(Process worker, string id) { try { await SendAsync(worker, new { type = "cancel", id }); } catch { } }
    private void DisposeWorker()
    {
        Ready = false;
        // The kill-on-close job already owns the complete process tree. Avoid the
        // expensive OS-wide descendant scan of Process.Kill(entireProcessTree: true).
        job?.Dispose(); job = null;
        try { if (process is { HasExited: false }) process.Kill(); } catch { }
        process?.Dispose(); process = null;
    }
    public void Reset() => DisposeWorker();
    public void Dispose() => DisposeWorker();
}

public sealed class SpeechPlayer : IDisposable
{
    private sealed class Meter(BufferedWaveProvider source) : IWaveProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;
        public long NonSilentSamples;
        public int Read(byte[] target, int offset, int count)
        {
            int read = source.Read(target, offset, count);
            long nonSilent = 0;
            for (int i = offset; i + 4 <= offset + read; i += 4) if (Math.Abs(BitConverter.ToSingle(target, i)) > .001f) nonSilent++;
            Interlocked.Add(ref NonSilentSamples, nonSilent);
            return read;
        }
    }
    private readonly BufferedWaveProvider buffer = new(WaveFormat.CreateIeeeFloatWaveFormat(24000, 1)) {
        BufferDuration = TimeSpan.FromSeconds(6), ReadFully = true, DiscardOnBufferOverflow = false
    };
    private readonly WaveOutEvent output = new() { DesiredLatency = 120 };
    private bool started, stopped;
    private readonly Meter meter;
    public bool Paused { get; private set; }
    public int BufferedBytes => buffer.BufferedBytes;
    public bool Started => started;
    public PlaybackState PlaybackState => output.PlaybackState;
    public long NonSilentSamplesRead => Interlocked.Read(ref meter.NonSilentSamples);
    public Exception? PlaybackError { get; private set; }
    public SpeechPlayer()
    {
        meter = new Meter(buffer); output.Init(meter);
        output.PlaybackStopped += (_, e) => { if (e.Exception != null) PlaybackError = e.Exception; };
    }
    public async Task AddAsync(byte[] bytes, CancellationToken token)
    {
        while (buffer.BufferedBytes + bytes.Length > buffer.BufferLength)
        { token.ThrowIfCancellationRequested(); if (PlaybackError != null) throw new IOException("Audiogerät nicht mehr verfügbar.", PlaybackError); if (stopped) throw new OperationCanceledException(); await Task.Delay(20, token); }
        token.ThrowIfCancellationRequested(); if (stopped) throw new OperationCanceledException();
        if (PlaybackError != null) throw new IOException("Audiogerät nicht mehr verfügbar.", PlaybackError);
        buffer.AddSamples(bytes, 0, bytes.Length);
        if (!started && buffer.BufferedDuration.TotalSeconds >= .6) Start();
    }
    private void Start() { if (started || stopped) return; started = true; if (!Paused) output.Play(); }
    public void TogglePause() { Paused = !Paused; if (Paused) output.Pause(); else if (started) output.Play(); }
    public async Task FinishAsync(CancellationToken token)
    {
        Start();
        while (buffer.BufferedBytes > 0 || Paused)
        { token.ThrowIfCancellationRequested(); if (PlaybackError != null) throw new IOException("Audiogerät nicht mehr verfügbar.", PlaybackError); await Task.Delay(30, token); }
        await Task.Delay(150, token); // Let the last queued device buffer play before disposing it.
    }
    public void Stop() { stopped = true; output.Stop(); buffer.ClearBuffer(); }
    public void Dispose() { Stop(); output.Dispose(); }
}
