using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kotli.Domain.Interfaces;
using Kotli.Domain.Models;
using Spectre.Console;

namespace Kotli.Cli.UI;

public class ConsoleToolApprovalService : IToolApprovalService
{
    private readonly Action? _onBeforePrompt;

    public ConsoleToolApprovalService(Action? onBeforePrompt = null)
    {
        _onBeforePrompt = onBeforePrompt;
    }

    public Task<ToolApprovalDecision> RequestApprovalAsync(
        ToolCall toolCall,
        string? diffPreview = null,
        CancellationToken cancellationToken = default)
    {
        _onBeforePrompt?.Invoke();

        AnsiConsole.WriteLine();

        var markupBuilder = new StringBuilder();
        markupBuilder.AppendLine($"[bold yellow]ツール呼び出し:[/] [cyan]{Markup.Escape(toolCall.Name)}[/]");

        if (!string.IsNullOrWhiteSpace(diffPreview))
        {
            markupBuilder.AppendLine();
            var lines = diffPreview.Replace("\r\n", "\n").Split('\n');
            int displayedLines = 0;
            const int maxPreviewLines = 40;

            foreach (var line in lines)
            {
                if (displayedLines >= maxPreviewLines)
                {
                    markupBuilder.AppendLine($"[grey]... (他 {lines.Length - displayedLines} 行の差分を省略)[/]");
                    break;
                }

                var escaped = Markup.Escape(line);
                if (line.StartsWith("--- ") || line.StartsWith("+++ "))
                {
                    markupBuilder.AppendLine($"[bold blue]{escaped}[/]");
                }
                else if (line.StartsWith("+"))
                {
                    markupBuilder.AppendLine($"[green]{escaped}[/]");
                }
                else if (line.StartsWith("-"))
                {
                    markupBuilder.AppendLine($"[red]{escaped}[/]");
                }
                else
                {
                    markupBuilder.AppendLine($"[grey]{escaped}[/]");
                }

                displayedLines++;
            }
        }
        else
        {
            markupBuilder.AppendLine($"[grey]引数: {Markup.Escape(toolCall.ArgumentsJson)}[/]");
        }

        var panel = new Panel(new Markup(markupBuilder.ToString().TrimEnd()))
        {
            Header = new PanelHeader("[bold yellow]⚠ ファイル変更の確認[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow),
            Padding = new Padding(1, 0, 1, 0)
        };

        AnsiConsole.Write(panel);

        const string allowOnceChoice = "✔ 許可する (今回のみ)";
        const string allowAlwaysChoice = "✔✔ セッション中は常に許可する";
        const string rejectChoice = "✖ 拒否する (変更をキャンセル)";

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]この変更を適用しますか？[/]")
                .HighlightStyle(new Style(Color.Cyan1))
                .AddChoices(allowOnceChoice, allowAlwaysChoice, rejectChoice));

        var decision = choice switch
        {
            allowOnceChoice => ToolApprovalDecision.AllowOnce,
            allowAlwaysChoice => ToolApprovalDecision.AllowAlwaysInSession,
            _ => ToolApprovalDecision.Reject
        };

        if (decision == ToolApprovalDecision.Reject)
        {
            AnsiConsole.MarkupLine("[red]✖ 変更をキャンセルしました。[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[green]✔ 変更を承認しました。[/]");
        }

        return Task.FromResult(decision);
    }
}
