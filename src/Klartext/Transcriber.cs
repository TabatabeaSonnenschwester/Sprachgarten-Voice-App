using System.Diagnostics;
using System.Text.Json;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace Klartext;

// FailureType carries the worker's exception type name only: no message, no path, no transcript.
public record TranscriptionResult(string Text, double Seconds, string? FailureType = null);

public sealed class Transcriber
{
    public async Task<TranscriptionResult> TranscribeAsync(string model, string pcm, CancellationToken token)
    {
        string resultPath = Path.Combine(AppPaths.Audio, Guid.NewGuid().ToString("N") + ".result");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var info = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(Transcriber).Assembly.Location);
        string startEventName = "Local\\Klartext.Worker." + Guid.NewGuid().ToString("N");
        using var startEvent = new EventWaitHandle(false, EventResetMode.ManualReset, startEventName);
        foreach (string arg in new[] { "--worker", model, pcm, resultPath, startEventName }) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        using var job = new WorkerJob();
        bool started = false;
        try
        {
            if (!process.Start()) throw new IOException("Spracherkennung konnte nicht gestartet werden.");
            started = true;
            job.Add(process);
            startEvent.Set(); // Worker opens no audio/model before the kill-on-close job is assigned.
            // Consume native streams without keeping contents in logs or memory.
            Task drainOut = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            Task drainErr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            try { await process.WaitForExitAsync(linked.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(true);
                await process.WaitForExitAsync();
                if (timeout.IsCancellationRequested && !token.IsCancellationRequested)
                    throw new TimeoutException("Die Erkennung hat das Zeitlimit erreicht. Audio ist zum Wiederholen erhalten.");
                throw;
            }
            await Task.WhenAll(drainOut, drainErr);
            TranscriptionResult? outcome = null;
            if (File.Exists(resultPath))
            {
                string raw = await File.ReadAllTextAsync(resultPath, token);
                try { outcome = JsonSerializer.Deserialize<TranscriptionResult>(raw); }
                catch (JsonException) { outcome = null; }
            }
            if (process.ExitCode != 0 || outcome == null || outcome.FailureType != null)
                throw new IOException(FailureAdvice(process.ExitCode, outcome?.FailureType, outcome != null));
            return outcome;
        }
        finally
        {
            if (started && !process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            if (File.Exists(resultPath)) File.Delete(resultPath);
        }
    }
    // Pure mapping: exit code plus reported exception type become one German sentence. No file access.
    public static string FailureAdvice(int exitCode, string? failureType, bool resultFileRead)
    {
        string type = SafeFailureType(failureType);
        string named = type.Length == 0 ? " Gemeldeter Fehlertyp: keiner." : " Gemeldeter Fehlertyp: " + type + ".";
        if (exitCode == 3)
            return "Die lokale Spracherkennung wurde vorsorglich beendet, weil ihr Hilfsprozess nicht rechtzeitig abgesichert werden konnte. Bitte erneut versuchen. Audio bleibt zum Wiederholen erhalten." + named;
        if (type.Length == 0 && !resultFileRead)
            return "Die lokale Spracherkennung hat kein lesbares Ergebnis hinterlassen. Bitte erneut versuchen; hilft das nicht, Sprachgarten neu starten. Audio bleibt zum Wiederholen erhalten." + named;
        if (type is "DllNotFoundException" or "BadImageFormatException" or "EntryPointNotFoundException" or "TypeInitializationException" or "TypeLoadException")
            return "Die lokale Spracherkennung konnte ihre Windows-Bausteine nicht laden. Bitte die Microsoft Visual C++ 2022 x64-Laufzeit installieren und erneut versuchen. Audio bleibt zum Wiederholen erhalten." + named;
        if (type is "OutOfMemoryException" or "InsufficientMemoryException" or "InsufficientExecutionStackException")
            return "Der Arbeitsspeicher hat für die Erkennung nicht gereicht. Bitte andere Programme schließen und erneut versuchen. Audio bleibt zum Wiederholen erhalten." + named;
        if (type is "FileNotFoundException" or "DirectoryNotFoundException")
            return "Modell prüfen: Der Hilfsprozess hat eine benötigte Datei nicht gefunden. Bitte das Sprachmodell erneut einrichten. Audio bleibt zum Wiederholen erhalten." + named;
        if (type is "InvalidDataException" or "FormatException" or "EndOfStreamException" or "ArgumentException" or "ArgumentOutOfRangeException" or "JsonException")
            return "Die Arbeitskopie der Aufnahme ist unbrauchbar. Bitte die Aufnahme erneut transkribieren; hilft das nicht, neu aufnehmen. Audio bleibt zum Wiederholen erhalten." + named;
        if (type is "IOException" or "UnauthorizedAccessException" or "SecurityException")
            return "Eine benötigte Datei war für die Erkennung nicht lesbar, etwa weil ein anderes Programm sie gerade hält. Bitte in einem Moment erneut versuchen. Audio bleibt zum Wiederholen erhalten." + named;
        return "Die lokale Spracherkennung wurde unerwartet beendet. Bitte erneut versuchen; hilft das nicht, Sprachgarten neu starten. Audio bleibt zum Wiederholen erhalten." + named;
    }
    private static string SafeFailureType(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Length > 64) return "";
        foreach (char c in value)
        {
            if (char.IsAsciiLetterOrDigit(c)) continue;
            if (c == '.') continue;
            if (c == '_') continue;
            return "";
        }
        return value;
    }
    public static async Task<int> RunWorkerAsync(string model, string pcm, string result)
    {
        try
        {
            RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];
            var sw = Stopwatch.StartNew();
            using var factory = WhisperFactory.FromPath(model);
            await using var processor = factory.CreateBuilder().WithLanguage("de").WithNoContext()
                .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 1, 4)).WithNoSpeechThreshold(.6f).Build();
            await using var input = File.OpenRead(pcm);
            var parts = new List<string>();
            await foreach (var segment in processor.ProcessAsync(input))
                if (!string.IsNullOrWhiteSpace(segment.Text)) parts.Add(segment.Text.Trim());
            var output = new TranscriptionResult(string.Join(" ", parts), sw.Elapsed.TotalSeconds);
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(output));
            return 0;
        }
        catch (Exception ex)
        {
            // Only the exception type name travels upwards; no audio, transcript, path or native output.
            try
            {
                var failure = new TranscriptionResult("", 0, ex.GetType().Name);
                await File.WriteAllTextAsync(result, JsonSerializer.Serialize(failure));
            }
            catch { }
            return 2;
        }
    }
    public static bool WaitForParentJob(string name)
    {
        try { using var gate = EventWaitHandle.OpenExisting(name); return gate.WaitOne(TimeSpan.FromSeconds(5)); }
        catch { return false; }
    }
}
