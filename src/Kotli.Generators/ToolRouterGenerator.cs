using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Kotli.Generators;

[Generator]
public class ToolRouterGenerator : IIncrementalGenerator
{
    private const string ToolAttrFullName = "Kotli.Domain.Attributes.KotliToolAttribute";
    private const string ParamAttrFullName = "Kotli.Domain.Attributes.KotliToolParamAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var toolsProvider = context.CompilationProvider.Select((compilation, _) => DiscoverTools(compilation));

        context.RegisterSourceOutput(toolsProvider, (spc, tools) =>
        {
            var source = GenerateRouterSource(tools);
            spc.AddSource("ToolExecutionRouter.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    private static ImmutableArray<ToolMethodModel> DiscoverTools(Compilation compilation)
    {
        var toolAttrSymbol = compilation.GetTypeByMetadataName(ToolAttrFullName);
        var paramAttrSymbol = compilation.GetTypeByMetadataName(ParamAttrFullName);

        if (toolAttrSymbol == null)
        {
            return ImmutableArray<ToolMethodModel>.Empty;
        }

        var candidateInterfaces = new List<INamedTypeSymbol>();

        // Collect interfaces from Kotli.Domain.Interfaces across compilation and referenced assemblies
        foreach (var assembly in new[] { compilation.Assembly }.Concat(compilation.SourceModule.ReferencedAssemblySymbols))
        {
            var domainNs = GetNamespaceSymbol(assembly.GlobalNamespace, new[] { "Kotli", "Domain", "Interfaces" });
            if (domainNs != null)
            {
                foreach (var type in domainNs.GetTypeMembers())
                {
                    if (type.TypeKind == TypeKind.Interface)
                    {
                        candidateInterfaces.Add(type);
                    }
                }
            }
        }

        var results = new List<ToolMethodModel>();
        var seenToolNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var iface in candidateInterfaces)
        {
            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
            {
                var attr = member.GetAttributes().FirstOrDefault(a =>
                    SymbolEqualityComparer.Default.Equals(a.AttributeClass, toolAttrSymbol));
                if (attr == null) continue;

                string toolName = attr.ConstructorArguments.Length > 0
                    ? attr.ConstructorArguments[0].Value?.ToString() ?? ""
                    : "";
                string description = attr.ConstructorArguments.Length > 1
                    ? attr.ConstructorArguments[1].Value?.ToString() ?? ""
                    : "";
                bool requiresApproval = attr.ConstructorArguments.Length > 2 &&
                    attr.ConstructorArguments[2].Value is bool b && b;

                foreach (var namedArg in attr.NamedArguments)
                {
                    if (namedArg.Key == "RequiresApproval" && namedArg.Value.Value is bool nb)
                    {
                        requiresApproval = nb;
                    }
                }

                if (string.IsNullOrEmpty(toolName) || !seenToolNames.Add(toolName))
                {
                    continue;
                }

                var parameters = new List<ToolParameterModel>();
                foreach (var param in member.Parameters)
                {
                    string paramDesc = "";
                    if (paramAttrSymbol != null)
                    {
                        var pAttr = param.GetAttributes().FirstOrDefault(a =>
                            SymbolEqualityComparer.Default.Equals(a.AttributeClass, paramAttrSymbol));
                        if (pAttr != null && pAttr.ConstructorArguments.Length > 0)
                        {
                            paramDesc = pAttr.ConstructorArguments[0].Value?.ToString() ?? "";
                        }
                    }

                    bool isNullable = param.NullableAnnotation == NullableAnnotation.Annotated ||
                                      param.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
                    bool isRequired = !param.HasExplicitDefaultValue && !isNullable;

                    string csharpType = param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    string jsonType = GetJsonSchemaType(param.Type);
                    string defaultLiteral = GetDefaultLiteral(param);

                    parameters.Add(new ToolParameterModel(
                        param.Name,
                        csharpType,
                        jsonType,
                        paramDesc,
                        isRequired,
                        isNullable,
                        param.HasExplicitDefaultValue,
                        defaultLiteral));
                }

                results.Add(new ToolMethodModel(
                    iface.Name,
                    GetFieldName(iface.Name),
                    member.Name,
                    toolName,
                    description,
                    requiresApproval,
                    parameters.ToImmutableArray()));
            }
        }

        return results.ToImmutableArray();
    }

    private static INamespaceSymbol? GetNamespaceSymbol(INamespaceSymbol root, string[] parts)
    {
        INamespaceSymbol? current = root;
        foreach (var part in parts)
        {
            current = current.GetNamespaceMembers().FirstOrDefault(ns => ns.Name == part);
            if (current == null) return null;
        }
        return current;
    }

    private static string GetFieldName(string interfaceName)
    {
        var baseName = interfaceName.StartsWith("I") && interfaceName.Length > 1 && char.IsUpper(interfaceName[1])
            ? interfaceName.Substring(1)
            : interfaceName;
        return "_" + char.ToLowerInvariant(baseName[0]) + baseName.Substring(1);
    }

    private static string GetParamName(string interfaceName)
    {
        var baseName = interfaceName.StartsWith("I") && interfaceName.Length > 1 && char.IsUpper(interfaceName[1])
            ? interfaceName.Substring(1)
            : interfaceName;
        return char.ToLowerInvariant(baseName[0]) + baseName.Substring(1);
    }

    private static string GetJsonSchemaType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            named.TypeArguments.Length == 1)
        {
            type = named.TypeArguments[0];
        }

        return type.SpecialType switch
        {
            SpecialType.System_Int32 or SpecialType.System_Int64 => "integer",
            SpecialType.System_Boolean => "boolean",
            SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Decimal => "number",
            _ => "string"
        };
    }

    private static string GetDefaultLiteral(IParameterSymbol param)
    {
        if (!param.HasExplicitDefaultValue || param.ExplicitDefaultValue == null)
        {
            return param.Type.SpecialType == SpecialType.System_String && param.NullableAnnotation != NullableAnnotation.Annotated
                ? "\"\""
                : "null";
        }

        if (param.ExplicitDefaultValue is string s)
        {
            return $"\"{EscapeString(s)}\"";
        }
        if (param.ExplicitDefaultValue is bool b)
        {
            return b ? "true" : "false";
        }
        return param.ExplicitDefaultValue.ToString();
    }

    private static string EscapeString(string input)
    {
        return input
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private static string ToPascalCase(string snakeCase)
    {
        var parts = snakeCase.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1) sb.Append(part.Substring(1));
        }
        return sb.ToString();
    }

    private static string BuildJsonSchemaLiteral(ImmutableArray<ToolParameterModel> parameters)
    {
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"object\",\"properties\":{");

        for (int i = 0; i < parameters.Length; i++)
        {
            if (i > 0) sb.Append(",");
            var p = parameters[i];
            sb.Append($"\"{EscapeString(p.Name)}\":{{\"type\":\"{p.JsonType}\",\"description\":\"{EscapeString(p.Description)}\"}}");
        }

        sb.Append("}");

        var requiredParams = parameters.Where(p => p.IsRequired).ToList();
        if (requiredParams.Count > 0)
        {
            sb.Append(",\"required\":[");
            for (int i = 0; i < requiredParams.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{EscapeString(requiredParams[i].Name)}\"");
            }
            sb.Append("]");
        }

        sb.Append("}");
        return sb.ToString();
    }

    private static string GenerateRouterSource(ImmutableArray<ToolMethodModel> tools)
    {
        // Order interfaces so core 4 come first, then known optional ones, then any others sorted by name
        var coreInterfaces = new[] { "IReadTool", "IWriteTool", "IWebSearchTool", "IWorkspaceProvider" };
        var preferredOrder = new[] { "IReadTool", "IWriteTool", "IWebSearchTool", "IWorkspaceProvider", "ISearchTool", "IMemoryStore", "ICommandTool" };
        var discoveredInterfaces = tools.Select(t => t.InterfaceName).Distinct().ToList();

        var orderedInterfaces = preferredOrder
            .Where(discoveredInterfaces.Contains)
            .Concat(discoveredInterfaces.Where(i => !preferredOrder.Contains(i)).OrderBy(i => i, StringComparer.Ordinal))
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using Kotli.Domain.Interfaces;");
        sb.AppendLine("using Kotli.Domain.Models;");
        sb.AppendLine();
        sb.AppendLine("namespace Kotli.Infrastructure.Tools;");
        sb.AppendLine();
        sb.AppendLine("public class ToolExecutionRouter : IToolExecutionRouter");
        sb.AppendLine("{");

        foreach (var iface in orderedInterfaces)
        {
            bool isOptional = !coreInterfaces.Contains(iface);
            string nullSuffix = isOptional ? "?" : "";
            sb.AppendLine($"    private readonly {iface}{nullSuffix} {GetFieldName(iface)};");
        }
        sb.AppendLine("    private readonly IToolApprovalService? _approvalService;");
        sb.AppendLine("    private bool _sessionApprovedAll;");
        sb.AppendLine();

        // Constructor
        sb.Append("    public ToolExecutionRouter(");
        var ctorParams = new List<string>();
        foreach (var iface in orderedInterfaces)
        {
            bool isOptional = !coreInterfaces.Contains(iface);
            if (isOptional)
            {
                ctorParams.Add($"{iface}? {GetParamName(iface)} = null");
            }
            else
            {
                ctorParams.Add($"{iface} {GetParamName(iface)}");
            }
        }
        ctorParams.Add("IToolApprovalService? approvalService = null");
        sb.Append(string.Join(", ", ctorParams));
        sb.AppendLine(")");
        sb.AppendLine("    {");
        foreach (var iface in orderedInterfaces)
        {
            bool isOptional = !coreInterfaces.Contains(iface);
            var field = GetFieldName(iface);
            var param = GetParamName(iface);
            if (isOptional)
            {
                sb.AppendLine($"        {field} = {param};");
            }
            else
            {
                sb.AppendLine($"        {field} = {param} ?? throw new ArgumentNullException(nameof({param}));");
            }
        }
        sb.AppendLine("        _approvalService = approvalService;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // ExecuteToolAsync
        sb.AppendLine("    public async Task<ToolResponse> ExecuteToolAsync(ToolCall toolCall)");
        sb.AppendLine("    {");
        sb.AppendLine("        string result = \"\";");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (toolCall.Name)");
        sb.AppendLine("            {");

        foreach (var tool in tools)
        {
            bool isOptionalService = !coreInterfaces.Contains(tool.InterfaceName);
            string dtoName = $"{ToPascalCase(tool.ToolName)}Args";

            sb.AppendLine($"                case \"{tool.ToolName}\":");
            sb.AppendLine("                    {");

            if (isOptionalService)
            {
                sb.AppendLine($"                        if ({tool.FieldName} == null)");
                sb.AppendLine("                        {");
                sb.AppendLine($"                            result = \"[エラー] ツール '{tool.ToolName}' は現在利用できません。\";");
                sb.AppendLine("                            break;");
                sb.AppendLine("                        }");
            }

            if (tool.Parameters.Length > 0)
            {
                sb.AppendLine($"                        var args = DeserializeArgs<{dtoName}>(toolCall.ArgumentsJson);");
            }

            var callArgs = new List<string>();
            foreach (var p in tool.Parameters)
            {
                callArgs.Add($"args.{p.Name}");
            }
            string joinedArgs = string.Join(", ", callArgs);

            if (tool.RequiresApproval)
            {
                string diffExpr = "Task.FromResult(\"\")";
                if (tool.InterfaceName == "IWriteTool" && tool.ToolName == "write_file")
                {
                    diffExpr = $"{tool.FieldName}.GetWriteDiffPreviewAsync({joinedArgs})";
                }
                else if (tool.InterfaceName == "IWriteTool" && tool.ToolName == "replace_text")
                {
                    diffExpr = $"{tool.FieldName}.GetReplaceDiffPreviewAsync({joinedArgs})";
                }

                sb.AppendLine($"                        if (!await IsApprovedAsync(toolCall, () => {diffExpr}))");
                sb.AppendLine("                        {");
                sb.AppendLine("                            return new ToolResponse(toolCall.Id, \"[キャンセル] ユーザーによって変更が拒否されました。変更は適用されていません。\");");
                sb.AppendLine("                        }");
            }

            sb.AppendLine($"                        result = await {tool.FieldName}.{tool.MethodName}({joinedArgs});");
            sb.AppendLine("                    }");
            sb.AppendLine("                    break;");
        }

        sb.AppendLine("                default:");
        sb.AppendLine("                    result = $\"[エラー] 不明なツール名: {toolCall.Name}\";");
        sb.AppendLine("                    break;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (Exception ex)");
        sb.AppendLine("        {");
        sb.AppendLine("            result = $\"[エラー] ツールの実行に失敗しました: {ex.Message}\";");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        return new ToolResponse(toolCall.Id, result);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static T DeserializeArgs<T>(string? json) where T : new()");
        sb.AppendLine("    {");
        sb.AppendLine("        return string.IsNullOrWhiteSpace(json)");
        sb.AppendLine("            ? new T()");
        sb.AppendLine("            : JsonSerializer.Deserialize<T>(json)!;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private async Task<bool> IsApprovedAsync(ToolCall toolCall, Func<Task<string>> diffFactory)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (_approvalService == null || _sessionApprovedAll)");
        sb.AppendLine("        {");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var diffPreview = await diffFactory();");
        sb.AppendLine("        var decision = await _approvalService.RequestApprovalAsync(toolCall, diffPreview);");
        sb.AppendLine("        if (decision == ToolApprovalDecision.Reject)");
        sb.AppendLine("        {");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        if (decision == ToolApprovalDecision.AllowAlwaysInSession)");
        sb.AppendLine("        {");
        sb.AppendLine("            _sessionApprovedAll = true;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        return true;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // GetToolDefinitions
        sb.AppendLine("    public IEnumerable<ToolDefinition> GetToolDefinitions()");
        sb.AppendLine("    {");
        foreach (var tool in tools)
        {
            bool isOptionalService = !coreInterfaces.Contains(tool.InterfaceName);
            string schemaJson = BuildJsonSchemaLiteral(tool.Parameters);
            string escapedSchema = EscapeString(schemaJson);
            string escapedDesc = EscapeString(tool.Description);

            if (isOptionalService)
            {
                sb.AppendLine($"        if ({tool.FieldName} != null)");
                sb.AppendLine("        {");
                sb.AppendLine("            yield return new ToolDefinition(");
                sb.AppendLine($"                \"{tool.ToolName}\",");
                sb.AppendLine($"                \"{escapedDesc}\",");
                sb.AppendLine($"                \"{escapedSchema}\"");
                sb.AppendLine("            );");
                sb.AppendLine("        }");
            }
            else
            {
                sb.AppendLine("        yield return new ToolDefinition(");
                sb.AppendLine($"            \"{tool.ToolName}\",");
                sb.AppendLine($"            \"{escapedDesc}\",");
                sb.AppendLine($"            \"{escapedSchema}\"");
                sb.AppendLine("        );");
            }
            sb.AppendLine();
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        // DTO classes
        foreach (var tool in tools)
        {
            if (tool.Parameters.Length == 0) continue;
            string dtoName = $"{ToPascalCase(tool.ToolName)}Args";
            sb.Append($"    private class {dtoName} {{ ");
            foreach (var p in tool.Parameters)
            {
                if (p.CSharpType == "string")
                {
                    sb.Append($"public string {p.Name} {{ get; set; }} = {p.DefaultLiteral}; ");
                }
                else
                {
                    string propType = p.CSharpType.EndsWith("?") ? p.CSharpType : p.CSharpType + "?";
                    sb.Append($"public {propType} {p.Name} {{ get; set; }} ");
                }
            }
            sb.AppendLine("}");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private sealed class ToolMethodModel
    {
        public string InterfaceName { get; }
        public string FieldName { get; }
        public string MethodName { get; }
        public string ToolName { get; }
        public string Description { get; }
        public bool RequiresApproval { get; }
        public ImmutableArray<ToolParameterModel> Parameters { get; }

        public ToolMethodModel(
            string interfaceName,
            string fieldName,
            string methodName,
            string toolName,
            string description,
            bool requiresApproval,
            ImmutableArray<ToolParameterModel> parameters)
        {
            InterfaceName = interfaceName;
            FieldName = fieldName;
            MethodName = methodName;
            ToolName = toolName;
            Description = description;
            RequiresApproval = requiresApproval;
            Parameters = parameters;
        }
    }

    private sealed class ToolParameterModel
    {
        public string Name { get; }
        public string CSharpType { get; }
        public string JsonType { get; }
        public string Description { get; }
        public bool IsRequired { get; }
        public bool IsNullable { get; }
        public bool HasDefaultValue { get; }
        public string DefaultLiteral { get; }

        public ToolParameterModel(
            string name,
            string csharpType,
            string jsonType,
            string description,
            bool isRequired,
            bool isNullable,
            bool hasDefaultValue,
            string defaultLiteral)
        {
            Name = name;
            CSharpType = csharpType;
            JsonType = jsonType;
            Description = description;
            IsRequired = isRequired;
            IsNullable = isNullable;
            HasDefaultValue = hasDefaultValue;
            DefaultLiteral = defaultLiteral;
        }
    }
}
