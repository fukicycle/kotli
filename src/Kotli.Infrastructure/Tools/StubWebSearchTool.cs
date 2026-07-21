using System.Threading.Tasks;
using Kotli.Domain.Interfaces;

namespace Kotli.Infrastructure.Tools;

public class StubWebSearchTool : IWebSearchTool
{
    public Task<string> SearchAsync(string query)
    {
        return Task.FromResult($"[WebSearch] Search query '{query}' processed. (Note: Web search backend is not configured yet. This is a stub implementation.)");
    }
}
