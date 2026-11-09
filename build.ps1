param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework 4.8 compiler is required.'
}
$output = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File
$manifest = Join-Path $PSScriptRoot 'src\app.manifest'
$icon = Join-Path $PSScriptRoot 'src\GrayShotV2.ico'
$exe = Join-Path $output 'GrayShotV2.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ /codepage:65001 "/win32manifest:$manifest" "/win32icon:$icon" "/out:$exe" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll $sources.FullName
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}
Write-Output $exe
