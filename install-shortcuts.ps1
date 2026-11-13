<#
.SYNOPSIS
    Adds "Eteris" shortcuts to the Desktop and Start menu for a RadioPlayer.exe.

.DESCRIPTION
    With no arguments it looks for RadioPlayer.exe next to this script, then in .\app and
    .\dist\Eteris-win-x64. Existing shortcuts are replaced so they point at the exe found.

.EXAMPLE
    .\install-shortcuts.ps1
    .\install-shortcuts.ps1 -Exe 'D:\Tools\Eteris\RadioPlayer.exe'
#>
param(
    [string]$Exe
)

$ErrorActionPreference = 'Stop'

if (-not $Exe) {
    $Exe = '.', 'app', 'dist\Eteris-win-x64' |
        ForEach-Object { Join-Path $PSScriptRoot (Join-Path $_ 'RadioPlayer.exe') } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
}
if (-not $Exe -or -not (Test-Path $Exe)) {
    throw 'RadioPlayer.exe not found. Pass its location with -Exe, or build it first (see README).'
}
$Exe = (Resolve-Path $Exe).Path

$shell = New-Object -ComObject WScript.Shell
foreach ($folder in [Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs')) {
    $shortcut = $shell.CreateShortcut((Join-Path $folder 'Eteris.lnk'))
    $shortcut.TargetPath = $Exe
    $shortcut.WorkingDirectory = Split-Path $Exe
    $shortcut.IconLocation = "$Exe,0"
    $shortcut.Description = 'Live radio player'
    $shortcut.Save()
    Write-Host "Shortcut: $(Join-Path $folder 'Eteris.lnk') -> $Exe"
}
