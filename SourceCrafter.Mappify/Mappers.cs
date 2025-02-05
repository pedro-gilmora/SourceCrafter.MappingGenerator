using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceCrafter.Helpers;
using SourceCrafter.Mappify.Helpers;

namespace SourceCrafter.Mappify;

internal sealed partial class Mappers(Compilation compilation) : Set<int, TypeMap>(m => m.Id)
{
    internal readonly TypeSet Types = new(compilation);

    internal readonly bool CanUseUnsafeAccessor =
        compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.UnsafeAccessorAttribute") is not null;

    internal TypeMap GetOrAdd(
        TypeMeta source, 
        TypeMeta target, 
        GenerateOn ignore, 
        bool dictionaryContext = false)
    {
        var mapperId = TypeMap.GetId(source.Id, target.Id);

        ref var mapper = ref GetOrAddDefault(mapperId, out var exists);

        if (exists)
        {
            return mapper;
        }

        return source.Id == target.Id
            ? new(this, ref mapper, mapperId, source, source, GenerateOn.None, dictionaryContext)
            : new(this, ref mapper, mapperId, target, source, ignore, dictionaryContext);
    }

    internal MemberMeta CreateMember(ITypeSymbol item, string name)
    {
        var sourceType = Types.GetOrAdd(item);
        
        return new(
            sourceType.Id,
            name,
            sourceType,
            isNullable: item.IsNullable());
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

        addSource("MappingExtras", code.Append("\n}").ToString());
    }
}