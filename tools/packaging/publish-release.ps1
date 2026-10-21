<#
.SYNOPSIS
  Builds the release package SqlExpert-<version>-win-x64.zip (self-contained single-file exe),
  writes <zip>.sha256 next to it and prints the "SHA256: <hex>" line for the release notes.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\packaging\publish-release.ps1
  powershell -ExecutionPolicy Bypass -File tools\packaging\publish-release.ps1 -OutputDir C:\temp\publish
#>
param(
    [string]$OutputDir = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'publish')
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$csproj = Join-Path $repo 'SqlExpert.App\SqlExpert.App.csproj'
$version = ([xml](Get-Content -Raw $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Versão não encontrada em $csproj" }

$name = "SqlExpert-$version-win-x64"
$folder = Join-Path $OutputDir $name
$zip = Join-Path $OutputDir "$name.zip"
if (Test-Path $folder) { Remove-Item -Recurse -Force $folder }
if (Test-Path $zip) { Remove-Item -Force $zip }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

dotnet publish (Join-Path $repo 'SqlExpert.App') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $folder
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou ($LASTEXITCODE)" }
if (-not (Test-Path (Join-Path $folder 'SqlExpert.exe'))) { throw 'SqlExpert.exe não foi gerado' }

# Files at the zip root (SqlExpert.exe + .pdb); the updater accepts the root or one top-level folder.
Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
$size = (Get-Item $zip).Length
"$hash  $name.zip" | Set-Content -Encoding ascii -NoNewline (Join-Path $OutputDir "$name.zip.sha256")

Write-Host ''
Write-Host "Pacote:  $zip"
Write-Host "Tamanho: $size bytes"
Write-Host "Hash:    $zip.sha256"
Write-Host ''
Write-Host 'Linha para as notas da Release:'
Write-Host "SHA256: $hash"
