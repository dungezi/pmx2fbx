param([string]$Destination = '')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$packageInfo = Get-Content -LiteralPath (Join-Path $workspace 'package.json') -Raw | ConvertFrom-Json
if (!$Destination) { $Destination = Join-Path $workspace "dist~\$($packageInfo.name)-$($packageInfo.version).tgz" }
$stage = Join-Path $workspace '.validation~\PackageStaging'
$packageStage = Join-Path $stage 'package'
New-Item -ItemType Directory -Force -Path $packageStage, (Split-Path -Parent $Destination) | Out-Null
foreach ($name in @('Editor','Editor.meta','Shaders','Shaders.meta','Tests~','Tools~','package.json','package.json.meta','README.md','README.md.meta','LICENSE','LICENSE.meta','ThirdPartyNotices.md','ThirdPartyNotices.md.meta','VALIDATION.md','VALIDATION.md.meta')) {
    $source = Join-Path $workspace $name
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $packageStage -Recurse -Force }
}
& tar -czf $Destination -C $stage package
if ($LASTEXITCODE -ne 0) { throw 'Package creation failed.' }
Write-Output "Created: $Destination"
