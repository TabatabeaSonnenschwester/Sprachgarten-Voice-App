param([Parameter(Mandatory)][string]$AppDirectory, [string]$ProfileDirectory, [string]$ReferencePath)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$appRoot=[IO.Path]::GetFullPath($AppDirectory)
if (!(Test-Path -LiteralPath (Join-Path $appRoot 'Sprachgarten.exe'))) { throw 'App-Verzeichnis enthält keine Sprachgarten.exe.' }
$python=Join-Path $projectRoot '.tools\pocket-tts-env\Scripts\python.exe'
$models=Join-Path $projectRoot '.tools\tts-experiment\pocket-german'
if (!(Test-Path -LiteralPath $python)) { throw 'Die lokale Pocket-TTS-Umgebung fehlt.' }
if ((Get-FileHash -LiteralPath (Join-Path $models 'model.safetensors')).Hash -ne '6b6a8004c17c7b4ddf6c58f309e9e58ba01daa35ad6b2e9761756ce62efbac77') { throw 'Vorlesemodell-Prüfwert stimmt nicht.' }
@{Python=$python; Models=$models} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $appRoot 'speech-runtime.json') -Encoding utf8NoBOM
if ($ReferencePath) {
    if (!$ProfileDirectory) { throw 'Für eine persönliche Stimmvorlage ein ausdrückliches Profilverzeichnis angeben.' }
    $voiceRoot=Join-Path ([IO.Path]::GetFullPath($ProfileDirectory)) 'voice'
    $voice=Join-Path $voiceRoot 'reference.wav'
    if ((Test-Path -LiteralPath $voice) -and ((Get-FileHash -LiteralPath $voice).Hash -ne (Get-FileHash -LiteralPath $ReferencePath).Hash)) { throw 'Andere Stimmvorlage vorhanden. Zum bewussten Ersetzen die App verwenden.' }
    New-Item -ItemType Directory -Force -Path $voiceRoot | Out-Null
    if (!(Test-Path -LiteralPath $voice)) { Copy-Item -LiteralPath $ReferencePath -Destination $voice }
}
Write-Output 'Lokales Vorlesepaket verbunden. Keine persönliche Stimme in das App-Paket kopiert.'
