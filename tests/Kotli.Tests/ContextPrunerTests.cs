using System;
using System.Collections.Generic;
using System.Linq;
using Kotli.Domain.Models;
using Kotli.Domain.Services;
using Xunit;

namespace Kotli.Tests;

public class ContextPrunerTests
{
    [Fact]
    public void PruneAndAssemble_AssemblesSystemPromptCorrectly()
    {
        // Arrange
        var pruner = new ContextPruner(maxHistoryTurns: 5, maxTokenLimit: 1000);
        var session = new ChatSession();
        var workspaceState = new WorkspaceState(
            CurrentBranch: "main",
            Diff: "+ added some cool code",
            ModifiedFiles: new[] { "src/Code.cs" },
            UntrackedFiles: new[] { "src/Untracked.cs" }
        );
        var memories = new[] { new MemoryItem("arch_rule", "Use DDD rules", DateTime.UtcNow) };

        // Act
        var result = pruner.PruneAndAssemble(session, "Write a class", workspaceState, memories);

        // Assert
        Assert.Equal(2, result.Count); // System prompt + latest user prompt
        Assert.Equal("system", result[0].Role);
        Assert.Equal("user", result[1].Role);
        Assert.Equal("Write a class", result[1].Content);

        var systemPrompt = result[0].Content;
        Assert.Contains("Use DDD rules", systemPrompt);
        Assert.Contains("Branch: main", systemPrompt);
        Assert.Contains("src/Code.cs", systemPrompt);
        Assert.Contains("src/Untracked.cs", systemPrompt);
        Assert.Contains("+ added some cool code", systemPrompt);
    }

    [Fact]
    public void PruneAndAssemble_PreservesHistoryWithinBudget()
    {
        // Arrange
        var pruner = new ContextPruner(maxHistoryTurns: 5, maxTokenLimit: 4000);
        var session = new ChatSession();
        session.AddMessage("user", "Hello");
        session.AddMessage("assistant", "Hi there!");

        var workspaceState = new WorkspaceState("main", "", Array.Empty<string>(), Array.Empty<string>());
        var memories = Array.Empty<MemoryItem>();

        // Act
        var result = pruner.PruneAndAssemble(session, "How are you?", workspaceState, memories);

        // Assert
        Assert.Equal(4, result.Count); // System + User Hello + Assistant Hi + New Prompt
        Assert.Equal("system", result[0].Role);
        Assert.Equal("user", result[1].Role);
        Assert.Equal("Hello", result[1].Content);
        Assert.Equal("assistant", result[2].Role);
        Assert.Equal("Hi there!", result[2].Content);
        Assert.Equal("user", result[3].Role);
        Assert.Equal("How are you?", result[3].Content);
    }

    [Fact]
    public void PruneAndAssemble_PrunesHistoryWhenExceedingTokenLimit()
    {
        // Arrange
        // Small token limit (approx 200 tokens) to trigger pruning
        var pruner = new ContextPruner(maxHistoryTurns: 10, maxTokenLimit: 150);
        var session = new ChatSession();
        
        // Add 5 long interactions (each roughly 80 characters = 20 tokens)
        for (int i = 1; i <= 5; i++)
        {
            session.AddMessage("user", $"This is user prompt number {i} which has quite some length.");
            session.AddMessage("assistant", $"This is assistant reply number {i} which is also quite verbose.");
        }

        var workspaceState = new WorkspaceState("main", "", Array.Empty<string>(), Array.Empty<string>());
        var memories = Array.Empty<MemoryItem>();

        // Act
        var result = pruner.PruneAndAssemble(session, "Latest short prompt", workspaceState, memories);

        // Assert
        // The total history should have been truncated. We should still have the system prompt and the latest user prompt,
        // and some subset of the recent history that fits in the tiny budget, but not all 10 history messages.
        Assert.True(result.Count < 12, $"History should be pruned. Count was {result.Count}");
        Assert.Equal("system", result[0].Role);
        Assert.Equal("user", result.Last().Role);
        Assert.Equal("Latest short prompt", result.Last().Content);
    }
}
