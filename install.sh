#!/usr/bin/env bash
set -euo pipefail

# ==============================================================================
# Kotli Cross-Platform Installer (Linux / macOS / Windows Git Bash & MSYS2 & WSL)
# ==============================================================================

KOTLI_REPO="${KOTLI_REPO:-fukicycle/kotli}"
KOTLI_VERSION="${KOTLI_VERSION:-latest}"
KOTLI_INSTALL_DIR="${KOTLI_INSTALL_DIR:-}"
KOTLI_RID="${KOTLI_RID:-}"
FROM_SOURCE=false
UNINSTALL=false
TMP_WORK_DIR=""

cleanup() {
    if [[ -n "${TMP_WORK_DIR:-}" && -d "${TMP_WORK_DIR}" ]]; then
        rm -rf "${TMP_WORK_DIR}"
    fi
}
trap cleanup EXIT

# カラー出力定義
if [ -t 1 ]; then
    RED=$'\033[0;31m'
    GREEN=$'\033[0;32m'
    YELLOW=$'\033[1;33m'
    CYAN=$'\033[0;36m'
    BOLD=$'\033[1m'
    RESET=$'\033[0m'
else
    RED=''
    GREEN=''
    YELLOW=''
    CYAN=''
    BOLD=''
    RESET=''
fi

info()  { printf "%s[INFO]%s %s\n" "${CYAN}" "${RESET}" "$*"; }
ok()    { printf "%s[OK]%s %s\n" "${GREEN}" "${RESET}" "$*"; }
warn()  { printf "%s[WARN]%s %s\n" "${YELLOW}" "${RESET}" "$*" >&2; }
err()   { printf "%s[ERROR]%s %s\n" "${RED}" "${RESET}" "$*" >&2; exit 1; }

usage() {
    cat <<EOF
${BOLD}Kotli Installer${RESET}

Usage:
  ./install.sh [options]
  curl -fsSL https://raw.githubusercontent.com/${KOTLI_REPO}/main/install.sh | bash

Options:
  -v, --version <tag>       インストールするバージョンを指定 (例: v0.1.0, デフォルト: latest)
  -d, --install-dir <path>  インストール先ディレクトリを指定 (デフォルト: ~/.local/bin または /usr/local/bin)
  -r, --rid <rid>           ターゲットRIDを明示指定 (linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64, win-arm64)
  -s, --from-source         ローカルのソースコードから dotnet publish でビルドしてインストール
  -u, --uninstall           インストール済みの kotli バイナリをアンインストール
  -h, --help                このヘルプを表示
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -v|--version)
            [[ $# -ge 2 ]] || err "--version には引数が必要です"
            KOTLI_VERSION="$2"
            shift 2
            ;;
        -d|--install-dir)
            [[ $# -ge 2 ]] || err "--install-dir には引数が必要です"
            KOTLI_INSTALL_DIR="$2"
            shift 2
            ;;
        -r|--rid)
            [[ $# -ge 2 ]] || err "--rid には引数が必要です"
            KOTLI_RID="$2"
            shift 2
            ;;
        -s|--from-source)
            FROM_SOURCE=true
            shift
            ;;
        -u|--uninstall)
            UNINSTALL=true
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            err "不明なオプションです: $1 (--help を参照してください)"
            ;;
    esac
done

detect_platform() {
    local uname_s uname_m os arch
    uname_s="$(uname -s 2>/dev/null || echo Unknown)"
    uname_m="$(uname -m 2>/dev/null || echo Unknown)"

    case "${uname_s}" in
        Linux*)
            os="linux"
            ;;
        Darwin*)
            os="osx"
            ;;
        MINGW*|MSYS*|CYGWIN*|Windows_NT*)
            os="win"
            ;;
        *)
            err "サポートされていないOSです: ${uname_s} (対応OS: Linux, macOS, Windows)"
            ;;
    esac

    case "${uname_m}" in
        x86_64|amd64|x64)
            arch="x64"
            ;;
        aarch64|arm64|armv8*)
            arch="arm64"
            ;;
        *)
            err "サポートされていないCPUアーキテクチャです: ${uname_m} (対応Arch: x64, arm64)"
            ;;
    esac

    echo "${os}-${arch}"
}

resolve_install_dir() {
    if [[ -n "${KOTLI_INSTALL_DIR}" ]]; then
        echo "${KOTLI_INSTALL_DIR}"
        return
    fi

    # root 実行時は /usr/local/bin、一般ユーザーは ~/.local/bin を優先
    if [[ "$(id -u 2>/dev/null || echo 1000)" -eq 0 ]] && [[ -d "/usr/local/bin" ]]; then
        echo "/usr/local/bin"
    else
        echo "${HOME}/.local/bin"
    fi
}

binary_name_for_rid() {
    local rid="$1"
    if [[ "${rid}" == win-* ]]; then
        echo "kotli.exe"
    else
        echo "kotli"
    fi
}

verify_checksum() {
    local archive_path="$1"
    local checksums_path="$2"
    local archive_name
    archive_name="$(basename "${archive_path}")"

    if [[ ! -f "${checksums_path}" ]]; then
        return 0
    fi

    local expected
    expected="$(grep " ${archive_name}\$" "${checksums_path}" | awk '{print $1}' || true)"
    if [[ -z "${expected}" ]]; then
        warn "checksums.txt 内に ${archive_name} のエントリが見つかりませんでした。検証をスキップします。"
        return 0
    fi

    local actual=""
    if command -v sha256sum >/dev/null 2>&1; then
        actual="$(sha256sum "${archive_path}" | awk '{print $1}')"
    elif command -v shasum >/dev/null 2>&1; then
        actual="$(shasum -a 256 "${archive_path}" | awk '{print $1}')"
    else
        warn "sha256sum / shasum が見つからないためチェックサム検証をスキップします。"
        return 0
    fi

    if [[ "${expected}" != "${actual}" ]]; then
        err "SHA-256 チェックサムが一致しません! (期待値: ${expected}, 実際値: ${actual})"
    fi
    ok "SHA-256 チェックサム検証に成功しました (${actual:0:16}...)"
}

download_file() {
    local url="$1"
    local dest="$2"
    if command -v curl >/dev/null 2>&1; then
        curl -fsSL "${url}" -o "${dest}"
    elif command -v wget >/dev/null 2>&1; then
        wget -qO "${dest}" "${url}"
    else
        err "ダウンロードに必要な curl または wget がインストールされていません。"
    fi
}

resolve_latest_tag() {
    local api_url="https://api.github.com/repos/${KOTLI_REPO}/releases/latest"
    local response=""
    if command -v curl >/dev/null 2>&1; then
        response="$(curl -fsSL "${api_url}" 2>/dev/null || true)"
    elif command -v wget >/dev/null 2>&1; then
        response="$(wget -qO- "${api_url}" 2>/dev/null || true)"
    fi

    if [[ -z "${response}" ]]; then
        echo ""
        return
    fi

    echo "${response}" | grep -Eo '"tag_name"\s*:\s*"[^"]+"' | head -n1 | sed -E 's/.*"([^"]+)"/\1/'
}

install_from_source() {
    local rid="$1"
    local install_dir="$2"
    local bin_name="$3"
    local script_dir
    script_dir="$(cd "$(dirname "${BASH_SOURCE[0]:-.}")" && pwd)"

    local csproj="${script_dir}/src/Kotli.Cli/Kotli.Cli.csproj"
    if [[ ! -f "${csproj}" ]]; then
        err "ローカルのソースプロジェクトが見つかりません: ${csproj}"
    fi

    if ! command -v dotnet >/dev/null 2>&1; then
        err ".NET SDK (dotnet) が見つかりません。ソースからビルドするには .NET 10.0 SDK をインストールしてください。"
    fi

    info "ローカルソースから ${rid} 向けにシングルファイルバイナリをビルドしています..."
    TMP_WORK_DIR="$(mktemp -d)"

    dotnet publish "${csproj}" \
        --configuration Release \
        --runtime "${rid}" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:DebugType=None \
        -p:DebugSymbols=false \
        --output "${TMP_WORK_DIR}"

    mkdir -p "${install_dir}"
    cp "${TMP_WORK_DIR}/${bin_name}" "${install_dir}/${bin_name}"
    chmod +x "${install_dir}/${bin_name}" || true

    if [[ "${rid}" == osx-* ]] && command -v xattr >/dev/null 2>&1; then
        xattr -d com.apple.quarantine "${install_dir}/${bin_name}" 2>/dev/null || true
    fi
}

install_from_release() {
    local rid="$1"
    local install_dir="$2"
    local bin_name="$3"
    local tag="${KOTLI_VERSION}"

    if [[ "${tag}" == "latest" ]]; then
        info "GitHub Releases (${KOTLI_REPO}) から最新バージョンを確認しています..."
        tag="$(resolve_latest_tag)"
        if [[ -z "${tag}" ]]; then
            local script_dir
            script_dir="$(cd "$(dirname "${BASH_SOURCE[0]:-.}")" 2>/dev/null && pwd || pwd)"
            if [[ -f "${script_dir}/src/Kotli.Cli/Kotli.Cli.csproj" ]] && command -v dotnet >/dev/null 2>&1; then
                warn "GitHub Release が見つかりませんでした。ローカルソースからのビルド (--from-source) にフォールバックします。"
                install_from_source "${rid}" "${install_dir}" "${bin_name}"
                return
            fi
            err "GitHub Releases から最新バージョンを取得できませんでした。ローカルからビルドする場合は --from-source を指定してください。"
        fi
    fi

    local archive_name="kotli-${rid}.tar.gz"
    local base_url="https://github.com/${KOTLI_REPO}/releases/download/${tag}"
    local archive_url="${base_url}/${archive_name}"
    local checksums_url="${base_url}/checksums.txt"

    TMP_WORK_DIR="$(mktemp -d)"

    info "Kotli ${tag} (${rid}) をダウンロードしています..."
    info "URL: ${archive_url}"
    if ! download_file "${archive_url}" "${TMP_WORK_DIR}/${archive_name}"; then
        err "アーカイブのダウンロードに失敗しました: ${archive_url}"
    fi

    if download_file "${checksums_url}" "${TMP_WORK_DIR}/checksums.txt" 2>/dev/null; then
        verify_checksum "${TMP_WORK_DIR}/${archive_name}" "${TMP_WORK_DIR}/checksums.txt"
    fi

    info "アーカイブを展開しています..."
    tar -xzf "${TMP_WORK_DIR}/${archive_name}" -C "${TMP_WORK_DIR}"

    if [[ ! -f "${TMP_WORK_DIR}/${bin_name}" ]]; then
        err "アーカイブ内に実行ファイル '${bin_name}' が見つかりませんでした。"
    fi

    mkdir -p "${install_dir}"
    mv "${TMP_WORK_DIR}/${bin_name}" "${install_dir}/${bin_name}"
    chmod +x "${install_dir}/${bin_name}" || true

    # macOS Gatekeeper 隔離属性の解除
    if [[ "${rid}" == osx-* ]] && command -v xattr >/dev/null 2>&1; then
        xattr -d com.apple.quarantine "${install_dir}/${bin_name}" 2>/dev/null || true
    fi
}

main() {
    local rid="${KOTLI_RID}"
    if [[ -z "${rid}" ]]; then
        rid="$(detect_platform)"
    fi

    local install_dir
    install_dir="$(resolve_install_dir)"
    local bin_name
    bin_name="$(binary_name_for_rid "${rid}")"
    local target_path="${install_dir}/${bin_name}"

    if [[ "${UNINSTALL}" == "true" ]]; then
        if [[ -f "${target_path}" ]]; then
            rm -f "${target_path}"
            ok "Kotli をアンインストールしました: ${target_path}"
        else
            warn "インストール済みのバイナリが見つかりません: ${target_path}"
        fi
        exit 0
    fi

    info "ターゲットプラットフォーム: ${BOLD}${rid}${RESET}"
    info "インストール先: ${BOLD}${target_path}${RESET}"

    if [[ "${FROM_SOURCE}" == "true" ]]; then
        install_from_source "${rid}" "${install_dir}" "${bin_name}"
    else
        install_from_release "${rid}" "${install_dir}" "${bin_name}"
    fi

    ok "Kotli のインストールが完了しました! -> ${target_path}"

    # PATH チェックと案内
    case ":${PATH}:" in
        *":${install_dir}:"*)
            printf "\n%s%sすぐに実行できます:%s\n  $ kotli\n\n" "${GREEN}" "${BOLD}" "${RESET}"
            ;;
        *)
            printf "\n%s[注意]%s %s%s%s が PATH に含まれていません。\n" "${YELLOW}" "${RESET}" "${BOLD}" "${install_dir}" "${RESET}"
            printf "以下をお使いのシェル設定ファイル (~/.zshrc, ~/.bashrc など) に追加してください:\n\n"
            printf "  %sexport PATH=\"%s:\$PATH\"%s\n\n" "${CYAN}" "${install_dir}" "${RESET}"
            ;;
    esac
}

main
