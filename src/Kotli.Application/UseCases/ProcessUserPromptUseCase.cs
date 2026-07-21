using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kotli.Domain.Interfaces;
using Kotli.Domain.Models;
using Kotli.Domain.Services;
using Kotli.Application.Models;

namespace Kotli.Application.UseCases;

public class ProcessUserPromptUseCase
{
    private readonly IOllamaClient _ollamaClient;
    private readonly IToolExecutionRouter _toolRouter;
    private readonly IWorkspaceProvider _workspaceProvider;
    private readonly IMemoryStore _memoryStore;
    private readonly ContextPruner _contextPruner;

    public ProcessUserPromptUseCase(
        IOllamaClient ollamaClient,
        IToolExecutionRouter toolRouter,
        IWorkspaceProvider workspaceProvider,
        IMemoryStore memoryStore,
        ContextPruner contextPruner)
    {
        _ollamaClient = ollamaClient;
        _toolRouter = toolRouter;
        _workspaceProvider = workspaceProvider;
        _memoryStore = memoryStore;
        _contextPruner = contextPruner;
    }

    public async Task ExecuteAsync(
        ChatSession session,
        string modelName,
        string userPrompt,
        Action<string> onTextChunk,
        Action<ProgressUpdate> onProgress,
        CancellationToken cancellationToken = default)
    {
        // 1. Fetch current workspace state
        onProgress(new ProgressUpdate("Reading Workspace", "Retrieving git diff and branch status..."));
        var workspaceState = await _workspaceProvider.GetCurrentStateAsync();

        // 2. Fetch memory items
        onProgress(new ProgressUpdate("Reading Memory", "Retrieving local memory files..."));
        var memories = await _memoryStore.LoadAllAsync();

        // 3. Prune and assemble prompt
        onProgress(new ProgressUpdate("Assembling Prompt", "Optimizing context window size..."));
        var payload = _contextPruner.PruneAndAssemble(session, userPrompt, workspaceState, memories);

        // Keep a copy of what we send so we can append tool responses in the loop
        var conversationPayload = payload.ToList();

        bool hasToolCalls = true;
        int toolLoopCount = 0;
        const int maxToolLoops = 5;

        string finalAssistantResponse = "";

        // First, append the user prompt to the active session history so it's persisted
        session.AddMessage("user", userPrompt);

        while (hasToolCalls && toolLoopCount < maxToolLoops)
        {
            toolLoopCount++;
            onProgress(new ProgressUpdate("Ollama Connection", "Waiting for Ollama response..."));

            var tools = _toolRouter.GetToolDefinitions();
            var responseChunks = _ollamaClient.ChatStreamAsync(modelName, conversationPayload, tools, cancellationToken);

            List<ToolCall> accumulatedToolCalls = new();
            string chunkTextAccumulator = "";

            await foreach (var chunk in responseChunks.WithCancellation(cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.DeltaText))
                {
                    chunkTextAccumulator += chunk.DeltaText;
                    onTextChunk(chunk.DeltaText);
                }

                if (chunk.ToolCalls != null && chunk.ToolCalls.Any())
                {
                    accumulatedToolCalls.AddRange(chunk.ToolCalls);
                }
            }

            if (accumulatedToolCalls.Any())
            {
                onProgress(new ProgressUpdate("Tool Execution", $"Executing {accumulatedToolCalls.Count} tool(s)..."));

                // Create and add assistant message requesting tool calls to payload and session history
                var assistantToolMessage = new ChatMessage("assistant", chunkTextAccumulator, accumulatedToolCalls);
                conversationPayload.Add(assistantToolMessage);
                session.AddMessage(assistantToolMessage);

                // Execute each tool and append tool response messages to payload and session history
                foreach (var toolCall in accumulatedToolCalls)
                {
                    onProgress(new ProgressUpdate("Executing Tool", $"Running tool: {toolCall.Name}..."));
                    var response = await _toolRouter.ExecuteToolAsync(toolCall);
                    
                    var toolMessage = new ChatMessage("tool", response.Content, ToolCallId: response.ToolCallId);
                    conversationPayload.Add(toolMessage);
                    session.AddMessage(toolMessage);
                }
            }
            else
            {
                hasToolCalls = false;
                finalAssistantResponse = chunkTextAccumulator;
            }
        }

        // Save the final text response from the assistant to the session history (if any was accumulated in the final turn)
        if (!string.IsNullOrEmpty(finalAssistantResponse))
        {
            session.AddMessage("assistant", finalAssistantResponse);
        }

        onProgress(new ProgressUpdate("Online", "Ready"));
    }
}
