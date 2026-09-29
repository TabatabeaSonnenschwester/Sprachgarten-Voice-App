using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Klartext;
using NAudio.Wave;

internal static class ExtensionTests
{
    private sealed class ProgressCollector : IProgress<PromptProgress>
    {
        public List<PromptProgress> Values { get; } = [];
        public void Report(PromptProgress value) => Values.Add(value);
    }
    public static async Task<int> RunAsync(string root, bool localModel, string? caseFilter = null)
    {
        string output = Path.Combine(root, "artifacts", "extensions", DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(output);
        AppPaths.Root = Path.Combine(output, "profile"); AppPaths.Initialize();
        var results = new List<object>(); int failed = 0;
        void Check(string name, bool pass, object? evidence = null) { results.Add(new { name, status = pass ? "passed" : "failed", evidence }); if (!pass) failed++; Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}: {JsonSerializer.Serialize(evidence)}"); }
        void Throws(string name, Action action) { try { action(); Check(name, false); } catch { Check(name, true); } }
        try
        {
            bool mainAvailable = true; var gate = new ActivityGate(() => mainAvailable); int changes = 0; gate.Changed += () => changes++;
            using (var lease = gate.TryEnter("Dateitest")) { Check("lease-acquired", lease != null && gate.Busy); Check("parallel-work-refused", gate.TryEnter("KI") == null); gate.Cancel(); Check("cancel-reaches-owner", lease!.Token.IsCancellationRequested); }
            Check("lease-released", !gate.Busy && changes == 2); mainAvailable = false; Check("dictation-busy-blocks-extension", gate.TryEnter("Datei") == null);
            string original = "Plane 3 vegane Mahlzeiten ohne Nüsse.\r\nBudget höchstens 20 € – äöü ß 😀";
            var doc = new PromptDocument(Original: original, Audience: "Zwei Erwachsene", Format: "Tabelle", Constraints: "Keine Erdnüsse", Draft: original);
            Check("composition-preserves-original-and-negation", doc.Compose().StartsWith(original, StringComparison.Ordinal) && doc.Compose().Contains("Keine Erdnüsse"));
            Check("simple-prompt-not-expanded", new PromptDocument(Original: "Nenne drei Blumen.").Compose() == "Nenne drei Blumen.");
            Check("empty-prompt-red", PromptRules.Inspect("")[0].Level == "Rot");
            Check("placeholder-warning", PromptRules.Inspect("Fasse [TEXT EINFÜGEN] zusammen.").Any(f => f.Level == "Gelb"));
            Check("no-false-green-semantic-approval", !PromptRules.Inspect("Nenne drei Blumen.").Any(f => f.Level == "Grün") && PromptRules.Inspect("Nenne drei Blumen.").Any(f => f.Text.Contains("NICHT")));
            Check("numbers-regression-warning", PromptRules.CandidateWarning("45 Minuten", "90 Minuten").StartsWith("Achtung"));
            Check("unchanged-numbers-still-require-review", PromptRules.CandidateWarning("45 Minuten", "45 Minuten").Contains("ungeprüft"));
            Check("missing-recipient-detected", PromptRules.MissingWords("Einladung für unsere Nachbarn", "Schreibe eine Einladung.").Contains("Nachbarn"));
            Check("short-negation-detected", PromptRules.MissingWords("ohne Nüsse", "mit Nüssen").Contains("ohne"));
            Check("numbers-change-guard", PromptRules.NumbersChanged("höchstens 80 Wörter", "maximal 90 Wörter"));
            var review = PromptReview.Parse("""{"summary":"Dauer klären.","questions":[{"question":"Welche Dauer gilt?","reason":"Vorgaben widersprechen sich.","options":["30 Minuten","80 Minuten"]}],"suggestions":[]}""");
            Check("review-options-never-preselected", review.Apply(original, "", false) == original);
            string confirmed = review.Apply(original, "30 Minuten", false);
            Check("confirmed-answer-preserves-source-exactly", confirmed.StartsWith(original + "\r\n\r\n", StringComparison.Ordinal) && confirmed.Contains("Meine Antwort: 30 Minuten"));
            var optional = PromptReview.Parse("""{"summary":"Quellen recherchieren.","questions":[],"suggestions":[{"text":"Kennzeichne unsichere Quellen.","reason":"Erleichtert die Prüfung."}]}""");
            Check("optional-addition-requires-selection", optional.Apply(original, "", false) == original && optional.Apply(original, "", true).EndsWith("Kennzeichne unsichere Quellen."));
            Throws("malformed-review-refused", () => PromptReview.Parse("{}"));
            Throws("null-review-fields-refused", () => PromptReview.Parse("""{"summary":"x","questions":null,"suggestions":[]}"""));
            Throws("unexpected-review-fields-refused", () => PromptReview.Parse("""{"summary":"x","questions":[],"suggestions":[],"prompt":"Ungefragter Ersatz"}"""));
            Throws("confirmed-answer-size-limit", () => review.Apply(original, new string('x', 4001), false));
            Check("model-input-retains-readable-unicode", PromptReviewProtocol.ReviewData(original).Contains("Nüsse") && !PromptReviewProtocol.ReviewData(original).Contains("\\u00"));
            Check("concrete-feedback-parsed", PromptReviewProtocol.ParseFeedback("""{"befund":"Text fehlt.","rueckfrage":"Welchen Text soll die KI bearbeiten?","ergaenzung":""}""", original).Questions.Length == 1);
            Check("empty-question-preserves-visible-finding", PromptReviewProtocol.ParseFeedback("""{"befund":"Keine konkrete Ergänzung gefunden.","rueckfrage":"","ergaenzung":""}""", original).Summary.Length > 0);
            Throws("unexpected-feedback-fields-refused", () => PromptReviewProtocol.ParseFeedback("""{"befund":"x","rueckfrage":"","ergaenzung":"","prompt":"Ersatztext"}""", original));
            // Sprint E (K5): Schlüssel über die Umgebung statt in der Kommandozeile; die Übergabevariable des Hosts wird nicht vererbt.
            Environment.SetEnvironmentVariable("SPRACHGARTEN_LOCAL_KEY", "synthetischer-hostschluessel");
            try
            {
                const string serverKey = "SYNTHETISCHERSERVERSCHLUESSEL0123";
                var createServerStart = typeof(LocalPromptModel).GetMethod("CreateServerStart", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                var server = createServerStart?.Invoke(null, [Path.Combine(output, "llama-server.exe"), Path.Combine(output, "synthetic-model.gguf"), "12345", serverKey]) as ProcessStartInfo;
                string[] serverArgs = server?.ArgumentList.ToArray() ?? [];
                int hostAt = Array.IndexOf(serverArgs, "--host");
                bool keyInEnvironment = server != null && server.Environment.TryGetValue("LLAMA_API_KEY", out var passed) && passed == serverKey;
                bool hostKeyRemoved = server != null && !server.Environment.ContainsKey("SPRACHGARTEN_LOCAL_KEY");
                Check("local-server-key-not-on-command-line", server != null && !serverArgs.Contains("--api-key") && !serverArgs.Any(a => a.Contains(serverKey)) && hostAt >= 0 && hostAt + 1 < serverArgs.Length && serverArgs[hostAt + 1] == "127.0.0.1" && keyInEnvironment && hostKeyRemoved,
                    new { found = createServerStart != null, serverArgs, keyInEnvironment, hostKeyRemoved });
            }
            finally { Environment.SetEnvironmentVariable("SPRACHGARTEN_LOCAL_KEY", null); }
            // Sprint E (K2): Eine übernommene KI-Ergänzung ist als KI-Vorschlag gekennzeichnet, eine eigene Antwort nicht.
            string suggested = optional.Apply(original, "", true);
            Check("accepted-suggestion-marked-as-ai", suggested.Contains("KI-Vorschlag: ") && !confirmed.Contains("KI-Vorschlag: "), new { suggestedTail = suggested[original.Length..], confirmedTail = confirmed[original.Length..] });
            Throws("copied-statement-is-not-question", () => PromptReviewProtocol.ParseFeedback("""{"befund":"Offen.","rueckfrage":"Budget","ergaenzung":""}""", original));
            string draftPath = Path.Combine(AppPaths.Root, "draft.bin"); ProtectedFile.Write(draftPath, doc);
            Check("draft-unicode-roundtrip", ProtectedFile.Read<PromptDocument>(draftPath) == doc);
            Check("draft-not-plaintext", !Encoding.UTF8.GetString(File.ReadAllBytes(draftPath)).Contains("Mahlzeiten"));
            // Sprint F (F1): Ein fremdes Programm hält die Zieldatei kurz offen (Griff ohne FileShare.Delete). Speichern wartet das ab statt zu scheitern.
            string heldPath = Path.Combine(AppPaths.Root, "held-by-other.bin");
            {
                string? briefError = null; bool briefRead = false, briefNoTemp = false; bool? shareDeleteHoldFails = null; string? shareDeleteError = null; long briefMs = -1;
                try
                {
                    // Nur Messung, ohne Bestehensbedingung: ein einzelnes Umbenennen gegen einen Griff mit ReadWrite | Delete.
                    ProtectedFile.Write(heldPath, "vorher");
                    string probe = heldPath + ".probe";
                    try
                    {
                        File.WriteAllBytes(probe, [1, 2, 3]);
                        using (new FileStream(heldPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            try { File.Move(probe, heldPath, true); shareDeleteHoldFails = false; }
                            catch (Exception e) { shareDeleteHoldFails = true; shareDeleteError = e.GetType().Name; }
                        }
                    }
                    finally { if (File.Exists(probe)) File.Delete(probe); }
                    ProtectedFile.Write(heldPath, "alt");
                    var holder = new FileStream(heldPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var release = Task.Run(async () => { await Task.Delay(50); holder.Dispose(); });
                    try
                    {
                        var clock = Stopwatch.StartNew();
                        try { ProtectedFile.Write(heldPath, "neu"); }
                        catch (Exception e) { briefError = e.GetType().Name; }
                        briefMs = clock.ElapsedMilliseconds;
                    }
                    finally { release.Wait(); holder.Dispose(); }
                    briefRead = briefError == null && ProtectedFile.Read<string>(heldPath) == "neu";
                    briefNoTemp = !File.Exists(heldPath + ".tmp");
                }
                catch (Exception e) { briefError ??= "unexpected-" + e.GetType().Name; }
                Check("protected-write-waits-out-brief-lock", briefError == null && briefRead && briefNoTemp, new { briefError, briefRead, briefNoTemp, briefMs, shareDeleteHoldFails, shareDeleteError });
            }
            {
                string? heldError = null; bool heldWrapped = false, heldUnchanged = false, heldNoTemp = false; long heldMs = -1;
                try
                {
                    byte[] heldBefore = File.ReadAllBytes(heldPath);
                    using (new FileStream(heldPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var clock = Stopwatch.StartNew();
                        try { ProtectedFile.Write(heldPath, "bleibt gesperrt"); }
                        catch (Exception e) { heldError = e.GetType().Name; heldWrapped = e.InnerException != null; }
                        heldMs = clock.ElapsedMilliseconds;
                    }
                    heldUnchanged = File.ReadAllBytes(heldPath).SequenceEqual(heldBefore);
                    heldNoTemp = !File.Exists(heldPath + ".tmp");
                }
                catch (Exception e) { heldError = "unexpected-" + e.GetType().Name; }
                bool originalType = heldError is nameof(UnauthorizedAccessException) or nameof(IOException);
                Check("protected-write-still-fails-on-held-lock", originalType && !heldWrapped && heldUnchanged && heldNoTemp && heldMs >= 250 && heldMs <= 2000, new { heldError, heldWrapped, heldUnchanged, heldNoTemp, heldMs });
            }
            // Sprint F (Antwort 2): derselbe Schutz für das Diktatergebnis uncopied.bin.
            {
                string recoveryPath = Path.Combine(AppPaths.Root, "uncopied.bin");
                string? recoveryError = null; bool recoveryRead = false, recoveryNoTemp = false; long recoveryMs = -1;
                try
                {
                    RecoveryStore.Save("alt");
                    var holder = new FileStream(recoveryPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var release = Task.Run(async () => { await Task.Delay(50); holder.Dispose(); });
                    try
                    {
                        var clock = Stopwatch.StartNew();
                        try { RecoveryStore.Save("neu"); }
                        catch (Exception e) { recoveryError = e.GetType().Name; }
                        recoveryMs = clock.ElapsedMilliseconds;
                    }
                    finally { release.Wait(); holder.Dispose(); }
                    recoveryRead = recoveryError == null && RecoveryStore.Load() == "neu";
                    recoveryNoTemp = !File.Exists(recoveryPath + ".tmp");
                }
                catch (Exception e) { recoveryError ??= "unexpected-" + e.GetType().Name; }
                finally
                {
                    try { RecoveryStore.Clear(); if (File.Exists(recoveryPath + ".tmp")) File.Delete(recoveryPath + ".tmp"); }
                    catch (Exception e) { recoveryError ??= "cleanup-" + e.GetType().Name; }
                }
                Check("recovery-save-waits-out-brief-lock", recoveryError == null && recoveryRead && recoveryNoTemp, new { recoveryError, recoveryRead, recoveryNoTemp, recoveryMs });
            }
            string path = Path.Combine(AppPaths.Root, "library.bin"); var lib = new PromptLibrary(path);
            Guid id = lib.Save(null, "Küchengarten", original, original); lib.Save(id, "Küchengarten", original + "\r\nAls Liste.", original); lib.Favorite(id);
            var loaded = new PromptLibrary(path);
            Check("library-restart-versions-favorite", loaded.Entries.Count == 1 && loaded.Entries[0].Favorite && loaded.Entries[0].Versions.Length == 2 && loaded.Entries[0].Versions[0].Text == original);
            Check("library-search-case-insensitive", loaded.Search("KÜCHEN").Count() == 1 && loaded.Search("vegan").Count() == 1);
            Throws("empty-title-refused", () => loaded.Save(null, "", original, original));
            for (int i = 2; i < 20; i++) loaded.Save(id, "Küchengarten", original, original);
            Throws("versions-cap-no-silent-eviction", () => loaded.Save(id, "Küchengarten", original, original));
            Check("all-versions-kept", new PromptLibrary(path).Entries[0].Versions.Length == 20);
            loaded.Delete(id); Check("explicit-delete-persists", new PromptLibrary(path).Entries.Count == 0);
            byte[] corrupt = [1, 2, 3, 4]; File.WriteAllBytes(path, corrupt); Throws("corrupt-library-refused", () => new PromptLibrary(path));
            Check("corrupt-library-not-overwritten", File.ReadAllBytes(path).SequenceEqual(corrupt));
            string denied = Path.Combine(AppPaths.Root, "save-blocked"); Directory.CreateDirectory(denied); var blocked = new PromptLibrary(denied);
            Throws("atomic-save-failure", () => blocked.Save(null, "Titel", original, original)); Check("save-failure-keeps-memory-state", blocked.Entries.Count == 0);
            Check("word-comparison-unicode", AudioToolsPage.WordErrorRate("Äpfel, Öl und Grüße!", "äpfel öl und grüße") == 0);
            Check("word-comparison-detects-substitution", AudioToolsPage.WordErrorRate("drei rote Blumen", "drei gelbe Blumen") == 1d / 3);
            string silence = Path.Combine(output, "synthetic-silence.wav"), sine = Path.Combine(output, "synthetic-signal.wav"), tooLong = Path.Combine(output, "synthetic-too-long.wav");
            WriteWave(silence, 1, false); WriteWave(sine, 1, true); WriteWave(tooLong, 302, false);
            byte[] inputHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(sine));
            string pcm = Path.Combine(AppPaths.Audio, "test.pcm.wav");
            var silentStats = AudioRecorder.ConvertToPcm(silence, pcm); Check("real-native-decoder-silence-rejected", !silentStats.HasSignal, silentStats);
            var signalStats = AudioRecorder.ConvertToPcm(sine, pcm); Check("real-native-decoder-signal-present", signalStats.HasSignal && Math.Abs(signalStats.Seconds - 1) < .02, signalStats);
            Throws("over-five-minutes-refused-not-truncated", () => AudioRecorder.ConvertToPcm(tooLong, pcm));
            using (var cancelled = new CancellationTokenSource()) { cancelled.Cancel(); Throws("native-conversion-honors-cancel", () => AudioRecorder.ConvertToPcm(sine, pcm, cancelled.Token)); }
            Check("input-audio-not-modified", inputHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(sine))));
            foreach (string ext in new[] { ".mp3", ".m4a" })
            {
                string compressed = Path.Combine(output, "synthetic-signal" + ext);
                using (var source = new WaveFileReader(sine))
                {
                    if (ext == ".mp3") MediaFoundationEncoder.EncodeToMp3(source, compressed, 128000);
                    else MediaFoundationEncoder.EncodeToAac(source, compressed, 128000);
                }
                var decoded = AudioRecorder.ConvertToPcm(compressed, pcm);
                Check("real-native-decoder-" + ext, decoded.HasSignal && Math.Abs(decoded.Seconds - 1) < .3, decoded);
            }
            string brokenAudio = Path.Combine(output, "invalid.wav"); File.WriteAllText(brokenAudio, "not a WAV");
            Throws("damaged-audio-rejected", () => AudioRecorder.ConvertToPcm(brokenAudio, pcm));
            // Sprint B, S4: real child process, real worker path; only the exception type may travel upwards.
            AudioRecorder.ConvertToPcm(sine, pcm);
            const string oldCollectiveSentence = "Lokale Spracherkennung fehlgeschlagen. Modell prüfen; unter Windows muss die Microsoft Visual C++ 2022 x64-Laufzeit vorhanden sein. Audio bleibt zum Wiederholen erhalten.";
            string fakeModel = Path.Combine(output, "not-a-model.bin");
            File.WriteAllText(fakeModel, "Dies ist keine Modelldatei.");
            string thrown = "", advice = "";
            try { await new Transcriber().TranscribeAsync(fakeModel, pcm, CancellationToken.None); }
            catch (Exception ex) { thrown = ex.GetType().Name; advice = ex.Message; }
            bool typeReported = advice.Contains("Gemeldeter Fehlertyp: ") && !advice.Contains("Gemeldeter Fehlertyp: keiner.");
            bool noPrivateContent = !advice.Contains('\\') && !advice.Contains(".bin") && !advice.Contains(".wav") && !advice.Contains("Klartext");
            Check("worker-failure-reports-its-exception-type", thrown == "IOException" && typeReported && advice != oldCollectiveSentence && noPrivateContent, new { thrown, advice });
            Check("worker-result-file-is-removed-after-failure", !Directory.GetFiles(AppPaths.Audio, "*.result").Any(), Directory.GetFiles(AppPaths.Audio, "*.result"));
            var advices = new[]
            {
                Transcriber.FailureAdvice(2, "DllNotFoundException", true),
                Transcriber.FailureAdvice(2, "OutOfMemoryException", true),
                Transcriber.FailureAdvice(2, "InvalidDataException", true),
                Transcriber.FailureAdvice(2, "IOException", true),
                Transcriber.FailureAdvice(2, "SynthetischerSonderfall", true),
                Transcriber.FailureAdvice(3, null, false)
            };
            string missingResult = Transcriber.FailureAdvice(2, null, false);
            string modelAdvice = Transcriber.FailureAdvice(2, "FileNotFoundException", true);
            bool distinct = advices.Distinct().Count() == 6 && !advices.Contains(missingResult);
            bool everyOneNamesTheType = advices.All(a => a.Contains("Gemeldeter Fehlertyp: ")) && missingResult.Contains("Gemeldeter Fehlertyp: keiner.");
            bool modelAdviceOnlyForTheModel = advices.All(a => !a.StartsWith("Modell prüfen")) && !missingResult.StartsWith("Modell prüfen") && modelAdvice.StartsWith("Modell prüfen");
            Check("failure-advice-separates-known-causes", distinct && everyOneNamesTheType && modelAdviceOnlyForTheModel, new { advices, missingResult, modelAdvice });
            string overlongType = new('A', 200);
            string controlType = "Bad" + (char)1 + "Type";
            string overlongAdvice = Transcriber.FailureAdvice(2, overlongType, true);
            string controlAdvice = Transcriber.FailureAdvice(2, controlType, true);
            Check("failure-advice-rejects-implausible-type-strings",
                !overlongAdvice.Contains("AAAA") && overlongAdvice.Contains("Gemeldeter Fehlertyp: keiner.") && !controlAdvice.Contains("Bad") && !controlAdvice.Contains((char)1) && controlAdvice.Contains("Gemeldeter Fehlertyp: keiner."),
                new { overlongAdvice, controlAdvice });
            string legacyResult = Path.Combine(output, "legacy-result.json");
            File.WriteAllText(legacyResult, "{\"Text\":\"Synthetischer Ergebnistext\",\"Seconds\":1.5}");
            var restored = JsonSerializer.Deserialize<TranscriptionResult>(File.ReadAllText(legacyResult));
            Check("successful-result-json-stays-backwards-compatible", restored != null && restored.Text == "Synthetischer Ergebnistext" && Math.Abs(restored.Seconds - 1.5) < 1e-9 && restored.FailureType == null, restored);
            Check("missing-llm-rejected", !await LocalPromptModel.VerifyAsync(Path.Combine(output, "absent.gguf")));
            if (localModel)
            {
                string model = Path.Combine(root, ".tools", "prompt-model-test", LocalPromptModel.FileName);
                string runtime = Path.Combine(root, ".tools", "prompt-model-test", "llama-b10964", "llama-server.exe");
                var scenarios = new[] {
                    (Name: "workshop", Input: "Erstelle einen Workshop über Küchenkräuter für Erwachsene ohne Vorkenntnisse. Dauer 45 Minuten. Ausgabe als Tabelle mit Zeit und Aktivität.", Required: new[] { "45", "Erwachsene", "Tabelle" }, Forbidden: new[] { "90 Minuten", "Kinder" }),
                    (Name: "vegan", Input: "Plane 3 vegane Mahlzeiten für zwei Erwachsene ohne Nüsse. Budget höchstens 20 Euro. Gib eine kurze Einkaufsliste dazu.", Required: new[] { "3", "vegan", "20", "Nüsse" }, Forbidden: new[] { "vegetarisch", "Fleisch", "Milch", "Eier" }),
                    (Name: "simple", Input: "Nenne fünf heimische Blumen für einen sonnigen Balkon. Antworte kurz auf Deutsch.", Required: new[] { "Blumen", "Balkon", "Deutsch" }, Forbidden: new[] { "Rolle", "Experte", "Schritt für Schritt" }),
                    (Name: "dictated-rough", Input: "Also ich möchte irgendwie eine kurze Einladung schreiben lassen, und zwar für unsere Nachbarn zum Gartenfest am 12. Juni um 16 Uhr. Bitte locker und freundlich. Keine Emojis, das mag ich nicht. Höchstens 80 Wörter.", Required: new[] { "Einladung", "Nachbarn", "12", "16", "80", "Emojis" }, Forbidden: new[] { "Liebe Nachbarn", "herzlich ein", "🎉" })
                };
                if (caseFilter != null && !scenarios.Any(s => s.Name == caseFilter)) throw new ArgumentException("Unbekannter Modelltestfall.");
                foreach (var s in scenarios.Where(s => caseFilter == null || s.Name == caseFilter))
                {
                    var progress = new ProgressCollector();
                    var response = await new LocalPromptModel().ComposeAsync(s.Input, CancellationToken.None, model, runtime, 42, progress);
                    var percentages = progress.Values.Where(p => p.Percent.HasValue).Select(p => p.Percent!.Value).ToArray();
                    Check("real-llm-progress-" + s.Name, percentages.Length > 2 && percentages[0] == 0 && percentages[^1] == 100 && percentages.SequenceEqual(percentages.Order()) && progress.Values.Any(p => p.Stage == "KI in den Arbeitsspeicher laden" && p.Percent == null) && progress.Values.Any(p => p.Stage == "Vorschlag formulieren" && p.Percent == null), progress.Values);
                    bool pass = !response.KeptOriginal && s.Required.All(x => response.Text.Contains(x, StringComparison.OrdinalIgnoreCase)) && s.Forbidden.All(x => !response.Text.Contains(x, StringComparison.OrdinalIgnoreCase));
                    Check("real-llm-formulation-" + s.Name, pass, new { s.Input, response.Text, response.Seconds, response.KeptOriginal, kind = "synthetic text; lexical invariants, manual review also required; rejected rewrite is NOT a quality pass" });
                    if (response.KeptOriginal) Check("rejected-rewrite-preserves-entire-input", response.Text == s.Input);
                }
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(4)); var clock = Stopwatch.StartNew();
                try { await new LocalPromptModel().ComposeAsync(scenarios[0].Input, cancel.Token, model, runtime); Check("real-llm-cancel", false); }
                catch (OperationCanceledException) { Check("real-llm-cancel", clock.Elapsed.TotalSeconds < 15, clock.Elapsed.TotalSeconds); }
            }
        }
        catch (Exception ex) { Check("runner", false, ex.ToString()); }
        finally { File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine("Evidence: " + output); }
        return failed == 0 ? 0 : 1;
    }
    private static void WriteWave(string path, int seconds, bool signal)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 1));
        var buffer = new byte[88200];
        if (signal) for (int i = 0; i < 44100; i++) BitConverter.TryWriteBytes(buffer.AsSpan(i * 2, 2), (short)(Math.Sin(i * 2 * Math.PI * 220 / 44100) * 10000));
        for (int i = 0; i < seconds; i++) writer.Write(buffer, 0, buffer.Length);
    }
}
