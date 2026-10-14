using System;

namespace Kotli.Domain.Attributes;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class KotliToolAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public bool RequiresApproval { get; set; }

    public KotliToolAttribute(string name, string description, bool requiresApproval = false)
    {
        Name = name;
        Description = description;
        RequiresApproval = requiresApproval;
    }
}

[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class KotliToolParamAttribute : Attribute
{
    public string Description { get; }

    public KotliToolParamAttribute(string description)
    {
        Description = description;
    }
}
