using System;
using System.Linq;
using Kotli.Domain.Models;
using Kotli.Domain.Services;
using Xunit;

namespace Kotli.Tests;

public class ContextPrunerTests
{
    [Fact]
    public void システムプロンプトにメモリ項目とGitステータスが正しく組み込まれること()
    {
        var pruner = new ContextPruner(maxHistoryTurns: 5, maxTokenLimit: 1000);
        var session = new ChatSession();
        var workspaceState = new WorkspaceState(
            CurrentBranch: "main",
            Diff: "+ added some cool code",
            ModifiedFiles: new[] { "src/Code.cs" },
            UntrackedFiles: new[] { "src/Untracked.cs" }
        );
        var memories = new[] { new MemoryItem("arch_rule", "Use DDD rules", DateTime.UtcNow) };

        var result = pruner.PruneAndAssemble(session, "Write a class", workspaceState, memories);

        Assert.Equal(2, result.Count);
        Assert.Equal("system", result[0].Role);
        Assert.Equal("user", result[1].Role);
        Assert.Equal("Write a class", result[1].Content);

        var systemPrompt = result[0].Content;
        Assert.Contains("Use DDD rules", systemPrompt);
        Assert.Contains("ブランチ: main", systemPrompt);
        Assert.Contains("src/Code.cs", systemPrompt);
        Assert.Contains("src/Untracked.cs", systemPrompt);
        Assert.DoesNotContain("+ added some cool code", systemPrompt);
    }

    [Fact]
    public void トークン予算内の会話履歴がすべて保持されること()
    {
        var pruner = new ContextPruner(maxHistoryTurns: 5, maxTokenLimit: 4000);
        var session = new ChatSession();
        session.AddMessage("user", "Hello");
        session.AddMessage("assistant", "Hi there!", Array.Empty<ToolCall>());

        var workspaceState = new WorkspaceState("main", "", Array.Empty<string>(), Array.Empty<string>());
        var memories = Array.Empty<MemoryItem>();

        var result = pruner.PruneAndAssemble(session, "How are you?", workspaceState, memories);

        Assert.Equal(4, result.Count);
        Assert.Equal("system", result[0].Role);
        Assert.Equal("user", result[1].Role);
        Assert.Equal("Hello", result[1].Content);
        Assert.Equal("assistant", result[2].Role);
        Assert.Equal("Hi there!", result[2].Content);
        Assert.Equal("user", result[3].Role);
        Assert.Equal("How are you?", result[3].Content);
    }

    [Fact]
    public void トークン上限を超えた場合に古い履歴が適切にプルーニングされること()
    {
        var pruner = new ContextPruner(maxHistoryTurns: 10, maxTokenLimit: 310);
        var session = new ChatSession();

        for (int i = 1; i <= 5; i++)
        {
            session.AddMessage("user", $"This is user prompt number {i} which has quite some length.");
            session.AddMessage("assistant", $"This is assistant reply number {i} which is also quite verbose.");
        }

        var workspaceState = new WorkspaceState("main", "", Array.Empty<string>(), Array.Empty<string>());
        var memories = Array.Empty<MemoryItem>();

        var result = pruner.PruneAndAssemble(session, "Latest short prompt", workspaceState, memories);

        Assert.True(result.Count > 2 && result.Count < 12, $"History should be partially pruned. Count was {result.Count}");
        Assert.Equal("system", result[0].Role);
        Assert.Equal("user", result.Last().Role);
        Assert.Equal("Latest short prompt", result.Last().Content);
    }

    [Fact]
    public void 日本語テキストのトークン数が文字数ベースで正確に推定されること()
    {
        string japaneseText = "これは日本語のトークン数計算テストです。";
        int tokens = ChatMessage.EstimateTokens(japaneseText);

        Assert.Equal(japaneseText.Length, tokens);
    }

    [Fact]
    public void ツール呼び出しとツール応答のペアが分断されず孤立したツール応答は除外されること()
    {
        var session = new ChatSession();
        // 先頭に親のない孤立したtoolメッセージがある場合（スキップされるべき）
        session.AddMessage(new ChatMessage("tool", "orphaned tool output", ToolCallId: "orphan_1"));
        session.AddMessage("user", "Read file");

        var toolCalls = new[] { new ToolCall("call_1", "read_file", "{\"filePath\":\"a.cs\"}") };
        session.AddMessage(new ChatMessage("assistant", "", toolCalls));
        session.AddMessage(new ChatMessage("tool", "file content of a.cs", ToolCallId: "call_1"));
        session.AddMessage("assistant", "Here is the summary of a.cs");

        // 末尾にもう1つツール呼び出しブロックを追加（末尾でのwhileループ終了分岐もカバー）
        var lastToolCalls = new[] { new ToolCall("call_2", "read_file", "{\"filePath\":\"b.cs\"}") };
        session.AddMessage(new ChatMessage("assistant", "", lastToolCalls));
        session.AddMessage(new ChatMessage("tool", "file content of b.cs", ToolCallId: "call_2"));

        var pruner = new ContextPruner(maxHistoryTurns: 10, maxTokenLimit: 4000);
        var workspaceState = new WorkspaceState("main", "", Array.Empty<string>(), Array.Empty<string>());

        var result = pruner.PruneAndAssemble(session, "Follow up question", workspaceState, Array.Empty<MemoryItem>());

        // 孤立したtoolメッセージが含まれていないこと
        Assert.DoesNotContain(result, m => m.Content == "orphaned tool output");
        Assert.NotEqual("tool", result[1].Role);

        int toolIndex = result.FindIndex(m => m.Role == "tool");
        Assert.True(toolIndex > 1);
        Assert.Equal("assistant", result[toolIndex - 1].Role);
        Assert.NotNull(result[toolIndex - 1].ToolCalls);
    }
}
