"""Stage an explicitly supplied Python 3.12 runtime and pinned Moonshine model.

No user profile, voice or development-lab paths are read. Model download happens
only with --download-model; subsequent dictation works offline.
"""
import argparse
import hashlib
import json
import shutil
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MODEL_URL = 'https://download.moonshine.ai/model/small-streaming-de/quantized_26_08_24'
REQUIRED = {'moonshine_voice': '0.1.5', 'numpy': '2.5.4'}


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def excluded(directory, names):
    return {name for name in names if name in ('__pycache__', 'test', 'tests', 'site-packages')
            or Path(name).suffix.lower() in ('.wav', '.mp3', '.flac', '.m4a', '.ogg', '.pyc')}


def stage(args):
    target = Path(args.output).resolve()
    if target == ROOT / 'dist' or not target.is_relative_to((ROOT / 'dist').resolve()):
        raise ValueError('Output must be an application directory below this repository\'s dist folder.')
    if not (target / 'Sprachgarten.exe').is_file():
        raise ValueError('Build with scripts/build.ps1 -Publish first.')
    base = Path(args.python_root).resolve()
    site = Path(args.site_packages).resolve()
    for name in ('python.exe', 'python3.dll', 'python312.dll', 'vcruntime140.dll', 'LICENSE.txt', 'Lib', 'DLLs'):
        if not (base / name).exists():
            raise ValueError('A complete Windows x64 Python 3.12 installation is required: ' + name)
    # This folder must be a dedicated dependency target, never a general profile.
    for name, version in REQUIRED.items():
        if not (site / (name + '-' + version + '.dist-info') / 'METADATA').is_file():
            raise ValueError('Missing pinned dependency: ' + name + '==' + version)
    config = json.loads((ROOT / 'src/Klartext/moonshine-config.json').read_text(encoding='utf-8-sig'))
    source = Path(args.model_source).resolve() if args.model_source else ROOT / '.tools/moonshine-model'
    if args.download_model:
        source.mkdir(parents=True, exist_ok=True)
    for entry in config['Files']:
        name = entry['Name']
        if Path(name).name != name:
            raise ValueError('Invalid model filename')
        path = source / name
        if not path.is_file() and args.download_model:
            temporary = source / (name + '.partial')
            with urllib.request.urlopen(MODEL_URL + '/' + name, timeout=120) as response, temporary.open('wb') as out:
                shutil.copyfileobj(response, out)
            if sha(temporary) != entry['Sha256'].lower():
                raise ValueError('Downloaded model checksum mismatch: ' + name)
            temporary.replace(path)
        if not path.is_file() or sha(path) != entry['Sha256'].lower():
            raise ValueError('Missing or invalid model file: ' + name)
    runtime = target / 'Moonshine/python'
    model = target / 'models/moonshine-de-small-streaming'
    if runtime.exists() or model.exists():
        raise ValueError('Use a fresh publish directory; existing model/runtime will not be overwritten.')
    runtime.mkdir(parents=True)
    for name in ('python.exe', 'python3.dll', 'python312.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'LICENSE.txt'):
        if (base / name).is_file():
            shutil.copy2(base / name, runtime / name)
    for name in ('Lib', 'DLLs'):
        shutil.copytree(base / name, runtime / name, ignore=excluded)
    # Copy packages from the dedicated pip --target directory, excluding audio.
    shutil.copytree(site, runtime / 'Lib/site-packages', ignore=excluded)
    model.mkdir(parents=True)
    for entry in config['Files']:
        shutil.copy2(source / entry['Name'], model / entry['Name'])
    shutil.copy2(ROOT / 'src/Klartext/moonshine-config.json', target / 'moonshine-config.json')
    licenses = target / 'licenses'
    licenses.mkdir(exist_ok=True)
    shutil.copy2(base / 'LICENSE.txt', licenses / 'Python-3.12-LICENSE.txt')
    shutil.copy2(site / 'moonshine_voice-0.1.5.dist-info/licenses/LICENSE', licenses / 'Moonshine-Voice-MIT.txt')
    (licenses / 'moonshine-provenance.json').write_text(json.dumps({
        'modelSource': MODEL_URL, 'modelFiles': config['Files'], 'dependencies': REQUIRED,
        'python': '3.12 (supplied installation; locally tested with 3.12.14)',
        'personalVoiceIncluded': False
    }, indent=2), encoding='utf-8')
    print(json.dumps({'status': 'PASS', 'modelFiles': len(config['Files']),
                      'modelBytes': sum((model / x['Name']).stat().st_size for x in config['Files'])}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output')
    parser.add_argument('--python-root', required=True, help='Base Windows Python 3.12 directory')
    parser.add_argument('--site-packages', required=True, help='Dedicated pip --target directory')
    parser.add_argument('--model-source', help='Existing directory containing the eight pinned model files')
    parser.add_argument('--download-model', action='store_true', help='Explicitly allow first model download')
    stage(parser.parse_args())
