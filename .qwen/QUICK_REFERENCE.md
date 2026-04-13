# SourceCrafter.Mappify - Quick Reference

## Commands

| Command | Description |
|---|---|
| `/build` | Build solution (Debug) |
| `/test` | Run all tests (Release) |
| `/build-test` | Build and test |
| `/pack` | Create NuGet package |
| `/add-mapping` | Add new mapping attribute |
| `/customize-mapping` | Customize member mappings |
| `/debug-generator` | Debug the source generator |

## Usage Patterns

### Assembly-level mapping
```csharp
[assembly: Map<Source, Target>]
[assembly: Map<Source, Target>(MappingKind.Normal)] // ToXxx only, no Update
[assembly: Map<Source, Target>(ignoreMembers: new[] { "Secret" })]
```

### Type-level mapping
```csharp
[Map<Source>]
public class Target { }
```

### Member customization
```csharp
[Map(nameof(OtherType.OtherMember))]
public string Member { get; set; }

[Ignore]
public string Secret { get; set; }

[IgnoreFor(nameof(OtherType.Member))]
public string LimitedMember { get; set; }

[Max(2)]
public RecursiveType Recursive { get; set; }
```

### Enum extensions
```csharp
[Extend]
public enum Status { Active, Inactive }

// Generates: Status.Values, Status.Names, Status.Descriptions
//            status.Name, status.Description, status.TryGetName(), etc.
```

## Generated Methods

For `[assembly: Map<User, UserDto>]`:

| Method | Signature | Purpose |
|---|---|---|
| **ToUserDto** | `UserDto ToUserDto(this User source)` | Create new DTO from User |
| **ToUser** | `User ToUser(this UserDto source)` | Create User from DTO |
| **Update** | `TTarget Update(this TTarget target, TSource source)` | Update existing instance |
| **Copy** | `TSelf Copy(this TSelf source)` | Deep structural copy |

## MappingKind Values

| Value | Methods Generated |
|---|---|
| `MappingKind.All` (default) | ToXxx + Update + Copy |
| `MappingKind.Normal` | ToXxx + Copy only |
| `MappingKind.Fill` | Update only |

## ApplyTo Values

| Value | Meaning |
|---|---|
| `None` | Apply in both directions (default) |
| `Source` | Apply only when this type is the source |
| `Target` | Apply only when this type is the target |
| `Both` | Apply in both roles |

## Supported Collections

- Arrays (`T[]`)
- `List<T>`, `IList<T>`, `ICollection<T>`
- `IEnumerable<T>`
- `Dictionary<TKey, TValue>`, `IDictionary<TKey, TValue>`
- `Stack<T>`, `Queue<T>`
- `Span<T>`, `ReadOnlySpan<T>`
- `ReadOnlyCollection<T>`, `IReadOnlyCollection<T>`, `IReadOnlyList<T>`
- Value tuples ↔ named types

## Type Conversion Priority

1. Direct assignment (compatible types)
2. Explicit cast (user-defined `explicit operator`)
3. Implicit cast (user-defined `implicit operator`)
4. Recursive mapper (registered complex type)
5. Collection mapper (supported collection types)

## Special Features

- **UnsafeAccessor**: Automatic on .NET 8+ for readonly fields and init-only properties
- **Recursive types**: Supported with `[Max(depth)]` to limit recursion
- **Interface mapping**: Use `IImplement<Interface, Implementation>`
- **Null-safety**: Full nullable-aware code generation
- **Bidirectional**: Single declaration generates both directions

## Architecture

```
Main.cs (IIncrementalGenerator)
  ↓
Mappers.cs (orchestration)
  ↓
TypeSet.cs (type cache)
  ↓
TypeMap.cs (mapping logic - 1250 lines)
  ↓
Generated: SourceCrafter.Mappify.Mappings.*
```

## File Naming

Generated files follow pattern: `{Id}_{Type1}_{Type2}.map.g.cs`

Example: `021_UserDto_User.map.g.cs`

## Testing

Test project uses the generator via:
```xml
<ProjectReference Include="..\SourceCrafter.Mappify\SourceCrafter.Mappify.csproj" 
                  OutputItemType="Analyzer" />
```

This makes the generator run during test compilation, producing mapping methods that tests can call directly.

## Common Issues

**Generator not running?**
- Verify attributes are correct
- Check build configuration (not Pack)
- Ensure types are visible to generator

**Mapping not working?**
- Check member names match (case-sensitive for non-tuples)
- Verify types are compatible
- Look for [Ignore] or [IgnoreFor] attributes

**Debugging?**
- Use DebugSGen configuration
- Add Debugger.Launch() temporarily
- Set breakpoints in Main.cs, TypeMap.cs
