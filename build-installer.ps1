param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Join-Path $rootDir "TeliLandOverlay"
$projectPath = Join-Path $projectDir "TeliLandOverlay.csproj"
$publishDir = Join-Path $projectDir "bin\$Configuration\net10.0-windows\$Runtime\publish"
$installerScript = Join-Path $rootDir "installer\TeliLandOverlay.iss"
$outputDir = Join-Path $rootDir "dist"

$isccCandidates = New-Object System.Collections.Generic.List[string]
$candidatePaths = @(
    (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

foreach ($candidatePath in $candidatePaths) {
    if ($candidatePath -and (Test-Path $candidatePath) -and -not $isccCandidates.Contains($candidatePath)) {
        $isccCandidates.Add($candidatePath)
    }
}

if (-not $isccCandidates) {
    throw "Inno Setup compiler was not found. Install Inno Setup 6 and run this script again."
}

$isccPath = $isccCandidates[0]

dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained true -p:PublishSingleFile=true

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

& $isccPath `
    "/DPublishDir=$publishDir" `
    "/DOutputDir=$outputDir" `
    "/DMyAppVersion=1.1.0" `
    $installerScript

if ($LASTEXITCODE -ne 0) {
    throw "Installer build failed."
}

Get-Item (Join-Path $outputDir "TeliLandOverlaySetup.exe")
