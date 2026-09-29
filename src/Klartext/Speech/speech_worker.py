"""Local-only speech worker. JSON lines over inherited pipes, never a network service."""
import base64
import hashlib
import json
import os
from pathlib import Path
import socket
import sys
import threading

os.environ['HF_HUB_OFFLINE'] = '1'
os.environ['HF_HUB_DISABLE_TELEMETRY'] = '1'
os.environ['DO_NOT_TRACK'] = '1'
os.environ['PYTHONDONTWRITEBYTECODE'] = '1'
sys.stdin.reconfigure(encoding='utf-8')
sys.stdout.reconfigure(encoding='utf-8')
write_lock = threading.Lock()


def emit(kind, **values):
    with write_lock:
        print(json.dumps(dict(type=kind, **values)), flush=True)


def deny_network(*args, **kwargs):
    raise RuntimeError('Offline worker')


socket.socket.connect = deny_network
socket.socket.connect_ex = deny_network
socket.create_connection = deny_network


def main():
    # Wait until the parent has assigned its job object before importing/loading anything.
    init = json.loads(sys.stdin.readline(16384))
    import numpy as np
    import scipy.io.wavfile
    from scipy.signal import resample_poly
    from math import gcd
    import torch
    import yaml
    import pocket_tts
    from pocket_tts import TTSModel
    model_dir = Path(init['models'])
    weights = model_dir / 'model.safetensors'
    for path, digest in [(weights, '6b6a8004c17c7b4ddf6c58f309e9e58ba01daa35ad6b2e9761756ce62efbac77'),
                         (model_dir / 'tokenizer.json', '2d77849811e3d1b6ae80e5b11b203f57e1847901104d8a0573b34f46f8dddb53')]:
        with path.open('rb') as stream:
            if hashlib.file_digest(stream, 'sha256').hexdigest() != digest:
                raise ValueError('Model integrity')
    cfg = yaml.safe_load((Path(pocket_tts.__file__).parent / 'config/german.yaml').read_text(encoding='utf-8'))
    cfg['weights_path'] = str(weights)
    cfg.pop('weights_path_without_voice_cloning', None)
    cfg['flow_lm']['lookup_table']['tokenizer_path'] = str(model_dir / 'tokenizer.json')
    # Only non-private configuration is written; speech input/output remains in pipes/RAM.
    config_file = Path(init['config'])
    config_file.write_text(yaml.safe_dump(cfg), encoding='utf-8')
    torch.set_num_threads(2)
    torch.set_num_interop_threads(1)
    model = TTSModel.load_model(config=str(config_file), quantize=True)
    sr, audio = scipy.io.wavfile.read(init['voice'])
    if audio.dtype == np.int16:
        audio = audio.astype(np.float32) / 32768
    elif audio.dtype != np.float32:
        raise ValueError('Unsupported voice format')
    if audio.ndim == 2:
        audio = audio.mean(axis=1)
    if audio.ndim != 1 or not 2 <= len(audio) / sr <= 30 or not np.isfinite(audio).all():
        raise ValueError('Voice duration or samples')
    if np.max(np.abs(audio)) < .01:
        raise ValueError('Silent voice')
    divisor = gcd(sr, 24000)
    audio = resample_poly(audio, 24000 // divisor, sr // divisor).astype(np.float32)
    voice = model.get_state_for_audio_prompt(torch.from_numpy(audio).unsqueeze(0))
    emit('ready', sampleRate=24000)
    active = None
    stop = threading.Event()

    def speak(request):
        try:
            for chunk in model.generate_audio_stream(voice, request['text'], stop=stop):
                if stop.is_set():
                    break
                data = chunk.detach().cpu().numpy().astype('<f4').tobytes()
                emit('audio', id=request['id'], data=base64.b64encode(data).decode('ascii'))
            emit('done', id=request['id'], cancelled=stop.is_set())
        except Exception:
            emit('error', id=request['id'], code='synthesis')

    for line in sys.stdin:
        request = json.loads(line)
        if request.get('type') == 'cancel':
            stop.set()
        elif request.get('type') == 'speak':
            if active is not None and active.is_alive():
                active.join(timeout=1)
            text = request.get('text', '')
            if (active is not None and active.is_alive()) or not isinstance(text, str) or not text.strip() or len(text) > 4000:
                emit('error', id=request.get('id'), code='request')
                continue
            stop.clear()
            active = threading.Thread(target=speak, args=(request,), daemon=True)
            active.start()
    stop.set()
    if active is not None:
        active.join(timeout=2)


if __name__ == '__main__':
    try:
        main()
    except Exception:
        # No exception text: library errors may contain private text or paths.
        emit('error', code='setup')
        sys.exit(2)
