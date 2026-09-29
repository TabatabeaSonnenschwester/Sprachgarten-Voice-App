param([switch]$Publish, [string]$OutputPath='dist\Sprachgarten-0.4.0-preview.14-win-x64')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\packages'
$sdkExe = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $sdkExe)) { $sdkExe = 'dotnet' }
if ($Publish) {
    & $sdkExe publish src\Klartext\Klartext.csproj -c Release -r win-x64 --self-contained true -o $OutputPath -p:PublishSingleFile=false -p:PublishReadyToRun=false
} else {
    & $sdkExe build src\Klartext\Klartext.csproj -c Release
}
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen' }
