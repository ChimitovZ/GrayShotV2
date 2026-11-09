$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework 4.8 compiler is required.'
}
$output = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File
$manifest = Join-Path $PSScriptRoot 'src\app.manifest'
$exe = Join-Path $output 'GrayShotV2.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ /codepage:65001 "/win32manifest:$manifest" "/out:$exe" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $sources.FullName
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}
Write-Output $exe
