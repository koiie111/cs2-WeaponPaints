[CmdletBinding()]
param(
    [string]$Cs2Root='E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive',
    [string]$Addon='cs2_4ill_weaponpaints_mockup',
    [switch]$Compile
)
$ErrorActionPreference='Stop'
if ($Addon -notmatch '^[a-z0-9_]+$') { throw 'Invalid addon name' }
$source=Join-Path $PSScriptRoot 'addon'
$contentRoot=[IO.Path]::GetFullPath((Join-Path $Cs2Root 'content\csgo_addons'))
$target=[IO.Path]::GetFullPath((Join-Path $contentRoot $Addon))
if (-not $target.StartsWith($contentRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Addon path escaped content tree' }
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'panorama') -Destination $target -Recurse -Force
Copy-Item -LiteralPath (Join-Path $source 'materials') -Destination $target -Recurse -Force
Copy-Item -LiteralPath (Join-Path $source 'addoninfo.txt') -Destination $target -Force
Write-Host "Workshop source: $target"
if ($Compile) {
    $compiler=Join-Path $Cs2Root 'game\bin\win64\resourcecompiler.exe'
    if (-not (Test-Path -LiteralPath $compiler)) { throw 'Workshop resource compiler not installed' }
    foreach($file in Get-ChildItem -LiteralPath $target -File -Recurse | Where-Object Extension -in '.xml','.css','.vtex') {
        & $compiler -i $file.FullName
        if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $($file.FullName)" }
        $relative=$file.FullName.Substring($target.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
        $compiledRelative=[IO.Path]::ChangeExtension($relative, $(if ($file.Extension -eq '.xml') { '.vxml_c' } elseif ($file.Extension -eq '.vtex') { '.vtex_c' } else { '.vcss_c' }))
        $artifact=Join-Path (Join-Path $Cs2Root "game\csgo_addons\$Addon") $compiledRelative
        if (-not (Test-Path -LiteralPath $artifact) -or (Get-Item -LiteralPath $artifact).Length -eq 0) { throw "Missing compiled resource: $artifact" }
        Write-Host "Compiled resource: $artifact"
    }
}
# Never copies into csgo/overrides, edits gameinfo, uploads to Workshop, or contacts a server.
