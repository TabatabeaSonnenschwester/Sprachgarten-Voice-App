using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Klartext;

internal static class ReviewFixTests
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
    internal static LocalSpeech Fake(string mode, int prepareMs = 3000, int idleMs = 400) => new(() => new ProcessStartInfo(Environment.ProcessPath!) {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        ArgumentList = { "--speech-fixture", mode }
    }, TimeSpan.FromMilliseconds(prepareMs), TimeSpan.FromMilliseconds(idleMs), TimeSpan.FromMilliseconds(400));
    private static bool Exited(int id) { try { using var process = Process.GetProcessById(id); return process.WaitForExit(3000); } catch (ArgumentException) { return true; } }
    private static readonly SpeechRuntime FixtureRuntime = new("fixture", "fixture");
    public static int Fixture(string mode)
    {
        // Synthetic JSON/float samples only; no model, microphone, network or private data.
        using var init = JsonDocument.Parse(Console.ReadLine()!);
        if (mode == "prepare-stall") { Thread.Sleep(Timeout.Infinite); return 0; }
        if (mode == "slow-prepare") Thread.Sleep(1500);
        object outputLock = new();
        void Emit(object value) { lock (outputLock) { Console.WriteLine(JsonSerializer.Serialize(value)); Console.Out.Flush(); } }
        Emit(new { type = "ready", sampleRate = 24000 });
        string? activeId = null; CancellationTokenSource? cancellation = null; Task active = Task.CompletedTask;
        while (Console.ReadLine() is string line)
        {
            using var request = JsonDocument.Parse(line);
            string? type = request.RootElement.GetProperty("type").GetString();
            if (type == "cancel")
            {
                if (mode != "ignore-cancel" && request.RootElement.GetProperty("id").GetString() == activeId) cancellation?.Cancel();
                continue;
            }
            if (type != "speak") continue;
            active.GetAwaiter().GetResult(); cancellation?.Dispose(); cancellation = new();
            activeId = request.RootElement.GetProperty("id").GetString()!; string id = activeId; var cancelSignal = cancellation.Token;
            active = Task.Run(async () => {
                try
                {
                    if (mode is "stall" or "ignore-cancel") await Task.Delay(Timeout.Infinite, cancelSignal);
                    for (int i = 0; i < 30; i++) { cancelSignal.ThrowIfCancellationRequested(); Emit(new { type = "audio", id, data = Convert.ToBase64String(new byte[9600]) }); await Task.Delay(10, cancelSignal); }
                }
                catch (OperationCanceledException) { }
                Emit(new { type = "done", id });
            });
        }
        cancellation?.Cancel(); return 0;
    }
    public static int Run(string root)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles();
        string folder = Path.Combine(root, "artifacts", "review-fixes", "cases-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        AppPaths.Root = Path.Combine(folder, "profile"); AppPaths.Initialize();
        int failures = 0; var results = new List<object>();
        void Check(string name, bool passed, object? detail = null)
        { Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name} {JsonSerializer.Serialize(detail)}"); results.Add(new { name, passed, detail }); if (!passed) failures++; }
        using var host = new Form { Text = "Sprachgarten · Review-Komponenten", ShowInTaskbar = false };
        host.Shown += async (_, _) => {
            try
            {
                using (var speech = Fake("stream"))
                {
                    await speech.PrepareAsync(FixtureRuntime, CancellationToken.None); int? pid = speech.WorkerId; int chunks = 0;
                    await speech.GenerateAsync("Synthetische Pause.", async _ => { if (++chunks == 2) await Task.Delay(1200); }, CancellationToken.None);
                    Check("pause-longer-than-watchdog-keeps-worker-and-finishes", chunks == 30 && speech.Ready && speech.WorkerId == pid, new { chunks });
                    using var cancel = new CancellationTokenSource(); bool stopped = false;
                    try { await speech.GenerateAsync("Synthetischer Stopp.", _ => { cancel.Cancel(); return Task.CompletedTask; }, cancel.Token); }
                    catch (OperationCanceledException) { stopped = true; }
                    Check("acknowledged-stop-keeps-warm-worker", stopped && speech.Ready && speech.WorkerId == pid);
                    chunks = 0; await speech.GenerateAsync("Danach neu starten.", _ => { chunks++; return Task.CompletedTask; }, CancellationToken.None);
                    Check("cancel-does-not-leak-into-next-request", chunks == 30 && speech.WorkerId == pid);
                }
                using (var speech = Fake("stall"))
                {
                    await speech.PrepareAsync(FixtureRuntime, CancellationToken.None); int workerId = speech.WorkerId!.Value; bool timedOut = false;
                    try { await speech.GenerateAsync("Antwort fehlt.", _ => Task.CompletedTask, CancellationToken.None); }
                    catch (TimeoutException e) { timedOut = e.Message.Contains("antwortet nicht"); }
                    Check("worker-stall-is-timeout-and-releases-process", timedOut && !speech.Ready && speech.WorkerId == null && Exited(workerId));
                }
                using (var speech = Fake("prepare-stall", 250))
                {
                    bool timedOut = false; try { await speech.PrepareAsync(FixtureRuntime, CancellationToken.None); } catch (TimeoutException) { timedOut = true; }
                    Check("preparation-timeout-distinct-from-stop", timedOut && speech.WorkerId == null);
                }
                using (var speech = Fake("ignore-cancel", idleMs: 3000))
                {
                    await speech.PrepareAsync(FixtureRuntime, CancellationToken.None); int workerId = speech.WorkerId!.Value; using var cancel = new CancellationTokenSource(100);
                    var watch = Stopwatch.StartNew(); bool stopped = false;
                    try { await speech.GenerateAsync("Hängender Abbruch.", _ => Task.CompletedTask, cancel.Token); } catch (OperationCanceledException) { stopped = true; }
                    Check("unacknowledged-stop-bounded-and-kills-worker", stopped && speech.WorkerId == null && watch.Elapsed.TotalSeconds < 2 && Exited(workerId), watch.Elapsed.TotalMilliseconds);
                }
                var gate = new ActivityGate(() => true);
                using (var page = new ReadingPage(gate, Fake("slow-prepare"), () => (FixtureRuntime, null)))
                {
                    var warm = page.WarmAsync(); var pid = Field<LocalSpeech>(page, "speech").WorkerId;
                    Check("warmup-does-not-hold-activity-gate", page.Warming && !page.Busy && !gate.Busy);
                    await page.PrepareAsync(); await warm;
                    Check("foreground-preparation-reuses-warmup", page.VoiceReady && !page.Busy && !gate.Busy && Field<LocalSpeech>(page, "speech").WorkerId == pid);
                }
                ReadingPage? mainReading = null;
                using (var main = new MainForm(false, activity => mainReading = new ReadingPage(activity, Fake("prepare-stall"), () => (FixtureRuntime, null))))
                {
                    foreach (string name in new[] { "modelReady", "modelChecked" }) typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, true);
                    typeof(MainForm).GetMethod("UpdateUi", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [null]);
                    var warm = mainReading!.WarmAsync(); int warmWorkerId = Field<LocalSpeech>(mainReading, "speech").WorkerId!.Value;
                    Check("main-record-button-enabled-during-slow-warmup", Field<GardenButton>(main, "toggle").Enabled && !Field<ActivityGate>(main, "activities").Busy);
                    var watch = Stopwatch.StartNew();
                    using var dictationLease = Field<ActivityGate>(main, "activities").TryEnter("Diktat wird gestartet");
                    double gateMs = watch.Elapsed.TotalMilliseconds;
                    await mainReading.StopWarmupAsync(); await warm;
                    double stopMs = watch.Elapsed.TotalMilliseconds - gateMs;
                    Check("dictation-priority-cancels-warmup-without-awaiting-preparation", stopMs < 2000 && !mainReading.Warming && Field<LocalSpeech>(mainReading, "speech").WorkerId == null && Exited(warmWorkerId), new { gateMs, stopMs });
                }
                using (var page = new ReadingPage(gate, Fake("slow-prepare"), () => (FixtureRuntime, null)))
                {
                    _ = page.WarmAsync(); var read = page.ReadAsync("Vorlesen während Vorbereitung.");
                    Check("first-read-during-warmup-becomes-read-not-stop", page.Busy && page.Warming && gate.Busy);
                    page.Stop(); await read;
                    Check("stop-during-adopted-warmup-releases-everything", !page.Busy && !page.Warming && !gate.Busy);
                }
                using (var page = new ReadingPage(gate, Fake("prepare-stall", 200), () => (FixtureRuntime, null)))
                {
                    await page.PrepareAsync();
                    Check("page-reports-timeout-not-user-stop", Field<Label>(page, "status").Text.Contains("antwortet") && !Field<Label>(page, "status").Text.Contains("gestoppt"));
                }
                using (var page = new ReadingPage(gate))
                {
                    Field<GardenTextBox>(page, "input").Text = "Mein vorhandener Text.";
                    await page.ReadAsync("Darf ohne Einrichtung nicht ersetzen.");
                    Check("missing-setup-never-starts-worker-or-replaces-text", !page.Busy && !Field<GardenButton>(page, "play").Enabled && Field<LocalSpeech>(page, "speech").WorkerId == null && Field<GardenTextBox>(page, "input").Text == "Mein vorhandener Text.");
                }
                byte[] data = [1, 2, 3, 4]; string path = Path.Combine(folder, "model.bin"); await File.WriteAllBytesAsync(path, data);
                using (var cache = new VerifiedModelCache(data.Length, Convert.ToHexString(SHA256.HashData(data))))
                {
                    Check("model-first-full-verification", await cache.VerifyAsync(path, default) && cache.FullChecks == 1);
                    Check("model-reuse-without-rehash", await cache.VerifyAsync(path, default) && cache.FullChecks == 1);
                    bool blocked = false; try { File.WriteAllBytes(path, [4, 3, 2, 1]); } catch (IOException) { blocked = true; }
                    Check("cached-model-cannot-be-modified", blocked);
                    await cache.ReleaseAsync(path, default); File.WriteAllBytes(path, [4, 3, 2, 1]);
                    Check("changed-model-rejected-after-release", !await cache.VerifyAsync(path, default) && cache.FullChecks == 2);
                }
                using (var page = new PromptPage(gate, () => ""))
                {
                    var provider = Field<ComboBox>(page, "reviewProvider"); provider.SelectedIndex = 0;
                    Check("local-review-labelled-local", Field<GardenButton>(page, "generate").Text.Contains("lokal", StringComparison.OrdinalIgnoreCase));
                    provider.SelectedIndex = 1;
                    Check("online-review-labelled-online", Field<GardenButton>(page, "generate").Text.Contains("Online"));
                }
                using (var panel = new Panel())
                {
                    panel.Controls.Add(new Label { Text = "Unrelated private-looking status" }); var child = new Panel(); panel.Controls.Add(child); BrandTheme.Apply(panel);
                    Check("layout-names-do-not-borrow-labels", child.AccessibilityObject.Name == "");
                }
                Check("product-version-has-no-commit-suffix", !typeof(MainForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Contains('+'));
            }
            catch (Exception e) { Check("unexpected", false, e.ToString()); }
            finally { await File.WriteAllTextAsync(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); host.Close(); }
        };
        Application.Run(host); Console.WriteLine($"Review fixes: {results.Count - failures} PASS / {failures} FAIL · {folder}"); return failures == 0 ? 0 : 1;
    }
}
