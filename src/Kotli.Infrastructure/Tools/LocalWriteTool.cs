using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Kotli.Domain.Interfaces;

namespace Kotli.Infrastructure.Tools;

public class LocalWriteTool : IWriteTool
{
    private readonly string _basePath;
    private readonly string _basePathWithSeparator;

    public LocalWriteTool(string? basePath = null)
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

    public async Task<string> WriteFileAsync(string filePath, string content)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, filePath));
        if (!IsWithinWorkspace(fullPath))
        {
            return $"[エラー] アクセスが拒否されました。パスはワークスペースディレクトリ内である必要があります: {_basePath}";
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8);
            return $"[成功] ファイル '{filePath}' が正常に書き込まれました。";
        }
        catch (Exception ex)
        {
            return $"[エラー] ファイルの書き込みに失敗しました: {ex.Message}";
        }
    }

    public async Task<string> ReplaceTextAsync(string filePath, string oldString, string newString)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, filePath));
        if (!IsWithinWorkspace(fullPath))
        {
            return $"[エラー] アクセスが拒否されました。パスはワークスペースディレクトリ内である必要があります: {_basePath}";
        }

        if (!File.Exists(fullPath))
        {
            return $"[エラー] ファイルが見つかりません: {filePath}";
        }

        try
        {
            var fileContent = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);

            // Check count of oldString to prevent ambiguous or wrong edits
            int index = fileContent.IndexOf(oldString, StringComparison.Ordinal);
            if (index == -1)
            {
                return $"[エラー] 置換対象の文字列が '{filePath}' 内で見つかりませんでした。正確なテキストを指定してください。";
            }

            int lastIndex = fileContent.LastIndexOf(oldString, StringComparison.Ordinal);
            if (index != lastIndex)
            {
                return $"[エラー] 置換対象の文字列が '{filePath}' 内で複数検出されました。ユニークな置換にするため、より多くの前後のコンテキストを指定してください。";
            }

            var updatedContent = fileContent.Replace(oldString, newString, StringComparison.Ordinal);
            await File.WriteAllTextAsync(fullPath, updatedContent, Encoding.UTF8);

            return $"[成功] '{filePath}' 内のテキストの置換が正常に完了しました。";
        }
        catch (Exception ex)
        {
            return $"[エラー] テキストの置換に失敗しました: {ex.Message}";
        }
    }

    public async Task<string> GetWriteDiffPreviewAsync(string filePath, string content)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, filePath));
        if (!IsWithinWorkspace(fullPath))
        {
            return $"[エラー] ワークスペース外のパスです: {filePath}";
        }

        string oldContent = File.Exists(fullPath)
            ? await File.ReadAllTextAsync(fullPath, Encoding.UTF8)
            : "";

        bool isNewFile = !File.Exists(fullPath);
        return BuildUnifiedDiff(filePath, oldContent, content, isNewFile);
    }

    public async Task<string> GetReplaceDiffPreviewAsync(string filePath, string oldString, string newString)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, filePath));
        if (!IsWithinWorkspace(fullPath) || !File.Exists(fullPath))
        {
            return BuildUnifiedDiff(filePath, oldString, newString, isNewFile: false);
        }

        var oldContent = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);
        int index = oldContent.IndexOf(oldString, StringComparison.Ordinal);
        if (index == -1)
        {
            return BuildUnifiedDiff(filePath, oldString, newString, isNewFile: false);
        }

        var newContent = oldContent.Replace(oldString, newString, StringComparison.Ordinal);
        return BuildUnifiedDiff(filePath, oldContent, newContent, isNewFile: false);
    }

    private static string BuildUnifiedDiff(string filePath, string oldText, string newText, bool isNewFile)
    {
        var sb = new StringBuilder();
        sb.AppendLine(isNewFile ? $"--- /dev/null" : $"--- a/{filePath}");
        sb.AppendLine($"+++ b/{filePath}");

        var oldLines = string.IsNullOrEmpty(oldText) ? Array.Empty<string>() : oldText.Replace("\r\n", "\n").Split('\n');
        var newLines = string.IsNullOrEmpty(newText) ? Array.Empty<string>() : newText.Replace("\r\n", "\n").Split('\n');

        // Find common prefix and suffix to display a compact contextual diff
        int prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix])
        {
            prefix++;
        }

        int oldSuffix = oldLines.Length - 1;
        int newSuffix = newLines.Length - 1;
        while (oldSuffix >= prefix && newSuffix >= prefix && oldLines[oldSuffix] == newLines[newSuffix])
        {
            oldSuffix--;
            newSuffix--;
        }

        int contextStart = Math.Max(0, prefix - 2);
        for (int i = contextStart; i < prefix; i++)
        {
            sb.AppendLine($"  {oldLines[i]}");
        }

        for (int i = prefix; i <= oldSuffix; i++)
        {
            sb.AppendLine($"- {oldLines[i]}");
        }

        for (int i = prefix; i <= newSuffix; i++)
        {
            sb.AppendLine($"+ {newLines[i]}");
        }

        int contextEnd = Math.Min(oldLines.Length - 1, oldSuffix + 2);
        for (int i = oldSuffix + 1; i <= contextEnd; i++)
        {
            sb.AppendLine($"  {oldLines[i]}");
        }

        return sb.ToString().TrimEnd();
    }
}
