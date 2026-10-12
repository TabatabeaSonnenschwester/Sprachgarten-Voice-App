namespace Klartext;

/// <summary>First-use instructions inside the empty dictation surface; no separate model state.</summary>
public sealed class GardenOnboarding : Panel
{
    private readonly FlowLayoutPanel content = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly Label heading = new() { Text = "Erste Schritte", Font = BrandTheme.Large, AutoSize = true, TextAlign = ContentAlignment.MiddleCenter };
    public Label Hint { get; } = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleCenter, Font = BrandTheme.Body };
    public GardenButton Action { get; } = new() { Text = "Sprachmodell laden", Primary = true, AutoSize = true, Font = BrandTheme.Body };
    public ProgressBar Progress { get; } = new() { Height = 12, AccessibleName = "Fortschritt der Modelleinrichtung" };
    public GardenButton Hotkey { get; } = new() { Text = "Optional: Tastenkürzel ändern", AutoSize = true, Font = BrandTheme.Small };
    private bool arranging;
    public GardenOnboarding()
    {
        Dock = DockStyle.Fill; AutoScroll = true; AccessibleName = "Erste Schritte";
        content.Controls.AddRange([heading, Hint, Action, Progress, Hotkey]); Controls.Add(content);
        foreach (Control child in content.Controls) child.Margin = new Padding(0, 4, 0, 8);
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (arranging || content == null) return;
        arranging = true;
        try
        {
            int s = DeviceDpi; int S(int v) => v * s / 96;
            int width = Math.Max(S(240), Math.Min(S(460), ClientSize.Width - SystemInformation.VerticalScrollBarWidth - S(24)));
            content.MinimumSize = new Size(width, 0); content.MaximumSize = new Size(width, 0);
            foreach (Control child in content.Controls)
            {
                child.Width = width; child.MinimumSize = new Size(width, child is GardenButton ? S(38) : 0);
                child.MaximumSize = new Size(width, 0);
            }
            content.Location = new Point(Math.Max(S(6), (ClientSize.Width - width) / 2), Math.Max(S(4), (ClientSize.Height - content.PreferredSize.Height) / 2) + AutoScrollPosition.Y);
        }
        finally { arranging = false; }
    }
}
