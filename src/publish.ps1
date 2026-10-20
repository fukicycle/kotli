param(
    [string]$OutBase = ""
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$CliProject = Join-Path $RepoRoot "src\Kotli.Cli\Kotli.Cli.csproj"

if ([string]::IsNullOrWhiteSpace($OutBase)) {
    $OutBase = Join-Path $RepoRoot "artifacts"
}

$Rids = @(
    "linux-x64",
    "linux-arm64",
    "osx-x64",
    "osx-arm64",
    "win-x64",
    "win-arm64"
)

foreach ($rid in $Rids) {
    Write-Host "==> Publishing Kotli for $rid..." -ForegroundColor Cyan
    & dotnet publish $CliProject `
        --configuration Release `
        --runtime $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        --output (Join-Path $OutBase $rid)
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $rid"
    }
}

Write-Host "All platform binaries published successfully to $OutBase" -ForegroundColor Green
