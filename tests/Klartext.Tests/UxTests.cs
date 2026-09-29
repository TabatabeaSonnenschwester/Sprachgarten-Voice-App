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
            var strip = Walk(host).OfType<GardenTabStrip>().Single(s => s.AccessibleName == "Bereiche");
            foreach (var size in new[] { new Size(800, 660), new Size(900, 740), new Size(1440, 900), new Size(800, 660) })
            {
                host.Size = new Size(size.Width * main.DeviceDpi / 96, size.Height * main.DeviceDpi / 96); host.PerformLayout(); Application.DoEvents();
                using var g = strip.CreateGraphics();
                Check("navigation-complete-" + size.Width, Enumerable.Range(0, tabs.TabCount).All(i =>
                    strip.ClientRectangle.Contains(strip.GetTabBounds(i)) &&
                    TextRenderer.MeasureText(g, tabs.TabPages[i].Text, strip.TabFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width <= strip.GetTabBounds(i).Width));
            }
            Check("header-compact", Walk(host).OfType<GardenHeader>().Single().Height <= 112 * main.DeviceDpi / 96f + 1);
            Check("replacement-empty-state-explains-first-rule", Field<Label>(main, "rulesHint").Text.Contains("Korrektur übernehmen"));
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
                bool wide = size.Width > 1000;
                Check("idea-result-order-" + size.Width, wide ? sections[0].Parent != sections[2].Parent :
                    sections[0].Parent == sections[2].Parent && sections[0].Bottom <= sections[2].Top && sections[2].Bottom <= sections[1].Top, new { page.ClientSize, main.DeviceDpi });
            }
            original.Text = "Schreibe eine Einladung zum Gartenfest. Höchstens 80 Wörter.";
            Field<GardenButton>(page, "adoptIdea").PerformClick(); Application.DoEvents();
            Check("simple-path-preserves-exact-idea", draft.Text == original.Text && !supplements.Expanded);
            Check("simple-path-enables-copy", Field<GardenButton>(page, "copyPrompt").Enabled);
            var workspace = Field<GardenScrollPanel>(page, "workspace");
            var promptTop = workspace.PointToClient(sections[2].PointToScreen(Point.Empty)).Y;
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
