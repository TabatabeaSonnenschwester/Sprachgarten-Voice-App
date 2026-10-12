using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;
using Klartext;

/// <summary>Unshown native controls only. No desktop input, live recording, playback or clipboard mutation.</summary>
internal static class ExtensionUiTests
{
    public static int Run(string root)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string folder = Path.Combine(root, "artifacts", "extension-controls", DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(folder);
        AppPaths.Root = Path.Combine(folder, "profile"); AppPaths.Initialize();
        var results = new List<object>(); int failed = 0;
        void Check(string name, bool pass, object? evidence = null) { results.Add(new { name, status = pass ? "passed" : "failed", evidence }); if (!pass) failed++; Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}"); }
        try
        {
            // Reproduce a restart with an unconfirmed transcript. Never show the form or start its devices.
            RecoveryStore.Save("Synthetischer gesicherter Testtext.");
            using (var main = new MainForm(false))
            {
                Check("window-title-matches-product-version", main.Text.EndsWith(Application.ProductVersion));
                typeof(MainForm).GetField("modelReady", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, true);
                Invoke(main, "LoadRecovery"); Invoke(main, "UpdateUi", "Bereit");
                var session = Field<SessionState>(main, "state");
                Check("restored-result-blocks-recording-without-data-loss", !session.CanStart && !Field<Button>(main, "toggle").Enabled && RecoveryStore.Load() == "Synthetischer gesicherter Testtext.");
                Check("restored-result-never-displays-ready", !Field<Label>(main, "stateSummary").Text.Contains("Bereit"));
                Check("restored-result-has-persistent-action", Field<Label>(main, "status").Text.Contains("kopieren", StringComparison.OrdinalIgnoreCase));
                Check("restored-result-copy-action-enabled", Field<Button>(main, "copy").Enabled);
                // Sprint E (B1): Die drei Meldungen nennen den Knopf, der in ihrem Zustand da ist; zwei davon sind im Prüflauf nicht erreichbar.
                var notices = new[] { "RestoredResultNotice", "RestoredTranscriptNotice", "ClipboardBusyNotice" }.Select(n => typeof(MainForm).GetField(n, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as string).ToArray();
                Check("restore-notices-name-the-copy-button", notices.All(t => t != null && t.Contains("„Text kopieren und freigeben“") && !t.Contains("Erneut kopieren")), notices);
                // Model successful copy acknowledgement without writing the real system clipboard.
                RecoveryStore.Clear(); session.HasUncopiedText = false; Invoke(main, "UpdateUi", "Text kopiert");
                Check("acknowledged-result-releases-recording", session.CanStart && Field<Button>(main, "toggle").Enabled && Field<Label>(main, "stateSummary").Text.Contains("Bereit"));
                typeof(MainForm).GetField("modelReady", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, false);
                Invoke(main, "UpdateUi", "Bereit");
                Check("missing-model-never-displays-ready", !Field<Label>(main, "stateSummary").Text.Contains("Bereit") && Field<Label>(main, "status").Text.Contains("Spracherkennung prüfen"));
                typeof(MainForm).GetField("modelReady", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, true);
                session.HasPendingAudio = true; Invoke(main, "UpdateUi", "Bereit");
                Check("pending-recording-has-actionable-status", !Field<Button>(main, "toggle").Enabled && Field<Label>(main, "stateSummary").Text.Contains("wartet") && Field<Label>(main, "status").Text.Contains("erneut transkribieren"));
                session.HasPendingAudio = false; RecoveryStore.Save(""); Invoke(main, "LoadRecovery"); Invoke(main, "UpdateUi", "Bereit");
                Check("empty-recovery-allows-recording-without-copy", session.CanStart && Field<Button>(main, "toggle").Enabled && !Field<Button>(main, "copy").Enabled && RecoveryStore.Exists);
                RecoveryStore.Save(" \r\n\t"); Invoke(main, "LoadRecovery"); Invoke(main, "UpdateUi", "Bereit");
                Check("whitespace-recovery-allows-recording", session.CanStart && Field<Button>(main, "toggle").Enabled && RecoveryStore.Load() == " \r\n\t");
                var transcript = Field<TextBox>(main, "transcript"); transcript.Text = "Synthetische Bearbeitung";
                Check("editing-nonempty-text-blocks-recording", !session.CanStart && !Field<Button>(main, "toggle").Enabled);
                transcript.Clear();
                Check("clearing-text-releases-recording-after-persistence", session.CanStart && Field<Button>(main, "toggle").Enabled && RecoveryStore.Load() == "");
                transcript.Text = "Noch zu sichernder Testtext";
                using (var lockedRecovery = File.Open(Path.Combine(AppPaths.Root, "uncopied.bin"), FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    transcript.Clear();
                    Check("failed-empty-save-keeps-recording-blocked", !session.CanStart && !Field<Button>(main, "toggle").Enabled && Field<bool>(main, "editSaveFailed"));
                }
                File.WriteAllBytes(Path.Combine(AppPaths.Root, "uncopied.bin"), [1, 2, 3]); Invoke(main, "LoadRecovery"); Invoke(main, "UpdateUi", "Bereit");
                Check("damaged-recovery-offers-reload-without-deletion", Field<Label>(main, "status").Text.Contains("Ergebnis erneut laden") && File.ReadAllBytes(Path.Combine(AppPaths.Root, "uncopied.bin")).SequenceEqual(new byte[] { 1, 2, 3 }));
                RecoveryStore.Clear();
            }
            // Sprint E (c, N6, K10): eigenes Fenster, in ein ungezeigtes Panel gehängt, damit Visible den eigenen Zustand liefert (Muster ThemeTests.MountMainForm).
            using (var main = new MainForm(false))
            using (var mounted = new Panel { Size = new Size(900, 740), Font = BrandTheme.Body })
            {
                var modelLabel = Field<Label>(main, "modelLabel");
                string beforeCheck = modelLabel.Text;
                Wait((Task)Call(main, "RefreshModelAsync", CancellationToken.None)!);
                Invoke(main, "UpdateUi", "Bereit");
                string afterCheck = modelLabel.Text;
                Check("model-card-says-checking-before-first-check", beforeCheck == "Moonshine Deutsch wird vorbereitet …" && afterCheck == "Moonshine ist momentan nicht bereit · erneut prüfen", new { beforeCheck, afterCheck, modelPresent = File.Exists(AppPaths.Model) });
                mounted.Controls.Add(main.Controls[0]); mounted.CreateControl(); mounted.PerformLayout();
                var session = Field<SessionState>(main, "state"); var cancelButton = Field<Button>(main, "cancel");
                session.Move(DictationState.Setup); Invoke(main, "UpdateUi", "Bereit");
                bool hiddenWithout = !cancelButton.Enabled && !cancelButton.Visible;
                bool shownWith;
                using (var download = new CancellationTokenSource())
                {
                    SetField(main, "operation", download); Invoke(main, "UpdateUi", "Bereit");
                    shownWith = cancelButton.Enabled && cancelButton.Visible;
                    SetField(main, "operation", null);
                }
                session.Move(DictationState.Ready); Invoke(main, "UpdateUi", "Bereit");
                Check("setup-without-operation-offers-no-cancel", hiddenWithout && shownWith, new { hiddenWithout, shownWith });
                // Bundled ASR: rechecking enters Setup and disables competing actions before preparation.
                // Stop at the UI boundary; no model process, download or clipboard operation is required.
                var downloadButton = Field<GardenOnboarding>(main, "onboarding").Action; var statusLabel = Field<Label>(main, "status");
                bool readyBefore = session.Current == DictationState.Ready && downloadButton.Enabled, hookFired = false, cancelShown = false, operationSet = false; string? faultType = null;
                EventHandler stopBeforeNetwork = (_, _) =>
                {
                    if (session.Current != DictationState.Setup || !statusLabel.Text.Contains("Sprachmodell wird erneut geprüft")) return;
                    hookFired = true; cancelShown = cancelButton.Enabled && cancelButton.Visible; operationSet = Field<CancellationTokenSource?>(main, "operation") != null;
                    throw new PreparationStopped();
                };
                if (readyBefore)
                {
                    Task started;
                    downloadButton.EnabledChanged += stopBeforeNetwork;
                    try { started = (Task)Call(main, "RecheckModelAsync")!; }
                    finally { downloadButton.EnabledChanged -= stopBeforeNetwork; }
                    if (started.IsFaulted) faultType = started.Exception!.InnerException?.GetType().Name;
                    else { Field<CancellationTokenSource?>(main, "operation")?.Cancel(); try { Wait(started); } catch { } }
                    Field<CancellationTokenSource?>(main, "operation")?.Dispose(); SetField(main, "operation", null);
                    if (session.Current != DictationState.Ready) session.Move(DictationState.Ready);
                    Invoke(main, "UpdateUi", "Bereit");
                }
                Check("offline-recheck-enters-setup-without-fake-download-cancel", readyBefore && hookFired && !cancelShown && !operationSet && faultType == nameof(PreparationStopped), new { readyBefore, hookFired, cancelShown, operationSet, faultType });
                var levelBar = Field<ProgressBar>(main, "level"); var downloadBar = Field<ProgressBar>(main, "progress");
                Check("progress-bars-have-names", levelBar.AccessibleName == "Mikrofonpegel" && levelBar.AccessibleDescription == "Zeigt, ob das Mikrofon etwas hört" && downloadBar.AccessibleName == "Download-Fortschritt Sprachmodell",
                    new { level = levelBar.AccessibleName, levelDescription = levelBar.AccessibleDescription, download = downloadBar.AccessibleName });
            }
            // Sprint B: Beenden-Schutz (S2), Bereichsnamen (S13), Schließverhalten (T1), Beschriftungen (S1), Modellzustände (S3).
            string recoveryFile = Path.Combine(AppPaths.Root, "uncopied.bin");
            string importFile = Path.Combine(AppPaths.Root, "import-result.bin");
            string laborFile = Path.Combine(AppPaths.Root, "labor-result.bin");
            string draftFile = Path.Combine(AppPaths.Root, "prompt-draft.bin");
            byte[] damagedRecovery = [4, 2, 7];
            const string uncopiedText = "Noch nicht kopierter Testtext.";
            RecoveryStore.Save(uncopiedText);
            using (var main = new MainForm(false))
            {
                Invoke(main, "LoadRecovery");
                var session = Field<SessionState>(main, "state");
                var transcript = Field<TextBox>(main, "transcript");
                using (var lockedRecovery = File.Open(recoveryFile, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Wait((Task)Call(main, "ExitAsync")!);
                    string status = Field<Label>(main, "status").Text;
                    Check("exit-blocked-when-uncopied-text-cannot-be-saved", !main.IsDisposed && !Field<bool>(main, "exiting") && status.Contains("Beenden angehalten") && transcript.Text == uncopiedText && session.HasUncopiedText, status);
                }
            }
            RecoveryStore.Clear();
            using (var main = new MainForm(false))
            {
                var session = Field<SessionState>(main, "state");
                var transcript = Field<TextBox>(main, "transcript");
                const string visibleText = "Sichtbarer Text kurz vor dem Beenden.";
                transcript.Text = visibleText;
                RecoveryStore.Clear();
                bool mayContinue = (bool)Call(main, "PersistUncopiedTextBeforeExit")!;
                Check("exit-saves-uncopied-text-before-closing", mayContinue && session.HasUncopiedText && RecoveryStore.Load() == visibleText, new { mayContinue, stored = RecoveryStore.Load() });
                byte[] storedBefore = File.ReadAllBytes(recoveryFile);
                SetField(main, "settingTranscript", true);
                transcript.Clear();
                SetField(main, "settingTranscript", false);
                session.HasUncopiedText = true;
                bool emptyAccepted = (bool)Call(main, "PersistUncopiedTextBeforeExit")!;
                Check("exit-never-writes-empty-text-over-existing-recovery", !emptyAccepted && File.ReadAllBytes(recoveryFile).SequenceEqual(storedBefore) && Field<Label>(main, "status").Text.Contains("verwerfen"), new { emptyAccepted, status = Field<Label>(main, "status").Text });
                SetField(main, "copyBusy", true);
                Wait((Task)Call(main, "ExitAsync")!);
                string waitingStatus = Field<Label>(main, "status").Text;
                SetField(main, "copyBusy", false);
                Check("exit-during-copy-explains-instead-of-silence", waitingStatus.Contains("Beenden wartet") && !main.IsDisposed && !Field<bool>(main, "exiting"), waitingStatus);
                File.WriteAllBytes(recoveryFile, damagedRecovery);
                Invoke(main, "LoadRecovery");
                bool unreadable = Field<bool>(main, "recoveryUnavailable");
                bool notBlockedByThis = (bool)Call(main, "PersistUncopiedTextBeforeExit")!;
                Check("damaged-recovery-never-overwritten-on-exit", unreadable && notBlockedByThis && File.ReadAllBytes(recoveryFile).SequenceEqual(damagedRecovery), new { unreadable, notBlockedByThis });
            }
            RecoveryStore.Clear();
            using (var main = new MainForm(false))
            {
                var session = Field<SessionState>(main, "state");
                var discard = Field<Button>(main, "discard");
                string fixtureAudio = Path.Combine(AppPaths.Audio, "sprint-b-fixture.wav");
                SetField(main, "modelReady", true);
                session.HasUncopiedText = true; session.HasPendingAudio = true; SetField(main, "pending", fixtureAudio);
                Invoke(main, "UpdateUi", "Bereit");
                string bothLabel = discard.Text;
                string bothReason = Field<Label>(main, "status").Text;
                session.HasPendingAudio = false; SetField(main, "pending", null);
                Invoke(main, "UpdateUi", "Bereit");
                string textLabel = discard.Text;
                session.HasUncopiedText = false; session.HasPendingAudio = true; SetField(main, "pending", fixtureAudio);
                Invoke(main, "UpdateUi", "Bereit");
                string audioLabel = discard.Text;
                Check("discard-label-names-both-losses", bothLabel == "Ergebnis und Aufnahme verwerfen" && textLabel == "Ergebnis verwerfen" && audioLabel == "Aufnahme verwerfen", new { bothLabel, textLabel, audioLabel });
                Check("block-reason-names-the-current-discard-label", bothReason.Contains(bothLabel) && Field<TextBox>(main, "transcript").Text.Length == 0, new { bothReason, bothLabel });
            }
            using (var main = new MainForm(false))
            {
                var importsPage = Field<AudioToolsPage>(main, "imports");
                var tabs = Field<TabControl>(main, "tabs");
                if (!File.Exists(importFile)) ProtectedFile.Write(importFile, "");
                using (var lockedImport = File.Open(importFile, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Field<TextBox>(importsPage, "result").Text = "Synthetisches Ergebnis im Audiobereich.";
                    Wait((Task)Call(main, "ExitAsync")!);
                    string status = Field<Label>(main, "status").Text;
                    Check("exit-names-and-selects-the-failing-area", status.Contains("Audiodateien") && tabs.SelectedTab?.Text == "Audiodateien" && !main.IsDisposed && !Field<bool>(main, "exiting"), new { status, tab = tabs.SelectedTab?.Text });
                }
            }
            const string laborText = "Synthetisches Laborergebnis.";
            const string draftText = "Synthetischer Werkstattentwurf.";
            using (var main = new MainForm(false))
            {
                var importsPage = Field<AudioToolsPage>(main, "imports");
                Field<TextBox>(Field<AudioToolsPage>(main, "laboratory"), "result").Text = laborText;
                Field<TextBox>(Field<PromptPage>(main, "workshop"), "draft").Text = draftText;
                if (File.Exists(laborFile)) File.Delete(laborFile);
                if (File.Exists(draftFile)) File.Delete(draftFile);
                using (var lockedImport = File.Open(importFile, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Field<TextBox>(importsPage, "result").Text = "Erneut synthetisches Ergebnis im Audiobereich.";
                    Wait((Task)Call(main, "ExitAsync")!);
                    Check("exit-flushes-all-areas-even-after-the-first-failure", ProtectedFile.Read<string>(laborFile) == laborText && ProtectedFile.Read<PromptDocument>(draftFile)?.Draft == draftText, new { labor = ProtectedFile.Read<string>(laborFile), draft = ProtectedFile.Read<PromptDocument>(draftFile)?.Draft });
                }
            }
            using (var main = new MainForm(false))
            {
                var tabs = Field<TabControl>(main, "tabs");
                if (!File.Exists(importFile)) ProtectedFile.Write(importFile, "");
                if (!File.Exists(laborFile)) ProtectedFile.Write(laborFile, "");
                using (var lockedImport = File.Open(importFile, FileMode.Open, FileAccess.Read, FileShare.None))
                using (var lockedLabor = File.Open(laborFile, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Field<TextBox>(Field<AudioToolsPage>(main, "imports"), "result").Text = "Gesperrtes Audioergebnis.";
                    Field<TextBox>(Field<AudioToolsPage>(main, "laboratory"), "result").Text = "Gesperrtes Laborergebnis.";
                    Wait((Task)Call(main, "ExitAsync")!);
                    string status = Field<Label>(main, "status").Text;
                    Check("exit-lists-every-failing-area", status.Contains("Audiodateien") && status.Contains("Sprachlabor") && tabs.SelectedTab?.Text == "Audiodateien", new { status, tab = tabs.SelectedTab?.Text });
                }
            }
            using (var main = new MainForm(false))
            {
                var userClose = new FormClosingEventArgs(CloseReason.UserClosing, false);
                Invoke(main, "OnFormClosing", userClose);
                Check("user-close-is-cancelled-and-starts-the-exit", userClose.Cancel && !main.IsDisposed, userClose.Cancel);
                var shutdown = new FormClosingEventArgs(CloseReason.WindowsShutDown, false);
                Invoke(main, "OnFormClosing", shutdown);
                Check("windows-shutdown-is-not-cancelled", !shutdown.Cancel && !main.IsDisposed, shutdown.Cancel);
            }
            File.WriteAllBytes(recoveryFile, damagedRecovery);
            using (var main = new MainForm(false))
            {
                Invoke(main, "LoadRecovery");
                var shutdown = new FormClosingEventArgs(CloseReason.WindowsShutDown, false);
                Invoke(main, "OnFormClosing", shutdown);
                Check("windows-shutdown-never-overwrites-damaged-recovery", !shutdown.Cancel && File.ReadAllBytes(recoveryFile).SequenceEqual(damagedRecovery), Field<bool>(main, "recoveryUnavailable"));
            }
            RecoveryStore.Clear();
            using (var main = new MainForm(false))
            {
                // This component binary intentionally lacks the bundled Moonshine runtime.
                Wait((Task)Call(main, "RefreshModelAsync", CancellationToken.None)!);
                string notice = (string)Call(main, "ModelNotice")!;
                Check("unavailable-bundled-runtime-advises-recheck", !Field<bool>(main, "modelReady") && Field<bool>(main, "modelBlocked") && notice.Contains("momentan nicht bereit") && notice.Contains("erneut prüfen") && !notice.Contains("herunterladen"), notice);
                Wait((Task)Call(main, "RefreshModelAsync", CancellationToken.None)!);
                string missingNotice = (string)Call(main, "ModelNotice")!;
                Check("recheck-without-runtime-remains-blocked", !Field<bool>(main, "modelReady") && Field<bool>(main, "modelBlocked") && missingNotice == notice, missingNotice);
            }
            // Sprint F (F2): „Modell herunterladen (488 MB)“ ist nur wählbar, wenn das Modell fehlt oder beschädigt ist.
            using (var main = new MainForm(false))
            {
                var downloadButton = Field<Button>(main, "modelDownload");
                SetField(main, "modelChecked", true);
                SetField(main, "modelReady", true); SetField(main, "modelBlocked", false); Invoke(main, "UpdateUi", "Bereit");
                bool checkedOff = !downloadButton.Enabled;
                SetField(main, "modelReady", false); Invoke(main, "UpdateUi", "Bereit");
                bool missingOn = downloadButton.Enabled;
                SetField(main, "modelBlocked", true); Invoke(main, "UpdateUi", "Bereit");
                bool blockedOff = !downloadButton.Enabled;
                Check("model-download-offered-only-when-needed", checkedOff && missingOn && blockedOff, new { checkedOff, missingOn, blockedOff });
                // Sprint F (F5): Eine gesperrte Datei im Diktatweg wird auf Deutsch gemeldet, ohne englischen Rahmentext und ohne Pfad; eigene deutsche Meldungen bleiben.
                var failureNotice = typeof(MainForm).GetMethod("FailureNotice", BindingFlags.Static | BindingFlags.NonPublic);
                string? DictationNotice(Exception e) => failureNotice?.Invoke(null, [e]) as string;
                string? lockedNotice = typeof(MainForm).GetField("FileLockedNotice", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as string;
                const string germanIo = "Synthetische deutsche Dateimeldung.";
                const string germanInvalid = "Modell fehlt oder ist beschädigt. Bitte erneut herunterladen; Aufnahme bleibt erhalten.";
                string? denied = DictationNotice(new UnauthorizedAccessException(@"Access to the path 'C:\Users\Synthetisch\AppData\Local\Klartext\uncopied.bin' is denied."));
                string? io = DictationNotice(new IOException(germanIo));
                string? invalid = DictationNotice(new InvalidDataException(germanInvalid));
                Check("dictation-denied-file-gets-german-notice", failureNotice != null && lockedNotice != null && denied != null && denied == lockedNotice && !denied.Contains("Access") && !denied.Contains('\\') && !denied.Contains("uncopied") && io == germanIo && invalid == germanInvalid,
                    new { found = failureNotice != null, denied, io, invalid });
            }
            // Sprint F (F6): Vor der ersten Modellprüfung steht oben „Modell wird geprüft“, nicht „Modell fehlt“.
            RecoveryStore.Clear();
            using (var main = new MainForm(false))
            {
                var summary = Field<Label>(main, "stateSummary");
                string beforeCheck = summary.Text;
                Wait((Task)Call(main, "RefreshModelAsync", CancellationToken.None)!);
                Invoke(main, "UpdateUi", "Bereit");
                string afterCheck = summary.Text;
                Check("state-summary-says-checking-before-first-check", beforeCheck == "● Modell wird geprüft" && afterCheck == "● Modell nicht lesbar", new { beforeCheck, afterCheck, modelPresent = File.Exists(AppPaths.Model) });
            }
            // Sprint F (F7): „Ergebnis erneut laden“ meldet eine leere Sicherung als leer; mit Text bleibt die bisherige Meldung.
            using (var main = new MainForm(false))
            {
                var reload = typeof(MainForm).GetMethod("ReloadRecovery", BindingFlags.Instance | BindingFlags.NonPublic);
                var restored = typeof(MainForm).GetField("RestoredResultNotice", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as string;
                var statusLabel = Field<Label>(main, "status");
                SetField(main, "modelReady", true);
                string? emptyStatus = null, textStatus = null;
                if (reload != null)
                {
                    RecoveryStore.Save(" "); SetField(main, "recoveryUnavailable", true);
                    reload.Invoke(main, null); emptyStatus = statusLabel.Text;
                    RecoveryStore.Save("Synthetischer wiederhergestellter Text."); SetField(main, "recoveryUnavailable", true);
                    reload.Invoke(main, null); textStatus = statusLabel.Text;
                }
                RecoveryStore.Clear();
                Check("empty-reload-says-nothing-to-copy", reload != null && restored != null && emptyStatus != null && emptyStatus.Contains("es gibt nichts zu kopieren") && !emptyStatus.Contains("Text kopieren und freigeben") && textStatus != null && textStatus.Contains(restored),
                    new { found = reload != null, emptyStatus, textStatus });
            }
            foreach (string written in new[] { recoveryFile, importFile, laborFile, draftFile })
                if (File.Exists(written)) File.Delete(written);
            var gate = new ActivityGate(() => true);
            using (var page = new PromptPage(gate, () => ""))
            {
                var draft = Field<TextBox>(page, "draft"); var original = Field<TextBox>(page, "original"); var findings = Field<Label>(page, "findings");
                original.Text = "Nenne drei Blumen."; draft.Text = original.Text; Invoke(page, "Check");
                Check("formal-result-neutral-not-perfect", findings.Text.Contains("Keine der begrenzten") && !findings.Text.Contains("Grün"));
                draft.Clear(); Check("editing-invalidates-old-check", findings.Text.StartsWith("Text geändert"));
                draft.Text = "Nenne drei Blumen."; Invoke(page, "Check");
                Invoke(page, "LoadDocument", new PromptDocument(Original: "[TEXT EINFÜGEN]"));
                Check("import-invalidates-old-check", findings.Text == "Noch nicht geprüft.");
                Check("working-draft-saved", page.Flush());
                Check("recovery-exact-original", ProtectedFile.Read<PromptDocument>(Path.Combine(AppPaths.Root, "prompt-draft.bin"))?.Original == "[TEXT EINFÜGEN]");
                var doc = new PromptDocument(Original: "Also ich brauche eine kurze Einladung zum Gartenfest.", Goal: "Einladung schreiben", Audience: "Nachbarn", Format: "Kurzer Text", Constraints: "Keine Emojis, höchstens 80 Wörter.", Draft: "Schreibe eine Einladung zum Gartenfest. Keine Emojis. Höchstens 80 Wörter.", Title: "Gartenfest");
                Invoke(page, "LoadDocument", doc);
                Check("no-stale-progress-at-idle", Field<ProgressBar>(page, "progress").Value == 0 && !Field<Button>(page, "cancelAi").Enabled);
                Check("cannot-adopt-empty-or-unbound-suggestion", !Field<Button>(page, "adopt").Enabled);
                Wait((Task)typeof(PromptPage).GetMethod("VerifyModelAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!);
                Check("verification-completion-clears-running-message", Field<Label>(page, "workStatus").Text.StartsWith("Abgeschlossen") && !Field<Label>(page, "info").Text.Contains("Abbrechen ist möglich"));
                var completion = new TaskCompletionSource();
                IProgress<PromptProgress>? reporter = null;
                var fakeJob = Job(page, async (token, report) => { reporter = report; report.Report(new("Synthetische Dateiprüfung", 42)); await completion.Task.WaitAsync(token); });
                PumpUntil(() => Field<ProgressBar>(page, "progress").Value == 42);
                Check("measured-step-progress-and-elapsed-visible", Field<Label>(page, "workStatus").Text.Contains("42 % dieses Schritts") && Field<Label>(page, "workStatus").Text.Contains("Sekunden"));
                Check("busy-disables-competing-actions", !Field<Button>(page, "generate").Enabled && !Field<Button>(page, "verify").Enabled && !Field<Button>(page, "adopt").Enabled && Field<Button>(page, "cancelAi").Enabled && draft.ReadOnly && draft.Enabled);
                reporter!.Report(new("Vorschlag formulieren")); PumpUntil(() => Field<ProgressBar>(page, "progress").Style == ProgressBarStyle.Marquee);
                Check("generation-has-no-fake-percentage", !Field<Label>(page, "workStatus").Text.Contains("%"));
                gate.Cancel(); Wait(fakeJob);
                Check("cancel-finishes-and-restores-editing", !gate.Busy && draft.Enabled && !draft.ReadOnly && !Field<Button>(page, "cancelAi").Enabled && Field<Label>(page, "workStatus").Text.StartsWith("Abgebrochen"));
                reporter.Report(new("Stale callback", 99)); Application.DoEvents();
                Check("late-progress-cannot-revive-finished-job", Field<ProgressBar>(page, "progress").Value == 0 && !Field<Label>(page, "workStatus").Text.Contains("Stale"));
                string preservedDraft = draft.Text;
                Wait(Job(page, (_, _) => throw new IOException("Synthetic failure")));
                Check("failure-preserves-draft-and-restores-actions", draft.Text == preservedDraft && draft.Enabled && !draft.ReadOnly && !gate.Busy && Field<Label>(page, "workStatus").Text.StartsWith("Nicht abgeschlossen"));
                using (var other = gate.TryEnter("Andere Testfunktion")) Check("other-job-disables-workshop-actions", !Field<Button>(page, "verify").Enabled && !Field<Button>(page, "cancelAi").Enabled);
                Check("other-job-release-restores-actions", Field<Button>(page, "verify").Enabled);
                using var host = new Panel { Size = new Size(1440, 900), Font = BrandTheme.Body }; host.Controls.Add(page); page.Dock = DockStyle.Fill; BrandTheme.Apply(host); host.CreateControl(); host.PerformLayout();
                var workspace = Field<Panel>(page, "workspace");
                var views = Field<TabControl>(page, "views");
                Check("exactly-creation-and-library-tabs", views.TabCount == 2 && views.TabPages[0].Text == "Erstellung" && views.TabPages[1].Text == "Bibliothek" && Descendants(page).OfType<TabControl>().Count() == 1);
                var supplements = Field<GardenExpander>(page, "supplementsBlock");
                Field<GardenExpander>(page, "reviewBlock").Expanded = true; Field<GardenExpander>(page, "sourceBlock").Expanded = true;
                supplements.Expanded = true;
                Check("all-editors-present-when-extras-open", new[] { "original", "goal", "audience", "format", "constraints", "draft" }.All(name => Field<TextBox>(page, name).Visible && Field<TextBox>(page, name).ReadOnly == (name == "original")));
                supplements.Expanded = false;
                Check("no-empty-result-box-before-selection", !Field<TextBox>(page, "candidate").Visible && !Field<TextBox>(page, "reviewAnswer").Visible);
                Check("creation-remains-one-common-scroll-page", !Descendants(workspace).OfType<ScrollableControl>().Any(c => c.AutoScroll) && workspace.AutoScroll);
                foreach (var size in new[] { new Size(1440, 900), new Size(760, 640), new Size(1440, 900) })
                {
                    host.Size = size; workspace.AutoScrollPosition = Point.Empty; host.PerformLayout(); Invoke(page, "ArrangeWorkspace"); Application.DoEvents();
                    var inputColumn = Field<FlowLayoutPanel>(page, "inputColumn"); var outputColumn = Field<FlowLayoutPanel>(page, "outputColumn");
                    bool wide = size.Width == 1440;
                    Check("responsive-arrangement-" + size.Width, !outputColumn.Visible && draft.Width > workspace.ClientSize.Width * .8, new { inputColumn.Bounds, Output = outputColumn.Bounds });
                    Check("no-horizontal-page-overflow-" + size.Width, !workspace.HorizontalScroll.Visible && draft.Width > 300 && original.Width > 300, new { workspace.ClientSize, workspace.DisplayRectangle });
                    using var bitmap = new Bitmap(host.Width, host.Height); host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.Size)); bitmap.Save(Path.Combine(folder, "workshop-" + size.Width + ".png"), ImageFormat.Png);
                }
                var candidate = Field<TextBox>(page, "candidate");
                var questionReview = new PromptReview("Eine Veranstaltung planen.", [new("Welche Dauer gilt?", "Die Zeiten widersprechen sich.", ["30 Minuten", "80 Minuten"])], []);
                Invoke(page, "SetReview", questionReview, draft.Text);
                var answer = Field<TextBox>(page, "reviewAnswer"); var options = Field<ComboBox>(page, "reviewOptions");
                Check("review-does-not-change-draft-or-preselect", draft.Text == doc.Draft && options.SelectedIndex == -1 && answer.Text == "" && !Field<Button>(page, "adopt").Enabled);
                options.SelectedIndex = 1;
                Check("selected-answer-only-prepares-preview", answer.Text == "80 Minuten" && draft.Text == doc.Draft && candidate.Text.StartsWith(doc.Draft, StringComparison.Ordinal) && Field<Button>(page, "adopt").Enabled);
                answer.AppendText(" inklusive Pausen."); page.Flush();
                var recovered = ProtectedFile.Read<PromptDocument>(Path.Combine(AppPaths.Root, "prompt-draft.bin"))!;
                Invoke(page, "LoadDocument", recovered);
                Check("review-and-answer-survive-reload", Field<TextBox>(page, "reviewAnswer").Text.EndsWith("inklusive Pausen.") && candidate.Text == recovered.Candidate && Field<Button>(page, "adopt").Enabled);
                draft.Text += " Neu bearbeitet.";
                Check("edited-draft-disables-stale-review-answer", !answer.Enabled && !options.Enabled && !Field<Button>(page, "adopt").Enabled);
                Invoke(page, "LoadDocument", doc);
                Invoke(page, "SetReview", new PromptReview("Ein einfacher Auftrag.", [], []), draft.Text);
                Check("no-findings-is-visible-not-perfect", Field<Label>(page, "candidateInfo").Text.Contains("nicht") && Field<Label>(page, "candidateInfo").Text.Contains("perfekt") && !candidate.Visible);
                Invoke(page, "LoadDocument", doc);
                candidate.Text = "Überarbeiteter synthetischer Vorschlag.";
                typeof(PromptPage).GetField("candidateSource", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, draft.Text); Invoke(page, "RefreshActions");
                candidate.AppendText(" Manuell ergänzt.");
                Check("candidate-edit-keeps-original-and-draft", original.Text == doc.Original && draft.Text == doc.Draft && Field<Button>(page, "adopt").Enabled);
                Field<Button>(page, "adopt").PerformClick();
                Check("edited-suggestion-adopted-only-on-click", draft.Text == candidate.Text && original.Text == doc.Original && !Field<Button>(page, "adopt").Enabled);
                draft.Text = "Ein später manuell bearbeiteter Prompt.";
                Check("draft-edit-cannot-adopt-stale-suggestion", !Field<Button>(page, "adopt").Enabled);
                Field<TextBox>(page, "title").Text = "Einseitige Werkstatt"; Invoke(page, "SaveLibrary", true);
                Check("library-save-from-shared-page", Field<ListBox>(page, "libraryList").Items.Count == 1);
                views.SelectedIndex = 1; Application.DoEvents();
                var libraryWorkspace = Field<Panel>(page, "libraryWorkspace");
                var library = Field<ListBox>(page, "libraryList"); library.SelectedIndex = 0;
                var preview = Field<TextBox>(page, "libraryPreview");
                Check("library-preview-without-replacing-draft", preview.ReadOnly && preview.Text == draft.Text && preview.Visible && views.SelectedIndex == 1);
                libraryWorkspace.ScrollControlIntoView(library); Application.DoEvents();
                var libraryBounds = libraryWorkspace.RectangleToClient(library.RectangleToScreen(library.ClientRectangle));
                Check("library-reachable-in-own-tab", libraryWorkspace.ClientRectangle.IntersectsWith(libraryBounds));
                var versionsBox = Field<ComboBox>(page, "versions"); var previewBox = Field<GardenTextBox>(page, "libraryPreview");
                int previewInset = (int)Math.Ceiling(3 * previewBox.DeviceDpi / 96f);
                Check("library-versions-clear-of-preview", versionsBox.Parent == previewBox.Parent && versionsBox.Bottom + Math.Max(previewInset, versionsBox.Margin.Bottom + previewBox.Margin.Top) <= previewBox.Top,
                    new { versions = versionsBox.Bounds, preview = previewBox.Bounds, versionsBottom = versionsBox.Bottom, previewTop = previewBox.Top, frameInset = previewInset, marginGap = versionsBox.Margin.Bottom + previewBox.Margin.Top, versionsBox.Height, versionsBox.PreferredHeight, versionsBox.ItemHeight, font = versionsBox.Font.Name + " " + versionsBox.Font.SizeInPoints, versionsBox.DeviceDpi, versionsBox.SelectedIndex, versionsText = versionsBox.Text, versionsBox.Items.Count });
                // Sprint E (K3): Der Erfolgstext der Dateiprüfung sagt, was geprüft wurde; im Profil liegt kein Promptmodell, deshalb über die reine Methode.
                string checkedMessage = typeof(PromptPage).GetMethod("VerificationMessage", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, [false, true]) as string ?? "";
                Check("file-check-message-names-what-was-checked", checkedMessage.Contains("Prüfwert") && checkedMessage.Contains("vorhanden"), checkedMessage);
                // Sprint E (ChangeFavorite): Ein Direktaufruf bei fremdem Auftrag ändert den Favoritenstatus nicht; der Knopf ist dann ohnehin gesperrt.
                var favoriteLibrary = Field<PromptLibrary>(page, "library");
                bool entrySelected = library.SelectedItem is PromptEntry;
                bool favoriteBefore = favoriteLibrary.Entries[0].Favorite;
                using (var foreign = gate.TryEnter("Synthetischer Fremdauftrag")) Invoke(page, "ChangeFavorite");
                bool favoriteAfter = favoriteLibrary.Entries[0].Favorite;
                Check("favorite-direct-call-respects-running-job", entrySelected && favoriteBefore == favoriteAfter, new { entrySelected, favoriteBefore, favoriteAfter });
                // Sprint E, Lücke über der Versionsauswahl: Abstand gleich der Summe der Margins (±1); die angeforderte Höhe hält ListBox privat, der Feldname wird gesucht.
                var requestedField = typeof(ListBox).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).FirstOrDefault(f => f.FieldType == typeof(int) && f.Name.Contains("requested", StringComparison.OrdinalIgnoreCase) && f.Name.Contains("height", StringComparison.OrdinalIgnoreCase));
                int? requestedHeight = (int?)requestedField?.GetValue(library);
                int listMargins = library.Margin.Bottom + versionsBox.Margin.Top, listGap = versionsBox.Top - library.Bottom;
                Check("library-list-meets-versions", library.Parent == versionsBox.Parent && Math.Abs(listGap - listMargins) <= 1,
                    new { listBounds = library.Bounds, library.Height, requestedField = requestedField?.Name, requestedHeight, library.ItemHeight, library.IntegralHeight, library.PreferredHeight, library.Items.Count, versionsTop = versionsBox.Top, gap = listGap, margins = listMargins, beyondMargins = listGap - listMargins, requestedMinusActual = requestedHeight - library.Height, library.DeviceDpi });
                using (var bitmap = new Bitmap(host.Width, host.Height)) { host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.Size)); bitmap.Save(Path.Combine(folder, "workshop-library.png"), ImageFormat.Png); }
                views.SelectedIndex = 0;
                Check("shared-page-draft-persists", page.Flush() && ProtectedFile.Read<PromptDocument>(Path.Combine(AppPaths.Root, "prompt-draft.bin"))?.Draft == draft.Text);
                foreach (var name in new[] { "supplementsBlock", "saveBlock", "connectionBlock", "helpBlock", "reviewBlock", "sourceBlock" }) Field<GardenExpander>(page, name).Expanded = false;
                SprintCWorkshopChecks(page, host, gate, Check);
            }
            string promptPath = Path.Combine(AppPaths.Root, "prompt-draft.bin"); byte[] bad = [3, 1, 4]; File.WriteAllBytes(promptPath, bad);
            using (var damaged = new PromptPage(gate, () => "")) { Check("damaged-draft-allows-safe-exit", damaged.Flush()); Check("damaged-draft-preserved", File.ReadAllBytes(promptPath).SequenceEqual(bad)); }
            string importPath = Path.Combine(AppPaths.Root, "import-result.bin"); File.WriteAllBytes(importPath, bad);
            using (var audio = new AudioToolsPage(false, gate, () => null)) { Check("damaged-audio-recovery-allows-safe-exit", audio.Flush()); Check("damaged-audio-recovery-preserved", File.ReadAllBytes(importPath).SequenceEqual(bad)); }
            // Sprint A, M1 und M2: Rückweg aus dem gesperrten Zustand, ohne die unlesbare Datei anzufassen.
            string freshPromptPath = Path.Combine(AppPaths.Root, "prompt-draft-neu.bin");
            string laborPath = Path.Combine(AppPaths.Root, "labor-result.bin");
            string freshImportPath = Path.Combine(AppPaths.Root, "import-result-neu.bin");
            string freshLaborPath = Path.Combine(AppPaths.Root, "labor-result-neu.bin");
            foreach (string stale in new[] { freshPromptPath, freshImportPath, freshLaborPath }) if (File.Exists(stale)) File.Delete(stale);
            File.WriteAllBytes(promptPath, bad);
            string freshDraftText = "Synthetischer Entwurf nach dem Neuanfang.";
            using (var locked = new PromptPage(gate, () => ""))
            {
                var lockedInfo = Field<Label>(locked, "info");
                Check("damaged-draft-names-file-not-operation", lockedInfo.Text.Contains("prompt-draft") && !lockedInfo.Text.Contains("Vorgang"), lockedInfo.Text);
                var reload = Field<Button>(locked, "reloadDraft"); var fresh = Field<Button>(locked, "freshDraft");
                Check("damaged-draft-offers-reload-and-fresh-start", Field<FlowLayoutPanel>(locked, "recoveryRow").Visible && reload.Visible && reload.Enabled && fresh.Visible && fresh.Enabled);
                var loadVersion = Descendants(locked).OfType<Button>().Single(b => b.Text == "Version laden");
                Check("damaged-draft-keeps-library-actions", Field<Button>(locked, "favorite").Enabled && Field<Button>(locked, "deleteEntry").Enabled && !loadVersion.Enabled && !Field<TextBox>(locked, "draft").Enabled);
                locked.ImportText("Synthetischer Text aus einem anderen Bereich.");
                Check("to-workshop-from-damaged-draft-names-the-reason", lockedInfo.Text.Contains("prompt-draft") && !lockedInfo.Text.Contains("Vorgang läuft"), lockedInfo.Text);
                Invoke(locked, "StartFreshDraft");
                var lockedDraft = Field<TextBox>(locked, "draft");
                Check("fresh-draft-restores-editing-and-preserves-file", !Field<bool>(locked, "damagedDraft") && lockedDraft.Enabled && lockedDraft.Text.Length == 0 && locked.Flush() && File.ReadAllBytes(promptPath).SequenceEqual(bad));
                lockedDraft.Text = freshDraftText; locked.Flush();
                Check("fresh-draft-persists-to-second-file", ProtectedFile.Read<PromptDocument>(freshPromptPath)?.Draft == freshDraftText && File.ReadAllBytes(promptPath).SequenceEqual(bad));
            }
            using (var restarted = new PromptPage(gate, () => ""))
                Check("restart-prefers-fresh-draft-file", !Field<bool>(restarted, "damagedDraft") && Field<TextBox>(restarted, "draft").Text == freshDraftText && File.ReadAllBytes(promptPath).SequenceEqual(bad));
            byte[] badFresh = [7, 7, 7]; File.WriteAllBytes(freshPromptPath, badFresh);
            using (var bothDamaged = new PromptPage(gate, () => ""))
            {
                var bothInfo = Field<Label>(bothDamaged, "info");
                Check("both-draft-files-damaged-hides-fresh-start", Field<FlowLayoutPanel>(bothDamaged, "recoveryRow").Visible && Field<Button>(bothDamaged, "reloadDraft").Visible && !Field<Button>(bothDamaged, "freshDraft").Visible && bothInfo.Text.Contains("prompt-draft.bin") && bothInfo.Text.Contains("prompt-draft-neu.bin") && File.ReadAllBytes(promptPath).SequenceEqual(bad) && File.ReadAllBytes(freshPromptPath).SequenceEqual(badFresh), bothInfo.Text);
            }
            File.WriteAllBytes(importPath, bad);
            string freshResultText = "Synthetisches Ergebnis nach dem Neuanfang.";
            using (var lockedImport = new AudioToolsPage(false, gate, () => null))
            {
                string importMessage = Field<Label>(lockedImport, "info").Text;
                bool importOffers = Field<FlowLayoutPanel>(lockedImport, "recoveryActions").Visible && Field<Button>(lockedImport, "reloadResult").Visible && Field<Button>(lockedImport, "freshResult").Visible;
                Invoke(lockedImport, "StartFreshResult");
                var importResult = Field<TextBox>(lockedImport, "result");
                bool importReleased = !Field<bool>(lockedImport, "damagedRecovery") && !importResult.ReadOnly && importResult.Text.Length == 0;
                importResult.Text = freshResultText;
                Check("damaged-import-result-names-file-not-operation", importMessage.Contains("import-result") && !importMessage.Contains("Zuerst aktuellen Vorgang abschließen.") && importOffers && importReleased && lockedImport.Flush() && ProtectedFile.Read<string>(freshImportPath) == freshResultText && File.ReadAllBytes(importPath).SequenceEqual(bad), new { importMessage, importOffers, importReleased });
            }
            File.WriteAllBytes(laborPath, bad);
            using (var lockedLabor = new AudioToolsPage(true, gate, () => null))
            {
                string laborMessage = Field<Label>(lockedLabor, "info").Text;
                bool laborOffers = Field<FlowLayoutPanel>(lockedLabor, "recoveryActions").Visible && Field<Button>(lockedLabor, "reloadResult").Visible && Field<Button>(lockedLabor, "freshResult").Visible;
                Invoke(lockedLabor, "StartFreshResult");
                var laborResult = Field<TextBox>(lockedLabor, "result");
                bool laborReleased = !Field<bool>(lockedLabor, "damagedRecovery") && !laborResult.ReadOnly && laborResult.Text.Length == 0;
                laborResult.Text = freshResultText;
                Check("damaged-labor-result-names-file-not-operation", laborMessage.Contains("labor-result") && !laborMessage.Contains("Zuerst aktuellen Vorgang abschließen.") && laborOffers && laborReleased && lockedLabor.Flush() && ProtectedFile.Read<string>(freshLaborPath) == freshResultText && File.ReadAllBytes(laborPath).SequenceEqual(bad), new { laborMessage, laborOffers, laborReleased });
            }
            File.Delete(freshImportPath); File.WriteAllBytes(importPath, bad);
            using (var healthy = new AudioToolsPage(true, gate, () => null))
            using (var lockedAgain = new AudioToolsPage(false, gate, () => null))
            {
                Field<TextBox>(healthy, "result").Clear();
                string busyMessage;
                using (var foreign = gate.TryEnter("Synthetische Fremdaufgabe")) { Invoke(healthy, "StartRecording"); busyMessage = Field<Label>(healthy, "info").Text; }
                string lockedMessage = Field<Label>(lockedAgain, "info").Text;
                Check("audio-busy-still-reports-running-operation", busyMessage.Contains("Vorgang") && !busyMessage.Contains("labor-result") && lockedMessage.Contains("import-result") && !lockedMessage.Contains("Vorgang") && !healthy.IsRecording, new { busyMessage, lockedMessage });
            }
            // Sprint A, Phase 4: der Ladeweg führt tatsächlich zurück, Bibliothekssperre bei Fremdauftrag, Bildbelege des gesperrten Zustands.
            if (File.Exists(freshPromptPath)) File.Delete(freshPromptPath);
            File.WriteAllBytes(promptPath, bad);
            string reloadedDraftText = "Wieder lesbarer synthetischer Ausgangstext.";
            using (var recovering = new PromptPage(gate, () => ""))
            {
                bool lockedBeforeReload = Field<bool>(recovering, "damagedDraft");
                Invoke(recovering, "ReloadDraft");
                string stillLocked = Field<Label>(recovering, "info").Text;
                Check("reload-keeps-lock-when-draft-still-unreadable", lockedBeforeReload && Field<bool>(recovering, "damagedDraft") && stillLocked.Contains("prompt-draft") && stillLocked.Contains("nicht geholfen") && File.ReadAllBytes(promptPath).SequenceEqual(bad), stillLocked);
                ProtectedFile.Write(promptPath, new PromptDocument(Original: reloadedDraftText));
                Invoke(recovering, "ReloadDraft");
                Check("reload-recovers-draft-after-file-becomes-readable", !Field<bool>(recovering, "damagedDraft") && Field<TextBox>(recovering, "draft").Enabled && Field<TextBox>(recovering, "original").Text == reloadedDraftText && !Field<FlowLayoutPanel>(recovering, "recoveryRow").Visible, Field<Label>(recovering, "info").Text);
            }
            if (File.Exists(freshImportPath)) File.Delete(freshImportPath);
            File.WriteAllBytes(importPath, bad);
            string reloadedResultText = "Wieder lesbares synthetisches Ergebnis.";
            using (var recoveringAudio = new AudioToolsPage(false, gate, () => null))
            {
                bool lockedBeforeReload = Field<bool>(recoveringAudio, "damagedRecovery");
                Invoke(recoveringAudio, "ReloadResult");
                string stillLocked = Field<Label>(recoveringAudio, "info").Text;
                Check("reload-keeps-lock-when-audio-result-still-unreadable", lockedBeforeReload && Field<bool>(recoveringAudio, "damagedRecovery") && stillLocked.Contains("import-result") && stillLocked.Contains("nicht geholfen") && File.ReadAllBytes(importPath).SequenceEqual(bad), stillLocked);
                ProtectedFile.Write(importPath, reloadedResultText);
                Invoke(recoveringAudio, "ReloadResult");
                var recoveredResult = Field<TextBox>(recoveringAudio, "result");
                Check("reload-recovers-audio-result-after-file-becomes-readable", !Field<bool>(recoveringAudio, "damagedRecovery") && !recoveredResult.ReadOnly && recoveredResult.Text == reloadedResultText && !Field<FlowLayoutPanel>(recoveringAudio, "recoveryActions").Visible, Field<Label>(recoveringAudio, "info").Text);
            }
            File.WriteAllBytes(importPath, bad); File.WriteAllBytes(freshImportPath, badFresh);
            using (var bothAudio = new AudioToolsPage(false, gate, () => null))
            {
                string bothAudioMessage = Field<Label>(bothAudio, "info").Text;
                Check("both-audio-files-damaged-hides-fresh-start", Field<FlowLayoutPanel>(bothAudio, "recoveryActions").Visible && Field<Button>(bothAudio, "reloadResult").Visible && !Field<Button>(bothAudio, "freshResult").Visible && bothAudioMessage.Contains("import-result.bin") && bothAudioMessage.Contains("import-result-neu.bin") && File.ReadAllBytes(importPath).SequenceEqual(bad) && File.ReadAllBytes(freshImportPath).SequenceEqual(badFresh), bothAudioMessage);
            }
            using (var libraryPage = new PromptPage(gate, () => ""))
            {
                var favoriteButton = Field<Button>(libraryPage, "favorite"); var deleteButton = Field<Button>(libraryPage, "deleteEntry");
                bool enabledBefore = favoriteButton.Enabled && deleteButton.Enabled;
                bool disabledDuring;
                using (var other = gate.TryEnter("Synthetischer Fremdauftrag")) { Invoke(libraryPage, "RefreshActions"); disabledDuring = !favoriteButton.Enabled && !deleteButton.Enabled; }
                Invoke(libraryPage, "RefreshActions");
                Check("library-actions-disabled-while-other-job-runs", enabledBefore && disabledDuring && favoriteButton.Enabled && deleteButton.Enabled, new { enabledBefore, disabledDuring });
            }
            File.WriteAllBytes(promptPath, bad); File.WriteAllBytes(importPath, bad); File.WriteAllBytes(laborPath, bad);
            foreach (string stale in new[] { freshPromptPath, freshImportPath, freshLaborPath }) if (File.Exists(stale)) File.Delete(stale);
            using (var damagedWorkshop = new PromptPage(gate, () => ""))
            using (var workshopHost = new Panel { Size = new Size(900, 740), Font = BrandTheme.Body })
            {
                workshopHost.Controls.Add(damagedWorkshop); damagedWorkshop.Dock = DockStyle.Fill; BrandTheme.Apply(workshopHost); workshopHost.CreateControl(); workshopHost.PerformLayout();
                var render = RenderExitButtons(workshopHost, Field<Button>(damagedWorkshop, "reloadDraft"), Field<Button>(damagedWorkshop, "freshDraft"), Path.Combine(folder, "workshop-damaged-900.png"));
                Check("damaged-workshop-exit-buttons-visible-900", render.Pass, render.Evidence);
            }
            using (var damagedImportPage = new AudioToolsPage(false, gate, () => null))
            using (var importHost = new Panel { Size = new Size(900, 740), Font = BrandTheme.Body })
            {
                importHost.Controls.Add(damagedImportPage); damagedImportPage.Dock = DockStyle.Fill; BrandTheme.Apply(importHost); importHost.CreateControl(); importHost.PerformLayout();
                var render = RenderExitButtons(importHost, Field<Button>(damagedImportPage, "reloadResult"), Field<Button>(damagedImportPage, "freshResult"), Path.Combine(folder, "audio-damaged-900.png"));
                Check("damaged-audio-exit-buttons-visible-900", render.Pass, render.Evidence);
            }
            using (var damagedLaborPage = new AudioToolsPage(true, gate, () => null))
            using (var laborHost = new Panel { Size = new Size(900, 740), Font = BrandTheme.Body })
            {
                laborHost.Controls.Add(damagedLaborPage); damagedLaborPage.Dock = DockStyle.Fill; BrandTheme.Apply(laborHost); laborHost.CreateControl(); laborHost.PerformLayout();
                var render = RenderExitButtons(laborHost, Field<Button>(damagedLaborPage, "reloadResult"), Field<Button>(damagedLaborPage, "freshResult"), Path.Combine(folder, "labor-damaged-900.png"));
                Check("damaged-labor-exit-buttons-visible-900", render.Pass, render.Evidence);
            }
            SprintCAudioChecks(gate, AppPaths.Root, Check);
        }
        catch (Exception ex) { Check("runner", false, ex.ToString()); }
        File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(folder); return failed == 0 ? 0 : 1;
    }
    // Sprint C, Prüffälle 18 bis 23 und 28 bis 31 aus Abschnitt 8: Werkstatt mit Karten, Klappblöcken, Hauptaktionen und wachsenden Feldern.
    private static void SprintCWorkshopChecks(PromptPage page, Panel host, ActivityGate gate, Action<string, bool, object?> check)
    {
        var views = Field<TabControl>(page, "views"); views.SelectedIndex = 0;
        var workspace = Field<Panel>(page, "workspace");
        var draft = Field<GardenTextBox>(page, "draft"); var original = Field<GardenTextBox>(page, "original"); var candidate = Field<GardenTextBox>(page, "candidate");
        var info = Field<Label>(page, "info");
        var sections = Field<List<FlowLayoutPanel>>(page, "sections");
        var blocks = new[] { "supplementsBlock", "saveBlock", "connectionBlock", "helpBlock" }.Select(n => Field<GardenExpander>(page, n)).ToArray();
        host.Size = new Size(1440, 900); host.PerformLayout(); Invoke(page, "ArrangeWorkspace"); Application.DoEvents();
        // 31: kurze Meldung genau eine Zeile.
        info.Text = "Bereit. Manuelle Werkstatt funktioniert ohne KI-Modell."; host.PerformLayout(); Application.DoEvents();
        int oneLine = TextRenderer.MeasureText(info.Text, info.Font).Height;
        int preferred = info.GetPreferredSize(new Size(info.Width, 0)).Height;
        check("short-info-is-one-line", info.Height == preferred && preferred <= oneLine + info.Padding.Vertical, new { info.Height, preferred, oneLine, info.Width, info.Padding, infoBounds = info.Bounds, page = page.ClientSize, views = views.Bounds, recovery = Field<FlowLayoutPanel>(page, "recoveryRow").Bounds, recoveryVisible = Field<FlowLayoutPanel>(page, "recoveryRow").Visible });
        // 21: 15 Schaltflächen im Reiter Erstellung.
        var creationButtons = Descendants(views.TabPages[0]).OfType<Button>().Select(b => b.Text).ToArray();
        check("creation-actions-complete-after-regrouping", creationButtons.Contains("Prompt kopieren") && creationButtons.Contains("Prüfen und ergänzen") && creationButtons.Contains("Änderungen übernehmen") && creationButtons.Contains("Letzte Übernahme rückgängig"), new { count = creationButtons.Length, creationButtons });
        // 19 und 20: zugeklappt, Kopfzeile während eines Auftrags bedienbar, Knöpfe darin gesperrt.
        bool allClosed = blocks.All(b => !b.Expanded) && blocks.All(b => (object)b.Header is not Button);
        var hold = new TaskCompletionSource();
        var job = Job(page, async (token, _) => await hold.Task.WaitAsync(token));
        PumpUntil(() => Field<bool>(page, "working"));
        bool lockedWhileClosed = !Field<Button>(page, "saveVersion").Enabled && !Field<Button>(page, "saveNew").Enabled && !Field<Button>(page, "verify").Enabled;
        check("collapsed-actions-still-locked-during-job", blocks.All(b => !b.Expanded) && lockedWhileClosed, new { lockedWhileClosed });
        foreach (var block in blocks) typeof(GardenExpanderHeader).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(block.Header, [new KeyEventArgs(Keys.Space)]);
        bool openedDuringJob = blocks.All(b => b.Expanded) && blocks.All(b => b.Header.Enabled);
        check("collapsible-blocks-default-closed-and-usable-during-job", allClosed && openedDuringJob, new { allClosed, openedDuringJob, headers = blocks.Select(b => b.Header.Text).ToArray(), descriptions = blocks.Select(b => b.Header.AccessibleDescription).ToArray() });
        // 22: Texte schreibgeschützt statt gesperrt, Auswahllisten und Ankreuzfelder gesperrt.
        string[] eight = ["original", "goal", "audience", "format", "constraints", "draft", "candidate", "title"];
        var during = eight.Select(n => Field<TextBox>(page, n)).Select(t => new { t.AccessibleName, t.ReadOnly, t.Enabled }).ToArray();
        // reviewOptions und reviewSuggestion folgen danach ihrer eigenen Regel (nur bei passender Prüfung frei).
        var reviewOwn = new Control[] { Field<ComboBox>(page, "reviewOptions"), Field<CheckBox>(page, "reviewSuggestion") };
        var choosers = Field<List<Control>>(page, "editing").Where(c => c is ComboBox or CheckBox && !reviewOwn.Contains(c)).ToArray();
        bool choosersLocked = choosers.All(c => !c.Enabled);
        var primaryDuringJob = SectionPrimaries(sections);
        hold.SetResult(); Wait(job);
        foreach (var block in blocks) block.Expanded = false;
        var after = eight.Select(n => Field<TextBox>(page, n)).Select(t => new { t.AccessibleName, t.ReadOnly, t.Enabled }).ToArray();
        check("job-makes-fields-readonly-not-disabled", during.All(f => f.ReadOnly && f.Enabled) && after.All(f => f.Enabled && f.ReadOnly == (f.AccessibleName == "Ausgangstext")) && choosersLocked && choosers.All(c => c.Enabled), new { during, after, choosers = choosers.Length });
        // 23: schreibgeschützt sichtbar anders.
        var preview = Field<GardenTextBox>(page, "libraryPreview");
        check("readonly-fields-visibly-distinct", preview.ReadOnly && !draft.ReadOnly && preview.BackColor != draft.BackColor, new { readOnly = preview.BackColor.Name, editable = draft.BackColor.Name });
        // 18: je Abschnitt genau eine Hauptaktion in sieben Zuständen.
        var table = new List<object>(); bool onePrimary = true;
        // Entwurf beschädigt (Entscheidung 23.09.2026): „Erneut laden“ ist die einzige Hauptaktion der Werkstatt, die Abschnitte bleiben ohne.
        var reloadDraft = Field<GardenButton>(page, "reloadDraft"); var freshDraft = Field<GardenButton>(page, "freshDraft");
        void State(string name, bool damaged = false)
        {
            Invoke(page, "RefreshActions"); var p = SectionPrimaries(sections);
            onePrimary &= damaged ? p.All(s => s.Count == 0) && reloadDraft.Primary && !freshDraft.Primary : p.Sum(s => s.Count) == 1 && !reloadDraft.Primary;
            table.Add(new { state = name, primaries = p.Select(s => string.Join("|", s)).ToArray(), exitPrimary = reloadDraft.Primary });
        }
        Invoke(page, "LoadDocument", new PromptDocument()); State("leer");
        original.Text = "Synthetische Idee."; State("Idee eingetragen");
        draft.Text = "Synthetischer Prompt."; State("Prompt vorhanden");
        candidate.Text = "Synthetischer Prompt. Mit Ergänzung."; SetField(page, "candidateSource", draft.Text); State("Ergänzung zur Übernahme bereit");
        SetField(page, "modelInvalid", true); State("lokale KI fehlt"); SetField(page, "modelInvalid", false);
        onePrimary &= primaryDuringJob.Sum(s => s.Count) == 1; table.Add(new { state = "Auftrag läuft", primaries = primaryDuringJob.Select(s => string.Join("|", s)).ToArray() });
        SetField(page, "damagedDraft", true); State("Entwurf beschädigt", damaged: true); SetField(page, "damagedDraft", false); Invoke(page, "RefreshActions");
        // Ohne lokales Modell gewinnt im KI-Abschnitt immer der Download; mit Claude als Anbieter laufen die Zweige „prüfen“ und „übernehmen“.
        var provider = Field<ComboBox>(page, "reviewProvider"); int providerBefore = provider.SelectedIndex; provider.SelectedIndex = 1;
        Invoke(page, "LoadDocument", new PromptDocument()); State("leer · Claude");
        draft.Text = "Synthetischer Prompt."; State("Prompt vorhanden · Claude");
        candidate.Text = "Synthetischer Prompt. Mit Ergänzung."; SetField(page, "candidateSource", draft.Text); State("Ergänzung zur Übernahme bereit · Claude");
        provider.SelectedIndex = providerBefore; Invoke(page, "RefreshActions");
        check("creation-one-main-action", onePrimary && sections.Count == 4, table);
        // 28: 24.000 Zeichen wachsen in die Seite, nicht in das Feld.
        string sentence = "Dies ist ein synthetischer Satz für einen sehr langen Prompt. ";
        string longPrompt = string.Concat(Enumerable.Repeat(sentence, 24000 / sentence.Length + 1))[..24000];
        draft.Text = longPrompt; host.PerformLayout(); Application.DoEvents();
        var lastChar = draft.GetPositionFromCharIndex(draft.TextLength - 1);
        check("long-prompt-grows-page-not-field", draft.TextLength == 24000 && draft.ScrollBars == ScrollBars.None && draft.FirstVisibleLine == 0 && lastChar.Y + draft.LineHeight <= draft.Height && workspace.VerticalScroll.Visible,
            new { draft.TextLength, draft.Height, draft.LineCount, draft.LineHeight, lastCharY = lastChar.Y, draft.FirstVisibleLine, workspaceClient = workspace.ClientSize, workspaceDisplay = workspace.DisplayRectangle.Size });
        // 29: Schreibmarke am Ende eines Felds, das höher ist als die Seite, bleibt nach der Nachführung sichtbar.
        workspace.AutoScrollPosition = Point.Empty; draft.SelectionStart = draft.TextLength;
        typeof(GardenTextBox).GetMethod("KeepCaretVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(draft, null);
        var caretInPage = workspace.PointToClient(draft.PointToScreen(draft.GetPositionFromCharIndex(draft.TextLength - 1)));
        check("caret-stays-visible-at-end-of-long-field", draft.Height > workspace.ClientSize.Height && caretInPage.Y >= 0 && caretInPage.Y + draft.LineHeight <= workspace.ClientSize.Height, new { caretInPage, workspace = workspace.ClientSize, scroll = workspace.AutoScrollPosition });
        // 30: Fokus auf ein teilweise sichtbares hohes Feld springt nicht.
        var before = workspace.AutoScrollPosition;
        var target = (Point)typeof(ScrollableControl).GetMethod("ScrollToControl", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, [draft])!;
        check("focus-does-not-jump-tall-field", target == before && before != Point.Empty, new { before, target });
        draft.Text = "Synthetischer Prompt."; workspace.AutoScrollPosition = Point.Empty; host.PerformLayout();
        // Risiko 3: 24.000 Zeichen aus kurzen Zeilen überschreiten die Fenstergrenze von 32.767 Bildpunkten.
        // Rückfall: Höchsthöhe mit eigenem Scrollbalken, nur solange der Grenzfall besteht; danach wieder ohne.
        string shortLines = string.Concat(Enumerable.Repeat("a\r\n", 8000));
        draft.Text = shortLines; host.PerformLayout(); Application.DoEvents();
        var capped = new { draft.TextLength, draft.Height, draft.ScrollBars, draft.Capped, draft.LineCount, draft.LineHeight, uncapped = draft.LineCount * draft.LineHeight, textKept = draft.Text == shortLines };
        draft.Text = "Synthetischer Prompt."; host.PerformLayout(); Application.DoEvents();
        var released = new { draft.Height, draft.ScrollBars, draft.Capped, draft.ContentHeight };
        check("short-lines-cap-field-height", capped.TextLength == 24000 && capped.textKept && capped.Capped && capped.ScrollBars == ScrollBars.Vertical && capped.Height == GardenTextBox.MaxFieldHeight && capped.Height < 32767 && capped.uncapped > GardenTextBox.MaxFieldHeight
            && !released.Capped && released.ScrollBars == ScrollBars.None && released.Height == released.ContentHeight && released.Height < 2000, new { capped, released, max = GardenTextBox.MaxFieldHeight });
        // Validator-Befund: „Version laden“ zeigt danach den Anfang von „Mein Prompt“, auch wenn das Feld hoch ist (schmale Anordnung).
        host.Size = new Size(760, 640); host.PerformLayout(); Invoke(page, "ArrangeWorkspace"); Application.DoEvents();
        Invoke(page, "LoadDocument", new PromptDocument());
        views.SelectedIndex = 1; Application.DoEvents();
        var libraryList = Field<ListBox>(page, "libraryList"); libraryList.SelectedIndex = 0;
        workspace.AutoScrollPosition = Point.Empty;
        Invoke(page, "LoadEntry");
        var draftInPage = workspace.RectangleToClient(draft.RectangleToScreen(draft.ClientRectangle));
        check("load-version-shows-draft", views.SelectedIndex == 0 && draft.TextLength > 0 && workspace.AutoScrollPosition.Y < 0 && draftInPage.Top >= 0 && draftInPage.Top < workspace.ClientSize.Height / 3,
            new { draftInPage, scroll = workspace.AutoScrollPosition, workspace = workspace.ClientSize, draft.TextLength });
        host.Size = new Size(1440, 900); workspace.AutoScrollPosition = Point.Empty; host.PerformLayout(); Invoke(page, "ArrangeWorkspace"); Application.DoEvents();
    }
    // Sprint C, Prüffälle 24 bis 27 aus Abschnitt 8: Audioseiten ohne Mikrofon, Zustände über Felder und eine gehaltene Lease.
    private static void SprintCAudioChecks(ActivityGate gate, string root, Action<string, bool, object?> check)
    {
        foreach (string name in new[] { "import-result.bin", "labor-result.bin", "import-result-neu.bin", "labor-result-neu.bin" })
        { string path = Path.Combine(root, name); if (File.Exists(path)) File.Delete(path); }
        using var imports = new AudioToolsPage(false, gate, () => null);
        using var labor = new AudioToolsPage(true, gate, () => null);
        // Der Seitentakt würde „info“ während eines simulierten Vorgangs überschreiben; für diese Fälle angehalten.
        foreach (var p in new[] { imports, labor }) Field<System.Windows.Forms.Timer>(p, "timer").Stop();
        string[] buttonNames = ["choose", "stop", "cancelOperation", "check", "transcribe", "play", "discardCopy", "copyText", "toWorkshop", "clearText", "compare", "reloadResult", "freshResult"];
        var primaryRows = new List<object>(); bool onePrimary = true;
        (bool Ok, List<object> Rows) Table(AudioToolsPage page, bool laboratory)
        {
            var rows = new List<object>(); bool ok = true;
            var result = Field<GardenTextBox>(page, "result"); var expected = Field<GardenTextBox>(page, "expected");
            void Reset()
            {
                SetField(page, "working", false); SetField(page, "recordingLease", null); SetField(page, "damagedRecovery", false); SetField(page, "pending", null);
                SetField(page, "loading", true); result.Text = ""; expected.Text = ""; SetField(page, "loading", false);
            }
            void Row(string state, Func<IDisposable?> arrange)
            {
                Reset();
                using var held = arrange();
                Invoke(page, "RefreshActions");
                bool inProgress = Field<bool>(page, "working") || Field<object?>(page, "recordingLease") != null;
                bool free = !inProgress && !gate.Busy, damaged = Field<bool>(page, "damagedRecovery"), hasCopy = Field<string?>(page, "pending") != null;
                bool hasText = !string.IsNullOrWhiteSpace(result.Text), recording = Field<object?>(page, "recordingLease") != null;
                // Soll nach Brief Abschnitt 5, unabhängig vom Code nachgerechnet.
                var want = new Dictionary<string, bool>
                {
                    ["choose"] = free && !damaged && !hasCopy && result.Text.Length == 0,
                    ["stop"] = recording,
                    ["cancelOperation"] = inProgress,
                    ["check"] = free && !damaged && hasCopy,
                    ["transcribe"] = free && !damaged && hasCopy && result.Text.Length == 0,
                    ["play"] = free && hasCopy,
                    ["discardCopy"] = free && hasCopy,
                    ["copyText"] = free && hasText,
                    ["toWorkshop"] = free && hasText,
                    ["clearText"] = free && !damaged && result.Text.Length > 0,
                };
                if (laboratory) want["compare"] = hasText && !string.IsNullOrWhiteSpace(expected.Text);
                var actual = want.Keys.ToDictionary(k => k, k => Field<GardenButton>(page, k).Enabled);
                bool rowOk = want.All(w => actual[w.Key] == w.Value);
                ok &= rowOk;
                var visiblePrimary = buttonNames.Select(n => Field<GardenButton>(page, n)).Where(b => b.Visible && b.Primary).Select(b => b.Text).ToArray();
                onePrimary &= visiblePrimary.Length == 1;
                primaryRows.Add(new { page = laboratory ? "Sprachlabor" : "Audiodateien", state, primary = visiblePrimary });
                rows.Add(new { state, rowOk, mismatches = want.Where(w => actual[w.Key] != w.Value).Select(w => new { w.Key, want = w.Value, actual = actual[w.Key] }).ToArray() });
            }
            Row("leer", () => null);
            Row("Arbeitskopie vorhanden", () => { SetField(page, "pending", Path.Combine(root, "synthetische-arbeitskopie.wav")); return null; });
            Row("Text vorhanden", () => { SetField(page, "loading", true); result.Text = "Synthetisches Ergebnis."; expected.Text = laboratory ? "Synthetisches Ergebnis." : ""; SetField(page, "loading", false); return null; });
            Row("Vorgang läuft", () => { SetField(page, "pending", Path.Combine(root, "synthetische-arbeitskopie.wav")); SetField(page, "working", true); return null; });
            if (laboratory) Row("Aufnahme läuft", () => { var lease = gate.TryEnter("Sprachlabor · Aufnahme"); SetField(page, "recordingLease", lease); SetField(page, "pending", Path.Combine(root, "synthetische-aufnahme.wav")); return lease; });
            Row("fremder Vorgang läuft", () => gate.TryEnter("Synthetische Fremdaufgabe"));
            Row("Datei beschädigt", () => { SetField(page, "damagedRecovery", true); return null; });
            Reset(); Invoke(page, "RefreshActions");
            return (ok, rows);
        }
        var audioTable = Table(imports, false);
        check("audio-actions-follow-state", audioTable.Ok, audioTable.Rows);
        var laborTable = Table(labor, true);
        check("labor-actions-follow-state", laborTable.Ok, laborTable.Rows);
        check("audio-pages-one-primary", onePrimary, primaryRows);
        // 27: Die sechs früher stummen Pfade nennen im unpassenden Zustand einen Grund.
        var info = Field<Label>(imports, "info"); var paths = new List<object>(); bool explained = true;
        void Silent(string name, Action call)
        {
            const string sentinel = "·";
            info.Text = sentinel; call();
            bool ok = info.Text != sentinel && !string.IsNullOrWhiteSpace(info.Text);
            explained &= ok; paths.Add(new { name, message = info.Text, ok });
        }
        Silent("Abbrechen / Wiedergabe stoppen", () => Invoke(imports, "CancelOperation"));
        Silent("Zur Promptwerkstatt", () => Invoke(imports, "SendToWorkshop"));
        Silent("Anhören", () => Invoke(imports, "Play"));
        Silent("Aufnahme / Arbeitskopie verwerfen", () => Invoke(imports, "Discard"));
        Silent("Text leeren", () => Invoke(imports, "ClearText"));
        Silent("Text kopieren", () => Wait((Task)Call(imports, "CopyAsync")!));
        check("audio-silent-paths-explain", explained && paths.Count == 6, paths);
    }
    private static List<List<string>> SectionPrimaries(List<FlowLayoutPanel> sections) =>
        sections.Select(s => Descendants(s).OfType<GardenButton>().Where(b => b.Primary).Select(b => b.Text).ToList()).ToList();
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static void SetField(object instance, string name, object? value) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
    private static object? Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);
    private static (bool Pass, object Evidence) RenderExitButtons(Panel host, Button reload, Button fresh, string imagePath)
    {
        host.PerformLayout();
        var reloadBounds = host.RectangleToClient(reload.RectangleToScreen(reload.ClientRectangle));
        var freshBounds = host.RectangleToClient(fresh.RectangleToScreen(fresh.ClientRectangle));
        using var bitmap = new Bitmap(host.Width, host.Height);
        host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.Size));
        bitmap.Save(imagePath, ImageFormat.Png);
        bool pass = reload.Visible && fresh.Visible && host.ClientRectangle.Contains(reloadBounds) && host.ClientRectangle.Contains(freshBounds) && !reloadBounds.IntersectsWith(freshBounds);
        return (pass, new { Image = Path.GetFileName(imagePath), ReloadVisible = reload.Visible, FreshVisible = fresh.Visible, reloadBounds, freshBounds, Host = host.ClientSize });
    }
    private static IEnumerable<Control> Descendants(Control parent) { foreach (Control child in parent.Controls) { yield return child; foreach (var item in Descendants(child)) yield return item; } }
    private static Task Job(PromptPage page, Func<CancellationToken, IProgress<PromptProgress>, Task> action) => (Task)typeof(PromptPage).GetMethod("ModelJobAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, ["Vorschlag erstellen", action])!;
    private static void Wait(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void PumpUntil(Func<bool> done)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!done()) { if (timeout.Elapsed.TotalSeconds > 10) throw new TimeoutException("Component event-loop timeout"); Application.DoEvents(); Thread.Sleep(5); }
    }
    private static void Invoke(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);
    private sealed class PreparationStopped() : Exception("Lokale Vorbereitung im Prüflauf am UI-Übergang angehalten.");
}
