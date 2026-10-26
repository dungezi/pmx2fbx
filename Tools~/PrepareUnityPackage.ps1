$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$project = Join-Path $workspace '.validation~\UnityPackageProject'
$plugin = Join-Path $project 'Assets\PMX2FBX'
New-Item -ItemType Directory -Force -Path $plugin, "$project\Assets\Editor", "$project\Packages", "$project\ProjectSettings" | Out-Null
foreach ($name in @('Editor','Editor.meta','Shaders','Shaders.meta','README.md','README.md.meta','LICENSE','LICENSE.meta','ThirdPartyNotices.md','ThirdPartyNotices.md.meta','VALIDATION.md','VALIDATION.md.meta')) {
    $source = Join-Path $workspace $name
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $plugin -Recurse -Force }
}
Copy-Item -LiteralPath "$workspace\Tests~\UnityPackageValidation.cs", "$workspace\Tests~\UnityValidation.cs", "$workspace\Tests~\PmxFixture.cs", "$workspace\Tests~\XpsFixture.cs", "$workspace\Tests~\MaterialRenderValidation.cs" -Destination "$project\Assets\Editor" -Force
Copy-Item -LiteralPath "$workspace\Tests~\VrmFixture.cs", "$workspace\Tests~\RigVrmValidation.cs" -Destination "$project\Assets\Editor" -Force
$dependencies = [ordered]@{}
foreach ($name in @('com.autodesk.fbx','com.unity.formats.fbx','com.unity.timeline','com.unity.test-framework','com.unity.ext.nunit')) {
    $path = Join-Path $workspace ".validation~\UnityProject\Packages\$name"
    if (!(Test-Path -LiteralPath "$path\package.json")) { throw "Missing validation dependency: $path" }
    $dependencies[$name] = "file:$($path.Replace('\','/'))"
}
foreach ($module in @('animation','audio','director','imgui','imageconversion','physics','particlesystem','jsonserialize','ui','uielements','assetbundle','unitywebrequest','unitywebrequestassetbundle','unitywebrequesttexture')) { $dependencies["com.unity.modules.$module"]='1.0.0' }
@{dependencies=$dependencies} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$project\Packages\manifest.json" -Encoding utf8
"m_EditorVersion: 2022.3.22f1`nm_EditorVersionWithRevision: 2022.3.22f1 (887be4894c44)" | Set-Content -LiteralPath "$project\ProjectSettings\ProjectVersion.txt"
Write-Output "Prepared Assets installation: $project"
