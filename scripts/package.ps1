param([Parameter(Mandatory)][string]$PythonRoot, [Parameter(Mandatory)][string]$SitePackages,
      [string]$ModelSource, [switch]$DownloadModel, [switch]$WithPromptRuntime, [switch]$WithoutPromptModel)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$packageRoot=Join-Path $projectRoot 'dist\Sprachgarten-0.4.0-preview.19-win-x64'
if (Test-Path -LiteralPath (Join-Path $packageRoot 'speech-runtime.json')) { throw 'Persönliche Vorlesekonfiguration im Paketordner: frischen Paketordner verwenden.' }
& (Join-Path $PSScriptRoot 'build.ps1') -Publish
$stageArgs=@((Join-Path $PSScriptRoot 'stage-moonshine.py'),$packageRoot,'--python-root',$PythonRoot,'--site-packages',$SitePackages)
if ($ModelSource) { $stageArgs+=@('--model-source',$ModelSource) }
if ($DownloadModel) { $stageArgs+='--download-model' }
& (Join-Path $PythonRoot 'python.exe') @stageArgs
if ($LASTEXITCODE -ne 0) { throw 'Moonshine-Paketvorbereitung fehlgeschlagen.' }
if ($WithPromptRuntime) { & (Join-Path $PSScriptRoot 'stage-prompt-runtime.ps1') -OutputPath $packageRoot -WithoutModel:$WithoutPromptModel }
# Connect reading locally after extracting. Never distribute machine-specific paths or voices.
if (Test-Path -LiteralPath (Join-Path $packageRoot 'speech-runtime.json')) { throw 'Persönliche Vorlesekonfiguration im Paketordner: bitte einen frischen Paketordner verwenden.' }
Copy-Item -LiteralPath 'README.md' -Destination (Join-Path $packageRoot 'LIESMICH.md') -Force
New-Item -ItemType Directory -Force -Path (Join-Path $packageRoot 'licenses') | Out-Null
Get-ChildItem -LiteralPath 'licenses' -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $packageRoot ('licenses\'+$_.Name)) -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $packageRoot 'docs') | Out-Null
Copy-Item -LiteralPath 'docs\READING.md' -Destination (Join-Path $packageRoot 'docs\READING.md') -Force
Copy-Item -LiteralPath 'docs\MOONSHINE.md' -Destination (Join-Path $packageRoot 'docs\MOONSHINE.md') -Force
Copy-Item -LiteralPath 'docs\WORKSHOP.md' -Destination (Join-Path $packageRoot 'docs\WORKSHOP.md') -Force
Copy-Item -LiteralPath 'docs\TEST_REPORT.md' -Destination (Join-Path $packageRoot 'TESTBERICHT.md') -Force
$manifest=Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object FullName | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetRelativePath($packageRoot,$_.FullName).Replace('\','/')
}
$manifest | Set-Content -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.txt') -Encoding utf8
$archive=Join-Path $projectRoot 'dist\Sprachgarten-0.4.0-preview.19-win-x64.zip'
$archiveTemp=$archive+'.partial'
if(Test-Path -LiteralPath $archiveTemp) { throw 'Unfertiges Archiv existiert bereits; vor erneutem Packen prüfen.' }
# Stream entries using Zip64-capable .NET APIs, including the 2.50 GB model file.
[IO.Compression.ZipFile]::CreateFromDirectory($packageRoot,$archiveTemp,[IO.Compression.CompressionLevel]::Fastest,$true)
Move-Item -LiteralPath $archiveTemp -Destination $archive -Force
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List
