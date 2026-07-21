using System.Threading.Tasks;

namespace Kotli.Domain.Interfaces;

public interface IWebSearchTool
{
    Task<string> SearchAsync(string query);
}
