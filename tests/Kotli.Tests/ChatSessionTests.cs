using System;
using System.Collections.Generic;
using Kotli.Domain.Attributes;
using Kotli.Domain.Models;
using Xunit;

namespace Kotli.Tests;

public class ChatSessionTests
{
    [Fact]
    public void 長いツール応答メッセージのみがスタブ化され短い応答やアシスタント応答は維持されること()
    {
        var session = new ChatSession();

        // 短いツール応答（スタブ化されない）
        session.AddMessage(new ChatMessage("tool", "Short success response", ToolCallId: "call_1"));

        // 空(null)のツール応答（スタブ化されない）
        session.AddMessage(new ChatMessage("tool", null!, ToolCallId: "call_null"));

        // 閾値を超える長いツール応答（スタブ化される）
        string longContent = new string('A', 600);
        session.AddMessage(new ChatMessage("tool", longContent, ToolCallId: "call_2"));

        // 長いアシスタント応答（roleがtoolではないためスタブ化されない）
        string longAssistantContent = new string('B', 600);
        session.AddMessage("assistant", longAssistantContent);

        session.CompactToolMessages(500);

        Assert.Equal(4, session.Messages.Count);
        Assert.Equal("Short success response", session.Messages[0].Content);
        Assert.Null(session.Messages[1].Content);
        Assert.Contains("[コンテキスト節約のため内容はスタブ化されました。AIはすでに内容を読み込んで回答済みです]", session.Messages[2].Content);
        Assert.Contains("元の内容は 600 文字ありました", session.Messages[2].Content);
        Assert.Equal(longAssistantContent, session.Messages[3].Content);
    }

    [Fact]
    public void メッセージの追加と履歴の読み込みおよびクリアが正しく動作すること()
    {
        var session = new ChatSession();
        session.AddMessage("user", "こんにちは");
        Assert.Single(session.Messages);

        var history = new List<ChatMessage>
        {
            new("user", "質問1"),
            new("assistant", "回答1")
        };

        session.LoadHistory(history);
        Assert.Equal(2, session.Messages.Count);
        Assert.Equal("質問1", session.Messages[0].Content);
        Assert.Equal("回答1", session.Messages[1].Content);

        session.Clear();
        Assert.Empty(session.Messages);
    }

    [Fact]
    public void 空文字列やnullおよびASCIIと日本語混在テキストのトークン推定が正しく計算されること()
    {
        Assert.Equal(0, ChatMessage.EstimateTokens(null));
        Assert.Equal(0, ChatMessage.EstimateTokens(""));

        // ASCII 8文字 = 8 / 4 = 2トークン
        var asciiMsg = new ChatMessage("user", "12345678");
        Assert.Equal(2, asciiMsg.EstimatedTokens);

        // 日本語5文字 + ASCII 4文字 = 5 + (4 / 4) = 6トークン
        Assert.Equal(6, ChatMessage.EstimateTokens("あいうえおabcd"));
    }

    [Fact]
    public void ドメインモデルおよび属性クラスのプロパティが正しく保持されること()
    {
        var now = DateTime.UtcNow;
        var memory = new MemoryItem("key1", "content1", now);
        Assert.Equal("key1", memory.Key);
        Assert.Equal("content1", memory.Content);
        Assert.Equal(now, memory.UpdatedAt);

        var toolCall = new ToolCall("id1", "read_file", "{\"filePath\":\"a.cs\"}");
        Assert.Equal("id1", toolCall.Id);
        Assert.Equal("read_file", toolCall.Name);
        Assert.Equal("{\"filePath\":\"a.cs\"}", toolCall.ArgumentsJson);

        var toolResponse = new ToolResponse("id1", "result");
        Assert.Equal("id1", toolResponse.ToolCallId);
        Assert.Equal("result", toolResponse.Content);

        var chunk = new OllamaResponseChunk("delta", new[] { toolCall }, true, 100L, 20L, 10, 5);
        Assert.Equal("delta", chunk.DeltaText);
        Assert.Single(chunk.ToolCalls!);
        Assert.True(chunk.Done);
        Assert.Equal(100L, chunk.TotalDuration);
        Assert.Equal(20L, chunk.LoadDuration);
        Assert.Equal(10, chunk.PromptEvalCount);
        Assert.Equal(5, chunk.EvalCount);

        var toolParam = new ToolParameter("string", "ファイルパス", true);
        Assert.Equal("string", toolParam.Type);
        Assert.Equal("ファイルパス", toolParam.Description);
        Assert.True(toolParam.Required);

        var toolDef = new ToolDefinition("read_file", "ファイルを読み込む", "{}");
        Assert.Equal("read_file", toolDef.Name);
        Assert.Equal("ファイルを読み込む", toolDef.Description);
        Assert.Equal("{}", toolDef.ParametersJson);

        var ws = new WorkspaceState("main", "diff", new[] { "a.cs" }, new[] { "b.cs" });
        Assert.Equal("main", ws.CurrentBranch);
        Assert.Equal("diff", ws.Diff);
        Assert.Single(ws.ModifiedFiles);
        Assert.Single(ws.UntrackedFiles);

        var toolAttr = new KotliToolAttribute("write_file", "書き込み", requiresApproval: true);
        Assert.Equal("write_file", toolAttr.Name);
        Assert.Equal("書き込み", toolAttr.Description);
        Assert.True(toolAttr.RequiresApproval);

        var paramAttr = new KotliToolParamAttribute("パラメータ説明");
        Assert.Equal("パラメータ説明", paramAttr.Description);
    }
}
