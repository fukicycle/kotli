using System.Collections.Generic;

namespace Kotli.Domain.Models;

public record ToolParameter(string Type, string Description, bool Required = false);

public record ToolDefinition(
    string Name,
    string Description,
    string ParametersJson);
