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
        var systemContent = "You are Kotli, a highly advanced local AI CLI developer assistant.\n" +
                            "You are helping the user write, edit, and understand code in their workspace.\n" +
                            "Keep your answers concise, direct, and focused on technical correctness.\n\n";

        // Inject memory items
        var memorySection = string.Join("\n", memoryItems.Select(m => $"- {m.Key}: {m.Content}"));
        if (!string.IsNullOrEmpty(memorySection))
        {
            systemContent += "### Long-term Memory Context:\n" + memorySection + "\n\n";
        }

        // Inject workspace state (git diff / branch)
        systemContent += $"### Workspace Git Status:\n" +
                         $"- Branch: {workspaceState.CurrentBranch}\n" +
                         $"- Modified files: {(workspaceState.ModifiedFiles.Any() ? string.Join(", ", workspaceState.ModifiedFiles) : "None")}\n" +
                         $"- Untracked files: {(workspaceState.UntrackedFiles.Any() ? string.Join(", ", workspaceState.UntrackedFiles) : "None")}\n\n";

        if (!string.IsNullOrEmpty(workspaceState.Diff))
        {
            // Limit Diff size to prevent overwhelming the context
            string diffContent = workspaceState.Diff;
            const int maxDiffChars = 12000; // approx 3000 tokens
            if (diffContent.Length > maxDiffChars)
            {
                diffContent = diffContent[..maxDiffChars] + "\n... [Diff truncated to save context] ...";
            }
            systemContent += "### Workspace Changes (git diff):\n```diff\n" + diffContent + "\n```\n\n";
        }

        assembledMessages.Add(new ChatMessage("system", systemContent));

        // 2. Select and Prune History
        // We only keep the last _maxHistoryTurns, and within those, we ensure we don't exceed the token budget.
        var historyToProcess = session.Messages.TakeLast(_maxHistoryTurns).ToList();

        // Calculate current system prompt + new prompt size
        int systemTokens = assembledMessages[0].EstimatedTokens;
        int newUserTokens = currentUserPrompt.Length / 4;
        int remainingTokenBudget = _maxTokenLimit - systemTokens - newUserTokens;

        // Keep messages from the end of history that fit within the remaining token budget
        var selectedHistory = new List<ChatMessage>();
        int runningTokenCount = 0;

        for (int i = historyToProcess.Count - 1; i >= 0; i--)
        {
            var msg = historyToProcess[i];
            int msgTokens = msg.EstimatedTokens;

            if (runningTokenCount + msgTokens > remainingTokenBudget)
            {
                break; // Stop including older messages once we hit the budget
            }

            selectedHistory.Insert(0, msg);
            runningTokenCount += msgTokens;
        }

        assembledMessages.AddRange(selectedHistory);

        // 3. Add latest User Prompt
        assembledMessages.Add(new ChatMessage("user", currentUserPrompt));

        return assembledMessages;
    }
}
