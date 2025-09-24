using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;



[assembly: InternalsVisibleTo("SourceCrafter.Bindings.UnitTests")]
namespace SourceCrafter
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

        static IEnumerable<(IParameterSymbol, AttributeArgumentSyntax?)> GetAttrParamsMap(
            ImmutableArray<IParameterSymbol> paramSymbols,
            SeparatedSyntaxList<AttributeArgumentSyntax> argsSyntax)
        {
            int i = 0;
            foreach (var param in paramSymbols)
            {
                if (argsSyntax.Count > i && argsSyntax[i] is { NameColon: null, NameEquals: null } argSyntax)
                {
                    yield return (param, argSyntax);
                }
                else
                {
                    yield return (param, argsSyntax.FirstOrDefault(arg => param.Name == arg.NameColon?.Name.Identifier.ValueText));
                }

                i++;
            }
        }

        private static bool GetStringExpressionOrValue(SemanticModel model, IParameterSymbol paramSymbol, AttributeArgumentSyntax? arg, out string value)
        {
            value = null!;

            if (arg is not null)
            {
                if (model.GetSymbolInfo(arg.Expression).Symbol is IFieldSymbol
                    {
                        IsConst: true,
                        Type.SpecialType: SpecialType.System_String,
                        ConstantValue: { } val
                    })
                {
                    value = val.ToString();
                    return true;
                }
                else if (arg.Expression is LiteralExpressionSyntax { Token.ValueText: { } valueText } e
                    && e.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    value = valueText;
                    return true;
                }
            }
            else if (paramSymbol.HasExplicitDefaultValue)
            {
                value = paramSymbol.ExplicitDefaultValue?.ToString()!;
                return value != null;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static string ToMetadataLongName(this ISymbol symbol)
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

        public static int FindIndex<T>(this ImmutableArray<T> source, Predicate<T> predicate)
        {
            int i = -1;

            foreach (var item in source)
                if (predicate(item))
                    return i;
                else
                    ++i;

            return -1;
        }

        internal static string ToMetadataLongName(this ISymbol symbol, Map<string, byte> uniqueName)
        {
            var existing = ToMetadataLongName(symbol);

            ref var count = ref uniqueName.GetValueRefOrAddDefault(existing, out var exists);

            if (exists) return existing + "_" + (++count);

            return existing;
        }

        internal static string Capitalize(this string str)
        {
            return (str is [{ } f, .. { } rest] ? char.ToUpper(f) + rest : str);
        }

        internal static string Camelize(this string str)
        {
            return (str is [{ } f, .. { } rest] ? char.ToLower(f) + rest : str);
        }

        internal static string? Pascalize(this string str)
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

        extension(ISymbol symbol)
        {
            internal string FullyQualifiedMetadata => symbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + '.' + symbol.MetadataName;

            internal string ToNameOnly() => symbol.ToDisplayString(_symbolNameOnly);
        }

        extension(ILocalSymbol symbol)
        {
            internal bool IsNullable => symbol.NullableAnnotation == NullableAnnotation.Annotated;
        }

        extension(IPropertySymbol symbol)
        {
            internal bool IsNullable => symbol.NullableAnnotation == NullableAnnotation.Annotated;
        }

        extension(IFieldSymbol symbol)
        {
            internal bool IsNullable => symbol.NullableAnnotation == NullableAnnotation.Annotated;
        }

        extension(IParameterSymbol symbol)
        {
            internal bool IsNullable => symbol.NullableAnnotation == NullableAnnotation.Annotated;
        }

        extension(ITypeSymbol type)
        {
            internal bool IsPrimitive => type.IsPrimitiveType();

            internal bool IsPrimitiveType(bool includeObject = true) =>
                (includeObject && type.SpecialType is SpecialType.System_Object)
                    || type.SpecialType is SpecialType.System_Enum
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
                    || type.Name is "DateTimeOffset" or "Guid"
                    || (type.SpecialType is SpecialType.System_Nullable_T
                        && IsPrimitiveType(((INamedTypeSymbol)type).TypeArguments[0]));


            internal ITypeSymbol ToNonNullable =>
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

            internal bool IsNullable
                => type.SpecialType is SpecialType.System_Nullable_T
                    || type.NullableAnnotation == NullableAnnotation.Annotated
                    || type is INamedTypeSymbol { Name: "Nullable" };

            internal bool AllowsNull
                => type is { IsValueType: false, IsTupleType: false, IsReferenceType: true };

            internal string FullyQualifiedName => type.ToDisplayString(_globalizedNamespace);

            internal string FullyQualifiedNonGeneric => type.ToDisplayString(_globalizedNonGenericNamespace);

            internal string TypeNameFormat => type.ToDisplayString(_typeNameFormat);

            internal bool IsRelatedTo(ITypeSymbol other)
            {
                return SymbolEqualityComparer.Default.Equals(type, other)
                    || HasBaseType(type, other)
                    || type.AllInterfaces.Any(type.HasBaseType);
            }

            internal bool HasBaseType(ITypeSymbol other)
            {
                return type?.BaseType is not null && (SymbolEqualityComparer.Default.Equals(type.BaseType, other) || HasBaseType(type.BaseType, other));
            }

        }

        internal static ImmutableArray<IParameterSymbol> GetParameters(this ITypeSymbol implType)
        {
            return implType is INamedTypeSymbol { Constructors: var ctor, InstanceConstructors: var insCtor }
                ? ctor.OrderBy(d => !d.Parameters.IsDefaultOrEmpty).FirstOrDefault()?.Parameters
                    ?? insCtor.OrderBy(d => !d.Parameters.IsDefaultOrEmpty).FirstOrDefault()?.Parameters
                    ?? []
                : [];
        }



        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static StringBuilder AddSpace(this StringBuilder sb, int count = 1) => sb.Append(new string(' ', count));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static StringBuilder CaptureGeneratedString(this StringBuilder code, Action action, out string expression)
        {
            int start = code.Length, end;
            action();
            end = code.Length;
            char[] e = new char[end - start];
            code.CopyTo(start, e, 0, end - start);
            expression = new(e, 0, e.Length);
            return code;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static string Wordify(this string identifier, short upper = 0)
            => ToJoined(identifier, " ", upper);


        static string ToJoined(string identifier, string separator = "-", short casing = 0)
        {
            var buffer = new char[identifier.Length * (separator.Length + 1)];
            var bufferIndex = 0;

            for (int i = 0; i < identifier.Length; i++)
            {
                char ch = identifier[i];
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

        internal static bool TryGetAsyncType(this ITypeSymbol typeSymbol, out ITypeSymbol factoryType)
        {
            switch ((factoryType = typeSymbol)?.FullyQualifiedNonGeneric)
            {
                case "global::System.Threading.Tasks.ValueTask" or "global::System.Threading.Tasks.Task"
                    when factoryType is INamedTypeSymbol { TypeArguments: [{ } firstTypeArg] }:

                    factoryType = firstTypeArg;
                    return true;

                default:

                    return false;
            }
            ;
        }

        internal static string RemoveDuplicates(this string? input)
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

        internal static bool TryGetFirst<T>(this IEnumerable<T> items, Func<T, bool> predicate, out T itemOut)
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
        public static bool IsAccessible(this ISymbol symbol, IModuleSymbol module) =>
            symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal
            || SymbolEqualityComparer.Default.Equals(symbol.ContainingModule, module);


        internal static T Exchange<T>(ref this T oldVal, T newVal) where T : struct =>
                    oldVal.Equals(newVal) ? oldVal : ((oldVal, _) = (newVal, oldVal)).Item2;

        // Write custom extension methods here. They will be available to all queries.
        public static void Compile(this string code, out CSharpCompilation compilation, out SyntaxNode root, out SemanticModel model, params Type[] assemblies)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(code);

            root = tree.GetRoot();

            compilation = CSharpCompilation
                .Create(
                    "Temp",
                    [tree],
                    [.. assemblies.Concat([typeof(object)])
                        .Select(a => a.Assembly.Location)
                        .Distinct()
                        .Where(l => l is not null)
                        .Select(l => MetadataReference.CreateFromFile(l))]
                );
            //SymbolInfo.objectTypeSymbol = compilation.GetTypeByMetadataName("System.Object")!;
            model = compilation.GetSemanticModel(tree);
        }

        internal static ReadOnlySpan<int> Primes => primes;
        private static readonly int[] primes =
        [
            3,
            7,
            11,
            17,
            23,
            29,
            37,
            47,
            59,
            71,
            89,
            107,
            131,
            163,
            197,
            239,
            293,
            353,
            431,
            521,
            631,
            761,
            919,
            1103,
            1327,
            1597,
            1931,
            2333,
            2801,
            3371,
            4049,
            4861,
            5839,
            7013,
            8419,
            10103,
            12143,
            14591,
            17519,
            21023,
            25229,
            30293,
            36353,
            43627,
            52361,
            62851,
            75431,
            90523,
            108631,
            130363,
            156437,
            187751,
            225307,
            270371,
            324449,
            389357,
            467237,
            560689,
            672827,
            807403,
            968897,
            1162687,
            1395263,
            1674319,
            2009191,
            2411033,
            2893249,
            3471899,
            4166287,
            4999559,
            5999471,
            7199369
        ];
    }
}

namespace SourceCrafter.DependencyInjection
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
}

#endif