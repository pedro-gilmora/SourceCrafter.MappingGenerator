using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceCrafter.Mapifier.Constants;
using SourceCrafter.Mapifier.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace SourceCrafter.Mapifier;

public static class Extensions
{
    extension(IgnoreBind value)
    {
        public IgnoreBind Inverse => value switch
        {
            IgnoreBind.Target => IgnoreBind.Source,
            IgnoreBind.Source => IgnoreBind.Target,
            _ => value
        };
    }
}

/// <summary>
/// Pure-data projection of an attribute. Captured during discovery so <see cref="MemberMeta"/>
/// never retains <c>AttributeData</c>, which would root the whole compilation.
/// </summary>
internal readonly record struct MemberAttributeInfo(
    string ClassName,
    object? FirstArgValue,
    object? SecondArgValue,
    string? BindMemberName,
    int? BindMemberId);

internal sealed record MemberMeta(
    int Id,
    string Name,
    bool IsNullable,
    bool IsReadOnly = false,
    bool IsWriteOnly = false,
    bool IsInit = false,
    bool IsProperty = false,
    ImmutableArray<MemberAttributeInfo> Attributes = default)
{
    internal string? DefaultBang, Bang;

    internal short MaxDepth = 1;

    internal TypeMeta Type = null!;

    internal TypeMeta OwningType = null!;

    internal bool IsAutoProperty, CanBeInitialized = true, IsAccessible = true;

    internal int Position;

    public override string ToString() =>
        $"({Type?.ToString() ?? "?"}) {(OwningType?.ExportNotNullFullName is { } name ? name + "." : null)}{Name}";
}


internal sealed class TypeMeta
{
    internal readonly string Id;

    internal readonly string
        FullName,
        NotNullFullName;

    private readonly string _nonGenericFullName;

    internal readonly string
        ExportNonGenericFullName,
        SanitizedName,
        ExportFullName,
        ExportNotNullFullName;

    internal ImmutableArray<string> ContainingPath = [];

    /// <summary>
    /// Containing types then namespaces, innermost first, snapshotted at construction so the
    /// rendering phase never has to touch <see cref="Symbol"/>.
    /// </summary>
    internal readonly ImmutableArray<(string Name, bool IsGlobalNamespace)> ContainerChain;

    /// <summary>Arity of the tuple, or 0 when this is not a tuple type.</summary>
    internal readonly int TupleElementCount;

    /// <summary>True for pointer, function-pointer, delegate or unknown type kinds.</summary>
    internal readonly bool IsDelegateOrPointer;

    internal readonly CollectionInfo CollectionInfo;

    internal readonly MemberCodeRenderer? NullableMethodUnsafeAccessor = null;

    public readonly HashSet<PropertyCodeRenderer> UnsafePropertyFieldsGetters = new(PropertyCodeEqualityComparer.Default);

    internal readonly bool
        AllowsNull,
        IsTupleType,
        IsObject,
        IsReadOnly,
        IsStruct,
        IsInterface,
        IsValueType,
        IsKeyValueType,
        IsReference,
        HasZeroArgsCtor,
        IsPrimitive,
        IsEnum,
        IsIterable;

    internal bool IsRecursive;
    internal readonly bool DictionaryOwned;
    internal readonly string Name;
    internal readonly bool IsNullable;

    internal readonly ImmutableArray<MemberMeta> Members = [];

    internal bool HasMembers => !Members.IsDefaultOrEmpty;

    internal TypeMeta(
        Dictionary<string, TypeMeta> typeRegistry,
        Compilation compilation,
        ITypeSymbol membersSource,
        ITypeSymbol? implementation,
        string typeId,
        bool dictionaryOwned,
        TypeMetaGetOrAddHandler getOrAddTypeFunc,
        Dictionary<string, ITypeSymbol> symbolRegistry)
    {
        var type = implementation ?? membersSource;

        Id = typeId;

        DictionaryOwned = dictionaryOwned;

        type.TryGetNullable(out type, out IsNullable);

        var precedingNamespace = implementation?.ContainingNamespace?.ToString() == "<global namespace>"
            ? membersSource.ContainingNamespace.GlobalNamespaced + "."
            : null;

        FullName = NotNullFullName = precedingNamespace + type.GlobalNamespaced;
        Name = type.TypeKind is TypeKind.Interface ? type.Name[1..] : type.Name;

        ExportFullName = ExportNotNullFullName = membersSource.AsNonNullable() is { } memberSource
            ? memberSource.GlobalNamespaced.TrimEnd('?')
            : FullName;

        if (IsNullable)
        {
            FullName += "?";
            ExportFullName += "?";
        }

        SanitizedName = SanitizeTypeName(type);
        AllowsNull = type.AllowsNull;
        _nonGenericFullName = type.FullGlobalQualifiedNonGenericName;
        IsObject = SymbolEqualityComparer.Default.Equals(type, compilation.ObjectType);
        ExportNonGenericFullName = type.FullGlobalQualifiedNonGenericName;
        IsTupleType = type.IsTupleType;
        IsValueType = type.IsValueType;
        IsKeyValueType = _nonGenericFullName.Length > 12 && _nonGenericFullName[^12..] is "KeyValuePair";
        IsStruct = type.TypeKind is TypeKind.Struct;
        IsPrimitive = type.IsPrimitive();
        IsReadOnly = type.IsReadOnly;
        IsInterface = type.TypeKind == TypeKind.Interface;
        IsReference = type.IsReferenceType;

        HasZeroArgsCtor =
            (type is INamedTypeSymbol { InstanceConstructors: { Length: > 0 } ctors }
             && ctors.FirstOrDefault(ctor => ctor.Parameters.IsDefaultOrEmpty)?
                    .DeclaredAccessibility is null or Microsoft.CodeAnalysis.Accessibility.Public or Microsoft.CodeAnalysis.Accessibility.Internal)
            || implementation?.Kind is SymbolKind.ErrorType;

        symbolRegistry[typeId] = type;

        IsDelegateOrPointer = type.TypeKind
            is TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Delegate or TypeKind.Unknown;

        TupleElementCount = type is INamedTypeSymbol { IsTupleType: true } tupleType
            ? tupleType.TupleElements.Length
            : 0;

        var containers = ImmutableArray.CreateBuilder<(string, bool)>();

        for (ISymbol? container = type.ContainingType ?? (ISymbol?)type.ContainingNamespace;
            container is not null;
            container = container.ContainingType ?? (ISymbol?)container.ContainingNamespace)
        {
            containers.Add((container.NameOnly, container is INamespaceSymbol { IsGlobalNamespace: true }));
        }

        ContainerChain = containers.ToImmutable();

        ContainingPath = [.. (type.ContainingNamespace?.ToDisplayParts() ?? [])
                                .Where(i => i.Kind is not SymbolDisplayPartKind.Punctuation)
                                .Select(i => i.ToString())];

        IsIterable = IsEnumerableType(_nonGenericFullName, type, getOrAddTypeFunc, out CollectionInfo);

        typeRegistry[typeId] = this;
        if (IsPrimitive) return;

        Members = IsTupleType
            ? GetAllMembers(((INamedTypeSymbol)membersSource!.AsNonNullable()).TupleElements, compilation, getOrAddTypeFunc)
            : GetAllMembers(membersSource!.AsNonNullable(), compilation, getOrAddTypeFunc);
    }

    /// <summary>
    /// Resolves everything the mapping rules need from an attribute while symbols are still
    /// available, so nothing Roslyn-shaped survives into the rendering phase.
    /// </summary>
    private static ImmutableArray<MemberAttributeInfo> ProjectAttributes(ISymbol member, Compilation compilation)
    {
        var attributes = member.GetAttributes();

        if (attributes.IsDefaultOrEmpty) return [];

        var projected = ImmutableArray.CreateBuilder<MemberAttributeInfo>(attributes.Length);

        foreach (var attr in attributes)
        {
            if (attr.AttributeClass?.GlobalNamespaced is not { } className) continue;

            // Only the mapping attributes are projected. Reading TypedConstant.Value on an
            // array-valued argument throws, so unrelated attributes must not be inspected.
            if (className is not ("global::SourceCrafter.Mapifier.Attributes.IgnoreBindAttribute"
                or "global::SourceCrafter.Mapifier.Attributes.MaxRecursionAttribute"
                or "global::SourceCrafter.Mapifier.Attributes.BindAttribute"))
                continue;

            object? firstArg = ScalarArg(attr, 0), secondArg = ScalarArg(attr, 1);

            string? bindMemberName = null;
            int? bindMemberId = null;

            if (className == "global::SourceCrafter.Mapifier.Attributes.BindAttribute"
                && (attr.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax)?.ArgumentList?.Arguments is
                    [{ Expression: InvocationExpressionSyntax
                    {
                        Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                        ArgumentList.Arguments: [{ Expression: MemberAccessExpressionSyntax { Name: { } id } }]
                    }}, ..])
            {
                bindMemberName = id.Identifier.Text;
                bindMemberId = SymbolEqualityComparer.Default.GetHashCode(
                    compilation.GetSemanticModel(id.SyntaxTree).GetSymbolInfo(id).Symbol);
            }

            projected.Add(new(className, firstArg, secondArg, bindMemberName, bindMemberId));
        }

        return projected.ToImmutable();

        // TypedConstant.Value throws when the argument is an array; guard on Kind.
        static object? ScalarArg(AttributeData attr, int index) =>
            attr.ConstructorArguments.Length > index
                && attr.ConstructorArguments[index] is { Kind: not TypedConstantKind.Array } arg
                    ? arg.Value
                    : null;
    }

    ImmutableArray<MemberMeta> GetAllMembers(ITypeSymbol type, Compilation compilation, TypeMetaGetOrAddHandler getOrAddType)
    {
        HashSet<MemberMeta> members = new(PropertyNameEqualityComparer.Default);

        HashSet<int> ids = [];

        var isInterface = type.TypeKind == TypeKind.Interface;

        ProcessMembers(type);

        return [.. members.OrderBy(m => m.Position)];

        void ProcessMembers(ITypeSymbol typeToCheck, bool isFirstLevel = true)
        {
            if (typeToCheck.Name is not (null or "Object"))
            {
                var i = 0;
                foreach (var member in typeToCheck!.GetMembers())
                {
                    if (member is IFieldSymbol { AssociatedSymbol: IPropertySymbol s })
                    {
                        ids.Add(SymbolEqualityComparer.Default.GetHashCode(s));
                        continue;
                    }
                    else if (member is not (IPropertySymbol or IFieldSymbol))
                    {
                        continue;
                    }

                    if (member.DeclaredAccessibility is not (Microsoft.CodeAnalysis.Accessibility.Internal or Microsoft.CodeAnalysis.Accessibility.Public))
                        continue;

                    var isAccessible = member.DeclaredAccessibility is Microsoft.CodeAnalysis.Accessibility.Public ||
                            SymbolEqualityComparer.Default.Equals(compilation.SourceModule, member.ContainingModule);


                    switch (member)
                    {
                        case IPropertySymbol
                        {
                            ContainingType.Name: not ['I', 'E', 'n', 'u', 'm', 'e', 'r', 'a', 't', 'o', 'r', ..],
                            IsIndexer: false,
                            Type: { } memberType,
                            RefCustomModifiers: { },
                            IsImplicitlyDeclared: var impl,
                            IsStatic: false,
                            IsReadOnly: var isReadonly,
                            IsWriteOnly: var isWriteOnly,
                            SetMethod: var setMethod
                        } when isInterface || !impl:
                            var id = SymbolEqualityComparer.Default.GetHashCode(member);

                            members.Add(
                                new(id,
                                    member.NameOnly,
                                    memberType.IsNullable,
                                    isReadonly,
                                    isWriteOnly,
                                    setMethod?.IsInitOnly == true,
                                    true,
                                    ProjectAttributes(member, compilation))
                                {
                                    Type = getOrAddType(memberType),
                                    CanBeInitialized = !isReadonly,
                                    IsAutoProperty = ids.Contains(id),
                                    Position = i++,
                                    IsAccessible = isAccessible
                                });

                            continue;

                        case IFieldSymbol
                        {
                            ContainingType.Name: not ['I', 'E', 'n', 'u', 'm', 'e', 'r', 'a', 't', 'o', 'r', ..],
                            Type: { } memberType,
                            IsStatic: false,
                            IsReadOnly: var isReadonly,

                        }:
                            members.Add(
                                new(SymbolEqualityComparer.Default.GetHashCode(member),
                                    member.NameOnly,
                                    memberType.IsNullable,
                                    true,
                                    !isReadonly,
                                    true,
                                    Attributes: ProjectAttributes(member, compilation))
                                {
                                    Type = getOrAddType(memberType.AsNonNullable()),
                                    Position = i++,
                                    CanBeInitialized = !isReadonly,
                                    IsAccessible = isAccessible
                                });

                            continue;

                        default:
                            continue;
                    }
                }

                if (typeToCheck.BaseType != null)
                    ProcessMembers(typeToCheck.BaseType);

                if (!isFirstLevel) return;

                foreach (var iface in typeToCheck.AllInterfaces)
                    ProcessMembers(iface, false);
            }
        }
    }

    private static ImmutableArray<MemberMeta> GetAllMembers(ImmutableArray<IFieldSymbol> tupleFields, Compilation compilation, TypeMetaGetOrAddHandler getOrAddType)
    {
        HashSet<MemberMeta> members = new(PropertyNameEqualityComparer.Default);

        if (tupleFields.IsDefaultOrEmpty) return [];

        foreach (var member in tupleFields)
            members.Add(
                new(SymbolEqualityComparer.Default.GetHashCode(member),
                    member.NameOnly,
                    member.Type.IsNullable,
                    false,
                    false,
                    true,
                    Attributes: [])
                {
                    Type = getOrAddType(member.Type.AsNonNullable())
                });

        return [.. members];
    }

    internal bool Equals(TypeMeta obj) => StringComparer.Ordinal.Equals(FullName, obj.FullName);

    public override string ToString() => FullName;

    private class PropertyCodeEqualityComparer : IEqualityComparer<PropertyCodeRenderer>
    {
        internal readonly static PropertyCodeEqualityComparer Default = new();
        public bool Equals(PropertyCodeRenderer? x, PropertyCodeRenderer? y)
        {
            return x?.Key == y?.Key;
        }

        public int GetHashCode(PropertyCodeRenderer obj)
        {
            return obj.Key.GetHashCode();
        }
    }

    internal static string SanitizeTypeName(ITypeSymbol type)
    {
        switch (type)
        {
            case INamedTypeSymbol { IsTupleType: true, TupleElements: { Length: > 0 } els }:

                return $"TupleOf{string.Join("", els.Select(f => SanitizeTypeName(f.Type)))}";

            case INamedTypeSymbol { IsGenericType: true, TypeArguments: { } args }:

                return type.Name + "Of" + string.Join("", args.Select(SanitizeTypeName));

            default:

                var typeName = type.TypeNameFormat;

                if (type is IArrayTypeSymbol { ElementType: { } elType })
                    typeName = $"{SanitizeTypeName(elType)}Array";

                return char.ToUpperInvariant(typeName[0]) + typeName[1..].TrimEnd('?', '_');
        }
        ;
    }

    private class PropertyNameEqualityComparer : IEqualityComparer<MemberMeta>
    {
        internal readonly static PropertyNameEqualityComparer Default = new();

        public bool Equals(MemberMeta? x, MemberMeta? y) => x?.Name == y?.Name;

        public int GetHashCode(MemberMeta obj) => obj.Name.GetHashCode();
    }

    private static bool IsEnumerableType(string nonGenericFullName, ITypeSymbol type, TypeMetaGetOrAddHandler getOrAddType, out CollectionInfo info)
    {
        if (type.IsPrimitive(true))
        {
            info = default!;
            return false;
        }

        switch (nonGenericFullName)
        {
            case "global::System.Collections.Generic.Dictionary" or "global::System.Collections.Generic.IDictionary"
            :
                info = GetCollectionInfo(getOrAddType, EnumerableType.Dictionary, GetEnumerableType(type, true));

                return true;

            case "global::System.Collections.Generic.Stack"
            :
                info = GetCollectionInfo(getOrAddType, EnumerableType.Stack, GetEnumerableType(type));

                return true;

            case "global::System.Collections.Generic.Queue"
            :
                info = GetCollectionInfo(getOrAddType, EnumerableType.Queue, GetEnumerableType(type));

                return true;

            case "global::System.ReadOnlySpan"
            :

                info = GetCollectionInfo(getOrAddType, EnumerableType.ReadOnlySpan, GetEnumerableType(type));

                return true;

            case "global::System.Span"
            :
                info = GetCollectionInfo(getOrAddType, EnumerableType.Span, GetEnumerableType(type));

                return true;

            case "global::System.Collections.Generic.ICollection" or
                "global::System.Collections.Generic.IList" or
                "global::System.Collections.Generic.List"
            :
                info = GetCollectionInfo(getOrAddType, EnumerableType.Collection, GetEnumerableType(type));

                return true;

            case "global::System.Collections.Generic.IReadOnlyList" or
                "global::System.Collections.Generic.ReadOnlyList" or
                "global::System.Collections.Generic.IReadOnlyCollection" or
                "global::System.Collections.Generic.ReadOnlyCollection"
            :
                info = GetCollectionInfo(getOrAddType, EnumerableType.ReadOnlyCollection, GetEnumerableType(type));

                return true;

            case "global::System.Collections.Generic.IEnumerable"
            :
                info = GetCollectionInfo(getOrAddType, EnumerableType.Enumerable, GetEnumerableType(type));

                return true;

            default:
                if (type is IArrayTypeSymbol { ElementType: { } elType })
                {
                    info = GetCollectionInfo(getOrAddType, EnumerableType.Array, elType);

                    return true;
                }
                else
                    foreach (var item in type.AllInterfaces)
                        if (IsEnumerableType(item.FullGlobalQualifiedNonGenericName, item, getOrAddType, out info))
                            return true;
                break;
        }

        info = default!;

        return false;
    }

    private static ITypeSymbol GetEnumerableType(ITypeSymbol enumerableType, bool isDictionary = false)
    {
        if (isDictionary)
            return ((INamedTypeSymbol)enumerableType)
                .AllInterfaces
                .First(i => i.Name.StartsWith("IEnumerable"))
                .TypeArguments
                .First();

        return ((INamedTypeSymbol)enumerableType)
            .TypeArguments
            .First();
    }

    private static CollectionInfo GetCollectionInfo(TypeMetaGetOrAddHandler getOrAddType, EnumerableType enumerableType, ITypeSymbol typeSymbol)
    {
        var itemDataType = getOrAddType((typeSymbol = typeSymbol.AsNonNullable()), enumerableType == EnumerableType.Dictionary);

        return enumerableType switch
        {
#pragma warning disable format
            EnumerableType.Dictionary =>
                new(itemDataType, 
                    enumerableType, 
                    typeSymbol.IsNullable, 
                    true, 
                    true, 
                    false, 
                    "Add", 
                    "Count"),
            EnumerableType.Queue =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable,
                    false,
                    true,
                    false,
                    "Enqueue",
                    "Count"),
            EnumerableType.Stack =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable,
                    false,
                    true,
                    false,
                    "Push",
                    "Count"),
            EnumerableType.Enumerable =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable,
                    false,
                    false,
                    true,
                    null,
                    "Length"),
            EnumerableType.ReadOnlyCollection =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable,
                    true,
                    true,
                    false,
                    "Add",
                    "Count"),
            EnumerableType.ReadOnlySpan =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable,
                    true,
                    true,
                    true,
                    null,
                    "Length"),
            EnumerableType.Collection =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable,
                    true,
                    true,
                    false,
                    "Add",
                    "Count"),
            EnumerableType.Span =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable,
                    true,
                    true,
                    true,
                    null,
                    "Length"),
            _ =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable,
                    true,
                    true,
                    true,
                    null,
                    "Length")
#pragma warning restore format
        };
    }
}

internal record PropertyCodeRenderer(string Key, string Code) : MemberCodeRenderer(Code);

internal record MemberCodeRenderer(string Code)
{
    internal bool Rendered { get; set; }

    internal void Render(StringBuilder code)
    {
        if (Rendered) return;

        Rendered = true;
        code.Append(Code);
    }
}

struct SpanCapture(StringBuilder code)
{
    int start = code.Length;
    int end = code.Length;
    public void StartCapture() => start = code.Length;
    public void EndCapture() => end = code.Length;
    public readonly string CapturedSpan => code.ToString(start, end - start);
}

#pragma warning disable CS8618
internal class TypeMappingMeta
{
    private RenderFlags _sourceRenderFlags, _targetRenderFlags;

    private bool _rendered = false;

    internal ValueRenderer?
        BuildTargetValue = default,
        BuildSourceValue = default;

    internal readonly string
        ToTargetMethodName,
        ToSourceMethodName,
        TryGetTargetMethodName,
        TryGetSourceMethodName,
        FillTargetMethodName,
        FillSourceMethodName;
    //internal readonly int Id;
    internal readonly string MappingId;

    internal readonly ExtraCodeRendererList ExtraMappings = new();

    private readonly bool _isScalar;

    internal readonly bool
        AreSameType,
        IsObjectMapping,
        CanDepth,
        IsTupleFromClass,
        IsReverseTupleFromClass;


    internal bool? CanMap;

    internal readonly TypeMeta TargetType, SourceType;

    internal CollectionMapping SourceCollectionMap, TargetCollectionMap;

    internal readonly TypeMappingMeta ItemMap;

    internal bool
        AddSourceTryGet,
        AddTargetTryGet,
        TargetHasScalarConversion,
        SourceHasScalarConversion,
        TargetRequiresMapper,
        SourceRequiresMapper,
        IsCollection;


    internal bool IsTargetRendered => _targetRenderFlags is not (false, false, false, false);
    internal bool IsSourceRendered => _sourceRenderFlags is not (false, false, false, false);

    internal int TargetMemberCount, SourceMemberCount;

    internal MappingMethodsRenderer? BuildTargetMethod, BuildSourceMethod;

    internal KeyValueMappings TargetKeyValueMap, SourceKeyValueMap;

    internal MappingKind MappingsKind { get; set; }

    internal bool HasTargetToSourceMap => SourceHasScalarConversion || IsCollection is true || SourceMemberCount > 0;

    internal bool HasSourceToTargetMap => TargetHasScalarConversion || IsCollection is true || TargetMemberCount > 0;

    internal void CollectTarget(ref TypeMeta existingTarget, string targetId)
    {
        if (TargetType.Id == targetId)
            existingTarget = TargetType;
        else if (SourceType.Id == targetId)
            existingTarget = SourceType;
    }

    internal void CollectSource(ref TypeMeta existingSource, string sourceId)
    {
        if (SourceType.Id == sourceId)
            existingSource = SourceType;
        else if (TargetType.Id == sourceId)
            existingSource = TargetType;
    }

    public override string ToString() => $"{TargetType.FullName} <=> {SourceType.FullName}";

    internal void EnsureDirection(ref MemberMeta target, ref MemberMeta source)
    {
        target.Type ??= TargetType;
        source.Type ??= SourceType;

        if ((TargetType.Id, SourceType.Id) == (source.Type.Id, target.Type.Id))
            (target, source) = (source, target);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal string GetFileName()
    {
        string source = SourceType.SanitizedName,
            target = TargetType.SanitizedName;

        var sourceChain = SourceType.ContainerChain;
        var targetChain = TargetType.ContainerChain;

        for (int i = 0; source == target; i++)
        {
            bool hasSource = i < sourceChain.Length,
                hasTarget = i < targetChain.Length;

            if (hasSource && hasTarget)
            {
                source = sourceChain[i].Name + source;
                target = targetChain[i].Name + target;
                continue;
            }

            if (hasSource && sourceChain[i].IsGlobalNamespace)
                return sourceChain[i].Name + source + "_" + target;

            if (hasTarget && targetChain[i].IsGlobalNamespace)
                return source + "_" + targetChain[i].Name + target;

            return source;
        }
        return source + "_" + target;
    }

    internal string? BuildFile()
    {
        StringBuilder code = new(@"#nullable enable
namespace SourceCrafter.Mapifier;

public static partial class BindingExtensions
{");
        var len = code.Length;

        BuildMethods(code);

        if (code.Length == len)
            return null;

        var id = GetFileName();

        return code.Append(@"
}").ToString();
    }

    internal void BuildMethods(StringBuilder code)
    {
        if (CanMap is not true || _isScalar && (!TargetType.IsTupleType || !HasTargetToSourceMap) && (!SourceType.IsTupleType || !HasSourceToTargetMap) || _rendered) return;

        _rendered = true;

        if (HasSourceToTargetMap)
        {
            BuildTargetMethod?.Render(code, ref _targetRenderFlags);
        }

        if (!AreSameType && HasTargetToSourceMap)
        {
            BuildSourceMethod?.Render(code, ref _sourceRenderFlags);
        }

        ExtraMappings.Render(code);
    }

    public TypeMappingMeta(Dictionary<string, TypeMappingMeta> mappingSet, string id, TypeMeta target, TypeMeta source, GetOrAddMappingHadler getOrAdd)
    {
        var sameType = target.Id == source.Id;

        ToTargetMethodName = (sameType)
            ? "Copy"
            : $"To{target.SanitizedName}";
        ToSourceMethodName = sameType
            ? "Copy"
            : $"To{source.SanitizedName}";
        TryGetTargetMethodName = sameType
            ? "TryCopy"
            : "TryGet";
        TryGetSourceMethodName = sameType
            ? "TryCopy"
            : "TryGet";
        FillTargetMethodName = sameType
            ? "Update"
            : "Fill";
        FillSourceMethodName = sameType
            ? "Update"
            : "Fill";

        MappingId = id;
        AreSameType = target.Id == source.Id;
        _isScalar = target.IsPrimitive && source.IsPrimitive;
        CanDepth = !target.IsPrimitive && !source.IsPrimitive
            && !(target.IsDelegateOrPointer && source.IsDelegateOrPointer);
        IsTupleFromClass = target.IsTupleType && source is { IsPrimitive: false, IsIterable: false };
        IsReverseTupleFromClass = source.IsTupleType && target is { IsPrimitive: false, IsIterable: false };
        TargetType = target;
        SourceType = source;
        IsObjectMapping = source.IsObject || target.IsObject;

        if (IsCollection = SourceType.IsIterable && TargetType.IsIterable)
        {
            TargetCollectionMap = BuildCollectionMapping(SourceType.CollectionInfo, TargetType.CollectionInfo, ToTargetMethodName, FillTargetMethodName);
            SourceCollectionMap = BuildCollectionMapping(TargetType.CollectionInfo, SourceType.CollectionInfo, ToSourceMethodName, FillSourceMethodName);

            ItemMap = getOrAdd(TargetType.CollectionInfo.ItemDataType, SourceType.CollectionInfo.ItemDataType, default, default);

            ItemMap.TargetType.IsRecursive |= ItemMap.TargetType.IsRecursive;
            ItemMap.SourceType.IsRecursive |= ItemMap.SourceType.IsRecursive;

            if (ItemMap.CanMap is false || !(SourceType.CollectionInfo.ItemDataType.HasZeroArgsCtor && TargetType.CollectionInfo.ItemDataType.HasZeroArgsCtor))
            {
                CanMap = IsCollection = false;
            }
        }
    }

    private static CollectionMapping BuildCollectionMapping(CollectionInfo source, CollectionInfo target, string copyMethodName, string fillMethodName)
    {
        var isDictionary = source.IsDictionary && target.IsDictionary || CanMap(source, target) || CanMap(target, source);

        var iterator = !isDictionary && source.Indexable && target.BackingArray ? "for" : "foreach";

        return new(
            isDictionary,
            target.BackingArray,
            target.BackingArray && !source.Indexable,
            iterator,
            !source.Countable && target.BackingArray,
            target.Method,
            copyMethodName,
            fillMethodName);

        static bool CanMap(CollectionInfo source, CollectionInfo target) =>
            source.IsDictionary && target.ItemDataType is { IsTupleType: true, TupleElementCount: 2 };
    }
}

delegate TypeMeta TypeMetaGetOrAddHandler(ITypeSymbol type, bool isEnumerable = false);
delegate TypeMappingMeta GetOrAddMappingHadler(TypeMeta target, TypeMeta source, MappingKind mappingKind, IgnoreBind ignoreBind);

internal readonly record struct CollectionInfo
(
    TypeMeta ItemDataType,
    EnumerableType Type,
    bool IsItemNullable,
    bool Indexable,
    bool Countable,
    bool BackingArray,
    string? Method,
    string CountProp
)
{
    internal readonly bool IsDictionary = Type is EnumerableType.Dictionary;
};

internal record struct CollectionMapping(bool IsDictionary, bool CreateArray, bool UseLenInsteadOfIndex, string Iterator, bool Redim, string? Method, string MethodName, string FillMethodName);

internal readonly record struct KeyValueMappings(ValueRenderer Key, ValueRenderer Value);
