using System.Security.Cryptography;

namespace Klartext;

/// <summary>Keep the verified bytes protected against replacement/writes for this process lifetime.</summary>
internal sealed class VerifiedModelCache(long size, string sha256) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1);
    private FileStream? verified;
    private string? verifiedPath;
    internal int FullChecks { get; private set; }
    internal async Task<bool> VerifyAsync(string path, CancellationToken token)
    {
        string full = Path.GetFullPath(path);
        await gate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            // Probe on every use: unreadable files must still be reported as blocked.
            if (!File.Exists(full)) return false;
            var file = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
            try
            {
                if (verified != null && string.Equals(full, verifiedPath, StringComparison.OrdinalIgnoreCase)) return true;
                verified?.Dispose(); verified = null; verifiedPath = null;
                if (file.Length != size) return false;
                FullChecks++;
                bool valid = Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(sha256, StringComparison.OrdinalIgnoreCase);
                if (!valid) return false;
                verified = file; verifiedPath = full; file = null!;
                return true;
            }
            finally { file?.Dispose(); }
        }
        finally { gate.Release(); }
    }
    internal async Task ReleaseAsync(string path, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { if (string.Equals(Path.GetFullPath(path), verifiedPath, StringComparison.OrdinalIgnoreCase)) { verified?.Dispose(); verified = null; verifiedPath = null; } }
        finally { gate.Release(); }
    }
    public void Dispose() { verified?.Dispose(); verified = null; }
}
