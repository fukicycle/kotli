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

    public void CompactToolMessages(int maxCharsThreshold = 500)
    {
        for (int i = 0; i < _messages.Count; i++)
        {
            var msg = _messages[i];
            if (msg.Role == "tool" && msg.Content != null && msg.Content.Length > maxCharsThreshold)
            {
                int originalLength = msg.Content.Length;
                string stubText = $"... (元の内容は {originalLength} 文字ありました) ... [コンテキスト節約のため内容はスタブ化されました。AIはすでに内容を読み込んで回答済みです]";
                _messages[i] = msg with { Content = stubText };
            }
        }
    }
}
