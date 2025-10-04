using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SourceCrafter.Mappify;

internal delegate void ValueBuilder(StringBuilder code, string sourceMemberName, bool notNull);

internal sealed partial class Mappers(Compilation compilation, bool canUseUnsafeAccesor, Action<string, string> addSource) : Set<int, TypeMap>(m => m.Id)
{
    internal readonly TypeSet Types = new(compilation);

    internal readonly bool CanUseUnsafeAccessor = canUseUnsafeAccesor;

    private int _count = 0;

    internal readonly HashSet<int> _rendered = [];

    internal TypeMap GetOrAdd(TypeMeta targetType, TypeMeta sourceType, ApplyTo ignore = ApplyTo.None)
    {
        var mapperId = (targetType.Id, sourceType.Id).ComputeHashCode();

        ref var typeMap = ref GetValueRefOrAddDefault(mapperId, out var exists);

        if (exists) return typeMap!;

        List<Action<StringBuilder>> methods = [];

        if (targetType.Id != sourceType.Id)
        {
            GetOrAdd(targetType);
            GetOrAdd(sourceType);
        }

        new TypeMap(mapperId, ref typeMap, targetType, sourceType, ignore, this, Types, methods);

        if(methods.Count > 0) BuildCode();

        return typeMap!;

        void GetOrAdd(TypeMeta type)
        {
            var mapperId = (type.Id, type.Id).ComputeHashCode();

            ref var typeMap = ref GetValueRefOrAddDefault(mapperId, out var exists);

            if (!exists) new TypeMap(mapperId, ref typeMap, type, type, ApplyTo.None, this, Types, methods);
        }

        void BuildCode()
        {
            StringBuilder code = new(@"#nullable enable
namespace SourceCrafter.Mappify;

public static partial class Mappings
{");

            methods.ForEach(m => m(code));

            addSource(
                $"{_count++.ToString().PadLeft(3, '0')}_{sourceType.SanitizedName}_{targetType.SanitizedName}.map.g",
                code.Append("}").ToString());
        }
    }



    internal void RenderExtra(Action<string, string> addSource)
    {
        StringBuilder code = new(@"namespace SourceCrafter.Mappify;

public static partial class Mappings
{");
        var len = code.Length;

        foreach (var item in Types.UnsafeAccessors)
        {
            item.Render(code);
        }

        if (len == code.Length) return;

        addSource("MappingExtras", code.Append("}").ToString());
    }
}