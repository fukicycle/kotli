using System.Threading.Tasks;
using Kotli.Domain.Attributes;

namespace Kotli.Domain.Interfaces;

public interface IWebSearchTool
{
    [KotliTool("web_search", "最新のドキュメントや一般的な情報をウェブで検索します。")]
    Task<string> SearchAsync(
        [KotliToolParam("検索クエリ")] string query);
}
