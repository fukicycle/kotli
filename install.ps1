<#
.SYNOPSIS
    Kotli Cross-Platform Installer for Windows (PowerShell 5.1 / PowerShell 7+)

.DESCRIPTION
    Downloads and installs the Kotli single-file CLI binary from GitHub Releases,
    or builds it from source using the .NET 10.0 SDK.

.EXAMPLE
    irm https://raw.githubusercontent.com/fukicycle/kotli/main/install.ps1 | iex

.EXAMPLE
    .\install.ps1
    .\install.ps1 -FromSource
    .\install.ps1 -Version v0.1.0 -InstallDir "$HOME\.local\bin" -Rid win-x64
    .\install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [Alias("v")]
    [string]$Version = $(if ($env:KOTLI_VERSION) { $env:KOTLI_VERSION } else { "latest" }),

    [Alias("d")]
    [string]$InstallDir = $(if ($env:KOTLI_INSTALL_DIR) { $env:KOTLI_INSTALL_DIR } else { Join-Path $HOME ".local\bin" }),

    [Alias("r")]
    [string]$Rid = $(if ($env:KOTLI_RID) { $env:KOTLI_RID } else { "" }),

    [Alias("s")]
    [switch]$FromSource = $(if ($env:KOTLI_FROM_SOURCE -in @("true", "1")) { $true } else { $false }),

    [Alias("u")]
    [switch]$Uninstall = $(if ($env:KOTLI_UNINSTALL -in @("true", "1")) { $true } else { $false }),

    [Alias("h")]
    [switch]$Help
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$KotliRepo = if ($env:KOTLI_REPO) { $env:KOTLI_REPO } else { "fukicycle/kotli" }

function Write-Info([string]$Message) { Write-Host "[INFO] $Message" -ForegroundColor Cyan }
function Write-Ok([string]$Message)   { Write-Host "[OK] $Message" -ForegroundColor Green }
function Write-Warn([string]$Message) { Write-Host "[WARN] $Message" -ForegroundColor Yellow }
function Exit-WithError([string]$Message) {
    Write-Host "[ERROR] $Message" -ForegroundColor Red
    throw $Message
}

function Show-Usage {
    Write-Host @"
Kotli Installer for Windows (PowerShell)

Usage:
  irm https://raw.githubusercontent.com/$KotliRepo/main/install.ps1 | iex
  .\install.ps1 [options]

Options:
  -Version, -v <tag>        インストールするバージョンを指定 (例: v0.1.0, デフォルト: latest)
  -InstallDir, -d <path>    インストール先ディレクトリを指定 (デフォルト: `$HOME\.local\bin)
  -Rid, -r <rid>            ターゲットRIDを明示指定 (win-x64, win-arm64)
  -FromSource, -s           ソースコードから dotnet publish でビルドしてインストール
  -Uninstall, -u            インストール済みの kotli.exe をアンインストール
  -Help, -h                 このヘルプを表示
"@
}

if ($Help) {
    Show-Usage
    return
}

function Get-TargetRid {
    if (-not [string]::IsNullOrWhiteSpace($Rid)) {
        return $Rid
    }

    $arch = "x64"
    try {
        $osArch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        if ($osArch -match "Arm64") {
            $arch = "arm64"
        }
    } catch {
        if ($env:PROCESSOR_ARCHITEW6432 -eq "ARM64" -or $env:PROCESSOR_ARCHITECTURE -eq "ARM64") {
            $arch = "arm64"
        }
    }

    return "win-$arch"
}

function Resolve-LatestTag {
    $apiUrl = "https://api.github.com/repos/$KotliRepo/releases/latest"
    try {
        $release = Invoke-RestMethod -Uri $apiUrl -Headers @{ "User-Agent" = "kotli-installer" } -Method Get
        if ($release -and $release.tag_name) {
            return [string]$release.tag_name
        }
    } catch {
        # Fallback to checking redirect URL
    }

    try {
        $req = [System.Net.WebRequest]::Create("https://github.com/$KotliRepo/releases/latest")
        $req.AllowAutoRedirect = $true
        $resp = $req.GetResponse()
        $finalUrl = $resp.ResponseUri.AbsoluteUri
        $resp.Close()
        if ($finalUrl -match "/releases/tag/([^/]+)") {
            return $Matches[1]
        }
    } catch {
        # No release found
    }

    return ""
}

function Resolve-LocalCsproj {
    if ($PSScriptRoot) {
        $candidate = Join-Path $PSScriptRoot "src\Kotli.Cli\Kotli.Cli.csproj"
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    $cwdCandidate = Join-Path (Get-Location).Path "src\Kotli.Cli\Kotli.Cli.csproj"
    if (Test-Path $cwdCandidate) {
        return $cwdCandidate
    }

    return ""
}

function Add-ToUserPath([string]$DirPath) {
    $normalizedTarget = $DirPath.TrimEnd('\', '/')

    # Add to current session PATH
    $sessionPaths = ($env:PATH -split ';') | ForEach-Object { $_.TrimEnd('\', '/') }
    if ($sessionPaths -notcontains $normalizedTarget) {
        $env:PATH = "$DirPath;$env:PATH"
    }

    # Add to persistent User PATH on Windows
    try {
        $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
        $userPaths = if ($userPath) { ($userPath -split ';') | ForEach-Object { $_.TrimEnd('\', '/') } } else { @() }
        if ($userPaths -notcontains $normalizedTarget) {
            $newUserPath = if ([string]::IsNullOrWhiteSpace($userPath)) { $DirPath } else { "$DirPath;$userPath" }
            [Environment]::SetEnvironmentVariable("Path", $newUserPath, "User")
            Write-Ok "ユーザー環境変数 PATH に '$DirPath' を追加しました。"
        }
    } catch {
        Write-Warn "環境変数 PATH の自動更新をスキップしました。手動で '$DirPath' を PATH に追加してください。"
    }
}

function Verify-Checksum([string]$ArchivePath, [string]$ChecksumsPath) {
    if (-not (Test-Path $ChecksumsPath)) {
        return
    }

    $archiveName = Split-Path $ArchivePath -Leaf
    $expected = ""
    foreach ($line in Get-Content $ChecksumsPath) {
        if ($line -match "^([0-9a-fA-F]{64})\s+\*?$([Regex]::Escape($archiveName))$") {
            $expected = $Matches[1].ToLowerInvariant()
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($expected)) {
        Write-Warn "checksums.txt 内に $archiveName のエントリが見つかりませんでした。検証をスキップします。"
        return
    }

    $actual = (Get-FileHash -Path $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -ne $actual) {
        Exit-WithError "SHA-256 チェックサムが一致しません! (期待値: $expected, 実際値: $actual)"
    }

    Write-Ok "SHA-256 チェックサム検証に成功しました ($($actual.Substring(0, 16))...)"
}

function Install-FromSource([string]$TargetRid, [string]$TargetInstallDir, [string]$BinName, [string]$TempWorkDir) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Exit-WithError ".NET SDK (dotnet) が見つかりません。ソースからビルドするには .NET 10.0 SDK をインストールしてください。"
    }

    $csproj = Resolve-LocalCsproj
    if ([string]::IsNullOrWhiteSpace($csproj)) {
        if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
            Exit-WithError "ローカルのソースが見つからず、git もインストールされていません。"
        }
        Write-Info "GitHub ($KotliRepo) からソースコードを取得しています..."
        $cloneDir = Join-Path $TempWorkDir "repo"
        if ($Version -ne "latest") {
            & git clone --depth 1 --branch $Version "https://github.com/$KotliRepo.git" $cloneDir
        } else {
            & git clone --depth 1 "https://github.com/$KotliRepo.git" $cloneDir
        }
        if ($LASTEXITCODE -ne 0) {
            Exit-WithError "リポジトリのクローンに失敗しました。"
        }
        $csproj = Join-Path $cloneDir "src\Kotli.Cli\Kotli.Cli.csproj"
    }

    $publishDir = Join-Path $TempWorkDir "publish"
    Write-Info "ソースコードから $TargetRid 向けにシングルファイルバイナリをビルドしています..."

    & dotnet publish $csproj `
        --configuration Release `
        --runtime $TargetRid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        --output $publishDir

    if ($LASTEXITCODE -ne 0) {
        Exit-WithError "dotnet publish に失敗しました。"
    }

    New-Item -ItemType Directory -Force -Path $TargetInstallDir | Out-Null
    Copy-Item -Path (Join-Path $publishDir $BinName) -Destination (Join-Path $TargetInstallDir $BinName) -Force
}

function Install-FromRelease([string]$TargetRid, [string]$TargetInstallDir, [string]$BinName, [string]$TempWorkDir) {
    $tag = $Version
    if ($tag -eq "latest") {
        Write-Info "GitHub Releases ($KotliRepo) から最新バージョンを確認しています..."
        $tag = Resolve-LatestTag
        if ([string]::IsNullOrWhiteSpace($tag)) {
            $hasDotnet = [bool](Get-Command dotnet -ErrorAction SilentlyContinue)
            $hasSourceOrGit = (-not [string]::IsNullOrWhiteSpace((Resolve-LocalCsproj))) -or [bool](Get-Command git -ErrorAction SilentlyContinue)
            if ($hasDotnet -and $hasSourceOrGit) {
                Write-Warn "公開済みの GitHub Release が見つかりませんでした。ソースからのビルド (-FromSource) に自動フォールバックします。"
                Install-FromSource -TargetRid $TargetRid -TargetInstallDir $TargetInstallDir -BinName $BinName -TempWorkDir $TempWorkDir
                return
            }
            Exit-WithError "GitHub Releases から最新バージョンを取得できませんでした。ローカルからビルドする場合は .NET 10.0 SDK を導入の上 -FromSource を指定してください。"
        }
    }

    $archiveName = "kotli-$TargetRid.zip"
    $baseUrl = "https://github.com/$KotliRepo/releases/download/$tag"
    $archiveUrl = "$baseUrl/$archiveName"
    $checksumsUrl = "$baseUrl/checksums.txt"

    $archivePath = Join-Path $TempWorkDir $archiveName
    $checksumsPath = Join-Path $TempWorkDir "checksums.txt"

    Write-Info "Kotli $tag ($TargetRid) をダウンロードしています..."
    Write-Info "URL: $archiveUrl"
    try {
        Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath -UseBasicParsing
    } catch {
        Exit-WithError "アーカイブのダウンロードに失敗しました: $archiveUrl ($($_.Exception.Message))"
    }

    try {
        Invoke-WebRequest -Uri $checksumsUrl -OutFile $checksumsPath -UseBasicParsing
        Verify-Checksum -ArchivePath $archivePath -ChecksumsPath $checksumsPath
    } catch {
        # checksums.txt optional if unavailable
    }

    Write-Info "アーカイブを展開しています..."
    $extractDir = Join-Path $TempWorkDir "extract"
    Expand-Archive -Path $archivePath -DestinationPath $extractDir -Force

    $extractedBin = Join-Path $extractDir $BinName
    if (-not (Test-Path $extractedBin)) {
        Exit-WithError "アーカイブ内に実行ファイル '$BinName' が見つかりませんでした。"
    }

    New-Item -ItemType Directory -Force -Path $TargetInstallDir | Out-Null
    Copy-Item -Path $extractedBin -Destination (Join-Path $TargetInstallDir $BinName) -Force
}

$targetRid = Get-TargetRid
$binName = if ($targetRid -like "win-*") { "kotli.exe" } else { "kotli" }
$targetPath = Join-Path $InstallDir $binName

if ($Uninstall) {
    if (Test-Path $targetPath) {
        Remove-Item -Path $targetPath -Force
        Write-Ok "Kotli をアンインストールしました: $targetPath"
    } else {
        Write-Warn "インストール済みのバイナリが見つかりません: $targetPath"
    }
    return
}

Write-Info "ターゲットプラットフォーム: $targetRid"
Write-Info "インストール先: $targetPath"

$tempWorkDir = Join-Path ([System.IO.Path]::GetTempPath()) ("kotli_install_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tempWorkDir | Out-Null

try {
    if ($FromSource) {
        Install-FromSource -TargetRid $targetRid -TargetInstallDir $InstallDir -BinName $binName -TempWorkDir $tempWorkDir
    } else {
        Install-FromRelease -TargetRid $targetRid -TargetInstallDir $InstallDir -BinName $binName -TempWorkDir $tempWorkDir
    }

    Add-ToUserPath -DirPath $InstallDir
    Write-Ok "Kotli のインストールが完了しました! -> $targetPath"
    Write-Host "`nすぐに実行できます:`n  PS> kotli`n" -ForegroundColor Green
} finally {
    if (Test-Path $tempWorkDir) {
        Remove-Item -Path $tempWorkDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
