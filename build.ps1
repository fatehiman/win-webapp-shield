<#
.SYNOPSIS
    Builds every part of win-webapp-shield and puts the result in .\dist.

.DESCRIPTION
    Produces four self contained single file executables that need no .NET install:

        webappshield.exe   the web wrapper itself
        encode.exe         encrypts a .conf file
        decode.exe         decrypts a .conf file
        setup.exe          installs the Microsoft Edge WebView2 Runtime if missing

    You rename webappshield.exe to whatever you like. The app looks for a .conf
    file with its own name, so mstodo.exe reads mstodo.conf.

.PARAMETER AppName
    Also place a renamed copy of the wrapper in .\dist, e.g. -AppName mstodo
    gives you dist\mstodo.exe together with dist\mstodo.conf.

.PARAMETER AppIcon
    Path to an .ico file to build into that renamed copy, so Explorer, the taskbar
    and Alt+Tab show it for the exe itself. Needs -AppName. The file is also copied
    next to the exe, because the window and tray icon come from the .conf at run
    time. tools\make-app-icon.ps1 produces a suitable .ico.

.PARAMETER Runtime
    Target runtime identifier. win-x64 (default), win-arm64 or win-x86.

.PARAMETER Clean
    Delete .\dist, .\artifacts and every obj/bin folder first.

.EXAMPLE
    .\build.ps1
.EXAMPLE
    .\build.ps1 -AppName mstodo -Clean
#>
[CmdletBinding()]
param(
    [string]$AppName,
    [string]$AppIcon,
    [ValidateSet('win-x64', 'win-arm64', 'win-x86')]
    [string]$Runtime = 'win-x64',
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = $PSScriptRoot
$dist = Join-Path $root 'dist'

function Step($text) {
    Write-Host ''
    Write-Host "==> $text" -ForegroundColor Cyan
}

if ($AppIcon) {
    if (-not $AppName) { throw '-AppIcon needs -AppName, because it is the renamed copy that gets the icon.' }
    if (-not (Test-Path $AppIcon)) { throw "Icon file not found: $AppIcon" }
    if ([System.IO.Path]::GetExtension($AppIcon) -ne '.ico') {
        throw "-AppIcon must be an .ico file. Convert one first:  .\tools\make-app-icon.ps1 -Source <file-or-url> -Out <name>.ico"
    }
    $AppIcon = (Resolve-Path $AppIcon).Path
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK was not found. Install .NET SDK 8.0 or newer from https://dotnet.microsoft.com/download"
}

if ($Clean) {
    Step 'Cleaning'
    foreach ($path in @($dist, (Join-Path $root 'artifacts'))) {
        if (Test-Path $path) { Remove-Item $path -Recurse -Force }
    }
    Get-ChildItem -Path (Join-Path $root 'src') -Include 'bin', 'obj' -Recurse -Directory |
        ForEach-Object { Remove-Item $_.FullName -Recurse -Force }
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null

Step 'Generating the application icon'
& (Join-Path $root 'tools\make-icon.ps1')

$projects = @(
    @{ Name = 'webappshield'; Path = 'src\WebAppShield\WebAppShield.csproj' }
    @{ Name = 'encode';       Path = 'src\Encode\Encode.csproj' }
    @{ Name = 'decode';       Path = 'src\Decode\Decode.csproj' }
    @{ Name = 'setup';        Path = 'src\Setup\Setup.csproj' }
)

foreach ($project in $projects) {
    Step "Publishing $($project.Name) ($Runtime)"
    $out = Join-Path $root "artifacts\publish\$($project.Name)"

    dotnet publish (Join-Path $root $project.Path) `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        --output $out `
        --nologo `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true

    if ($LASTEXITCODE -ne 0) { throw "Publishing $($project.Name) failed." }

    $exe = Join-Path $out "$($project.Name).exe"
    if (-not (Test-Path $exe)) { throw "Expected $exe but it was not produced." }
    Copy-Item $exe $dist -Force
}

Step 'Copying the sample configuration'
# A wrapper run from .\dist leaves these behind; they are not part of a release.
# -Include only filters when the path itself ends in a wildcard.
Get-ChildItem -Path (Join-Path $dist '*') -Include '*.session', '*.session.tmp', '*.conf.bak', '*.error.log' -File -ErrorAction SilentlyContinue |
    Remove-Item -Force
Copy-Item (Join-Path $root 'samples\*.conf') $dist -Force
Copy-Item (Join-Path $root 'samples\app.ico') $dist -Force
Copy-Item (Join-Path $root 'README.md') $dist -Force

if ($AppName) {
    $confTarget = Join-Path $dist "$AppName.conf"

    if ($AppIcon) {
        # A different icon has to be compiled in, so publish the wrapper again with
        # the icon and the app name baked into the exe.
        Step "Publishing $AppName.exe with $(Split-Path -Leaf $AppIcon) built in"
        $namedOut = Join-Path $root "artifacts\publish\$AppName"

        # The icon is compiled into the exe as a Win32 resource, and MSBuild decides
        # whether to recompile by comparing file timestamps only. Changing a property
        # does not count, so without clearing these the publish above would be reused
        # and the exe would keep the previous icon.
        foreach ($stale in @('obj\Release', 'bin\Release')) {
            $path = Join-Path $root "src\WebAppShield\$stale"
            if (Test-Path $path) { Remove-Item $path -Recurse -Force }
        }

        dotnet publish (Join-Path $root 'src\WebAppShield\WebAppShield.csproj') `
            --configuration Release `
            --runtime $Runtime `
            --self-contained true `
            --output $namedOut `
            --nologo `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:ApplicationIcon=$AppIcon

        if ($LASTEXITCODE -ne 0) { throw "Publishing $AppName failed." }

        # AssemblyName is deliberately left alone: overriding it makes NuGet restore
        # fail with "Ambiguous project name". The wrapper reads its own file name at
        # run time, so renaming the output is all that is needed.
        $namedExe = Join-Path $namedOut 'webappshield.exe'
        if (-not (Test-Path $namedExe)) { throw "Expected $namedExe but it was not produced." }
        Copy-Item $namedExe (Join-Path $dist "$AppName.exe") -Force

        # The window and tray icon are read from the .conf at run time, so the .ico
        # has to sit next to the exe as well.
        Copy-Item $AppIcon (Join-Path $dist "$AppName.ico") -Force
    }
    else {
        Step "Making a renamed copy: $AppName.exe"
        Copy-Item (Join-Path $dist 'webappshield.exe') (Join-Path $dist "$AppName.exe") -Force
    }

    if (-not (Test-Path $confTarget)) {
        Copy-Item (Join-Path $root 'samples\mstodo.conf') $confTarget -Force
    }

    if ($AppIcon) {
        # dist is build output, so the icon line is rewritten every time. Keep your
        # edited config somewhere else and copy it in.
        $text = Get-Content $confTarget -Raw
        $updated = $text -replace '(?m)^(\s*)"icon":\s*"[^"]*",', ('${1}"icon": "' + $AppName + '.ico",')
        if ($updated -eq $text) {
            Write-Warning "No icon line found in $confTarget. Add:  `"icon`": `"$AppName.ico`","
        }
        else {
            Set-Content -Path $confTarget -Value $updated -Encoding utf8 -NoNewline
            Write-Host "  $AppName.conf now points at $AppName.ico" -ForegroundColor DarkGray
        }
    }
}

Step 'Done'
Get-ChildItem $dist -File | Sort-Object Name |
    Select-Object Name, @{ Name = 'Size'; Expression = { '{0:N1} MB' -f ($_.Length / 1MB) } } |
    Format-Table -AutoSize

Write-Host "Output folder: $dist" -ForegroundColor Green
Write-Host ''
Write-Host 'Next steps:' -ForegroundColor Yellow
Write-Host '  1. Rename webappshield.exe to the name you want, e.g. mstodo.exe'
Write-Host '  2. Put mstodo.conf next to it and edit the settings'
Write-Host '  3. Optional: encode.exe "mstodo.conf" "Kopolop0_90u"   to encrypt it'
Write-Host '  4. Run mstodo.exe'
