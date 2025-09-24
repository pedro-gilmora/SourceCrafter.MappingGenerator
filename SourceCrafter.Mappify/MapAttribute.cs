using System;
using SourceCrafter.Mappify;


namespace SourceCrafter.Mappify.Attributes
{
#pragma warning disable CS9113 // Parameter is unread.
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class MapAttribute<TIn, TOut>(MappingKind kind = MappingKind.All, ApplyTo ignore = ApplyTo.None, string[] ignoreMembers = default!) : Attribute;
    
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class MapAttribute<TIn>(MappingKind kind = MappingKind.All, ApplyTo ignore = ApplyTo.None, string[] ignoreMembers = default!) : Attribute;
    
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class MapAttribute(string memberNameof, ApplyTo ignore = ApplyTo.None) : Attribute;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class IgnoreForAttribute(string value = "", ApplyTo ignore = ApplyTo.Both) : Attribute;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class IgnoreAttribute(ApplyTo ignore = ApplyTo.Both) : Attribute;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public sealed class MaxAttribute(short count = 1, ApplyTo ignore = ApplyTo.Both) : Attribute;

    [AttributeUsage(AttributeTargets.Enum, AllowMultiple = true)]
    public sealed class ExtendAttribute(string ignore = "") : Attribute;

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class ExtendAttribute<T>(string ignore = "") : Attribute where T : Enum;
    
#pragma warning restore CS9113 // Parameter is unread.
}

namespace SourceCrafter.Mappify
{
    public interface IImplement<IInterface, IImplementation>
        where IImplementation : class, IInterface;
}