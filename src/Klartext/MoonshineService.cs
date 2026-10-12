// Local Moonshine streaming ASR. Child lifetime and cancellation belong to this app.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using NAudio.Wave;

namespace Klartext;

public sealed class MoonshineService : IDisposable
{
    private sealed record ModelFile(string Name,string Sha256);
    private sealed record Config(string Python,string Model,ModelFile[] Files);
    private Process? process;
    private WorkerJob? job;
    private Channel<JsonElement>? responses;
    private Task? reader;
    private Task? stderr;
    private Channel<object>? audio;
    private Task? pump;
    private CancellationTokenSource? streamLifetime;
    private readonly SemaphoreSlim serial=new(1,1);
    private readonly CancellationTokenSource shutdown=new();
    private readonly object lifecycle=new();
    private volatile bool disposed;
    public bool HasActiveStream { get; private set; }
    internal int? WorkerProcessId => process is {HasExited:false} ? process.Id : null;
    private static Config LoadConfig()
    {
        var config=JsonSerializer.Deserialize<Config>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"moonshine-config.json"))) ?? throw new InvalidDataException("Moonshine-Konfiguration fehlt.");
        string Local(string path)=>Path.IsPathRooted(path)?Path.GetFullPath(path):Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,path));
        return config with{Python=Local(config.Python),Model=Local(config.Model)};
    }
    public async Task PrepareAsync(CancellationToken cancelSignal)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancelSignal,shutdown.Token);
        cancelSignal=linked.Token;
        if(process is { HasExited:false })return;
        DisposeWorker();
        var config=LoadConfig();
        if(!File.Exists(config.Python))throw new FileNotFoundException("Die lokale Moonshine-Laufzeit fehlt. Bitte die vollständige Sprachgarten-Ausgabe erneut entpacken.");
        foreach(var f in config.Files)
        {
            if(Path.GetFileName(f.Name)!=f.Name)throw new InvalidDataException("Ungültiger Modellpfad.");
            using var file=File.OpenRead(Path.Combine(config.Model,f.Name));
            var actual=Convert.ToHexString(await SHA256.HashDataAsync(file,cancelSignal));
            if(!actual.Equals(f.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Moonshine-Modellprüfung fehlgeschlagen.");
        }
        cancelSignal.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed,this);
        var info=new ProcessStartInfo(config.Python){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,
            RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8,WorkingDirectory=AppContext.BaseDirectory};
        info.ArgumentList.Add("-u");info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"Speech","moonshine_worker.py"));info.ArgumentList.Add(config.Model);
        info.Environment["PYTHONHOME"]=Path.GetDirectoryName(config.Python)!;info.Environment.Remove("PYTHONPATH");
        info.Environment["PYTHONNOUSERSITE"]="1";
        info.Environment["PYTHONIOENCODING"]="utf-8";info.Environment["PYTHONUTF8"]="1";info.Environment["PYTHONDONTWRITEBYTECODE"]="1";
        info.Environment["OMP_NUM_THREADS"]="4";info.Environment["MKL_NUM_THREADS"]="4";info.Environment["OPENBLAS_NUM_THREADS"]="4";
        try
        {
            lock(lifecycle)
            {
            ObjectDisposedException.ThrowIf(disposed,this);cancelSignal.ThrowIfCancellationRequested();
            process=new Process{StartInfo=info};if(!process.Start())throw new IOException("Moonshine konnte nicht starten.");
            job=new WorkerJob();job.Add(process);
            responses=Channel.CreateUnbounded<JsonElement>();
            var child=process;var target=responses;
            reader=Task.Run(async()=>
            {
                try
                {
                    while(await child.StandardOutput.ReadLineAsync() is {} line)
                    {
                        try{using var doc=JsonDocument.Parse(line);await target.Writer.WriteAsync(doc.RootElement.Clone());}
                        catch(JsonException){/* Never persist native output. */}
                    }
                    target.Writer.TryComplete(new IOException("Moonshine-Hilfsprozess wurde beendet."));
                }
                catch{target.Writer.TryComplete(new IOException("Moonshine-Verbindung wurde unterbrochen."));}
            });
            stderr=child.StandardError.BaseStream.CopyToAsync(Stream.Null);
            }
            await WriteAsync(new{type="initialize"},cancelSignal);
            var ready=await ResponseAsync(cancelSignal,TimeSpan.FromSeconds(90));
            if(ready.GetProperty("type").GetString()!="ready")throw new IOException("Moonshine meldete keine Bereitschaft.");
        }
        catch{DisposeWorker();throw;}
    }
    public async Task BeginAsync(CancellationToken cancelSignal)
    {
        await PrepareAsync(cancelSignal);
        if(HasActiveStream)throw new InvalidOperationException("Moonshine-Aufnahme läuft bereits.");
        await RequestAsync(new{type="start"},cancelSignal);
        streamLifetime=new();
        audio=Channel.CreateBounded<object>(new BoundedChannelOptions(128){SingleReader=true,SingleWriter=true,FullMode=BoundedChannelFullMode.Wait});
        var queue=audio;var lifetime=streamLifetime;
        pump=Task.Run(async()=>{await foreach(var packet in queue.Reader.ReadAllAsync(lifetime.Token))await RequestAsync(packet,lifetime.Token);});
        HasActiveStream=true;
    }
    public void Feed(byte[] buffer,WaveFormat format)
    {
        if(!HasActiveStream||audio==null)return;
        bool isFloat=format.Encoding==WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible ex && ex.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71");
        var packet=new{type="audio",rate=format.SampleRate,channels=format.Channels,bits=format.BitsPerSample,encoding=isFloat?"float":"pcm",data=Convert.ToBase64String(buffer)};
        if(!audio.Writer.TryWrite(packet))audio.Writer.TryComplete(new IOException("Moonshine konnte mit dem Audio nicht Schritt halten. Aufnahme bleibt für Wiederholung erhalten."));
    }
    public async Task<TranscriptionResult> FinishAsync(CancellationToken cancelSignal)
    {
        if(!HasActiveStream)throw new InvalidOperationException("Kein Moonshine-Stream vorhanden.");
        audio!.Writer.TryComplete();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancelSignal,timeout.Token);
        try
        {
            await pump!.WaitAsync(linked.Token);
            var result=await RequestAsync(new{type="stop"},linked.Token);
            return Result(result);
        }
        catch{DisposeWorker();throw;}
        finally{HasActiveStream=false;streamLifetime?.Dispose();streamLifetime=null;audio=null;pump=null;}
    }
    public async Task AbortAsync()
    {
        if(!HasActiveStream)return;
        streamLifetime?.Cancel();audio?.Writer.TryComplete();DisposeWorker();
        try{if(pump!=null)await pump;}catch{}
        HasActiveStream=false;streamLifetime?.Dispose();streamLifetime=null;audio=null;pump=null;
    }
    public async Task<TranscriptionResult> TranscribeAsync(string unused,string pcm,CancellationToken cancelSignal)
    {
        if(HasActiveStream)throw new InvalidOperationException("Eine laufende Aufnahme kann nicht durch eine Datei ersetzt werden.");
        try{await PrepareAsync(cancelSignal);return Result(await RequestAsync(new{type="file",path=pcm},cancelSignal,TimeSpan.FromMinutes(5)));}
        catch{DisposeWorker();throw;} // Cancellation must discard the old response before any retry.
    }
    private static TranscriptionResult Result(JsonElement item)
    {if(item.GetProperty("type").GetString()!="result")throw new IOException("Moonshine lieferte kein endgültiges Ergebnis.");return new(item.GetProperty("text").GetString()??"",item.GetProperty("seconds").GetDouble());}
    private async Task<JsonElement> RequestAsync(object value,CancellationToken cancelSignal,TimeSpan? limit=null)
    {
        await serial.WaitAsync(cancelSignal);
        try{await WriteAsync(value,cancelSignal);return await ResponseAsync(cancelSignal,limit??TimeSpan.FromSeconds(60));}
        finally{serial.Release();}
    }
    private async Task WriteAsync(object value,CancellationToken cancelSignal)
    {if(process==null||process.HasExited)throw new IOException("Moonshine ist nicht bereit.");await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(),cancelSignal);await process.StandardInput.FlushAsync(cancelSignal);}
    private async Task<JsonElement> ResponseAsync(CancellationToken cancelSignal,TimeSpan limit)
    {
        var result=await responses!.Reader.ReadAsync(cancelSignal).AsTask().WaitAsync(limit,cancelSignal);
        if(result.TryGetProperty("type",out var type)&&type.GetString()=="error")throw new IOException("Moonshine-Erkennung fehlgeschlagen. Aufnahme bleibt erhalten. Fehlertyp: "+result.GetProperty("failure").GetString());
        return result;
    }
    private void DisposeWorker()
    {
        lock(lifecycle)
        {
        job?.Dispose();job=null;
        if(process!=null){try{if(!process.HasExited)process.Kill(true);}catch{}process.Dispose();process=null;}
        }
    }
    public void Dispose(){lock(lifecycle){if(disposed)return;disposed=true;shutdown.Cancel();streamLifetime?.Cancel();audio?.Writer.TryComplete();DisposeWorker();HasActiveStream=false;}}
}
