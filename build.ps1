param(
    [string]$ClientDir = 'D:\libreko\build\client\data_LibreKO_windows_x86_64',
    [string]$InstallDir = 'D:\libreko\build\client\plugins\knightonline-ui-classic'
)
$ErrorActionPreference = 'Stop'
dotnet build (Join-Path $PSScriptRoot 'KnightOnlineUiClassic.csproj') -c Release "-p:LibreKOClientDir=$ClientDir" --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Classic UI build failed' }
New-Item -ItemType Directory -Force (Join-Path $InstallDir 'bin') | Out-Null
foreach ($item in @('plugin.json','LICENSE','README.md','THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $item) -Destination $InstallDir -Force
}
foreach ($sub in @('layouts','textures','overrides')) {
    $src=Join-Path $PSScriptRoot "assets\$sub"
    if (Test-Path -LiteralPath $src) {
        $dst=Join-Path $InstallDir "assets\$sub"
        New-Item -ItemType Directory -Force $dst | Out-Null
        Get-ChildItem -LiteralPath $src -File | Copy-Item -Destination $dst -Force
    }
}
foreach ($file in @('index.json','theme.json')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "assets\$file") -Destination (Join-Path $InstallDir 'assets') -Force
}
foreach ($file in @('KnightOnlineUiClassic.dll','KnightOnlineUiClassic.pdb')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "bin\$file") -Destination (Join-Path $InstallDir 'bin') -Force
}
Write-Output "Installed: $InstallDir"
