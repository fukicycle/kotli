# Kotli 設計書 v0.1

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


