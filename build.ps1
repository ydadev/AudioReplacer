$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $projectRoot 'dist'
$portable = Join-Path $dist 'AudioReplacer-Portable-v1.0'
$lite = Join-Path $dist 'AudioReplacer-Lite-v1.0'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (!(Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (!(Test-Path -LiteralPath $csc)) {
    throw 'The system C# .NET Framework compiler was not found.'
}

if (Test-Path -LiteralPath $dist) {
    Remove-Item -LiteralPath $dist -Recurse -Force
}
New-Item -ItemType Directory -Path $portable -Force | Out-Null
New-Item -ItemType Directory -Path $lite -Force | Out-Null

$exe = Join-Path $dist 'AudioReplacer.exe'
& $csc /nologo /target:winexe /optimize+ /platform:anycpu `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.IO.Compression.dll `
    /reference:System.IO.Compression.FileSystem.dll `
    "/win32icon:$projectRoot\assets\duck.ico" `
    "/resource:$projectRoot\assets\duck.png,DuckPng" `
    "/out:$exe" `
    "$projectRoot\src\Program.cs"

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath $exe -Destination (Join-Path $portable 'AudioReplacer.exe')
Copy-Item -LiteralPath $exe -Destination (Join-Path $lite 'AudioReplacer.exe')
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\README-Portable.txt') -Destination $portable
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\README-Lite.txt') -Destination $lite
Set-Content -LiteralPath (Join-Path $portable 'portable-full.mode') `
    -Value 'Portable mode: FFmpeg must be stored next to AudioReplacer.exe.' `
    -Encoding ASCII

Compress-Archive -Path (Join-Path $portable '*') `
    -DestinationPath (Join-Path $dist 'AudioReplacer-Portable-v1.0.zip') -Force
Compress-Archive -Path (Join-Path $lite '*') `
    -DestinationPath (Join-Path $dist 'AudioReplacer-Lite-v1.0.zip') -Force

Write-Host 'Build completed:'
Get-ChildItem -LiteralPath $dist -Filter '*.zip'
