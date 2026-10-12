param([string]$AppDirectory='dist\Sprachgarten-0.4.0-preview.19-win-x64', [string]$DesktopDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$appRoot=if([IO.Path]::IsPathRooted($AppDirectory)){[IO.Path]::GetFullPath($AppDirectory)}else{[IO.Path]::GetFullPath((Join-Path $root $AppDirectory))}
$exe=Join-Path $appRoot 'Sprachgarten.exe'
foreach($file in @($exe,(Join-Path $appRoot 'Moonshine\python\python.exe'),(Join-Path $appRoot 'moonshine-config.json'),(Join-Path $appRoot 'SHA256SUMS.txt'))){if(!(Test-Path -LiteralPath $file)){throw ('Unvollständiges Paket: '+$file)}}
if(!$DesktopDirectory){$DesktopDirectory=[Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)}
$desktop=[IO.Path]::GetFullPath($DesktopDirectory)
if(!(Test-Path -LiteralPath $desktop -PathType Container)){throw 'Desktopordner fehlt.'}
$link=Join-Path $desktop 'Sprachgarten - Moonshine.lnk'
$shell=New-Object -ComObject WScript.Shell
if(Test-Path -LiteralPath $link){
    $existing=$shell.CreateShortcut($link)
    if($existing.TargetPath -ine $exe){throw 'Eine andere Verknüpfung liegt unter diesem Namen. Sie bleibt erhalten.'}
}else{
    $shortcut=$shell.CreateShortcut($link)
    $shortcut.TargetPath=$exe
    $shortcut.WorkingDirectory=$appRoot
    $shortcut.IconLocation=$exe+',0'
    $shortcut.Description='Sprachgarten 0.4.0-preview.19 mit lokaler Moonshine-Diktaterkennung'
    $shortcut.Save()
}
$verified=$shell.CreateShortcut($link)
if($verified.TargetPath -ine $exe -or $verified.WorkingDirectory -ine $appRoot){throw 'Verknüpfungskontrolle fehlgeschlagen.'}
[ordered]@{Shortcut=$link;Target=$verified.TargetPath;WorkingDirectory=$verified.WorkingDirectory;Version=[Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion;AppStarted=$false;ExistingAppStopped=$false}|ConvertTo-Json
