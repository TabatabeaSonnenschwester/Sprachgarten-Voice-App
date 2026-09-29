using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Klartext;

/// <summary>Asset/native font and offscreen layout checks. Never shows/clicks a desktop window or records audio.</summary>
internal static class ThemeTests
{
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int GetTextFaceW(IntPtr hdc, int count, StringBuilder name);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam);
    private static readonly List<object> results = [];
    private static int failed;
    private static void Check(string name, bool passed, object? evidence = null)
    {
        results.Add(new { name, status = passed ? "passed" : "failed", evidence });
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}: {JsonSerializer.Serialize(evidence)}");
        if (!passed) failed++;
    }
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    // Sprint C: Geclippter Renderer. DrawToBitmap zeichnet überstehende Kindfenster ungeclippt; deshalb wird jede
    // Scrollfläche einzeln in ein Bild ihrer eigenen Größe gezeichnet und an ihre Stelle gesetzt. Rendering, kein Screenshot.
    private static Bitmap RenderClipped(Control host)
    {
        var surfaces = Descendants(host).OfType<ScrollableControl>()
            .Where(s => s.AutoScroll && s.Visible && !Ancestors(s, host).Any(a => a.AutoScroll)).ToList();
        var positions = surfaces.Select(s => s.AutoScrollPosition).ToList();
        var bitmap = new Bitmap(host.Width, host.Height);
        foreach (var surface in surfaces) surface.Visible = false;
        try { host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); }
        finally { foreach (var surface in surfaces) surface.Visible = true; }
        using var g = Graphics.FromImage(bitmap);
        for (int i = 0; i < surfaces.Count; i++)
        {
            var surface = surfaces[i];
            surface.AutoScrollPosition = new Point(-positions[i].X, -positions[i].Y);
            using var part = new Bitmap(Math.Max(1, surface.Width), Math.Max(1, surface.Height));
            surface.DrawToBitmap(part, new Rectangle(Point.Empty, part.Size));
            var at = host.PointToClient(surface.Parent!.PointToScreen(surface.Location));
            g.DrawImage(part, at);
        }
        host.PerformLayout();
        for (int i = 0; i < surfaces.Count; i++)
            if (surfaces[i].AutoScrollPosition != positions[i]) throw new InvalidOperationException($"Renderer hat die Scrollposition verändert: {positions[i]} -> {surfaces[i].AutoScrollPosition}");
        return bitmap;
    }
    // Sprint C, Nachtrag 6b: Unterkante der Versionsauswahl plus Elementabstand (Margins) liegt über der Vorschau; der gezeichnete Feldrahmen (3 px je 96 dpi außerhalb) ebenfalls.
    private static void CheckLibraryVersionsClear(PromptPage page, int width)
    {
        var versions = Get<ComboBox>(page, "versions"); var preview = Get<GardenTextBox>(page, "libraryPreview");
        int frameInset = (int)Math.Ceiling(3 * preview.DeviceDpi / 96f);
        bool sameParent = versions.Parent == preview.Parent;
        Check("library-versions-clear-of-preview-" + width, sameParent && versions.Bottom + Math.Max(frameInset, versions.Margin.Bottom + preview.Margin.Top) <= preview.Top,
            new { versions = versions.Bounds, preview = preview.Bounds, versionsBottom = versions.Bottom, previewTop = preview.Top, frameInset, marginGap = versions.Margin.Bottom + preview.Margin.Top, versions.Height, versions.PreferredHeight, versions.ItemHeight, font = versions.Font.Name + " " + versions.Font.SizeInPoints, versions.DeviceDpi, versions.SelectedIndex, versionsText = versions.Text, versions.Items.Count, sameParent });
    }
    // Sprint E, Lücke über der Versionsauswahl: Abstand zwischen Trefferliste und Auswahl gleich der Summe der Margins (±1). Die Messwerte stehen in der Evidenz.
    private static void CheckLibraryListMeetsVersions(PromptPage page, int width)
    {
        var list = Get<ListBox>(page, "libraryList"); var versions = Get<ComboBox>(page, "versions");
        var requested = RequestedHeight(list);
        int margins = list.Margin.Bottom + versions.Margin.Top;
        int gap = versions.Top - list.Bottom;
        Check("library-list-meets-versions-" + width, list.Parent == versions.Parent && Math.Abs(gap - margins) <= 1,
            new { listBounds = list.Bounds, list.Height, requestedField = requested.Name, requestedHeight = requested.Value, list.ItemHeight, list.IntegralHeight, list.PreferredHeight, list.Items.Count, versionsTop = versions.Top, gap, margins, beyondMargins = gap - margins, requestedMinusActual = requested.Value - list.Height, list.DeviceDpi });
    }
    // Die angeforderte Höhe hält ListBox privat; der Feldname wird gesucht, nicht fest angenommen.
    private static (string? Name, int? Value) RequestedHeight(ListBox list)
    {
        for (var type = list.GetType(); type != null; type = type.BaseType)
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (field.FieldType == typeof(int) && field.Name.Contains("requested", StringComparison.OrdinalIgnoreCase) && field.Name.Contains("height", StringComparison.OrdinalIgnoreCase))
                    return (type.Name + "." + field.Name, (int)field.GetValue(list)!);
        return (null, null);
    }
    // Sprint E, Kartenränder: Abstand vom rechten Kartenrand zur Innenkante der Scrollfläche ist auf den vier Kartenseiten gleich (±1 px).
    private static void CheckCardRightEdges(Control host, GardenTabs tabs, int width)
    {
        var rows = new List<object>(); var distances = new List<int>();
        foreach (var (tab, name) in new[] { (1, "Einstellungen"), (3, "Audiodateien"), (4, "Sprachlabor"), (5, "Promptwerkstatt") })
        {
            tabs.SelectedIndex = tab; host.PerformLayout(); Application.DoEvents();
            var surface = Descendants(tabs.TabPages[tab]).OfType<ScrollableControl>().FirstOrDefault(s => s.AutoScroll && s.Visible && Descendants(s).OfType<GardenCardFlow>().Any(c => c.Visible));
            if (surface == null) { distances.Add(int.MinValue / 2); rows.Add(new { name, surface = "keine" }); continue; }
            var cards = Descendants(surface).OfType<GardenCardFlow>().Where(c => c.Visible).ToArray();
            int right = cards.Max(c => surface.RectangleToClient(c.RectangleToScreen(c.ClientRectangle)).Right);
            int distance = surface.ClientSize.Width - right;
            distances.Add(distance);
            rows.Add(new { name, surface = surface.GetType().Name, client = surface.ClientSize, scrollBar = surface.VerticalScroll.Visible, cards = cards.Length, right, distance });
        }
        tabs.SelectedIndex = 0; host.PerformLayout();
        Check("card-right-edges-equal-" + width, distances.Max() - distances.Min() <= 1, rows);
    }
    private static bool Near(Color actual, Color expected, int tolerance = 12) => Math.Abs(actual.R - expected.R) <= tolerance && Math.Abs(actual.G - expected.G) <= tolerance && Math.Abs(actual.B - expected.B) <= tolerance;
    // Sprint C, Prüffälle 1 bis 4 aus Abschnitt 8: Reiterleiste einzeilig, Beschriftungen passen, native Kopfzeile ersetzt, Marke ohne Fokus.
    private static void CheckTabStrip(Control host, GardenTabs tabs, int width, float dpiScale)
    {
        var strip = Descendants(host).OfType<GardenTabStrip>().Single(s => s.AccessibleName == "Bereiche");
        var rects = Enumerable.Range(0, strip.TabCount).Select(strip.GetTabBounds).ToArray();
        bool sameRow = rects.Length == 7 && rects.All(r => r.Height == rects[0].Height);
        bool inside = rects.All(r => strip.ClientRectangle.Contains(r));
        bool apart = rects.Zip(rects.Skip(1)).All(p => !p.First.IntersectsWith(p.Second));
        Check($"tab-strip-all-tabs-visible-{width}", sameRow && inside && apart,
            new { rects, strip = strip.ClientSize, stripHeight = strip.Height, expectedHeight = (int)(BrandTheme.TabStripHeight * dpiScale), rows = rects.Select(r => r.Top).Distinct().Count() });
        int minimum = (int)Math.Ceiling(2 * 8 * dpiScale);
        var labels = rects.Select((r, i) => new { text = tabs.TabPages[i].Text, tab = r.Width, measured = TextRenderer.MeasureText(tabs.TabPages[i].Text, strip.TabFont).Width }).ToArray();
        Check($"tab-labels-fit-{width}", labels.All(l => l.measured + minimum <= l.tab) && strip.TabFont.Name == "Indie Flower",
            new { font = strip.UsesSmallFont ? "BrandTheme.Small" : "BrandTheme.Body", strip.TabFont.SizeInPoints, paddingLogical = strip.TabPadding, minimum, labels });
        tabs.SelectedIndex = 0; host.PerformLayout();
        using var image = RenderClipped(host);
        var origin = host.PointToClient(strip.PointToScreen(Point.Empty));
        var last = rects[^1]; var first = rects[0];
        var right = image.GetPixel(Math.Min(image.Width - 1, origin.X + last.Right + (strip.ClientSize.Width - last.Right) / 2), origin.Y + last.Height / 2);
        Check($"tab-strip-replaces-native-header-{width}", tabs.DisplayRectangle == tabs.ClientRectangle && Near(right, BrandTheme.Cream, 2),
            new { display = tabs.DisplayRectangle, client = tabs.ClientRectangle, rightOfLastTab = right.Name, freeWidth = strip.ClientSize.Width - last.Right });
        var marker = image.GetPixel(origin.X + first.Left + first.Width / 2, origin.Y + first.Bottom - (int)Math.Ceiling(2 * dpiScale));
        var fill = image.GetPixel(origin.X + first.Left + (int)Math.Ceiling(4 * dpiScale), origin.Y + first.Height / 2);
        Check($"selected-tab-marker-without-focus-{width}", !strip.Focused && Near(marker, BrandTheme.Petrol) && Near(fill, BrandTheme.Mustard),
            new { focused = strip.Focused, marker = marker.Name, fill = fill.Name });
    }
    // Sprint C, Prüffall 6: Das Transkript wächst ohne eigenen Scrollbalken, die Diktatseite scrollt.
    private static void CheckGrowingTranscript(MainForm form, Control host, GardenTextBox transcript, int width)
    {
        var viewport = (ScrollableControl)BrandThemeScrollHost(transcript)!;
        string sixty = string.Join("\r\n", Enumerable.Range(1, 60).Select(i => $"Zeile {i} des synthetischen Transkripts."));
        var setTranscript = typeof(MainForm).GetMethod("SetTranscript", BindingFlags.Instance | BindingFlags.NonPublic)!;
        setTranscript.Invoke(form, [sixty]); host.PerformLayout();
        var frame = transcript.Parent!;
        int measuredLineSpacing = transcript.GetPositionFromCharIndex(transcript.GetFirstCharIndexFromLine(1)).Y - transcript.GetPositionFromCharIndex(0).Y;
        bool grows = transcript.ScrollBars == ScrollBars.None && transcript.Height >= transcript.ContentHeight && transcript.FirstVisibleLine == 0 && transcript.LineCount >= 60 && measuredLineSpacing == transcript.LineHeight;
        bool pageScrolls = viewport.VerticalScroll.Visible && viewport.DisplayRectangle.Height > viewport.ClientSize.Height;
        Check($"dictation-transcript-grows-without-inner-scrollbar-{width}", grows && pageScrolls,
            new { transcript.ScrollBars, transcript.Height, transcript.ContentHeight, transcript.LineCount, transcript.FirstVisibleLine, lineHeight = transcript.LineHeight, measuredLineSpacing, frame = frame.Height, viewport = viewport.ClientSize, display = viewport.DisplayRectangle.Size });
        setTranscript.Invoke(form, [""]); viewport.AutoScrollPosition = Point.Empty; host.PerformLayout();
    }
    private static T Get<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static int LinesOf(Label label) => Math.Max(1, (int)Math.Round((label.Height - label.Padding.Vertical) / (double)TextRenderer.MeasureText("Ag", label.Font).Height));
    private static string Lines(int count) => string.Join("\r\n", Enumerable.Range(1, count).Select(i => "Zeile " + i));
    private static IntPtr WheelDown => new(-120 * 65536);
    private static IntPtr ScreenPoint(Control c) { var p = c.PointToScreen(new Point(c.Width / 2, Math.Min(c.Height / 2, 20))); return new IntPtr((p.Y << 16) | (p.X & 0xFFFF)); }
    // Sprint C, Prüffälle 5, 7, 8, 9, 12 und 13 aus Abschnitt 8.
    private static void CheckSprintCLayout(MainForm form, Control host, GardenTabs tabs, int width, float dpiScale)
    {
        // 5: höchstens ein sichtbarer senkrechter Scrollbalken je Reiter; dazu die Zeilenzahl jedes statischen Hinweises (Kriterium 23).
        var counts = new List<object>(); bool atMostOne = true;
        for (int i = 0; i < tabs.TabCount; i++)
        {
            tabs.SelectedIndex = i; host.PerformLayout(); Application.DoEvents();
            var visible = Descendants(tabs.TabPages[i]).Where(c => c.Visible).ToArray();
            int surfaces = visible.OfType<ScrollableControl>().Count(s => s.AutoScroll && s.VerticalScroll.Visible);
            int fields = visible.OfType<TextBox>().Count(t => t.ScrollBars != ScrollBars.None);
            atMostOne &= surfaces + fields <= 1;
            var hints = visible.OfType<Label>().Where(l => l.Text.Length > 40).Select(l => new { start = l.Text[..32], lines = LinesOf(l), overTwo = LinesOf(l) > 2 }).ToArray();
            counts.Add(new { tab = tabs.TabPages[i].Text, scrollSurfaces = surfaces, fieldsWithScrollBars = fields, total = surfaces + fields, hints });
        }
        Check($"one-scroll-surface-per-tab-{width}", atMostOne, counts);
        var workshop = Get<PromptPage>(form, "workshop"); var imports = Get<AudioToolsPage>(form, "imports"); var laboratory = Get<AudioToolsPage>(form, "laboratory");
        tabs.SelectedIndex = 5; host.PerformLayout(); Application.DoEvents();
        // 7 und 8: leer so hoch wie genau die Mindestzeilen; mit mehr Zeilen höher, alle Zeilen sichtbar.
        object Measure(GardenTextBox box, int minimum, out bool pass)
        {
            string saved = box.Text;
            box.Text = ""; host.PerformLayout(); int empty = box.Height;
            box.Text = Lines(minimum); host.PerformLayout(); int exact = box.Height;
            box.Text = Lines(minimum + 8); host.PerformLayout(); int more = box.Height;
            var last = box.GetPositionFromCharIndex(box.TextLength - 1);
            bool allVisible = box.FirstVisibleLine == 0 && last.Y + box.LineHeight <= box.Height && box.ScrollBars == ScrollBars.None;
            box.Text = "A\r\nB"; int spacing = box.GetPositionFromCharIndex(3).Y - box.GetPositionFromCharIndex(0).Y;
            // Visible enthält den Zustand des Elternreiters. Ihn zurückzuschreiben würde
            // ein Feld in einem gerade verdeckten Reiter dauerhaft ausblenden.
            box.Text = saved; host.PerformLayout();
            pass = empty == exact && more > exact && allVisible && spacing == box.LineHeight;
            return new { field = box.AccessibleName, minimum, empty, exact, more, lastY = last.Y, box.LineHeight, measuredLineSpacing = spacing, allVisible };
        }
        var promptFields = new[] { ("original", 4), ("draft", 6), ("candidate", 12), ("libraryPreview", 12) };
        bool promptOk = true; var promptEvidence = promptFields.Select(f => { var e = Measure(Get<GardenTextBox>(workshop, f.Item1), f.Item2, out bool ok); promptOk &= ok; return e; }).ToArray();
        Check($"prompt-fields-grow-from-compact-minimum-{width}", promptOk, promptEvidence);
        var small = new List<(GardenTextBox Box, int Lines)>();
        foreach (var n in new[] { "goal", "audience", "format", "constraints", "reviewAnswer" }) small.Add((Get<GardenTextBox>(workshop, n), 3));
        small.Add((Get<GardenTextBox>(laboratory, "expected"), 3));
        small.Add((Get<GardenTextBox>(imports, "result"), 8));
        bool smallOk = true; var smallEvidence = small.Select(s => { var e = Measure(s.Box, s.Lines, out bool ok); smallOk &= ok; return e; }).ToArray();
        Check($"small-fields-minimum-lines-{width}", smallOk, smallEvidence);
        // 9: kein FixedSingle im Baum; die Ecke der Transkriptkarte hat die Umgebungsfarbe.
        int fixedSingle = Descendants(host).Count(c => c.GetType().GetProperty("BorderStyle")?.GetValue(c) is BorderStyle.FixedSingle);
        tabs.SelectedIndex = 0; host.PerformLayout();
        var card = Descendants(host).OfType<GardenCard>().Single();
        using (var image = RenderClipped(host))
        {
            var at = host.PointToClient(card.PointToScreen(Point.Empty));
            var corner = image.GetPixel(at.X, at.Y); var inside = image.GetPixel(at.X + card.Width / 2, at.Y + (int)(8 * dpiScale));
            Check($"cards-replace-fixedsingle-frames-{width}", fixedSingle == 0 && Near(corner, BrandTheme.Cream, 2) && Near(inside, BrandTheme.Paper, 2),
                new { fixedSingle, corner = corner.Name, inside = inside.Name, radius = BrandTheme.CornerRadius * card.DeviceDpi / 96f });
        }
        // 12: Mausrad über Feldern verschiebt die Seite, nicht das Feld.
        var transcript = Descendants(host).OfType<GardenTextBox>().Single(t => t.AccessibleName == "Letztes Transkript");
        var setTranscript = typeof(MainForm).GetMethod("SetTranscript", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var wheel = new List<object>(); bool wheelOk = true;
        void Wheel(int tab, GardenTextBox box, string name)
        {
            tabs.SelectedIndex = tab; host.PerformLayout(); Application.DoEvents();
            var surface = BrandThemeScrollHost(box)!; surface.AutoScrollPosition = Point.Empty; host.PerformLayout();
            string text = box.Text; int selection = box.SelectionStart; var before = surface.AutoScrollPosition;
            SendMessageW(box.Handle, 0x020A, WheelDown, ScreenPoint(box)); Application.DoEvents();
            var after = surface.AutoScrollPosition;
            bool ok = after != before && box.Text == text && box.SelectionStart == selection && box.FirstVisibleLine == 0;
            wheelOk &= ok; wheel.Add(new { name, before, after, ok, box.Height, box.ContentHeight, surface.ClientSize, surface.DisplayRectangle, surface.VerticalScroll.Visible, surface.IsHandleCreated });
            surface.AutoScrollPosition = Point.Empty;
        }
        setTranscript.Invoke(form, [Lines(60)]); host.PerformLayout();
        Wheel(0, transcript, "Transkript");
        setTranscript.Invoke(form, [""]);
        var draft = Get<GardenTextBox>(workshop, "draft"); Wheel(5, draft, "draft");
        var result = Get<GardenTextBox>(imports, "result"); string savedResult = result.Text; result.Text = Lines(30);
        Wheel(3, result, "result"); result.Text = savedResult;
        Check($"wheel-over-text-field-scrolls-page-{width}", wheelOk, wheel);
        // 13: Mausrad über geschlossenen Auswahllisten ändert die Auswahl nicht und scrollt die Seite.
        tabs.SelectedIndex = 5; host.PerformLayout(); Application.DoEvents();
        var connectionBlock = Get<GardenExpander>(workshop, "connectionBlock"); connectionBlock.Expanded = true; host.PerformLayout(); Application.DoEvents();
        var lists = new[] { Descendants(workshop).OfType<ComboBox>().Single(c => c.AccessibleName == "Formatvorschläge"), Get<ComboBox>(workshop, "reviewProvider") };
        var listEvidence = new List<object>(); bool listsOk = true;
        foreach (var list in lists)
        {
            var surface = BrandThemeScrollHost(list)!; surface.AutoScrollPosition = Point.Empty; host.PerformLayout();
            int index = list.SelectedIndex; var before = surface.AutoScrollPosition;
            SendMessageW(list.Handle, 0x020A, WheelDown, ScreenPoint(list)); Application.DoEvents();
            var after = surface.AutoScrollPosition;
            bool ok = list.SelectedIndex == index && after != before;
            listsOk &= ok; listEvidence.Add(new { list.AccessibleName, index, selectedAfter = list.SelectedIndex, before, after, ok });
            surface.AutoScrollPosition = Point.Empty;
        }
        connectionBlock.Expanded = false;
        Check($"wheel-over-closed-list-keeps-selection-{width}", listsOk, listEvidence);
        tabs.SelectedIndex = 0; host.PerformLayout();
    }
    private static ScrollableControl? BrandThemeScrollHost(Control control)
    {
        for (var parent = control.Parent; parent != null; parent = parent.Parent) if (parent is ScrollableControl s && s.AutoScroll) return s;
        return null;
    }
    private static IEnumerable<ScrollableControl> Ancestors(Control control, Control stop)
    {
        for (var parent = control.Parent; parent != null && parent != stop; parent = parent.Parent)
            if (parent is ScrollableControl scrollable) yield return scrollable;
    }
    // Sprint C: gleicher Aufbau wie die Hauptschleife, damit Fixtures dasselbe dpi-skalierte Fenster rendern.
    private static (MainForm Form, Panel Host, GardenTabs Tabs, float DpiScale) MountMainForm(Size size)
    {
        var form = new MainForm(false);
        float dpiScale = form.DeviceDpi / 96f;
        var host = new Panel { Size = new Size((int)(size.Width * dpiScale), (int)(size.Height * dpiScale)), Font = form.Font, BackColor = form.BackColor };
        host.Controls.Add(form.Controls[0]); host.CreateControl(); host.PerformLayout();
        typeof(MainForm).GetField("modelReady", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
        typeof(MainForm).GetMethod("UpdateUi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, ["Bereit"]);
        host.PerformLayout();
        return (form, host, Descendants(host).OfType<GardenTabs>().Single(), dpiScale);
    }
    private static Rectangle InHost(Control host, Control c) => host.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
    // Pixelprobe: Anzahl tintenfarbener Bildpunkte im untersten Zeilenband eines Hinweises, also dort, wo der letzte Satz steht.
    private static int InkInLastLine(Bitmap image, Control host, Label label)
    {
        var r = InHost(host, label); int line = TextRenderer.MeasureText("Ag", label.Font).Height; int count = 0;
        for (int y = Math.Max(0, r.Bottom - line); y < Math.Min(image.Height, r.Bottom); y++)
            for (int x = Math.Max(0, r.Left); x < Math.Min(image.Width, r.Right); x++)
                if (Near(image.GetPixel(x, y), BrandTheme.Ink, 70)) count++;
        return count;
    }
    // Sprint C, Sperrfixture (Abschnitt 8) mit den Prüffällen 10 und 11 und den zwölf Bildern layout-…-locked-tab{0..5}.png.
    // Drei Müllbytes in die drei Sicherungen, ein eigenes MainForm, danach werden die Dateien wieder gelöscht.
    private static void CheckLockedFixture(string output, Size size)
    {
        string[] names = ["prompt-draft.bin", "import-result.bin", "labor-result.bin"];
        var files = names.Select(n => Path.Combine(AppPaths.Root, n)).ToArray();
        var fresh = files.Select(WorkshopUi.FreshPath).ToArray();
        foreach (string stale in fresh) if (File.Exists(stale)) File.Delete(stale);
        foreach (string file in files) File.WriteAllBytes(file, [3, 1, 4]);
        try
        {
            var mounted = MountMainForm(size);
            using var form = mounted.Form; using var host = mounted.Host; var tabs = mounted.Tabs;
            foreach (int index in Enumerable.Range(0, tabs.TabCount))
            {
                tabs.SelectedIndex = index; host.PerformLayout(); Application.DoEvents();
                using var bitmap = RenderClipped(host);
                bitmap.Save(Path.Combine(output, $"layout-{size.Width}x{size.Height}-locked-tab{index}.png"), ImageFormat.Png);
            }
            const string ending = "dasselbe Windows-Konto verwenden.";
            // 10: Sperrmeldung der Promptwerkstatt vollständig, Auswege sichtbar, „Erneut laden“ ist die Hauptaktion.
            var workshop = Get<PromptPage>(form, "workshop");
            tabs.SelectedIndex = 5; host.PerformLayout(); Application.DoEvents();
            var info = Get<Label>(workshop, "info"); var reload = Get<GardenButton>(workshop, "reloadDraft"); var freshDraft = Get<GardenButton>(workshop, "freshDraft");
            var views = Get<GardenPages>(workshop, "views"); var recoveryRow = Get<FlowLayoutPanel>(workshop, "recoveryRow");
            int preferred = info.GetPreferredSize(new Size(info.Width, 0)).Height;
            Rectangle InPage(Control c) => workshop.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
            var page = workshop.ClientRectangle;
            bool exits = reload.Visible && freshDraft.Visible && page.Contains(InPage(reload)) && page.Contains(InPage(freshDraft)) && !InPage(reload).IntersectsWith(InPage(info));
            bool placed = info.Top >= 0 && info.Bottom <= workshop.ClientSize.Height && info.Top >= views.Bottom && info.Bottom <= recoveryRow.Top;
            int ink;
            using (var image = RenderClipped(host)) ink = InkInLastLine(image, host, info);
            bool primaryOnExit = reload.Primary && !freshDraft.Primary && Descendants(views).OfType<GardenButton>().All(b => !b.Primary);
            Check($"locked-workshop-message-complete-{size.Width}", info.Text.EndsWith(ending) && info.Height >= preferred && placed && exits && ink > 20 && primaryOnExit,
                new { info = info.Bounds, preferred, page = workshop.ClientSize, views = views.Bounds, recoveryRow = recoveryRow.Bounds, reload = InPage(reload), fresh = InPage(freshDraft), inkPixelsInLastLine = ink, primaryOnExit, endsWith = ending });
            // 11: Sperrmeldung beider Audioseiten unten angedockt, vollständig und auch nach Scrollen sichtbar.
            var audio = new List<object>(); bool audioOk = true;
            foreach (var (name, tab) in new[] { ("imports", 3), ("laboratory", 4) })
            {
                var audioPage = Get<AudioToolsPage>(form, name);
                tabs.SelectedIndex = tab; host.PerformLayout(); Application.DoEvents();
                var audioInfo = Get<Label>(audioPage, "info"); var status = Get<Panel>(audioPage, "statusArea");
                var actions = Get<FlowLayoutPanel>(audioPage, "recoveryActions"); var reloadResult = Get<GardenButton>(audioPage, "reloadResult"); var freshResult = Get<GardenButton>(audioPage, "freshResult");
                var flow = Descendants(audioPage).OfType<GardenScrollFlow>().Single();
                Rectangle InAudio(Control c) => audioPage.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
                int audioPreferred = audioInfo.GetPreferredSize(new Size(audioInfo.Width, 0)).Height;
                bool docked = audioInfo.Parent == status && status.Dock == DockStyle.Bottom && status.Bottom == audioPage.ClientSize.Height && !flow.Bounds.IntersectsWith(status.Bounds);
                bool inside = audioPage.ClientRectangle.Contains(InAudio(audioInfo)) && actions.Visible && reloadResult.Visible && freshResult.Visible && audioPage.ClientRectangle.Contains(InAudio(reloadResult)) && audioPage.ClientRectangle.Contains(InAudio(freshResult));
                flow.AutoScrollPosition = new Point(0, flow.DisplayRectangle.Height); host.PerformLayout(); Application.DoEvents();
                var scrolled = flow.AutoScrollPosition;
                bool visibleAfterScroll = scrolled.Y < 0 && audioPage.ClientRectangle.Contains(InAudio(audioInfo));
                int audioInk;
                using (var image = RenderClipped(host)) audioInk = InkInLastLine(image, host, audioInfo);
                flow.AutoScrollPosition = Point.Empty; host.PerformLayout();
                bool ok = audioInfo.Text.EndsWith(ending) && audioInfo.Height >= audioPreferred && docked && inside && visibleAfterScroll && audioInk > 20 && reloadResult.Primary;
                audioOk &= ok;
                audio.Add(new { name, info = InAudio(audioInfo), audioPreferred, status = status.Bounds, page = audioPage.ClientSize, scrolled, inkPixelsInLastLine = audioInk, reloadPrimary = reloadResult.Primary, ok });
            }
            Check($"locked-audio-message-docked-and-complete-{size.Width}", audioOk, audio);
        }
        finally
        {
            foreach (string file in files.Concat(fresh)) if (File.Exists(file)) File.Delete(file);
        }
    }
    // Sprint C, Prüffall 14: Treffer auf eine Reitermitte und die Tasten wechseln den Bereich.
    private static void CheckTabStripInput()
    {
        var mounted = MountMainForm(new Size(900, 740));
        using var form = mounted.Form; using var host = mounted.Host; var tabs = mounted.Tabs;
        var strip = Descendants(host).OfType<GardenTabStrip>().Single(s => s.AccessibleName == "Bereiche");
        var mouseDown = typeof(GardenTabStrip).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var keyDown = typeof(GardenTabStrip).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var steps = new List<object>(); bool ok = true;
        void Step(string name, Action act, int expected) { act(); ok &= tabs.SelectedIndex == expected; steps.Add(new { name, expected, actual = tabs.SelectedIndex }); }
        tabs.SelectedIndex = 0;
        var target = strip.GetTabBounds(3); var center = new Point(target.X + target.Width / 2, target.Y + target.Height / 2);
        Step("Klick Reitermitte 3", () => mouseDown.Invoke(strip, [new MouseEventArgs(MouseButtons.Left, 1, center.X, center.Y, 0)]), 3);
        Step("Links", () => keyDown.Invoke(strip, [new KeyEventArgs(Keys.Left)]), 2);
        Step("Rechts", () => keyDown.Invoke(strip, [new KeyEventArgs(Keys.Right)]), 3);
        Step("Pos1", () => keyDown.Invoke(strip, [new KeyEventArgs(Keys.Home)]), 0);
        Step("Ende", () => keyDown.Invoke(strip, [new KeyEventArgs(Keys.End)]), tabs.TabPages.Count - 1);
        Step("Strg+Tab", () => keyDown.Invoke(strip, [new KeyEventArgs(Keys.Control | Keys.Tab)]), 0);
        Step("Strg+Umschalt+Tab", () => keyDown.Invoke(strip, [new KeyEventArgs(Keys.Control | Keys.Shift | Keys.Tab)]), tabs.TabPages.Count - 1);
        Check("tab-strip-click-and-keys-switch-tabs", ok && strip.HitTest(center) == 3, new { center, steps });
    }
    public static int Run(string projectRoot)
    {
        string output = Path.Combine(projectRoot, "artifacts", "theme"); Directory.CreateDirectory(output);
        AppPaths.Root = Path.Combine(output, "profile"); AppPaths.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            Check("font-file-integrity", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(BrandTheme.Asset("Fonts/IndieFlower-Regular.ttf")))) == "CCC94B22B156E9C5DFE50FD051F01B097600B252C24473E624BB43A143140A94");
            using (var box = new TextBox { Font = BrandTheme.Body })
            {
                var font = SendMessageW(box.Handle, 0x0031, IntPtr.Zero, IntPtr.Zero); // WM_GETFONT, own unshown test control.
                IntPtr dc = CreateCompatibleDC(IntPtr.Zero), old = SelectObject(dc, font);
                try
                {
                    var face = new StringBuilder(100); GetTextFaceW(dc, face.Capacity, face);
                    Check("native-edit-control-actual-gdi-font", face.ToString() == "Indie Flower", face.ToString());
                }
                finally { SelectObject(dc, old); DeleteDC(dc); }
            }
            using (var ico = new BinaryReader(File.OpenRead(BrandTheme.Asset("Sprachgarten.ico"))))
            {
                ico.ReadUInt16(); Check("windows-icon-type", ico.ReadUInt16() == 1); int count = ico.ReadUInt16(); var dimensions = new List<int>();
                for (int i = 0; i < count; i++) { int width = ico.ReadByte(); dimensions.Add(width == 0 ? 256 : width); ico.BaseStream.Position += 15; }
                Check("windows-icon-all-dpi-sizes", new[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 }.SequenceEqual(dimensions), dimensions);
            }
            using (var icon = Icon.ExtractAssociatedIcon(typeof(MainForm).Assembly.Location.Replace(".dll", ".exe")))
                Check("exe-embedded-icon-loads", icon != null);
            foreach (string asset in new[] { "microphone.png", "wordmark.png" })
            {
                using var bitmap = new Bitmap(BrandTheme.Asset(asset));
                var bounds = GardenImage.AlphaBounds(bitmap);
                Check(asset + "-transparent-canvas", bitmap.GetPixel(0, 0).A == 0 && bounds.Width > 100 && bounds.Height > 100, new { bitmap.Width, bitmap.Height, bounds });
            }
            foreach (var size in new[] { new Size(900, 740), new Size(800, 660) })
            {
                using var form = new MainForm(false);
                // Unparented Panel can render child HWNDs without showing a Form (which would initialize live devices).
                float dpiScale = form.DeviceDpi / 96f;
                var physicalSize = new Size((int)(size.Width * dpiScale), (int)(size.Height * dpiScale));
                using var host = new Panel { Size = physicalSize, Font = form.Font, BackColor = form.BackColor };
                host.Controls.Add(form.Controls[0]); host.CreateControl(); host.PerformLayout();
                // Controlled layout states only: no initialization, Show, microphone, hotkey registration or clipboard call.
                typeof(MainForm).GetField("modelReady", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
                typeof(MainForm).GetMethod("UpdateUi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, ["Bereit"]);
                host.PerformLayout();
                var tabs = Descendants(host).OfType<GardenTabs>().Single();
                Check("seven-functional-tabs-" + size.Width, tabs.TabCount == 7);
                foreach (int index in Enumerable.Range(0, tabs.TabCount))
                {
                    tabs.SelectedIndex = index; host.PerformLayout();
                    using var bitmap = RenderClipped(host);
                    bitmap.Save(Path.Combine(output, $"layout-{size.Width}x{size.Height}-tab{index}.png"), ImageFormat.Png);
                }
                CheckCardRightEdges(host, tabs, size.Width);
                // Sprint C, Pflicht-Renderings: Bibliothek der Werkstatt und Auftragsfixture (fremde Lease, Muster ExtensionUiTests.cs:236).
                var workshopViews = Get<GardenPages>(Get<PromptPage>(form, "workshop"), "views");
                tabs.SelectedIndex = 5; workshopViews.SelectedIndex = 1; host.PerformLayout(); Application.DoEvents();
                using (var libraryImage = RenderClipped(host)) libraryImage.Save(Path.Combine(output, $"layout-{size.Width}x{size.Height}-tab5-library.png"), ImageFormat.Png);
                CheckLibraryVersionsClear(Get<PromptPage>(form, "workshop"), size.Width);
                CheckLibraryListMeetsVersions(Get<PromptPage>(form, "workshop"), size.Width);
                workshopViews.SelectedIndex = 0; host.PerformLayout(); Application.DoEvents();
                using (var foreignJob = Get<ActivityGate>(form, "activities").TryEnter("Synthetischer Fremdauftrag"))
                {
                    host.PerformLayout(); Application.DoEvents();
                    using var busyImage = RenderClipped(host);
                    busyImage.Save(Path.Combine(output, $"layout-{size.Width}x{size.Height}-busy-tab5.png"), ImageFormat.Png);
                }
                // Sprint F, Pflichtbild: Einstellungen mit dem Knopf „Modell herunterladen“ im Bild (geprüftes Modell, also ausgegraut). Kein Prüffall.
                tabs.SelectedIndex = 1; host.PerformLayout();
                var modelButton = Get<GardenButton>(form, "modelDownload");
                Control? scrollParent = modelButton.Parent;
                while (scrollParent != null && scrollParent is not ScrollableControl { AutoScroll: true }) scrollParent = scrollParent.Parent;
                if (scrollParent is ScrollableControl modelSurface)
                {
                    modelSurface.ScrollControlIntoView(modelButton); modelSurface.PerformLayout();
                    var shown = modelSurface.RectangleToClient(modelButton.RectangleToScreen(modelButton.ClientRectangle));
                    if (!modelSurface.ClientRectangle.Contains(shown))
                    {
                        // Wie PromptPage.LoadEntry: KeepsScrollPosition kann ScrollControlIntoView stehen lassen, dann direkt setzen.
                        int buttonTop = modelSurface.PointToClient(modelButton.Parent!.PointToScreen(modelButton.Location)).Y - modelSurface.AutoScrollPosition.Y;
                        int gap = (int)Math.Round(BrandTheme.ElementGap * modelSurface.DeviceDpi / 96f);
                        modelSurface.AutoScrollPosition = new Point(0, Math.Max(0, buttonTop - gap));
                    }
                }
                host.PerformLayout(); Application.DoEvents();
                using (var modelImage = RenderClipped(host)) modelImage.Save(Path.Combine(output, $"layout-{size.Width}x{size.Height}-tab1-model.png"), ImageFormat.Png);
                if (scrollParent is ScrollableControl resetSurface) resetSurface.AutoScrollPosition = Point.Empty;
                tabs.SelectedIndex = 0; host.PerformLayout();
                var text = Descendants(host).OfType<TextBox>().Single(t => t.AccessibleName == "Letztes Transkript");
                Check($"transcript-usable-{size.Width}", text.Width >= 500 * dpiScale && text.Height >= (size.Width == 900 ? 150 : 60) * dpiScale, new { text.Width, text.Height, form.DeviceDpi, host.Size });
                CheckTabStrip(host, tabs, size.Width, dpiScale);
                CheckGrowingTranscript(form, host, (GardenTextBox)text, size.Width);
                CheckSprintCLayout(form, host, tabs, size.Width, dpiScale);
                var headerImages = Descendants(host).OfType<GardenImage>().ToArray();
                Check($"brand-images-fit-{size.Width}", headerImages.All(i => i.Right <= i.Parent!.ClientSize.Width && i.Bottom <= i.Parent.ClientSize.Height));
                Check($"header-labels-clear-of-wordmark-{size.Width}", headerImages.Where(i => i.AccessibleName == "Sprachgarten").All(i => i.Parent!.Controls.OfType<Label>().All(l => !i.Bounds.IntersectsWith(l.Bounds))));
                var mainButtons = Descendants(host).OfType<Button>().Where(b => b.Text is "Aufnahme starten" or "Korrektur übernehmen" or "Erneut kopieren" or "Im Hintergrund weiterlaufen" or "Beenden");
                foreach (var b in mainButtons)
                {
                    var preferred = b.GetPreferredSize(Size.Empty);
                    Check($"button-label-fits-{size.Width}-{b.Text}", b.Width >= preferred.Width && b.Height >= preferred.Height, new { b.Size, preferred });
                }
                // Sprint E (S19): Die Schleife prüft nur, was sie findet; genau fünf Knöpfe müssen gefunden werden.
                var foundLabels = mainButtons.Select(b => b.Text).ToArray();
                Check($"button-labels-found-{size.Width}", foundLabels.Length == 5, foundLabels);
                var footer = Descendants(host).OfType<GardenFooter>().Single();
                Check($"footer-visible-{size.Width}", footer.Bottom <= footer.Parent!.ClientSize.Height && footer.Controls.OfType<Button>().All(b => b.Bottom <= footer.ClientSize.Height));
                var state = (SessionState)typeof(MainForm).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                state.Move(DictationState.Error); state.HasPendingAudio = true;
                typeof(MainForm).GetField("pending", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, Path.Combine(AppPaths.Audio, "layout-fixture.wav"));
                typeof(MainForm).GetMethod("UpdateUi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, ["Die Aufnahme konnte nicht transkribiert werden. Bitte erneut versuchen oder bewusst verwerfen."]);
                host.PerformLayout();
                var pendingActions = Descendants(host).OfType<Button>().Where(b => b.Text is "Erneut transkribieren" or "Aufnahme verwerfen").ToArray();
                Check($"error-actions-reachable-{size.Width}", pendingActions.Length == 2 && pendingActions.All(b => b.Enabled && b.Visible && b.Bottom <= b.Parent!.ClientSize.Height) && footer.Bottom <= footer.Parent!.ClientSize.Height);
                var frame = text.Parent!;
                var content = (TableLayoutPanel)frame.Parent!;
                Check($"error-no-overlap-{size.Width}", content.Controls.Cast<Control>().Where(c => c.Visible && c != frame).All(c => !frame.Bounds.IntersectsWith(c.Bounds)), new { frame.Bounds, content.Size });
                using (var errorImage = RenderClipped(host)) { errorImage.Save(Path.Combine(output, $"layout-{size.Width}-error.png"), ImageFormat.Png); }
                // Sprint B, S1: second fixture, text AND recording waiting. Only this state renders the double label.
                state.HasUncopiedText = true;
                typeof(MainForm).GetMethod("UpdateUi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, ["Ein Ergebnis und eine Aufnahme warten noch."]);
                host.PerformLayout();
                var doubleActions = Descendants(host).OfType<Button>().Where(b => b.Text == "Ergebnis und Aufnahme verwerfen" && b.Visible).ToArray();
                var doubleButton = doubleActions.FirstOrDefault();
                var doublePreferred = doubleButton?.GetPreferredSize(Size.Empty) ?? Size.Empty;
                bool insideParent = doubleButton != null && doubleButton.Left >= 0 && doubleButton.Top >= 0 && doubleButton.Right <= doubleButton.Parent!.ClientSize.Width && doubleButton.Bottom <= doubleButton.Parent.ClientSize.Height;
                bool coversNothing = doubleButton != null && doubleButton.Parent!.Controls.Cast<Control>().Where(c => c.Visible && c != doubleButton).All(c => !c.Bounds.IntersectsWith(doubleButton.Bounds));
                bool labelFits = doubleButton != null && doubleButton.Width >= doublePreferred.Width && doubleButton.Height >= doublePreferred.Height;
                Check($"both-pending-actions-labelled-honestly-{size.Width}", doubleActions.Length == 1 && insideParent && coversNothing && labelFits, new { count = doubleActions.Length, bounds = doubleButton?.Bounds, doublePreferred, parent = doubleButton?.Parent!.ClientSize });
                // The dictation page scrolls; bring the action row into view so the image can show the label.
                var dictationViewport = Descendants(host).OfType<Panel>().FirstOrDefault(p => p.AutoScroll && doubleButton != null && Descendants(p).Contains(doubleButton));
                if (dictationViewport != null && doubleButton != null) { dictationViewport.ScrollControlIntoView(doubleButton); host.PerformLayout(); }
                using (var doubleImage = RenderClipped(host)) { doubleImage.Save(Path.Combine(output, $"layout-{size.Width}-error.png"), ImageFormat.Png); }
                if (dictationViewport != null) { dictationViewport.AutoScrollPosition = Point.Empty; host.PerformLayout(); }
                state.HasUncopiedText = false;
                typeof(MainForm).GetMethod("UpdateUi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, ["Die Aufnahme konnte nicht transkribiert werden. Bitte erneut versuchen oder bewusst verwerfen."]);
                host.PerformLayout();
                // Same control tree at larger scale; checks geometry, not actual monitor/DPI switching.
                if (size.Width == 900)
                {
                    host.Scale(new SizeF(1.5f, 1.5f)); host.PerformLayout();
                    using var large = RenderClipped(host);
                    large.Save(Path.Combine(output, "layout-scale150.png"), ImageFormat.Png);
                    Check("scaled-transcript-usable", text.Width >= 750 && text.Height >= 90, new { text.Width, text.Height });
                }
            }
            using (var dialog = new CorrectionDialog(new("Hauß", "Haus"), true, true))
            {
                using var host = new Panel { Size = dialog.ClientSize, Font = dialog.Font, BackColor = dialog.BackColor };
                host.Controls.Add(dialog.Controls[0]); host.CreateControl(); host.PerformLayout();
                using var bitmap = new Bitmap(host.Width, host.Height); host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(output, "correction-dialog.png"), ImageFormat.Png);
                var buttons = Descendants(host).OfType<Button>().ToArray();
                Check("correction-dialog-actions-fit", buttons.All(b => b.Bottom <= b.Parent!.ClientSize.Height && b.Right <= b.Parent.ClientSize.Width), buttons.Select(b => new { b.Text, b.Bounds, ParentSize = b.Parent!.Size }).ToArray());
                // Sprint C, Prüffall 15: genau eine hervorgehobene Schaltfläche, und Enter löst genau sie aus.
                var primaries = Descendants(host).OfType<GardenButton>().Where(b => b.Primary).ToArray();
                Check("correction-dialog-enter-is-the-highlighted-action", primaries.Length == 1 && ReferenceEquals(dialog.AcceptButton, primaries[0]) && primaries[0].Text == "Nur diesmal",
                    new { primaries = primaries.Select(b => b.Text).ToArray(), accept = (dialog.AcceptButton as Button)?.Text, dialog.ClientSize });
            }
            // Sprint C, Prüffall 14: eigene Instanz, damit Treffer und Tasten nicht vom skalierten Bild der Hauptschleife abhängen.
            CheckTabStripInput();
            // Sprint C, Prüffälle 10 und 11 mit Sperrfixture; erst nach dem Hauptfenster, damit dessen Sicherungen die Müllbytes nicht überschreiben.
            foreach (var size in new[] { new Size(900, 740), new Size(800, 660) }) CheckLockedFixture(output, size);
            // Sprint C, Prüffall 16: Overlayfarben als reine Funktion, mit und ohne Hochkontrast.
            var palette = typeof(StatusOverlay).GetMethod("Palette", BindingFlags.Static | BindingFlags.NonPublic)!;
            var highRecording = ((Color Back, Color Fore))palette.Invoke(null, [true, true])!; var highIdle = ((Color Back, Color Fore))palette.Invoke(null, [false, true])!;
            var brandRecording = ((Color Back, Color Fore))palette.Invoke(null, [true, false])!; var brandIdle = ((Color Back, Color Fore))palette.Invoke(null, [false, false])!;
            Check("overlay-high-contrast-palette",
                highRecording == (SystemColors.Highlight, SystemColors.HighlightText) && highIdle == (SystemColors.Highlight, SystemColors.HighlightText)
                && brandRecording == (BrandTheme.Recording, Color.White) && brandIdle == (BrandTheme.Petrol, Color.White),
                new { highRecording = new[] { highRecording.Back.Name, highRecording.Fore.Name }, highIdle = new[] { highIdle.Back.Name, highIdle.Fore.Name }, brandRecording = new[] { brandRecording.Back.Name, brandRecording.Fore.Name }, brandIdle = new[] { brandIdle.Back.Name, brandIdle.Fore.Name } });
            // Sprint C, Prüffall 17: Der Hotkey-Dialog bekommt seine Farben aus BrandTheme.Apply.
            var built = ((Form Dialog, Label Hint))typeof(MainForm).GetMethod("CreateHotkeyDialog", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
            using (built.Dialog)
            using (var probe = new Panel())
            {
                BrandTheme.Apply(probe);
                Check("hotkey-dialog-uses-theme", built.Dialog.BackColor == probe.BackColor && built.Dialog.ForeColor == probe.ForeColor && built.Hint.BackColor == probe.BackColor && built.Hint.ForeColor == probe.ForeColor,
                    new { dialog = built.Dialog.BackColor.Name, applied = probe.BackColor.Name, fore = built.Dialog.ForeColor.Name, highContrast = SystemInformation.HighContrast });
            }
        }
        catch (Exception e) { Check("theme-test-runner", false, e.ToString()); }
        finally { File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); BrandTheme.Release(); }
        return failed == 0 ? 0 : 1;
    }
}
