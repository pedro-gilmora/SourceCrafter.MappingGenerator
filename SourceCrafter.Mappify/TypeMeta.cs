using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SourceCrafter.Helpers;
using SourceCrafter.Mappify.Helpers;

namespace SourceCrafter.Mappify;

internal sealed class TypeMeta
{
    internal readonly int Id;
    internal readonly TypeSet Types;
    internal readonly ITypeSymbol Symbol;
    internal readonly Set<MemberMeta> Members;

    internal readonly string
        Name,
        ShortName,
        FullName,
        FullNonGenericName,
        ExportFullName,
        ExportNonFullGenericName;

    internal readonly bool
        IsTupleType,
        DictionaryOwned,
        IsKeyValueType,
        IsValueType,
        IsCollection,
        IsInterface,
        IsObject,
        HasZeroArgsCtor,
        IsMemberless = true;

    internal bool IsRecursive;
    internal CollectionMeta Collection = default;
    internal readonly string SanitizedName;
    internal readonly bool IsPrimitive;

    private readonly HashSet<CodeRenderer> _unsafeAccesors;

    // internal readonly bool AllowNull;

    internal TypeMeta(
        TypeSet types,
        // ReSharper disable once RedundantAssignment
        ref TypeMeta @this,
        int typeAId,
        ITypeSymbol membersSource,
        ITypeSymbol? implementation = null,
        bool isDictionaryOwned = false)
    {
        @this = this;

        _unsafeAccesors = types.UnsafeAccessors;

        var type = (implementation ?? membersSource).AsNonNullable();
        Symbol = membersSource;
        Types = types;
        Id = typeAId;

        IsTupleType = Symbol.IsTupleType;
        IsValueType = Symbol.IsValueType;
        IsInterface = Symbol.TypeKind is TypeKind.Interface;
        DictionaryOwned = isDictionaryOwned;
        Name = Symbol.Name;
        ShortName = Symbol.ToNameOnly();
        FullName = Symbol.ToGlobalNamespaced();

        ExportNonFullGenericName = type.ToGlobalNonGenericNamespace();
        FullNonGenericName = Symbol.ToGlobalNonGenericNamespace();
        IsKeyValueType = Name is "KeyValuePair";
        IsObject = SymbolEqualityComparer.Default.Equals(Symbol, types.Compilation.ObjectType);
        IsPrimitive = type.IsPrimitive();

        HasZeroArgsCtor =
            (type is INamedTypeSymbol { InstanceConstructors: { Length: > 0 } ctors } //Has instance constructors
             && ctors.Any(ctor => ctor.Parameters.IsDefaultOrEmpty && ctor.IsAccessible(Types.Compilation.SourceModule))) //With zero-args

            || implementation?.Kind is SymbolKind.ErrorType;

        (SanitizedName, ExportFullName) = membersSource.AsNonNullable() is { } memberSource
            ? (types.SanitizeName(memberSource), ExportFullName = memberSource.AsNonNullable().ToGlobalNamespaced().TrimEnd('?'))
            : (types.SanitizeName(type), ExportFullName = FullName);

        IsCollection = IsEnumerableType(FullNonGenericName, type, out Collection);

        Members = Set<MemberMeta>.Create(m => m.HashCode);

        if (IsPrimitive) return;

        if (IsTupleType) GetTupleMembers(out IsMemberless);
        else GetObjectMembers(out IsMemberless);
    }

    internal bool HasConversion(
        TypeMeta source,
        out ScalarConversion scalarConversion,
        out ScalarConversion reverseScalarConversion) 
        => HasConversion(source, this, out scalarConversion)
           | HasConversion(this, source, out reverseScalarConversion);

    private bool HasConversion(TypeMeta source, TypeMeta target, out ScalarConversion info)
    {
        if ((source, target) is not (
            ({ IsTupleType: false }, { IsTupleType: false }) and
            ({ DictionaryOwned: false, IsKeyValueType: false }, { DictionaryOwned: false, IsKeyValueType: false })))
        {
            info = default;
            return false;
        }

        ITypeSymbol
            targetTypeSymbol = target.Symbol,
            sourceTypeSymbol = source.Symbol;

        var conversion = Types.Compilation.ClassifyConversion(sourceTypeSymbol, targetTypeSymbol);

        info = new(conversion.Exists, conversion.IsExplicit, false);

        if (!info.Exists)
        {
            info.Exists = info.IsExplicit = sourceTypeSymbol
                .GetMembers()
                .Any(m =>
                    m is IMethodSymbol
                    {
                        MethodKind: MethodKind.Conversion,
                        Parameters: [{ Type: { } firstParam }],
                        ReturnType: { } returnType
                    }
                    && SymbolEqualityComparer.Default.Equals(returnType, sourceTypeSymbol)
                    && SymbolEqualityComparer.Default.Equals(firstParam, targetTypeSymbol));
        }
        else
        {
            info.InheritsFromTarget = target.IsInterface && sourceTypeSymbol.AllInterfaces.Any(targetTypeSymbol.Equals);
        }

        info.Exists |= info.IsExplicit |= source.IsObject && !target.IsObject;

        return info.Exists;
    }

    private void GetObjectMembers(out bool isMemberless)
    {
        Dictionary<int, string> ids = [];
        HashSet<string> memberNames = new(StringComparer.OrdinalIgnoreCase);

        var isInterface = Symbol.TypeKind == TypeKind.Interface;

        getMembers(Symbol);

        isMemberless = Members.Count == 0;

        return;

        void getMembers(ITypeSymbol typeSymbol, bool isFirstLevel = true)
        {
            if (typeSymbol.AsNonNullable().IsPrimitive())
                return;

            var members = typeSymbol.GetMembers();

            if (members.IsDefaultOrEmpty) return;

            foreach (var member in members)
            {
                if (member is IFieldSymbol { AssociatedSymbol: IPropertySymbol autoProp})
                {
                    ids.Add(SymbolEqualityComparer.Default.GetHashCode(autoProp), autoProp.ToNameOnly());
                    continue;
                }

                string memberName = member.ToNameOnly();

                if (!memberNames.Add(memberName)
                    || member is not (IPropertySymbol or IFieldSymbol)
                    || member.DeclaredAccessibility is not (Accessibility.Internal or Accessibility.Public)
                    || IsExcludedByMetadata(member.GetAttributes(), out var ignoreFor, out var manualMatches, out var maxDepth)) continue;
                
                string typeName, getPrivateFieldMethodName = "";
                bool isProperty = false, isNullable, useUnsafeAccessor = false;
                TypeMeta type;

                switch (member)
                {
                    case IPropertySymbol
                    {
                        ContainingType.Name: not "IEnumerator",
                        IsIndexer: false,
                        IsImplicitlyDeclared: var impl,
                        Type: { } memberType,
                        IsStatic: false,
                    } prop when isInterface || !impl:
                        
                        var id = SymbolEqualityComparer.Default.GetHashCode(member);
                        
                        type = Types.GetOrAdd(memberType);

                        typeName = type.FullName;

                        if(useUnsafeAccessor = ids.TryGetValue(id, out var fieldName) && 
                            (prop is not { IsReadOnly: false, IsIndexer : false, SetMethod.IsInitOnly: false }
                             || type is { IsValueType: true, IsMemberless: false }))
                        {
                            getPrivateFieldMethodName = $"Get{SanitizedName}{fieldName}";
                        }

                        var canRead = prop.GetMethod is not null;
                        var canWrite = prop.SetMethod is { IsInitOnly: false } ;
                        
                        if(!canRead && !canWrite && !useUnsafeAccessor) continue;

                        isProperty = true;
                        
                        Members.TryAdd(
                            new(id,
                                memberName,
                                type,
                                this, 
                                isNullable = prop.IsNullable(),
                                manualMatches,
                                ignoreFor,
                                canRead,
                                canWrite, 
                                useUnsafeAccessor, 
                                maxDepth,
                                getPrivateFieldMethodName));
                
                        if (useUnsafeAccessor)
                        {
                            addFieldUnsafeAccessor();

                            if (memberType.IsValueType && memberType.IsNullable())
                            {
                                addNullUnsafeAccessor(type);
                            }
                        }

                        continue;

                    case IFieldSymbol
                    {
                        ContainingType.Name: not "IEnumerator",
                        Type: { } memberType,
                        IsStatic: false,
                        IsImplicitlyDeclared: false,
                    } field:
                        
                        if(useUnsafeAccessor = field.IsReadOnly)
                            getPrivateFieldMethodName = $"Get{SanitizedName}{memberName}";

                        type = Types.GetOrAdd(memberType);
                        typeName = type.FullName;
                        
                        Members.TryAdd(
                            new(SymbolEqualityComparer.Default.GetHashCode(member),
                                memberName,
                                type,
                                this, 
                                isNullable = field.IsNullable(),
                                manualMatches,
                                ignoreFor,
                                true,
                                !useUnsafeAccessor, 
                                useUnsafeAccessor,
                                maxDepth,
                                getPrivateFieldMethodName));
                
                        if (useUnsafeAccessor)
                        {
                            addFieldUnsafeAccessor();

                            if (memberType.IsValueType && memberType.IsNullable())
                            {
                                addNullUnsafeAccessor(type);
                            }
                        }

                        continue;

                    default:
                        continue;
                }

                void addFieldUnsafeAccessor()
                {
                    var targetOwnerXmlDocType = FullName.Replace("<", "{").Replace(">", "}");

                    _unsafeAccesors.Add(new(FullName + "." + memberName, code =>
                    {
                        code.Append(@"
    /// <summary>
    /// Gets a reference to ");

                        if (isProperty)
                        {
                            code.Append(@"the backing field of <see cref=""")
                                .Append(targetOwnerXmlDocType)
                                .Append('.')
                                .Append(memberName)
                                .Append(@"""/> property");
                        }
                        else
                        {
                            code.Append(@"the field <see cref=""")
                                .Append(targetOwnerXmlDocType)
                                .Append('.')
                                .Append(memberName)
                                .Append(@"""/>");
                        }

                        code.Append(@"
    /// </summary>
    /// <param name=""_""><see cref=""")
                            .Append(targetOwnerXmlDocType)
                            .Append(@"""/> container reference of ")
                            .Append(memberName).Append(" ").Append(isProperty ? "property" : "field")
                            .Append(@"</param>
    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = """);

                        if (isProperty)
                            code.Append("<").Append(memberName).Append(">k__BackingField");
                        else
                            code.Append(memberName);
                        code.Append(@""")]
    extern static ref ").Append(typeName);

                        if (isNullable) code.Append("?");

                        code.Append(" ")
                            .Append(getPrivateFieldMethodName)
                            .Append("(");
                        
                        if(IsValueType) code.Append("ref ");

                        code.Append(FullName)
                            .AppendLine(" _);");
                    }));
                }

                void addNullUnsafeAccessor(TypeMeta type)
                {
                    var targetOwnerXmlDocType = $"Nullable{{{type.FullName.Replace("<", "{").Replace(">", "}")}}}";

                    _unsafeAccesors.Add(new("UnNull-"+type.FullName, code => code
                        .Append(@"
    /// <summary>
    /// Gets a reference to the backing field of <see cref=""")
                        .Append(targetOwnerXmlDocType).Append(@".Value""/> property
    /// </summary>
    /// <param name=""_"">Value holder for not null value of <see cref=""")
                        .Append(targetOwnerXmlDocType).Append(@"""/></param>
    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = ""value"")]
    extern static ref ").Append(type.FullName)
                        .Append(" UnNull(ref ")
                        .Append(type.FullName)
                        .AppendLine("? _);")));
                }

            }

            if (typeSymbol.BaseType != null)
                getMembers(typeSymbol.BaseType);

            if (!isFirstLevel) return;

            foreach (var iFace in typeSymbol.AllInterfaces)
                getMembers(iFace, false);
        }
    }

    private bool IsExcludedByMetadata(ImmutableArray<AttributeData> attributes, out HashSet<int> ignoreFor, out HashSet<int> manualMatch, out short maxDepth)
    {
        maxDepth = 0;

        if (attributes.IsDefaultOrEmpty)
        {
            ignoreFor = null!;
            manualMatch = null!;  
            return false;
        }

        ignoreFor = [];
        manualMatch = [];

        foreach (var attr in attributes)
        {
            if (attr.AttributeClass?.ToGlobalNamespaced() is not { } className) continue;
                        
            switch (className)
            {
                case "global::SourceCrafter.Mappify.Attributes.IgnoreAttribute":
                        
                    return true;
                        
                case "global::SourceCrafter.Mappify.Attributes.IgnoreForAttribute":
                        
                    if (IsNameOfMember(attr, out var ignoredId)) ignoreFor.Add(ignoredId);
                        
                    continue;
                        
                case "global::SourceCrafter.Mappify.Attributes.MaxAttribute":
                        
                    maxDepth = (short)attr.ConstructorArguments[0].Value!;
                        
                    continue;
            }
                        
            if (className != "global::SourceCrafter.Mappify.Attributes.MapAttribute")
                continue;
                        
            if (IsNameOfMember(attr, out var targetId)) manualMatch.Add(targetId);
        }
        
        return false;
    }

    private bool IsNameOfMember(AttributeData attr, out int memberId)
    {
        if ((attr.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax)?.ArgumentList?.Arguments[0]
            .Expression is not
            InvocationExpressionSyntax {
                Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                ArgumentList.Arguments: [{ Expression: MemberAccessExpressionSyntax { Name: { } ignoreId } }]
            })
        {
            memberId = 0;
            return false;
        }

        memberId = SymbolEqualityComparer.Default
            .GetHashCode(
                Types.Compilation
                    .GetSemanticModel(ignoreId.SyntaxTree)
                    .GetSymbolInfo(ignoreId).Symbol);

        return true;
    }
    internal void AsCast(StringBuilder code, bool addNullable, string item)
    {
        code.Append('(').Append(FullName);
        if(addNullable) code.Append('?');
        code.Append(")").Append(item);
    }
    private void GetTupleMembers(out bool isMemberless)
    {   
        var members = ((INamedTypeSymbol)Symbol.AsNonNullable()).TupleElements;

        if (isMemberless = members.IsDefaultOrEmpty) return;

        foreach (var member in members)

            Members.TryAdd(
                new(SymbolEqualityComparer.Default.GetHashCode(member),
                    member.ToNameOnly(),
                    Types.GetOrAdd(member.Type),
                    this,
                    member.IsNullable(),
                    [], 
                    []));
    }

    private bool IsEnumerableType(string nonGenericFullName, ITypeSymbol type, out CollectionMeta info)
    {
        if (type.IsPrimitive())
        {
            info = default!;
            return false;
        }

        switch (nonGenericFullName)
        {
            case "global::System.Collections.Generic.Dictionary" or "global::System.Collections.Generic.IDictionary"
            :
                info = GetCollectionInfo(EnumerableType.Dictionary, GetEnumerableType(type, true));

                return true;

            case "global::System.Collections.Generic.Stack"
            :
                info = GetCollectionInfo(EnumerableType.Stack, GetEnumerableType(type));

                return true;

            case "global::System.Collections.Generic.Queue"
            :
                info = GetCollectionInfo(EnumerableType.Queue, GetEnumerableType(type));

                return true;

            case "global::System.ReadOnlySpan"
            :

                info = GetCollectionInfo(EnumerableType.ReadOnlySpan, GetEnumerableType(type));

                return true;

            case "global::System.Span"
            :
                info = GetCollectionInfo(EnumerableType.Span, GetEnumerableType(type));

                return true;

            case "global::System.Collections.Generic.ICollection" or
                "global::System.Collections.Generic.IList" or
                "global::System.Collections.Generic.List"
            :
                info = GetCollectionInfo(EnumerableType.Collection, GetEnumerableType(type));

                return true;

            case "global::System.Collections.Generic.IReadOnlyList" or
                "global::System.Collections.Generic.ReadOnlyList" or
                "global::System.Collections.Generic.IReadOnlyCollection" or
                "global::System.Collections.Generic.ReadOnlyCollection"
            :
                info = GetCollectionInfo(EnumerableType.ReadOnlyCollection, GetEnumerableType(type));

                return true;

            case "global::System.Collections.Generic.IEnumerable"
            :
                info = GetCollectionInfo(EnumerableType.Enumerable, GetEnumerableType(type));

                return true;

            default:
                if (type is IArrayTypeSymbol { ElementType: { } elType })
                {
                    info = GetCollectionInfo(EnumerableType.Array, elType);

                    return true;
                }
                else
                    foreach (var item in type.AllInterfaces)
                        if (IsEnumerableType(item.ToGlobalNonGenericNamespace(), item, out info))
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

    private CollectionMeta GetCollectionInfo(EnumerableType enumerableType, ITypeSymbol typeSymbol)
    {
        var itemDataType = Types.GetOrAdd((typeSymbol = typeSymbol.AsNonNullable()), enumerableType == EnumerableType.Dictionary);

        return enumerableType switch
        {
#pragma warning disable format
            EnumerableType.Dictionary =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    true,
                    false,
                    "Add",
                    "Count"),
            EnumerableType.Queue =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    false,
                    true,
                    false,
                    "Enqueue",
                    "Count"),
            EnumerableType.Stack =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    false,
                    true,
                    false,
                    "Push",
                    "Count"),
            EnumerableType.Enumerable =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    false,
                    false,
                    true,
                    null,
                    "Length"),
            EnumerableType.ReadOnlyCollection =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    true,
                    false,
                    "Add",
                    "Count"),
            EnumerableType.ReadOnlySpan =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    true,
                    true,
                    null,
                    "Length"),
            EnumerableType.Collection =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    true,
                    false,
                    "Add",
                    "Count"),
            EnumerableType.Span =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    true,
                    true,
                    null,
                    "Length"),
            _ =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable(),
                    true,
                    true,
                    true,
                    null,
                    "Length")
#pragma warning restore format
        };
    }

    public override string ToString()
    {
        return FullName;
    }

    public void BuildEnumExtensions()
    {
    }

}

internal class CodeEqualityComparer : IEqualityComparer<CodeRenderer>
{
    internal static readonly CodeEqualityComparer Default = new();
    public bool Equals(CodeRenderer x, CodeRenderer y)
    {
        return x.Equals(y);
    }

    public int GetHashCode(CodeRenderer obj)
    {
        return obj.GetHashCode();
    }
}

internal class CodeRenderer(string key, Action<StringBuilder> renderer)
{
    private readonly string key = key;
    internal bool Rendered { get; set; }

    internal void Render(StringBuilder code)
    {
        if (Rendered) return;

        Rendered = true;

        renderer(code);
    }
    
    public bool Equals(CodeRenderer y) => key == y.key;
    
    public override int GetHashCode() => key.GetHashCode();
}

public record struct ScalarConversion(bool Exists, bool IsExplicit, bool InheritsFromTarget);