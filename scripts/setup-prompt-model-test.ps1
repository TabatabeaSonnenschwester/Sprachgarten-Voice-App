param([switch]$Comparison, [switch]$Modern, [switch]$Coach, [switch]$InstructCoach)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root '.tools\prompt-model-test'
New-Item -ItemType Directory -Force -Path $dest | Out-Null
$manifest = @{
  runtime = @{
    version = 'b10964'; stable = 'v0.4.1'
    url = 'https://github.com/ggml-org/llama.cpp/releases/download/b10964/llama-b10964-bin-win-cpu-x64.zip'
    file = 'llama-b10964-bin-win-cpu-x64.zip'; bytes = 18427629
    sha256 = '917f39c076402c421224824607397af20f53625a60defc20e8dd22446bf4c5d7'
  }
  model = @{
    repository = 'Qwen/Qwen3-1.7B-GGUF'; revision = '90862c4b9d2787eaed51d12237eafdfe7c5f6077'
    url = 'https://huggingface.co/Qwen/Qwen3-1.7B-GGUF/resolve/90862c4b9d2787eaed51d12237eafdfe7c5f6077/Qwen3-1.7B-Q8_0.gguf'
    file = 'Qwen3-1.7B-Q8_0.gguf'; bytes = 1834426016
    sha256 = '061b54daade076b5d3362dac252678d17da8c68f07560be70818cace6590cb1a'
  }
  comparison = @{
    repository = 'Qwen/Qwen2.5-1.5B-Instruct-GGUF'; revision = '91cad51170dc346986eccefdc2dd33a9da36ead9'
    url = 'https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/91cad51170dc346986eccefdc2dd33a9da36ead9/qwen2.5-1.5b-instruct-q4_k_m.gguf'
    file = 'qwen2.5-1.5b-instruct-q4_k_m.gguf'; bytes = 1117320736
    sha256 = '6a1a2eb6d15622bf3c96857206351ba97e1af16c30d7a74ee38970e434e9407e'
  }
  modern = @{
    repository = 'lmstudio-community/Qwen3.5-2B-GGUF'; revision = 'bb84e11355a036e28f080c7793fa6d22b7c4e344'
    original_model = 'Qwen/Qwen3.5-2B'; publisher = 'LM Studio team, GGUF conversion using llama.cpp b8185'
    url = 'https://huggingface.co/lmstudio-community/Qwen3.5-2B-GGUF/resolve/bb84e11355a036e28f080c7793fa6d22b7c4e344/Qwen3.5-2B-Q4_K_M.gguf'
    file = 'Qwen3.5-2B-Q4_K_M.gguf'; bytes = 1270808032
    sha256 = '0bfe35afc9f05b7fac3fa04925e051ac7939a42a8a17ea11afc99701bea826cc'
  }
  instructCoach = @{
    repository = 'lmstudio-community/Qwen3-4B-Instruct-2507-GGUF'; revision = '4edb920b6f14e3b9284d4502a6485103d72cde05'
    original_model = 'Qwen/Qwen3-4B-Instruct-2507'; original_revision = 'cdbee75f17c01a7cc42f958dc650907174af0554'
    url = 'https://huggingface.co/lmstudio-community/Qwen3-4B-Instruct-2507-GGUF/resolve/4edb920b6f14e3b9284d4502a6485103d72cde05/Qwen3-4B-Instruct-2507-Q4_K_M.gguf'
    file = 'Qwen3-4B-Instruct-2507-Q4_K_M.gguf'; bytes = 2497280448
    sha256 = '8cdb57cbb880d313736a9bc4e3d3d2485f145b5e19cf33783746e753e82641fc'
  }
  coach = @{
    repository = 'lmstudio-community/Qwen3.5-4B-GGUF'; revision = 'f9f88ac3e234be915e23811a6d28ea287bdb927e'
    original_model = 'Qwen/Qwen3.5-4B'; original_revision = '851bf6e806efd8d0a36b00ddf55e13ccb7b8cd0a'
    url = 'https://huggingface.co/lmstudio-community/Qwen3.5-4B-GGUF/resolve/f9f88ac3e234be915e23811a6d28ea287bdb927e/Qwen3.5-4B-Q4_K_M.gguf'
    file = 'Qwen3.5-4B-Q4_K_M.gguf'; bytes = 2707513696
    sha256 = '25082a7dd3776cc3c741c6347d3bd04523f05796607b3fbc32fa3a25dfa1418c'
  }
}
$items = @($manifest.runtime, $manifest.model)
if ($Comparison) { $items = @($manifest.runtime, $manifest.comparison) }
if ($Modern) { $items = @($manifest.runtime, $manifest.modern) }
if ($Coach) { $items = @($manifest.runtime, $manifest.coach) }
if ($InstructCoach) { $items = @($manifest.runtime, $manifest.instructCoach) }
foreach ($item in $items) {
  $target = Join-Path $dest $item.file
  if (!(Test-Path -LiteralPath $target)) {
    $partial = "$target.partial"
    Write-Output "Download: $($item.file) ($($item.bytes) Bytes)"
    & curl.exe --fail --location --retry 2 --connect-timeout 30 --max-time 1800 --speed-time 120 --speed-limit 1024 --silent --show-error --output $partial $item.url
    if ($LASTEXITCODE -ne 0) { throw "Download fehlgeschlagen: $LASTEXITCODE" }
    if ((Get-Item -LiteralPath $partial).Length -ne $item.bytes) { throw 'Falsche Dateigröße.' }
    if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.sha256) { throw 'SHA256-Prüfung fehlgeschlagen.' }
    Move-Item -LiteralPath $partial -Destination $target
  }
  if ((Get-Item -LiteralPath $target).Length -ne $item.bytes -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.sha256) { throw "Vorhandene Datei ungültig: $target" }
  Write-Output "Geprüft: $($item.file)"
}
$runtimeDir = Join-Path $dest 'llama-b10964'
if (!(Test-Path -LiteralPath $runtimeDir)) {
  Expand-Archive -LiteralPath (Join-Path $dest $manifest.runtime.file) -DestinationPath $runtimeDir
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $dest 'sources.json') -Encoding utf8
if ($Coach) {
  $licenseSources = @{
    'Qwen3.5-4B-LICENSE' = 'https://huggingface.co/Qwen/Qwen3.5-4B/raw/851bf6e806efd8d0a36b00ddf55e13ccb7b8cd0a/LICENSE'
    'Qwen3.5-4B-conversion-README.md' = 'https://huggingface.co/lmstudio-community/Qwen3.5-4B-GGUF/raw/f9f88ac3e234be915e23811a6d28ea287bdb927e/README.md'
    'llama-cpp-LICENSE' = 'https://raw.githubusercontent.com/ggml-org/llama.cpp/b10964/LICENSE'
  }
  foreach($file in $licenseSources.Keys) { Invoke-WebRequest $licenseSources[$file] -OutFile (Join-Path $dest $file) }
  $manifest.coach | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dest 'Qwen3.5-4B-provenance.json') -Encoding utf8
}
if ($InstructCoach) {
  $licenseSources = @{
    'Qwen3-4B-Instruct-LICENSE' = 'https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507/raw/cdbee75f17c01a7cc42f958dc650907174af0554/LICENSE'
    'Qwen3-4B-Instruct-conversion-README.md' = 'https://huggingface.co/lmstudio-community/Qwen3-4B-Instruct-2507-GGUF/raw/4edb920b6f14e3b9284d4502a6485103d72cde05/README.md'
    'llama-cpp-LICENSE' = 'https://raw.githubusercontent.com/ggml-org/llama.cpp/b10964/LICENSE'
  }
  foreach($file in $licenseSources.Keys) { Invoke-WebRequest $licenseSources[$file] -OutFile (Join-Path $dest $file) }
  $manifest.instructCoach | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dest 'Qwen3-4B-Instruct-provenance.json') -Encoding utf8
}
Write-Output "Setup fertig: $dest"

