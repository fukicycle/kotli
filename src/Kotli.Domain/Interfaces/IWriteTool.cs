using System.Threading.Tasks;
using Kotli.Domain.Attributes;

namespace Kotli.Domain.Interfaces;

public interface IWriteTool
{
    [KotliTool("write_file", "ファイルに完全な内容を書き込みます。既存のファイルを上書きするか、新しいファイルを作成します。", requiresApproval: true)]
    Task<string> WriteFileAsync(
        [KotliToolParam("ファイルの書き込み先パス")] string filePath,
        [KotliToolParam("ファイルの完全なテキスト内容")] string content);

    [KotliTool("replace_text", "既存のファイル内のユニークな部分文字列をピンポイントで置換します。オーバーヘッドを最小限に抑えるため、変更の際にはこのツールが推奨されます。", requiresApproval: true)]
    Task<string> ReplaceTextAsync(
        [KotliToolParam("変更するファイルのパス")] string filePath,
        [KotliToolParam("検索して置換する正確な文字列")] string oldString,
        [KotliToolParam("挿入する新しい文字列")] string newString);

    Task<string> GetWriteDiffPreviewAsync(string filePath, string content);
    Task<string> GetReplaceDiffPreviewAsync(string filePath, string oldString, string newString);
}
