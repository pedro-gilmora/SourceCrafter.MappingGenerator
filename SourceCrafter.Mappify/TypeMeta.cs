using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;


using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.SqlTypes;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Permissions;
using System.Text;
using System.Xml.Linq;

namespace SourceCrafter.Mappify;

internal sealed class TypeMeta
{
    internal readonly int Id;
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
        IsKeyValueType,
        IsValueType,
        IsCollection,
        IsInterface,
        IsObject,
        HasZeroArgsCtor,
        IsMemberless = true,
        IsRefType,
        IsRecursive,
        HasRefTypeMembers;

    internal readonly CollectionMeta Collection;
    internal readonly string SanitizedName;
    internal readonly bool IsPrimitive;

    private readonly HashSet<CodeRenderer> _unsafeAccesors;

    // internal readonly bool AllowNull;

    internal TypeMeta(
        // ReSharper disable once RedundantAssignment
        ref TypeMeta @this,
        TypeSet types,
        int id,
        ITypeSymbol membersSource,
        ITypeSymbol? implementation = null)
    {
        @this = this;

        _unsafeAccesors = types.UnsafeAccessors;

        var type = (implementation ?? membersSource).ToNonNullable;
        Symbol = membersSource.ToNonNullable;
        Id = id;

        IsTupleType = Symbol.IsTupleType;
        IsValueType = Symbol.IsValueType;
        IsRefType = type is not { IsRefLikeType: false, IsReferenceType: false };
        IsInterface = Symbol.TypeKind is TypeKind.Interface;
        Name = Symbol.Name;
        ShortName = Symbol.ToNameOnly();
        FullName = Symbol.FullyQualifiedName;

        ExportNonFullGenericName = type.FullyQualifiedNonGeneric;
        FullNonGenericName = Symbol.FullyQualifiedNonGeneric;
        IsKeyValueType = Name is "KeyValuePair";
        IsObject = SymbolEqualityComparer.Default.Equals(Symbol, types.Compilation.ObjectType);
        IsPrimitive = type.IsPrimitive;

        HasZeroArgsCtor =
            (type is INamedTypeSymbol { InstanceConstructors: { Length: > 0 } ctors } //Has instance constructors
             && ctors.Any(ctor => ctor.Parameters.IsDefaultOrEmpty && ctor.IsAccessible(types.Compilation.SourceModule))) //With zero-args
             || type.Kind is SymbolKind.ErrorType;

        (SanitizedName, ExportFullName) = implementation is null
            ? (types.SanitizeName(Symbol), ExportFullName = Symbol.FullyQualifiedName)
            : (types.SanitizeName(type), ExportFullName = FullName);
        
        Members = Set<MemberMeta>.Create(m => m.HashCode);

        if (!(IsCollection = IsEnumerableType(types, FullNonGenericName, type, out Collection, out IsRecursive, ref HasRefTypeMembers)))
        {
            if (IsPrimitive) return;

            if (IsTupleType)
                GetTupleMembers(types, out IsMemberless, ref IsRecursive, ref HasRefTypeMembers);
            else if (IsKeyValueType)
            {
                IsMemberless = false;
                GetKeyValueMembers(types, ref IsRecursive, ref HasRefTypeMembers);
            }
            else
                GetObjectMembers(types, out IsMemberless, ref IsRecursive, ref HasRefTypeMembers); 
        }
    }

    private void GetTupleMembers(TypeSet types, out bool isMemberless, ref bool isRecursive, ref bool hasRefMembers)
    {
        var members = ((INamedTypeSymbol)Symbol.ToNonNullable).TupleElements;

        if (isMemberless = members.IsDefaultOrEmpty) return;

        foreach (var member in members)
        {
            TypeMeta type = types.GetOrAdd(member.Type);

            if (Id == type.Id) isRecursive = true;
            if (!hasRefMembers && member.Type is not { IsRefLikeType: false, IsReferenceType: false } || type.HasRefTypeMembers) hasRefMembers = true;

            Members.TryAdd(
                new(SymbolEqualityComparer.Default.GetHashCode(member),
                    member.ToNameOnly(),
                    type,
                    this,
                    member.IsNullable));
        }
    }

    private void GetKeyValueMembers(TypeSet types, ref bool isRecursive, ref bool hasRefMembers)
    {
        foreach (var member in Symbol.GetMembers())

            if (member is IPropertySymbol { Name: "Key" or "Value" } prop)
            {
                TypeMeta type = types.GetOrAdd(prop.Type);

                string name = member.ToNameOnly();

                if (!isRecursive && Id == type.Id) isRecursive = true;
                if (!hasRefMembers && prop.Type is not { IsRefLikeType: false, IsReferenceType: false } || type.HasRefTypeMembers) hasRefMembers = true;

                string propBackingFieldAccesor = $"Get{name}";
                AddFieldUnsafeAccessor(name, type.FullName, propBackingFieldAccesor, true, prop.IsNullable);
                
                if(prop.IsNullable) AddNullUnsafeAccessor(type);

                Members.TryAdd(
                    new(SymbolEqualityComparer.Default.GetHashCode(member),
                        name,
                        type,
                        this,
                        prop.IsNullable,
                        useUnsafeAccessor: true,
                        privateFieldMethodName: propBackingFieldAccesor,
                        isKey: prop.Name is "Key",
                        isValue: prop.Name is "Value",
                        canWrite: false));
            }
    }

    private void GetObjectMembers(TypeSet types, out bool isMemberless, ref bool isRecursive, ref bool hasRefMembers)
    {
        Dictionary<int, string> ids = [];
        HashSet<string> memberNames = new(StringComparer.OrdinalIgnoreCase);

        var isInterface = Symbol.TypeKind == TypeKind.Interface;

        getMembers(Symbol, ref isRecursive, ref hasRefMembers);

        isMemberless = Members.Count == 0;

        return;

        void getMembers(ITypeSymbol typeSymbol, ref bool isRecursive, ref bool hasRefMembers, bool isFirstLevel = true)
        {
            if (typeSymbol.ToNonNullable.IsPrimitive || typeSymbol.GetMembers() is not { IsDefaultOrEmpty: false } members)
            {
                hasRefMembers = false;
                return;
            }

            foreach (var member in members)
            {
                if (member is IFieldSymbol { AssociatedSymbol: IPropertySymbol autoProp })
                {
                    ids.Add(SymbolEqualityComparer.Default.GetHashCode(autoProp), autoProp.ToNameOnly());
                    continue;
                }

                string memberName = member.ToNameOnly();

                if (!memberNames.Add(memberName)
                    || member is not (IPropertySymbol or IFieldSymbol)
                    || member.DeclaredAccessibility is not (Accessibility.Internal or Accessibility.Public)
                    || IsExcludedByMetadata(types.Compilation, member.GetAttributes(), out var ignoreFor, out var manualMatches, out var maxDepth)) continue;

                string typeName, getPrivateFieldMethodName = "";
                bool isProperty = false, isNullable, useUnsafeAccessor;
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

                        type = types.GetOrAdd(memberType);

                        if (!isRecursive && (Id == type.Id || type.IsRecursive)) isRecursive = true;

                        typeName = type.FullName;

                        if (useUnsafeAccessor = ids.TryGetValue(id, out var fieldName) &&
                            (prop is not { IsReadOnly: false, IsIndexer: false, SetMethod.IsInitOnly: false }
                             || type is { IsValueType: true, IsMemberless: false }))
                        {
                            getPrivateFieldMethodName = $"Get{fieldName}";
                        }

                        var canRead = prop.GetMethod is not null;
                        var canWrite = prop.SetMethod is { IsInitOnly: false };

                        if (!canRead && !canWrite && !useUnsafeAccessor) continue;

                        isProperty = true;

                        if (!hasRefMembers && memberType is not { IsRefLikeType: false, IsReferenceType: false } || type.HasRefTypeMembers) hasRefMembers = true;

                        Members.TryAdd(
                            new(id,
                                memberName,
                                type,
                                this,
                                isNullable = prop.IsNullable,
                                manualMatches,
                                ignoreFor,
                                canRead,
                                canWrite,
                                useUnsafeAccessor,
                                maxDepth,
                                getPrivateFieldMethodName));

                        if (useUnsafeAccessor)
                        {
                            AddFieldUnsafeAccessor(memberName, typeName, getPrivateFieldMethodName, isProperty, isNullable);

                            if (memberType.IsValueType && memberType.IsNullable)
                            {
                                AddNullUnsafeAccessor(type);
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

                        if (useUnsafeAccessor = field.IsReadOnly)
                            getPrivateFieldMethodName = $"Get{memberName}";

                        type = types.GetOrAdd(memberType);

                        if (!isRecursive && (Id == type.Id || type.IsRecursive)) isRecursive = true;

                        typeName = type.FullName;

                        if (!hasRefMembers && memberType is not { IsRefLikeType: false, IsReferenceType: false } || type.HasRefTypeMembers) hasRefMembers = true;

                        Members.TryAdd(
                            new(SymbolEqualityComparer.Default.GetHashCode(member),
                                memberName,
                                type,
                                this,
                                isNullable = field.IsNullable,
                                manualMatches,
                                ignoreFor,
                                true,
                                !useUnsafeAccessor,
                                useUnsafeAccessor,
                                maxDepth,
                                getPrivateFieldMethodName));

                        if (useUnsafeAccessor)
                        {
                            AddFieldUnsafeAccessor(memberName, typeName, getPrivateFieldMethodName, isProperty, isNullable);

                            if (memberType is { IsValueType: true, IsNullable: true })
                            {
                                AddNullUnsafeAccessor(type);
                            }
                        }

                        continue;
                }

            }

            if (typeSymbol.BaseType != null)
                getMembers(typeSymbol.BaseType, ref isRecursive, ref hasRefMembers, false);

            if (!isFirstLevel) return;

            foreach (var iFace in typeSymbol.AllInterfaces)
                getMembers(iFace, ref isRecursive, ref hasRefMembers, false);
        }
    }

    private void AddNullUnsafeAccessor(TypeMeta type)
    {
        var targetOwnerXmlDocType = $"Nullable{{{type.FullName.Replace("<", "{").Replace(">", "}")}}}";

        _unsafeAccesors.Add(new("UnNull-" + type.FullName, code => code
            .Append(@"
    /// <summary>
    /// Gets a reference to the backing field of <see cref=""")
            .Append(targetOwnerXmlDocType).Append(@".Value""/> property
    /// </summary>
    /// <param name=""_"">Value holder for not null value of <see cref=""")
            .Append(targetOwnerXmlDocType).Append(@"""/></param>
    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = ""value"")]
    extern static ref ").Append(type.FullName)
            .Append(" UnNull(").Append(type.IsValueType ? "this " : "ref this ")
            .Append(type.FullName)
            .AppendLine("? _);")));
    }

    private void AddFieldUnsafeAccessor(string memberName, string typeName, string getPrivateFieldMethodName, bool isProperty, bool isNullable)
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

            if (IsValueType) code.Append("ref ");

            code.Append("this ").Append(FullName)
                .AppendLine(" _);");
        }));
    }
    

    private bool IsExcludedByMetadata(Compilation compilation, ImmutableArray<AttributeData> attributes, out HashSet<int> ignoreFor, out Dictionary<int, bool?> manualMatches, out short maxDepth)
    {
        maxDepth = 0;

        if (attributes.IsDefaultOrEmpty)
        {
            ignoreFor = null!;
            manualMatches = null!;
            return false;
        }

        ignoreFor = [];
        manualMatches = [];
        bool? defaultAllowNull = null;

        var result = false;

        foreach (var attr in attributes)
        {
            if (attr.AttributeClass?.FullyQualifiedName is not { } className) continue;

            switch (className)
            {
                case "global::SourceCrafter.Mappify.Attributes.AllowNullAttribute":
                    defaultAllowNull = true;
                    break;

                case "global::SourceCrafter.Mappify.Attributes.IgnoreAttribute":
                    result = true;
                    continue;

                case "global::SourceCrafter.Mappify.Attributes.IgnoreForAttribute":

                    if (TryGetSymbolIdFromNameOf(compilation, attr, 0, out var ignoredId))
                        ignoreFor.Add(ignoredId);

                    continue;

                case "global::SourceCrafter.Mappify.Attributes.MaxAttribute":

                    maxDepth = (short)attr.ConstructorArguments[0].Value!;

                    continue;
                case "global::SourceCrafter.Mappify.Attributes.MapAttribute":

                    if (TryGetSymbolIdFromNameOf(compilation, attr, 0, out var targetId))
                        manualMatches.Add(targetId, attr.ConstructorArguments is [_, { IsNull: false, Value: bool allowNull }] ? allowNull : defaultAllowNull);

                    break;

            }
        }

        foreach (var item in manualMatches.Keys)
        {
            manualMatches[item] ??= defaultAllowNull;
        }

        return result;
    }

    private bool TryGetSymbolIdFromNameOf(Compilation compilation, AttributeData attr, int index, out int memberId)
    {
        if ((attr.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax)?.ArgumentList?.Arguments.ElementAtOrDefault(index) is not
            {
                Expression: InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                    ArgumentList.Arguments: [{ Expression: MemberAccessExpressionSyntax { Name: { } ignoreId } }]
                }
            })
        {
            memberId = 0;
            return false;
        }

        memberId = SymbolEqualityComparer.Default
            .GetHashCode(
                compilation
                    .GetSemanticModel(ignoreId.SyntaxTree)
                    .GetSymbolInfo(ignoreId).Symbol);

        return true;
    }

    internal void AsCast(StringBuilder code, bool addNullable, string item)
    {
        code.Append('(').Append(FullName);
        if (addNullable) code.Append('?');
        code.Append(")").Append(item);
    }

    private bool IsEnumerableType(TypeSet types, string nonGenericFullName, ITypeSymbol type, out CollectionMeta info, out bool isRecursive, ref bool hasRefTypeMembers)
    {
        isRecursive = false;

        if (type.IsPrimitive)
        {
            info = default!;
            return false;
        }

        switch (nonGenericFullName)
        {
            case "global::System.Collections.Generic.Dictionary" or "global::System.Collections.Generic.IDictionary"
            :
                info = GetCollectionInfo(types, EnumerableType.Dictionary, GetEnumerableType(type, true));

                if (!hasRefTypeMembers && !info.ItemType.IsValueType) hasRefTypeMembers = true;

                if (!isRecursive && (Id == info.ItemType.Id || info.ItemType.IsRecursive)) isRecursive = true;

                return true;

            case "global::System.Collections.Generic.Stack"
            :
                info = GetCollectionInfo(types, EnumerableType.Stack, GetEnumerableType(type));

                if (!isRecursive && (Id == info.ItemType.Id || info.ItemType.IsRecursive)) isRecursive = true;

                return true;

            case "global::System.Collections.Generic.Queue"
            :
                info = GetCollectionInfo(types, EnumerableType.Queue, GetEnumerableType(type));

                if (!isRecursive && (Id == info.ItemType.Id || info.ItemType.IsRecursive)) isRecursive = true;

                return true;

            case "global::System.ReadOnlySpan"
            :

                info = GetCollectionInfo(types, EnumerableType.ReadOnlySpan, GetEnumerableType(type));

                if (!isRecursive && (Id == info.ItemType.Id || info.ItemType.IsRecursive)) isRecursive = true;

                return true;

            case "global::System.Span"
            :
                info = GetCollectionInfo(types, EnumerableType.Span, GetEnumerableType(type));

                return true;

            case "global::System.Collections.Generic.ICollection" or
                "global::System.Collections.Generic.IList" or
                "global::System.Collections.Generic.List"
            :
                info = GetCollectionInfo(types, EnumerableType.Collection, GetEnumerableType(type));

                if (!isRecursive && (Id == info.ItemType.Id || info.ItemType.IsRecursive)) isRecursive = true;

                return true;

            case "global::System.Collections.Generic.IReadOnlyList" or
                "global::System.Collections.Generic.ReadOnlyList" or
                "global::System.Collections.Generic.IReadOnlyCollection" or
                "global::System.Collections.Generic.ReadOnlyCollection"
            :
                info = GetCollectionInfo(types, EnumerableType.ReadOnlyCollection, GetEnumerableType(type));

                if (!isRecursive && (Id == info.ItemType.Id || info.ItemType.IsRecursive)) isRecursive = true;

                return true;

            case "global::System.Collections.Generic.IEnumerable"
            :
                info = GetCollectionInfo(types, EnumerableType.Enumerable, GetEnumerableType(type));

                if (!isRecursive && (Id == info.ItemType.Id || info.ItemType.IsRecursive)) isRecursive = true;

                return true;

            default:
                if (type is IArrayTypeSymbol { ElementType: { } elType })
                {
                    info = GetCollectionInfo(types, EnumerableType.Array, elType);

                    return true;
                }
                else
                    foreach (var item in type.AllInterfaces)
                        if (IsEnumerableType(types, item.FullyQualifiedNonGeneric, item, out info, out isRecursive, ref hasRefTypeMembers))
                            return true;
                break;
        }

        info = default!;

        return false;
    }
    internal void BuildEnumMethods(Action<string, string> addSource, ref int i)
    {
        if (Symbol.GetMembers() is { Length: 0 } members) return;

        string?
            collectionsComma = null,
            caseComma = null,
            values = null,
            descriptions = null,
            names = null,
            name = null,
            description = null,
            definedByName = null,
            definedByInt = "",
            tryGetValue = null,
            tryGetName = null,
            tryGetDesc = null;

        foreach (var m in members.OfType<IFieldSymbol>())
        {
            string fullMemberName = MemberFullName(m);

            values += collectionsComma + fullMemberName;

            string descriptionStr = GetEnumDescription(m);

            descriptions += collectionsComma + descriptionStr;

            names += collectionsComma + "nameof(" + fullMemberName + ")";

            name += caseComma + "            case " + fullMemberName + ": return nameof(" + fullMemberName + ");";

            description += caseComma + "            case " + fullMemberName + @": 
                        return " + descriptionStr + ";";

            string distinctIntCase = "        case " + Convert.ToString(m.ConstantValue!);

            if (!definedByInt.Contains(distinctIntCase)) definedByInt += caseComma + distinctIntCase + ":";

            definedByName += caseComma + "        case nameof(" + fullMemberName + "):";

            tryGetValue += caseComma + "        case nameof(" + fullMemberName + @"): 
                    result = " + fullMemberName + @"; 
                    return true;";

            tryGetName += caseComma + "        case " + fullMemberName + @": 
                    result = nameof(" + fullMemberName + @"); 
                    return true;";

            tryGetDesc += caseComma + "        case " + fullMemberName + @": 
                    result = " + descriptionStr + @"; 
                    return true;";

            collectionsComma ??= "," + (caseComma ??= @"
        ");
        }

        var code = new StringBuilder().AppendFormat(@"#nullable enable
namespace SourceCrafter.Mappify;

public static class Mappings{0}
{{
    
    private static {1}[] {2}Values => field ??= [
        {3}
    ];

    private static string[] {2}Descriptions => field ??= [
        {4}
    ];

    private static string[] {2}Names => field ??= [
        {5}
    ];

    extension({1} target)
    {{
        public string? Name
	    {{
            get
            {{
		        switch(target)
                {{
        {6}
                    default: return null; 
                }}
            }}
        }}

        public string? Description
        {{
            get
            {{
		        switch(target)
                {{
        {7}
                    default: return null; 
                }}
            }}
        }}

        public static global::System.ReadOnlySpan<string> Names => {2}Names;

        public static global::System.ReadOnlySpan<{1}> Values => {2}Values;

        public static global::System.ReadOnlySpan<string> Descriptions => {2}Descriptions;

        public static bool IsDefined(string value)
        {{
		    switch(value)
            {{
        {8}
                    return true; 
                default: 
                    return false; 
            }}
        }}

        public static bool IsDefined(int value)
        {{
            switch(value)
            {{
        {9}
                    return true;
                default: 
                    return false; 
            }}
        }}

        public static bool TryGetValue(string value, out {1} result)
        {{
            switch(value)
            {{
        {10}
                default: result = default; return false; 
            }}
        }}

        public bool TryGetName(out string result)
        {{
            switch(target)
            {{
        {11}
                default: result = default!; return false; 
            }}
        }}

        public bool TryGetDescription(out string result)
        {{
            switch(target)
            {{
        {12}
                default: result = default!; return false; 
            }}
        }}
    }}
}}",
                /* 0 */  ++i,
                /* 1 */  ExportFullName,
                /* 2 */  SanitizedName,
                /* 3 */  values,
                /* 4 */  descriptions,
                /* 5 */  names,
                /* 6 */  name,
                /* 7 */  description,
                /* 8 */  definedByName,
                /* 9 */  definedByInt,
                /* 10 */ tryGetValue,
                /* 11 */ tryGetName,
                /* 12 */ tryGetDesc);

        addSource($"{i++.ToString().PadLeft(3, '0')}_{SanitizedName}.enum.g.cs", code.ToString());

        string MemberFullName(IFieldSymbol m) => ExportFullName + "." + m.Name;

        static string GetEnumDescription(IFieldSymbol m) => $@"""{m
            .GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.FullyQualifiedName is "global::System.ComponentModel.DescriptionAttribute")
            ?.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? m.Name.Wordify()}""";
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

    private CollectionMeta GetCollectionInfo(TypeSet types, EnumerableType enumerableType, ITypeSymbol typeSymbol)
    {
        var itemDataType = types.GetOrAdd((typeSymbol = typeSymbol.ToNonNullable));

        return enumerableType switch
        {
#pragma warning disable format
            EnumerableType.Dictionary =>
                new(itemDataType,
                    enumerableType,
                    typeSymbol.IsNullable,
                    true,
                    false,
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

    public override string ToString() => FullName;
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