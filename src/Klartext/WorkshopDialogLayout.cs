namespace Klartext;

internal static class WorkshopDialogLayout
{
    public static void Attach(FlowLayoutPanel flow)
    {
        bool busy = false;
        void Resize()
        {
            if (busy) return; busy = true;
            try
            {
                int width = Math.Max(230, flow.ClientSize.Width - flow.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 12);
                foreach (Control child in flow.Controls)
                {
                    if (child is Label or CheckBox or FlowLayoutPanel) child.MaximumSize = new(width, 0);
                    if (child is TextBox) child.Width = width;
                    if (child is FlowLayoutPanel row)
                        foreach (Control nested in row.Controls)
                            if (nested is TextBox) nested.Width = Math.Min(550, width);
                }
            }
            finally { busy = false; }
        }
        flow.ClientSizeChanged += (_, _) => Resize(); Resize();
    }
}
