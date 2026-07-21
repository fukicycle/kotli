using System.Collections.Generic;

namespace Kotli.Domain.Models;

public record OllamaResponseChunk(
    string? DeltaText,
    IReadOnlyList<ToolCall>? ToolCalls,
    bool Done,
    long? TotalDuration = null,
    long? LoadDuration = null,
    int? PromptEvalCount = null,
    int? EvalCount = null);
