---
description: Roslyn incremental source generator for compile-time type mapping. Zero runtime overhead, no reflection.
applyTo: '**/*.cs'
---

# SourceCrafter.MappingGenerator

Roslyn incremental source generator producing type-mapping extension methods at compile-time.

## Project Structure

| Project | Purpose |
|---|---|
| `SourceCrafter.Mappify` | Primary generator (NuGet package). Targets netstandard2.0 |
| `SourceCrafter.MappingGenerator` | Alternate generator (SourceCrafter.Bindings). Not in solution |
| `SourceCrafter.MappingGenerator.UnitTests` | xUnit tests for Mappify generator (net9.0) |

## Key Files

### SourceCrafter.Mappify (primary generator)
- `Main.cs` - Entry point: `GeneratedMappers : IIncrementalGenerator`
- `Mappers.cs` - Orchestration: TypeSet management, TypeMap creation, source emission
- `TypeMap.cs` - Core mapping logic (1250 lines): member matching, collections, conversions
- `TypeMeta.cs` - Type metadata extraction, member enumeration, UnsafeAccessor support
- `TypeSet.cs` - Type cache/registry, deduplication
- `MemberMeta.cs` - Property/field representation with matching logic
- `MapAttribute.cs` - All attributes: MapAttribute, IgnoreAttribute, IgnoreForAttribute, MaxAttribute, ExtendAttribute
- `Constants.cs` - Enums: ApplyTo, EnumerableType, MappingKind (Normal=1, Fill=2, All=3)
- `Helpers.cs` - String utilities, type helpers, hash code computation
- `Helpers/Map.cs` - Custom hash table (no LINQ)
- `Helpers/Set.cs` - Custom hash set (no LINQ)

## Build Commands

```bash
# Build all projects
dotnet build SourceCrafter.MappingGenerator.slnx -c Debug

# Build generator only
dotnet build SourceCrafter.Mappify/SourceCrafter.Mappify.csproj -c Debug

# Run tests
dotnet test SourceCrafter.MappingGenerator.UnitTests/SourceCrafter.MappingGenerator.UnitTests.csproj -c Release

# Pack for NuGet
dotnet pack SourceCrafter.Mappify/SourceCrafter.Mappify.csproj -c Pack
```

## Configuration

| Config | Purpose |
|---|---|
| Debug | Normal development |
| DebugSGen / DEBUG_SG | Debugger.Launch() at generator start |
| Release | Optimized build |
| Pack | NuGet package creation |

## Usage

```csharp
// Assembly-level mapping registration
[assembly: Map<User, UserDto>]

// Type-level
[Map<User>]
public class UserDto { ... }
```

Generated methods in `SourceCrafter.Mappify.Mappings`:
- `TTarget ToTarget(this TSource source)` - Create new populated target
- `TTarget Update(this TTarget target, TSource source)` - In-place update
- `TSelf Copy(this TSelf source)` - Deep structural copy

## Attributes

| Attribute | Purpose |
|---|---|
| `[Map<TSource, TTarget>]` | Register bidirectional mapping |
| `[Map(nameof(Other.Member))]` | Manual member binding |
| `[Ignore]` | Exclude member from all mappings |
| `[IgnoreFor(nameof(Other.Member))]` | Exclude for specific counterpart |
| `[Max(n)]` | Cap recursion depth for self-referential types |
| `[Extend]` | Generate enum extension helpers |

## Important Patterns

- Uses custom hash tables (not Dictionary) for source generator performance
- UnsafeAccessorAttribute on .NET 8+ for readonly fields and init-only properties
- Bidirectional mapping from single declaration
- Supports: arrays, List, Dictionary, Stack, Queue, Span, ReadOnlyCollection, tuples
- Interface mapping via `IImplement<IInterface, Implementation>`
- Recursive types with configurable depth limit
