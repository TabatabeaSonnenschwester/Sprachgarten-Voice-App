using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Klartext;
using NAudio.Wave;

internal static class Program
{
    private static int failed;
    private static readonly List<object> results = [];
    private static string root = "";
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--read-selection") return SelectedText.Run();
        if (args.Length == 2 && args[0] == "--read-test-selection") return SelectedText.WriteSelection(() => System.Windows.Automation.AutomationElement.FromHandle((nint)long.Parse(args[1])));
        if (args.Length == 2 && args[0] == "--reading-tests") return ReadingTests.Run(args[1], false);
        if (args.Length == 2 && args[0] == "--reading-live") return ReadingTests.Run(args[1], true);
        if (args.Length == 2 && args[0] == "--ux-only") return UxTests.Run(args[1]);
        if (args.Length > 0 && args[^1] == CloudAndHandoffTests.CaptureMarker) { File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "captured-arguments.json"), JsonSerializer.Serialize(args)); return 0; }
        if (args.Length == 2 && args[0] == "--cloud-handoff-tests") return CloudAndHandoffTests.Run(args[1]);
        if (args.Length == 2 && args[0] == "--prompt-review-tests") return PromptReviewTests.RunAsync(args[1]).GetAwaiter().GetResult();
        if (args.Length == 4 && args[0] == "--private-review-case")
        {
            try
            {
                var result = new LocalPromptModel().ReviewAsync(File.ReadAllText(args[2]), CancellationToken.None,
                    Path.Combine(args[1], ".tools", "prompt-model-test", LocalPromptModel.FileName),
                    Path.Combine(args[1], ".tools", "prompt-model-test", "llama-b10964", "llama-server.exe"), 42).GetAwaiter().GetResult();
                File.WriteAllText(args[3], JsonSerializer.Serialize(result));
                Console.WriteLine(JsonSerializer.Serialize(new { result.Seconds, Questions = result.Review.Questions.Length, Suggestions = result.Review.Suggestions.Length }));
                return 0; // Structural success only. Read private result separately for semantic review.
            }
            catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); return 2; }
        }
        if (args.Length == 4 && args[0] == "--private-prompt-case")
        {
            // Explicit user-provided text, local model only. No tools, microphone or directory scanning.
            try
            {
                string input = File.ReadAllText(args[2]);
                var attempts = new List<PromptAttempt>();
                var result = new LocalPromptModel().ComposeAsync(input, CancellationToken.None,
                    Path.Combine(args[1], ".tools", "prompt-model-test", LocalPromptModel.FileName),
                    Path.Combine(args[1], ".tools", "prompt-model-test", "llama-b10964", "llama-server.exe"), 42, diagnostic: attempts.Add).GetAwaiter().GetResult();
                File.WriteAllText(args[3], JsonSerializer.Serialize(new { result.Text, result.Seconds, result.KeptOriginal, Attempts = attempts }));
                Console.WriteLine(JsonSerializer.Serialize(new { input.Length, result.Seconds, result.KeptOriginal, ExactOriginal = result.Text == input }));
                return result.KeptOriginal ? 4 : 0;
            }
            catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); return 2; }
        }
        if (args.Length == 5 && args[0] == "--prompt-host") return LocalPromptModel.RunHostAsync(args[1], args[2], args[3], args[4]).GetAwaiter().GetResult();
        if (args.Length >= 2 && args[0] == "--extensions-only") return ExtensionTests.RunAsync(args[1], args.Contains("--local-model"), args.FirstOrDefault(a => a.StartsWith("--case="))?[7..]).GetAwaiter().GetResult();
        if (args.Length == 2 && args[0] == "--theme-only") return ThemeTests.Run(args[1]);
        if (args.Length == 2 && args[0] == "--extension-ui-only") return ExtensionUiTests.Run(args[1]);
        if (args.Length == 5 && args[0] == "--worker" && !Transcriber.WaitForParentJob(args[4])) return 3;
        if (args.Length is 4 or 5 && args[0] == "--worker") return Transcriber.RunWorkerAsync(args[1], args[2], args[3]).GetAwaiter().GetResult();
        if (args.Length == 2 && args[0] == "--copy-result")
        {
            var result = JsonSerializer.Deserialize<TranscriptionResult>(File.ReadAllText(args[1]))!;
            using var owner = new HotkeyWindow(); return UnicodeClipboard.TryCopy(owner.Handle, result.Text) ? 0 : 1;
        }
        if (args.Length == 1 && args[0] == "--idle") { Thread.Sleep(Timeout.Infinite); return 0; }
        if (args.Length == 2 && args[0] == "--job-parent")
        {
            using var job = new WorkerJob();
            using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { "--idle" }, UseShellExecute = false, CreateNoWindow = true })!;
            job.Add(child); File.WriteAllText(args[1], child.Id.ToString()); Thread.Sleep(Timeout.Infinite); return 0;
        }
        if (args.Length == 2 && args[0] == "--analyze") { Console.WriteLine(JsonSerializer.Serialize(AudioRecorder.Analyze(args[1]))); return 0; }
        if (args.Length == 2 && args[0] == "--seed-correction-demo")
        {
            AppPaths.Root = Path.GetFullPath(args[1]); AppPaths.Initialize();
            RecoveryStore.Save("Das Hauß ist groß. Wir nutzen Klar Text.\r\nÄÖÜ äöü ß € 😀");
            new UserSettings(null, new(7, 119)).Save(AppPaths.Settings); return 0;
        }
        if (args.Length == 2 && args[0] == "--play")
        {
            using var source = new AudioFileReader(args[1]); using var output = new WaveOutEvent();
            output.Init(source); output.Play(); while (output.PlaybackState == PlaybackState.Playing) Thread.Sleep(100); return 0;
        }
        root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        AppPaths.Root = Path.Combine(root, "artifacts", "test-profile"); AppPaths.Initialize();
        using var form = new Form { Text = "Klartext · Komponentenprüfung", Size = new Size(420, 130), ShowInTaskbar = false };
        form.Shown += async (_, _) =>
        {
            try
            {
                if (args.Contains("--corrections-only")) CorrectionTests.Run(Record);
                else if (args.Contains("--asr-only")) await TestAsrAsync(Path.Combine(root, "artifacts", "test-audio"));
                else await RunAsync(args.Contains("--asr"));
            }
            catch (Exception e) { Record("test-runner", false, e.ToString()); }
            finally
            {
                File.WriteAllText(Path.Combine(root, "artifacts", args.Contains("--corrections-only") ? "test-results-corrections.json" : args.Contains("--asr-only") ? "test-results-offline-asr.json" : "test-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
                form.Close();
            }
        };
        Application.Run(form); return failed == 0 ? 0 : 1;
    }
    private static void Record(string name, bool pass, object? details = null)
    {
        results.Add(new { name, status = pass ? "passed" : "failed", details });
        Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}: {JsonSerializer.Serialize(details)}");
        if (!pass) failed++;
    }
    private static async Task RunAsync(bool asr)
    {
        CorrectionTests.Run(Record);
        var s = new SessionState();
        Record("state-ready", s.CanStart);
        s.Move(DictationState.Recording); s.HasPendingAudio = true;
        bool rejects = false; try { s.Move(DictationState.Recording); } catch (InvalidOperationException) { rejects = true; }
        Record("state-rejects-parallel-recording", rejects);
        s.Move(DictationState.Stopping); s.Move(DictationState.Transcribing);
        Record("state-transcribing-ignores-new-record", !s.CanStart);
        s.Move(DictationState.Error); Record("state-error-pending-requires-decision", !s.CanStart);
        s.Move(DictationState.Transcribing); s.HasPendingAudio = false; s.Move(DictationState.Result);
        Record("state-repeat-after-result", s.CanStart);
        s.HasUncopiedText = true; Record("state-protects-uncopied-result", !s.CanStart); s.HasUncopiedText = false;
        RecoveryStore.Save("Test äöü\r\nWiederherstellung");
        Record("encrypted-result-recovery", RecoveryStore.Load() == "Test äöü\r\nWiederherstellung" && !System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(AppPaths.Root, "uncopied.bin"))).Contains("Wiederherstellung"));
        RecoveryStore.Clear(); Record("confirmed-result-cleared", !RecoveryStore.Exists);
        Record("reserved-hotkeys-rejected", !new HotkeySpec(8, 65).IsValid && !new HotkeySpec(3, 123).IsValid && !new HotkeySpec(0, 65).IsValid);
        string settingsPath = Path.Combine(root, "artifacts", "test-settings.json");
        var settings = new UserSettings("test-device", new(6, 75)); settings.Save(settingsPath);
        Record("settings-roundtrip", UserSettings.Load(settingsPath) == settings);
        File.WriteAllText(settingsPath, "{invalid"); Record("settings-corruption-defaults", UserSettings.Load(settingsPath) == new UserSettings());
        using var h1 = new HotkeyWindow(); using var h2 = new HotkeyWindow();
        var spec = new HotkeySpec(7, 121);
        bool registered = h1.TryRegister(spec);
        Record("native-hotkey-register", registered);
        Record("native-hotkey-conflict", registered && !h2.TryRegister(spec));
        Record("native-hotkey-change", h1.TryRegister(new(7, 120)) && h2.TryRegister(spec));
        const string unicode = "ÄÖÜ äöü ß € — café 😀\r\nZweite Zeile: 42 %";
        bool copied = await UnicodeClipboard.CopyAsync(h1.Handle, unicode);
        Record("native-unicode-clipboard-roundtrip", copied && Clipboard.GetText(TextDataFormat.UnicodeText) == unicode);
        Record("empty-clipboard-write-rejected", !await UnicodeClipboard.CopyAsync(h1.Handle, " ") && Clipboard.GetText() == unicode);
        // Hold clipboard on a dedicated thread with its own owner; real lock, no mock.
        using var held = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var locker = new Thread(() => { using var owner = new HotkeyWindow(); bool opened = false; for (int attempt = 0; attempt < 20 && !opened; attempt++) { opened = Native.OpenClipboard(owner.Handle); if (!opened) Thread.Sleep(50); } if (opened) held.Set(); release.Wait(TimeSpan.FromSeconds(5)); if (opened) Native.CloseClipboard(); });
        locker.SetApartmentState(ApartmentState.STA); locker.Start();
        if (await Task.Run(() => held.Wait(TimeSpan.FromSeconds(3))))
        {
            var sw = Stopwatch.StartNew(); bool busy = await UnicodeClipboard.CopyAsync(h1.Handle, "must not copy");
            Record("clipboard-busy-bounded-failure", !busy && sw.Elapsed.TotalSeconds < 3, new { seconds = sw.Elapsed.TotalSeconds });
        }
        else Record("clipboard-lock-acquired", false);
        release.Set(); await Task.Run(locker.Join);
        Record("clipboard-recovery-after-lock", await UnicodeClipboard.CopyAsync(h1.Handle, unicode) && Clipboard.GetText() == unicode);
        try { var devices = AudioRecorder.Devices(); Record("native-microphone-enumeration", devices.Count > 0, devices.Select(d => d.Name)); }
        catch (Exception e) { Record("native-microphone-enumeration", false, e.GetType().Name); }
        string generated = Path.Combine(root, "artifacts", "test-audio"); Directory.CreateDirectory(generated);
        string silence = Path.Combine(generated, "synthetic-silence.wav");
        using (var w = new WaveFileWriter(silence, new WaveFormat(16000, 16, 1))) w.Write(new byte[32000 * 2], 0, 32000 * 2);
        Record("synthetic-silence-gate", !AudioRecorder.Analyze(silence).HasSignal);
        string shortFile = Path.Combine(generated, "synthetic-short.wav");
        using (var w = new WaveFileWriter(shortFile, new WaveFormat(16000, 16, 1))) { for (int i = 0; i < 3200; i++) w.WriteSample((float)Math.Sin(i * .1) * .3f); }
        Record("too-short-gate", !AudioRecorder.Analyze(shortFile).HasSignal);
        bool pathGuard = false; try { AppPaths.DeleteOwned(Path.Combine(root, "README.md")); } catch (InvalidOperationException) { pathGuard = true; }
        Record("cleanup-scope-guard", pathGuard);
        Record("missing-model-rejected", !await ModelStore.VerifyAsync(Path.Combine(generated, "missing.bin")));
        File.WriteAllText(Path.Combine(generated, "corrupt.bin"), "invalid");
        Record("corrupt-model-rejected", !await ModelStore.VerifyAsync(Path.Combine(generated, "corrupt.bin")));
        string downloadDestination = Path.Combine(generated, "download-check.bin");
        File.WriteAllText(downloadDestination, "previous model must survive");
        using (var client = new HttpClient(new FixtureTransport("bad", null)))
        {
            bool rejected = false;
            try { await ModelStore.DownloadAsync(downloadDestination, new Progress<int>(), CancellationToken.None, client); } catch (InvalidDataException) { rejected = true; }
            Record("download-wrong-size-preserves-previous", rejected && File.ReadAllText(downloadDestination) == "previous model must survive" && !File.Exists(downloadDestination + ".download"));
        }
        using (var cancellation = new CancellationTokenSource())
        using (var client = new HttpClient(new FixtureTransport("cancel", null)))
        {
            cancellation.Cancel(); bool canceled = false;
            try { await ModelStore.DownloadAsync(downloadDestination, new Progress<int>(), cancellation.Token, client); } catch (OperationCanceledException) { canceled = true; }
            Record("download-cancel-preserves-previous", canceled && File.ReadAllText(downloadDestination) == "previous model must survive" && !File.Exists(downloadDestination + ".download"));
        }
        if (File.Exists(AppPaths.Model))
        {
            using var client = new HttpClient(new FixtureTransport("valid", AppPaths.Model));
            await ModelStore.DownloadAsync(downloadDestination, new Progress<int>(), CancellationToken.None, client);
            Record("download-staging-hash-atomic-adoption", await ModelStore.VerifyAsync(downloadDestination) && !File.Exists(downloadDestination + ".download"), "Controlled local HTTP transport fixture, not a live network download");
        }
        File.Delete(downloadDestination);
        string childPid = Path.Combine(root, "artifacts", "job-child.pid");
        if (File.Exists(childPid)) File.Delete(childPid);
        using (var parent = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { "--job-parent", childPid }, UseShellExecute = false, CreateNoWindow = true })!)
        {
            for (int i = 0; i < 100 && !File.Exists(childPid); i++) await Task.Delay(100);
            if (!File.Exists(childPid)) { parent.Kill(true); Record("worker-parent-death", false, "Child PID unavailable"); }
            else
            {
                int id = int.Parse(File.ReadAllText(childPid));
                using var child = Process.GetProcessById(id); parent.Kill(); await parent.WaitForExitAsync();
                bool ended = false; try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); ended = true; } catch (TimeoutException) { child.Kill(); }
                Record("worker-parent-death-kills-child", ended);
            }
        }
        if (asr) await TestAsrAsync(generated);
    }
    private static async Task TestAsrAsync(string generated)
    {
        Record("model-trusted-sha256", await ModelStore.VerifyAsync(AppPaths.Model));
        string fixtureRoot = Environment.GetEnvironmentVariable("SPRACHGARTEN_ASR_FIXTURES") ?? throw new InvalidOperationException("Für ASR-Tests SPRACHGARTEN_ASR_FIXTURES auf einen eigenen Testdatenordner setzen. Siehe test-data/README.md.");
        Directory.CreateDirectory(generated);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureRoot, "cases.json")));
        string? cancellationAudio = null;
        foreach (var item in manifest.RootElement.EnumerateArray())
        {
            string name = item.GetProperty("file").GetString()!;
            if (Path.GetFileName(name) != name || !name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Nur WAV-Dateinamen ohne Verzeichnisse sind erlaubt.");
            string original = Path.Combine(fixtureRoot, name), pcm = Path.Combine(generated, Path.GetFileNameWithoutExtension(name) + "-16k.wav");
            var stats = AudioRecorder.ConvertToPcm(original, pcm);
            if (item.TryGetProperty("silence", out var silence) && silence.GetBoolean()) { Record("real-room-silence-gate", !stats.HasSignal, stats); continue; }
            cancellationAudio = pcm;
            var result = await new Transcriber().TranscribeAsync(AppPaths.Model, pcm, CancellationToken.None);
            double wer = WordErrorRate(item.GetProperty("text").GetString()!, result.Text);
            // Test-only report contains user-authorized fixed reading phrases, never production diagnostic logs.
            Record("asr-" + name, stats.HasSignal && wer <= .25, new { stats.Seconds, processingSeconds = result.Seconds, realTimeFactor = result.Seconds / stats.Seconds, wordErrorRate = wer, transcript = result.Text, referenceKind = "reading-prompt-not-independently-transcribed" });
        }
        if (cancellationAudio == null) throw new InvalidDataException("Mindestens einen gesprochenen Testfall bereitstellen.");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        bool canceled = false;
        try { await new Transcriber().TranscribeAsync(AppPaths.Model, cancellationAudio, cancellation.Token); }
        catch (OperationCanceledException) { canceled = true; }
        Record("real-worker-cancellation", canceled && !Directory.GetFiles(AppPaths.Audio, "*.result").Any());
    }
    private static double WordErrorRate(string expected, string actual)
    {
        static string[] Words(string s) => Regex.Matches(s.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value).ToArray();
        var a = Words(expected); var b = Words(actual); var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++) for (int j = 1; j <= b.Length; j++) d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return (double)d[a.Length, b.Length] / Math.Max(1, a.Length);
    }

    private sealed class FixtureTransport(string kind, string? path) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HttpContent content = kind == "valid" ? new StreamContent(File.OpenRead(path!)) : new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentLength = kind == "valid" ? ModelStore.Size : 3;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }
    }
}
