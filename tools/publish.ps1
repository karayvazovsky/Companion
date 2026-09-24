$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (Get-Process Companion -ErrorAction SilentlyContinue) {
    throw 'Companion is running. Quit it from the tray first.'
}
dotnet publish "$root\src\Companion\Companion.csproj" -c Release -r win-x64 --self-contained false -o "$root\app"
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
dotnet publish "$root\src\Companion.Memory\Companion.Memory.csproj" -c Release -r win-x64 --self-contained false -o "$root\app"
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (memory server) failed.' }
Write-Host "Published to $root\app\Companion.exe"
