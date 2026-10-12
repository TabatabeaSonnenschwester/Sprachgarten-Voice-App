using System.Reflection;
using System.Text.Json;
using Klartext;

internal static class UxSafetyTests
{
    public static int Preview(string root)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles();
        AppPaths.Root = Path.Combine(root, "artifacts", "ux-safety", "native-profile"); AppPaths.Initialize();
        using var host = new Form { Text = "Sprachgarten · isolierte Textschutzprobe", ClientSize = new Size(950, 760) };
        using var page = new ReadingPage(new ActivityGate(() => true));
        var paste = new Button { Text = "Test: synthetischen Text einfügen", Dock = DockStyle.Top, Height = 44 };
        paste.Click += (_, _) => page.PasteText("Synthetischer eingefügter Text für die Dialogprüfung.");
        Field<GardenTextBox>(page, "input").Text = "Mein eigener Entwurf bleibt erhalten.";
        host.Controls.Add(page); host.Controls.Add(paste); BrandTheme.Apply(host);
        Application.Run(host); return 0;
    }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
    public static int Run(string root)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles();
        string folder = Path.Combine(root, "artifacts", "ux-safety", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        AppPaths.Root = Path.Combine(folder, "profile"); AppPaths.Initialize();
        int failed = 0; var results = new List<object>();
        void Check(string name, bool ok) { results.Add(new { name, ok }); Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}"); if (!ok) failed++; }
        using var host = new Form { Text = "Sprachgarten · Textschutzprüfung", ClientSize = new Size(850, 700), ShowInTaskbar = false };
        host.Shown += async (_, _) => {
            try
            {
                var gate = new ActivityGate(() => true); bool consent = false; int prompts = 0;
                using (var page = new ReadingPage(gate, new LocalSpeech(), () => (null, "Test ohne Vorlesepaket"), () => { prompts++; return consent; }))
                {
                    host.Controls.Add(page); var input = Field<GardenTextBox>(page, "input");
                    string original = "Mein eigener Text.\r\nÄÖÜ – unverändert.";
                    input.Text = original; string exact = input.Text;
                    Check("paste-declined-preserves-exact-text", !page.PasteText("Fremder Text") && input.Text == exact && prompts == 1);
                    Check("identical-paste-needs-no-confirmation", page.PasteText(exact) && prompts == 1);
                    Check("invalid-paste-preserves-text-without-prompt", !page.PasteText(new string('a', 4001)) && !page.PasteText(" ") && input.Text == exact && prompts == 1);
                    using (var lease = gate.TryEnter("Anderer Vorgang")) Check("busy-paste-blocked-without-prompt", !page.PasteText("Neu") && input.Text == exact && prompts == 1 && input.ReadOnly);
                    consent = true;
                    Check("confirmed-paste-provides-restore", page.PasteText("Bewusst eingefügt") && input.Text == "Bewusst eingefügt" && Field<GardenButton>(page, "restoreText").Visible);
                    Check("restore-recovers-exact-original", page.RestoreText() && input.Text == exact && prompts == 2 && !Field<GardenButton>(page, "restoreText").Visible);
                    page.PasteText("Neue Fassung"); input.Text = "Danach von mir bearbeitet"; consent = false;
                    Check("restore-does-not-discard-new-edits-without-consent", !page.RestoreText() && input.Text == "Danach von mir bearbeitet");
                    consent = true;
                    Check("restore-after-edit-with-consent", page.RestoreText() && input.Text == exact);
                    input.Text = ""; int prior = prompts;
                    Check("empty-editor-accepts-without-prompt", page.PasteText("Erster Text") && prompts == prior && !Field<GardenButton>(page, "restoreText").Visible);
                    page.PasteText("Zweiter Text");
                    Check("invalid-paste-keeps-restore-history", !page.PasteText("a\0b") && page.RestoreText() && input.Text == "Erster Text");
                    using var bitmap = new Bitmap(host.ClientSize.Width, host.ClientSize.Height); host.DrawToBitmap(bitmap, host.ClientRectangle); bitmap.Save(Path.Combine(folder, "textschutz.png"));
                    host.Controls.Remove(page);
                }
                using (var page = new ReadingPage(gate, new LocalSpeech(), () => (null, "Test"), () => { gate.Cancel(); return true; }))
                {
                    var input = Field<GardenTextBox>(page, "input"); input.Text = "Bleibt erhalten";
                    Check("cancel-during-confirmation-prevents-replacement", !page.PasteText("Neu") && input.Text == "Bleibt erhalten" && !gate.Busy);
                }
                ReadingPage? reentrant = null;
                using (reentrant = new ReadingPage(gate, new LocalSpeech(), () => (null, "Test"), () => { Field<GardenTextBox>(reentrant!, "input").Text = "Zwischenzeitlich geändert"; return true; }))
                {
                    Field<GardenTextBox>(reentrant, "input").Text = "Ausgangstext";
                    Check("changed-editor-during-confirmation-not-overwritten", !reentrant.PasteText("Neu") && Field<GardenTextBox>(reentrant, "input").Text == "Zwischenzeitlich geändert");
                }
                var runtime = new SpeechRuntime("fixture", "fixture");
                using (var page = new ReadingPage(gate, ReviewFixTests.Fake("prepare-stall"), () => (runtime, null), () => throw new Exception("Selection must not ask to replace"), () => Task.FromResult<string?>("Markierung aus einem anderen Programm")))
                {
                    var input = Field<GardenTextBox>(page, "input"); input.Text = "Mein Entwurf";
                    var reading = page.ReadSelectionAsync();
                    Check("selection-hotkey-keeps-draft-during-preparation", input.Text == "Mein Entwurf" && page.Busy && Field<Label>(page, "status").Text.Contains("Textmarkierung"));
                    page.Stop(); await reading;
                    Check("selection-stop-keeps-draft-and-releases-gate", input.Text == "Mein Entwurf" && !gate.Busy && !page.Busy);
                }
                using (var page = new ReadingPage(gate, ReviewFixTests.Fake("prepare-stall", 200), () => (runtime, null)))
                {
                    Field<GardenTextBox>(page, "input").Text = "Eigener Text trotz Fehler";
                    await page.ReadAsync("Externer Text");
                    Check("selection-timeout-keeps-draft", Field<GardenTextBox>(page, "input").Text == "Eigener Text trotz Fehler" && Field<Label>(page, "status").Text.Contains("antwortet"));
                }
                using (var page = new ReadingPage(gate, new LocalSpeech(), () => (null, "Fehlt"), null, () => Task.FromResult<string?>(null)))
                {
                    Field<GardenTextBox>(page, "input").Text = "Eigener Text"; await page.ReadSelectionAsync();
                    Check("unsupported-selection-keeps-draft", Field<GardenTextBox>(page, "input").Text == "Eigener Text" && !page.Busy);
                }
                using (var held = new HotkeyWindow())
                {
                    var free = HotkeyWindow.FindAvailable();
                    Check("shortcut-suggestion-valid-or-none", free == null || free.IsValid);
                    if (free != null)
                    {
                        Check("suggestion-releases-probe-registration", held.TryRegister(free));
                        var another = HotkeyWindow.FindAvailable();
                        Check("suggestion-skips-occupied-and-keeps-current", another != free && held.Registered == free);
                    }
                    var blockers = new List<HotkeyWindow>();
                    try { foreach (var spec in HotkeyWindow.Suggestions) { var blocker = new HotkeyWindow(); blockers.Add(blocker); blocker.TryRegister(spec); }
                        Check("all-suggestions-occupied-returns-none", HotkeyWindow.FindAvailable() == null); }
                    finally { foreach (var blocker in blockers) blocker.Dispose(); }
                }
                using (var tabs = new TabControl())
                using (var strip = new GardenTabStrip(tabs))
                {
                    tabs.TabPages.Add("Erstellung"); tabs.TabPages.Add("Bibliothek"); host.Controls.Add(strip);
                    var accessible = strip.AccessibilityObject;
                    Check("inner-tabs-expose-both-names", accessible.GetChildCount() == 2 && accessible.GetChild(0)?.Name == "Erstellung" && accessible.GetChild(1)?.Name == "Bibliothek");
                    accessible.GetChild(1)!.DoDefaultAction();
                    Check("inner-tab-accessible-action-changes-selection", tabs.SelectedIndex == 1 && accessible.GetSelected()?.Name == "Bibliothek" && accessible.GetChild(1)!.State.HasFlag(AccessibleStates.Selected));
                    host.Controls.Remove(strip);
                }
                using (var help = new ReadingHelpDialog())
                {
                    var text = help.Controls.OfType<TextBox>().Single();
                    Check("reading-help-is-local-and-scrollable", text.ReadOnly && text.ScrollBars == ScrollBars.Vertical && text.Text.Contains("laufenden Sitzung") && text.Text.Contains("docs/READING.md"));
                }
                using (var main = new MainForm(false))
                {
                    main.Show(); await Task.Delay(100);
                    Check("empty-dictation-hides-useless-text-actions", !Field<GardenButton>(main, "copy").Visible && !Field<GardenButton>(main, "applyCorrection").Visible);
                    Check("correction-button-names-copy-effect", Field<GardenButton>(main, "applyCorrection").Text.Contains("kopieren"));
                    Field<GardenTextBox>(main, "transcript").Text = "Synthetischer Diktattext";
                    typeof(MainForm).GetMethod("UpdateUi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, [null]);
                    Check("dictation-text-reveals-copy-and-correction-actions", Field<GardenButton>(main, "copy").Visible && Field<GardenButton>(main, "applyCorrection").Visible);
                }
            }
            catch (Exception e) { Console.WriteLine(e); Check("unexpected", false); }
            finally { await File.WriteAllTextAsync(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); host.Close(); }
        };
        Application.Run(host); Console.WriteLine($"UX safety: {results.Count - failed} PASS / {failed} FAIL · {folder}"); return failed == 0 ? 0 : 1;
    }
}
