using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PrettyPrompt;
using PrettyPrompt.Completion;
using PrettyPrompt.Consoles;
using PrettyPrompt.Documents;
using PrettyPrompt.Highlighting;

namespace Kotli.Cli.UI;

public class KotliPromptCallbacks : PromptCallbacks
{
    private static readonly (string Command, string Description)[] SlashCommands =
    {
        ("/help", "利用可能なコマンドと使い方を表示します"),
        ("/model", "使用するOllamaモデルを切り替えます"),
        ("/clear", "画面と現在の会話セッション履歴をクリアします"),
        ("/memory", "保存されているプロジェクトの永続メモリ (.kotli/MEMORY.md) を表示・追加します"),
        ("/diff", "現在のワークスペースの git diff を表示します"),
        ("/exit", "Kotli CLI を終了します")
    };

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", ".vs", ".idea", ".kotli"
    };

    private static readonly Regex HighlightRegex = new(@"(?:^|\s)(?<cmd>/[a-zA-Z0-9_-]+)|(?:^|\s)(?<file>@[^\s]+)", RegexOptions.Compiled);

    private readonly string _workspaceRoot;

    public KotliPromptCallbacks(string? workspaceRoot = null)
    {
        _workspaceRoot = workspaceRoot ?? Directory.GetCurrentDirectory();
    }

    protected override Task<bool> ShouldOpenCompletionWindowAsync(
        string text,
        int caret,
        KeyPress keyPress,
        CancellationToken cancellationToken)
    {
        if (caret > 0 && caret <= text.Length)
        {
            char justTyped = text[caret - 1];
            if (justTyped == '/' && caret == 1)
            {
                return Task.FromResult(true);
            }
            if (justTyped == '@' && (caret == 1 || char.IsWhiteSpace(text[caret - 2])))
            {
                return Task.FromResult(true);
            }
        }

        return base.ShouldOpenCompletionWindowAsync(text, caret, keyPress, cancellationToken);
    }

    protected override Task<TextSpan> GetSpanToReplaceByCompletionAsync(
        string text,
        int caret,
        CancellationToken cancellationToken)
    {
        int start = caret;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            start--;
        }

        int end = caret;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        return Task.FromResult(new TextSpan(start, end - start));
    }

    protected override Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(
        string text,
        int caret,
        TextSpan spanToBeReplaced,
        CancellationToken cancellationToken)
    {
        if (spanToBeReplaced.Start >= text.Length)
        {
            return Task.FromResult<IReadOnlyList<CompletionItem>>(Array.Empty<CompletionItem>());
        }

        string token = text.Substring(spanToBeReplaced.Start, spanToBeReplaced.Length);

        // 1. Slash command completion (at the beginning of the prompt)
        if (spanToBeReplaced.Start == 0 && token.StartsWith('/'))
        {
            var items = SlashCommands
                .Where(c => c.Command.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                .Select(c => new CompletionItem(
                    replacementText: c.Command,
                    displayText: new FormattedString(c.Command, new FormatSpan(0, c.Command.Length, AnsiColor.Cyan)),
                    filterText: c.Command,
                    getExtendedDescription: _ => Task.FromResult(new FormattedString(c.Description))))
                .ToList();

            return Task.FromResult<IReadOnlyList<CompletionItem>>(items);
        }

        // 2. @file completion
        if (token.StartsWith('@'))
        {
            string query = token[1..];
            var files = GetWorkspaceRelativeFiles()
                .Where(f => string.IsNullOrEmpty(query) ||
                            f.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(40)
                .Select(f =>
                {
                    var replacement = "@" + f;
                    return new CompletionItem(
                        replacementText: replacement,
                        displayText: new FormattedString(replacement, new FormatSpan(0, replacement.Length, AnsiColor.Yellow)),
                        filterText: replacement,
                        getExtendedDescription: _ => Task.FromResult(new FormattedString($"ファイルをコンテキストに添付: {f}")));
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<CompletionItem>>(files);
        }

        return Task.FromResult<IReadOnlyList<CompletionItem>>(Array.Empty<CompletionItem>());
    }

    protected override Task<IReadOnlyCollection<FormatSpan>> HighlightCallbackAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var spans = new List<FormatSpan>();
        foreach (Match match in HighlightRegex.Matches(text))
        {
            if (match.Groups["cmd"].Success)
            {
                var g = match.Groups["cmd"];
                spans.Add(new FormatSpan(g.Index, g.Length, AnsiColor.Cyan));
            }
            else if (match.Groups["file"].Success)
            {
                var g = match.Groups["file"];
                spans.Add(new FormatSpan(g.Index, g.Length, AnsiColor.Yellow));
            }
        }

        return Task.FromResult<IReadOnlyCollection<FormatSpan>>(spans);
    }

    private IEnumerable<string> GetWorkspaceRelativeFiles()
    {
        var dirs = new Stack<string>();
        dirs.Push(_workspaceRoot);

        while (dirs.Count > 0)
        {
            var current = dirs.Pop();
            string[] files;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return Path.GetRelativePath(_workspaceRoot, file).Replace('\\', '/');
            }

            try
            {
                foreach (var subDir in Directory.GetDirectories(current).OrderByDescending(d => d))
                {
                    var name = Path.GetFileName(subDir);
                    if (!ExcludedDirectories.Contains(name))
                    {
                        dirs.Push(subDir);
                    }
                }
            }
            catch
            {
                // Ignore inaccessible directories
            }
        }
    }
}
