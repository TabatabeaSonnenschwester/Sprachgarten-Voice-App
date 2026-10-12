using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Klartext;

public record Microphone(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed class AudioRecorder : IDisposable
{
    private WasapiCapture? capture;
    private MMDevice? device;
    private WaveFileWriter? writer;
    private TaskCompletionSource<Exception?>? stopped;
    private readonly object sync = new();
    public string? FilePath { get; private set; }
    public float Peak { get; private set; }
    public long BytesRecorded { get; private set; }
    public bool IsRecording => capture != null;
    public event Action<Exception?>? UnexpectedStop;
    public event Action<byte[],WaveFormat>? AudioChunk;
    private volatile bool requestedStop;
    public static IReadOnlyList<Microphone> Devices()
    {
        using var e = new MMDeviceEnumerator();
        var result = new List<Microphone>();
        foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        { using (d) result.Add(new(d.ID, d.FriendlyName)); }
        return result;
    }
    public void Start(string? deviceId, string path)
    {
        if (capture != null) throw new InvalidOperationException("Aufnahme läuft bereits.");
        using var e = new MMDeviceEnumerator();
        requestedStop = false; BytesRecorded = 0; Peak = 0; FilePath = path;
        stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            device = deviceId == null ? e.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia) : e.GetDevice(deviceId);
            capture = new WasapiCapture(device);
            writer = new WaveFileWriter(path, capture.WaveFormat);
            capture.DataAvailable += (_, args) =>
            {
                try
                {
                    lock (sync)
                    {
                        writer?.Write(args.Buffer, 0, args.BytesRecorded);
                        writer?.Flush();
                        BytesRecorded += args.BytesRecorded;
                        float peak = 0;
                        if (capture?.WaveFormat.BitsPerSample == 32)
                            for (int i = 0; i + 3 < args.BytesRecorded; i += 4) peak = Math.Max(peak, Math.Abs(BitConverter.ToSingle(args.Buffer, i)));
                        Peak = Math.Clamp(peak, 0, 1);
                        if (capture != null && args.BytesRecorded > 0) AudioChunk?.Invoke(args.Buffer.AsSpan(0,args.BytesRecorded).ToArray(),capture.WaveFormat);
                    }
                }
                catch (Exception ex) { stopped.TrySetResult(ex); UnexpectedStop?.Invoke(ex); }
            };
            capture.RecordingStopped += (_, args) =>
            {
                stopped.TrySetResult(args.Exception);
                if (!requestedStop) UnexpectedStop?.Invoke(args.Exception ?? new IOException("Mikrofon wurde getrennt."));
            };
            capture.StartRecording();
        }
        catch { Dispose(); throw; }
    }
    public async Task StopAsync()
    {
        if (capture == null) return;
        requestedStop = true;
        var source = capture;
        try
        {
            await Task.Run(source.StopRecording);
            var error = await stopped!.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (error != null) throw new IOException("Die Aufnahme wurde unterbrochen. Bereits aufgenommenes Audio bleibt erhalten.", error);
        }
        finally { Dispose(); }
    }
    public void Dispose()
    {
        requestedStop = true;
        var source = capture; capture = null;
        try { source?.Dispose(); }
        finally
        {
            lock (sync) { writer?.Dispose(); writer = null; }
            device?.Dispose(); device = null; Peak = 0;
        }
    }
    public static AudioStats ConvertToPcm(string input, string output, CancellationToken token = default)
    {
        using var source = new AudioFileReader(input);
        if (source.TotalTime.TotalSeconds > 301) throw new InvalidDataException("Aufnahme überschreitet fünf Minuten.");
        using var resampler = new MediaFoundationResampler(source, new WaveFormat(16000, 16, 1)) { ResamplerQuality = 60 };
        using (var writer = new WaveFileWriter(output, resampler.WaveFormat))
        {
            byte[] buffer = new byte[8192]; long bytes = 0; int count;
            while ((count = resampler.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested(); bytes += count;
                if (bytes > 301L * 16000 * 2) throw new InvalidDataException("Aufnahme überschreitet fünf Minuten.");
                writer.Write(buffer, 0, count);
            }
        }
        token.ThrowIfCancellationRequested();
        return Analyze(output);
    }
    public static AudioStats Analyze(string pcmPath)
    {
        using var reader = new WaveFileReader(pcmPath);
        if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1 || reader.WaveFormat.BitsPerSample != 16)
            throw new InvalidDataException("Erwartet wird PCM16 mono mit 16 kHz.");
        byte[] buffer = new byte[640]; // 20 ms frames
        double sum = 0, peak = 0, active = 0; long samples = 0;
        int count;
        while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            double frame = 0;
            for (int i = 0; i + 1 < count; i += 2)
            {
                double value = BitConverter.ToInt16(buffer, i) / 32768d;
                frame += value * value; peak = Math.Max(peak, Math.Abs(value));
            }
            int n = count / 2; sum += frame; samples += n;
            if (n > 0 && Math.Sqrt(frame / n) >= .005) active += n / 16000d;
        }
        return new(samples / 16000d, samples == 0 ? 0 : Math.Sqrt(sum / samples), peak, active);
    }
}
