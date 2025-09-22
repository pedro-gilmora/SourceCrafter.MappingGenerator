using Microsoft.CodeAnalysis;


using System;
using System.Collections.Generic;
using System.Text;

namespace SourceCrafter.Mappify
{
    internal sealed class TypeSet(Compilation compilation) : Set<int, TypeMeta>(t => t.Id)
    {
        internal readonly Compilation Compilation = compilation;
        internal readonly HashSet<CodeRenderer> UnsafeAccessors = new(CodeEqualityComparer.Default);
        private readonly HashSet<string> _sanitizedNames = new(StringComparer.Ordinal);

        internal  TypeMeta GetOrAdd(ITypeSymbol membersSource, bool isDictionaryOwned = false)
        {
            ITypeSymbol? typeSymbol = null;
            
            if((membersSource = membersSource.ToNonNullable) is INamedTypeSymbol { 
                   IsGenericType: true,
                   Name: "IImplement", 
                   ContainingNamespace.ContainingNamespace.Name: "SourceCrafter", 
                   ContainingNamespace.Name: "Mappify", 
                   TypeArguments: [{ } iFace, { } impl]
               })
            {
                membersSource = iFace;
                typeSymbol = impl;
            }
            
            var id = SymbolEqualityComparer.Default.GetHashCode(membersSource);

            ref var type = ref GetOrAddDefault(id, out var exists);

            return exists
                ? type!
                : new TypeMeta(ref type, this, id, membersSource, typeSymbol, isDictionaryOwned);
        }

        internal string SanitizeName(ITypeSymbol type)
        {
            StringBuilder id = new();
            
            SanitizeTypeName(type);
            
            var sanitizedTypeName = id.ToString();

            if (_sanitizedNames.Add(sanitizedTypeName) || (type.ContainingType ?? (ISymbol)type.ContainingNamespace) is not { } ns)
            {
                return sanitizedTypeName;
            }

            while (ns != null && !_sanitizedNames.Add(sanitizedTypeName = ns.ToNameOnly() + sanitizedTypeName))
            {
                ns = ns.ContainingNamespace;
            }

            return sanitizedTypeName;

            void SanitizeTypeName(ITypeSymbol inType)
            {
                switch (inType)
                {
                    case INamedTypeSymbol { IsTupleType: true, TupleElements: { Length: > 0 } els }:
                        
                        id.Append("TupleOf");
                        
                        foreach (var x1 in els)
                        {
                            SanitizeTypeName(x1.Type);
                        }

                        return ;

                    case INamedTypeSymbol { IsGenericType: true, TypeArguments: { } args }:
                        
                        id.Append(inType.Name).Append("Of");
                        
                        foreach (var x1 in args)
                        {
                            SanitizeTypeName(x1);
                        }

                        return ;

                    default:
                        
                        var start = id.Length;

                        if (inType is IArrayTypeSymbol { ElementType: { } elType })
                        {
                            id.Append("ArrayOf");
                            SanitizeTypeName(elType);
                        }
                        else
                        {
                            id.Append(inType.ToTypeNameFormat());
                        }
                        
                        if(start == id.Length || char.IsUpper(id[start] )) return;
                        
                        id[start] = char.ToUpperInvariant(id[start]);
                        
                        return ;
                };
            }
        }
    }
}