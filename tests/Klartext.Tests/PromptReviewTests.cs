using System.Diagnostics;
using System.Text.Json;
using Klartext;

internal static class PromptReviewTests
{
    private sealed class Stages : IProgress<PromptProgress>
    {
        public readonly List<PromptProgress> Values = [];
        public void Report(PromptProgress value) => Values.Add(value);
    }
    public static async Task<int> RunAsync(string root)
    {
        string folder = Path.Combine(root, "artifacts", "product-prompt-review", DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(folder);
        string model = Path.Combine(root, ".tools", "prompt-model-test", LocalPromptModel.FileName);
        string runtime = Path.Combine(root, ".tools", "prompt-model-test", "llama-b10964", "llama-server.exe");
        var checks = new List<object>(); int failures = 0;
        void Check(string name, bool pass, object? evidence = null) { checks.Add(new { name, pass, evidence }); if (!pass) failures++; Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}"); }
        // Windows can briefly enumerate an already terminated process. A stale PID
        // is not a live worker; preserve the explicit exit evidence below.
        static HashSet<int> Helpers() { var processes = Process.GetProcessesByName("llama-server"); try { return processes.Where(p => !p.HasExited).Select(p => p.Id).ToHashSet(); } finally { foreach (var p in processes) p.Dispose(); } }
        var before = Helpers();
        try
        {
            const string input = "Fasse den folgenden Text in fünf Stichpunkten zusammen.";
            var stages = new Stages();
            var result = await new LocalPromptModel().ReviewAsync(input, CancellationToken.None, model, runtime, 42, stages);
            File.WriteAllText(Path.Combine(folder, "synthetic-review.json"), JsonSerializer.Serialize(result));
            Check("real-product-review-asks-for-missing-text", result.Review.Questions.Length == 1 && result.Review.Questions[0].Question.Contains("Text", StringComparison.OrdinalIgnoreCase) && result.Review.Suggestions.Length == 0, new { result.Seconds, result.Review });
            Check("real-review-empty-answer-keeps-source", result.Review.Apply(input, "", false) == input);
            Check("real-review-confirmed-answer-appended", result.Review.Apply(input, "Ich liefere den Text beim Verwenden mit.", false).StartsWith(input + "\r\n\r\n", StringComparison.Ordinal));
            Check("real-review-progress-hash-and-inference", stages.Values.Any(p => p.Percent == 100) && stages.Values.Any(p => p.Stage == "Wichtigsten offenen Punkt prüfen"));
            var remaining = Helpers().Except(before).Select(id =>
            {
                try { using var p = Process.GetProcessById(id); return new { Id = id, Exited = p.HasExited, Path = p.HasExited ? "" : p.MainModule?.FileName }; }
                catch (ArgumentException) { return new { Id = id, Exited = true, Path = (string?)"" }; }
            }).ToArray();
            Check("real-review-worker-stopped", remaining.All(p => p.Exited), remaining);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(6)); var clock = Stopwatch.StartNew();
            try { await new LocalPromptModel().ReviewAsync(input, cancel.Token, model, runtime, 42); Check("real-review-cancellation", false); }
            catch (OperationCanceledException) { Check("real-review-cancellation", clock.Elapsed.TotalSeconds < 12, clock.Elapsed.TotalSeconds); }
            Check("cancelled-review-worker-stopped", Helpers().Except(before).Count() == 0);
            string badModel = Path.Combine(folder, "damaged.gguf"); File.WriteAllText(badModel, "Not a model");
            try { await new LocalPromptModel().ReviewAsync(input, CancellationToken.None, badModel, runtime); Check("damaged-review-model-rejected", false); }
            catch (InvalidDataException) { Check("damaged-review-model-rejected", true); }
        }
        catch (Exception ex) { Check("runner", false, ex.GetType().Name + ": " + ex.Message); }
        File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(folder);
        return failures == 0 ? 0 : 1;
    }
}
