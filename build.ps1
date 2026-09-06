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
Copy-Item (Join-Path $root 'samples\*.conf') $dist -Force
Copy-Item (Join-Path $root 'samples\app.ico') $dist -Force
Copy-Item (Join-Path $root 'README.md') $dist -Force

if ($AppName) {
    Step "Making a renamed copy: $AppName.exe"
    Copy-Item (Join-Path $dist 'webappshield.exe') (Join-Path $dist "$AppName.exe") -Force

    $confSource = Join-Path $root 'samples\mstodo.conf'
    $confTarget = Join-Path $dist "$AppName.conf"
    if (-not (Test-Path $confTarget)) { Copy-Item $confSource $confTarget -Force }
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
