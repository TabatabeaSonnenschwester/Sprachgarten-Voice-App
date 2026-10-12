namespace Klartext;

/// <summary>Grouped native selection controls; existing pages and their state stay alive.</summary>
public sealed class GardenNavigation : Panel
{
    private readonly TabControl pages;
    private readonly List<(Label Heading, RadioButton[] Items)> groups = [];
    private readonly List<RadioButton> items = [];
    private readonly GardenImage microphone = new(BrandTheme.Microphone) { AccessibleName = "Blumenmikrofon", TabStop = false };
    private readonly GardenImage wordmark = new(BrandTheme.Wordmark) { AccessibleName = "Sprachgarten", TabStop = false };
    private bool arranging, syncing;
    public IReadOnlyList<RadioButton> Items => items;

    public GardenNavigation(TabControl pages)
    {
        this.pages = pages; AccessibleName = "Bereiche"; Dock = DockStyle.Fill;
        Margin = Padding.Empty; BackColor = BrandTheme.Cream;
        Controls.AddRange([microphone, wordmark]);
        foreach (var (heading, names) in new[] {
            ("SCHREIBEN", new[] { "Diktat", "Audiodateien" }), ("HÖREN", new[] { "Vorlesen" }),
            ("GESTALTEN", new[] { "Promptwerkstatt" }), ("WERKZEUGE", new[] { "Sprachlabor", "Ersetzungen", "Einstellungen" }) })
        {
            var label = new Label { Text = heading, AutoSize = false, Font = BrandTheme.Small, TabStop = false };
            Controls.Add(label);
            var buttons = names.Select(name =>
            {
                var page = pages.TabPages.Cast<TabPage>().Single(p => p.Text == name);
                var item = new NavigationChoice { Text = name, AccessibleName = name, Tag = page, TabIndex = items.Count };
                item.CheckedChanged += (_, _) => { if (!syncing && item.Checked) pages.SelectedTab = page; };
                item.KeyDown += (_, e) =>
                {
                    if (e.KeyCode is Keys.Home or Keys.End)
                    {
                        var target = e.KeyCode == Keys.Home ? items[0] : items[^1];
                        target.Checked = true; target.Focus(); e.Handled = true; e.SuppressKeyPress = true;
                    }
                };
                Controls.Add(item); items.Add(item); return (RadioButton)item;
            }).ToArray();
            groups.Add((label, buttons));
        }
        pages.SelectedIndexChanged += SelectionChanged;
        SyncSelection();
    }
    private void SelectionChanged(object? sender, EventArgs e) => SyncSelection();
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is (Keys.Control | Keys.Tab) or (Keys.Control | Keys.Shift | Keys.Tab))
        {
            int current = items.FindIndex(item => item.Checked);
            int next = (current + (keyData.HasFlag(Keys.Shift) ? items.Count - 1 : 1)) % items.Count;
            items[next].Checked = true; items[next].Focus();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private void SyncSelection()
    {
        syncing = true;
        try { foreach (var item in items) item.Checked = ReferenceEquals(item.Tag, pages.SelectedTab); }
        finally { syncing = false; }
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (arranging || groups.Count == 0) return;
        arranging = true;
        try
        {
            float scale = DeviceDpi / 96f; int S(int n) => (int)Math.Round(n * scale);
            // Reserve scrollbar width even while it is hidden to avoid horizontal overflow.
            int width = Math.Max(S(160), ClientSize.Width - SystemInformation.VerticalScrollBarWidth - S(16));
            int offset = AutoScrollPosition.Y;
            microphone.SetBounds(S(80), S(6) + offset, S(46), S(66));
            wordmark.SetBounds(S(12), S(74) + offset, width, S(35));
            int y = S(118);
            foreach (var (heading, buttons) in groups)
            {
                heading.SetBounds(S(16), y + offset, width - S(8), S(22)); y += S(24);
                foreach (var item in buttons)
                {
                    item.SetBounds(S(8), y + offset, width, S(34)); item.Padding = new Padding(S(12), 0, 0, 0); y += S(35);
                }
                y += S(6);
            }
            AutoScrollMinSize = new Size(0, y);
            AutoScroll = y > ClientSize.Height;
        }
        finally { arranging = false; }
    }
    protected override void Dispose(bool disposing) { if (disposing) pages.SelectedIndexChanged -= SelectionChanged; base.Dispose(disposing); }

    private sealed class NavigationChoice : RadioButton
    {
        public NavigationChoice()
        {
            Appearance = Appearance.Button; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            TextAlign = ContentAlignment.MiddleLeft; Font = BrandTheme.Body; UseVisualStyleBackColor = false;
            CheckedChanged += (_, _) => Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            bool high = SystemInformation.HighContrast;
            var fill = Checked ? (high ? SystemColors.Highlight : BrandTheme.Mustard) : (high ? SystemColors.Control : BrandTheme.Cream);
            var ink = high ? (Checked ? SystemColors.HighlightText : SystemColors.ControlText) : BrandTheme.Ink;
            e.Graphics.Clear(fill);
            if (Checked) { using var marker = new SolidBrush(high ? SystemColors.HighlightText : BrandTheme.Petrol); e.Graphics.FillRectangle(marker, 0, 0, Math.Max(3, DeviceDpi / 24), Height); }
            var text = new Rectangle(Padding.Left, 0, Width - Padding.Left, Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, text, ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), ink, fill);
        }
    }
}
