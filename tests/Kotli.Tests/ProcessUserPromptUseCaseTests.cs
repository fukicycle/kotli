using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Kotli.Application.Models;
using Kotli.Application.UseCases;
using Kotli.Domain.Interfaces;
using Kotli.Domain.Models;
using Kotli.Domain.Services;
using Xunit;

namespace Kotli.Tests;

public class ProcessUserPromptUseCaseTests
{
    [Fact]
    public async Task ツール呼び出しなしの通常会話でストリーミング応答と履歴保存が正しく行われること()
    {
        var ollama = new FakeOllamaClient(new[]
        {
            new[]
            {
                new OllamaResponseChunk("", null, false),
                new OllamaResponseChunk("こんにちは！", Array.Empty<ToolCall>(), false),
                new OllamaResponseChunk("お手伝いします。", null, true)
            }
        });
        var router = new FakeToolRouter();
        var workspace = new FakeWorkspaceProvider();
        var memory = new FakeMemoryStore();
        var pruner = new ContextPruner();

        // readToolがnullのケースも検証
        var useCase = new ProcessUserPromptUseCase(ollama, router, workspace, memory, pruner, readTool: null);
        var session = new ChatSession();
        var receivedChunks = new List<string>();
        var progresses = new List<ProgressUpdate>();

        await useCase.ExecuteAsync(
            session,
            "gemma4:26b",
            "@Sample.cs について教えて",
            chunk => receivedChunks.Add(chunk),
            p => progresses.Add(p));

        Assert.Equal("こんにちは！お手伝いします。", string.Concat(receivedChunks));
        Assert.Equal(2, session.Messages.Count);
        Assert.Equal("user", session.Messages[0].Role);
        Assert.Equal("assistant", session.Messages[1].Role);
        Assert.Equal("こんにちは！お手伝いします。", session.Messages[1].Content);
        Assert.Contains(progresses, p => p.Stage == "オンライン" && p.Details == "準備完了");
    }

    [Fact]
    public async Task ツール呼び出しが発生した場合にツールを実行して最終回答までループすること()
    {
        var toolCall = new ToolCall("tc_1", "read_file", "{\"filePath\":\"App.cs\"}");
        var ollama = new FakeOllamaClient(new[]
        {
            // 1ターン目: ツール呼び出しを要求
            new[]
            {
                new OllamaResponseChunk("ファイルを確認します。", new[] { toolCall }, true)
            },
            // 2ターン目: ツールの結果を受けて最終回答（空の最終回答ケースも別テストで検証）
            new[]
            {
                new OllamaResponseChunk("App.csの内容を確認しました。", null, true)
            }
        });

        var router = new FakeToolRouter();
        var workspace = new FakeWorkspaceProvider();
        var memory = new FakeMemoryStore();
        var pruner = new ContextPruner();
        var readTool = new FakeReadTool();

        var useCase = new ProcessUserPromptUseCase(ollama, router, workspace, memory, pruner, readTool);
        var session = new ChatSession();
        var receivedText = new List<string>();

        await useCase.ExecuteAsync(
            session,
            "gemma4:26b",
            "App.csを読んで",
            t => receivedText.Add(t),
            _ => { });

        Assert.Single(router.ExecutedCalls);
        Assert.Equal("read_file", router.ExecutedCalls[0].Name);
        // user -> assistant(tool_calls) -> tool -> assistant(final)
        Assert.Equal(4, session.Messages.Count);
        Assert.Equal("App.csの内容を確認しました。", session.Messages[3].Content);
    }

    [Fact]
    public async Task ツール実行ループ上限に達した場合にツールなしでの最終要約リクエストが行われること()
    {
        var toolCall = new ToolCall("tc_loop", "read_file", "{\"filePath\":\"Loop.cs\"}");
        var ollama = new FakeOllamaClient(new[]
        {
            // ループ1回目（上限1回に設定）: まだツールを呼ぼうとする
            new[]
            {
                new OllamaResponseChunk("", new[] { toolCall }, true)
            },
            // 上限到達後のフォールバック要約リクエスト (tools: null)
            new[]
            {
                new OllamaResponseChunk(null, null, false),
                new OllamaResponseChunk("上限に達したため要約します。", null, true)
            }
        });

        var router = new FakeToolRouter();
        var workspace = new FakeWorkspaceProvider();
        var memory = new FakeMemoryStore();
        var pruner = new ContextPruner();

        var useCase = new ProcessUserPromptUseCase(ollama, router, workspace, memory, pruner, maxToolLoops: 1);
        var session = new ChatSession();
        var chunks = new List<string>();

        await useCase.ExecuteAsync(
            session,
            "gemma4:26b",
            "調査して",
            t => chunks.Add(t),
            _ => { });

        Assert.Contains("上限に達したため要約します。", string.Concat(chunks));
        Assert.Equal("上限に達したため要約します。", session.Messages[^1].Content);
    }

    [Fact]
    public async Task 最終アシスタント応答が空文字の場合には空のアシスタントメッセージを履歴に追加しないこと()
    {
        var ollama = new FakeOllamaClient(new[]
        {
            new[]
            {
                new OllamaResponseChunk("", null, true)
            }
        });

        var useCase = new ProcessUserPromptUseCase(
            ollama,
            new FakeToolRouter(),
            new FakeWorkspaceProvider(),
            new FakeMemoryStore(),
            new ContextPruner());

        var session = new ChatSession();
        await useCase.ExecuteAsync(session, "gemma4:26b", "テスト", _ => { }, _ => { });

        // ユーザープロンプトのみが記録され、空のassistantメッセージは追加されない
        Assert.Single(session.Messages);
        Assert.Equal("user", session.Messages[0].Role);
    }

    [Fact]
    public async Task アットマークによるファイルメンション展開が各種エッジケースを含めて正しく処理されること()
    {
        var readTool = new FakeReadTool();
        var workspace = new FakeWorkspaceProvider();
        var memory = new FakeMemoryStore();
        var pruner = new ContextPruner();

        // 1. メールアドレス形式（先頭や空白直後の@ではない）の場合は展開しない
        var ollama1 = new FakeOllamaClient(new[] { new[] { new OllamaResponseChunk("OK", null, true) } });
        var useCase1 = new ProcessUserPromptUseCase(ollama1, new FakeToolRouter(), workspace, memory, pruner, readTool);
        await useCase1.ExecuteAsync(new ChatSession(), "gemma4:26b", "user@example.com に連絡", _ => { }, _ => { });
        Assert.DoesNotContain("添付ファイル", ollama1.LastSentMessages[^1].Content);

        // 2. 記号のみのメンション(@...)や存在しないファイル(@missing.cs)のみの場合は添付セクションを追加しない
        var ollama2 = new FakeOllamaClient(new[] { new[] { new OllamaResponseChunk("OK", null, true) } });
        var useCase2 = new ProcessUserPromptUseCase(ollama2, new FakeToolRouter(), workspace, memory, pruner, readTool);
        await useCase2.ExecuteAsync(new ChatSession(), "gemma4:26b", "@... と @missing.cs を確認", _ => { }, _ => { });
        Assert.DoesNotContain("添付ファイル", ollama2.LastSentMessages[^1].Content);

        // 3. 有効なファイル(@Valid.cs,)と重複メンション(@Valid.cs)がある場合は1回だけ添付される
        var ollama3 = new FakeOllamaClient(new[] { new[] { new OllamaResponseChunk("OK", null, true) } });
        var useCase3 = new ProcessUserPromptUseCase(ollama3, new FakeToolRouter(), workspace, memory, pruner, readTool);
        await useCase3.ExecuteAsync(new ChatSession(), "gemma4:26b", "@Valid.cs, と @Valid.cs を読んで", _ => { }, _ => { });
        Assert.Contains("### 添付ファイル (@Valid.cs):", ollama3.LastSentMessages[^1].Content);
        Assert.Contains("1: class Valid {}", ollama3.LastSentMessages[^1].Content);
    }

    private sealed class FakeOllamaClient : IOllamaClient
    {
        private readonly Queue<IReadOnlyList<OllamaResponseChunk>> _turns;
        public List<ChatMessage> LastSentMessages { get; private set; } = new();

        public FakeOllamaClient(IEnumerable<IReadOnlyList<OllamaResponseChunk>> turns)
        {
            _turns = new Queue<IReadOnlyList<OllamaResponseChunk>>(turns);
        }

        public async IAsyncEnumerable<OllamaResponseChunk> ChatStreamAsync(
            string model,
            IEnumerable<ChatMessage> messages,
            IEnumerable<ToolDefinition>? tools = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastSentMessages = new List<ChatMessage>(messages);
            var chunks = _turns.Count > 0 ? _turns.Dequeue() : Array.Empty<OllamaResponseChunk>();
            foreach (var chunk in chunks)
            {
                await Task.Yield();
                yield return chunk;
            }
        }

        public Task<List<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<string> { "gemma4:26b" });
    }

    private sealed class FakeToolRouter : IToolExecutionRouter
    {
        public List<ToolCall> ExecutedCalls { get; } = new();

        public Task<ToolResponse> ExecuteToolAsync(ToolCall toolCall)
        {
            ExecutedCalls.Add(toolCall);
            return Task.FromResult(new ToolResponse(toolCall.Id, "tool execution result"));
        }

        public IEnumerable<ToolDefinition> GetToolDefinitions()
        {
            return new[] { new ToolDefinition("read_file", "desc", "{}") };
        }
    }

    private sealed class FakeWorkspaceProvider : IWorkspaceProvider
    {
        public Task<WorkspaceState> GetCurrentStateAsync()
            => Task.FromResult(new WorkspaceState("main", "", Array.Empty<string>(), Array.Empty<string>()));

        public Task<string> GetGitDiffAsync()
            => Task.FromResult("変更はありません。");
    }

    private sealed class FakeMemoryStore : IMemoryStore
    {
        public Task<IEnumerable<MemoryItem>> LoadAllAsync()
            => Task.FromResult<IEnumerable<MemoryItem>>(Array.Empty<MemoryItem>());

        public Task SaveAsync(MemoryItem memoryItem) => Task.CompletedTask;

        public Task<string> SaveMemoryToolAsync(string key, string content) => Task.FromResult("[成功]");

        public Task DeleteAsync(string key) => Task.CompletedTask;
    }

    private sealed class FakeReadTool : IReadTool
    {
        public Task<string> ReadFileAsync(string filePath, int? startLine = null, int? endLine = null)
        {
            if (filePath.Equals("missing.cs", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult("[エラー] ファイルが見つかりません: missing.cs");
            }
            return Task.FromResult("1: class Valid {}");
        }

        public Task<string> ListDirectoryAsync(string dirPath = "") => Task.FromResult("");
    }
}
