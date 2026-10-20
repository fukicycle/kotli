using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kotli.Domain.Models;
using Kotli.Infrastructure.Ollama;
using Kotli.Infrastructure.Storage;
using Kotli.Infrastructure.Workspace;
using Xunit;

namespace Kotli.Tests;

public class InfrastructureServicesTests : IDisposable
{
    private readonly string _tempDir;

    public InfrastructureServicesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "kotli_infra_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.None
            };
            foreach (var entry in Directory.EnumerateFileSystemEntries(_tempDir, "*", options))
            {
                try
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(entry, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                    File.SetAttributes(entry, FileAttributes.Normal);
                }
                catch { }
            }
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task MarkdownMemoryStoreがメモリの保存と上書きと削除およびエラーハンドリングを正しく行うこと()
    {
        // デフォルトコンストラクタが ~/.kotli/<SessionId>/MEMORY.md を指し、リポジトリを汚染しないことの確認
        var defaultStore = new MarkdownMemoryStore();
        Assert.NotEmpty(defaultStore.SessionId);
        var expectedHomeKotli = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kotli", defaultStore.SessionId, "MEMORY.md");
        Assert.Equal(expectedHomeKotli, defaultStore.FilePath);

        var memRootDir = Path.Combine(_tempDir, "kotli_home");
        var store = new MarkdownMemoryStore("session-123", memRootDir);
        Assert.Equal("session-123", store.SessionId);
        Assert.Equal(Path.Combine(memRootDir, "session-123", "MEMORY.md"), store.FilePath);

        // 1. ファイル未存在時は空を返すこと
        Assert.Empty(await store.LoadAllAsync());

        // 2. SaveAsync による新規作成・複数項目保存・同一キー（大文字小文字無視）の上書き
        await store.SaveAsync(new MemoryItem("Rule1", "First rule", DateTime.UtcNow));
        await store.SaveAsync(new MemoryItem("Rule2", "Second rule\nLine 2", DateTime.UtcNow));
        await store.SaveAsync(new MemoryItem("rule1", "Updated first rule", DateTime.UtcNow));

        var loaded = (await store.LoadAllAsync()).ToList();
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, i => i.Key == "rule1" && i.Content == "Updated first rule");
        Assert.Contains(loaded, i => i.Key == "Rule2" && i.Content.Contains("Second rule"));

        // 3. DeleteAsync（存在するキーの削除・存在しないキーの削除）
        await store.DeleteAsync("non_existent_key");
        Assert.Equal(2, (await store.LoadAllAsync()).Count());

        await store.DeleteAsync("RULE1");
        var afterDelete = (await store.LoadAllAsync()).ToList();
        Assert.Single(afterDelete);
        Assert.Equal("Rule2", afterDelete[0].Key);

        // 4. SaveMemoryToolAsync（空キーエラー・nullコンテンツ許容・正常保存）
        Assert.Contains("[エラー] メモリのキー名が指定されていません", await store.SaveMemoryToolAsync("   ", "val"));
        var saveToolMsg = await store.SaveMemoryToolAsync("ToolKey", null!);
        Assert.Contains("[成功]", saveToolMsg);
        Assert.Contains("~/.kotli/session-123/MEMORY.md", saveToolMsg);

        // 5. 読み込み権限がない場合の LoadAllAsync 例外キャッチ分岐
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(store.FilePath, UnixFileMode.None);
            Assert.Empty(await store.LoadAllAsync());
            File.SetUnixFileMode(store.FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        // 6. MEMORY.mdと同名のディレクトリが存在する場合の SaveMemoryToolAsync 例外キャッチ分岐
        var brokenRootDir = Path.Combine(_tempDir, "broken_mem");
        Directory.CreateDirectory(Path.Combine(brokenRootDir, "broken-session", "MEMORY.md"));
        var brokenStore = new MarkdownMemoryStore("broken-session", brokenRootDir);
        var failResult = await brokenStore.SaveMemoryToolAsync("key", "value");
        Assert.Contains("[エラー] メモリの保存に失敗しました", failResult);
    }

    [Fact]
    public async Task GitDiffProviderがGitリポジトリの状態取得と差分取得および非Git環境でのフォールバックを正しく処理すること()
    {
        // デフォルトコンストラクタのカバレッジ確認
        _ = new GitDiffProvider();

        // 1. 存在しないディレクトリ（process.Start失敗時のキャッチ分岐）
        var nonExistentProvider = new GitDiffProvider(Path.Combine(_tempDir, "does_not_exist"));
        var nonExistentState = await nonExistentProvider.GetCurrentStateAsync();
        Assert.Equal("non-git", nonExistentState.CurrentBranch);

        // 2. Git管理外の通常ディレクトリ
        var nonGitDir = Path.Combine(_tempDir, "plain_dir");
        Directory.CreateDirectory(nonGitDir);
        var nonGitProvider = new GitDiffProvider(nonGitDir);
        var nonGitState = await nonGitProvider.GetCurrentStateAsync();
        Assert.Equal("non-git", nonGitState.CurrentBranch);

        // 3. 実際のGitリポジトリを初期化してブランチ・変更ファイル・未追跡ファイル・Diffを検証
        var gitRepoDir = Path.Combine(_tempDir, "git_repo");
        Directory.CreateDirectory(gitRepoDir);
        RunGit(gitRepoDir, "init");
        RunGit(gitRepoDir, "config user.email \"test@example.com\"");
        RunGit(gitRepoDir, "config user.name \"Test User\"");

        var trackedFile = Path.Combine(gitRepoDir, "Tracked.cs");
        await File.WriteAllTextAsync(trackedFile, "class Tracked {}\n");
        RunGit(gitRepoDir, "add Tracked.cs");
        RunGit(gitRepoDir, "commit -m \"initial\"");

        var gitProvider = new GitDiffProvider(gitRepoDir);

        // 変更がない状態のDiff
        var cleanDiff = await gitProvider.GetGitDiffAsync();
        Assert.Equal("変更はありません。", cleanDiff);

        // 追跡ファイルを変更し、未追跡ファイルを追加
        await File.WriteAllTextAsync(trackedFile, "class Tracked { void NewMethod() {} }\n");
        await File.WriteAllTextAsync(Path.Combine(gitRepoDir, "Untracked.cs"), "class Untracked {}\n");

        var dirtyState = await gitProvider.GetCurrentStateAsync();
        Assert.NotEqual("non-git", dirtyState.CurrentBranch);
        Assert.Contains("Tracked.cs", dirtyState.ModifiedFiles);
        Assert.Contains("Untracked.cs", dirtyState.UntrackedFiles);

        var dirtyDiff = await gitProvider.GetGitDiffAsync();
        Assert.Contains("NewMethod", dirtyDiff);
    }

    [Fact]
    public async Task HttpOllamaClientがチャットストリームの正常応答と不正JSONスキップおよび通信エラーを正しく処理すること()
    {
        var ndjsonLines = new[]
        {
            "", // 空行（スキップ）
            "{malformed_json", // 不正JSON行（スキップ）
            "null", // chunkDto == null（スキップ）
            "{\"model\":\"gemma4:26b\",\"done\":false}", // message == null
            "{\"model\":\"gemma4:26b\",\"message\":{\"role\":\"assistant\",\"content\":\"こんにちは\"},\"done\":false}",
            "{\"model\":\"gemma4:26b\",\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[{\"id\":\"tc1\",\"function\":{\"name\":\"read_file\",\"arguments\":{\"filePath\":\"a.cs\"}}},{\"id\":\"tc2\",\"function\":{\"name\":\"get_git_diff\",\"arguments\":null}}]},\"done\":true,\"total_duration\":1000,\"load_duration\":100,\"prompt_eval_count\":50,\"eval_count\":10}"
        };
        var streamPayload = string.Join("\n", ndjsonLines);

        var client = new HttpOllamaClient(new HttpClient(new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(streamPayload, Encoding.UTF8, "application/x-ndjson")
            })), "http://localhost:11434/");

        var messages = new List<ChatMessage>
        {
            new("user", "こんにちは"),
            new("assistant", "", new[]
            {
                new ToolCall("c1", "read_file", "{\"filePath\":\"a.cs\"}"),
                new ToolCall("c2", "read_file", null!),
                new ToolCall("c3", "read_file", "{invalid_args_json")
            })
        };

        var tools = new List<ToolDefinition>
        {
            new("read_file", "desc", "{\"type\":\"object\",\"properties\":{}}"),
            new("broken_schema_tool", "desc", "{invalid_schema_json")
        };

        var chunks = new List<OllamaResponseChunk>();
        await foreach (var chunk in client.ChatStreamAsync("gemma4:26b", messages, tools))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(3, chunks.Count);
        Assert.Null(chunks[0].DeltaText);
        Assert.Equal("こんにちは", chunks[1].DeltaText);
        Assert.True(chunks[2].Done);
        Assert.Equal(2, chunks[2].ToolCalls!.Count);
        Assert.Equal("{\"filePath\":\"a.cs\"}", chunks[2].ToolCalls![0].ArgumentsJson);
        Assert.Equal("{}", chunks[2].ToolCalls![1].ArgumentsJson);

        // 2. HttpRequestException 発生時のエラーチャンク検証
        var throwingClient = new HttpOllamaClient(new HttpClient(new FakeHttpMessageHandler(_ =>
            throw new HttpRequestException("Connection refused"))));

        var connErrChunks = new List<OllamaResponseChunk>();
        await foreach (var c in throwingClient.ChatStreamAsync("gemma4:26b", new[] { new ChatMessage("user", "hi") }, tools: null))
        {
            connErrChunks.Add(c);
        }
        Assert.Single(connErrChunks);
        Assert.Contains("Ollama に接続できません", connErrChunks[0].DeltaText);

        // 3. 非200ステータスコード時のエラーチャンク検証
        var statusErrClient = new HttpOllamaClient(new HttpClient(new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("model not found")
            })));

        var statusErrChunks = new List<OllamaResponseChunk>();
        await foreach (var c in statusErrClient.ChatStreamAsync("gemma4:26b", new[] { new ChatMessage("user", "hi") }))
        {
            statusErrChunks.Add(c);
        }
        Assert.Single(statusErrChunks);
        Assert.Contains("InternalServerError", statusErrChunks[0].DeltaText);
        Assert.Contains("model not found", statusErrChunks[0].DeltaText);
    }

    [Fact]
    public async Task HttpOllamaClientがモデル一覧取得の正常系および各種フォールバックを正しく処理すること()
    {
        // 1. 正常にモデル一覧が返る場合（空文字のモデル名は除外）
        var okClient = new HttpOllamaClient(new HttpClient(new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"models\":[{\"name\":\"qwen2.5-coder:7b\"},{\"name\":\"\"}]}")
            })));
        var models = await okClient.GetAvailableModelsAsync();
        Assert.Single(models);
        Assert.Equal("qwen2.5-coder:7b", models[0]);

        // 2. レスポンス自体がnullまたはmodelsがnullの場合のフォールバック
        var nullJsonClient = new HttpOllamaClient(new HttpClient(new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("null")
            })));
        Assert.Contains("gemma4:26b", await nullJsonClient.GetAvailableModelsAsync());

        var nullModelsClient = new HttpOllamaClient(new HttpClient(new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"models\":null}")
            })));
        var fallback1 = await nullModelsClient.GetAvailableModelsAsync();
        Assert.Contains("gemma4:26b", fallback1);

        // 3. HTTPエラーステータス時のフォールバック
        var errorStatusClient = new HttpOllamaClient(new HttpClient(new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadGateway))));
        var fallback2 = await errorStatusClient.GetAvailableModelsAsync();
        Assert.Contains("gemma4:26b", fallback2);

        // 4. 通信例外時のフォールバック
        var exceptionClient = new HttpOllamaClient(new HttpClient(new FakeHttpMessageHandler(_ =>
            throw new HttpRequestException("Network down"))));
        var fallback3 = await exceptionClient.GetAvailableModelsAsync();
        Assert.Contains("gemma4:26b", fallback3);
    }

    private static void RunGit(string workingDir, string args)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        p.WaitForExit();
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
