using Microsoft.CodeAnalysis;

using SourceCrafter.Mapifier.Constants;

using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace SourceCrafter.Mapifier;

/// <summary>
/// A value-equatable wrapper around <see cref="ImmutableArray{T}"/>.
/// <para>
/// <see cref="ImmutableArray{T}"/> compares by reference of the underlying array, so a pipeline
/// step that returns one can never hit its cache. Wrapping it restores structural equality and
/// lets the incremental generator cut off before the expensive step runs.
/// </para>
/// </summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> values) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _values = values.IsDefault ? ImmutableArray<T>.Empty : values;

    internal ImmutableArray<T> Values => _values.IsDefault ? ImmutableArray<T>.Empty : _values;

    internal int Length => Values.Length;

    public bool Equals(EquatableArray<T> other)
    {
        var left = Values;
        var right = other.Values;

        if (left.Length != right.Length) return false;

        for (var i = 0; i < left.Length; i++)
            if (!left[i].Equals(right[i]))
                return false;

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;

        foreach (var value in Values)
            unchecked { hash = hash * 31 + (value?.GetHashCode() ?? 0); }

        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator EquatableArray<T>(ImmutableArray<T> values) => new(values);
}

/// <summary>
/// A symbol referenced by its documentation-comment id.
/// <para>
/// Symbols must never cross a pipeline boundary: they compare by reference and keep the whole
/// <see cref="Compilation"/> that produced them alive. The documentation id is a plain string that
/// round-trips through <see cref="DocumentationCommentId"/>, so it is both value-equatable and able
/// to name constructed generics, nested and array types that
/// <see cref="Compilation.GetTypeByMetadataName(string)"/> cannot resolve.
/// </para>
/// </summary>
internal readonly record struct TypeRef(string DocumentationId)
{
    internal static TypeRef From(ITypeSymbol? symbol) =>
        new(symbol is null ? "" : DocumentationCommentId.CreateReferenceId(symbol));

    internal readonly bool IsEmpty => string.IsNullOrEmpty(DocumentationId);

    internal readonly ITypeSymbol? Resolve(Compilation compilation) =>
        IsEmpty
            ? null
            : DocumentationCommentId.GetFirstSymbolForReferenceId(DocumentationId, compilation) as ITypeSymbol
              ?? ResolveByName(DocumentationId, compilation);

    /// <summary>
    /// Rebuilds a type that <see cref="DocumentationCommentId"/> cannot look up because it does
    /// not exist in this compilation. Types emitted by another source generator are the common
    /// case: they bind to error symbols, which is exactly what the mapper expects to see and what
    /// it would have received had the symbol never left the attribute.
    /// </summary>
    private static ITypeSymbol? ResolveByName(string id, Compilation compilation)
    {
        if (id.StartsWith("T:", StringComparison.Ordinal)) id = id.Substring(2);

        if (id.EndsWith("[]", StringComparison.Ordinal))
            return ResolveByName(id.Substring(0, id.Length - 2), compilation) is { } elementType
                ? compilation.CreateArrayTypeSymbol(elementType)
                : null;

        var name = id;
        var typeArguments = ImmutableArray<ITypeSymbol>.Empty;

        var brace = id.IndexOf('{');

        if (brace >= 0 && id[id.Length - 1] == '}')
        {
            name = id.Substring(0, brace);

            var builder = ImmutableArray.CreateBuilder<ITypeSymbol>();

            foreach (var argument in SplitTypeArguments(id.Substring(brace + 1, id.Length - brace - 2)))
            {
                if (ResolveByName(argument, compilation) is not { } resolved) return null;

                builder.Add(resolved);
            }

            typeArguments = builder.ToImmutable();
        }

        var arity = typeArguments.Length;

        var named = compilation.GetTypeByMetadataName(arity > 0 ? name + "`" + arity : name);

        if (named is null)
        {
            var lastDot = name.LastIndexOf('.');

            // The container must never be null: TypeMeta distinguishes a type that lives in the
            // global namespace from one with no namespace at all, and only the former gets its
            // declaring type's namespace prepended.
            var container = (lastDot < 0 ? null : ResolveNamespace(name.Substring(0, lastDot), compilation))
                ?? compilation.GlobalNamespace;

            named = compilation.CreateErrorTypeSymbol(container, name.Substring(lastDot + 1), arity);
        }

        return arity > 0 ? named.Construct([.. typeArguments]) : named;
    }

    private static INamespaceSymbol? ResolveNamespace(string fullName, Compilation compilation)
    {
        INamespaceSymbol? current = compilation.GlobalNamespace;

        foreach (var part in fullName.Split('.'))
        {
            current = current?.GetNamespaceMembers().FirstOrDefault(n => n.Name == part);

            if (current is null) return null;
        }

        return current;
    }

    private static IEnumerable<string> SplitTypeArguments(string arguments)
    {
        var depth = 0;
        var start = 0;

        for (var i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case '{': depth++; break;
                case '}': depth--; break;
                case ',' when depth == 0:
                    yield return arguments.Substring(start, i - start);
                    start = i + 1;
                    break;
            }
        }

        yield return arguments.Substring(start);
    }
}

/// <summary>
/// A request to generate a mapping, captured as pure data at the syntax boundary.
/// </summary>
internal readonly record struct BindRequest(TypeRef From, TypeRef To, MappingKind MapKind, IgnoreBind Ignore)
{
    internal readonly bool IsValid => !From.IsEmpty && !To.IsEmpty;
}

/// <summary>
/// A finished source file. Strings are perfectly equatable, so this is the only shape allowed to
/// leave the generation step.
/// </summary>
internal readonly record struct GeneratedSource(string HintName, string Text);
