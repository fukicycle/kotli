using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kotli.Domain.Models;

namespace Kotli.Domain.Interfaces;

public interface IOllamaClient
{
    IAsyncEnumerable<OllamaResponseChunk> ChatStreamAsync(
        string model,
        IEnumerable<ChatMessage> messages,
        IEnumerable<ToolDefinition>? tools = null,
        CancellationToken cancellationToken = default);

    Task<List<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);
}
