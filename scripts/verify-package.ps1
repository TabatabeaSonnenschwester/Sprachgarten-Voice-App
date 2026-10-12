param([string]$Archive='dist\Sprachgarten-0.4.0-preview.19-win-x64.zip', [string]$AudioPath, [string]$PrivateResultPath, [switch]$Prompt, [string]$ExpectedText, [string]$AsrFixtures)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
if($AudioPath -and !$PrivateResultPath) { throw 'Für einen Dateitest PrivateResultPath außerhalb des ausgelieferten Pakets angeben.' }
$archivePath=(Resolve-Path -LiteralPath $Archive).Path
$extractRoot=Join-Path $projectRoot ('artifacts\package-check-'+[guid]::NewGuid().ToString('N'))
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($archivePath,$extractRoot)
$packageRoot=(Get-ChildItem -LiteralPath $extractRoot -Directory | Select-Object -First 1).FullName
$checked=0
foreach($line in Get-Content -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.txt')) {
    if($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw 'Ungültige Manifestzeile' }
    $expected=$Matches[1]; $relative=$Matches[2]
    $path=[IO.Path]::GetFullPath((Join-Path $packageRoot $relative))
    if(!$path.StartsWith($packageRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Manifestpfad außerhalb des Pakets' }
    if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) { throw ('Abweichung: '+$relative) }
    $checked++
}
$evidence=[ordered]@{Build=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $packageRoot 'Sprachgarten.exe')).ProductVersion; Archive=$archivePath; ArchiveSha256=(Get-FileHash -LiteralPath $archivePath).Hash; Extracted=$packageRoot; VerifiedFiles=$checked; Signature=(Get-AuthenticodeSignature -LiteralPath (Join-Path $packageRoot 'Sprachgarten.exe')).Status.ToString(); Timestamp=(Get-Date -Format o)}
if($AsrFixtures){
    $report=Join-Path $extractRoot 'synthetic-asr-result.json'
    $info=[Diagnostics.ProcessStartInfo]::new((Join-Path $packageRoot 'Sprachgarten.exe'))
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.WorkingDirectory=$packageRoot
    foreach($argument in @('--moonshine-selftest',[IO.Path]::GetFullPath($AsrFixtures),$report)){$info.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($info)
    try{
        if(!$process.WaitForExit(240000)){$process.Kill($true);throw 'Moonshine-Paketprüfung: Zeitlimit.'}
        if($process.ExitCode -ne 0){throw ('Moonshine-Paketprüfung fehlgeschlagen: '+$process.ExitCode)}
        $result=Get-Content -Raw -LiteralPath $report|ConvertFrom-Json
        if($result.status -ne 'PASS'){throw 'Moonshine-Paketprüfung meldet keinen PASS.'}
        $evidence.AsrChecks=$result.checks.Count;$evidence.TestKind='synthetic-Moonshine-ASR-no-microphone-no-clipboard'
    }finally{$process.Dispose()}
}
if($AudioPath) {
    if(Test-Path -LiteralPath (Join-Path $packageRoot 'moonshine-config.json')){throw 'AudioPath prüft historische Whisper-Ausgaben. Für Moonshine AsrFixtures mit synthetischem short.wav und long.wav verwenden.'}
    $info=[Diagnostics.ProcessStartInfo]::new((Join-Path $packageRoot 'Sprachgarten.exe'))
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden; $info.WorkingDirectory=$packageRoot
    foreach($argument in @('--worker',(Join-Path $packageRoot 'models\ggml-small.bin'),[IO.Path]::GetFullPath($AudioPath),[IO.Path]::GetFullPath($PrivateResultPath))) { $info.ArgumentList.Add($argument) }
    $process=[Diagnostics.Process]::Start($info)
    try {
        if(!$process.WaitForExit(600000)) { $process.Kill(); throw 'Datei-ASR-Zeitlimit' }
        if($process.ExitCode -ne 0) { throw ('Datei-ASR fehlgeschlagen: '+$process.ExitCode) }
        $result=Get-Content -Raw -LiteralPath $PrivateResultPath | ConvertFrom-Json
        $evidence.AsrExitCode=$process.ExitCode; $evidence.AsrSeconds=$result.Seconds; $evidence.ResultCharacters=$result.Text.Length
        if($ExpectedText) { $evidence.AsrReferenceExact=($result.Text.Trim() -ceq $ExpectedText); if(!$evidence.AsrReferenceExact) { throw 'Datei-ASR weicht vom angegebenen Solltext ab.' } }
        $evidence.TestKind='extracted-package-file-ASR-no-microphone-no-clipboard'
    } finally { $process.Dispose() }
}
if($Prompt) {
    $promptResult=Join-Path $extractRoot 'synthetic-prompt-result.json'
    $info=[Diagnostics.ProcessStartInfo]::new((Join-Path $packageRoot 'Sprachgarten.exe'))
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden; $info.WorkingDirectory=$packageRoot
    $info.ArgumentList.Add('--verify-prompt-review'); $info.ArgumentList.Add($promptResult)
    $process=[Diagnostics.Process]::Start($info)
    try {
        if(!$process.WaitForExit(240000)) { $process.Kill(); throw 'Paket-KI-Zeitlimit.' }
        if($process.ExitCode -ne 0) { throw ('Paket-KI-Prüfung fehlgeschlagen: '+$process.ExitCode) }
        $result=Get-Content -Raw -LiteralPath $promptResult | ConvertFrom-Json
        $evidence.PromptExitCode=$process.ExitCode; $evidence.PromptSeconds=$result.Seconds; $evidence.PromptQuestionCount=$result.Review.Questions.Count
        $evidence.PromptTest='real local review of missing source text; expected one concrete Text question; no microphone or clipboard; not general quality acceptance'
    } finally { $process.Dispose() }
}
$evidence | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts\package-verification.json') -Encoding utf8
$evidence | ConvertTo-Json
