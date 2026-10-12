# Local Moonshine streaming ASR worker for Sprachgarten.
# Local ASR only. No microphone access, transcript logs, downloads or training.
import base64, json, socket, sys, time, wave
import numpy as np
def no_network(*a,**k): raise OSError('Local inference only')
socket.socket.connect=no_network;socket.socket.connect_ex=no_network;socket.create_connection=no_network
# Parent assigns the Windows kill-on-close job before native model initialization.
if json.loads(sys.stdin.readline()).get('type')!='initialize':sys.exit(3)
from moonshine_voice import Transcriber,ModelArch
engine=Transcriber(sys.argv[1],ModelArch.SMALL_STREAMING,
    options={'ort_providers':'cpu','log_output_text':'false','log_api_calls':'false'})
def send(**item):print(json.dumps(item,ensure_ascii=True),flush=True)
send(type='ready')
stream=None;converter=None;stream_errors=[]

class Converter:
    def __init__(self,rate,channels,bits,encoding):
        if not 16000<=rate<=192000 or channels not in (1,2) or (bits,encoding) not in ((16,'pcm'),(32,'pcm'),(32,'float')):
            raise ValueError('Unsupported capture format')
        self.rate=rate;self.channels=channels;self.bits=bits;self.encoding=encoding
        offsets=np.arange(-96,97,dtype=np.float64);cutoff=min(7400,rate*.45)/rate
        self.kernel=2*cutoff*np.sinc(2*cutoff*offsets)*np.kaiser(193,8);self.kernel/=self.kernel.sum()
        self.history=np.zeros(192);self.count=0;self.position=0.;self.previous=0.;self.step=rate/16000
    def samples(self,data):
        kind='<f4' if self.encoding=='float' else ('<i2' if self.bits==16 else '<i4')
        x=np.frombuffer(data,dtype=kind).reshape(-1,self.channels).astype(np.float64)
        if self.encoding=='pcm':x/=32768 if self.bits==16 else 2147483648
        if not np.isfinite(x).all():raise ValueError('Nonfinite capture data')
        return self.filter(x.mean(axis=1))
    def filter(self,x):
        if not len(x):return []
        joined=np.concatenate((self.history,x));filtered=np.convolve(joined,self.kernel,mode='valid')
        self.history=joined[-192:];end=self.count+len(x)-1
        n=max(0,int(np.floor((end-self.position)/self.step))+1)
        positions=self.position+np.arange(n)*self.step
        result=np.interp(positions,np.arange(self.count-1,end+1),np.concatenate(([self.previous],filtered)))
        self.position+=n*self.step;self.count+=len(x);self.previous=float(filtered[-1])
        return np.clip(result,-1,1).astype(np.float32).tolist()
    def finish(self):return self.filter(np.zeros(96))

def reset():
    global stream,converter,stream_errors
    if stream is not None:stream.close()
    stream=None;converter=None;stream_errors=[]
def errors(event):
    if type(event).__name__=='Error':stream_errors.append(True)
try:
    for line in sys.stdin:
        request=json.loads(line);kind=request.get('type')
        try:
            if kind=='quit':break
            if kind=='start':
                reset();stream=engine.create_stream(update_interval=.5);stream.add_listener(errors);stream.start();send(type='ack')
            elif kind=='audio':
                if stream is None:raise ValueError('Stream not started')
                if converter is None:converter=Converter(request['rate'],request['channels'],request['bits'],request['encoding'])
                floats=converter.samples(base64.b64decode(request['data'],validate=True))
                if floats:stream.add_audio(floats,16000)
                if stream_errors:raise RuntimeError('Native streaming failure')
                send(type='ack')
            elif kind=='stop':
                if stream is None:raise ValueError('Stream not started')
                begin=time.perf_counter()
                if converter is not None:stream.add_audio(converter.finish(),16000)
                result=stream.stop()
                if result is None or stream_errors:raise RuntimeError('Native streaming failure')
                text=' '.join(x.text.strip() for x in result.lines if x.text.strip())
                send(type='result',text=text,seconds=time.perf_counter()-begin);reset()
            elif kind=='abort':reset();send(type='ack')
            elif kind=='file':
                reset()
                with wave.open(request['path'],'rb') as w:
                    if (w.getframerate(),w.getnchannels(),w.getsampwidth())!=(16000,1,2):raise ValueError('Expected PCM16 mono16k')
                    data=np.frombuffer(w.readframes(w.getnframes()),dtype='<i2').astype(np.float32)/32768
                begin=time.perf_counter();result=engine.transcribe_without_streaming(data.tolist(),16000)
                send(type='result',text=' '.join(x.text.strip() for x in result.lines if x.text.strip()),seconds=time.perf_counter()-begin)
            else:raise ValueError('Unknown command')
        except Exception as error:
            reset();send(type='error',failure=type(error).__name__)
finally:reset();engine.close()
