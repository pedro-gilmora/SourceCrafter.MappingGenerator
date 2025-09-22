global using Mapping =
    (Microsoft.CodeAnalysis.ITypeSymbol a, Microsoft.CodeAnalysis.ITypeSymbol b, SourceCrafter.Mappify.MappingKind
    mapKind, SourceCrafter.Mappify.GenerateOn ignore);
global using ScalarConversion = (bool exists, bool isExplicit, bool targetInherits);
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SourceCrafter.Mappify;

[Generator]
public class GeneratedMappers : IIncrementalGenerator
{
    // ReSharper disable once UnusedMember.Global
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if DEBUGSGEN
        Debugger.Launch();
#endif
        try
        {
            context.RegisterSourceOutput(
                context.CompilationProvider.Combine(

                    FindMapperAttributes(context,
                        "SourceCrafter.Mappify.Attributes.ExtendAttribute`1",
                        static n => true,
                        static (_, _, attr) => attr.AttributeClass?.TypeArguments.FirstOrDefault()!
                    ).Combine(

                    FindMapperAttributes(context,
                        "SourceCrafter.Mappify.Attributes.ExtendAttribute",
                        static n => n is EnumDeclarationSyntax,
                        static (targetSymbol, _, _) => (ITypeSymbol)targetSymbol
                    ).Combine(
                            
                    FindMapperAttributes(context,
                        "SourceCrafter.Mappify.Attributes.MapAttribute`1",
                        static n => n is ClassDeclarationSyntax,
                        static (targetSymbol, model, attr) =>
                            attr is {
                            AttributeClass.TypeArguments: [{ } target], 
                                ConstructorArguments: [{ Value: int mapKind }, { Value: int ignore }, ..]
                        }
                            ? new Mapping(
                                (ITypeSymbol)targetSymbol,
                                target,
                                (MappingKind)mapKind,
                                (GenerateOn)ignore)
                            : default
                    ).Combine(
                    
                    FindMapperAttributes(
                        context,
                        "SourceCrafter.Mappify.Attributes.MapAttribute`2",
                        static n => n is CompilationUnitSyntax,
                        static (_, model, attr) =>
                            attr is { 
                                AttributeClass.TypeArguments: [{ } target, { } source], 
                                ConstructorArguments: [{ Value: int mapKind }, { Value: int ignore }, ..] 
                            }
                                ? new Mapping(
                                    target,
                                    source,
                                    (MappingKind)mapKind,
                                    (GenerateOn)ignore)
                                : default
                    ))))), (ctx, info)
                =>
            {
                var (compilation, (enumGlobal, (enumOnClass, (globalConfig, onClass)))) = info;

                try
                {
                    Mappers mappers = new(compilation, ctx.AddSource);
        
                    var i = 0;


                    foreach (var (a, b, mapKind, ignore) in globalConfig.Concat(onClass))
                    {
                        var targetType = mappers.Types.GetOrAdd(a);

                        mappers.GetOrAdd(targetType, targetType, GenerateOn.None, ref i);

                        if (SymbolEqualityComparer.Default.Equals(a, b)) continue;

                        var sourceType = mappers.Types.GetOrAdd(b);

                        mappers.GetOrAdd(sourceType, sourceType, GenerateOn.None, ref i);

                        mappers.GetOrAdd(targetType, sourceType, ignore, ref i);

                        i++;
                    }

                    mappers.RenderExtra(ctx.AddSource);

                    foreach (var item in enumGlobal.Concat(enumOnClass))
                    {
                        mappers.Types.GetOrAdd(item).BuildEnumMethods(ctx.AddSource, ref i);
                    }
                }
                catch (Exception e)
                {
                    if(Debugger.IsLogging())
                        Debugger.Log(0, "Source Generation", $"[SourceCrafter Exception]: \n{e}");
                }
            });
        }
        catch (Exception e)
        {
            if(Debugger.IsLogging())
                Debugger.Log(0, "Source Generation", $"[SourceCrafter Exception]: \n{e}");
        }
    }

    private IncrementalValueProvider<ImmutableArray<T>> FindMapperAttributes<T>(
        IncrementalGeneratorInitializationContext context,
        string attrFullQualifiedName,
        Predicate<SyntaxNode> predicate,
        Func<ISymbol, SemanticModel, AttributeData, T> selector
    ) =>
        context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attrFullQualifiedName,
                (n, _) => predicate(n),
                (gasc, _) => gasc.Attributes.Select(attr => selector(gasc.TargetSymbol, gasc.SemanticModel, attr)))
            .SelectMany((i, _) => i)
            .Collect();
}