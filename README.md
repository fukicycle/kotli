# Kotli 設計書 v0.1

## インストールと使い方

### 前提条件

- [Ollama](https://ollama.com/) がローカル（デフォルト: `http://localhost:11434`）で起動していること
- 任意のモデル（例: `ollama pull gemma4:26b` または Tool Calling 対応モデル）が取得済みであること

---

### 1. インストール (`install.sh`)

Linux (`x64` / `arm64`)、macOS (`x64` / `arm64`)、Windows Git Bash / MSYS2 / WSL (`x64` / `arm64`) に対応しています。

#### ワンライナーインストール（GitHub Releases から自動取得）

```bash
curl -fsSL https://raw.githubusercontent.com/fukicycle/kotli/main/install.sh | bash
```

#### クローンしたリポジトリからインストール

```bash
# 最新リリースバイナリをダウンロードしてインストール（未リリース時は自動的にソースビルドへフォールバック）
./install.sh

# ローカルのソースコードからシングルファイルバイナリをビルドしてインストール (.NET 10.0 SDK が必要)
./install.sh --from-source

# バージョン・インストール先・ターゲットRIDを指定する場合
./install.sh --version v0.1.0 --install-dir ~/.local/bin --rid osx-arm64

# アンインストール
./install.sh --uninstall
```

| オプション | 説明 |
| :--- | :--- |
| `-v, --version <tag>` | インストールするバージョンを指定（デフォルト: `latest`） |
| `-d, --install-dir <path>` | インストール先ディレクトリを指定（デフォルト: `~/.local/bin`、root 実行時は `/usr/local/bin`） |
| `-r, --rid <rid>` | ターゲット RID を明示指定 (`linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`, `win-arm64`) |
| `-s, --from-source` | ローカルのソースコードから `dotnet publish` でビルドしてインストール |
| `-u, --uninstall` | インストール済みの `kotli` バイナリを削除 |
| `-h, --help` | ヘルプを表示 |

> [!TIP]
> `~/.local/bin` が `PATH` に含まれていない場合は、シェル設定ファイル（`~/.zshrc` や `~/.bashrc`）に以下を追加してください:
> ```bash
> export PATH="$HOME/.local/bin:$PATH"
> ```

---

### 2. 基本的な使い方

任意のプロジェクトディレクトリで `kotli` を実行します。

```bash
$ kotli
```

1. 起動時にローカルの Ollama から利用可能なモデル一覧を取得します。上下キーでモデルを選択して `Enter` を押します。
2. `kotli> ` プロンプトが表示されたら、自然言語で質問やコード変更・コマンド実行を依頼できます。

#### スラッシュコマンド & ショートカット一覧

| コマンド / 記法 | 説明 |
| :--- | :--- |
| `/help` | コマンドと記法のヘルプ一覧を表示します |
| `/model` | 使用する Ollama モデルを対話的に切り替えます |
| `/clear` | 画面と現在の会話セッション履歴をクリアします |
| `/memory` | セッションメモリ（`~/.kotli/<セッションID>/MEMORY.md`）の一覧を表示します |
| `/memory add <Key> <Content>` | セッションメモリに新しい項目を手動で保存します |
| `/diff` | 現在のワークスペースの未コミット変更（`git diff`）を表示します |
| `/exit` (または `exit`, `quit`) | Kotli CLI を終了します |
| `@path/to/file` | 指定したファイルをプロンプトに自動添付して LLM に送信します（`@` 入力でファイルパス補完が機能します） |
| `Ctrl+C` | 推論中・ツール実行中の処理を安全に中断します |

#### 内蔵ツールと承認フロー（Human-in-the-Loop）

LLM は必要に応じて以下のツールを自律的に呼び出します。ファイルを変更するツールやシェルコマンド実行ツールは、実行前にターミナル上でプレビューが表示され、ユーザーの承認（`今回のみ許可` / `このセッション中は常に許可` / `拒否`）を求めます。

| ツール名 | 承認要否 | 説明 |
| :--- | :---: | :--- |
| `read_file` | 不要 | ワークスペース内のファイルを行番号・範囲指定付きで読み取ります |
| `list_directory` | 不要 | ディレクトリ内のファイル・フォルダ一覧を取得します |
| `find_files` | 不要 | Glob パターンでワークスペース内のファイルを高速検索します |
| `grep_search` | 不要 | 正規表現でファイル内のテキストを横断検索します |
| `get_git_diff` | 不要 | 現在の Git 変更差分を取得します |
| `web_search` | 不要 | ウェブ検索を実行します |
| `save_memory` | 不要 | `~/.kotli/<セッションID>/MEMORY.md` に知識・規約を保存します |
| `write_file` | **要承認** | ファイルの新規作成・上書きを行います（事前に Diff プレビューを表示） |
| `replace_text` | **要承認** | ファイル内の特定箇所を部分置換します（事前に Diff プレビューを表示） |
| `execute_command` | **要承認** | ワークスペース内でシェルコマンド（ビルド・テスト等）を実行します |

#### セッションデータの保存場所（リポジトリ非汚染）

会話履歴（`history`）や長期記憶（`MEMORY.md`）はすべてホームディレクトリ配下の **`~/.kotli/<セッションID>/`** に保存されます。作業対象のリポジトリ内に `.kotli` フォルダ等が生成されることはありません。

---

## 1. コンセプト

### 目的

ローカルLLMを快適に利用するためのCLIアシスタント。

Gemini CLIやClaude Codeのような使用感を持ちながら、

* Ollama専用
* ローカルLLMに最適化
* 高速
* シンプル

を目指す。

---

## 2. 開発方針

### やらないこと

* GUI
* Electron
* Web UI
* ブラウザ
* VSCode Extension
* IDE依存

すべて排除する。

---

### やること

CLIのみ。

```
$ kotli
```

で起動できる。

Linux / macOS / Windows対応。

---

## 3. 設計思想

### 最優先

> 必要最低限のコンテキストしか送らない。

クラウドLLMのように

* 全プロジェクト
* 全履歴

を毎回送らない。

---

### ローカルLLMを信頼する

GPUもRAMも有限。

コンテキストは資源。

無駄遣いしない。

---

## 4. ゴール

ユーザーが

```
$ kotli
```

だけ実行すれば、

自然に会話できる。

---

## 5. システム構成

```
User

↓

CLI

↓

Session

↓

Context Builder

↓

Prompt Builder

↓

Ollama Client

↓

Ollama
```

---

## 6. モジュール

### CLI

責務

* 起動
* コマンド解析
* 対話ループ

他を知らない。

---

### Session

責務

会話管理。

保持するもの

* 会話履歴
* 現在モデル
* Context情報

---

### Context Builder

責務

LLMへ送る情報を収集する。

例

* 現在ディレクトリ
* Git情報
* 添付ファイル
* 会話履歴

Contextの優先順位を決める。

---

### Prompt Builder

責務

最終Promptを生成。

System Prompt

*

Context

*

History

*

User Input

↓

Prompt

---

### Ollama Client

責務

HTTP通信のみ。

知らなくていいもの

* Git
* CLI
* Prompt

---

## 7. ディレクトリ構成

```
Kotli

Application

Domain

Infrastructure

Commands

Models

Services

Configuration
```

---

## 8. コマンド

最初は最低限。

```
/help

/clear

/model

/exit
```

だけ。

増やさない。

---

## 9. Contextの優先順位

```
System Prompt

↓

Current Conversation

↓

Attached Files

↓

Git Diff

↓

Repository Information
```

Repository全体は最後。

---

## 10. Session

1 Session = 1 Conversation

チャットを変えればSessionも変わる。

---

## 11. Tool

v0.1では最低限。

```
Read File

Write File

List Directory
```

だけ。

Shell実行は後回し。

---

## 12. エラー設計

ユーザーに

```
Internal Error
```

を見せない。

必ず

* 理由
* 対処方法

を表示する。

---

## 13. パフォーマンス目標

起動

```
1秒以内
```

チャット開始

```
500ms以内
```

ストリーミング開始

```
1秒以内
```

---

## 14. 今後追加予定

対象外

* MCP
* Agent
* Workflow
* GUI
* Plugin

まずCLIを完成させる。

---

## 15. Kotliが大切にする価値

```
Simple

Fast

Predictable

Transparent
```

---

## 16. 実装しない機能

v0.1では作らない。

* 自動RAG
* 自動Embedding
* Vector DB
* Browser
* 自動検索
* マルチエージェント
* 自動コード修正
* IDE連携

必要になるまで実装しない。

---

# 最後に

一つだけ、設計として提案したいことがあります。

**「KotliはAIアプリではなく、ローカルLLMのOSである」**という考え方です。

つまり、OllamaはCPU、モデルはプロセス、KVキャッシュはメモリ、セッションはターミナルのような存在です。

その視点に立つと、

* セッション管理
* メモリ管理
* コンテキスト管理
* モデル管理

はすべて一貫した設計になります。


