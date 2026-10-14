using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kotli.Application.Models;
using Kotli.Domain.Interfaces;
using Kotli.Domain.Models;
using Kotli.Domain.Services;

namespace Kotli.Application.UseCases;

public class ProcessUserPromptUseCase
{
    private static readonly Regex FileMentionRegex = new(@"(?:^|\s)@(?<path>[^\s]+)", RegexOptions.Compiled);

    private readonly IOllamaClient _ollamaClient;
    private readonly IToolExecutionRouter _toolRouter;
    private readonly IWorkspaceProvider _workspaceProvider;
    private readonly IMemoryStore _memoryStore;
    private readonly ContextPruner _contextPruner;
    private readonly IReadTool? _readTool;
    private readonly int _maxToolLoops;

    public ProcessUserPromptUseCase(
        IOllamaClient ollamaClient,
        IToolExecutionRouter toolRouter,
        IWorkspaceProvider workspaceProvider,
        IMemoryStore memoryStore,
        ContextPruner contextPruner,
        IReadTool? readTool = null,
        int maxToolLoops = 15)
    {
        _ollamaClient = ollamaClient;
        _toolRouter = toolRouter;
        _workspaceProvider = workspaceProvider;
        _memoryStore = memoryStore;
        _contextPruner = contextPruner;
        _readTool = readTool;
        _maxToolLoops = maxToolLoops;
    }

    public async Task ExecuteAsync(
        ChatSession session,
        string modelName,
        string userPrompt,
        Action<string> onTextChunk,
        Action<ProgressUpdate> onProgress,
        CancellationToken cancellationToken = default)
    {
        // 1. Expand @file mentions if present and IReadTool is available
        string expandedPrompt = await ExpandFileMentionsAsync(userPrompt, onProgress);

        // 2. Fetch current workspace state
        onProgress(new ProgressUpdate("ワークスペース読み込み中", "Gitブランチと変更ファイル一覧を取得しています..."));
        var workspaceState = await _workspaceProvider.GetCurrentStateAsync();

        // 3. Fetch memory items
        onProgress(new ProgressUpdate("メモリ読み込み中", "ローカルメモリファイルを読み込んでいます..."));
        var memories = await _memoryStore.LoadAllAsync();

        // 4. Prune and assemble prompt
        onProgress(new ProgressUpdate("プロンプト構築中", "コンテキストウィンドウサイズを最適化しています..."));
        var payload = _contextPruner.PruneAndAssemble(session, expandedPrompt, workspaceState, memories);

        // Keep a copy of what we send so we can append tool responses in the loop
        var conversationPayload = payload.ToList();

        bool hasToolCalls = true;
        int toolLoopCount = 0;
        string finalAssistantResponse = "";

        // First, append the user prompt to the active session history so it's persisted
        session.AddMessage("user", userPrompt);

        while (hasToolCalls && toolLoopCount < _maxToolLoops)
        {
            toolLoopCount++;
            onProgress(new ProgressUpdate("Ollama接続中", "Ollamaの応答を待っています..."));

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
                onProgress(new ProgressUpdate("ツール実行中", $"{accumulatedToolCalls.Count}個のツールを実行しています..."));

                // Create and add assistant message requesting tool calls to payload and session history
                var assistantToolMessage = new ChatMessage("assistant", chunkTextAccumulator, accumulatedToolCalls);
                conversationPayload.Add(assistantToolMessage);
                session.AddMessage(assistantToolMessage);

                // Execute each tool and append tool response messages to payload and session history
                foreach (var toolCall in accumulatedToolCalls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    onProgress(new ProgressUpdate("ツール実行中", $"ツールを実行中: {toolCall.Name}..."));
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

        // If the loop terminated because we hit _maxToolLoops while the model still called tools,
        // request one final summary response with tools disabled so the user is never left hanging.
        if (hasToolCalls && toolLoopCount >= _maxToolLoops)
        {
            onProgress(new ProgressUpdate("Ollama接続中", "ツール実行上限に達したため最終回答を生成しています..."));
            var finalChunks = _ollamaClient.ChatStreamAsync(modelName, conversationPayload, tools: null, cancellationToken);
            string summaryAccumulator = "";

            await foreach (var chunk in finalChunks.WithCancellation(cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.DeltaText))
                {
                    summaryAccumulator += chunk.DeltaText;
                    onTextChunk(chunk.DeltaText);
                }
            }

            finalAssistantResponse = summaryAccumulator;
        }

        // Save the final text response from the assistant to the session history (if any was accumulated in the final turn)
        if (!string.IsNullOrEmpty(finalAssistantResponse))
        {
            session.AddMessage("assistant", finalAssistantResponse);
        }

        // Compact tool response contents in the session history to save context space in subsequent turns
        session.CompactToolMessages(500);

        onProgress(new ProgressUpdate("オンライン", "準備完了"));
    }

    private async Task<string> ExpandFileMentionsAsync(string userPrompt, Action<ProgressUpdate> onProgress)
    {
        if (_readTool == null || !userPrompt.Contains('@'))
        {
            return userPrompt;
        }

        var matches = FileMentionRegex.Matches(userPrompt);
        if (matches.Count == 0)
        {
            return userPrompt;
        }

        var attachments = new StringBuilder();
        var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in matches)
        {
            var rawPath = match.Groups["path"].Value.TrimEnd(',', '.', ':', ';', ')', ']');
            if (string.IsNullOrWhiteSpace(rawPath) || !processedPaths.Add(rawPath))
            {
                continue;
            }

            onProgress(new ProgressUpdate("ファイル添付中", $"@{rawPath} を読み込んでいます..."));
            var content = await _readTool.ReadFileAsync(rawPath, 1, 300);
            if (!content.StartsWith("[エラー]", StringComparison.Ordinal))
            {
                attachments.AppendLine();
                attachments.AppendLine($"### 添付ファイル (@{rawPath}):");
                attachments.AppendLine(content);
            }
        }

        return attachments.Length > 0
            ? userPrompt + "\n" + attachments.ToString()
            : userPrompt;
    }
}
