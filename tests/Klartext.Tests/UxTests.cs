using System.Reflection;
using System.Text.Json;
using Klartext;

/// <summary>Native UI component checks with synthetic data; no microphone, cloud or clipboard.</summary>
internal static class UxTests
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
    private static IEnumerable<Control> Walk(Control control)
    {
        foreach (Control child in control.Controls) { yield return child; foreach (var nested in Walk(child)) yield return nested; }
    }
    public static int Run(string root)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        AppPaths.Root = Path.Combine(root, "artifacts", "ux", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        AppPaths.Initialize();
        var results = new List<object>(); int failures = 0;
        void Check(string name, bool pass, object? evidence = null)
        {
            results.Add(new { name, status = pass ? "passed" : "failed", evidence });
            Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name} {JsonSerializer.Serialize(evidence)}"); if (!pass) failures++;
        }
        using (var main = new MainForm(false))
        using (var host = new Panel { Size = new Size(800, 660), Font = main.Font })
        {
            host.Controls.Add(main.Controls[0]); host.CreateControl(); host.PerformLayout();
            var tabs = Field<GardenTabs>(main, "tabs");
            var navigation = Walk(host).OfType<GardenNavigation>().Single();
            Check("initial-dictation-selection-visible", navigation.Items.Single(b => b.Text == "Diktat").Checked && navigation.Items.Count(b => b.Checked) == 1);
            foreach (var size in new[] { new Size(800, 660), new Size(900, 740), new Size(1440, 900), new Size(800, 660) })
            {
                host.Size = new Size(size.Width * main.DeviceDpi / 96, size.Height * main.DeviceDpi / 96); host.PerformLayout(); Application.DoEvents();
                using var g = navigation.CreateGraphics();
                Check("navigation-complete-" + size.Width, navigation.Items.Count == 7 && navigation.Items.All(item =>
                    item.Left >= 0 && item.Right <= navigation.ClientSize.Width &&
                    TextRenderer.MeasureText(g, item.Text, item.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width <= item.Width - item.Padding.Horizontal));
            }
            Check("header-compact", Walk(host).OfType<GardenHeader>().Single().Height <= 112 * main.DeviceDpi / 96f + 1);
            Check("replacement-empty-state-explains-first-rule", Field<Label>(main, "rulesHint").Text.Contains("Korrektur übernehmen"));
            var onboarding = Field<GardenOnboarding>(main, "onboarding");
            void SetFlag(string name, bool value) => typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, value);
            void Refresh() => typeof(MainForm).GetMethod("UpdateUi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object?[] { null });
            SetFlag("modelChecked", true); Refresh();
            Check("missing-model-onboarding-reachable", onboarding.Visible && onboarding.Action.Enabled && !Field<GardenButton>(main, "toggle").Enabled);
            Check("bundled-model-offline-explained", onboarding.Hint.Text.Contains("enthalten") && onboarding.Hint.Text.Contains("offline") && onboarding.Action.Text=="Spracherkennung prüfen");
            using (var lease = Field<ActivityGate>(main, "activities").TryEnter("Synthetische Zusatzfunktion"))
            {
                Refresh();
                Check("onboarding-respects-activity-gate", lease != null && !onboarding.Action.Enabled && !onboarding.Hotkey.Enabled);
            }
            SetFlag("recoveryUnavailable", true); Refresh();
            Check("unavailable-recovery-not-covered-by-onboarding", !onboarding.Visible);
            SetFlag("recoveryUnavailable", false);
            var setupState = Field<SessionState>(main, "state");
            setupState.Move(DictationState.Setup);
            using (var cancellation = new CancellationTokenSource())
            {
                var operationField = typeof(MainForm).GetField("operation", BindingFlags.Instance | BindingFlags.NonPublic)!;
                operationField.SetValue(main, cancellation); Refresh();
                Check("download-progress-and-cancel-reachable", onboarding.Visible && onboarding.Progress.Visible && !onboarding.Action.Enabled && Field<GardenButton>(main, "cancel").Enabled);
                operationField.SetValue(main, null);
            }
            setupState.Move(DictationState.Ready); Refresh();
            Check("download-cancel-restores-onboarding-action", onboarding.Action.Enabled && !onboarding.Progress.Visible);
            SetFlag("modelBlocked", true); Refresh();
            Check("blocked-model-rechecks-instead-of-download", onboarding.Action.Text == "Erneut prüfen" && onboarding.Hint.Text.Contains("nicht bereit"));
            SetFlag("modelBlocked", false); SetFlag("modelReady", true); Refresh();
            Check("ready-model-removes-onboarding-without-hotkey", !onboarding.Visible && Field<GardenButton>(main, "toggle").Enabled);
            SetFlag("modelReady", false);
            var session = Field<SessionState>(main, "state"); session.HasUncopiedText = true; Refresh();
            Check("recovery-not-covered-by-onboarding", !onboarding.Visible);
            session.HasUncopiedText = false; session.HasPendingAudio = true; Refresh();
            Check("pending-audio-not-covered-by-onboarding", !onboarding.Visible);
            session.HasPendingAudio = false; SetFlag("modelReady", true); Refresh();
            tabs.SelectedIndex = 5;
            var page = Field<PromptPage>(main, "workshop");
            var sections = Field<List<FlowLayoutPanel>>(page, "sections");
            var supplements = Field<GardenExpander>(page, "supplementsBlock");
            var original = Field<GardenTextBox>(page, "original");
            var draft = Field<GardenTextBox>(page, "draft");
            Check("optional-fields-start-collapsed", !supplements.Expanded);
            foreach (var size in new[] { new Size(800, 660), new Size(1440, 900), new Size(800, 660) })
            {
                host.Size = new Size(size.Width * main.DeviceDpi / 96, size.Height * main.DeviceDpi / 96); host.PerformLayout(); Call(page, "ArrangeWorkspace"); Application.DoEvents();
                bool wide = Field<GardenScrollPanel>(page, "workspace").ClientSize.Width >= 820 * main.DeviceDpi / 96f;
                Check("central-editor-first-" + size.Width, sections[0].AccessibleName == "Mein Prompt" && sections[0].Parent == sections[1].Parent && sections[0].Parent!.Controls.GetChildIndex(sections[0]) == 0 && (!sections[1].Visible || sections[0].Bottom <= sections[1].Top), new { page.ClientSize, main.DeviceDpi });
            }
            page.ImportText("Schreibe eine Einladung zum Gartenfest. Höchstens 80 Wörter."); Application.DoEvents();
            Check("simple-path-preserves-exact-idea", draft.Text == original.Text && !supplements.Expanded);
            Check("simple-path-enables-copy", Field<GardenButton>(page, "copyPrompt").Enabled);
            var workspace = Field<GardenScrollPanel>(page, "workspace");
            var promptTop = workspace.PointToClient(draft.PointToScreen(Point.Empty)).Y;
            Check("adoption-shows-result", promptTop >= 0 && promptTop < workspace.ClientSize.Height, new { promptTop, workspace.ClientSize, workspace.AutoScrollPosition });
            supplements.Expanded = true; Field<GardenTextBox>(page, "goal").Text = "Als freundliche Nachricht";
            supplements.Expanded = false; supplements.Expanded = true;
            Check("collapse-preserves-extra-input", Field<GardenTextBox>(page, "goal").Text == "Als freundliche Nachricht" && draft.Text == original.Text);
            supplements.Expanded = false;
            var views = Field<GardenPages>(page, "views"); views.SelectedIndex = 1; Application.DoEvents();
            Check("empty-library-explains-save", Field<Label>(page, "libraryHint").Visible && Field<Label>(page, "libraryHint").Text.Contains("In der Bibliothek speichern"));
            Field<GardenButton>(page, "createFirstPrompt").PerformClick();
            Check("empty-library-action-opens-creation", views.SelectedIndex == 0 && draft.Text == original.Text);
            views.SelectedIndex = 1; Field<GardenTextBox>(page, "search").Text = "kein Treffer";
            Check("empty-search-explained-separately", Field<Label>(page, "libraryHint").Text.Contains("Suche") && !Field<GardenButton>(page, "createFirstPrompt").Visible);
            foreach (var name in new[] { "imports", "laboratory" })
            {
                tabs.SelectedIndex = name == "imports" ? 3 : 4;
                var audio = Field<AudioToolsPage>(main, name); Call(audio, "RefreshActions");
                Check(name + "-empty-has-one-start-action", Field<GardenButton>(audio, "choose").Visible && !Field<GardenButton>(audio, "transcribe").Visible && !Field<GardenButton>(audio, "cancelOperation").Visible);
                typeof(AudioToolsPage).GetField("pending", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(audio, Path.Combine(AppPaths.Root, "synthetic.wav"));
                Call(audio, "RefreshActions");
                Check(name + "-audio-reveals-processing", Field<GardenButton>(audio, "transcribe").Visible && Field<GardenButton>(audio, "play").Visible && Field<Label>(audio, "stepHint").Text.StartsWith("2."));
                Field<GardenTextBox>(audio, "result").Text = "Synthetischer Text.";
                Check(name + "-result-reveals-next-actions", Field<GardenButton>(audio, "copyText").Visible && Field<GardenButton>(audio, "toWorkshop").Visible && !Field<GardenButton>(audio, "transcribe").Visible && Field<Label>(audio, "stepHint").Text.StartsWith("3."));
            }
        }
        File.WriteAllText(Path.Combine(AppPaths.Root, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"UX: {results.Count - failures} PASS / {failures} FAIL · {AppPaths.Root}");
        return failures == 0 ? 0 : 1;
    }
}
