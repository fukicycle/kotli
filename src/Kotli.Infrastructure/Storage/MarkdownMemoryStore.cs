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

    public MarkdownMemoryStore(string? basePath = null)
    {
        var dir = basePath ?? Path.Combine(Directory.GetCurrentDirectory(), ".kotli");
        _filePath = Path.Combine(dir, "MEMORY.md");
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
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Kotli Memory");
        sb.AppendLine();
        sb.AppendLine("This file stores persistent project-specific memory context for Kotli.");
        sb.AppendLine("Feel free to edit this file manually. Use '## Key' as headers for memory entries.");
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
