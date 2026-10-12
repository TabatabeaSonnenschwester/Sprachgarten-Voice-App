param([switch]$Asr, [switch]$CorrectionsOnly, [switch]$ThemeOnly, [switch]$ExtensionsOnly, [switch]$LocalModel, [switch]$ExtensionUiOnly, [switch]$PromptReview, [switch]$CloudHandoff, [switch]$UxOnly, [switch]$ReadingOnly, [switch]$ReadingLive, [string]$ModelCase, [switch]$ReviewFixes, [switch]$UxSafety, [switch]$WorkshopUx)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$env:DOTNET_CLI_HOME=Join-Path $projectRoot '.tools\cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:NUGET_PACKAGES=Join-Path $projectRoot '.tools\packages'
$sdkExe=Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $sdkExe)) { $sdkExe='dotnet' }
& $sdkExe build tests\Klartext.Tests\Klartext.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Testbuild fehlgeschlagen' }
$env:DOTNET_ROOT=Split-Path $sdkExe -Parent
$testArgs=@($projectRoot)
if ($Asr) { $testArgs+='--asr' }
if ($CorrectionsOnly) { $testArgs+='--corrections-only' }
if ($ThemeOnly) { $testArgs=@('--theme-only', $projectRoot) }
if ($ExtensionsOnly) { $testArgs=@('--extensions-only', $projectRoot); if ($LocalModel) { $testArgs+='--local-model'; if ($ModelCase) { $testArgs+=('--case='+$ModelCase) } } }
if ($ExtensionUiOnly) { $testArgs=@('--extension-ui-only', $projectRoot) }
if ($PromptReview) { $testArgs=@('--prompt-review-tests', $projectRoot) }
if ($CloudHandoff) { $testArgs=@('--cloud-handoff-tests', $projectRoot) }
if ($UxOnly) { $testArgs=@('--ux-only', $projectRoot) }
if ($ReadingOnly) { $testArgs=@('--reading-tests', $projectRoot) }
if ($ReadingLive) { $testArgs=@('--reading-live', $projectRoot) }
if ($ReviewFixes) { $testArgs=@('--review-fixes', $projectRoot) }
if ($UxSafety) { $testArgs=@('--ux-safety', $projectRoot) }
if ($WorkshopUx) { $testArgs=@('--workshop-ux', $projectRoot) }
& '.\tests\Klartext.Tests\bin\Release\net10.0-windows\Klartext.Tests.exe' @testArgs
if ($LASTEXITCODE -ne 0) { throw 'Tests fehlgeschlagen; artifacts/test-results.json prüfen' }
