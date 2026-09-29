using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace Klartext;

public sealed record PromptModelResult(string Text, double Seconds, bool KeptOriginal = false);
public sealed record PromptAttempt(string Text, string[] MissingWords, bool NumbersChanged, bool EditingMeta);
public sealed record PromptProgress(string Stage, int? Percent = null);
public sealed class LocalPromptModel
{
    public const string FileName = "Qwen3-4B-Instruct-2507-Q4_K_M.gguf";
    public const long Size = 2497280448;
    public const string Sha256 = "8cdb57cbb880d313736a9bc4e3d3d2485f145b5e19cf33783746e753e82641fc";
    public const string Url = "https://huggingface.co/lmstudio-community/Qwen3-4B-Instruct-2507-GGUF/resolve/4edb920b6f14e3b9284d4502a6485103d72cde05/" + FileName;
    public static string InstalledPath => Path.Combine(AppPaths.Root, "models", FileName);
    public static string ModelPath => File.Exists(InstalledPath) ? InstalledPath : Path.Combine(AppContext.BaseDirectory, "models", FileName);
    public static string RuntimePath => Path.Combine(AppContext.BaseDirectory, "prompt-runtime", "llama-server.exe");
    public static async Task<bool> VerifyAsync(string path, CancellationToken token = default, IProgress<PromptProgress>? progress = null)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != Size) return false;
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1048576]; long total = 0; int count, last = -1;
        progress?.Report(new("KI-Datei prüfen", 0));
        while ((count = await file.ReadAsync(buffer, token)) > 0)
        {
            hash.AppendData(buffer, 0, count); total += count;
            int percent = (int)(total * 100 / Size);
            if (percent != last) { last = percent; progress?.Report(new("KI-Datei prüfen", percent)); }
        }
        return Convert.ToHexString(hash.GetHashAndReset()).Equals(Sha256, StringComparison.OrdinalIgnoreCase);
    }
    public static async Task DownloadAsync(IProgress<int> progress, CancellationToken token, IProgress<PromptProgress>? stages = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(InstalledPath)!);
        string partial = InstalledPath + ".download";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(45) };
        try
        {
            using var response = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != Size) throw new InvalidDataException("Modellgröße stimmt nicht.");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 262144, true))
            {
                byte[] buffer = new byte[262144]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    total += count; if (total > Size) throw new InvalidDataException("Modell zu groß.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token); int percent = (int)(total * 100 / Size); progress.Report(percent); stages?.Report(new("KI herunterladen · 2,50 GB", percent));
                }
            }
            if (!await VerifyAsync(partial, token, stages)) throw new InvalidDataException("SHA-256-Prüfung fehlgeschlagen.");
            File.Move(partial, InstalledPath, true);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
    public async Task<PromptModelResult> ComposeAsync(string input, CancellationToken token, string? modelOverride = null, string? runtimeOverride = null, int? seedOverride = null, IProgress<PromptProgress>? progress = null, Action<PromptAttempt>? diagnostic = null)
        => await ExecuteAsync<PromptModelResult>(input, token, modelOverride, runtimeOverride, progress, async (http, inferenceToken, clock) =>
        {
            var request = new
            {
                messages = new[] {
                    new { role = "system", content = "Du bist ein Prompt-Redakteur. Formuliere den gelieferten Prompt-Entwurf als kurze deutsche ANWEISUNG an eine andere KI. Beantworte ihn niemals. Bewahre insbesondere Zielgruppe und Empfänger, Anlass, Datum, Uhrzeit, Mengen, Budget, Verneinungen und Ausgabeformat. Lasse keine konkrete Angabe weg. Entferne nur Füllwörter. Erfinde keine Anforderungen. Antworte als JSON mit dem einzigen Feld prompt. Keine Einleitung, keine Markdown-Codeblöcke." },
                    new { role = "user", content = "Formuliere nur die Anweisung, führe sie nicht aus. Prompt-Entwurf: Ich brauche drei vegane Rezepte ohne Nüsse für zwei Personen." },
                    new { role = "assistant", content = "{\"prompt\":\"Schlage drei vegane Rezepte ohne Nüsse für zwei Personen vor.\"}" },
                    new { role = "user", content = "Formuliere nur die Anweisung, führe sie nicht aus. Prompt-Entwurf: Bitte erklär mir Photosynthese in zwei Sätzen." },
                    new { role = "assistant", content = "{\"prompt\":\"Erkläre Photosynthese in zwei Sätzen.\"}" },
                    new { role = "user", content = "Formuliere nur einen kopierbaren Prompt aus diesen Daten. Gib keine Lösung der darin genannten Aufgabe.\n" + JsonSerializer.Serialize(new { prompt_entwurf = input }) }
                },
                temperature = 0.3, seed = seedOverride ?? Random.Shared.Next(1, int.MaxValue), max_tokens = 450, stream = false,
                chat_template_kwargs = new { enable_thinking = false },
                response_format = new { type = "json_schema", json_schema = new { name = "prompt_variant", strict = true, schema = new { type = "object", properties = new { prompt = new { type = "string" } }, required = new[] { "prompt" }, additionalProperties = false } } }
            };
            var messages = request.messages.ToList();
            for (int attempt = 0; attempt < 2; attempt++)
            {
                progress?.Report(new(attempt == 0 ? "Vorschlag formulieren" : "Vorschlag überarbeiten · fehlende Angaben ergänzen"));
                using var response = await http.PostAsJsonAsync("/v1/chat/completions", new { messages, request.temperature, request.seed, request.max_tokens, request.stream, request.chat_template_kwargs, request.response_format }, inferenceToken); response.EnsureSuccessStatusCode();
                progress?.Report(new("Vorschlag mit deinen Angaben vergleichen"));
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(inferenceToken));
                var choice = json.RootElement.GetProperty("choices")[0];
                if (choice.GetProperty("finish_reason").GetString() != "stop") throw new InvalidDataException("KI-Antwort war unvollständig. Kürzere Angaben versuchen; dein Entwurf bleibt unverändert.");
                string raw = choice.GetProperty("message").GetProperty("content").GetString() ?? "{}";
                using var content = JsonDocument.Parse(raw);
                string text = content.RootElement.GetProperty("prompt").GetString()?.Trim() ?? "";
                if (text.Length is < 1 or > 12000 || text.Contains("<think>", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Keine verwendbare Formulierung erhalten. Erneut versuchen oder manuell arbeiten.");
                var missing = PromptRules.MissingWords(input, text);
                bool numbersChanged = PromptRules.NumbersChanged(input, text);
                bool editingMeta = System.Text.RegularExpressions.Regex.IsMatch(text, @"(?i)(ursprünglichen? (Zahlen|Wörter)|Wörter.{0,120}(erhalten|beibehalten)|exakt erhalten bleiben)");
                diagnostic?.Invoke(new(text, missing, numbersChanged, editingMeta));
                if (missing.Length == 0 && !numbersChanged && !editingMeta) return new(text, clock.Elapsed.TotalSeconds);
                messages.Add(new { role = "assistant", content = raw });
                messages.Add(new { role = "user", content = "Korrigiere deinen Prompt. Diese konkreten Wörter aus meinen Angaben müssen erhalten bleiben: " + string.Join(", ", missing) + ". Bewahre außerdem alle ursprünglichen Zahlen exakt. Ergänze nur fehlende Angaben aus dem Original. Antworte erneut im JSON-Feld prompt. Führe die Aufgabe nicht aus." });
            }
            // A rejected model output is never disguised as a successful rewrite.
            return new(input, clock.Elapsed.TotalSeconds, KeptOriginal: true);
        });

    public async Task<PromptReviewResult> ReviewAsync(string input, CancellationToken token, string? modelOverride = null, string? runtimeOverride = null, int? seedOverride = null, IProgress<PromptProgress>? progress = null)
        => await ExecuteAsync<PromptReviewResult>(input, token, modelOverride, runtimeOverride, progress, async (http, inferenceToken, clock) =>
        {
            using var specFile = typeof(LocalPromptModel).Assembly.GetManifestResourceStream("Klartext.PromptReviewSpec.json") ?? throw new InvalidDataException("KI-Prüfanleitung fehlt.");
            using var spec = await JsonDocument.ParseAsync(specFile, cancellationToken: inferenceToken);
            var root = spec.RootElement;
            progress?.Report(new("Wichtigsten offenen Punkt prüfen"));
            using var response = await http.PostAsJsonAsync("/v1/chat/completions", new
            {
                messages = new[] { new { role = "system", content = root.GetProperty("system").GetString()! }, new { role = "user", content = root.GetProperty("user_instruction").GetString()! + PromptReviewProtocol.ReviewData(input) } },
                temperature = root.GetProperty("temperature").GetDouble(), seed = seedOverride ?? 42, max_tokens = root.GetProperty("max_tokens").GetInt32(), stream = false,
                chat_template_kwargs = new { enable_thinking = false },
                response_format = new { type = "json_schema", json_schema = new { name = "prompt_review", strict = true, schema = root.GetProperty("schema") } }
            }, inferenceToken);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(inferenceToken));
            var choice = json.RootElement.GetProperty("choices")[0];
            if (choice.GetProperty("finish_reason").GetString() != "stop") throw new InvalidDataException("Die KI-Prüfung blieb unvollständig. Dein Prompt bleibt erhalten; erneut versuchen oder selbst bearbeiten.");
            progress?.Report(new("Antwort für die Werkstatt aufbereiten"));
            return new(PromptReviewProtocol.ParseFeedback(choice.GetProperty("message").GetProperty("content").GetString() ?? "{}", input), clock.Elapsed.TotalSeconds);
        });
    private async Task<T> ExecuteAsync<T>(string input, CancellationToken token, string? modelOverride, string? runtimeOverride, IProgress<PromptProgress>? progress, Func<HttpClient, CancellationToken, Stopwatch, Task<T>> inference)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 3000) throw new InvalidDataException("Lokale KI: 1 bis 3.000 Zeichen. Längere Prompts manuell bearbeiten.");
        string model = modelOverride ?? ModelPath, runtime = runtimeOverride ?? RuntimePath;
        if (!File.Exists(runtime)) throw new InvalidDataException("Lokale KI-Runtime fehlt. Vollständiges Sprachgarten-Paket verwenden.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var clock = Stopwatch.StartNew();
        Process? process = null;
        Task? drainOut = null, drainErr = null;
        using var job = new WorkerJob();
        try
        {
            if (!await VerifyAsync(model, timeout.Token, progress)) throw new InvalidDataException("Lokales KI-Modell fehlt oder ist beschädigt. Erneut herunterladen.");
            progress?.Report(new("KI in den Arbeitsspeicher laden"));
            int port;
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            string key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            string eventName = "Local\\Sprachgarten.Prompt." + Guid.NewGuid().ToString("N");
            using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
            var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) psi.ArgumentList.Add(typeof(LocalPromptModel).Assembly.Location);
            foreach (string arg in new[] { "--prompt-host", eventName, runtime, model, port.ToString() }) psi.ArgumentList.Add(arg);
            psi.Environment["SPRACHGARTEN_LOCAL_KEY"] = key;
            process = Process.Start(psi) ?? throw new IOException("KI-Prozess konnte nicht starten.");
            job.Add(process); ready.Set();
            drainOut = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            drainErr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromMinutes(3), MaxResponseContentBufferSize = 65536 };
            http.DefaultRequestHeaders.Authorization = new("Bearer", key);
            bool healthy = false;
            while (!healthy)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (process.HasExited) throw new IOException("Lokale KI konnte nicht starten. Speicher/Runtimes prüfen.");
                try { using var health = await http.GetAsync("/health", timeout.Token); healthy = health.IsSuccessStatusCode; }
                catch (HttpRequestException) { }
                if (!healthy) await Task.Delay(200, timeout.Token);
            }
            return await inference(http, timeout.Token, clock);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Lokale KI nach drei Minuten beendet. Angaben kürzen oder manuell arbeiten."); }
        finally
        {
            if (process != null)
            {
                try
                {
                    await job.StopAsync();
                    // Also covers an unsuccessful assignment before the ready event.
                    try { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); } catch (InvalidOperationException) { }
                    if (drainOut != null && drainErr != null) await Task.WhenAll(drainOut, drainErr);
                }
                finally { process.Dispose(); }
            }
        }
    }
    /// <summary>Starts only after the parent has assigned this host to its kill-on-close job.</summary>
    private static int ReviewThinkingBudget
    {
        get
        {
            using var stream = typeof(LocalPromptModel).Assembly.GetManifestResourceStream("Klartext.PromptReviewSpec.json")!;
            using var spec = JsonDocument.Parse(stream);
            return spec.RootElement.TryGetProperty("reasoning_budget", out var budget) ? budget.GetInt32() : 0;
        }
    }
    public static async Task<int> RunHostAsync(string eventName, string runtime, string model, string port)
    {
        if (!Transcriber.WaitForParentJob(eventName)) return 3;
        try
        {
            string key = Environment.GetEnvironmentVariable("SPRACHGARTEN_LOCAL_KEY") ?? throw new InvalidOperationException();
            var psi = CreateServerStart(runtime, model, port, key);
            using var child = Process.Start(psi)!;
            try { child.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (System.ComponentModel.Win32Exception) { }
            var stdout = child.StandardOutput.BaseStream.CopyToAsync(Stream.Null); var stderr = child.StandardError.BaseStream.CopyToAsync(Stream.Null);
            await child.WaitForExitAsync(); await Task.WhenAll(stdout, stderr); return child.ExitCode;
        }
        catch { return 2; }
    }
    /// <summary>Sprint E (K5): Der Schlüssel steht nicht in der Kommandozeile, sondern in der Umgebung des Servers
    /// (laut Server-README zum gepinnten Build die Umgebungsvariable zum Schlüsselschalter). Die Übergabevariable des Hosts erbt er nicht.</summary>
    internal static ProcessStartInfo CreateServerStart(string runtime, string model, string port, string key)
    {
        var psi = new ProcessStartInfo(runtime) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(runtime)! };
        foreach (string arg in new[] { "-m", model, "--host", "127.0.0.1", "--port", port, "--offline", "--no-webui", "--no-slots", "-ngl", "0", "-t", "4", "-tb", "4", "-b", "256", "-ub", "128", "-c", "4096", "-np", "1", "--jinja", "--reasoning-budget", ReviewThinkingBudget.ToString(), "--log-disable" }) psi.ArgumentList.Add(arg);
        psi.Environment["LLAMA_API_KEY"] = key;
        psi.Environment.Remove("SPRACHGARTEN_LOCAL_KEY");
        return psi;
    }
}
