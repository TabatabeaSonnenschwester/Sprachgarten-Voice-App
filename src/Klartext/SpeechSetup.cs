using NAudio.Wave;

namespace Klartext;

internal static class SpeechSetup
{
    internal static (SpeechRuntime? Runtime, string? Error) Check()
    {
        try
        {
            var runtime = SpeechRuntime.Load();
            if (runtime == null || !File.Exists(Path.Combine(runtime.Models, "tokenizer.json")) ||
                !File.Exists(Path.Combine(AppContext.BaseDirectory, "Speech", "speech_worker.py")))
                return (null, "Das lokale Vorlesepaket fehlt oder ist unvollständig. Einrichtung: docs/READING.md im App-Ordner.");
            if (!File.Exists(SpeechRuntime.VoicePath))
                return (runtime, "Bitte zuerst „Stimmaufnahme auswählen …“ wählen. Einrichtung: docs/READING.md im App-Ordner.");
            using var wave = new WaveFileReader(SpeechRuntime.VoicePath);
            CheckFormat(wave);
            return (runtime, null);
        }
        catch { return (null, "Vorlesepaket oder Stimmaufnahme nicht lesbar. Bitte Einrichtung und WAV prüfen: docs/READING.md im App-Ordner."); }
    }
    internal static void Validate(SpeechRuntime runtime)
    {
        if (!File.Exists(runtime.Python) || !File.Exists(Path.Combine(runtime.Models, "model.safetensors")) ||
            !File.Exists(Path.Combine(runtime.Models, "tokenizer.json")))
            throw new IOException("Das lokale Vorlesepaket ist unvollständig. Bitte docs/READING.md im App-Ordner prüfen.");
        using (var wave = new WaveFileReader(SpeechRuntime.VoicePath)) CheckFormat(wave);
        using var audio = new AudioFileReader(SpeechRuntime.VoicePath);
        float[] samples = new float[4096]; int n; float peak = 0;
        while ((n = audio.Read(samples, 0, samples.Length)) > 0)
            for (int i = 0; i < n; i++)
            {
                if (!float.IsFinite(samples[i])) throw new IOException("Die Stimmaufnahme enthält ungültige Audiodaten.");
                peak = Math.Max(peak, Math.Abs(samples[i]));
            }
        if (peak < .01f) throw new IOException("Die Stimmaufnahme ist zu leise oder still. Bitte eine klare WAV-Aufnahme auswählen.");
    }
    private static void CheckFormat(WaveFileReader wave)
    {
        if (wave.Length > 12_000_000 || wave.TotalTime.TotalSeconds is < 2 or > 30 || wave.WaveFormat.Channels is < 1 or > 2 ||
            !((wave.WaveFormat.Encoding == WaveFormatEncoding.Pcm && wave.WaveFormat.BitsPerSample == 16) ||
              (wave.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat && wave.WaveFormat.BitsPerSample == 32)))
            throw new IOException("Bitte eine WAV-Stimmaufnahme mit 2–30 Sekunden auswählen (16-Bit PCM oder 32-Bit Float).");
    }
}
