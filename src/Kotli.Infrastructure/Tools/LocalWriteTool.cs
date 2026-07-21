using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Kotli.Domain.Interfaces;

namespace Kotli.Infrastructure.Tools;

public class LocalWriteTool : IWriteTool
{
    private readonly string _basePath;

    public LocalWriteTool(string? basePath = null)
    {
        _basePath = basePath ?? Directory.GetCurrentDirectory();
    }

    public async Task<string> WriteFileAsync(string filePath, string content)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, filePath));
        if (!fullPath.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
        {
            return $"[Error] Access denied. Path must be inside the workspace directory: {_basePath}";
        }

        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8);
            return $"[Success] File '{filePath}' written successfully.";
        }
        catch (Exception ex)
        {
            return $"[Error] Failed to write file: {ex.Message}";
        }
    }

    public async Task<string> ReplaceTextAsync(string filePath, string oldString, string newString)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, filePath));
        if (!fullPath.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
        {
            return $"[Error] Access denied. Path must be inside the workspace directory: {_basePath}";
        }

        if (!File.Exists(fullPath))
        {
            return $"[Error] File not found: {filePath}";
        }

        try
        {
            var fileContent = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);
            
            // Check count of oldString to prevent ambiguous or wrong edits
            int index = fileContent.IndexOf(oldString, StringComparison.Ordinal);
            if (index == -1)
            {
                return $"[Error] The target string to replace was not found in '{filePath}'. Make sure you provide the exact text.";
            }

            int lastIndex = fileContent.LastIndexOf(oldString, StringComparison.Ordinal);
            if (index != lastIndex)
            {
                return $"[Error] Multiple occurrences of the target string were found in '{filePath}'. Please provide more surrounding context to make the replacement unique.";
            }

            var updatedContent = fileContent.Replace(oldString, newString, StringComparison.Ordinal);
            await File.WriteAllTextAsync(fullPath, updatedContent, Encoding.UTF8);
            
            return $"[Success] Replaced text in '{filePath}' successfully.";
        }
        catch (Exception ex)
        {
            return $"[Error] Failed to replace text: {ex.Message}";
        }
    }
}
