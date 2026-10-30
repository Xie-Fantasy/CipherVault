[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.15',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$portableDirectory = Join-Path $repoRoot 'artifacts\portable\win-x64'
$liteDirectory = Join-Path $repoRoot 'artifacts\lite\win-x64'
$installerProject = Join-Path $repoRoot 'installer\CipherVault.Installer.wixproj'
$appProject = Join-Path $repoRoot 'src\CipherVault.App\CipherVault.App.csproj'
$solution = Join-Path $repoRoot 'CipherVault.sln'

if (-not $SkipTests) {
    dotnet test $solution -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }
}

if (Test-Path -LiteralPath $portableDirectory) {
    $resolvedArtifacts = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
    $resolvedPortable = [IO.Path]::GetFullPath($portableDirectory)
    if (-not $resolvedPortable.StartsWith($resolvedArtifacts, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean unexpected path: $resolvedPortable"
    }
    Remove-Item -LiteralPath $resolvedPortable -Recurse -Force
}

if (Test-Path -LiteralPath $liteDirectory) {
    $resolvedArtifacts = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
    $resolvedLite = [IO.Path]::GetFullPath($liteDirectory)
    if (-not $resolvedLite.StartsWith($resolvedArtifacts, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean unexpected path: $resolvedLite"
    }
    Remove-Item -LiteralPath $resolvedLite -Recurse -Force
}

dotnet publish $appProject `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $liteDirectory
if ($LASTEXITCODE -ne 0) { throw 'Lite publish failed.' }

dotnet publish $appProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $portableDirectory
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }

dotnet build $installerProject -c Release -p:ProductVersion=$Version
if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }

$portableExe = Join-Path $portableDirectory 'CipherVault.exe'
$liteExe = Join-Path $liteDirectory 'CipherVault.exe'
$msi = Get-ChildItem (Join-Path $repoRoot 'artifacts\installer') `
    -Filter "CipherVault-Setup-$Version-win-x64.msi" | Select-Object -First 1
if ($null -eq $msi) { throw 'MSI output was not found.' }

Write-Host ''
Write-Host 'Build completed:' -ForegroundColor Green
Write-Host "  Lite: $liteExe"
Write-Host "  Portable: $portableExe"
Write-Host "  Installer: $($msi.FullName)"
Get-FileHash $liteExe, $portableExe, $msi.FullName -Algorithm SHA256 | Format-Table Path, Hash -AutoSize
