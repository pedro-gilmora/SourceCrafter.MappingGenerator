using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SourceCrafter.DependencyInjection;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;



[assembly: InternalsVisibleTo("SourceCrafter.Bindings.UnitTests")]
namespace SourceCrafter.Mapifier.Helpers
{
    internal static class Helpers
    {
        internal static readonly int EmptyStringHashCode = "".GetHashCode();
        internal readonly static SymbolDisplayFormat
            _globalizedNamespace = new(
                memberOptions:
                    SymbolDisplayMemberOptions.IncludeType |
                    SymbolDisplayMemberOptions.IncludeModifiers |
                    SymbolDisplayMemberOptions.IncludeExplicitInterface |
                    SymbolDisplayMemberOptions.IncludeParameters |
                    SymbolDisplayMemberOptions.IncludeContainingType |
                    SymbolDisplayMemberOptions.IncludeConstantValue |
                    SymbolDisplayMemberOptions.IncludeRef,
                globalNamespaceStyle:
                    SymbolDisplayGlobalNamespaceStyle.Included,
                typeQualificationStyle:
                    SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions:
                    SymbolDisplayGenericsOptions.IncludeTypeParameters |
                    SymbolDisplayGenericsOptions.IncludeVariance,
                miscellaneousOptions:
                    SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                    SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier,
                parameterOptions:
                    SymbolDisplayParameterOptions.IncludeType |
                    SymbolDisplayParameterOptions.IncludeModifiers |
                    SymbolDisplayParameterOptions.IncludeName |
                    SymbolDisplayParameterOptions.IncludeDefaultValue),
            _globalizedNonGenericNamespace = new(
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes),
            _symbolNameOnly = new(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly),
            _typeNameFormat = new(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeVariance,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        extension(AttributeData t)
        {
            internal AttributeSyntax? Syntax => t.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
        }

        extension(IFieldSymbol t)
        {
            internal string GlobalNamespaced => 
                t.ToDisplayString(
                    _globalizedNamespace.RemoveMemberOptions(                        
                        SymbolDisplayMemberOptions.IncludeType |
                        SymbolDisplayMemberOptions.IncludeModifiers |
                        SymbolDisplayMemberOptions.IncludeExplicitInterface |
                        SymbolDisplayMemberOptions.IncludeParameters |
                        SymbolDisplayMemberOptions.IncludeConstantValue |
                        SymbolDisplayMemberOptions.IncludeRef));
        }
        extension(ISymbol t)
        {
            internal string GlobalNamespaced => t.ToDisplayString(_globalizedNamespace);

            internal string FullGlobalQualifiedName => t.ToDisplayString(_globalizedNamespace);

            internal string FullGlobalQualifiedNonGenericName => t.ToDisplayString(_globalizedNonGenericNamespace);

            internal string TypeNameFormat => t.ToDisplayString(_typeNameFormat);

            internal string NameOnly => t.ToDisplayString(_symbolNameOnly);
        }

        extension(ITypeSymbol type)
        {
            //internal readonly record struct DependencyInfo
            //{
            //    internal readonly bool IsAsync;
            //    internal readonly bool IsExternal;
            //    internal readonly bool IsCached { get; init; }
            //    internal readonly bool IsValid { get; init; } = false;
            //    internal readonly string Key { get; init; } = string.Empty;
            //    internal readonly int KeyHash { get; init; } = EmptyStringHashCode;

            //    internal readonly string? NameOrFormat;
            //    internal readonly ImmutableArray<IParameterSymbol> DefaultParamValues = [];
            //    internal readonly Disposability Disposability;
            //    internal readonly AttributeSyntax AttrSyntax = default!;
            //    internal readonly INamedTypeSymbol AttrClass = default!;
            //    internal readonly Lifetime Lifetime;
            //    internal readonly ITypeSymbol? InterfaceType;
            //    internal readonly ISymbol? Factory;
            //    internal readonly SymbolKind FactoryKind;

            //    internal readonly ITypeSymbol FinalType { get; init; } = default!;
            //    internal readonly ITypeSymbol Type { get; init; } = default!;

            //    internal DependencyInfo(SemanticModel model,
            //        AttributeData attrData,
            //        HashSet<string> externalAssemblies,
            //        string paramName,
            //        ITypeSymbol? fallbackType)
            //    {
            //        if (attrData is { AttributeClass: { } _attrClass, ApplicationSyntaxReference: { } attrSyntaxRef }
            //            && attrSyntaxRef.GetSyntax() is AttributeSyntax { } attrSyntax
            //            && model.GetSymbolInfo(attrSyntax).Symbol is IMethodSymbol { Parameters: var attrParams }
            //            && !_attrClass.Name.StartsWith("ServiceContainer")
            //            && GetLifetimeFromCtor(ref _attrClass, ref IsExternal, attrSyntax, out Lifetime))
            //        {
            //            AttrSyntax = attrSyntax;
            //            AttrClass = _attrClass;

            //            if (IsExternal = _attrClass.ContainingNamespace.ToDisplayString() != "SourceCrafter.DependencyInjection.Attributes")
            //                externalAssemblies.Add(_attrClass.ContainingAssembly.MetadataName.Replace(".Metadata", ""));

            //            if (attrData.AttributeClass!.TypeArguments.Length > 0 is { } isGeneric)
            //            {
            //                switch (_attrClass!.TypeArguments)
            //                {
            //                    case [{ } t1, { } t2, ..]:

            //                        InterfaceType = t1;
            //                        Type = t2;

            //                        break;

            //                    case [{ } t1]:

            //                        Type = t1;

            //                        break;
            //                }
            //            }

            //            foreach (var (param, arg) in GetAttributeParamsMap(attrParams, attrSyntax.ArgumentList?.Arguments ?? []))
            //            {
            //                switch (param.Name)
            //                {
            //                    case ImplParamName when !isGeneric && arg is { Expression: TypeOfExpressionSyntax { Type: { } type } }:

            //                        Type = (ITypeSymbol)model!.GetSymbolInfo(type).Symbol!;

            //                        continue;

            //                    case IfaceParamName when fallbackType?.TypeKind is not TypeKind.Interface && !isGeneric && arg is { Expression: TypeOfExpressionSyntax { Type: { } type } }:

            //                        InterfaceType = (ITypeSymbol)model!.GetSymbolInfo(type).Symbol!;

            //                        continue;

            //                    case KeyParamName when GetStringExpressionOrValue(model, param!, arg, out var keyValue):

            //                        KeyHash = (Key = keyValue).GetHashCode();

            //                        continue;

            //                    case NameFormatParamName when GetStringExpressionOrValue(model, param, arg, out var keyValue):

            //                        NameOrFormat = keyValue;

            //                        continue;

            //                    case SourceParamName

            //                        when arg?.Expression is InvocationExpressionSyntax
            //                        {
            //                            Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
            //                            ArgumentList.Arguments: [{ } methodRef]
            //                        }:

            //                        switch (model.GetSymbolInfo(methodRef.Expression))
            //                        {
            //                            case { Symbol: (IFieldSymbol or IPropertySymbol) and { IsStatic: true, Kind: { } kind } fieldOrProp }:

            //                                Factory = fieldOrProp;
            //                                FactoryKind = kind;

            //                                continue;

            //                            case { CandidateReason: CandidateReason.MemberGroup, CandidateSymbols: [IMethodSymbol { ReturnsVoid: false, IsStatic: true } method] }:

            //                                Factory = method;
            //                                FactoryKind = SymbolKind.Method;
            //                                DefaultParamValues = method.Parameters;

            //                                IsAsync = method.ReturnType.TryGetAsyncType(out var returnType);

            //                                if (returnType.TypeKind is TypeKind.Interface || returnType.IsAbstract) InterfaceType ??= returnType;

            //                                else Type ??= returnType;

            //                                continue;
            //                        }

            //                        continue;

            //                    case "disposability" when param.HasExplicitDefaultValue:

            //                        Disposability = (Disposability)(byte)param.ExplicitDefaultValue!;

            //                        continue;
            //                }
            //            }

            //            FinalType = FactoryKind switch
            //            {
            //                SymbolKind.Method => ((IMethodSymbol)Factory!).ReturnType,
            //                SymbolKind.Field => ((IFieldSymbol)Factory!).Type,
            //                SymbolKind.Property => ((IPropertySymbol)Factory!).Type,
            //                _ => InterfaceType ?? Type ?? fallbackType!
            //            };

            //            if (fallbackType is { })
            //            {
            //                if (fallbackType.TypeKind == TypeKind.Interface)
            //                    InterfaceType ??= fallbackType;
            //                else
            //                    Type ??= fallbackType;
            //            }

            //            Key ??= paramName ?? "";

            //            IsValid = FinalType is not null && Type is not null && AttrClass is not null && AttrSyntax is not null;

            //            IsCached = IsValid && Lifetime is not Lifetime.Transient;
            //        }
            //    }

            //    internal (string, string) GetMethodName(
            //        HashSet<string> methodsRegistry,
            //        DependencyNamesMap dependencyRegistry)
            //    {
            //        var methodName = NameOrFormat is not null
            //            ? string.Format(NameOrFormat, Key.Pascalize()!).RemoveDuplicates()
            //            : SanitizedTypeName(Type ?? FinalType, methodsRegistry, dependencyRegistry, Lifetime, Key.Pascalize()!);

            //        methodName = IsExternal ? methodName : Factory?.Name ?? methodName;

            //        if (Factory is not null &&IsCached && !methodName.EndsWith("Cached") && !methodName.EndsWith("Cache")) methodName += "Cached";
            //        if (!methodName.EndsWith("Async") && IsAsync) methodName += "Async";

            //        var fieldName = "_" + methodName.Camelize();

            //        if (!IsExternal && Factory is null) methodName = "Get" + methodName;

            //        return (fieldName, methodName);
            //    }
            //}

            //internal static bool TryGetDependencyInfo(
            //    this SemanticModel model,
            //    AttributeData attrData,
            //    HashSet<string> externalAssemblies,
            //    string paramName,
            //    ITypeSymbol? fallbackType,
            //    out DependencyInfo info) => (info = new(model, attrData, externalAssemblies, paramName, fallbackType)).IsValid;

            //bool IsRelatedTo(ITypeSymbol other)
            //{
            //    return SymbolEqualityComparer.Default.Equals(type, other)
            //        || type.HasBaseType(other)
            //        || type.AllInterfaces.Any(type.HasBaseType);
            //}

            bool HasBaseType(ITypeSymbol other)
            {
                return type is not null && type.BaseType is not null && (SymbolEqualityComparer.Default.Equals(type.BaseType, other) || HasBaseType(type.BaseType, other));
            }

            internal ITypeSymbol AsNonNullable() =>
                type.Name == "Nullable"
                    ? ((INamedTypeSymbol)type).TypeArguments[0]
                    : type.WithNullableAnnotation(NullableAnnotation.None);

            internal void TryGetNullable(out ITypeSymbol outType, out bool outIsNullable)
                        => (outType, outIsNullable) = type.SpecialType is SpecialType.System_Nullable_T
                            || type is INamedTypeSymbol { Name: "Nullable" }
                                ? (((INamedTypeSymbol)type).TypeArguments[0], true)
                                : type.NullableAnnotation == NullableAnnotation.Annotated
                                    ? (type.WithNullableAnnotation(NullableAnnotation.None), true)
                                    : (type, false);
        }

        //static IEnumerable<(IParameterSymbol, AttributeArgumentSyntax?)> GetAttributeParamsMap(
        //    ImmutableArray<IParameterSymbol> paramSymbols,
        //    SeparatedSyntaxList<AttributeArgumentSyntax> argsSyntax)
        //{
        //    int i = 0;
        //    foreach (var param in paramSymbols)
        //    {
        //        if (argsSyntax.Count > i && argsSyntax[i] is { NameColon: null, NameEquals: null } argSyntax)
        //        {
        //            yield return (param, argSyntax);
        //        }
        //        else
        //        {
        //            yield return (param, argsSyntax.FirstOrDefault(arg => param.Name == arg.NameColon?.Name.Identifier.ValueText));
        //        }

        //        i++;
        //    }
        //}

        //private static bool GetStringExpressionOrValue(SemanticModel model, IParameterSymbol paramSymbol, AttributeArgumentSyntax? arg, out string value)
        //{
        //    value = null!;

        //    if (arg is not null)
        //    {
        //        if (model.GetSymbolInfo(arg.Expression).Symbol is IFieldSymbol
        //            {
        //                IsConst: true,
        //                Type.SpecialType: SpecialType.System_String,
        //                ConstantValue: { } val
        //            })
        //        {
        //            value = val.ToString();
        //            return true;
        //        }
        //        else if (arg.Expression is LiteralExpressionSyntax { Token.ValueText: { } valueText } e
        //            && e.IsKind(SyntaxKind.StringLiteralExpression))
        //        {
        //            value = valueText;
        //            return true;
        //        }
        //    }
        //    else if (paramSymbol.HasExplicitDefaultValue)
        //    {
        //        value = paramSymbol.ExplicitDefaultValue?.ToString()!;
        //        return value is not null;
        //    }

        //    return false;
        //}

        extension(ISymbol symbol)
        {
            //internal static bool GetLifetimeFromCtor(ref INamedTypeSymbol attrClass, ref bool isExternal, AttributeSyntax attrSyntax, out Lifetime lifetime)
            //{
            //    if (GetLifetimeFromSyntax(attrSyntax, out lifetime)) return true;

            //    bool found;
            //    do
            //    {
            //        (isExternal, (found, lifetime)) = attrClass.ToGlobalNonGenericNamespace() switch
            //        {
            //            SingletonAttr => (isExternal, (true, Lifetime.Singleton)),
            //            ScopedAttr => (isExternal, (true, Lifetime.Scoped)),
            //            TransientAttr => (isExternal, (true, Lifetime.Transient)),
            //            { } val => (val is not DependencyAttr, GetFromCtorSymbol(attrClass))
            //        };

            //        if (found) return true;

            //        isExternal = true;
            //    }
            //    while ((attrClass = attrClass?.BaseType!) is not null);

            //    return false;

            //    static (bool, Lifetime) GetFromCtorSymbol(INamedTypeSymbol attrClass)
            //    {
            //        foreach (var ctor in attrClass.Constructors)
            //            foreach (var param in ctor.Parameters)
            //                if (param.Name.ToLower() is "lifetime" && param.HasExplicitDefaultValue)
            //                    return (true, (Lifetime)(byte)param.ExplicitDefaultValue!);

            //        return (false, default);
            //    }

            //    static bool GetLifetimeFromSyntax(AttributeSyntax attribute, out Lifetime lifetime)
            //    {
            //        foreach (var arg in attribute.ArgumentList?.Arguments ?? [])
            //        {
            //            if (arg is { NameColon.Name.Identifier.ValueText: "lifetime", Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: { } memberName } }
            //                && Enum.TryParse(memberName, out lifetime))
            //            {
            //                return true;
            //            }
            //        }

            //        lifetime = default;
            //        return false;
            //    }
            //}

            internal string MetadataLongName
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    var ret = new StringBuilder();

                    foreach (var part in symbol.ToDisplayParts(_typeNameFormat))
                    {
                        if (part.Symbol is { Name: string name })
                            ret.Append(name.Capitalize());
                        else
                            switch (part.ToString())
                            {
                                case ",": ret.Append("And"); break;
                                case "<": ret.Append("Of"); break;
                                case "[": ret.Append("Array"); break;
                            }
                    }

                    return ret.ToString();
                }
            }

            internal string ToMetadataLongName(Dictionary<string, byte> uniqueName)
            {
                var existing = symbol.MetadataLongName;

                var exists = false;

                if (!uniqueName.TryGetValue(existing, out var count))
                {
                    uniqueName[existing] = count = 1;
                }
                else
                {
                    exists = true;
                    uniqueName[existing] = count += 1;
                }

                return exists ? existing + "_" + count : existing;
            }
        }

        extension(string str)
        {
            internal string Capitalize()
            {
                return (str is [{ } f, .. { } rest] ? char.ToUpper(f) + rest : str);
            }

            internal string Camelize()
            {
                return (str is [{ } f, .. { } rest] ? char.ToLower(f) + rest : str);
            }

            internal string? Pascalize()
            {
                if (string.IsNullOrEmpty(str))
                    return str;

                ReadOnlySpan<char> span = str.AsSpan();
                Span<char> result = stackalloc char[span.Length];
                int resultIndex = 0;
                bool newWord = true;

                foreach (char c in span)
                {
                    if (char.IsWhiteSpace(c) || c == '-' || c == '_')
                    {
                        newWord = true;
                    }
                    else
                    {
                        if (newWord)
                        {
                            result[resultIndex++] = char.ToUpperInvariant(c);
                            newWord = false;
                        }
                        else
                        {
                            result[resultIndex++] = c;
                        }
                    }
                }

                return result[0..resultIndex].ToString();
            }
        }

        extension(ITypeSymbol target)
        {
            internal bool IsPrimitive(bool includeObject = true) =>
            (includeObject && target.SpecialType is SpecialType.System_Object)
                || target.SpecialType is SpecialType.System_Enum
                    or SpecialType.System_Boolean
                    or SpecialType.System_Byte
                    or SpecialType.System_SByte
                    or SpecialType.System_Char
                    or SpecialType.System_DateTime
                    or SpecialType.System_Decimal
                    or SpecialType.System_Double
                    or SpecialType.System_Int16
                    or SpecialType.System_Int32
                    or SpecialType.System_Int64
                    or SpecialType.System_Single
                    or SpecialType.System_UInt16
                    or SpecialType.System_UInt32
                    or SpecialType.System_UInt64
                    or SpecialType.System_String
                || target is IArrayTypeSymbol { ElementType: { Name: ['B' or 'b','y','t','e'], ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } }
                || target is { Name: "DateTimeOffset" or "Guid", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
                || (target.SpecialType is SpecialType.System_Nullable_T
                    && IsPrimitive(((INamedTypeSymbol)target).TypeArguments[0]));

            internal ImmutableArray<IParameterSymbol> Parameters => 
                target is INamedTypeSymbol { Constructors: var ctor, InstanceConstructors: var insCtor }
                    ? ctor.OrderBy(d => !d.Parameters.IsDefaultOrEmpty).FirstOrDefault()?.Parameters
                        ?? insCtor.OrderBy(d => !d.Parameters.IsDefaultOrEmpty).FirstOrDefault()?.Parameters
                        ?? []
                    : [];

            internal bool IsNullable => target.SpecialType is SpecialType.System_Nullable_T
                || target.NullableAnnotation == NullableAnnotation.Annotated
                || target is INamedTypeSymbol { Name: "Nullable" };

            internal bool AllowsNull => target is { IsValueType: false, IsTupleType: false, IsReferenceType: true };

            internal bool TryGetAsyncType(out ITypeSymbol factoryType)
            {
                switch (factoryType = target)
                {
                    case INamedTypeSymbol
                    {
                        ContainingType:
                        {
                            Name: "ValueTask" or "Task", ContainingNamespace:
                            {
                                Name: "Tasks",
                                ContainingNamespace:
                                {
                                    Name: "Threading",
                                    ContainingNamespace: { 
                                        Name: "System", 
                                        ContainingNamespace.IsGlobalNamespace: true 
                                    }
                                }
                            }
                        }, 
                        TypeArguments: [{ } firstTypeArg]
                    }:

                        factoryType = firstTypeArg;
                        return true;

                    default:

                        return false;
                }
                ;
            }
        }

        extension(StringBuilder sb)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal StringBuilder AddSpace(int count = 1) => sb.Append(new string(' ', count));
        }

        extension(StringBuilder code)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal StringBuilder CaptureGeneratedString(Action action, out string expression)
            {
                int start = code.Length, end;
                action();
                end = code.Length;
                char[] e = new char[end - start];
                code.CopyTo(start, e, 0, end - start);
                expression = new(e, 0, e.Length);
                return code;
            }
        }

        extension(string? input)
        {
            //internal static Disposability GetDisposability(this ITypeSymbol type)
            //{
            //    if (type is null) return Disposability.None;

            //    Disposability disposability = Disposability.None;

            //    foreach (var iFace in type.AllInterfaces)
            //    {
            //        switch (iFace.ToGlobalNonGenericNamespace())
            //        {
            //            case "global::System.IDisposable" when disposability is Disposability.None:
            //                disposability = Disposability.Disposable;
            //                break;
            //            case "global::System.IAsyncDisposable" when disposability < Disposability.AsyncDisposable:
            //                return Disposability.AsyncDisposable;
            //        }
            //    }

            //    return disposability;
            //}

            internal string RemoveDuplicates()
            {
                if ((input = input?.Trim()) is null or "")
                    return "";

                var result = "";
                var wordStart = 0;

                for (int i = 1; i < input.Length; i++)
                {
                    if (char.IsUpper(input[i]))
                    {
                        string word = input[wordStart..i];

                        if (!result.EndsWith(word))
                        {
                            result += word;
                        }

                        wordStart = i;
                    }
                }

                string lastWord = input[wordStart..];

                if (!result.EndsWith(lastWord, StringComparison.OrdinalIgnoreCase))
                {
                    result += lastWord;
                }

                return result;
            }
        }

        extension<T>(IEnumerable<T> items)
        {
            //internal static string SanitizedTypeName(
            //    ITypeSymbol type,
            //    HashSet<string> methodsRegistry,
            //    DependencyNamesMap dependencyRegistry,
            //    Lifetime lifeTime,
            //    string key)
            //{
            //    int hashCode = SymbolEqualityComparer.Default.GetHashCode(type);

            //    string id = Sanitize(type).Replace(" ", "").Capitalize();

            //    ref var idOut = ref dependencyRegistry.GetValueOrAddDefault((lifeTime, hashCode, key), out var exists);

            //    if (exists)
            //    {
            //        return idOut!;
            //    }

            //    if (key is "")
            //    {
            //        if (!methodsRegistry.Add(idOut = id)) methodsRegistry.Add(idOut = $"{lifeTime}{id}");
            //    }
            //    else if (!(methodsRegistry.Add(idOut = key)
            //        || methodsRegistry.Add(idOut = $"{key}{id}")
            //        || methodsRegistry.Add(idOut = $"{lifeTime}{key}")))
            //    {
            //        methodsRegistry.Add(idOut = $"{lifeTime}{key}{id}");
            //    }

            //    return idOut;

            //    static string Sanitize(ITypeSymbol type)
            //    {
            //        switch (type)
            //        {
            //            case INamedTypeSymbol { IsTupleType: true, TupleElements: { Length: > 0 } els }:

            //                return "TupleOf" + string.Join("", els.Select(f => Sanitize(f.Type)));

            //            case INamedTypeSymbol { IsGenericType: true, TypeParameters: { } args }:

            //                return type.Name + "Of" + string.Join("", args.Select(Sanitize));

            //            default:

            //                string typeName = type.ToTypeNameFormat();

            //                if (type is IArrayTypeSymbol { ElementType: { } elType })
            //                    typeName = Sanitize(elType) + "Array";

            //                return char.ToUpperInvariant(typeName[0]) + typeName[1..].TrimEnd('?', '_');
            //        }
            //        ;
            //    }
            //}

            internal bool TryGetFirst(Func<T, bool> predicate, out T itemOut)
            {
                foreach (T item in items)
                {
                    if (predicate(item))
                    {
                        itemOut = item;
                        return true;
                    }
                }
                itemOut = default!;
                return false;
            }
        }

        extension<T>(ref T oldVal) where T : struct
        {
            internal T Exchange(T newVal) =>
                    oldVal.Equals(newVal) ? oldVal : ((oldVal, _) = (newVal, oldVal)).Item2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static string Wordify(this string identifier, short upper = 0)
            => ToJoined(identifier, " ", upper);

        private static string ToJoined(string identifier, string separator = "-", short casing = 0)
        {
            var buffer = new char[identifier.Length * (separator.Length + 1)];
            var bufferIndex = 0;

            for (var i = 0; i < identifier.Length; i++)
            {
                var ch = identifier[i];
                bool isLetterOrDigit = char.IsLetterOrDigit(ch), isUpper = char.IsUpper(ch);

                if (i > 0 && isUpper && char.IsLower(identifier[i - 1]))
                {
                    separator.CopyTo(0, buffer, bufferIndex, separator.Length);
                    bufferIndex += separator.Length;
                }

                if (isLetterOrDigit)
                {
                    buffer[bufferIndex++] = (casing, isUpper) switch
                    {
                        (1, false) => char.ToUpperInvariant(ch),
                        (-1, true) => char.ToLowerInvariant(ch),
                        _ => ch
                    };
                }
            }

            return new string(buffer, 0, bufferIndex);
        }
    }
}

namespace SourceCrafter.Mapifier.Helpers
{
    internal static class CollectionExtensions<T>
    {
        internal static Collection<T> EmptyCollection => [];
        internal static ReadOnlyCollection<T> EmptyReadOnlyCollection => new([]);
    }
}

#if NETSTANDARD2_0 || NETSTANDARD2_1 || NETCOREAPP2_0 || NETCOREAPP2_1 || NETCOREAPP2_2 || NETCOREAPP3_0 || NETCOREAPP3_1 || NET45 || NET451 || NET452 || NET6 || NET461 || NET462 || NET47 || NET471 || NET472 || NET48


// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Reserved to be used by the compiler for tracking metadata.
    /// This class should not be used by developers in source code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }

    internal enum UnsafeAccessorKind
    {
        Constructor,
        Method,
        StaticMethod,
        Field,
        StaticField
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    internal sealed class UnsafeAccessorAttribute(UnsafeAccessorKind kind) : Attribute
    {
        public UnsafeAccessorKind Kind { get; } = kind;

        public string? Name { get; set; }
    }
}
#endif
