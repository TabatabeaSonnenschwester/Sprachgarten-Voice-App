using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;

namespace Klartext;

public static class SelectedText
{
    // Isolated helper: a hung accessibility provider must never hang the app UI.
    public static int Run() => WriteSelection(() => AutomationElement.FocusedElement);
    // The provider is injectable for isolated tests against a known synthetic control.
    public static int WriteSelection(Func<AutomationElement?> focusedElement)
    {
        try
        {
            var element = focusedElement();
            if (element == null || element.Current.IsPassword || !element.TryGetCurrentPattern(TextPattern.Pattern, out var raw)) return 1;
            var pattern = (TextPattern)raw;
            if (pattern.SupportedTextSelection == SupportedTextSelection.None) return 1;
            string text = string.Join("\n", pattern.GetSelection().Select(r => r.GetText(4001)));
            if (!LocalSpeech.ValidText(text)) return 1;
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.Write(JsonSerializer.Serialize(text)); return 0;
        }
        catch { return 1; }
    }
    public static async Task<string?> ReadAsync()
    {
        using var job = new WorkerJob();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var process = new Process { StartInfo = new ProcessStartInfo(Environment.ProcessPath!) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, ArgumentList = { "--read-selection" }
        }};
        try
        {
            process.Start(); job.Add(process);
            string output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0 || output.Length > 30000) return null;
            string? text = JsonSerializer.Deserialize<string>(output);
            return text != null && LocalSpeech.ValidText(text) ? text : null;
        }
        catch { try { if (!process.HasExited) process.Kill(true); } catch { } return null; }
    }
}
