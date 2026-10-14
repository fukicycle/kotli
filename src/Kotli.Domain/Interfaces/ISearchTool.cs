using System.Threading.Tasks;
using Kotli.Domain.Attributes;

namespace Kotli.Domain.Interfaces;

public interface ISearchTool
{
    [KotliTool("find_files", "ワークスペース内のファイル名をパターン（部分一致またはワイルドカード）で高速に検索します。")]
    Task<string> FindFilesAsync(
        [KotliToolParam("検索するファイル名パターン（例: '*.cs', 'Program'）")] string pattern = "*");

    [KotliTool("grep_search", "ワークスペース内のファイルから指定されたテキストまたは正規表現に一致する行を検索します。")]
    Task<string> GrepSearchAsync(
        [KotliToolParam("検索する文字列または正規表現パターン")] string query,
        [KotliToolParam("検索対象を絞り込むディレクトリまたはファイルパス（省略時はワークスペース全体）")] string? path = null);
}
