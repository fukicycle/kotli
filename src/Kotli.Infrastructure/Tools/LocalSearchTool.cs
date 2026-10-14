using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Kotli.Domain.Interfaces;

namespace Kotli.Infrastructure.Tools;

public class LocalSearchTool : ISearchTool
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", ".vs", ".idea", ".kotli", "dist", "build"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".pdb", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".pdf", ".zip", ".tar", ".gz", ".woff", ".woff2"
    };

    private readonly string _basePath;
    private readonly string _basePathWithSeparator;

    public LocalSearchTool(string? basePath = null)
    {
        _basePath = Path.GetFullPath(basePath ?? Directory.GetCurrentDirectory())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _basePathWithSeparator = _basePath + Path.DirectorySeparatorChar;
    }

    private bool IsWithinWorkspace(string fullPath)
    {
        return string.Equals(fullPath, _basePath, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(_basePathWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    public Task<string> FindFilesAsync(string pattern = "*")
    {
        var regex = GlobToRegex(string.IsNullOrWhiteSpace(pattern) ? "*" : pattern.Trim());
        var matchedFiles = new List<string>();
        const int maxResults = 100;

        foreach (var file in EnumerateWorkspaceFiles(_basePath))
        {
            var relativePath = Path.GetRelativePath(_basePath, file).Replace('\\', '/');
            var fileName = Path.GetFileName(file);

            if (regex.IsMatch(fileName) || regex.IsMatch(relativePath) ||
                relativePath.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                matchedFiles.Add(relativePath);
                if (matchedFiles.Count >= maxResults)
                {
                    break;
                }
            }
        }

        if (matchedFiles.Count == 0)
        {
            return Task.FromResult($"パターン '{pattern}' に一致するファイルは見つかりませんでした。");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"--- ファイル検索結果: '{pattern}' ({matchedFiles.Count}件) ---");
        foreach (var f in matchedFiles)
        {
            sb.AppendLine(f);
        }
        if (matchedFiles.Count >= maxResults)
        {
            sb.AppendLine($"... (上限 {maxResults} 件に達したため省略されました)");
        }
        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public async Task<string> GrepSearchAsync(string query, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "[エラー] 検索クエリが空です。";
        }

        string targetRoot = string.IsNullOrWhiteSpace(path)
            ? _basePath
            : Path.GetFullPath(Path.Combine(_basePath, path));

        if (!IsWithinWorkspace(targetRoot))
        {
            return $"[エラー] アクセスが拒否されました。パスはワークスペースディレクトリ内である必要があります: {_basePath}";
        }

        Regex regex;
        try
        {
            regex = new Regex(query, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        catch
        {
            regex = new Regex(Regex.Escape(query), RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        var candidateFiles = File.Exists(targetRoot)
            ? new[] { targetRoot }
            : Directory.Exists(targetRoot)
                ? EnumerateWorkspaceFiles(targetRoot)
                : Enumerable.Empty<string>();

        var matches = new List<string>();
        const int maxMatches = 50;

        foreach (var file in candidateFiles)
        {
            if (IsBinaryFile(file)) continue;

            try
            {
                var lines = await File.ReadAllLinesAsync(file, Encoding.UTF8);
                var relativePath = Path.GetRelativePath(_basePath, file).Replace('\\', '/');

                for (int i = 0; i < lines.Length; i++)
                {
                    if (regex.IsMatch(lines[i]))
                    {
                        var trimmedLine = lines[i].Length > 160 ? lines[i][..160] + "..." : lines[i];
                        matches.Add($"{relativePath}:{i + 1}: {trimmedLine}");
                        if (matches.Count >= maxMatches)
                        {
                            break;
                        }
                    }
                }

                if (matches.Count >= maxMatches)
                {
                    break;
                }
            }
            catch
            {
                // Skip unreadable files
            }
        }

        if (matches.Count == 0)
        {
            return $"クエリ '{query}' に一致する箇所は見つかりませんでした。";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"--- Grep検索結果: '{query}' ({matches.Count}件) ---");
        foreach (var m in matches)
        {
            sb.AppendLine(m);
        }
        if (matches.Count >= maxMatches)
        {
            sb.AppendLine($"... (上限 {maxMatches} 件に達したため省略されました)");
        }
        return sb.ToString().TrimEnd();
    }

    private static IEnumerable<string> EnumerateWorkspaceFiles(string rootDir)
    {
        var dirs = new Stack<string>();
        dirs.Push(rootDir);

        while (dirs.Count > 0)
        {
            var currentDir = dirs.Pop();
            string[] files;
            string[] subDirs;
            try
            {
                files = Directory.GetFiles(currentDir);
                subDirs = Directory.GetDirectories(currentDir);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var subDir in subDirs.OrderByDescending(d => d))
            {
                var dirName = Path.GetFileName(subDir);
                if (!ExcludedDirectories.Contains(dirName))
                {
                    dirs.Push(subDir);
                }
            }
        }
    }

    private static bool IsBinaryFile(string filePath)
    {
        return BinaryExtensions.Contains(Path.GetExtension(filePath));
    }

    private static Regex GlobToRegex(string glob)
    {
        var pattern = "^" + Regex.Escape(glob).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
