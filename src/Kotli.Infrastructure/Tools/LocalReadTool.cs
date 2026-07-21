using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kotli.Domain.Interfaces;

namespace Kotli.Infrastructure.Tools;

public class LocalReadTool : IReadTool
{
    private readonly string _basePath;

    public LocalReadTool(string? basePath = null)
    {
        _basePath = basePath ?? Directory.GetCurrentDirectory();
    }

    public async Task<string> ReadFileAsync(string filePath, int? startLine = null, int? endLine = null)
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
            var lines = await File.ReadAllLinesAsync(fullPath, Encoding.UTF8);
            
            int start = startLine.HasValue ? Math.Max(1, startLine.Value) : 1;
            int end = endLine.HasValue ? Math.Min(lines.Length, endLine.Value) : lines.Length;

            if (start > lines.Length)
            {
                return $"[Error] Start line {start} is beyond the total number of lines ({lines.Length}).";
            }
            if (start > end)
            {
                return $"[Error] Start line {start} cannot be greater than end line {end}.";
            }

            var selectedLines = lines.Skip(start - 1).Take(end - start + 1);
            var sb = new StringBuilder();
            sb.AppendLine($"--- File: {filePath} (Lines {start}-{end} of {lines.Length}) ---");
            foreach (var line in selectedLines)
            {
                sb.AppendLine(line);
            }
            sb.AppendLine("--- End of Section ---");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"[Error] Failed to read file: {ex.Message}";
        }
    }

    public Task<string> ListDirectoryAsync(string dirPath)
    {
        var targetPath = string.IsNullOrWhiteSpace(dirPath) ? _basePath : Path.Combine(_basePath, dirPath);
        var fullPath = Path.GetFullPath(targetPath);

        if (!fullPath.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult($"[Error] Access denied. Path must be inside the workspace directory: {_basePath}");
        }

        if (!Directory.Exists(fullPath))
        {
            return Task.FromResult($"[Error] Directory not found: {dirPath}");
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"--- Directory List: {dirPath} ---");

            var directories = Directory.GetDirectories(fullPath);
            foreach (var dir in directories)
            {
                var relativeName = Path.GetRelativePath(_basePath, dir);
                sb.AppendLine($"[Dir]  {relativeName}/");
            }

            var files = Directory.GetFiles(fullPath);
            foreach (var file in files)
            {
                var relativeName = Path.GetRelativePath(_basePath, file);
                var size = new FileInfo(file).Length;
                sb.AppendLine($"[File] {relativeName} ({size} bytes)");
            }

            return Task.FromResult(sb.ToString());
        }
        catch (Exception ex)
        {
            return Task.FromResult($"[Error] Failed to list directory: {ex.Message}");
        }
    }
}
