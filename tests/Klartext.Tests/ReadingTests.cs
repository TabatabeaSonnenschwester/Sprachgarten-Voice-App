using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Klartext;
using NAudio.Wave;

internal static class ReadingTests
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static async Task<string?> ReadTestSelection(Control target)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var job = new WorkerJob();
        using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            ArgumentList = { "--read-test-selection", target.Handle.ToInt64().ToString() }
        })!;
        job.Add(process);
        string result = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        return process.ExitCode == 0 ? JsonSerializer.Deserialize<string>(result) : null;
    }
    public static int Run(string root, bool live)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles();
        string folder = Path.Combine(root, "artifacts", "reading", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder); AppPaths.Root = Path.Combine(folder, "profile"); AppPaths.Initialize();
        var results = new List<object>(); int failures = 0;
        void Check(string name, bool ok, object? evidence = null) {
            results.Add(new { name, status = ok ? "passed" : "failed", evidence });
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name} {JsonSerializer.Serialize(evidence)}");
            if (!ok) failures++;
        }
        using var host = new Form { Text = "Sprachgarten · Vorleseprüfung", ClientSize = new Size(800, 660), ShowInTaskbar = true };
        var gate = new ActivityGate(() => true);
        using var page = new ReadingPage(gate); host.Controls.Add(page);
        host.Shown += async (_, _) => {
            try {
                Check("reject-empty-or-oversize-without-worker", !LocalSpeech.ValidText(" ") && !LocalSpeech.ValidText(new string('a', 4001)) && !LocalSpeech.ValidText("a\0b"));
                var input = Field<GardenTextBox>(page, "input");
                var play = Field<GardenButton>(page, "play");
                Check("initial-play-disabled", !play.Enabled);
                input.Text = "Eine kurze künstliche Hörprobe.";
                Check("missing-setup-keeps-play-disabled", !play.Enabled && !Field<GardenButton>(page, "prepare").Enabled && Field<Label>(page, "setupHint").Text.Contains("docs/READING.md"));
                using (var lease = gate.TryEnter("Diktat-Test")) Check("dictation-blocks-reading", !play.Enabled);
                Check("gate-release-does-not-bypass-setup", !play.Enabled);
                using (var picture = new Bitmap(host.ClientSize.Width, host.ClientSize.Height)) { host.DrawToBitmap(picture, host.ClientRectangle); picture.Save(Path.Combine(folder, "reading-800.png")); }
                Check("actions-within-viewport", Field<GardenButton>(page, "stop").PointToScreen(Point.Empty).Y < host.PointToScreen(new Point(0, host.ClientSize.Height)).Y);
                page.Visible = false;
                using (var target = new RichTextBox { Dock = DockStyle.Fill, Text = "Anfang. Nur dieser künstliche Satz. Ende." }) {
                    host.Controls.Add(target); target.BringToFront(); host.Activate(); target.Focus();
                    await Task.Delay(200);
                    host.Activate(); target.Focus();
                    target.Select(8, 26);
                    // Explicit HWND avoids reading any private foreground window in an unattended run.
                    Check("selection-provider-reads-exact-range", await ReadTestSelection(target) == target.SelectedText);
                    target.Select(0, 0);
                    Check("selection-provider-rejects-empty-range", await ReadTestSelection(target) == null);
                    host.Controls.Remove(target);
                }
                using (var password = new TextBox { Dock = DockStyle.Top, UseSystemPasswordChar = true, Text = "YOUR_PASSWORD" }) {
                    host.Controls.Add(password); password.BringToFront(); password.Focus(); password.SelectAll();
                    Check("selection-provider-rejects-password", await ReadTestSelection(password) == null);
                    host.Controls.Remove(password);
                }
                page.Visible = true;
                if (live) {
                    var runtime = new SpeechRuntime(Path.Combine(root, ".tools/pocket-tts-env/Scripts/python.exe"), Path.Combine(root, ".tools/tts-experiment/pocket-german"));
                    Directory.CreateDirectory(Path.GetDirectoryName(SpeechRuntime.VoicePath)!);
                    string reference = Environment.GetEnvironmentVariable("SPRACHGARTEN_TEST_VOICE") ?? throw new InvalidOperationException("Für den hörbaren Test SPRACHGARTEN_TEST_VOICE auf eine eigene freigegebene WAV setzen.");
                    File.Copy(reference, SpeechRuntime.VoicePath);
                    var speech = Field<LocalSpeech>(page, "speech");
                    var watch = Stopwatch.StartNew();
                    await speech.PrepareAsync(runtime, CancellationToken.None);
                    Check("real-worker-ready", speech.Ready, new { seconds = watch.Elapsed.TotalSeconds });
                    int pid = speech.WorkerId!.Value;
                    using (var player = new SpeechPlayer()) {
                        watch.Restart(); double firstAudio = 0;
                        var generation = speech.GenerateAsync("Dies ist eine künstliche Hörprobe. Wir prüfen jetzt Pause, Fortsetzen und die lokale Wiedergabe.", async bytes => {
                            if (firstAudio == 0) firstAudio = watch.Elapsed.TotalSeconds;
                            await player.AddAsync(bytes, CancellationToken.None);
                        }, CancellationToken.None);
                        while (!player.Started && !generation.IsCompleted) await Task.Delay(25);
                        player.TogglePause(); await Task.Delay(250);
                        Check("real-player-pauses", player.PlaybackState == PlaybackState.Paused);
                        player.TogglePause();
                        await generation; await player.FinishAsync(CancellationToken.None);
                        Check("real-stream-reaches-windows-audio", player.NonSilentSamplesRead > 24000 && player.PlaybackError == null,
                            new { firstAudio, player.NonSilentSamplesRead, seconds = watch.Elapsed.TotalSeconds });
                    }
                    using (var cancellation = new CancellationTokenSource()) {
                        int chunks = 0; bool cancelled = false;
                        try { await speech.GenerateAsync("Dieser längere künstliche Text wird gleich gestoppt. Danach soll dieselbe Stimme ohne neuen Kaltstart wieder bereitstehen.", bytes => {
                            if (++chunks == 3) cancellation.Cancel(); return Task.CompletedTask;
                        }, cancellation.Token); }
                        catch (OperationCanceledException) { cancelled = true; }
                        Check("cancel-ack-keeps-warm-worker", cancelled && speech.Ready && speech.WorkerId == pid, new { chunks });
                    }
                    long received = 0;
                    await speech.GenerateAsync("Ich bin wieder bereit.", bytes => { received += bytes.Length; return Task.CompletedTask; }, CancellationToken.None);
                    Check("new-request-after-cancel", received > 9600 && speech.WorkerId == pid);
                    string runtimeFile = Path.Combine(AppContext.BaseDirectory, "speech-runtime.json");
                    byte[]? previousRuntime = File.Exists(runtimeFile) ? File.ReadAllBytes(runtimeFile) : null;
                    try {
                        File.WriteAllText(runtimeFile, JsonSerializer.Serialize(runtime));
                        var reading = page.ReadAsync("Noch eine künstliche Probe für die Bedienelemente. Dieser Text wird gleich angehalten und beendet.");
                        var wait = Stopwatch.StartNew();
                        while (Field<SpeechPlayer?>(page, "player")?.Started != true && !reading.IsCompleted && wait.Elapsed.TotalSeconds < 15) await Task.Delay(30);
                        var uiPlayer = Field<SpeechPlayer?>(page, "player");
                        Check("reading-page-holds-shared-activity", page.Busy && gate.Busy && !play.Enabled && uiPlayer?.Started == true);
                        var pauseButton = Field<GardenButton>(page, "pause");
                        pauseButton.PerformClick();
                        Check("reading-page-pause-button", uiPlayer?.Paused == true && pauseButton.Text == "Weiter");
                        pauseButton.PerformClick();
                        Field<GardenButton>(page, "stop").PerformClick();
                        await reading;
                        Check("reading-page-stop-releases-gate-and-keeps-voice", !page.Busy && !gate.Busy && play.Enabled && speech.Ready && speech.WorkerId == pid && Field<Label>(page, "status").Text == "Vorlesen gestoppt.");
                    }
                    finally {
                        if (previousRuntime == null) File.Delete(runtimeFile); else File.WriteAllBytes(runtimeFile, previousRuntime);
                    }
                    speech.Dispose();
                    bool gone; try { using var p = Process.GetProcessById(pid); gone = p.WaitForExit(3000); } catch (ArgumentException) { gone = true; }
                    Check("worker-exits-with-owner", gone);
                }
            }
            catch (Exception e) { Check("unexpected", false, new { type = e.GetType().Name, e.Message, e.StackTrace }); }
            finally { File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); host.Close(); }
        };
        Application.Run(host); Console.WriteLine("Evidence: " + folder); return failures == 0 ? 0 : 1;
    }
}
