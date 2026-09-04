using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SourceCrafter.Mapifier.Constants;

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace SourceCrafter.Mapifier;

/// <summary>
/// Pipeline wiring only: it collects the mapping attributes as pure, value-equatable data and
/// hands them off. Parsing lives in <see cref="MappingParser"/> and rendering in
/// <see cref="Emitter"/>.
/// </summary>
[Generator]
public sealed class Generator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if DEBUG_SG
        Debugger.Launch();
#endif
        // Every provider below projects straight to pure, value-equatable data. Nothing that can
        // root a Compilation (symbols, syntax nodes, AttributeData) is allowed past this point.
        var enums = FindMapperAttributes(
                context,
                "SourceCrafter.Mapifier.Attributes.ExtendAttribute`1",
                static _ => true,
                static (_, attr) => TypeRef.From(attr.AttributeClass?.TypeArguments is [{ } arg] ? arg : null))
            .Concat(FindMapperAttributes(
                context,
                "SourceCrafter.Mapifier.Attributes.ExtendAttribute",
                static n => n is EnumDeclarationSyntax,
                static (target, _) => TypeRef.From(target as ITypeSymbol)))
            .WithTrackingName("EnumRefs");

        var classLevel = FindMapperAttributes(
                context,
                "SourceCrafter.Mapifier.Attributes.BindAttribute`1",
                static n => n is ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax,
                static (target, attr) =>
                    attr is { AttributeClass.TypeArguments: [{ } to], ConstructorArguments: [{ Value: int mapKind }, { Value: int ignore }, ..] }
                        ? new BindRequest(TypeRef.From(target as ITypeSymbol), TypeRef.From(to), (MappingKind)mapKind, (IgnoreBind)ignore)
                        : default);

        var assemblyLevel = FindMapperAttributes(
                context,
                "SourceCrafter.Mapifier.Attributes.BindAttribute`2",
                static n => n is CompilationUnitSyntax,
                static (_, attr) =>
                    attr is { AttributeClass.TypeArguments: [{ } from, { } to], ConstructorArguments: [{ Value: int mapKind }, { Value: int ignore }, ..] }
                        ? new BindRequest(TypeRef.From(from), TypeRef.From(to), (MappingKind)mapKind, (IgnoreBind)ignore)
                        : default);

        var requests = assemblyLevel.Concat(classLevel).WithTrackingName("BindRequests");

        // Discovery needs the Compilation, so this step reruns whenever the compilation changes.
        // That is safe: the whole object graph it builds is created and discarded inside the step,
        // and only strings come back out.
        var sources = enums
            .Combine(requests)
            .Combine(context.CompilationProvider)
            .Select(static (input, token) =>
            {
                var ((enumRefs, bindRequests), compilation) = input;

                return Emit(compilation, enumRefs, bindRequests, token);
            })
            .WithTrackingName("GeneratedSources");

        context.RegisterSourceOutput(sources, static (ctx, generated) =>
        {
            foreach (var source in generated)
                ctx.AddSource(source.HintName, source.Text);
        });
    }

    /// <summary>
    /// Runs a complete generation pass: a fresh parser reads the compilation, and the emitter
    /// renders whatever it finds. Both are discarded when the call returns, so no compilation is
    /// rooted between runs of the pipeline.
    /// </summary>
    internal static EquatableArray<GeneratedSource> Emit(
        Compilation compilation,
        EquatableArray<TypeRef> enums,
        EquatableArray<BindRequest> requests,
        CancellationToken cancellationToken)
    {
        try
        {
            return new Emitter(new MappingParser(compilation, cancellationToken), cancellationToken)
                .Emit(enums, requests);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Trace.Write($"[SourceCrafter.Mapifier Exception] {e}");

            return ImmutableArray<GeneratedSource>.Empty;
        }
    }

    private static IncrementalValueProvider<EquatableArray<T>> FindMapperAttributes<T>(
        IncrementalGeneratorInitializationContext context,
        string attrFullQualifiedName,
        Predicate<SyntaxNode> predicate,
        Func<ISymbol, AttributeData, T> selector)
        where T : IEquatable<T>
        =>
            context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    attrFullQualifiedName,
                    (n, _) => predicate(n),
                    (gasc, _) => gasc.Attributes.Select(attr => selector(gasc.TargetSymbol, attr)).ToImmutableArray())
                .SelectMany(static (i, _) => i)
                .Collect()
                .Select(static (values, _) => new EquatableArray<T>(values));
}

internal static class ProviderExtensions
{
    internal static IncrementalValueProvider<EquatableArray<T>> Concat<T>(
        this IncrementalValueProvider<EquatableArray<T>> left,
        IncrementalValueProvider<EquatableArray<T>> right)
        where T : IEquatable<T>
        =>
            left.Combine(right).Select(static (pair, _) =>
                new EquatableArray<T>(pair.Left.Values.AddRange(pair.Right.Values)));
}
