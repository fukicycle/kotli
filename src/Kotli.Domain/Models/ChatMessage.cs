using System.Collections.Generic;

namespace Kotli.Domain.Models;

public record ChatMessage(
    string Role,
    string Content,
    IReadOnlyList<ToolCall>? ToolCalls = null,
    string? ToolCallId = null)
{
    /// <summary>
    /// 日本語（マルチバイト文字）とASCII文字の双方を考慮した軽量トークン数推計。
    /// ASCII文字は約4文字で1トークン、非ASCII文字（日本語など）は1文字あたり1トークンとして計算します。
    /// </summary>
    public static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        int asciiChars = 0;
        int nonAsciiChars = 0;

        foreach (char c in text)
        {
            if (c <= 127)
            {
                asciiChars++;
            }
            else
            {
                nonAsciiChars++;
            }
        }

        return (asciiChars / 4) + nonAsciiChars;
    }

    public int EstimatedTokens
    {
        get
        {
            int tokens = EstimateTokens(Content);
            if (ToolCalls != null)
            {
                foreach (var tc in ToolCalls)
                {
                    tokens += EstimateTokens(tc.Name) + EstimateTokens(tc.ArgumentsJson);
                }
            }
            if (ToolCallId != null)
            {
                tokens += EstimateTokens(ToolCallId);
            }
            return tokens;
        }
    }
}
