param()
$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $projectDirectory 'RoamClock.csproj'
$publishDirectory = Join-Path $projectDirectory 'bin\msi-publish'
$toolDirectory = Join-Path $projectDirectory 'bin\tools'
$wix = Join-Path $toolDirectory 'wix.exe'
$wixVersion = '5.0.2'

if (!(Test-Path -LiteralPath $wix)) {
    & dotnet tool install wix --version $wixVersion --tool-path $toolDirectory
    if ($LASTEXITCODE) { throw 'WiX tool installation failed.' }
}
$installedVersion = & $wix --version
if ($LASTEXITCODE -or !$installedVersion.StartsWith($wixVersion + '+')) {
    throw "Expected WiX $wixVersion; found $installedVersion. Update the tool in $toolDirectory."
}

$uiExtension = Join-Path $projectDirectory ".wix\extensions\WixToolset.UI.wixext\$wixVersion\wixext5\WixToolset.UI.wixext.dll"
if (!(Test-Path -LiteralPath $uiExtension)) {
    Push-Location $projectDirectory
    try {
        & $wix extension add "WixToolset.UI.wixext/$wixVersion"
        if ($LASTEXITCODE) { throw 'WiX UI extension installation failed.' }
    }
    finally {
        Pop-Location
    }
}

# Start with a clean staging directory so previous runtime/cache files cannot survive a publish.
$expectedPublishDirectory = [IO.Path]::GetFullPath((Join-Path $projectDirectory 'bin\msi-publish'))
if ([IO.Path]::GetFullPath($publishDirectory) -ne $expectedPublishDirectory) {
    throw 'Refusing to clean a publish directory outside bin\msi-publish.'
}
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
& dotnet publish $projectPath -c Release -r win-x64 -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:SelfContained=false "-p:PublishDir=$publishDirectory\" --nologo
if ($LASTEXITCODE) { throw 'MSI application publish failed.' }
$expectedFiles = @('RoamClock.exe', 'RoamClock.Native.dll', 'THIRD-PARTY-NOTICES.md')
$unexpectedFiles = Get-ChildItem -LiteralPath $publishDirectory -Force | Where-Object { $_.PSIsContainer -or $_.Name -notin $expectedFiles }
if ($unexpectedFiles) { throw "Unexpected MSI publish content: $($unexpectedFiles.Name -join ', ')" }
foreach ($file in $expectedFiles) {
    if (!(Test-Path -LiteralPath (Join-Path $publishDirectory $file) -PathType Leaf)) {
        throw "Missing MSI publish file: $file"
    }
}
[xml]$project = Get-Content -LiteralPath $projectPath
$version = [string]$project.Project.PropertyGroup.AssemblyVersion
$msiPath = Join-Path $projectDirectory "bin\RoamClock-$version-win-x64.msi"
& $wix build (Join-Path $PSScriptRoot 'Package.wxs') -arch x64 `
    -ext $uiExtension -culture zh-CN `
    -d "ProductVersion=$version" -d "ProjectDir=$projectDirectory" -d "PublishDir=$publishDirectory" `
    -intermediateFolder (Join-Path $projectDirectory 'bin\msi-obj') -o $msiPath
if ($LASTEXITCODE) { throw 'MSI build failed.' }
Write-Output "MSI package: $msiPath"
