using System.Reflection;
using System.Text.Json;
using Klartext;

internal static class WorkshopUxTests
{
    private static T Field<T>(object x, string n) => (T)x.GetType().GetField(n, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(x)!;
    private static object? Call(object x, string n, params object[] a) => x.GetType().GetMethod(n, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(x, a);
    public static int Run(string root)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles();
        var folder = Path.Combine(root, "artifacts", "workshop-ux", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        AppPaths.Root = Path.Combine(folder, "profile"); AppPaths.Initialize();
        int failures = 0; var results = new List<object>();
        void Check(string n, bool ok) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {n}"); results.Add(new { name = n, ok }); if (!ok) failures++; }
        using var host = new Form { Text = "Sprachgarten · Werkstattprüfung", ClientSize = new Size(900, 760), ShowInTaskbar = false };
        host.Shown += (_, _) =>
        {
            try
            {
                var gate = new ActivityGate(() => true);
                using var page = new PromptPage(gate, () => "Ein synthetisches Diktat.");
                host.Controls.Add(page); BrandTheme.Apply(host); host.PerformLayout(); Application.DoEvents();
                var draft = Field<GardenTextBox>(page, "draft"); var original = Field<GardenTextBox>(page, "original");
                var review = Field<GardenExpander>(page, "reviewBlock");
                PromptDocument Doc() => (PromptDocument)Call(page, "Document")!;
                void Load(PromptDocument d) => Call(page, "LoadDocument", d);
                Check("empty-central-editor-ready", draft.Visible && !draft.ReadOnly && !Field<GardenButton>(page, "copyPrompt").Enabled);
                Check("extras-and-source-collapsed", !review.Expanded && !Field<GardenExpander>(page, "sourceBlock").Expanded && !Field<GardenExpander>(page, "saveBlock").Visible && !Field<GardenExpander>(page, "moreBlock").Expanded);
                Check("no-ai-controls-leak-out-of-collapsed-review", !Field<GardenButton>(page, "generate").Visible && !Field<Label>(page, "candidateInfo").Visible);
                page.ImportText("Ein synthetischer Ausgangstext.");
                Check("first-import-is-direct-and-keeps-source", draft.Text == original.Text && draft.Text.Length > 0 && original.ReadOnly);
                Check("first-import-reveals-source-card", Field<GardenExpander>(page, "sourceBlock").Visible && Field<GardenExpander>(page, "sourceBlock").Parent!.Visible);
                Check("copy-is-main-action", Field<GardenButton>(page, "copyPrompt").Primary && Field<GardenButton>(page, "copyPrompt").Enabled);
                draft.Text = "Mein bearbeiteter Prompt – bitte NICHT ersetzen.";
                Field<GardenTextBox>(page, "goal").Text = "Eine kurze Einladung";
                Field<GardenTextBox>(page, "title").Text = "Mein Titel";
                var before = Doc();
                review.Expanded = true; Field<GardenExpander>(page, "supplementsBlock").Expanded = true;
                Field<GardenButton>(page, "build").PerformClick();
                Check("build-only-previews-current-edited-prompt", Doc() == before && Field<GardenTextBox>(page, "replacementPreview").Text.StartsWith(before.Draft) && Field<GardenTextBox>(page, "replacementPreview").Text.Contains(before.Goal));
                Check("manual-preview-is-readonly-and-not-ai-candidate", Field<GardenTextBox>(page, "replacementPreview").ReadOnly && Doc().Candidate == before.Candidate);
                draft.AppendText(" Spätere Änderung.");
                Check("stale-preview-cannot-replace-new-edit", !page.ApplyPending() && draft.Text.EndsWith("Spätere Änderung."));
                Load(before); page.QueueReplacement(before with { Draft = "Bewusst neue Fassung" }, "Testvorschau");
                Check("confirmed-preview-applies", page.ApplyPending() && draft.Text == "Bewusst neue Fassung");
                Check("undo-restores-entire-document", page.UndoReplacement() && Doc() == before && !Field<GardenButton>(page, "undoReplacement").Visible);
                page.QueueReplacement(before with { Draft = "Noch eine Fassung" }, "Test"); page.ApplyPending(); draft.AppendText(" Nachbearbeitung");
                page.ConfirmUndo = () => false;
                Check("undo-decline-keeps-later-edits", !page.UndoReplacement() && draft.Text.EndsWith("Nachbearbeitung"));
                page.ConfirmUndo = () => true;
                Check("undo-consent-restores-full-original", page.UndoReplacement() && Doc() == before);
                page.QueueReplacement(before with { Draft = "Neu" }, "Test"); page.ApplyPending(); draft.Text = "Danach";
                page.ConfirmUndo = () => { gate.Cancel(); return true; };
                Check("cancel-during-undo-dialog-keeps-current", !page.UndoReplacement() && draft.Text == "Danach" && !gate.Busy);
                page.ConfirmUndo = () => { draft.Text = "Änderung während Dialog"; return true; };
                Check("edit-during-undo-dialog-not-overwritten", !page.UndoReplacement() && draft.Text == "Änderung während Dialog");
                Load(before); page.ImportText("Anderes Diktat");
                Check("existing-import-only-previews", Doc() == before && Field<GardenTextBox>(page, "replacementPreview").Text == "Anderes Diktat");
                page.ApplyPending();
                Check("import-replaces-entire-doc-only-on-adoption", draft.Text == "Anderes Diktat" && original.Text == "Anderes Diktat" && Field<GardenTextBox>(page, "goal").Text == "");
                Check("import-undo-restores-fields", page.UndoReplacement() && Doc() == before);
                Call(page, "NewPrompt");
                Check("new-prompt-previews-empty-without-loss", Doc() == before && Field<GardenTextBox>(page, "replacementPreview").Text.Contains("Leerer"));
                page.ApplyPending(); Check("new-prompt-undo-keeps-complete-draft", draft.Text == "" && page.UndoReplacement() && Doc() == before);
                page.QueueReplacement(before with { Draft = "Andere Fassung" }, "Test");
                using (gate.TryEnter("Andere Arbeit")) Check("busy-blocks-adoption-and-undo", !page.ApplyPending() && !page.UndoReplacement() && draft.ReadOnly);
                Check("invalid-document-cannot-replace", !page.QueueReplacement(before with { Draft = new string('x', 24001) }, "Zu lang") && Doc() == before);
                Check("draft-status-separate-from-library", page.Flush() && Field<Label>(page, "draftStatus").Text.Contains("lokal gesichert") && Field<Label>(page, "libraryStatus").Text.Contains("Noch nicht"));
                Call(page, "SaveLibrary", true);
                Check("library-status-after-explicit-save", Field<Label>(page, "libraryStatus").Text.StartsWith("Aktuelle Fassung"));
                draft.Text += " Änderung nach Speichern.";
                Check("library-status-does-not-claim-new-edits-saved", Field<Label>(page, "libraryStatus").Text.Contains("andere Fassung") && Field<Label>(page, "draftStatus").Text.Contains("noch nicht"));
                page.Flush();
                using (File.Open(Path.Combine(AppPaths.Root, "prompt-draft.bin"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Check("failed-autosave-never-claims-saved", !page.Flush() && !Field<Label>(page, "draftStatus").Text.Contains("lokal gesichert"));
                var library = Field<ListBox>(page, "libraryList"); library.SelectedIndex = 0;
                var edited = Doc(); Call(page, "LoadEntry");
                Check("library-load-first-previews", Doc() == edited && Field<GardenTextBox>(page, "replacementPreview").Text == before.Draft);
                page.ApplyPending(); Check("library-load-can-be-undone", draft.Text == before.Draft && page.UndoReplacement() && Doc() == edited);
                Load(before);
                Call(page, "SetReview", new PromptReview("Synthetische Rückfrage.", [new("Für wen?", "Kontext fehlt.", ["Nachbarn", "Verein"])], []), draft.Text);
                review.Expanded = true; Field<ComboBox>(page, "reviewOptions").SelectedIndex = 0;
                var answer = Field<GardenTextBox>(page, "reviewAnswer").Text;
                review.Expanded = false; review.Expanded = true;
                Check("collapse-does-not-reset-ki-answer", Field<GardenTextBox>(page, "reviewAnswer").Text == answer && Field<ComboBox>(page, "reviewOptions").SelectedIndex == 0);
                Check("ki-answer-is-preview-only", draft.Text == before.Draft && Field<GardenTextBox>(page, "candidate").Text.Contains(answer));
                var beforeAi = Doc(); Field<GardenButton>(page, "adopt").PerformClick();
                Check("ki-adoption-is-undoable", draft.Text != before.Draft && page.UndoReplacement() && Doc() == beforeAi);
                // Render the actual production control with synthetic data at two desktop widths.
                Load(before); review.Expanded = false; Field<GardenExpander>(page, "sourceBlock").Expanded = false;
                foreach (int width in new[] { 800, 1440 })
                {
                    host.ClientSize = new Size(width, 760); host.PerformLayout(); Call(page, "ArrangeWorkspace"); Application.DoEvents();
                    var workspace = Field<GardenScrollPanel>(page, "workspace"); workspace.AutoScrollPosition = Point.Empty;
                    Check("central-layout-no-horizontal-scroll-" + width, draft.Visible && !workspace.HorizontalScroll.Visible && draft.Width > workspace.ClientSize.Width * .8);
                    using var bitmap = new Bitmap(host.ClientSize.Width, host.ClientSize.Height); host.DrawToBitmap(bitmap, host.ClientRectangle); bitmap.Save(Path.Combine(folder, "workshop-" + width + ".png"));
                }
                page.ImportText("Neue importierte Fassung für die bewusste Übernahme.");
                using (var bitmap = new Bitmap(host.ClientSize.Width, host.ClientSize.Height)) { host.DrawToBitmap(bitmap, host.ClientRectangle); bitmap.Save(Path.Combine(folder, "workshop-preview.png")); }
                Check("pending-preview-not-written-as-current-document", page.Flush() && ProtectedFile.Read<PromptDocument>(Path.Combine(AppPaths.Root, "prompt-draft.bin")) == Doc());
                draft.Clear(); page.Flush();
                using (var reopened = new PromptPage(new ActivityGate(() => true), () => ""))
                    Check("restart-preserves-deliberately-empty-draft", Field<GardenTextBox>(reopened, "draft").Text == "" && Field<GardenTextBox>(reopened, "original").Text == before.Original && Field<Label>(reopened, "info").Text.Contains("Ausgangstext ansehen"));
            }
            catch (Exception e) { Console.WriteLine(e); Check("unexpected", false); }
            finally { File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); host.Close(); }
        };
        Application.Run(host); Console.WriteLine($"Workshop UX: {results.Count - failures} PASS / {failures} FAIL · {folder}"); return failures == 0 ? 0 : 1;
    }
}
