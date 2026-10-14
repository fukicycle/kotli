using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Kotli.Domain.Interfaces;
using Kotli.Domain.Models;

namespace Kotli.Infrastructure.Workspace;

public class GitDiffProvider : IWorkspaceProvider
{
    private static readonly char[] ModifiedStatusChars = { 'M', 'R', 'A', 'D' };
    private readonly string _workingDirectory;

    public GitDiffProvider(string? workingDirectory = null)
    {
        _workingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();
    }

    public async Task<WorkspaceState> GetCurrentStateAsync()
    {
        var isGit = await RunGitCommandAsync("rev-parse --is-inside-work-tree");
        if (string.IsNullOrWhiteSpace(isGit) || !isGit.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return new WorkspaceState("non-git", "", Array.Empty<string>(), Array.Empty<string>());
        }

        // Only run lightweight branch and status commands every turn; full diff is fetched lazily via GetGitDiffAsync()
        var branchTask = RunGitCommandAsync("rev-parse --abbrev-ref HEAD");
        var statusTask = RunGitCommandAsync("status --porcelain");

        await Task.WhenAll(branchTask, statusTask);

        var branch = (await branchTask).Trim();
        var statusLines = (await statusTask)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var modified = new List<string>();
        var untracked = new List<string>();

        foreach (var line in statusLines)
        {
            if (line.Length < 4) continue;
            var statusCode = line[..2];
            var file = line[3..].Trim();

            if (statusCode.Contains('?'))
            {
                untracked.Add(file);
            }
            else if (statusCode.IndexOfAny(ModifiedStatusChars) >= 0)
            {
                modified.Add(file);
            }
        }

        return new WorkspaceState(branch, "", modified, untracked);
    }

    public async Task<string> GetGitDiffAsync()
    {
        var diff = await RunGitCommandAsync("diff HEAD");
        return !string.IsNullOrWhiteSpace(diff) ? diff : "変更はありません。";
    }

    private async Task<string> RunGitCommandAsync(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = _workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch
        {
            return ""; // Git might not be installed or in PATH
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            return "";
        }

        return await outputTask;
    }
}
