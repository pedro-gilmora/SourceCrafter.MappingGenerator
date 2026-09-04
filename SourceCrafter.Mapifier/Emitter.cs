using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;

namespace SourceCrafter.Mapifier;

/// <summary>
/// Turns the parsed mapping model into source files. It never sees a <c>Compilation</c> or a
/// symbol: it asks the <see cref="MappingParser"/> for models and renders them.
/// </summary>
internal sealed class Emitter(MappingParser parser, CancellationToken cancelToken)
{
    private readonly ImmutableArray<GeneratedSource>.Builder _generated = ImmutableArray.CreateBuilder<GeneratedSource>();
    private readonly HashSet<string> _hintNames = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _enumClassNames = new(StringComparer.OrdinalIgnoreCase);

    internal EquatableArray<GeneratedSource> Emit(EquatableArray<TypeRef> enums, EquatableArray<BindRequest> requests)
    {
        var seenEnums = new HashSet<string>(StringComparer.Ordinal);

        foreach (var enumRef in enums)
        {
            cancelToken.ThrowIfCancellationRequested();

            if (enumRef.IsEmpty || !seenEnums.Add(enumRef.DocumentationId)) continue;

            if (!parser.TryParseEnum(enumRef, out var meta)) continue;

            if (BuildEnumFile(meta) is { } enumFile) Add(enumFile);
        }

        var seenMappings = new HashSet<(string, string)>();

        foreach (var request in requests)
        {
            cancelToken.ThrowIfCancellationRequested();

            if (!request.IsValid || !seenMappings.Add((request.From.DocumentationId, request.To.DocumentationId)))
                continue;

            if (!parser.TryParseMapping(request.From, request.To, request.MapKind, request.Ignore, out var mapping))
                continue;

            Emit(mapping);

            if (mapping.AreSameType) continue;

            if (parser.TryParseSelfMapping(request.To, request.MapKind, request.Ignore, out var targetSelfMap))
                Emit(targetSelfMap);

            if (parser.TryParseSelfMapping(request.From, request.MapKind, request.Ignore, out var sourceSelfMap))
                Emit(sourceSelfMap);
        }

        return _generated.ToImmutable();
    }

    private void Emit(TypeMappingMeta mapping)
    {
        // BuildFile yields null once a mapping has already been rendered, which keeps a type that
        // participates in several requests from producing duplicate files.
        if (mapping.BuildFile() is { } text)
            Add(new GeneratedSource(mapping.GetFileName(), text));
    }

    private void Add(GeneratedSource source)
    {
        // AddSource throws on duplicate hint names, and two distinct types can sanitize to the
        // same file name, so disambiguate rather than crash the whole compilation.
        var hintName = source.HintName;

        for (var i = 1; !_hintNames.Add(hintName); i++)
            hintName = source.HintName + i;

        _generated.Add(new GeneratedSource(hintName, source.Text));
    }

    private GeneratedSource? BuildEnumFile(EnumMeta meta)
    {
        if (meta.Members.IsDefaultOrEmpty) return null;

        // Static extension members lower to plain statics named after the member, so two enums
        // cannot share a container: `Status.Names` and `MappingKind.Names` would both become
        // `get_Names()`. Hence one static class per enum.
        StringBuilder code = new(@"#nullable enable
namespace SourceCrafter.Mapifier;

public static partial class ");

        code.Append(BuildEnumClassName(meta))
            .Append(@"
{
    private static readonly object __lock = new();
");

        var start = code.Length;

        meta.BuildEnumMethods(code);

        if (code.Length == start) return null;

        code.Append(@"
}");

        return new GeneratedSource(meta.FullName.Replace("global::", ""), code.ToString());
    }

    private string BuildEnumClassName(EnumMeta meta)
    {
        var candidate = meta.SanitizedName;

        if (_enumClassNames.Add(candidate)) return candidate + "EnumExtensions";

        // Walk outwards through the enclosing types and then the assembly, prefixing each name
        // until the result is unique.
        foreach (var container in meta.ContainerNames)
            if (_enumClassNames.Add(candidate = container + candidate))
                return candidate + "EnumExtensions";

        var i = 0;

        while (!_enumClassNames.Add(candidate + ++i)) ;

        return candidate + i + "EnumExtensions";
    }
}
