using System.Threading.Tasks;
using Kotli.Domain.Attributes;
using Kotli.Domain.Models;

namespace Kotli.Domain.Interfaces;

public interface IWorkspaceProvider
{
    Task<WorkspaceState> GetCurrentStateAsync();

    [KotliTool("get_git_diff", "現在のワークスペースにおける、コミットされていないGitの変更（git diff）を取得します。コードの変更状況を確認したい時に呼び出してください。")]
    Task<string> GetGitDiffAsync();
}
