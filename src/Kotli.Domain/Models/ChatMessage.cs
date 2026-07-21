using System.Collections.Generic;

namespace Kotli.Domain.Models;

public record ChatMessage(
    string Role,
    string Content,
    IReadOnlyList<ToolCall>? ToolCalls = null,
    string? ToolCallId = null)
{
    // A simple approximation: 1 token is roughly 4 characters.
    // This allows us to run token calculations with zero external dependency.
    public int EstimatedTokens
    {
        get
        {
            int length = (Content ?? "").Length;
            if (ToolCalls != null)
            {
                foreach (var tc in ToolCalls)
                {
                    length += tc.Name.Length + (tc.ArgumentsJson ?? "").Length;
                }
            }
            if (ToolCallId != null)
            {
                length += ToolCallId.Length;
            }
            return length / 4;
        }
    }
}
