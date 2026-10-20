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
    private readonly string _basePathWithSeparator;

    public LocalReadTool(string? basePath = null)
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

    public async Task<string> ReadFileAsync(string filePath, int? startLine = null, int? endLine = null)
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
            var lines = await File.ReadAllLinesAsync(fullPath, Encoding.UTF8);

            if (lines.Length == 0)
            {
                return $"--- ファイル: {filePath} (空のファイル) ---\n--- セクションの終了 ---";
            }

            int start = startLine.HasValue ? Math.Max(1, startLine.Value) : 1;
            int end = endLine.HasValue ? Math.Min(lines.Length, endLine.Value) : lines.Length;

            if (start > lines.Length)
            {
                return $"[エラー] 開始行 {start} は総行数 ({lines.Length}) を超えています。";
            }
            if (start > end)
            {
                return $"[エラー] 開始行 {start} は終了行 {end} より大きくすることはできません。";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"--- ファイル: {filePath} ({lines.Length}行中 {start}-{end}行目) ---");
            for (int i = start; i <= end; i++)
            {
                sb.AppendLine($"{i,4}: {lines[i - 1]}");
            }
            sb.AppendLine("--- セクションの終了 ---");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"[エラー] ファイルの読み込みに失敗しました: {ex.Message}";
        }
    }

    public Task<string> ListDirectoryAsync(string dirPath)
    {
        var targetPath = string.IsNullOrWhiteSpace(dirPath) ? _basePath : Path.Combine(_basePath, dirPath);
        var fullPath = Path.GetFullPath(targetPath);

        if (!IsWithinWorkspace(fullPath))
        {
            return Task.FromResult($"[エラー] アクセスが拒否されました。パスはワークスペースディレクトリ内である必要があります: {_basePath}");
        }

        if (!Directory.Exists(fullPath))
        {
            return Task.FromResult($"[エラー] ディレクトリが見つかりません: {dirPath}");
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"--- ディレクトリ一覧: {(string.IsNullOrWhiteSpace(dirPath) ? "." : dirPath)} ---");

            var directories = Directory.GetDirectories(fullPath);
            foreach (var dir in directories)
            {
                var relativeName = Path.GetRelativePath(_basePath, dir).Replace('\\', '/');
                sb.AppendLine($"[ディレクトリ]  {relativeName}/");
            }

            var files = Directory.GetFiles(fullPath);
            foreach (var file in files)
            {
                var relativeName = Path.GetRelativePath(_basePath, file).Replace('\\', '/');
                var size = new FileInfo(file).Length;
                sb.AppendLine($"[ファイル] {relativeName} ({size} バイト)");
            }

            return Task.FromResult(sb.ToString());
        }
        catch (Exception ex)
        {
            return Task.FromResult($"[エラー] ディレクトリ一覧の取得に失敗しました: {ex.Message}");
        }
    }
}
