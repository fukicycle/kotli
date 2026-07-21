using System.Threading.Tasks;

namespace Kotli.Domain.Interfaces;

public interface IReadTool
{
    Task<string> ReadFileAsync(string filePath, int? startLine = null, int? endLine = null);
    Task<string> ListDirectoryAsync(string dirPath);
}
