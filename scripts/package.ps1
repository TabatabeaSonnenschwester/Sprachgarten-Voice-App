param([Parameter(Mandatory)][string]$ModelPath, [switch]$WithPromptRuntime, [switch]$WithoutPromptModel)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
& (Join-Path $PSScriptRoot 'build.ps1') -Publish
$packageRoot=Join-Path $projectRoot 'dist\Sprachgarten-0.4.0-preview.14-win-x64'
$modelHash='1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b'
if ((Get-FileHash -LiteralPath $ModelPath -Algorithm SHA256).Hash -ne $modelHash) { throw 'Modell-SHA256 stimmt nicht' }
New-Item -ItemType Directory -Force -Path (Join-Path $packageRoot 'models') | Out-Null
Copy-Item -LiteralPath $ModelPath -Destination (Join-Path $packageRoot 'models\ggml-small.bin') -Force
if ($WithPromptRuntime) { & (Join-Path $PSScriptRoot 'stage-prompt-runtime.ps1') -OutputPath $packageRoot -WithoutModel:$WithoutPromptModel }
# Connect reading locally after extracting. Never distribute machine-specific paths or voices.
if (Test-Path -LiteralPath (Join-Path $packageRoot 'speech-runtime.json')) { throw 'Persönliche Vorlesekonfiguration im Paketordner: bitte einen frischen Paketordner verwenden.' }
Copy-Item -LiteralPath 'README.md' -Destination (Join-Path $packageRoot 'LIESMICH.md') -Force
New-Item -ItemType Directory -Force -Path (Join-Path $packageRoot 'licenses') | Out-Null
Get-ChildItem -LiteralPath 'licenses' -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $packageRoot ('licenses\'+$_.Name)) -Force }
Copy-Item -LiteralPath 'docs\TEST_REPORT.md' -Destination (Join-Path $packageRoot 'TESTBERICHT.md') -Force
$manifest=Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object FullName | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetRelativePath($packageRoot,$_.FullName).Replace('\','/')
}
$manifest | Set-Content -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.txt') -Encoding utf8
$archive=Join-Path $projectRoot 'dist\Sprachgarten-0.4.0-preview.14-win-x64.zip'
$archiveTemp=$archive+'.partial'
if(Test-Path -LiteralPath $archiveTemp) { throw 'Unfertiges Archiv existiert bereits; vor erneutem Packen prüfen.' }
# Stream entries using Zip64-capable .NET APIs, including the 2.50 GB model file.
[IO.Compression.ZipFile]::CreateFromDirectory($packageRoot,$archiveTemp,[IO.Compression.CompressionLevel]::Fastest,$true)
Move-Item -LiteralPath $archiveTemp -Destination $archive -Force
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List
