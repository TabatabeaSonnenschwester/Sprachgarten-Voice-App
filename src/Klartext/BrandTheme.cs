using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Klartext;

/// <summary>Process-local brand assets. No machine-wide font installation or profile migration.</summary>
public static class BrandTheme
{
    public const string AppName = "Sprachgarten";
    public static readonly Color Cream = ColorTranslator.FromHtml("#FFF7E9");
    public static readonly Color Paper = ColorTranslator.FromHtml("#FFFEFB");
    public static readonly Color Ink = ColorTranslator.FromHtml("#432719");
    public static readonly Color Copper = ColorTranslator.FromHtml("#A94F28");
    public static readonly Color Petrol = ColorTranslator.FromHtml("#1D5C61");
    public static readonly Color Mustard = ColorTranslator.FromHtml("#E1A735");
    public static readonly Color Coral = ColorTranslator.FromHtml("#EF9986");
    public static readonly Color Olive = ColorTranslator.FromHtml("#808B46");
    public static readonly Color Recording = ColorTranslator.FromHtml("#AC353F");
    private static readonly Lazy<Resources> resources = new(() => new Resources());
    public static Font Body => resources.Value.Body;
    public static Font Small => resources.Value.Small;
    public static Font Large => resources.Value.Large;
    public static Image Microphone => resources.Value.Microphone;
    public static Image Wordmark => resources.Value.Wordmark;
    public static Icon Icon => resources.Value.Icon;
    public static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);
    public static void Release() { if (resources.IsValueCreated) resources.Value.Dispose(); }

    // Sprint C: Abstandsraster in logischen Pixeln, Grundeinheit 8.
    public const int SpaceUnit = 8;
    public const int PagePadding = 2 * SpaceUnit;
    public const int CardPadding = 2 * SpaceUnit;
    public const int CardGap = 2 * SpaceUnit;
    public const int ElementGap = SpaceUnit;
    public const int CornerRadius = 9;
    public const int TabStripHeight = 44;
    // Vorhandene Werte aus Fußzeile, Knopf-Hover und gesperrten Knöpfen, nur benannt; keine neue Palettenfarbe.
    internal static readonly Color CardLine = Color.FromArgb(213, 200, 174);
    internal static readonly Color HoverFill = Color.FromArgb(252, 235, 207);
    internal static readonly Color LockedLine = Color.FromArgb(177, 162, 140);
    internal const int WM_MOUSEWHEEL = 0x020A;
    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);

    /// <summary>Nächste Vorfahrfläche mit eigenem Scrollen; dorthin gehen Mausrad und Schreibmarken-Nachführung.</summary>
    internal static ScrollableControl? ScrollHost(Control control)
    {
        for (var parent = control.Parent; parent != null; parent = parent.Parent)
            if (parent is ScrollableControl scrollable && scrollable.AutoScroll) return scrollable;
        return null;
    }

    /// <summary>Regel gegen den Fokussprung: ein hohes Feld, das schon teilweise sichtbar ist, verschiebt die Seite nicht.</summary>
    /// <remarks>Falle für künftigen Code: Auch ein ausdrückliches <c>ScrollControlIntoView</c> auf ein teilweise sichtbares Textfeld
    /// bewegt deshalb nichts. Zum gezielten Springen <c>AutoScrollPosition</c> setzen, Muster <c>PromptPage.LoadEntry</c> (PromptPage.cs:403-407).</remarks>
    internal static bool KeepsScrollPosition(ScrollableControl surface, Control target)
    {
        if (!surface.IsHandleCreated || !target.IsHandleCreated) return false;
        var bounds = surface.RectangleToClient(target.RectangleToScreen(target.ClientRectangle));
        bool partlyVisible = bounds.IntersectsWith(surface.ClientRectangle);
        return partlyVisible && (target is TextBoxBase || bounds.Height > surface.ClientSize.Height);
    }

    /// <summary>Sprint C (S9): Größe und Mindestgröße eines Dialogs höchstens so groß wie der Arbeitsbereich seines Bildschirms.</summary>
    internal static void FitToWorkingArea(Form form)
    {
        var area = Screen.FromControl(form).WorkingArea;
        form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, area.Width), Math.Min(form.MinimumSize.Height, area.Height));
        form.Size = new Size(Math.Min(form.Width, area.Width), Math.Min(form.Height, area.Height));
    }

    /// <summary>Hängt an eine Auswahlliste an: Bei geschlossener Liste scrollt das Mausrad die Seite statt still den Eintrag zu wechseln.</summary>
    public static void RelayWheel(ComboBox box) => GardenWheelRelay.Attach(box);

    internal static GraphicsPath RoundTop(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddLine(r.Right, r.Y + d / 2, r.Right, r.Bottom); p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
        p.CloseFigure(); return p;
    }

    /// <summary>Kartenfläche mit weichen Ecken; die Ecken außerhalb des Radius behalten die Farbe der Umgebung.</summary>
    internal static void PaintCard(Control card, Graphics g, bool highContrast)
    {
        float scale = card.DeviceDpi / 96f;
        g.Clear(card.Parent?.BackColor ?? (highContrast ? SystemColors.Control : Cream));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Round(new RectangleF(scale / 2, scale / 2, card.Width - scale, card.Height - scale), CornerRadius * scale);
        using var fill = new SolidBrush(card.BackColor); g.FillPath(fill, path);
        using var pen = new Pen(highContrast ? SystemColors.WindowFrame : CardLine, scale); g.DrawPath(pen, path);
    }

    /// <summary>Gezeichneter Rahmen um die direkten wachsenden Textfelder einer Fläche: Kupfer, bei Fokus Petrol, schreibgeschützt gedämpft.</summary>
    internal static void PaintFieldFrames(Control surface, Graphics g, bool highContrast)
    {
        float scale = surface.DeviceDpi / 96f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (Control child in surface.Controls)
        {
            if (child is not GardenTextBox box || !box.Visible) continue;
            bool focused = box.Focused;
            float inset = 3 * scale;
            var frame = new RectangleF(box.Left - inset, box.Top - inset, box.Width + inset * 2, box.Height + inset * 2);
            using var path = Round(frame, 6 * scale);
            using var fill = new SolidBrush(box.BackColor); g.FillPath(fill, path);
            Color line = highContrast ? (focused ? SystemColors.Highlight : SystemColors.WindowFrame) : focused ? Petrol : box.ReadOnly ? LockedLine : Copper;
            using var pen = new Pen(line, (focused ? 2 : 1) * scale); g.DrawPath(pen, path);
        }
    }

    private sealed class Resources : IDisposable
    {
        private readonly PrivateFontCollection fonts = new();
        public readonly Font Body, Small, Large;
        public readonly Image Microphone, Wordmark;
        public readonly Icon Icon;
        public Resources()
        {
            // .NET 10 AddFontFile registers both GDI+ and GDI FR_PRIVATE; keep collection alive.
            fonts.AddFontFile(Asset(Path.Combine("Fonts", "IndieFlower-Regular.ttf")));
            var family = fonts.Families.Single(f => f.Name == "Indie Flower");
            Body = new Font(family, 14f, FontStyle.Regular);
            Small = new Font(family, 12f, FontStyle.Regular);
            Large = new Font(family, 18f, FontStyle.Regular);
            Microphone = Image.FromFile(Asset("microphone.png"));
            Wordmark = Image.FromFile(Asset("wordmark.png"));
            Icon = new Icon(Asset("Sprachgarten.ico"), 48, 48);
        }
        public void Dispose()
        {
            Icon.Dispose(); Wordmark.Dispose(); Microphone.Dispose();
            Large.Dispose(); Small.Dispose(); Body.Dispose(); fonts.Dispose();
        }
    }

    public static void Apply(Control parent) => Apply(parent, SystemInformation.HighContrast ? SystemColors.Control : Cream);

    // Karten tragen Paper als Fläche; alles darin erbt sie, damit Hinweise nicht als Cream-Flecken erscheinen.
    private static void Apply(Control parent, Color surface)
    {
        if (!SystemInformation.HighContrast && parent is GardenCard or GardenCardFlow) surface = Paper;
        parent.ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : Ink;
        parent.BackColor = surface;
        foreach (Control child in parent.Controls)
        {
            Apply(child, surface);
            if (child is GardenTextBox garden) garden.ApplyTheme();
            else if (child is TextBoxBase or ListView or ComboBox)
            {
                child.BackColor = SystemInformation.HighContrast ? SystemColors.Window : Paper;
                child.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : Ink;
            }
            if (child is GardenImage) child.BackColor = Color.Transparent;
        }
    }

    internal static GraphicsPath Round(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
}

/// <summary>Native Button keyboard/accessibility semantics with the approved rounded treatment.</summary>
public sealed class GardenButton : Button
{
    private bool primary;
    [DefaultValue(false)]
    public bool Primary
    {
        get => primary;
        set { if (primary == value) return; primary = value; Invalidate(); }
    }
    private bool hovering, pressed;
    public GardenButton()
    {
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        Padding = new Padding(16, 7, 16, 7); Margin = new Padding(0, BrandTheme.ElementGap / 2, BrandTheme.ElementGap + BrandTheme.ElementGap / 2, BrandTheme.ElementGap / 2);
        MinimumSize = new Size(0, 44); Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovering = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
    protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
        e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color fill = Primary ? BrandTheme.Petrol : BrandTheme.Cream;
        if (pressed && Enabled) fill = Primary ? Color.FromArgb(17, 66, 69) : Color.FromArgb(239, 217, 183);
        else if (hovering && Enabled) fill = Primary ? Color.FromArgb(35, 110, 115) : Color.FromArgb(252, 235, 207);
        if (!Enabled) fill = Color.FromArgb(235, 227, 211);
        using var path = BrandTheme.Round(new RectangleF(1, 1, Width - 3, Height - 3), 9f * DeviceDpi / 96);
        using var brush = new SolidBrush(fill); e.Graphics.FillPath(brush, path);
        using var pen = new Pen(Enabled ? (Primary ? BrandTheme.Petrol : BrandTheme.Copper) : Color.FromArgb(177, 162, 140), DeviceDpi / 96f);
        e.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
            Enabled ? (Primary ? Color.White : BrandTheme.Ink) : Color.FromArgb(120, 111, 97),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        if (Focused && ShowFocusCues)
        {
            var focus = ClientRectangle; focus.Inflate(-5, -5);
            ControlPaint.DrawFocusRectangle(e.Graphics, focus, Primary ? Color.White : BrandTheme.Ink, fill);
        }
    }
}

/// <summary>TabControl ohne native Kopfzeile: Die Seiten füllen die ganze Fläche, die Auswahl trägt eine GardenTabStrip.</summary>
public class GardenPages : TabControl
{
    private const int TCM_ADJUSTRECT = 0x1328;
    public GardenPages() { TabStop = false; }
    protected override void WndProc(ref Message m)
    {
        // Die Anzeigefläche bleibt das ganze Rechteck; damit gibt es weder graue Leiste noch Pfeilknöpfe.
        if (m.Msg == TCM_ADJUSTRECT) { m.Result = (IntPtr)1; return; }
        base.WndProc(ref m);
    }
}

/// <summary>Die sechs Bereiche des Hauptfensters; einzige Instanz, Auswahl über die Leiste „Bereiche“.</summary>
public sealed class GardenTabs : GardenPages { }

/// <summary>Reiterleiste mit vollständigen Beschriftungen; bei Platzmangel zusätzliche Zeilen.</summary>
public sealed class GardenTabStrip : Control
{
    private readonly TabControl pages;
    private Rectangle[] tabs = [];
    private int hover = -1;
    private Font tabFont = BrandTheme.Body;
    private bool layingOut;
    /// <summary>True, wenn die Leiste auf BrandTheme.Small zurückfallen musste.</summary>
    public bool UsesSmallFont { get; private set; }
    /// <summary>Tatsächlich benutzter Innenabstand je Seite, in logischen Pixeln (16 bis 8).</summary>
    public int TabPadding { get; private set; } = 16;
    public GardenTabStrip(TabControl pages)
    {
        this.pages = pages;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true; AccessibleRole = AccessibleRole.PageTabList; Dock = DockStyle.Top; Margin = Padding.Empty;
        Height = BrandTheme.TabStripHeight; Cursor = Cursors.Hand;
        pages.SelectedIndexChanged += (_, _) => Invalidate();
        pages.ControlAdded += (_, e) => { if (e.Control != null) e.Control.TextChanged += (_, _) => Relayout(); Relayout(); };
        pages.ControlRemoved += (_, _) => Relayout();
        foreach (TabPage page in pages.TabPages) page.TextChanged += (_, _) => Relayout();
    }
    public int TabCount => tabs.Length;
    public Rectangle GetTabBounds(int index) => index >= 0 && index < tabs.Length ? tabs[index] : Rectangle.Empty;
    public Font TabFont => tabFont;
    public int HitTest(Point point) { for (int i = 0; i < tabs.Length; i++) if (tabs[i].Contains(point)) return i; return -1; }
    private void Relayout()
    {
        if (layingOut || Width <= 0) return;
        layingOut = true;
        try
        {
        int count = pages.TabCount;
        tabs = new Rectangle[count];
        if (count == 0) { Invalidate(); return; }
        float scale = DeviceDpi / 96f;
        string[] texts = pages.TabPages.Cast<TabPage>().Select(p => p.Text).ToArray();
        int[] widths = [];
        // Auf derselben Zeichenfläche messen wie beim Zeichnen: wichtig bei Windows-DPI-Skalierung.
        using var graphics = CreateGraphics();
        foreach (var font in new[] { BrandTheme.Body, BrandTheme.Small })
        {
            for (int pad = 16; pad >= 8; pad--)
            {
                widths = texts.Select(t => TextRenderer.MeasureText(graphics, t, font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width + (int)Math.Ceiling(2 * pad * scale)).ToArray();
                tabFont = font; TabPadding = pad; UsesSmallFont = font == BrandTheme.Small;
                if (widths.Sum() <= Width) goto measured;
            }
        }
        measured:
        int x = 0, y = 0, rowHeight = (int)Math.Ceiling(BrandTheme.TabStripHeight * scale);
        for (int i = 0; i < count; i++)
        {
            if (x > 0 && x + widths[i] > Width) { x = 0; y += rowHeight; }
            tabs[i] = new Rectangle(x, y, widths[i], rowHeight - (int)Math.Ceiling(scale)); x += widths[i];
        }
        Height = y + rowHeight;
        Invalidate();
        }
        finally { layingOut = false; }
    }
    private void SelectTab(int index)
    {
        if (pages.TabCount == 0) return;
        pages.SelectedIndex = Math.Clamp(index, 0, pages.TabCount - 1);
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Height = BrandTheme.TabStripHeight * DeviceDpi / 96; Relayout(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); Height = BrandTheme.TabStripHeight * DeviceDpi / 96; Relayout(); }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Relayout(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int hit = HitTest(e.Location);
        if (e.Button == MouseButtons.Left && hit >= 0) { SelectTab(hit); if (CanFocus) Focus(); }
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int hit = HitTest(e.Location); if (hit != hover) { hover = hit; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Home or Keys.End || keyData is (Keys.Control | Keys.Tab) or (Keys.Control | Keys.Shift | Keys.Tab) || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        int current = pages.SelectedIndex, count = pages.TabCount;
        if (e.KeyCode == Keys.Tab && e.Control && count > 0) { SelectTab((current + (e.Shift ? count - 1 : 1)) % count); e.Handled = true; }
        else if (e.KeyCode == Keys.Left) { SelectTab(current - 1); e.Handled = true; }
        else if (e.KeyCode == Keys.Right) { SelectTab(current + 1); e.Handled = true; }
        else if (e.KeyCode == Keys.Home) { SelectTab(0); e.Handled = true; }
        else if (e.KeyCode == Keys.End) { SelectTab(count - 1); e.Handled = true; }
        base.OnKeyDown(e);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; float scale = DeviceDpi / 96f;
        bool highContrast = SystemInformation.HighContrast;
        g.Clear(highContrast ? SystemColors.Control : BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int marker = (int)Math.Round(4 * scale);
        for (int i = 0; i < tabs.Length && i < pages.TabCount; i++)
        {
            var r = tabs[i]; bool selected = i == pages.SelectedIndex;
            Color text;
            if (highContrast)
            {
                // Hochkontrast: Auswahl wie bisher über Systemfarben, Marke in HighlightText.
                if (selected) { using var b = new SolidBrush(SystemColors.Highlight); g.FillRectangle(b, r); using var m = new SolidBrush(SystemColors.HighlightText); g.FillRectangle(m, r.X, r.Bottom - marker, r.Width, marker); }
                text = selected ? SystemColors.HighlightText : SystemColors.ControlText;
            }
            else
            {
                if (selected || i == hover)
                {
                    using var path = BrandTheme.RoundTop(new RectangleF(r.X, r.Y, r.Width, r.Height), BrandTheme.CornerRadius * scale);
                    using var b = new SolidBrush(selected ? BrandTheme.Mustard : BrandTheme.HoverFill); g.FillPath(b, path);
                }
                if (selected) { using var m = new SolidBrush(BrandTheme.Petrol); g.FillRectangle(m, r.X, r.Bottom - marker, r.Width, marker); }
                text = BrandTheme.Ink;
            }
            var textArea = new Rectangle(r.X, r.Y, r.Width, r.Height - marker);
            TextRenderer.DrawText(g, pages.TabPages[i].Text, tabFont, textArea, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (selected && Focused && ShowFocusCues) { var f = textArea; f.Inflate(-4, -4); ControlPaint.DrawFocusRectangle(g, f); }
        }
        using var line = new Pen(highContrast ? SystemColors.ControlText : BrandTheme.CardLine, Math.Max(1, scale));
        float y = Height - Math.Max(1, scale) / 2;
        g.SmoothingMode = SmoothingMode.None; g.DrawLine(line, 0, y, Width, y);
    }
}

/// <summary>Resizable code-native ornament based on the approved wave/flower composition.</summary>
public sealed class GardenHeader : Panel
{
    public GardenHeader() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (SystemInformation.HighContrast) return;
        var g = e.Graphics; var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        g.TranslateTransform(Width - 300 * scale, 0); g.ScaleTransform(scale, scale);
        // Nested silhouettes create flowing ribbons that leave the status text clear below.
        Color[] colors = [BrandTheme.Coral, BrandTheme.Copper, BrandTheme.Mustard, BrandTheme.Petrol];
        for (int i = 0; i < colors.Length; i++)
        {
            float offset = i * 19;
            using var path = new GraphicsPath();
            path.AddLine(-35 + offset * 1.8f, 0, 320, 0); path.AddLine(320, 0, 320, 104 - offset);
            path.AddBezier(320, 104 - offset, 249, 92 - offset, 283, 50 - offset / 2, 178, 49 - offset / 2);
            path.AddBezier(178, 49 - offset / 2, 86, 50 - offset / 2, 90, 8, -35 + offset * 1.8f, 0);
            path.CloseFigure(); using var b = new SolidBrush(colors[i]); g.FillPath(b, path);
        }
        DrawFlower(g, 258, 44, 28, BrandTheme.Paper);
        g.Restore(saved);
    }
    internal static void DrawFlower(Graphics g, float x, float y, float radius, Color petals)
    {
        var saved = g.Save(); g.TranslateTransform(x, y); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var leaves = new SolidBrush(BrandTheme.Olive);
        g.RotateTransform(-38); g.FillEllipse(leaves, -radius * .38f, radius * .7f, radius * .7f, radius * 1.5f);
        g.RotateTransform(100); g.FillEllipse(leaves, -radius * .32f, radius * .7f, radius * .6f, radius * 1.4f);
        g.RotateTransform(-62);
        using var brush = new SolidBrush(petals);
        for (int i = 0; i < 8; i++) { g.FillEllipse(brush, -radius * .3f, -radius, radius * .6f, radius); g.RotateTransform(45); }
        using var center = new SolidBrush(BrandTheme.Mustard); g.FillEllipse(center, -radius * .32f, -radius * .32f, radius * .64f, radius * .64f);
        g.Restore(saved);
    }
}

public sealed class GardenFooter : FlowLayoutPanel
{
    public GardenFooter() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (SystemInformation.HighContrast) return;
        using var pen = new Pen(Color.FromArgb(213, 200, 174)); e.Graphics.DrawLine(pen, 0, 0, Width, 0);
        if (Width > 700 * DeviceDpi / 96) GardenHeader.DrawFlower(e.Graphics, Width - 40 * DeviceDpi / 96, Height / 2f, 17 * DeviceDpi / 96f, BrandTheme.Mustard);
    }
}

/// <summary>Fits the visible artwork, ignoring transparent artboard margins without changing source files.</summary>
public sealed class GardenImage : Control
{
    private readonly Image image;
    private readonly Rectangle source;
    public GardenImage(Image image)
    {
        this.image = image; DoubleBuffered = true; AccessibleRole = AccessibleRole.Graphic;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent;
        source = AlphaBounds(image); SetStyle(ControlStyles.ResizeRedraw, true);
    }
    public static Rectangle AlphaBounds(Image image)
    {
        using var bitmap = new Bitmap(image);
        int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[bitmap.Width * 4];
            for (int y = 0; y < bitmap.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = 0; x < bitmap.Width; x++)
                    if (row[x * 4 + 3] > 12) { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            }
        }
        finally { bitmap.UnlockBits(data); }
        return right < left ? new Rectangle(0, 0, image.Width, image.Height) : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        float ratio = Math.Min(Width / (float)source.Width, Height / (float)source.Height);
        var target = new RectangleF((Width - source.Width * ratio) / 2, (Height - source.Height * ratio) / 2, source.Width * ratio, source.Height * ratio);
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(image, target, source, GraphicsUnit.Pixel);
    }
}

/// <summary>
/// Mehrzeiliges Feld ohne eigenen Scrollbalken: Es wächst mit seinen Zeilen, hat eine Mindestzeilenzahl,
/// reicht das Mausrad an die Seite weiter und zeigt Schreibschutz durch eine eigene Füllung.
/// </summary>
public sealed class GardenTextBox : TextBox
{
    private const int EM_LINESCROLL = 0xB6, EM_GETLINECOUNT = 0xBA, EM_GETFIRSTVISIBLELINE = 0xCE, EM_SETMARGINS = 0xD3;
    private int minLines = 1;
    private bool autoGrow = true, sizing;
    public event EventHandler? ContentHeightChanged;
    public GardenTextBox()
    {
        Multiline = true; WordWrap = true; ScrollBars = ScrollBars.None; BorderStyle = BorderStyle.None;
        Margin = new Padding(4, 6, 4, 6);
        ApplyTheme();
    }
    [DefaultValue(1)]
    public int MinLines { get => minLines; set { minLines = Math.Max(1, value); UpdateHeight(); } }
    /// <summary>False, wenn ein Elternteil die Höhe vorgibt (Transkript füllt seine Karte); ContentHeight bleibt gültig.</summary>
    [DefaultValue(true)]
    public bool AutoGrow { get => autoGrow; set { autoGrow = value; UpdateHeight(); } }
    /// <summary>Höhe, die alle Zeilen (mindestens MinLines) ohne Scrollen zeigt.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ContentHeight { get; private set; }
    /// <summary>Zeilenabstand der Schrift, wie ihn auch das Eingabefeld benutzt.</summary>
    public int LineHeight => TextRenderer.MeasureText("Ag", Font).Height;
    /// <summary>Anzahl der dargestellten Zeilen einschließlich Umbrüchen; ohne Fenster nach harten Zeilenwechseln geschätzt.</summary>
    public int LineCount => IsHandleCreated ? Math.Max(1, (int)BrandTheme.SendMessage(Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero)) : Text.Split('\n').Length;
    public int FirstVisibleLine => IsHandleCreated ? (int)BrandTheme.SendMessage(Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero) : 0;
    private int Slack => (int)Math.Ceiling(4 * DeviceDpi / 96f);
    internal void ApplyTheme()
    {
        bool highContrast = SystemInformation.HighContrast;
        BackColor = highContrast ? (ReadOnly ? SystemColors.Control : SystemColors.Window) : ReadOnly ? BrandTheme.Cream : BrandTheme.Paper;
        ForeColor = highContrast ? SystemColors.WindowText : BrandTheme.Ink;
    }
    /// <summary>
    /// Risiko 3 aus dem Sprint-C-Brief: Windows begrenzt Fensterhöhen auf 32.767 Bildpunkte. Darüber (zum Beispiel
    /// 24.000 Zeichen aus lauter kurzen Zeilen) bleibt das Feld bei dieser Höchsthöhe und bekommt ausnahmsweise
    /// einen eigenen Scrollbalken. Das ist die eine benannte Ausnahme von „kein Textfeld scrollt selbst“.
    /// </summary>
    public const int MaxFieldHeight = 30000;
    /// <summary>True, solange das Feld die Höchsthöhe erreicht hat und deshalb selbst scrollt.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Capped { get; private set; }
    private void UpdateHeight()
    {
        if (sizing || !Multiline) return;
        bool changed = false;
        sizing = true;
        try
        {
            int content = Math.Max(minLines, LineCount) * LineHeight + Slack;
            bool capped = content > MaxFieldHeight;
            if (capped) content = MaxFieldHeight;
            if (capped != Capped)
            {
                // Nur beim Wechsel umschalten: Der Setter erzeugt das Fenster neu; OnHandleCreated läuft wegen „sizing“ nicht erneut hierher.
                Capped = capped;
                ScrollBars = capped ? ScrollBars.Vertical : ScrollBars.None;
                SetInnerMargins();
            }
            if (content != ContentHeight) { ContentHeight = content; changed = true; }
            if (autoGrow && Dock == DockStyle.None && Height != content) Height = content;
            ResetFirstLine();
        }
        finally { sizing = false; }
        if (changed) ContentHeightChanged?.Invoke(this, EventArgs.Empty);
    }
    private void ResetFirstLine()
    {
        // Das Feld selbst scrollt nie; ein Rest aus dem Tippen am unteren Rand wird zurückgesetzt.
        if (!IsHandleCreated || ScrollBars != ScrollBars.None || Height < ContentHeight) return;
        int first = FirstVisibleLine;
        if (first > 0) BrandTheme.SendMessage(Handle, EM_LINESCROLL, IntPtr.Zero, -first);
    }
    private void SetInnerMargins()
    {
        if (!IsHandleCreated) return;
        int side = (int)Math.Round(4 * DeviceDpi / 96f);
        BrandTheme.SendMessage(Handle, EM_SETMARGINS, 3, (side & 0xFFFF) | (side << 16));
    }
    /// <summary>Schiebt die Seite so, dass die Schreibmarke samt einer Zeile darunter sichtbar ist.</summary>
    internal void KeepCaretVisible()
    {
        if (!IsHandleCreated || BrandTheme.ScrollHost(this) is not { IsHandleCreated: true } host) return;
        int line = LineHeight, index = SelectionStart;
        Point caret;
        if (TextLength == 0) caret = Point.Empty;
        else if (index >= TextLength)
        {
            caret = GetPositionFromCharIndex(TextLength - 1);
            if (Text[^1] == '\n') caret = new Point(0, caret.Y + line);
        }
        else caret = GetPositionFromCharIndex(index);
        int top = host.PointToClient(PointToScreen(caret)).Y;
        int bottom = top + line * 2;
        int current = -host.AutoScrollPosition.Y, target = current;
        if (bottom > host.ClientSize.Height) target = current + bottom - host.ClientSize.Height;
        else if (top < 0) target = Math.Max(0, current + top - line);
        if (target != current) host.AutoScrollPosition = new Point(-host.AutoScrollPosition.X, target);
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); SetInnerMargins(); UpdateHeight(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); SetInnerMargins(); UpdateHeight(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); SetInnerMargins(); UpdateHeight(); }
    protected override void OnMultilineChanged(EventArgs e) { base.OnMultilineChanged(e); UpdateHeight(); }
    protected override void OnTextChanged(EventArgs e)
    {
        // Erst die Höhe, dann die Zuhörer: Wer auf TextChanged das Layout rechnet, sieht schon die neue Höhe.
        UpdateHeight();
        base.OnTextChanged(e);
        if (Focused) KeepCaretVisible();
    }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (Focused) KeepCaretVisible(); }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateHeight(); Parent?.Invalidate(); }
    protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); Parent?.Invalidate(); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); Parent?.Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Parent?.Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Parent?.Invalidate(); }
    protected override void OnReadOnlyChanged(EventArgs e) { base.OnReadOnlyChanged(e); ApplyTheme(); Parent?.Invalidate(); }
    protected override void WndProc(ref Message m)
    {
        // Das Mausrad scrollt die Seite, nicht das Feld.
        if (m.Msg == BrandTheme.WM_MOUSEWHEEL && ScrollBars == ScrollBars.None && BrandTheme.ScrollHost(this) is { IsHandleCreated: true } host)
        {
            BrandTheme.SendMessage(host.Handle, m.Msg, m.WParam, m.LParam);
            m.Result = IntPtr.Zero; return;
        }
        base.WndProc(ref m);
    }
}

/// <summary>Karte mit weichen Ecken statt des klassischen Einfachrahmens; zeichnet auch die Rahmen der enthaltenen wachsenden Felder.</summary>
public sealed class GardenCard : Panel
{
    public GardenCard() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); Padding = new Padding(BrandTheme.CardPadding); }
    protected override void OnPaintBackground(PaintEventArgs e) => BrandTheme.PaintCard(this, e.Graphics, SystemInformation.HighContrast);
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); BrandTheme.PaintFieldFrames(this, e.Graphics, SystemInformation.HighContrast); }
}

/// <summary>Dieselbe Karte als senkrechter Fluss, für Werkstattabschnitte, Audiogruppen und Einstellungen.</summary>
public sealed class GardenCardFlow : FlowLayoutPanel
{
    public GardenCardFlow()
    {
        DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true);
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoScroll = false;
        Padding = new Padding(BrandTheme.CardPadding); Margin = new Padding(0, 0, 0, BrandTheme.CardGap);
    }
    protected override void OnPaintBackground(PaintEventArgs e) => BrandTheme.PaintCard(this, e.Graphics, SystemInformation.HighContrast);
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); BrandTheme.PaintFieldFrames(this, e.Graphics, SystemInformation.HighContrast); }
}

/// <summary>Einklappbarer Block ohne eigenes Scrollen; die Kopfzeile ist kein Button und bleibt deshalb auch während eines Auftrags bedienbar.</summary>
public sealed class GardenExpander : FlowLayoutPanel
{
    private bool expanded;
    private string title, detail = "";
    public GardenExpanderHeader Header { get; }
    public event EventHandler? ExpandedChanged;
    public GardenExpander(string title)
    {
        this.title = title;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoScroll = false;
        Margin = new Padding(0, BrandTheme.ElementGap / 2, 0, BrandTheme.ElementGap / 2);
        DoubleBuffered = true; AccessibleName = title;
        Header = new GardenExpanderHeader(this);
        Controls.Add(Header);
        ControlAdded += (_, e) => { if (e.Control != null && e.Control != Header) e.Control.Visible = expanded; };
        UpdateHeader();
    }
    [DefaultValue("")]
    public string Title { get => title; set { title = value; UpdateHeader(); } }
    /// <summary>Zusatz in der Kopfzeile, etwa der gewählte Anbieter.</summary>
    [DefaultValue("")]
    public string Detail { get => detail; set { detail = value ?? ""; UpdateHeader(); } }
    [DefaultValue(false)]
    public bool Expanded
    {
        get => expanded;
        set
        {
            if (expanded == value) return;
            expanded = value;
            SuspendLayout();
            foreach (Control child in Controls) if (child != Header) child.Visible = value;
            ResumeLayout(true);
            UpdateHeader();
            ExpandedChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public void Toggle() => Expanded = !Expanded;
    private void UpdateHeader()
    {
        Header.Text = detail.Length == 0 ? title : title + " · " + detail;
        Header.AccessibleName = Header.Text;
        Header.AccessibleDescription = expanded ? "aufgeklappt" : "zugeklappt";
        Header.Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); BrandTheme.PaintFieldFrames(this, e.Graphics, SystemInformation.HighContrast); }
}

/// <summary>Kopfzeile eines GardenExpander: Klick, Leertaste und Enter klappen auf und zu.</summary>
public sealed class GardenExpanderHeader : Control
{
    private readonly GardenExpander owner;
    private bool hovering;
    internal GardenExpanderHeader(GardenExpander owner)
    {
        this.owner = owner;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.OutlineButton;
        Height = 40; Width = 300; Margin = new Padding(0, 2, 0, 2);
    }
    private void FitHeight() => Height = Math.Max(40 * DeviceDpi / 96, TextRenderer.MeasureText("Ag", Font).Height + 12 * DeviceDpi / 96);
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); FitHeight(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitHeight(); Invalidate(); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); owner.Toggle(); if (CanFocus) Focus(); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { owner.Toggle(); e.Handled = true; e.SuppressKeyPress = true; }
        base.OnKeyDown(e);
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovering = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovering = false; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; float scale = DeviceDpi / 96f;
        bool highContrast = SystemInformation.HighContrast;
        g.Clear(highContrast ? SystemColors.Control : BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (hovering && !highContrast)
        {
            using var path = BrandTheme.Round(new RectangleF(0, 0, Width - 1, Height - 1), BrandTheme.CornerRadius * scale);
            using var hover = new SolidBrush(BrandTheme.HoverFill); g.FillPath(hover, path);
        }
        Color ink = highContrast ? SystemColors.ControlText : BrandTheme.Ink;
        Color mark = highContrast ? SystemColors.ControlText : BrandTheme.Copper;
        float size = 7 * scale, cx = 10 * scale, cy = Height / 2f;
        PointF[] arrow = owner.Expanded
            ? [new(cx - size / 2, cy - size / 3), new(cx + size / 2, cy - size / 3), new(cx, cy + size / 2)]
            : [new(cx - size / 3, cy - size / 2), new(cx + size / 2, cy), new(cx - size / 3, cy + size / 2)];
        using (var brush = new SolidBrush(mark)) g.FillPolygon(brush, arrow);
        var textArea = new Rectangle((int)(22 * scale), 0, Math.Max(1, Width - (int)(22 * scale)), Height);
        TextRenderer.DrawText(g, Text, Font, textArea, ink, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues) { var focus = ClientRectangle; focus.Inflate(-2, -2); ControlPaint.DrawFocusRectangle(g, focus, ink, highContrast ? SystemColors.Control : BackColor); }
    }
}

/// <summary>Scrollfläche mit Regel gegen den Fokussprung.</summary>
public sealed class GardenScrollPanel : Panel
{
    public GardenScrollPanel() { AutoScroll = true; }
    protected override Point ScrollToControl(Control activeControl) => BrandTheme.KeepsScrollPosition(this, activeControl) ? AutoScrollPosition : base.ScrollToControl(activeControl);
}

/// <summary>Scrollender senkrechter Fluss mit derselben Regel gegen den Fokussprung.</summary>
public sealed class GardenScrollFlow : FlowLayoutPanel
{
    public GardenScrollFlow() { AutoScroll = true; FlowDirection = FlowDirection.TopDown; WrapContents = false; }
    protected override Point ScrollToControl(Control activeControl) => BrandTheme.KeepsScrollPosition(this, activeControl) ? AutoScrollPosition : base.ScrollToControl(activeControl);
}

/// <summary>Anhang an eine Auswahlliste, ohne ihren Typ zu ändern: Bei geschlossener Liste geht das Mausrad an die Seite.</summary>
internal sealed class GardenWheelRelay : NativeWindow
{
    private readonly ComboBox box;
    private GardenWheelRelay(ComboBox box)
    {
        this.box = box;
        box.HandleCreated += (_, _) => { if (Handle == IntPtr.Zero) AssignHandle(box.Handle); };
        box.HandleDestroyed += (_, _) => ReleaseHandle();
        if (box.IsHandleCreated) AssignHandle(box.Handle);
    }
    public static void Attach(ComboBox box) => _ = new GardenWheelRelay(box);
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == BrandTheme.WM_MOUSEWHEEL && !box.DroppedDown)
        {
            if (BrandTheme.ScrollHost(box) is { IsHandleCreated: true } host) BrandTheme.SendMessage(host.Handle, m.Msg, m.WParam, m.LParam);
            m.Result = IntPtr.Zero; return;
        }
        base.WndProc(ref m);
    }
}
