using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kotli.Application.Models;
using Kotli.Application.UseCases;
using Kotli.Cli.UI;
using Kotli.Domain.Models;
using Kotli.Domain.Services;
using Kotli.Infrastructure.Ollama;
using Kotli.Infrastructure.Storage;
using Kotli.Infrastructure.Tools;
using Kotli.Infrastructure.Workspace;
using PrettyPrompt;
using PrettyPrompt.Highlighting;
using Spectre.Console;

namespace Kotli.Cli;

class Program
{
    static async Task Main(string[] args)
    {
        RenderWelcomeHeader();

        // 1. Setup DI / Dependencies
        var session = new ChatSession();

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var ollamaClient = new HttpOllamaClient(httpClient, "http://localhost:11434");

        var readTool = new LocalReadTool();
        var writeTool = new LocalWriteTool();
        var webSearchTool = new StubWebSearchTool();
        var searchTool = new LocalSearchTool();
        var workspaceProvider = new GitDiffProvider();
        var memoryStore = new MarkdownMemoryStore(session.SessionId);
        var commandTool = new LocalCommandTool();

        bool isStatusLineActive = false;
        void ClearInlineStatusLine()
        {
            if (isStatusLineActive)
            {
                try
                {
                    int width = Math.Max(20, Console.WindowWidth - 1);
                    Console.Write("\r" + new string(' ', width) + "\r");
                }
                catch
                {
                    Console.WriteLine();
                }
                isStatusLineActive = false;
            }
        }

        void WriteInlineStatusLine(string text)
        {
            try
            {
                int width = Math.Max(20, Console.WindowWidth - 1);
                string padded = text.Length >= width ? text[..(width - 1)] : text.PadRight(width);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("\r" + padded);
                Console.ResetColor();
                isStatusLineActive = true;
            }
            catch
            {
                // Ignore if console output is redirected
            }
        }

        var approvalService = new ConsoleToolApprovalService(onBeforePrompt: ClearInlineStatusLine);

        var toolRouter = new ToolExecutionRouter(
            readTool,
            writeTool,
            webSearchTool,
            workspaceProvider,
            searchTool,
            memoryStore,
            commandTool,
            approvalService);

        // 10 turns max history, 8192 token limit
        var contextPruner = new ContextPruner(maxHistoryTurns: 10, maxTokenLimit: 8192);

        var useCase = new ProcessUserPromptUseCase(
            ollamaClient,
            toolRouter,
            workspaceProvider,
            memoryStore,
            contextPruner,
            readTool,
            maxToolLoops: 15);

        string modelName = "";

        // 2. Fetch Models once with Spinner
        List<string> availableModels = new();
        await AnsiConsole.Status()
            .StartAsync("利用可能なOllamaモデルを読み込み中...", async ctx =>
            {
                availableModels = await ollamaClient.GetAvailableModelsAsync();
                ctx.Status("モデルが取得できました");
            });

        if (availableModels.Count > 0)
        {
            modelName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("使用する[green]Ollamaモデル[/]を選択してください:")
                    .PageSize(10)
                    .MoreChoicesText("[grey](上下キーで選択)[/]")
                    .AddChoices(availableModels));
        }
        else
        {
            modelName = "gemma4:26b";
        }

        AnsiConsole.MarkupLine($"[green]✔[/] 使用中のモデル: [bold blue]{Markup.Escape(modelName)}[/]");
        AnsiConsole.MarkupLine("[grey]ヒント: '/' でコマンド補完、'@' でファイル添付補完が利用できます。推論中の Ctrl+C で応答を中断します。[/]");
        AnsiConsole.WriteLine();

        // Setup PrettyPrompt with persistent history and completions under ~/.kotli/<SessionId>/
        var kotliDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".kotli",
            session.SessionId);
        Directory.CreateDirectory(kotliDir);
        var historyFile = Path.Combine(kotliDir, "history");

        var prettyPrompt = new Prompt(
            persistentHistoryFilepath: historyFile,
            callbacks: new KotliPromptCallbacks(),
            configuration: new PromptConfiguration(
                prompt: new FormattedString("kotli> ", new FormatSpan(0, 6, AnsiColor.BrightBlue))));

        CancellationTokenSource? activeTurnCts = null;
        Console.CancelKeyPress += (_, e) =>
        {
            if (activeTurnCts != null && !activeTurnCts.IsCancellationRequested)
            {
                e.Cancel = true;
                activeTurnCts.Cancel();
            }
        };

        // 3. Main Conversation Loop
        while (true)
        {
            var promptResult = await prettyPrompt.ReadLineAsync();
            if (!promptResult.IsSuccess)
            {
                // User pressed Ctrl+C at the input prompt
                AnsiConsole.MarkupLine("[grey]終了するには /exit または exit を入力してください。[/]");
                continue;
            }

            var prompt = promptResult.Text;
            if (string.IsNullOrWhiteSpace(prompt)) continue;

            var trimmedPrompt = prompt.Trim();

            if (trimmedPrompt.Equals("/exit", StringComparison.OrdinalIgnoreCase) ||
                trimmedPrompt.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                trimmedPrompt.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (trimmedPrompt.Equals("/help", StringComparison.OrdinalIgnoreCase))
            {
                RenderHelpTable();
                continue;
            }

            if (trimmedPrompt.Equals("/clear", StringComparison.OrdinalIgnoreCase) ||
                trimmedPrompt.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                session.Clear();
                RenderWelcomeHeader();
                AnsiConsole.MarkupLine("[green]✔[/] 画面と会話セッション履歴をクリアしました。");
                AnsiConsole.WriteLine();
                continue;
            }

            if (trimmedPrompt.Equals("/model", StringComparison.OrdinalIgnoreCase))
            {
                var models = await ollamaClient.GetAvailableModelsAsync();
                modelName = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("変更先の[green]Ollamaモデル[/]を選択してください:")
                        .PageSize(10)
                        .MoreChoicesText("[grey](上下キーで選択)[/]")
                        .AddChoices(models));

                AnsiConsole.MarkupLine($"[green]✔[/] モデルを [bold blue]{Markup.Escape(modelName)}[/] に切り替えました。");
                AnsiConsole.WriteLine();
                continue;
            }

            if (trimmedPrompt.StartsWith("/memory", StringComparison.OrdinalIgnoreCase))
            {
                await HandleMemoryCommandAsync(trimmedPrompt, memoryStore);
                continue;
            }

            if (trimmedPrompt.Equals("/diff", StringComparison.OrdinalIgnoreCase))
            {
                var diff = await workspaceProvider.GetGitDiffAsync();
                AnsiConsole.Write(new Panel(Markup.Escape(diff))
                {
                    Header = new PanelHeader("[bold cyan]Git Diff[/]"),
                    Border = BoxBorder.Rounded
                });
                AnsiConsole.WriteLine();
                continue;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule($"[bold cyan]Kotli ({Markup.Escape(modelName)})[/]") { Justification = Justify.Left });

            activeTurnCts = CancellationTokenSource.CreateLinkedTokenSource(promptResult.CancellationToken);

            try
            {
                await useCase.ExecuteAsync(
                    session,
                    modelName,
                    prompt,
                    chunk =>
                    {
                        ClearInlineStatusLine();
                        Console.Write(chunk);
                    },
                    progress =>
                    {
                        if (progress.Stage == "オンライン")
                        {
                            ClearInlineStatusLine();
                        }
                        else if (progress.Stage == "ツール実行中" && !string.IsNullOrEmpty(progress.Details))
                        {
                            ClearInlineStatusLine();
                            AnsiConsole.MarkupLine($"[grey]⚙ {Markup.Escape(progress.Details)}[/]");
                        }
                        else if (!string.IsNullOrEmpty(progress.Details))
                        {
                            WriteInlineStatusLine($"⏳ {progress.Stage}: {progress.Details}");
                        }
                    },
                    activeTurnCts.Token);
            }
            catch (OperationCanceledException)
            {
                ClearInlineStatusLine();
                AnsiConsole.MarkupLine("\n[yellow]⚠ 応答の生成を中断しました。[/]");
            }
            catch (Exception ex)
            {
                ClearInlineStatusLine();
                AnsiConsole.MarkupLine($"\n[red][エラー] 処理中に問題が発生しました: {Markup.Escape(ex.Message)}[/]");
                AnsiConsole.MarkupLine("[grey]対処方法: Ollamaが起動しているか確認するか、/model で別のモデルを選択してください。[/]");
            }
            finally
            {
                ClearInlineStatusLine();
                activeTurnCts.Dispose();
                activeTurnCts = null;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine();
        }
    }

    private static void RenderWelcomeHeader()
    {
        AnsiConsole.Clear();

        AnsiConsole.Write(
            new FigletText("KOTLI")
                .Centered()
                .Color(Color.Blue));

        AnsiConsole.Write(
            new Panel(
                new Markup("[cyan]🐦 Kotli AI CLI アシスタント[/]\n" +
                           "[grey]ローカルOllama駆動の超高速DDD設計パートナー[/]\n\n" +
                           "コマンド一覧は [bold cyan]/help[/] 、モデル変更は [bold green]/model[/] 、履歴クリアは [bold yellow]/clear[/]\n" +
                           "ファイル添付は [bold yellow]@ファイル名[/] 、終了は [bold red]/exit[/] を入力。")
            )
            {
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 1, 1, 1),
                Header = new PanelHeader("[yellow]Welcome[/]")
            });
    }

    private static void RenderHelpTable()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold cyan]コマンド / 記法[/]")
            .AddColumn("[bold white]説明[/]");

        table.AddRow("[cyan]/help[/]", "このヘルプ一覧を表示します");
        table.AddRow("[cyan]/model[/]", "使用するOllamaモデルを切り替えます");
        table.AddRow("[cyan]/clear[/]", "画面と現在の会話セッション履歴をクリアします");
        table.AddRow("[cyan]/memory[/]", "セッションメモリ (~/.kotli/<セッションID>/MEMORY.md) の一覧表示、または `/memory add <Key> <Content>` で追加します");
        table.AddRow("[cyan]/diff[/]", "現在のワークスペースの未コミット変更 (git diff) を表示します");
        table.AddRow("[cyan]/exit[/]", "Kotli CLI を終了します");
        table.AddRow("[yellow]@path/to/file[/]", "指定したファイルをプロンプトに自動添付してAIに送信します");
        table.AddRow("[grey]Ctrl+C[/]", "AIの応答生成・ツール実行を安全に中断します");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static async Task HandleMemoryCommandAsync(string input, MarkdownMemoryStore memoryStore)
    {
        var parts = input.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4 && parts[1].Equals("add", StringComparison.OrdinalIgnoreCase))
        {
            var key = parts[2];
            var content = parts[3];
            var msg = await memoryStore.SaveMemoryToolAsync(key, content);
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(msg)}[/]");
            AnsiConsole.WriteLine();
            return;
        }

        var items = (await memoryStore.LoadAllAsync()).ToList();
        if (items.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]保存されているメモリはありません。`/memory add <キー> <内容>` で追加できます。[/]");
            AnsiConsole.WriteLine();
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[bold yellow]~/.kotli/{Markup.Escape(memoryStore.SessionId)}/MEMORY.md[/]")
            .AddColumn("[bold cyan]キー[/]")
            .AddColumn("[bold white]内容[/]");

        foreach (var item in items)
        {
            table.AddRow(Markup.Escape(item.Key), Markup.Escape(item.Content));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }
}
