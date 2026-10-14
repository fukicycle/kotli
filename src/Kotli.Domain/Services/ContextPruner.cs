using System;
using System.Collections.Generic;
using System.Linq;
using Kotli.Domain.Models;

namespace Kotli.Domain.Services;

public class ContextPruner
{
    private readonly int _maxHistoryTurns;
    private readonly int _maxTokenLimit;

    public ContextPruner(int maxHistoryTurns = 10, int maxTokenLimit = 8192)
    {
        _maxHistoryTurns = maxHistoryTurns;
        _maxTokenLimit = maxTokenLimit;
    }

    public List<ChatMessage> PruneAndAssemble(
        ChatSession session,
        string currentUserPrompt,
        WorkspaceState workspaceState,
        IEnumerable<MemoryItem> memoryItems)
    {
        var assembledMessages = new List<ChatMessage>();

        // 1. Construct the System Prompt
        var systemContent = "あなたはKotli（コトリ）です。高度に洗練されたローカルAI CLI開発アシスタントです。\n" +
                            "ユーザーがワークスペース内のコードを記述、編集、理解するのを支援しています。\n" +
                            "回答は簡潔かつ直接的で、技術的な正確さに焦点を当てたものにしてください。必ず日本語で回答してください。\n\n";

        // Inject memory items
        var memorySection = string.Join("\n", memoryItems.Select(m => $"- {m.Key}: {m.Content}"));
        if (!string.IsNullOrEmpty(memorySection))
        {
            systemContent += "### 長期記憶コンテキスト:\n" + memorySection + "\n\n";
        }

        // Inject workspace state (git status metadata)
        systemContent += $"### ワークスペースのGitステータス:\n" +
                         $"- ブランチ: {workspaceState.CurrentBranch}\n" +
                         $"- 変更されたファイル: {(workspaceState.ModifiedFiles.Any() ? string.Join(", ", workspaceState.ModifiedFiles) : "なし")}\n" +
                         $"- 追跡されていないファイル: {(workspaceState.UntrackedFiles.Any() ? string.Join(", ", workspaceState.UntrackedFiles) : "なし")}\n\n" +
                         $"注意: 変更内容の詳細（git diff）が必要な場合は、`get_git_diff` ツールを使用して取得してください。\n\n";

        assembledMessages.Add(new ChatMessage("system", systemContent));

        // 2. Group history into atomic blocks so assistant(tool_calls) and subsequent tool responses are never separated
        var atomicBlocks = GroupIntoAtomicBlocks(session.Messages);
        var blocksToProcess = atomicBlocks.TakeLast(_maxHistoryTurns).ToList();

        // Calculate current system prompt + new prompt size
        int systemTokens = assembledMessages[0].EstimatedTokens;
        int newUserTokens = ChatMessage.EstimateTokens(currentUserPrompt);
        int remainingTokenBudget = _maxTokenLimit - systemTokens - newUserTokens;

        // Keep blocks from the end of history that fit within the remaining token budget
        var selectedBlocks = new List<List<ChatMessage>>();
        int runningTokenCount = 0;

        for (int i = blocksToProcess.Count - 1; i >= 0; i--)
        {
            var block = blocksToProcess[i];
            int blockTokens = block.Sum(m => m.EstimatedTokens);

            if (runningTokenCount + blockTokens > remainingTokenBudget)
            {
                break; // Stop including older blocks once we hit the budget
            }

            selectedBlocks.Insert(0, block);
            runningTokenCount += blockTokens;
        }

        foreach (var block in selectedBlocks)
        {
            assembledMessages.AddRange(block);
        }

        // 3. Add latest User Prompt
        assembledMessages.Add(new ChatMessage("user", currentUserPrompt));

        return assembledMessages;
    }

    private static List<List<ChatMessage>> GroupIntoAtomicBlocks(IReadOnlyList<ChatMessage> messages)
    {
        var blocks = new List<List<ChatMessage>>();
        int i = 0;

        while (i < messages.Count)
        {
            var current = messages[i];

            // If an assistant message contains tool calls, group it together with all immediately following tool messages
            if (current.Role == "assistant" && current.ToolCalls != null && current.ToolCalls.Count > 0)
            {
                var toolBlock = new List<ChatMessage> { current };
                i++;
                while (i < messages.Count && messages[i].Role == "tool")
                {
                    toolBlock.Add(messages[i]);
                    i++;
                }
                blocks.Add(toolBlock);
            }
            else if (current.Role == "tool")
            {
                // Skip orphaned tool message that has no preceding assistant tool_calls message
                i++;
            }
            else
            {
                blocks.Add(new List<ChatMessage> { current });
                i++;
            }
        }

        return blocks;
    }
}
