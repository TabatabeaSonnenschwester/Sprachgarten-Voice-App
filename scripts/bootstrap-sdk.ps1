$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$toolsDir=Join-Path $projectRoot '.tools'
New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
$archive=Join-Path $toolsDir 'dotnet-sdk.zip'
$expected='24b670ad3d923bfcf47df6c3b034152398b42f6dbc388e10d783aee1cfb5e5817d399fc0ae2a12cfa822a55e61d34830ccb15c50ef6efee437ab874bb7c79430'
if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $expected) {
    Invoke-WebRequest 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-win-x64.zip' -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $expected) { throw 'SDK-Integritätsprüfung fehlgeschlagen' }
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $toolsDir 'dotnet') -Force
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_CLI_HOME=Join-Path $toolsDir 'cli'
& (Join-Path $toolsDir 'dotnet\dotnet.exe') --version
