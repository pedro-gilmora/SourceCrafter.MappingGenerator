using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using SourceCrafter.Mapifier.Constants;

namespace SourceCrafter.Mapifier;


/// <summary>
/// Tracks the separator that must precede every member but the first one of an object/tuple initializer.
/// Replaces the <c>ref string?</c> local that closures used to capture.
/// </summary>
internal sealed class CommaState
{
    private string? _value;

    internal string? Next(string? update = ",")
    {
        var previous = _value;

        _value = update;

        return previous;
    }
}

/// <summary>
/// Mutable boolean shared between the discovery phase and the renderers created during it.
/// Replaces the by-reference capture that closures used to perform over local variables.
/// </summary>
internal sealed class MutableFlag
{
    internal bool Value;

    internal void Set() => Value = true;

    internal void Or(bool value) => Value |= value;
}

/// <summary>
/// Produces the portion of code representing a single value expression.
/// </summary>
internal abstract class ValueRenderer
{
    internal abstract void Render(StringBuilder code, string value);
}

/// <summary>Emits the incoming value verbatim.</summary>
internal sealed class LiteralValueRenderer : ValueRenderer
{
    internal static readonly LiteralValueRenderer Instance = new();

    private LiteralValueRenderer() { }

    internal override void Render(StringBuilder code, string value) => code.Append(value);
}

/// <summary>Emits a captured expression, ignoring the incoming value.</summary>
internal sealed class FixedValueRenderer(string text) : ValueRenderer
{
    internal override void Render(StringBuilder code, string value) => code.Append(text);
}

/// <summary>Emits a scalar conversion, either implicit (<c>{0}</c>) or explicit (<c>(T){0}</c>).</summary>
internal sealed class ScalarConversionRenderer(string format) : ValueRenderer
{
    internal override void Render(StringBuilder code, string value) => code.AppendFormat(format, value);
}

/// <summary>Wraps the built collection into a <see cref="System.Collections.ObjectModel.ReadOnlyCollection{T}"/>.</summary>
internal sealed class ReadOnlyCollectionValueRenderer(string itemFullTypeName) : ValueRenderer
{
    internal override void Render(StringBuilder code, string value) =>
        code.Append("new global::System.Collections.ObjectModel.ReadOnlyCollection<")
            .Append(itemFullTypeName)
            .Append(">(")
            .Append(value)
            .Append(')');
}

/// <summary>Emits the call to the generated collection copy method, honoring recursion depth arguments.</summary>
internal sealed class CollectionCopyValueRenderer(string copyMethodName, TypeMeta itemDataType, MemberMeta target) : ValueRenderer
{
    internal override void Render(StringBuilder code, string value)
    {
        code.Append(copyMethodName).Append('(').Append(value);

        if (itemDataType.IsRecursive)
        {
            if (target.MaxDepth == 0)
                target.MaxDepth = 1;

            code.Append(", -1 + depth + 1");

            if (target.MaxDepth > 1)
                code.Append(", ").Append(target.MaxDepth);
        }

        code.Append(')');
    }
}

/// <summary>Emits the call to a generated mapping method, honoring recursion depth arguments.</summary>
internal sealed class MapCallValueRenderer(string mapMethodName, MutableFlag isRecursive, MemberMeta depthOwner) : ValueRenderer
{
    internal override void Render(StringBuilder code, string value)
    {
        code.Append(mapMethodName).Append('(').Append(value);

        if (isRecursive.Value)
        {
            code.Append(", depth + 1");

            if (depthOwner.MaxDepth > 1)
                code.Append(", ").Append(depthOwner.MaxDepth);
        }

        code.Append(')');
    }
}

/// <summary>Emits <c> new TKeyValue(key, value</c> for dictionary-owned object types.</summary>
internal sealed class KeyValueObjectRenderer(string notNullFullName, KeyValueMappings parts) : ValueRenderer
{
    internal override void Render(StringBuilder code, string value)
    {
        code.Append(" new ").Append(notNullFullName).Append('(');

        parts.Key.Render(code, value);

        code.Append(", ");

        parts.Value.Render(code, value);
    }
}

/// <summary>Emits <c>(key, value)</c> for dictionary-owned tuple types.</summary>
internal sealed class KeyValueTupleRenderer(KeyValueMappings parts) : ValueRenderer
{
    internal override void Render(StringBuilder code, string value)
    {
        code.Append('(');

        parts.Key.Render(code, value);

        code.Append(", ");

        parts.Value.Render(code, value);

        code.Append(')');
    }
}

/// <summary>Emits the mapped value of a single member of the key/value pair being projected.</summary>
internal sealed class KeyValueMemberRenderer(
    TypeMappingMeta memberMap,
    bool towardsTarget,
    string memberName,
    bool checkNull,
    bool isValueType,
    string? sourceBang,
    string? defaultSourceBang) : ValueRenderer
{
    internal override void Render(StringBuilder code, string value) =>
        ValueEmitter.Generate(
            code,
            value + "." + memberName,
            towardsTarget ? memberMap.BuildTargetValue : memberMap.BuildSourceValue,
            checkNull,
            false,
            isValueType,
            sourceBang,
            defaultSourceBang);
}

/// <summary>
/// Wraps a <see cref="ValueRenderer"/> with the null-checking, caching and null-forgiving decorations
/// required by the consuming context.
/// </summary>
internal static class ValueEmitter
{
    internal static void Generate(
        StringBuilder code,
        string item,
        ValueRenderer? value,
        bool checkNull,
        bool call,
        bool isValueType,
        string? sourceBang,
        string? defaultSourceBang)
    {
        value ??= new FixedValueRenderer(item);

        var indexerBracketPos = item.IndexOf('[');

        bool hasIndexer = indexerBracketPos > -1,
            shouldCache = checkNull && call && (hasIndexer || item.Contains('.'));

        var itemCache = shouldCache
            ? "_" + (hasIndexer ? item[..indexerBracketPos] : item).Replace(".", "")
            : item;

        if (checkNull)
        {
            if (call)
            {
                code.Append(item).Append(" is {} ").Append(itemCache).Append(" ? ");

                value.Render(code, itemCache);

                code.Append(" : default").Append(defaultSourceBang);
            }
            else
            {
                if (isValueType && defaultSourceBang != null)
                {
                    var startIndex = code.Length;

                    value.Render(code, itemCache);

                    if (code[startIndex] == '(')
                    {
                        var count = code.Length - startIndex;

                        // The renderer emitted an explicit conversion prefix, so wrap only the item
                        // to keep that prefix applied to the null checked value.
                        code.Replace(item, $"({item} ?? default{defaultSourceBang})", startIndex, count);
                    }
                    else
                    {
                        code.Append(" ?? default").Append(defaultSourceBang);
                    }
                }
                else
                {
                    value.Render(code, item);

                    code.Append(sourceBang);
                }
            }
        }
        else
        {
            value.Render(code, itemCache);

            code.Append(sourceBang);
        }
    }
}

/// <summary>
/// Produces an additional, self-contained portion of code appended after the main mapping methods.
/// </summary>
internal abstract class ExtraCodeRenderer
{
    internal abstract void Render(StringBuilder code);
}

/// <summary>
/// Produces the whole set of methods (create, fill and try-get) for one direction of a mapping.
/// </summary>
internal abstract class MappingMethodsRenderer
{
    internal abstract void Render(StringBuilder code, ref RenderFlags rendered);
}

/// <summary>
/// Emits the copy method that materializes a destination collection from an origin collection,
/// covering the dictionary, indexed <c>for</c>, array-backed and add-based variants.
/// </summary>
internal sealed class CollectionMappingMethodRenderer(
    MemberMeta source,
    MemberMeta target,
    MemberMeta sourceItem,
    MemberMeta targetItem,
    bool call,
    CollectionInfo sourceCollInfo,
    CollectionInfo targetCollInfo,
    CollectionMapping collMapInfo,
    ValueRenderer? itemValueCreator,
    KeyValueMappings keyValueMapping) : MappingMethodsRenderer
{
    private readonly string
        _targetFullTypeName = target.Type.ExportNotNullFullName,
        _sourceFullTypeName = source.Type.ExportNotNullFullName,
        _targetItemFullTypeName = targetCollInfo.ItemDataType.FullName,
        _countProp = sourceCollInfo.CountProp,
        _copyMethodName = collMapInfo.MethodName;

    private readonly bool _isFor = collMapInfo.Iterator == "for";

    internal override void Render(StringBuilder code, ref RenderFlags isRendered)
    {
        if (isRendered.defaultMethod)
            return;

        isRendered.defaultMethod = true;

        string
            targetExportFullXmlDocTypeName = _targetFullTypeName.Replace("<", "{").Replace(">", "}"),
            sourceExportFullXmlDocTypeName = _sourceFullTypeName.Replace("<", "{").Replace(">", "}"),
            underlyingCollectionType = $"global::System.Collections.Generic.List<{_targetItemFullTypeName}>()";

        (var defaultType, var initType, var returnExpr) = (targetCollInfo.Type, target.Type.IsInterface) switch
        {
            (EnumerableType.ReadOnlyCollection, true) =>
                ($"global::SourceCrafter.Mapifier.CollectionExtensions<{_targetItemFullTypeName}>.EmptyReadOnlyCollection", underlyingCollectionType, (ValueRenderer)new ReadOnlyCollectionValueRenderer(_targetItemFullTypeName)),
            (EnumerableType.Collection, true) =>
                ($"global::SourceCrafter.Mapifier.CollectionExtensions<{_targetItemFullTypeName}>.EmptyCollection", underlyingCollectionType, LiteralValueRenderer.Instance),
            _ =>
                ($"new {_targetFullTypeName}()", $"{_targetFullTypeName}()", LiteralValueRenderer.Instance)
        };

        //Entity? <== EntityDto?
        bool checkNull = (!targetItem.IsNullable || !targetCollInfo.ItemDataType.IsStruct) && sourceItem.IsNullable,
            hasSuffix = (sourceCollInfo.Type, targetCollInfo.Type) is (not EnumerableType.Array, EnumerableType.ReadOnlySpan);

        string? suffix = hasSuffix ? ".AsSpan()" : null,
            sourceBang = sourceItem.Bang,
            defaultSourceBang = sourceItem.DefaultBang;

        if (targetCollInfo.IsDictionary)
        {
            code.Append(@"
    /// <summary>
    /// Creates a new instance of <see cref=""").Append(targetExportFullXmlDocTypeName).Append(@"""/> based from a given <see cref=""").Append(sourceExportFullXmlDocTypeName).Append(@"""/>
    /// </summary>
    /// <param name=""source"">Data source to be mapped</param>");

            if (targetCollInfo.ItemDataType.IsRecursive)
            {
                code.Append(@"
    /// <param name=""depth"">Depth index for recursivity control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>");
            }

            code.Append(@"
    public static ").Append(_targetFullTypeName).Append(' ').Append(_copyMethodName).Append("(this ").Append(_sourceFullTypeName).Append(" source");

            if (targetCollInfo.ItemDataType.IsRecursive)
            {
                code.Append(@", int depth = 0, int maxDepth = ").Append(target.MaxDepth).Append(@")
    {
        if (depth >= maxDepth) 
            return ").Append(defaultType).Append(@";
");
            }
            else
            {
                code.Append(@")
    {");
            }

            code.Append(@"
        var target = ").Append(defaultType).Append(@";

        foreach (var item in source)
        {
            target[");

            keyValueMapping.Key.Render(code, "item");

            code.Append("] = ");

            keyValueMapping.Value.Render(code, "item");

            code.Append(@";
        }

        return target;
    }
");
            return;
        }

        code.Append(@"
    /// <summary>
    /// Creates a new instance of <see cref=""").Append(targetExportFullXmlDocTypeName).Append(@"""/> based from a given <see cref=""").Append(sourceExportFullXmlDocTypeName).Append(@"""/>
    /// </summary>
    /// <param name=""source"">Source instance to be mapped</param>");

        if (targetCollInfo.ItemDataType.IsRecursive)
            code.Append(@"
    /// <param name=""depth"">Depth index for recursive control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>");

        code.Append(@"
    public static ").Append(_targetFullTypeName).Append(' ').Append(_copyMethodName).Append("(this ").Append(_sourceFullTypeName).Append(@" source");

        if (targetCollInfo.ItemDataType.IsRecursive)
        {
            code.Append(@", int depth = 0, int maxDepth = ").Append(target.MaxDepth).Append(@")
    {
        if (depth >= maxDepth) 
            return ");

            if (collMapInfo.CreateArray)
            {
                code.Append(@"global::System.Array.Empty<").Append(_targetItemFullTypeName).Append(">()");
            }
            else
            {
                code.Append(defaultType);
            }

            code.Append(@";
");
        }
        else
        {
            code.Append(@")
    {");
        }

        if (collMapInfo.CreateArray)
        {
            if (collMapInfo.Redim)
            {
                code.Append(@"
        int len = 0, aux = 16;
        var target = new ").Append(_targetItemFullTypeName).Append(@"[aux];
");
            }
            else
            {
                code.Append(@"
        int len = ");

                if (_isFor)
                {
                    code.Append("source.").Append(_countProp);
                }
                else
                {
                    code.Append('0');
                }

                code.Append(@";
        var target = new ").Append(_targetItemFullTypeName).Append('[');

                if (_isFor)
                {
                    code.Append("len");
                }
                else
                {
                    code.Append("source.").Append(_countProp);
                }

                code.Append(@"];
");
            }
        }
        else
        {
            code.Append(@"
        var target = new ").Append(initType).Append(';').Append(@"
");
        }

        if (_isFor)
        {
            code.Append(@"
        for (int i = 0; i < len; i++)
        {
            target[i] = ");

            ValueEmitter.Generate(code, "source[i]", itemValueCreator, checkNull, call, target.Type.IsValueType, sourceBang, defaultSourceBang);

            code.Append(@";
        }

        return target").Append(suffix).Append(@";
    }
");
        }
        else
        {
            code.Append(@"
        foreach (var item in source)
        {");

            if (collMapInfo.CreateArray)
            {
                code.Append(@"
            target[len");

                if (!collMapInfo.Redim)
                {
                    code.Append("++");
                }

                code.Append("] = ");

                ValueEmitter.Generate(code, "item", itemValueCreator, checkNull, call, target.Type.IsValueType, sourceBang, defaultSourceBang);

                code.Append(';');

                if (collMapInfo.Redim)
                {
                    //redim array
                    code.Append(@"

            if (aux == ++len)
                global::System.Array.Resize(ref target, aux *= 2);
        }

        return ");

                    if (hasSuffix) code.Append('(');

                    code.Append("len < aux ? target[..len] : target");

                    if (hasSuffix) code.Append(')').Append(suffix);

                    code.Append(@";
    }
");
                }
                //normal ending
                else
                {
                    code.Append(@"
        }

        return target").Append(suffix).Append(@";
    }
");
                }
            }
            else
            {
                code.Append(@"
            target.").Append(collMapInfo.Method);

                ValueEmitter.Generate(code, "item", itemValueCreator, checkNull, call, target.Type.IsValueType, sourceBang, defaultSourceBang); code.Append(@");
        }

        return ");

                returnExpr.Render(code, "target");

                code.Append(@";
    }
");
            }
        }
    }
}

/// <summary>
/// Emits the single conversion method projecting a key/value pair onto its counterpart.
/// </summary>
internal sealed class KeyValueMappingMethodRenderer(
    string returnTypeFullName,
    string methodName,
    string parameterTypeFullName,
    ValueRenderer value) : MappingMethodsRenderer
{
    internal override void Render(StringBuilder code, ref RenderFlags rendered)
    {
        if (rendered.defaultMethod)
            return;

        rendered.defaultMethod = true;

        code
    .Append(@"
    public static ")
    .Append(returnTypeFullName)
    .Append(' ')
    .Append(methodName)
    .Append("(ref this ")
    .Append(parameterTypeFullName)
    .Append(@" source)
    {
        return ");

        value.Render(code, "source");

        code.Append(@";
    }");
    }
}

/// <summary>
/// Emits the create/fill/try-get methods projecting an origin type onto a destination type.
/// </summary>
internal sealed class ObjectMappingMethodsRenderer(
    TypeMappingMeta map,
    bool towardsTarget,
    MemberMeta destination,
    MemberMeta origin,
    MutableFlag isRecursive,
    string typeStart,
    string typeEnd,
    string destinationFullTypeName,
    string originFullTypeName,
    MemberRendererList members) : MappingMethodsRenderer
{
    private readonly string
        _destinationXmlDocTypeName = map.TargetType.ExportNotNullFullName.Replace("<", "{").Replace(">", "}"),
        _originXmlDocTypeName = map.SourceType.ExportNotNullFullName.Replace("<", "{").Replace(">", "}");

    internal override void Render(StringBuilder code, ref RenderFlags rendered)
    {
        if (towardsTarget ? map.IsTargetRendered : map.IsSourceRendered)
            return;

        var maxDepth = destination.MaxDepth;

        if (isRecursive.Value && destination.MaxDepth == 0)
            maxDepth = destination.MaxDepth = 1;

        rendered.defaultMethod = true;

        if (destination.Type.HasZeroArgsCtor)
            CreateDefaultMethod(code, maxDepth);

        if (MappingKind.Fill.HasFlag(map.MappingsKind))
        {
            rendered.fillMethod = true;

            CreateFillMethod(code, maxDepth);

            foreach (var item in (towardsTarget ? map.TargetType : map.SourceType).UnsafePropertyFieldsGetters)
                item.Render(code);
        }

        if (towardsTarget ? map.AddTargetTryGet : map.AddSourceTryGet)
        {
            rendered.tryGetMethod = true;

            CreateTryGetMethod(code, maxDepth);
        }
    }

    private void CreateDefaultMethod(StringBuilder code, int maxDepth)
    {
        if (members.IsEmpty) return;

        code.Append(@"
    /// <summary>
    /// Creates a new instance of <see cref=""")
            .Append(_destinationXmlDocTypeName)
            .Append(@"""/> based on the given instance of <see cref=""")
            .Append(_originXmlDocTypeName)
            .Append(@"""/>
    /// </summary>
    /// <param name=""source"">Data source to be mapped</param>");

        code.Append(isRecursive.Value
            ? @"
    /// <param name=""depth"">Depth index for recursivity control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>"
            : @"
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");

        code.Append(@"
    public static ").Append(destinationFullTypeName).Append(' ').Append(towardsTarget ? map.ToTargetMethodName : map.ToSourceMethodName)
            .Append("(this ").Append(originFullTypeName).Append(" source");

        if (isRecursive.Value)
        {
            code.Append(@", int depth = 0, int maxDepth = ").Append(maxDepth).Append(@")
    {
        if (depth >= maxDepth) 
            return default").Append(origin.DefaultBang).Append(@";
");
        }
        else
        {
            code.Append(@")
    {");
        }

        code.Append(@"
        return ").Append(typeStart);

        members.Render(code, false);

        code.Append(typeEnd).Append(@";
    }
");
    }

    private void CreateTryGetMethod(StringBuilder code, int maxDepth)
    {
        if (members.IsEmpty) return;

        code.Append(@"
    /// <summary>
    /// Tries to create a new instance of <see cref=""").Append(_destinationXmlDocTypeName).Append(@"""/> based on a given instance of <see cref=""").Append(_originXmlDocTypeName).Append(@"""/> if it's not null
    /// </summary>
    /// <param name=""source"">Source instance</param>
    /// <param name=""target"">Target instance</param>")
            .Append(isRecursive.Value
            ? @"
    /// <param name=""depth"">Depth index for recursive control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>"
            : @"
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]")
            .Append(@"
    public static bool ").Append(towardsTarget ? map.TryGetTargetMethodName : map.TryGetSourceMethodName)
            .Append("(this ").Append(originFullTypeName).Append(" source, out ")
            .Append(destinationFullTypeName).Append(" target");

        if (isRecursive.Value)
        {
            code.Append(", int depth = 0, int maxDepth = ").Append(maxDepth).Append(@")
    {");
        }
        else
        {
            code.Append(@")
    {");
        }

        code.Append(@"
        if (source is { } _source)
        {
            target = ").Append(towardsTarget ? map.ToTargetMethodName : map.ToSourceMethodName).Append("(_source");

        if (isRecursive.Value)
        {
            code.Append(", depth, maxDepth");
        }

        code.Append(@");
            return true;
        }
        target = default").Append(origin.DefaultBang).Append(@";
        return false;
    }
");
    }

    private void CreateFillMethod(StringBuilder code, int maxDepth)
    {
        if (members.IsEmpty) return;

        var @ref = (towardsTarget ? map.TargetType : map.SourceType).IsValueType ? "ref " : null;

        code.Append(@"
    /// <summary>
    /// Update an instance of <see cref=""").Append(_destinationXmlDocTypeName).Append(@"""/> based on a given instance of <see cref=""")
            .Append(_originXmlDocTypeName)
            .Append(@""" />
    /// </summary>
    /// <param name=""source"">Source instance</param>
    /// <param name=""target"">Target instance</param>");

        if (isRecursive.Value)
        {
            code.Append(@"
    /// <param name=""depth"">Depth index for recursivity control</param>
    /// <param name=""maxDepth"">Max of recursion to be allowed to map</param>");
        }
        else
        {
            code.Append(@"
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
        }

        code.Append(@"
    public static ").Append(@ref).Append(destinationFullTypeName).Append(' ').Append(towardsTarget ? map.FillTargetMethodName : map.FillSourceMethodName).Append('(')
            .Append(@ref).Append("this ").Append(destinationFullTypeName).Append(" target, ")
            .Append(originFullTypeName).Append(" source");

        if (isRecursive.Value)
        {
            code.Append(", int depth = 0, int maxDepth = ").Append(maxDepth).Append(@")
    {
        if (depth >= maxDepth) 
            return ").Append(@ref).Append("target").Append(origin.DefaultBang).Append(';');
        }
        else
        {
            code.Append(@")
    {");
        }

        members.Render(code, true);

        code.Append(@"

        return ").Append(@ref).Append(@"target;
    }
");
    }
}

/// <summary>Ordered composition of extra code renderers. Replaces the multicast <c>Action&lt;StringBuilder&gt;</c>.</summary>
internal sealed class ExtraCodeRendererList : ExtraCodeRenderer
{
    private readonly List<ExtraCodeRenderer> _items = [];

    internal void Add(ExtraCodeRenderer renderer) => _items.Add(renderer);

    internal override void Render(StringBuilder code)
    {
        for (var i = 0; i < _items.Count; i++)
            _items[i].Render(code);
    }
}

/// <summary>Emits the mapping methods of a nested (member or item) mapping.</summary>
internal sealed class NestedMappingMethodsRenderer(TypeMappingMeta map) : ExtraCodeRenderer
{
    internal override void Render(StringBuilder code) => map.BuildMethods(code);
}

/// <summary>Emits an <c>UnsafeAccessor</c> declaration required to reach a non assignable member.</summary>
internal sealed class UnsafeAccessorRenderer(MemberCodeRenderer accessor) : ExtraCodeRenderer
{
    internal override void Render(StringBuilder code) => accessor.Render(code);
}

/// <summary>
/// Produces the portion of code that projects a single member, either as an initializer
/// entry (<paramref name="isFill"/> == <see langword="false"/>) or as an assignment inside a fill method.
/// </summary>
internal abstract class MemberRenderer
{
    internal abstract void Render(StringBuilder code, bool isFill);
}

/// <summary>Ordered composition of member renderers. Replaces the multicast delegate accumulation.</summary>
internal sealed class MemberRendererList : MemberRenderer
{
    private readonly List<MemberRenderer> _items = [];

    internal bool IsEmpty => _items.Count == 0;

    internal void Add(MemberRenderer renderer) => _items.Add(renderer);

    internal override void Render(StringBuilder code, bool isFill)
    {
        foreach (var item in _items)
            item.Render(code, isFill);
    }
}

/// <summary>Emits the projection of a member resolved through a <see cref="TypeMappingMeta"/>.</summary>
internal sealed class MappedMemberRenderer(
    TypeMappingMeta memberMap,
    bool towardsTarget,
    MemberMeta source,
    MemberMeta target,
    CommaState comma,
    string spacing,
    TypeMappingMeta extrasOwner,
    bool canUseUnsafeAccessor) : MemberRenderer
{
    internal override void Render(StringBuilder code, bool isFill)
    {
        MemberMappingEmitter.Build(
            code,
            isFill,
            comma,
            spacing,
            new MemberMappingContext(source, target),
            towardsTarget ? memberMap.BuildTargetValue : memberMap.BuildSourceValue,
            towardsTarget ? memberMap.ToTargetMethodName : memberMap.ToSourceMethodName,
            towardsTarget ? memberMap.FillTargetMethodName : memberMap.FillSourceMethodName,
            towardsTarget ? memberMap.SourceRequiresMapper : memberMap.TargetRequiresMapper,
            towardsTarget ? memberMap.SourceHasScalarConversion : memberMap.TargetHasScalarConversion,
            canUseUnsafeAccessor);

        if (target.Type.NullableMethodUnsafeAccessor is { } nullUnsafeAccesor)
            extrasOwner.ExtraMappings.Add(new UnsafeAccessorRenderer(nullUnsafeAccesor));
    }
}

/// <summary>
/// Flattened, emission-ready snapshot of the two members taking part in a projection.
/// It isolates <see cref="MemberMappingEmitter"/> from the analysis metadata types.
/// </summary>
internal readonly struct MemberMappingContext(MemberMeta source, MemberMeta target)
{
    public string SourceName { get; } = source.Name;
    public string? SourceBang { get; } = source.Bang;
    public string? SourceDefaultBang { get; } = source.DefaultBang;
    public bool SourceIsNullable { get; } = source.IsNullable;
    public bool SourceTypeIsValueType { get; } = source.Type.IsValueType;
    public bool SourceTypeHasMembers { get; } = source.Type.HasMembers;
    public string TargetName { get; } = target.Name;
    public string TargetTypeFullName { get; } = target.Type.FullName;
    public bool TargetIsNullable { get; } = target.IsNullable;
    public bool TargetTypeIsPrimitive { get; } = target.Type.IsPrimitive;
    public bool TargetTypeIsValueType { get; } = target.Type.IsValueType;
    public bool TargetTypeIsStruct { get; } = target.Type.IsStruct;
    public bool TargetTypeIsIterable { get; } = target.Type.IsIterable;
    public bool TargetTypeHasMembers { get; } = target.Type.HasMembers;
    public bool TargetCanBeInitialized { get; } = target.CanBeInitialized;
    public bool TargetIsAccessible { get; } = target.IsAccessible;
    public bool TargetIsReadOnly { get; } = target.IsReadOnly;
    public bool TargetIsInit { get; } = target.IsInit;
    public bool TargetIsProperty { get; } = target.IsProperty;
    public bool TargetIsAutoProperty { get; } = target.IsAutoProperty;

    public bool? OwnerIsTupleType { get; } = target.OwningType?.IsTupleType;
    public bool OwnerIsValueTypeOrStruct { get; } = target.OwningType is not { IsValueType: false, IsStruct: false };
    public string? OwnerSanitizedName { get; } = target.OwningType?.SanitizedName;
    public string? OwnerNotNullFullName { get; } = target.OwningType?.NotNullFullName;
    public HashSet<PropertyCodeRenderer>? OwnerUnsafePropertyFieldsGetters { get; } = target.OwningType?.UnsafePropertyFieldsGetters;
}

/// <summary>
/// Emits the code of a single member projection, including the unsafe accessors required
/// to reach non assignable members.
/// </summary>
internal static class MemberMappingEmitter
{
    internal static void Build(
        StringBuilder code,
        bool isFill,
        CommaState comma,
        string spacing,
        MemberMappingContext member,
        ValueRenderer? generateSourceValue,
        string copyTargetMethodName,
        string fillTargetMethodName,
        bool sourceRequiresMapper,
        bool sourceHasScalarConversion,
        bool canUseUnsafeAccessor)
    {
        bool checkNull = (!member.TargetIsNullable || !member.TargetTypeIsPrimitive || sourceRequiresMapper || sourceHasScalarConversion) && member.SourceIsNullable,
             isSourceValueType = member.SourceTypeIsValueType,
             isTargetValueType = member.TargetTypeIsValueType,
             isAccessibleOrInitializable = member is { TargetCanBeInitialized: true, TargetIsAccessible: true };

        string sourceName = member.SourceName;

        if (isFill)
        {
            string? defaultSourceBang = member.SourceDefaultBang;

            bool
                isNotAssignable = (member.TargetIsReadOnly || member.TargetIsInit)
                    && (!member.TargetIsProperty || member.TargetIsAutoProperty), useUnsafeWriter = member.OwnerIsTupleType is not true
                    && isNotAssignable
                    && (member.TargetTypeIsValueType || member.TargetTypeHasMembers), isAssignable = member is { TargetIsProperty: true, TargetIsInit: false, TargetIsReadOnly: false } or { OwnerIsTupleType: true };

            if (useUnsafeWriter && !canUseUnsafeAccessor) return;

            bool
                useFillMethod = !sourceHasScalarConversion && sourceRequiresMapper && member is { TargetTypeIsIterable: false, TargetTypeHasMembers: true, TargetTypeIsPrimitive: false }, useCopyMethod = !sourceHasScalarConversion && sourceRequiresMapper && (!member.TargetTypeIsIterable || member.TargetTypeHasMembers), isValueType = member is not { TargetTypeIsValueType: false, TargetTypeIsStruct: false }, isParentValueType = member.OwnerIsValueTypeOrStruct, useNullUnsafeWriter = isValueType && member.TargetIsNullable, isSourceNullable = member.SourceIsNullable, isSourceNullableValueType = isSourceNullable && member.SourceTypeIsValueType, isTargetNullable = member.TargetIsNullable && member.SourceTypeHasMembers, hasSourceTypeMembers = member.SourceTypeHasMembers, isTargetProperty = member.TargetIsProperty;

            string targetMemberName = member.TargetName;

            if (useUnsafeWriter)
            {
                string
                    getPrivFieldMethodName = $"{member.OwnerSanitizedName}_{targetMemberName}",
                    targetOwnerXmlDocType = member.OwnerNotNullFullName!.Replace("<", "{").Replace(">", "}"),
                    owningTypeNotNullFullName = member.OwnerNotNullFullName!;

                member.OwnerUnsafePropertyFieldsGetters!.Add(new(targetMemberName, $@"
    /// <summary>
    /// Gets a reference to {(member.TargetIsProperty ? $@"the backing field of <see cref=""{targetOwnerXmlDocType}.{targetMemberName}""/> property" : $"the field <see cref=\"{targetOwnerXmlDocType}.{targetMemberName}\"/>")}
    /// </summary>
    /// <param name=""_""><see cref=""{targetOwnerXmlDocType}""/> container reference of {targetMemberName} {(isTargetProperty ? "property" : "field")}</param>
    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = ""{(member.TargetIsProperty ? $"<{targetMemberName}>k__BackingField" : targetMemberName)}"")]
    extern static ref {member.TargetTypeFullName}{(member.TargetIsNullable ? "?" : null)} {getPrivFieldMethodName}({owningTypeNotNullFullName} _);
"));
                if (isSourceNullable)
                {
                    if (isTargetNullable && hasSourceTypeMembers)
                    {
                        code.Append(@"
        if (source.").Append(member.SourceName).Append(@" != null)
            if(target.").Append(targetMemberName).Append(@" != null) 
                ");
                        var capturedTarget = "";

                        if (useFillMethod)
                        {
                            code.Append(fillTargetMethodName).Append('(');

                            if (isValueType)
                            {
                                code.Append("ref ");
                            }

                            BuildTargetValue(code, out capturedTarget);

                            code.Append("!, ");

                            BuildSourceParam(fill: true);

                            code.Append(')');
                        }
                        else
                        {
                            BuildTargetValue(code, out capturedTarget);

                            code.Append(" = ");

                            BuildSourceParam();
                        }

                        code.Append(@";
            else
                ").Append(capturedTarget).Append(" = ");

                        BuildSourceParam();

                        code.Append(@";
        else
            ").Append(capturedTarget).Append(" = default").Append(defaultSourceBang).Append(';');
                    }
                    else
                    {
                        code.Append(@"
        if (source.").Append(member.SourceName).Append(@" is not null)
            ");
                        string targetValue;

                        if (useFillMethod)
                        {
                            code.Append(fillTargetMethodName).Append('(');

                            if (isValueType) code.Append("ref ");

                            BuildTargetValue(code, out targetValue);

                            code.Append(", ");

                            BuildSourceParam(fill: true);

                            code.Append(')');
                        }
                        else
                        {
                            BuildTargetValue(code, out targetValue);

                            code.Append(" = ");

                            BuildSourceParam();
                        }

                        code.Append(@";
        else
            ").Append(targetValue).Append(" = default").Append(member.SourceDefaultBang).Append(';');
                    }
                }
                else
                {
                    code.Append(@"
        ");

                    if (useFillMethod)
                    {
                        code.Append(fillTargetMethodName).Append('(');

                        if (isValueType)
                        {
                            code.Append("ref ");
                        }

                        BuildTargetValue(code, out _);

                        code.Append(", ");

                        BuildSourceParam(fill: true);

                        code.Append(')');
                    }
                    else
                    {
                        BuildTargetValue(code, out _);

                        code.Append(" = ");

                        BuildValue();
                    }

                    code.Append(';');
                }

                void BuildTargetValue(StringBuilder code, out string targetValue, string targetPrefix = "target.")
                {
                    var start = code.Length;
                    if (isNotAssignable)
                    {
                        targetPrefix = getPrivFieldMethodName + "(";

                        if (isParentValueType)
                        {
                            targetPrefix += "ref ";
                        }

                        code.Append(targetPrefix).Append("target)");
                    }
                    else
                    {
                        code.Append(targetPrefix + targetMemberName);
                    }

                    targetValue = code.ToString(start, code.Length - start);
                }

                void BuildSourceParam(string sourceExpr = "source.", bool fill = false)
                {
                    var pos = code.Length;

                    code.Append(sourceExpr).Append(member.SourceName);

                    code.Append(isSourceNullable && member.SourceTypeIsValueType ? ".Value" : member.SourceBang);

                    if (!fill && useCopyMethod)
                    {
                        code.Insert(pos, '(').Insert(pos, copyTargetMethodName).Append(')');
                    }
                }
            }
            else if (isAssignable)
            {
                code.Append(@"
        target.").Append(targetMemberName).Append(" = ");

                BuildValue();

                code.Append(';');
            }
        }
        else if (isAccessibleOrInitializable)
        {
            var isOwningValueType = member.OwnerIsTupleType is not true;

            code.Append(comma.Next(",")).Append(spacing);

            if (isOwningValueType)
            {
                code.Append(member.TargetName).Append(" = ");
            }

            BuildValue();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void BuildValue()
        {
            ValueEmitter.Generate(
                code,
                "source." + sourceName,
                generateSourceValue,
                checkNull,
                (!sourceHasScalarConversion || !isSourceValueType) && sourceRequiresMapper,
                isTargetValueType,
                member.SourceBang,
                member.SourceDefaultBang);
        }
    }
}
