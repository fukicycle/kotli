using System.Collections.Generic;
using System.Threading.Tasks;
using Kotli.Domain.Models;

namespace Kotli.Domain.Interfaces;

public interface IToolExecutionRouter
{
    Task<ToolResponse> ExecuteToolAsync(ToolCall toolCall);
    IEnumerable<ToolDefinition> GetToolDefinitions();
}
