param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
# Some hosts pass both PATH and Path; desktop MSBuild rejects that environment.
$taskPath = [Environment]::GetEnvironmentVariable('Path', 'Process')
[Environment]::SetEnvironmentVariable('PATH', $null, 'Process')
[Environment]::SetEnvironmentVariable('Path', $null, 'Process')
[Environment]::SetEnvironmentVariable('Path', $taskPath, 'Process')
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$installation) { throw 'Install Visual Studio 2022 C++ desktop tools and Windows SDK.' }
$cmake = Join-Path $installation 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
if (!(Test-Path -LiteralPath $cmake)) { $cmake = (Get-Command cmake -ErrorAction Stop).Source }
$source = Join-Path $PSScriptRoot 'native'
$output = Join-Path $source 'obj'
& $cmake -S $source -B $output -G 'Visual Studio 17 2022' -A x64
if ($LASTEXITCODE) { throw 'Native adapter configuration failed.' }
& $cmake --build $output --config $Configuration --parallel
if ($LASTEXITCODE) { throw 'Native adapter build failed.' }
