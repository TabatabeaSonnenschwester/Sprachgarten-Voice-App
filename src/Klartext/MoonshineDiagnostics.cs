// Explicit installation diagnostics use synthetic fixtures only, no microphone or clipboard.
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using NAudio.Wave;
namespace Klartext;
internal static class MoonshineDiagnostics
{
    internal static bool WindowCheckMode {get;private set;}
    private static readonly List<object> checks=[];
    private static void Check(string name,bool pass,double? seconds=null){checks.Add(new{name,pass,seconds});if(!pass)throw new InvalidDataException("Check failed: "+name);}
    private static bool ShortMatches(string text)=>text.ToLowerInvariant().Replace("9","neun").Trim().TrimEnd('.').Replace(",","")=="morgen treffen wir uns um neun uhr im garten";
    private static async Task Replay(MoonshineService service,byte[] data,WaveFormat format,CancellationToken cancelSignal)
    {
        int chunk=format.AverageBytesPerSecond/10;var clock=Stopwatch.StartNew();
        for(int offset=0;offset<data.Length;offset+=chunk)
        {
            int size=Math.Min(chunk,data.Length-offset);double arrival=(offset+size)/(double)format.AverageBytesPerSecond;
            var delay=TimeSpan.FromSeconds(arrival-clock.Elapsed.TotalSeconds);
            if(delay>TimeSpan.Zero)await Task.Delay(delay,cancelSignal);
            service.Feed(data.AsSpan(offset,size).ToArray(),format);
        }
    }
    public static async Task<int> RunAsync(string fixtures,string output)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));var cancelSignal=timeout.Token;
        string report=Path.GetFullPath(output);Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        try
        {
            Check("default-profile-compatible",Path.GetFileName(AppPaths.Root)=="Klartext");
            Check("default-hotkey-compatible",new UserSettings().EffectiveHotkey==new HotkeySpec(3,32));
            using(var startup=new MoonshineService())
            {
                var preparation=startup.PrepareAsync(cancelSignal);startup.Dispose();bool canceled=false;
                try{await preparation;}catch(Exception e)when(e is OperationCanceledException or ObjectDisposedException or IOException){canceled=true;}
                Check("dispose-during-preparation-stops-startup",canceled&&startup.WorkerProcessId==null);
            }
            var fixture=Path.Combine(fixtures,"short.wav");using var wav=new WaveFileReader(fixture);
            byte[] pcm=new byte[(int)wav.Length];wav.ReadExactly(pcm,0,pcm.Length);
            using var service=new MoonshineService();await service.BeginAsync(cancelSignal);
            bool duplicateRejected=false;try{await service.BeginAsync(cancelSignal);}catch(InvalidOperationException){duplicateRejected=true;}
            Check("duplicate-start-rejected",duplicateRejected);
            await Replay(service,pcm,new WaveFormat(16000,16,1),cancelSignal);var clock=Stopwatch.StartNew();var result=await service.FinishAsync(cancelSignal);
            Check("live-16k-recognized",ShortMatches(result.Text),clock.Elapsed.TotalSeconds);
            Check("live-stream-cleared",!service.HasActiveStream);
            byte[] floats=new byte[pcm.Length/2*3*2*4];
            for(int n=0;n<pcm.Length/2*3;n++)
            {
                int source=n/3;float sample=BitConverter.ToInt16(pcm,source*2)/32768f;
                BitConverter.GetBytes(sample).CopyTo(floats,n*8);BitConverter.GetBytes(sample).CopyTo(floats,n*8+4);
            }
            await service.BeginAsync(cancelSignal);await Replay(service,floats,WaveFormat.CreateIeeeFloatWaveFormat(48000,2),cancelSignal);
            result=await service.FinishAsync(cancelSignal);Check("live-48k-stereo-float-recognized",ShortMatches(result.Text));
            await service.BeginAsync(cancelSignal);await Replay(service,new byte[32000],new WaveFormat(16000,16,1),cancelSignal);
            result=await service.FinishAsync(cancelSignal);Check("silence-empty",string.IsNullOrWhiteSpace(result.Text));
            await service.BeginAsync(cancelSignal);await service.AbortAsync();Check("abort-clears-stream",!service.HasActiveStream);
            result=await service.TranscribeAsync("",fixture,cancelSignal);Check("retry-after-abort",ShortMatches(result.Text));
            await service.BeginAsync(cancelSignal);using(var canceled=new CancellationTokenSource())
            {
                canceled.Cancel();bool stopped=false;try{await service.FinishAsync(canceled.Token);}catch(OperationCanceledException){stopped=true;}
                Check("cancel-finish-observed",stopped);Check("cancel-clears-stream",!service.HasActiveStream);
            }
            result=await service.TranscribeAsync("",fixture,cancelSignal);Check("retry-after-cancellation",ShortMatches(result.Text));
            using(var canceledFile=new CancellationTokenSource())
            {
                var fileTask=service.TranscribeAsync("",Path.Combine(fixtures,"long.wav"),canceledFile.Token);
                await Task.Delay(100,cancelSignal);canceledFile.Cancel();bool stopped=false;
                try{await fileTask;}catch(OperationCanceledException){stopped=true;}
                Check("cancel-file-stops-worker",stopped&&service.WorkerProcessId==null);
            }
            result=await service.TranscribeAsync("",fixture,cancelSignal);Check("retry-after-file-cancellation",ShortMatches(result.Text));
            await service.BeginAsync(cancelSignal);int pid=service.WorkerProcessId!.Value;using var child=Process.GetProcessById(pid);
            service.Dispose();Check("owned-worker-stopped-on-dispose",child.WaitForExit(5000));
            File.WriteAllText(report,JsonSerializer.Serialize(new{status="PASS",checks,microphoneUsed=false,clipboardUsed=false}));return 0;
        }
        catch(Exception e){File.WriteAllText(report,JsonSerializer.Serialize(new{status="FAIL",failure=e.GetType().Name,checks,microphoneUsed=false,clipboardUsed=false}));return 2;}
    }
    private static T Field<T>(MainForm form,string name)=>(T)typeof(MainForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
    public static int WindowCheck(string output)
    {
        WindowCheckMode=true;
        ApplicationConfiguration.Initialize();
        AppPaths.Root=Path.Combine(Path.GetTempPath(),"Sprachgarten-Moonshine-windowcheck-"+Guid.NewGuid().ToString("N"));AppPaths.Initialize();
        using var form=new MainForm(false){WindowState=FormWindowState.Minimized,ShowInTaskbar=false};
        using var poll=new System.Windows.Forms.Timer{Interval=200};var clock=Stopwatch.StartNew();int exit=2;
        poll.Tick+=(_,_)=>
        {
            if(!Field<bool>(form,"modelReady")&&clock.Elapsed.TotalSeconds<95)return;
            poll.Stop();
            try
            {
                bool ready=Field<bool>(form,"modelReady");var recorder=Field<AudioRecorder>(form,"audio");var key=Field<HotkeyWindow>(form,"hotkey");
                var result=new{status=ready&&!recorder.IsRecording?"PASS":"FAIL",title=form.Text,modelReady=ready,modelLabel=Field<Label>(form,"modelLabel").Text,
                    startEnabled=Field<GardenButton>(form,"toggle").Enabled,hotkeyRegistered=key.Registered?.ToString(),
                    microphoneCount=Field<ComboBox>(form,"microphones").Items.Count,microphoneStarted=recorder.IsRecording,
                    readingHotkeyRegistered=Field<HotkeyWindow>(form,"readingHotkey").Registered!=null,profile=AppPaths.Root};
                File.WriteAllText(output,JsonSerializer.Serialize(result));exit=result.status=="PASS"?0:2;
                // A minimized native window has no drawable client size. Readiness is checked above without activating it.
            }
            catch(Exception e){exit=2;File.WriteAllText(output,JsonSerializer.Serialize(new{status="FAIL",failure=e.GetType().Name}));}
            finally{form.Dispose();Application.ExitThread();}
        };
        poll.Start();Application.Run(form);return exit;
    }
}
