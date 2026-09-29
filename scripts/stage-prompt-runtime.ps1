param([Parameter(Mandatory=$true)][string]$OutputPath, [switch]$WithoutModel)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$toolsRoot=Join-Path $projectRoot '.tools\prompt-model-test'
$archive=Join-Path $toolsRoot 'llama-b10964-bin-win-cpu-x64.zip'
$model=Join-Path $toolsRoot 'Qwen3-4B-Instruct-2507-Q4_K_M.gguf'
if (!(Test-Path -LiteralPath $archive) -or !(Test-Path -LiteralPath $model)) { throw 'Zuerst scripts/setup-prompt-model-test.ps1 -InstructCoach ausführen.' }
if ((Get-FileHash -LiteralPath $archive).Hash -ne '917f39c076402c421224824607397af20f53625a60defc20e8dd22446bf4c5d7') { throw 'Runtime-SHA256 stimmt nicht.' }
if ((Get-FileHash -LiteralPath $model).Hash -ne '8cdb57cbb880d313736a9bc4e3d3d2485f145b5e19cf33783746e753e82641fc') { throw 'Qwen-Modell-SHA256 stimmt nicht.' }
$target=if([IO.Path]::IsPathRooted($OutputPath)) { [IO.Path]::GetFullPath($OutputPath) } else { [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputPath)) }
if (!$target.StartsWith($projectRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Paket muss im Projekt liegen.' }
New-Item -ItemType Directory -Force -Path (Join-Path $target 'prompt-runtime'),(Join-Path $target 'models'),(Join-Path $target 'licenses') | Out-Null
# Extract from the verified archive; never trust an independently modified unpacked tool directory.
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $target 'prompt-runtime') -Force
if (!$WithoutModel) { Copy-Item -LiteralPath $model -Destination (Join-Path $target 'models\Qwen3-4B-Instruct-2507-Q4_K_M.gguf') -Force }
foreach($file in @('llama-cpp-LICENSE','Qwen3-4B-Instruct-LICENSE','Qwen3-4B-Instruct-conversion-README.md','Qwen3-4B-Instruct-provenance.json')) {
    Copy-Item -LiteralPath (Join-Path $toolsRoot $file) -Destination (Join-Path $target ('licenses\'+$file)) -Force
}

