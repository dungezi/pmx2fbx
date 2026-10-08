param([string]$UnityEditor = '')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$validation = Join-Path $workspace '.validation~\UnityProject'
$cache = Join-Path $env:LOCALAPPDATA 'Unity\cache\npm\packages.unity.com'
New-Item -ItemType Directory -Force -Path "$validation\Assets\Editor", "$validation\Packages", "$validation\ProjectSettings" | Out-Null
Copy-Item -LiteralPath "$workspace\Tests~\PmxFixture.cs", "$workspace\Tests~\XpsFixture.cs", "$workspace\Tests~\UnityValidation.cs" -Destination "$validation\Assets\Editor" -Force
$cachedPackages = @{'com.unity.formats.fbx'='4.2.1'; 'com.autodesk.fbx'='4.2.1'; 'com.unity.timeline'='1.7.6'; 'com.unity.test-framework'='1.1.33'; 'com.unity.ext.nunit'='1.0.6'}
foreach ($packageName in $cachedPackages.Keys) {
    $packageSource = Join-Path $cache "$packageName\$($cachedPackages[$packageName])\package"
    if (!(Test-Path -LiteralPath $packageSource)) { throw "Missing cached package: $packageSource" }
    $packageDestination = Join-Path "$validation\Packages" $packageName
    if (!(Test-Path -LiteralPath $packageDestination)) {
        $packageTarball = Join-Path $cache "$packageName\$($cachedPackages[$packageName])\package.tgz"
        if (Test-Path -LiteralPath $packageTarball) {
            New-Item -ItemType Directory -Force -Path $packageDestination | Out-Null
            & tar -xzf $packageTarball -C $packageDestination --strip-components=1
            if ($LASTEXITCODE -ne 0) { throw "Tar extraction failed: $packageName" }
        } else { Copy-Item -LiteralPath $packageSource -Destination $packageDestination -Recurse }
    }
}
$dependencies = [ordered]@{'com.pmx2fbx.converter'="file:$($workspace.Replace('\','/'))"}
foreach ($module in @('animation','audio','director','imgui','imageconversion','physics','particlesystem','jsonserialize','ui','uielements','assetbundle','unitywebrequest','unitywebrequestassetbundle','unitywebrequesttexture')) { $dependencies["com.unity.modules.$module"]='1.0.0' }
@{dependencies=$dependencies} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$validation\Packages\manifest.json" -Encoding utf8
"m_EditorVersion: 2022.3.22f1`nm_EditorVersionWithRevision: 2022.3.22f1 (887be4894c44)" | Set-Content -LiteralPath "$validation\ProjectSettings\ProjectVersion.txt"
Write-Output "Prepared isolated Unity validation project: $validation"
if ($UnityEditor) { Write-Output "Editor: $UnityEditor" }
