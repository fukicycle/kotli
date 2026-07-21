using System.Collections.Generic;

namespace Kotli.Domain.Models;

public record ToolCall(string Id, string Name, string ArgumentsJson);

public record ToolResponse(string ToolCallId, string Content);
