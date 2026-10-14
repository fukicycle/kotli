using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kotli.Domain.Interfaces;

namespace Kotli.Infrastructure.Tools;

public class LocalCommandTool : ICommandTool
{
    private readonly string _basePath;
    private readonly string _basePathWithSeparator;
    private readonly TimeSpan _timeout;
    private readonly int _maxOutputChars;
    private readonly string _shellExecutable;
    private readonly string _shellSwitch;

    public LocalCommandTool(
        string? basePath = null,
        TimeSpan? timeout = null,
        int maxOutputChars = 8000,
        string? shellExecutable = null,
        string? shellSwitch = null)
    {
        _basePath = Path.GetFullPath(basePath ?? Directory.GetCurrentDirectory())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _basePathWithSeparator = _basePath + Path.DirectorySeparatorChar;
        _timeout = timeout ?? TimeSpan.FromSeconds(60);
        _maxOutputChars = maxOutputChars;
        _shellExecutable = shellExecutable ?? "/bin/sh";
        _shellSwitch = shellSwitch ?? "-c";
    }

    private bool IsWithinWorkspace(string fullPath)
    {
        return string.Equals(fullPath, _basePath, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(_basePathWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> ExecuteCommandAsync(string command, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return "[エラー] 実行するコマンドが空です。";
        }

        var targetDir = string.IsNullOrWhiteSpace(workingDirectory)
            ? _basePath
            : Path.GetFullPath(Path.Combine(_basePath, workingDirectory));

        if (!IsWithinWorkspace(targetDir))
        {
            return $"[エラー] アクセスが拒否されました。作業ディレクトリはワークスペース内である必要があります: {_basePath}";
        }

        if (!Directory.Exists(targetDir))
        {
            return $"[エラー] 作業ディレクトリが見つかりません: {workingDirectory}";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _shellExecutable,
            WorkingDirectory = targetDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add(_shellSwitch);
        startInfo.ArgumentList.Add(command);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return $"[エラー] コマンドの起動に失敗しました: {ex.Message}";
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(_timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return $"[エラー] コマンドの実行がタイムアウトしました ({_timeout.TotalSeconds:0.#}秒): {command}";
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        var sb = new StringBuilder();
        var statusLabel = process.ExitCode == 0 ? "[成功]" : "[失敗]";
        sb.AppendLine($"{statusLabel} コマンド終了コード: {process.ExitCode}");

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            sb.AppendLine("--- 標準出力 ---");
            sb.AppendLine(TruncateOutput(stdout.TrimEnd()));
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            sb.AppendLine("--- 標準エラー出力 ---");
            sb.AppendLine(TruncateOutput(stderr.TrimEnd()));
        }

        if (string.IsNullOrWhiteSpace(stdout) && string.IsNullOrWhiteSpace(stderr))
        {
            sb.AppendLine("(出力なし)");
        }

        return sb.ToString().TrimEnd();
    }

    private string TruncateOutput(string text)
    {
        if (text.Length <= _maxOutputChars)
        {
            return text;
        }

        return text[.._maxOutputChars] + $"\n... (出力が上限 {_maxOutputChars} 文字を超えたため省略されました)";
    }
}
