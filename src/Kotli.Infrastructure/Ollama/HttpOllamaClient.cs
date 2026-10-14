using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kotli.Domain.Interfaces;
using Kotli.Domain.Models;

namespace Kotli.Infrastructure.Ollama;

public class HttpOllamaClient : IOllamaClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public HttpOllamaClient(HttpClient httpClient, string baseUrl = "http://localhost:11434")
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async IAsyncEnumerable<OllamaResponseChunk> ChatStreamAsync(
        string model,
        IEnumerable<ChatMessage> messages,
        IEnumerable<ToolDefinition>? tools = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestUrl = $"{_baseUrl}/api/chat";

        // Map domain messages to DTOs
        var messageDtos = new List<OllamaMessageDto>();
        foreach (var msg in messages)
        {
            var dto = new OllamaMessageDto
            {
                role = msg.Role,
                content = msg.Content,
                tool_call_id = msg.ToolCallId
            };

            if (msg.ToolCalls != null)
            {
                dto.tool_calls = new List<OllamaToolCallDto>();
                foreach (var tc in msg.ToolCalls)
                {
                    JsonElement argsElement;
                    try
                    {
                        argsElement = JsonSerializer.Deserialize<JsonElement>(tc.ArgumentsJson ?? "{}");
                    }
                    catch
                    {
                        argsElement = JsonSerializer.SerializeToElement(new Dictionary<string, string>());
                    }

                    dto.tool_calls.Add(new OllamaToolCallDto
                    {
                        id = tc.Id,
                        function = new OllamaFunctionCallDto
                        {
                            name = tc.Name,
                            arguments = argsElement
                        }
                    });
                }
            }

            messageDtos.Add(dto);
        }

        // Map tools
        List<OllamaToolDto>? toolDtos = null;
        if (tools != null)
        {
            toolDtos = new List<OllamaToolDto>();
            foreach (var t in tools)
            {
                JsonElement paramsElement;
                try
                {
                    paramsElement = JsonSerializer.Deserialize<JsonElement>(t.ParametersJson);
                }
                catch
                {
                    paramsElement = JsonSerializer.SerializeToElement(new { type = "object", properties = new { } });
                }

                toolDtos.Add(new OllamaToolDto
                {
                    function = new OllamaFunctionDto
                    {
                        name = t.Name,
                        description = t.Description,
                        parameters = paramsElement
                    }
                });
            }
        }

        var requestBody = new OllamaChatRequest
        {
            model = model,
            messages = messageDtos,
            tools = toolDtos,
            stream = true
        };

        var jsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        var requestJson = JsonSerializer.Serialize(requestBody, jsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        HttpResponseMessage? response = null;
        string? connectionError = null;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            connectionError = $"\n[エラー] {_baseUrl} の Ollama に接続できません。Ollama はローカルで起動していますか？ ({ex.Message})\n";
        }

        if (connectionError != null)
        {
            yield return new OllamaResponseChunk(connectionError, null, true);
            yield break;
        }

        if (!response!.IsSuccessStatusCode)
        {
            var errContent = await response.Content.ReadAsStringAsync(cancellationToken);
            yield return new OllamaResponseChunk(
                DeltaText: $"\n[エラー] Ollama がステータスコード {response.StatusCode} を返しました: {errContent}\n",
                ToolCalls: null,
                Done: true);
            yield break;
        }

        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(responseStream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            OllamaChatResponseChunk? chunkDto;
            try
            {
                chunkDto = JsonSerializer.Deserialize<OllamaChatResponseChunk>(line);
            }
            catch
            {
                continue; // Skip malformed lines
            }

            if (chunkDto == null) continue;

            var deltaText = chunkDto.message?.content;
            List<ToolCall>? toolCalls = null;

            if (chunkDto.message?.tool_calls != null)
            {
                toolCalls = new List<ToolCall>();
                foreach (var tc in chunkDto.message.tool_calls)
                {
                    var argsJson = tc.function.arguments?.GetRawText() ?? "{}";
                    toolCalls.Add(new ToolCall(tc.id, tc.function.name, argsJson));
                }
            }

            yield return new OllamaResponseChunk(
                DeltaText: deltaText,
                ToolCalls: toolCalls,
                Done: chunkDto.done,
                TotalDuration: chunkDto.total_duration,
                LoadDuration: chunkDto.load_duration,
                PromptEvalCount: chunkDto.prompt_eval_count,
                EvalCount: chunkDto.eval_count);
        }
    }

    public async Task<List<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var requestUrl = $"{_baseUrl}/api/tags";
        try
        {
            using var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var tagsResponse = JsonSerializer.Deserialize<OllamaTagsResponse>(content);
                if (tagsResponse?.models != null)
                {
                    var names = new List<string>();
                    foreach (var m in tagsResponse.models)
                    {
                        if (!string.IsNullOrEmpty(m.name))
                        {
                            names.Add(m.name);
                        }
                    }
                    return names;
                }
            }
        }
        catch
        {
            // Fall back silently
        }

        // Default models fallback
        return new List<string> { "gemma4:26b", "llama3:latest", "mistral:latest" };
    }
}

#region DTOs
internal class OllamaTagsResponse
{
    public List<OllamaTagModelDto>? models { get; set; }
}

internal class OllamaTagModelDto
{
    public string name { get; set; } = "";
}

internal class OllamaChatRequest
{
    public string model { get; set; } = "";
    public List<OllamaMessageDto> messages { get; set; } = new();
    public List<OllamaToolDto>? tools { get; set; }
    public bool stream { get; set; } = true;
}

internal class OllamaMessageDto
{
    public string role { get; set; } = "";
    public string content { get; set; } = "";
    public List<OllamaToolCallDto>? tool_calls { get; set; }
    public string? tool_call_id { get; set; }
}

internal class OllamaToolDto
{
    public string type { get; set; } = "function";
    public OllamaFunctionDto function { get; set; } = new();
}

internal class OllamaFunctionDto
{
    public string name { get; set; } = "";
    public string description { get; set; } = "";
    public object? parameters { get; set; }
}

internal class OllamaToolCallDto
{
    public string id { get; set; } = "";
    public string type { get; set; } = "function";
    public OllamaFunctionCallDto function { get; set; } = new();
}

internal class OllamaFunctionCallDto
{
    public string name { get; set; } = "";
    public JsonElement? arguments { get; set; }
}

internal class OllamaChatResponseChunk
{
    public string model { get; set; } = "";
    public string created_at { get; set; } = "";
    public OllamaMessageDto? message { get; set; }
    public bool done { get; set; }
    public long? total_duration { get; set; }
    public long? load_duration { get; set; }
    public int? prompt_eval_count { get; set; }
    public int? eval_count { get; set; }
}
#endregion
