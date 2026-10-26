$ErrorActionPreference = 'Stop'
$cameraCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$cameraOutput = Join-Path $PSScriptRoot 'WarthogCamera.exe'
& $cameraCompiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$cameraOutput" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'src\WarthogCamera.cs') (Join-Path $PSScriptRoot 'src\SeatCameraData.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$cameraManifestPath = Join-Path $PSScriptRoot 'manifest.json'
$cameraManifest = Get-Content -LiteralPath $cameraManifestPath -Raw | ConvertFrom-Json
$cameraStream = [IO.File]::OpenRead($cameraOutput)
$cameraSha = [Security.Cryptography.SHA256]::Create()
try { $cameraManifest.helper_sha256 = [BitConverter]::ToString($cameraSha.ComputeHash($cameraStream)).Replace('-', '') }
finally { $cameraSha.Dispose(); $cameraStream.Dispose() }
$cameraManifest | ConvertTo-Json | Set-Content -LiteralPath $cameraManifestPath -Encoding UTF8
Write-Host 'Built WarthogCamera.exe and updated its package checksum.'
