using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kotli.Domain.Interfaces;
using Kotli.Domain.Models;

namespace Kotli.Infrastructure.Storage;

public class MarkdownMemoryStore : IMemoryStore
{
    private readonly string _filePath;

    public string SessionId { get; }
    public string FilePath => _filePath;

    public MarkdownMemoryStore(string? sessionId = null, string? kotliRootPath = null)
    {
        SessionId = string.IsNullOrWhiteSpace(sessionId)
            ? Guid.NewGuid().ToString("N")
            : sessionId.Trim();

        var rootDir = string.IsNullOrWhiteSpace(kotliRootPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kotli")
            : kotliRootPath;

        var sessionDir = Path.Combine(rootDir, SessionId);
        _filePath = Path.Combine(sessionDir, "MEMORY.md");
    }

    public async Task<IEnumerable<MemoryItem>> LoadAllAsync()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<MemoryItem>();
        }

        try
        {
            var content = await File.ReadAllTextAsync(_filePath, Encoding.UTF8);
            return ParseMarkdown(content);
        }
        catch
        {
            return Array.Empty<MemoryItem>();
        }
    }

    public async Task SaveAsync(MemoryItem memoryItem)
    {
        var items = (await LoadAllAsync()).ToList();

        // Remove existing item with the same key
        items.RemoveAll(i => i.Key.Equals(memoryItem.Key, StringComparison.OrdinalIgnoreCase));

        // Add new item
        items.Add(memoryItem with { UpdatedAt = DateTime.UtcNow });

        await WriteAllAsync(items);
    }

    public async Task<string> SaveMemoryToolAsync(string key, string content)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "[エラー] メモリのキー名が指定されていません。";
        }

        try
        {
            await SaveAsync(new MemoryItem(key.Trim(), content ?? "", DateTime.UtcNow));
            return $"[成功] メモリ項目 '{key.Trim()}' を ~/.kotli/{SessionId}/MEMORY.md に保存しました。";
        }
        catch (Exception ex)
        {
            return $"[エラー] メモリの保存に失敗しました: {ex.Message}";
        }
    }

    public async Task DeleteAsync(string key)
    {
        var items = (await LoadAllAsync()).ToList();
        int initialCount = items.Count;
        items.RemoveAll(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (items.Count != initialCount)
        {
            await WriteAllAsync(items);
        }
    }

    private async Task WriteAllAsync(IEnumerable<MemoryItem> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        var sb = new StringBuilder();
        sb.AppendLine("# Kotli メモリ");
        sb.AppendLine();
        sb.AppendLine("このファイルは、Kotli用のプロジェクト固有の永続的なメモリコンテキストを保存します。");
        sb.AppendLine("このファイルは手動で編集して構いません。メモリのエントリの見出しには '## キー名' を使用してください。");
        sb.AppendLine();

        foreach (var item in items)
        {
            sb.AppendLine($"## {item.Key}");
            sb.AppendLine(item.Content.Trim());
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(_filePath, sb.ToString(), Encoding.UTF8);
    }

    private List<MemoryItem> ParseMarkdown(string markdown)
    {
        var list = new List<MemoryItem>();
        var lines = markdown.Split(new[] { '\r', '\n' }, StringSplitOptions.None);

        string? currentKey = null;
        var currentContent = new StringBuilder();

        foreach (var line in lines)
        {
            if (line.StartsWith("## "))
            {
                // Save previous item
                if (currentKey != null)
                {
                    list.Add(new MemoryItem(currentKey, currentContent.ToString().Trim(), File.GetLastWriteTimeUtc(_filePath)));
                    currentContent.Clear();
                }

                currentKey = line[3..].Trim();
            }
            else if (currentKey != null)
            {
                currentContent.AppendLine(line);
            }
        }

        // Save last item
        if (currentKey != null)
        {
            list.Add(new MemoryItem(currentKey, currentContent.ToString().Trim(), File.GetLastWriteTimeUtc(_filePath)));
        }

        return list;
    }
}
