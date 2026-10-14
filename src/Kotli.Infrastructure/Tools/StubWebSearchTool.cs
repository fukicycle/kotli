using System.Threading.Tasks;
using Kotli.Domain.Interfaces;

namespace Kotli.Infrastructure.Tools;

public class StubWebSearchTool : IWebSearchTool
{
    public Task<string> SearchAsync(string query)
    {
        return Task.FromResult($"[ウェブ検索] 検索クエリ '{query}' が処理されました。(注意: ウェブ検索バックエンドはまだ設定されていません。これはスタブ実装です。)");
    }
}
