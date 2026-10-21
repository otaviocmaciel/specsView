<#
.SYNOPSIS
    Builds SpecsView.exe as a single file and optionally installs it for the current user.

.PARAMETER SelfContained
    Bundle the .NET runtime (larger file, runs on machines without the .NET 10 Desktop Runtime).

.PARAMETER Install
    Copy the executable to %LOCALAPPDATA%\Programs\SpecsView and create a Start Menu shortcut.

.EXAMPLE
    ./publish.ps1 -Install
#>
param(
    [switch]$SelfContained,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'

$flavor = if ($SelfContained) { 'self-contained' } else { 'framework-dependent' }
$output = Join-Path $PSScriptRoot "artifacts\publish\$flavor"

dotnet publish (Join-Path $PSScriptRoot 'src\SpecsView.App') `
    -c Release `
    --self-contained $SelfContained.IsPresent.ToString().ToLowerInvariant() `
    -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$exe = Join-Path $output 'SpecsView.exe'
Write-Host ("Published {0} ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB))

if (-not $Install) { return }

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\SpecsView'
$installedExe = Join-Path $installDir 'SpecsView.exe'

$running = Get-Process -Name SpecsView -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $installedExe }
if ($running) {
    throw "SpecsView is running from $installDir. Exit it (right-click > Sair) and run this script again."
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item $exe $installedExe -Force

$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'SpecsView.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installedExe
$shortcut.WorkingDirectory = $installDir
$shortcut.Description = 'Taskbar system monitor'
$shortcut.Save()

Write-Host "Installed to $installedExe"
Write-Host "Start Menu shortcut: $shortcutPath"
