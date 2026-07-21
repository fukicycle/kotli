using System.Collections.Generic;

namespace Kotli.Domain.Models;

public class ChatSession
{
    private readonly List<ChatMessage> _messages = new();

    public IReadOnlyList<ChatMessage> Messages => _messages;

    public void AddMessage(ChatMessage message)
    {
        _messages.Add(message);
    }

    public void AddMessage(string role, string content, IReadOnlyList<ToolCall>? toolCalls = null, string? toolCallId = null)
    {
        _messages.Add(new ChatMessage(role, content, toolCalls, toolCallId));
    }

    public void Clear()
    {
        _messages.Clear();
    }

    public void LoadHistory(IEnumerable<ChatMessage> messages)
    {
        _messages.Clear();
        _messages.AddRange(messages);
    }
}
