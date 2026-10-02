param([Parameter(Mandatory)][string]$NativeDll)
$ErrorActionPreference = 'Stop'
$nativePath = (Resolve-Path -LiteralPath $NativeDll).Path
$buildHash = (Get-FileHash -LiteralPath $nativePath -Algorithm SHA256).Hash.Substring(0,16)
$checksPath = Join-Path $PSScriptRoot 'bin\Debug\net8.0-windows\AdapterChecks.dll'
if (!(Test-Path -LiteralPath $checksPath)) { throw 'Build AdapterChecks.csproj first.' }
$stdoutPath = Join-Path $PSScriptRoot 'bin\crash-check.stdout'
$stderrPath = Join-Path $PSScriptRoot 'bin\crash-check.stderr'
$arguments = '"{0}" --hold "{1}"' -f $checksPath, $nativePath
$helper = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList $arguments `
    -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    $output = ''
    while ([DateTime]::UtcNow -lt $deadline) {
        $output = Get-Content -LiteralPath $stdoutPath -Raw -ErrorAction SilentlyContinue
        if ($output -match 'ATTACHED:') { break }
        Start-Sleep -Milliseconds 200
    }
    if ($output -notmatch 'ATTACHED:.*ExplorerPid = (\d+)') {
        throw ('Helper did not attach: ' + (Get-Content -LiteralPath $stderrPath -Raw))
    }
    $explorerPid = $Matches[1]
    Write-Output $output
    $name = 'Local\RoamClock.Adapter.v3.' + $explorerPid + '.' + $buildHash
    # Retain the acknowledgement channel: the native worker now closes its mapping.
    $mapping = [System.IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting($name)
    $view = $mapping.CreateViewAccessor()
    $mutex = [Threading.Mutex]::new($false, $name + '.Lock')
    # Terminate only the helper created by this test. Leave Explorer running.
    Stop-Process -Id $helper.Id
    $helper.WaitForExit()
    Start-Sleep -Seconds 2
    try {
        $held = $false
        try { $held = $mutex.WaitOne(1000) } catch [Threading.AbandonedMutexException] { $held = $true }
        if (!$held) { throw 'Status channel timed out.' }
        try {
            if ($view.ReadUInt32(24) -ne 0 -or $view.ReadUInt32(32) -ne 3) {
                throw 'Native cleanup was not acknowledged after controller death.'
            }
        } finally { $mutex.ReleaseMutex() }
        Write-Output 'PASS: abrupt controller exit removed all native roots'
        $remaining = (Get-Process -Id $explorerPid).Modules | Where-Object { $_.FileName -eq $nativePath }
        if ($remaining) { throw 'Adapter/native DLL still loaded after controller death.' }
        $file = [IO.File]::Open($nativePath, 'Open', 'ReadWrite', 'None')
        $file.Dispose()
        Write-Output 'PASS: abrupt controller exit unloaded the DLL and unlocked its file'
    } finally { $mutex.Dispose(); $view.Dispose(); $mapping.Dispose() }
    & dotnet $checksPath $nativePath
    if ($LASTEXITCODE) { throw 'New controller could not recover.' }
} finally {
    if (!$helper.HasExited) { Stop-Process -Id $helper.Id }
}
