using System.Text.Json;

namespace Klartext;

public enum DictationState { Ready, Recording, Stopping, Transcribing, Result, Error, Setup }

public sealed class SessionState
{
    public DictationState Current { get; private set; } = DictationState.Ready;
    public bool HasPendingAudio { get; set; }
    public bool HasUncopiedText { get; set; }
    public bool CanStart => !HasPendingAudio && !HasUncopiedText && Current is DictationState.Ready or DictationState.Result or DictationState.Error;
    public bool Busy => Current is DictationState.Recording or DictationState.Stopping or DictationState.Transcribing or DictationState.Setup;
    public void Move(DictationState next)
    {
        bool valid = next == DictationState.Error || (Current, next) switch
        {
            (_, DictationState.Ready) => Current != DictationState.Recording,
            (_, DictationState.Recording) => CanStart,
            (DictationState.Recording, DictationState.Stopping) => true,
            (DictationState.Stopping, DictationState.Transcribing) => true,
            (DictationState.Error, DictationState.Transcribing) => HasPendingAudio,
            (DictationState.Transcribing, DictationState.Result) => true,
            (_, DictationState.Setup) => !Busy,
            _ => false
        };
        if (!valid) throw new InvalidOperationException($"Ungültiger Zustandswechsel {Current} → {next}.");
        Current = next;
    }
}

public record HotkeySpec(uint Modifiers = 3, uint Key = 32)
{
    public bool IsValid => (Modifiers & ~7u) == 0 && (Modifiers & 3) != 0 &&
        (Key == 32 || Key is >= 65 and <= 90 || Key is >= 112 and <= 122);
    public override string ToString() => string.Join("+", new[] {
        (Modifiers & 2) != 0 ? "Strg" : null, (Modifiers & 1) != 0 ? "Alt" : null,
        (Modifiers & 4) != 0 ? "Umschalt" : null,
        Key == 32 ? "Leertaste" : Key >= 112 ? $"F{Key - 111}" : ((char)Key).ToString()
    }.Where(x => x != null));
}

public record UserSettings(string? DeviceId = null, HotkeySpec? Hotkey = null)
{
    public HotkeySpec EffectiveHotkey => Hotkey is { IsValid: true } ? Hotkey : new();
    public static UserSettings Load(string path)
    {
        try { return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this));
        File.Move(path + ".tmp", path, true);
    }
}

public static class AppPaths
{
    public static string Root { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Klartext");
    public static string Audio => Path.Combine(Root, "pending");
    public static string InstalledModel => Path.Combine(Root, "models", "ggml-small.bin");
    public static string Model => File.Exists(InstalledModel) ? InstalledModel :
        File.Exists(Path.Combine(AppContext.BaseDirectory, "models", "ggml-small.bin")) ? Path.Combine(AppContext.BaseDirectory, "models", "ggml-small.bin") : InstalledModel;
    public static string Settings => Path.Combine(Root, "settings.json");
    public static void Initialize() { Directory.CreateDirectory(Audio); Directory.CreateDirectory(Path.GetDirectoryName(InstalledModel)!); }
    public static void DeleteOwned(string? file)
    {
        if (file == null) return;
        var full = Path.GetFullPath(file);
        if (!full.StartsWith(Path.GetFullPath(Audio) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Datei außerhalb des temporären Aufnahmeordners.");
        if (File.Exists(full)) File.Delete(full);
    }
}

public record AudioStats(double Seconds, double Rms, double Peak, double ActiveSeconds)
{
    // At least 250 ms above -46 dBFS, minimum 0.6 s; conservative gate, not a speech classifier.
    public bool HasSignal => Seconds >= .6 && ActiveSeconds >= .25 && Peak >= .01;
}
