using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kotli.Application.Models;
using Kotli.Application.UseCases;
using Kotli.Domain.Models;
using Kotli.Domain.Services;
using Kotli.Infrastructure.Ollama;
using Kotli.Infrastructure.Storage;
using Kotli.Infrastructure.Tools;
using Kotli.Infrastructure.Workspace;
using Spectre.Console;

namespace Kotli.Cli;

class Program
{
    static async Task Main(string[] args)
    {
        AnsiConsole.Write(
            new FigletText("K O T L I")
                .Color(Color.Blue));

        AnsiConsole.MarkupLine("[grey]Local Ollama-powered DDD AI CLI Developer Assistant (Gemma4:26b)[/]");
        AnsiConsole.MarkupLine("[grey]Type [bold white]exit[/] or [bold white]quit[/] to close.[/]");
        AnsiConsole.WriteLine();

        // 1. Setup DI / Dependencies
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var ollamaClient = new HttpOllamaClient(httpClient, "http://localhost:11434");
        
        var readTool = new LocalReadTool();
        var writeTool = new LocalWriteTool();
        var searchTool = new StubWebSearchTool();
        
        // This is emitted by our ToolRouterGenerator (Roslyn Source Generator)
        var toolRouter = new ToolExecutionRouter(readTool, writeTool, searchTool);

        var workspaceProvider = new GitDiffProvider();
        var memoryStore = new MarkdownMemoryStore();
        
        // 10 turns max history, 8192 token limit
        var contextPruner = new ContextPruner(maxHistoryTurns: 10, maxTokenLimit: 8192);

        var useCase = new ProcessUserPromptUseCase(
            ollamaClient,
            toolRouter,
            workspaceProvider,
            memoryStore,
            contextPruner);

        var session = new ChatSession();
        var modelName = "gemma4:26b"; // Target model

        // Keep track of state
        var currentStage = "Online";
        var currentDetails = "Ready";

        while (true)
        {
            // Render Status Bar
            var totalTokens = CalculateSessionTokens(session);
            RenderStatusBar(modelName, currentStage, currentDetails, totalTokens);

            Console.Write("\nkotli> ");
            var prompt = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(prompt)) continue;
            if (prompt.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase) || 
                prompt.Trim().Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            AnsiConsole.MarkupLine("\n[bold blue]Kotli is thinking...[/]");

            // Set stage to generating
            currentStage = "Ollama Connection";
            currentDetails = "Connecting...";

            var cts = new CancellationTokenSource();
            
            try
            {
                await useCase.ExecuteAsync(
                    session,
                    modelName,
                    prompt,
                    chunk =>
                    {
                        // Print the chunk text directly
                        Console.Write(chunk);
                    },
                    progress =>
                    {
                        currentStage = progress.Stage;
                        currentDetails = progress.Details ?? "Processing";
                    },
                    cts.Token);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"\n[bold red]Error:[/] {ex.Message}");
            }

            Console.WriteLine(); // New line after streaming ends
            currentStage = "Online";
            currentDetails = "Ready";
        }
    }

    private static int CalculateSessionTokens(ChatSession session)
    {
        int total = 0;
        foreach (var msg in session.Messages)
        {
            total += msg.EstimatedTokens;
        }
        return total;
    }

    private static void RenderStatusBar(string model, string stage, string details, int tokens)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().LeftAligned());
        grid.AddColumn(new GridColumn().RightAligned());

        var statusColor = stage == "Online" ? "green" : "yellow";
        var stageMarkup = $"[{statusColor}]{stage}[/]";
        if (!string.IsNullOrEmpty(details) && details != "Ready")
        {
            stageMarkup += $" ({details})";
        }

        grid.AddRow(
            new Markup($"[bold blue]Model:[/] {model} | [bold cyan]KV Cache (Est. Tokens):[/] {tokens}"),
            new Markup($"[bold]Status:[/] {stageMarkup}")
        );

        AnsiConsole.Write(
            new Panel(grid)
                .Header("[bold grey] Kotli System Status [/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Grey30));
    }
}
