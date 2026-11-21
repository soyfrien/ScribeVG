# Builds the ClickOnce install for https://ppdac.ltd/scribevg/ on Windows.
# Windows PowerShell 5.1 compatible. See docs/publishing.md.
#   powershell -ExecutionPolicy Bypass -File publish-clickonce.ps1 -Revision 2 -Source C:\path\to\ScribeVG
# Output: clickonce.tgz next to this script's -Work folder, ready to upload.
param(
    [Parameter(Mandatory = $true)][int]$Revision,
    [Parameter(Mandatory = $true)][string]$Source,
    [string]$Work = (Join-Path $env:TEMP 'scribevg-build')
)
$ErrorActionPreference = 'Stop'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'Visual Studio MSBuild not found' }
Write-Host "MSBuild: $msbuild"

$proj = Join-Path $Source 'ScribeVG\ScribeVG.csproj'
$out = Join-Path $Source 'ScribeVG\bin\publish\clickonce'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

& $msbuild $proj /restore /t:Publish /p:Configuration=Release /p:PublishProfile=ClickOnce "/p:ApplicationRevision=$Revision" /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed with exit code $LASTEXITCODE" }

foreach ($f in @('ScribeVG.application', 'setup.exe')) {
    if (-not (Test-Path (Join-Path $out $f))) { throw "$f missing from $out" }
}
[xml]$m = Get-Content (Join-Path $out 'ScribeVG.application')
Write-Host ('Deployment version ' + $m.assembly.assemblyIdentity.version)

New-Item -ItemType Directory -Force -Path $Work | Out-Null
$tgz = Join-Path $Work 'clickonce.tgz'
if (Test-Path $tgz) { Remove-Item $tgz -Force }
& tar.exe -czf $tgz -C $out .
if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE" }
Get-Item $tgz | Select-Object FullName, Length
