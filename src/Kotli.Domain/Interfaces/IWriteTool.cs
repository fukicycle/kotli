using System.Threading.Tasks;

namespace Kotli.Domain.Interfaces;

public interface IWriteTool
{
    Task<string> WriteFileAsync(string filePath, string content);
    Task<string> ReplaceTextAsync(string filePath, string oldString, string newString);
}
