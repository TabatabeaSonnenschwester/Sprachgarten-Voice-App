using System.Security.Cryptography;

namespace Klartext;

public static class ModelStore
{
    public const long Size = 487601967;
    public const string Sha256 = "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b";
    private static readonly VerifiedModelCache cache = new(Size, Sha256);
    public static Task<bool> VerifyForUseAsync(string path, CancellationToken token = default) => cache.VerifyAsync(path, token);
    public const string Url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-small.bin";
    public static async Task<bool> VerifyAsync(string path, CancellationToken token = default)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != Size) return false;
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(Sha256, StringComparison.OrdinalIgnoreCase);
    }
    public static async Task DownloadAsync(string path, IProgress<int> progress, CancellationToken token, HttpClient? transport = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string partial = path + ".download";
        try
        {
            using var ownedClient = transport == null ? new HttpClient { Timeout = TimeSpan.FromMinutes(30) } : null;
            var client = transport ?? ownedClient!;
            using var response = await client.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != Size)
                throw new InvalidDataException("Der Modellserver lieferte eine unerwartete Dateigröße.");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
            {
                byte[] bytes = new byte[256 * 1024]; long total = 0; int n;
                while ((n = await input.ReadAsync(bytes, token)) > 0)
                {
                    total += n;
                    if (total > Size) throw new InvalidDataException("Die Modelldatei ist zu groß.");
                    await output.WriteAsync(bytes.AsMemory(0, n), token);
                    progress.Report((int)(total * 100 / Size));
                }
            }
            if (!await VerifyAsync(partial, token)) throw new InvalidDataException("Modellprüfung fehlgeschlagen (SHA-256). Bitte erneut laden.");
            await cache.ReleaseAsync(path, token);
            File.Move(partial, path, true);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
}
