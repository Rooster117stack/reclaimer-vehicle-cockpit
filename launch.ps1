param([switch]$CheckOnly, [switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
$cameraRoot = $PSScriptRoot
$cameraHelper = Join-Path $cameraRoot 'WarthogCamera.exe'
$cameraRuntime = Join-Path $cameraRoot 'runtime'
$cameraCache = Join-Path $cameraRuntime 'last-verified-release.json'
$cameraRepo = 'https://github.com/ProjectReclaimer/project-reclaimer-releases'
function Get-CameraHash([string]$Path) {
    $cameraStream = [IO.File]::OpenRead($Path)
    $cameraSha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($cameraSha.ComputeHash($cameraStream)).Replace('-', '') }
    finally { $cameraSha.Dispose(); $cameraStream.Dispose() }
}
function Get-CameraBytesHash([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-', '') }
    finally { $sha.Dispose() }
}
function Get-CameraBytes([string]$Url) {
    try { $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -Headers @{ 'User-Agent' = 'VehicleCockpit' } -TimeoutSec 30 }
    catch { $script:cameraNetworkFailure = $true; throw }
    if ($response.Content -is [byte[]]) { return ,$response.Content }
    return ,[Text.Encoding]::UTF8.GetBytes([string]$response.Content)
}
function Test-CameraDigest([string]$Digest, [string]$Hash) {
    if ($Digest -and ($Digest -notmatch '^sha256:[0-9a-fA-F]{64}$' -or $Digest.Substring(7) -ne $Hash)) { throw 'Official API asset digest does not match its checksum.' }
}
function Test-CameraRelease($Release) {
    if ($Release.schema -ne 1 -or $Release.tag -notmatch '^v\d+\.\d+\.\d+(\.\d+)?$') { throw 'Invalid cached release metadata.' }
    $version = $Release.tag.Substring(1)
    if ($Release.name -cne "project-reclaimer-client-v$version.exe" -and $Release.name -cne "project-reclaimer-v$version.exe") { throw 'Invalid official client asset name.' }
    if ($Release.url -cne "$cameraRepo/releases/download/$($Release.tag)/$($Release.name)" -or $Release.sums_url -cne "$cameraRepo/releases/download/$($Release.tag)/SHA256SUMS.txt") { throw 'Invalid official release asset URL.' }
    if ($Release.sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'Invalid official client checksum.' }
    Test-CameraDigest $Release.api_digest $Release.sha256
}
function Get-CameraLatest {
    $json = [Text.Encoding]::UTF8.GetString((Get-CameraBytes 'https://api.github.com/repos/ProjectReclaimer/project-reclaimer-releases/releases/latest'))
    $release = $json | ConvertFrom-Json
    if ($release.draft -ne $false -or $release.prerelease -ne $false -or $release.tag_name -notmatch '^v\d+\.\d+\.\d+(\.\d+)?$' -or $release.html_url -cne "$cameraRepo/releases/tag/$($release.tag_name)") { throw 'Invalid latest stable official release metadata.' }
    $version = $release.tag_name.Substring(1)
    $assets = @($release.assets | Where-Object { $_.name -ceq "project-reclaimer-client-v$version.exe" })
    if ($assets.Count -eq 0) { $assets = @($release.assets | Where-Object { $_.name -ceq "project-reclaimer-v$version.exe" }) }
    $sums = @($release.assets | Where-Object { $_.name -ceq 'SHA256SUMS.txt' })
    if ($assets.Count -ne 1 -or $sums.Count -ne 1) { throw 'The official release must contain one client and SHA256SUMS.txt.' }
    $asset = $assets[0]; $sum = $sums[0]
    $record = [pscustomobject]@{ schema = 1; tag = $release.tag_name; name = $asset.name; url = $asset.browser_download_url; sums_url = $sum.browser_download_url; sha256 = ('0' * 64); api_digest = '' }
    Test-CameraRelease $record
    $bytes = Get-CameraBytes $record.sums_url
    Test-CameraDigest $sum.digest (Get-CameraBytesHash $bytes)
    $pattern = '(?m)^([0-9a-fA-F]{64})[ \t]+\*?' + [regex]::Escape($record.name) + '\r?$'
    $matches = [regex]::Matches([Text.Encoding]::UTF8.GetString($bytes), $pattern)
    if ($matches.Count -ne 1) { throw 'The official checksum list has no unique checksum for the client.' }
    $record.sha256 = $matches[0].Groups[1].Value.ToUpperInvariant(); $record.api_digest = [string]$asset.digest
    Test-CameraRelease $record
    return $record
}
function Get-CameraCached {
    if (-not (Test-Path -LiteralPath $cameraCache -PathType Leaf)) { throw 'No verified offline client is cached. Connect to the internet and try again.' }
    $record = Get-Content -LiteralPath $cameraCache -Raw | ConvertFrom-Json
    Test-CameraRelease $record
    $path = Join-Path $cameraRuntime $record.name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-CameraHash $path) -ne $record.sha256) { throw 'The cached official client is missing or its checksum changed.' }
    return $record
}
function Get-CameraReusableHelpers {
    foreach ($helper in @(Get-Process -Name 'WarthogCamera' -ErrorAction SilentlyContinue)) {
        $row = Get-CimInstance Win32_Process -Filter "ProcessId=$($helper.Id)"
        if ($null -eq $row) { continue }
        # Test launchers may bind a helper to one particular game. It cannot attach
        # to the next game after that target exits, despite its window staying open.
        $bound = [regex]::Match([string]$row.CommandLine, '(?i)(?:^|\s)"?--pid"?\s+"?([0-9]+)"?(?=\s|$)')
        if (-not $bound.Success) { Write-Output $helper; continue }
        $target = Get-Process -Id ([int]$bound.Groups[1].Value) -ErrorAction SilentlyContinue
        $stale = $null -eq $target
        if (-not $stale) {
            # A PID reused by a newer process is not the originally bound game.
            $stale = $target.ProcessName -notlike '*reclaimer*' -or $target.StartTime -gt $helper.StartTime
        }
        if (-not $stale) { Write-Output $helper; continue }
        $helper.Refresh()
        if ($helper.HasExited) { continue }
        if ($helper.MainModule.FileName -ine [string]$row.ExecutablePath -or
            [Math]::Abs(($helper.StartTime.ToUniversalTime() - $row.CreationDate.ToUniversalTime()).TotalMilliseconds) -gt 1) {
            throw 'The existing helper changed during startup. Run the launcher again.'
        }
        Write-Host 'Closing a leftover helper whose test game has ended...'
        $closed = $helper.CloseMainWindow()
        if ($closed) { $closed = $helper.WaitForExit(5000) }
        if (-not $closed) {
            # A hidden test helper has no main window to close. Only terminate it
            # when its explicitly bound game is still absent: no live edits remain.
            if ($null -ne (Get-Process -Id ([int]$bound.Groups[1].Value) -ErrorAction SilentlyContinue)) {
                throw 'The leftover helper did not close normally. Close its window, then run PLAY_FIRST_PERSON.cmd again.'
            }
            $helper.Refresh()
            if (-not $helper.HasExited) { $helper.Kill(); [void]$helper.WaitForExit(5000) }
            if (-not $helper.HasExited) { throw 'The stale test helper could not be stopped.' }
        }
    }
}
try {
    if ($CheckOnly -and $PrepareOnly) { throw 'Use either -CheckOnly or -PrepareOnly.' }
    if (-not (Test-Path -LiteralPath $cameraHelper -PathType Leaf)) { throw 'WarthogCamera.exe is missing. Extract the whole ZIP first.' }
    $cameraManifest = Get-Content -LiteralPath (Join-Path $cameraRoot 'manifest.json') -Raw | ConvertFrom-Json
    if ((Get-CameraHash $cameraHelper) -ne $cameraManifest.helper_sha256) { throw 'The camera helper does not match this package.' }
    $cameraClients = @(Get-Process | Where-Object { $_.ProcessName -like '*reclaimer*' -and $_.MainWindowTitle -like 'Project Reclaimer*' })
    if ($cameraClients.Count -gt 0 -and -not $CheckOnly -and -not $PrepareOnly) {
        Write-Host 'Using the Reclaimer client that is already open; no client update or restart.'
    } else {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $cameraNetworkFailure = $false
        try { $cameraRelease = Get-CameraLatest }
        catch {
            if (-not $cameraNetworkFailure) { throw }
            $cameraRelease = Get-CameraCached
            Write-Host "Official release lookup unavailable. Using verified cached Reclaimer $($cameraRelease.tag) offline."
        }
        $cameraClient = Join-Path $cameraRuntime $cameraRelease.name
        if (Test-Path -LiteralPath $cameraClient -PathType Leaf) {
            if ((Get-CameraHash $cameraClient) -ne $cameraRelease.sha256) { throw 'Official client checksum mismatch. Nothing was launched.' }
        } else {
            if ($CheckOnly) { throw "Official Reclaimer $($cameraRelease.tag) is available but not prepared. Run launch.ps1 -PrepareOnly or PLAY_FIRST_PERSON.cmd." }
            New-Item -ItemType Directory -Path $cameraRuntime -Force | Out-Null
            $cameraDownload = $cameraClient + '.download'
            Write-Host "Downloading official Reclaimer $($cameraRelease.tag)..."
            try {
                Invoke-WebRequest -UseBasicParsing -Uri $cameraRelease.url -OutFile $cameraDownload -TimeoutSec 180
                if ((Get-CameraHash $cameraDownload) -ne $cameraRelease.sha256) { throw 'Official client checksum mismatch. Nothing was launched.' }
                [IO.File]::Move($cameraDownload, $cameraClient)
            } finally { if (Test-Path -LiteralPath $cameraDownload -PathType Leaf) { Remove-Item -LiteralPath $cameraDownload } }
        }
        Write-Host "Package and official Reclaimer $($cameraRelease.tag) checksums verified."
        if ($CheckOnly) { exit 0 }
        $cameraTemp = $cameraCache + '.tmp'
        $cameraRelease | ConvertTo-Json | Set-Content -LiteralPath $cameraTemp -Encoding UTF8
        # PowerShell 5.1 converts $null to an empty string for this .NET string argument.
        if (Test-Path -LiteralPath $cameraCache) { [IO.File]::Replace($cameraTemp, $cameraCache, [System.Management.Automation.Language.NullString]::Value) } else { [IO.File]::Move($cameraTemp, $cameraCache) }
        if ($PrepareOnly) { exit 0 }
    }
    $cameraHelpers = @(Get-CameraReusableHelpers)
    if ($cameraHelpers.Count -eq 0) {
        Start-Process -FilePath $cameraHelper -WorkingDirectory $cameraRoot -WindowStyle Normal | Out-Null
    } else {
        Write-Host 'Using the existing camera/audio helper. Leave its window open.'
    }
    if ($cameraClients.Count -eq 0) {
        $cameraStart = New-Object System.Diagnostics.ProcessStartInfo
        $cameraStart.FileName = $cameraClient; $cameraStart.Arguments = 'game-client'; $cameraStart.WorkingDirectory = $cameraRoot
        $cameraStart.UseShellExecute = $false; $cameraStart.CreateNoWindow = $true
        foreach ($cameraKey in @($cameraStart.EnvironmentVariables.Keys)) {
            if ([string]$cameraKey -like 'RECLAIMER_*') { $cameraStart.EnvironmentVariables.Remove([string]$cameraKey) }
        }
        [Diagnostics.Process]::Start($cameraStart) | Out-Null
    }
    Write-Host 'Leave the helper open. F8: cockpit camera. F9: Warthog radio on/off. F10: next station.'
} catch {
    Write-Host ('Cannot start: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
