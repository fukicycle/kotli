using System.Threading.Tasks;
using Kotli.Domain.Attributes;

namespace Kotli.Domain.Interfaces;

public interface ICommandTool
{
    [KotliTool("execute_command", "ワークスペース内でシェルコマンドを実行し、標準出力・標準エラー出力および終了コードを取得します。ビルド、テスト、パッケージ操作などの実行に使用してください。", requiresApproval: true)]
    Task<string> ExecuteCommandAsync(
        [KotliToolParam("実行するシェルコマンド（例: 'dotnet test', 'git status'）")] string command,
        [KotliToolParam("コマンドを実行する作業ディレクトリ（ワークスペースルートからの相対パス、省略時はワークスペースルート）")] string? workingDirectory = null);
}
