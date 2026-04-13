# SourceCrafter.Mappify

A **Roslyn incremental source generator** for .NET that produces type-mapping extension methods at compile-time — zero runtime overhead, no reflection.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

---

## Table of Contents

- [Features](#features)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Declaring Mappings](#declaring-mappings)
  - [Assembly-level](#assembly-level)
  - [Type-level](#type-level)
- [Attributes Reference](#attributes-reference)
  - [MapAttribute](#mapattribute)
  - [IgnoreAttribute](#ignoreattribute)
  - [IgnoreForAttribute](#ignoreforattribute)
  - [MaxAttribute](#maxattribute)
  - [ExtendAttribute](#extendattribute)
- [Generated Methods](#generated-methods)
  - [Object Mapping](#object-mapping)
  - [Collection Mapping](#collection-mapping)
- [Member Matching Rules](#member-matching-rules)
- [Type Conversion](#type-conversion)
- [Collection Types Supported](#collection-types-supported)
- [Enum Extensions](#enum-extensions)
- [UnsafeAccessor Support](#unsafeaccessor-support)
- [Interface Mapping](#interface-mapping)
- [MappingKind](#mappingkind)
- [ApplyTo](#applyto)
- [Advanced Scenarios](#advanced-scenarios)
- [Generated Output Example](#generated-output-example)

---

## Features

| Feature | Description |
|---|---|
| **Compile-time generation** | All mapper code is emitted at build time as `partial` extension methods — no runtime reflection |
| **Bidirectional mapping** | A single attribute declaration generates both `ToTarget()` and `ToSource()` |
| **In-place update** | `Update(source)` reuses the existing target instance |
| **Deep copy** | `Copy()` performs a full structural copy |
| **Collection mapping** | Arrays, `List<T>`, `Dictionary`, `Span<T>`, `Stack<T>`, `Queue<T>`, `ReadOnlyCollection<T>`, and more |
| **Tuple ↔ object** | Value tuples map automatically to/from named types |
| **UnsafeAccessor** | Read-only fields and `init`-only properties are assigned via `UnsafeAccessorAttribute` on .NET 8+ |
| **Recursive types** | Self-referential types are supported with a configurable depth limit |
| **Enum extensions** | Rich enum helpers (names, values, descriptions, lookup) via `[Extend]` |
| **Null-safety** | Full nullable-aware code generation |

---

## Installation

Reference the generator package so it runs during compilation:

`dotnet package add SourceCrafter.Mappify`

---

## Quick Start

```csharp
// AssemblyInfo.cs (or any file)
using SourceCrafter.Mappify.Attributes;
using MyApp.Models;

[assembly: Map<User, UserDto>]
```

That's it. The generator immediately produces the following extension methods inside `SourceCrafter.Mappify.Mappings`:

```csharp
// Created → new target populated from source
UserDto dto = user.ToUserDto();

// Update → existing target populated from source (returns target)
dto.Update(user);

// Copy → deep structural copy of the same type
User copy = user.Copy();
```

---

## Declaring Mappings

### Assembly-level

Use `[assembly: Map<TSource, TTarget>]` in any file to register a bidirectional mapping. The `MappingKind` and `ApplyTo` parameters are optional.

```csharp
[assembly:
    Map<User, UserDto>,
    Map<IAppUser, MeAsUser>,
    Map<User, UserMiniDto>(MappingKind.Normal)   // only ToXxx, no Update
]
```

### Type-level

Decorate a class or struct directly with `[Map<TSource>]`:

```csharp
[Map<User>]
public class UserMiniDto
{
    public string FullName { get; set; } = null!;
    public int    Count    { get; set; }
    public int    Age      { get; set; }
}
```

---

## Attributes Reference

### `MapAttribute`

**Assembly / type level** — registers a bidirectional mapping.

```csharp
// Generic two-type form (assembly attribute)
[assembly: Map<TSource, TTarget>(
    kind          = MappingKind.All,   // Normal | Fill | All
    ignore        = ApplyTo.None,      // suppress a side
    ignoreMembers = new[]{ "Secret" }  // member name blacklist
)]

// Generic one-type form (on a class/struct)
[Map<TSource>(MappingKind.Fill)]
public class MyDto { ... }
```

**Member level** — manually binds this member to a named member on another type.

```csharp
// Map Balance (on User) → TotalAmount (on UserDto)
[Map(nameof(UserDto.TotalAmount))]
public double? Balance { get; set; }

// Apply only when User is the source
[Map(nameof(User.FullName), ApplyTo.Source)]
public string Name { get; set; }
```

---

### `IgnoreAttribute`

Excludes a member from **all** mappings in both directions.

```csharp
[Ignore]
public string? InternalSecret { get; set; }
```

---

### `IgnoreForAttribute`

Excludes a member only when mapping **against a specific counterpart member**.

```csharp
[IgnoreFor(nameof(UserDto.Supervisor))]
public User? Supervisor { get; init; }
```

Use the `ApplyTo` parameter to restrict which direction the exclusion applies:

```csharp
[IgnoreFor(nameof(UserDto.Supervisor), ApplyTo.Source)]
public User? Supervisor { get; init; }
```

---

### `MaxAttribute`

Caps the recursion depth for **self-referential** members.

```csharp
[Max(2)]
public Role MainRole { get; set; }
```

---

### `ExtendAttribute`

Generates [enum extension helpers](#enum-extensions).

```csharp
// On an enum declaration
[Extend]
public enum Status { NotStarted, Started, Stopped, Cancelled, Failed }

// As an assembly attribute (for types you don't own)
[assembly: Extend<MappingKind>]
```

---

## Generated Methods

### Object Mapping

For every registered `Map<TSource, TTarget>` pair the generator emits the methods below inside `public static partial class SourceCrafter.Mappify.Mappings`.

| Method | Signature | Description |
|---|---|---|
| **Convert** | `TTarget ToTarget(this TSource source)` | Creates a new `TTarget` populated from `source` |
| **Update** | `TTarget Update(this TTarget target, TSource source)` | Updates `target` in-place; returns `target` |
| **Copy** | `TSelf Copy(this TSelf source)` | Structural copy (same-type mapping) |

All three methods are generated by default (`MappingKind.All`).  
Use `MappingKind.Normal` to generate only `ToXxx` / `Copy`, or `MappingKind.Fill` for only `Update`.

### Collection Mapping

For every pair of compatible collection types:

| Method | Description |
|---|---|
| `ToTargetCollection(this TSourceCol source)` | Creates a new target collection |
| `Update(this TTargetCol target, TSourceCol source)` | Clears and refills `target` from `source` |

---

## Member Matching Rules

The generator matches members automatically using the following priority:

1. **Manual binding** — `[Map(nameof(Other.Member))]` on either side
2. **Exact name** — `Balance` ↔ `Balance`
3. **Type-prefixed name** — `UserBalance` on source matches `Balance` on a type named `User`
4. **Case-insensitive** — when at least one side is a value tuple
5. **Key/Value semantics** — tuple elements `id`/`item` map to `Key`/`Value` in `KeyValuePair` and `Dictionary` items

Matching respects `[Ignore]` and `[IgnoreFor]` before any assignment is emitted.

---

## Type Conversion

The generator selects a conversion strategy per member pair:

| Strategy | When used |
|---|---|
| **Direct assignment** | Types are assignment-compatible or identical |
| **Explicit cast** | A user-defined `explicit operator` exists |
| **Implicit cast** | A user-defined `implicit operator` exists |
| **Recursive mapper** | A nested complex type is also registered for mapping |
| **Collection mapper** | Both sides are supported collection types |

Nullability is respected throughout — a nullable source assigned to a non-nullable target gets `?? default!` or a null-guard as appropriate.

---

## Collection Types Supported

| .NET Type | Notes |
|---|---|
| `T[]` | Array with index-based `for` loop and `Array.Resize` for unknown-length sources |
| `List<T>` / `IList<T>` / `ICollection<T>` | Standard `Add` append |
| `IEnumerable<T>` | Forward-only enumeration |
| `ReadOnlyCollection<T>` / `IReadOnlyCollection<T>` / `IReadOnlyList<T>` | Wrapped `List<T>` internally |
| `Stack<T>` | `Push` append |
| `Queue<T>` | `Enqueue` append |
| `Span<T>` | Ref-like; used as method parameter |
| `ReadOnlySpan<T>` | Read-only ref-like |
| `Dictionary<TKey,TValue>` / `IDictionary<TKey,TValue>` | Key/Value mapping; also maps from `List<(id, item)>` |

Cross-collection-type mapping is fully supported, e.g. `List<(string id, string item)>` → `Dictionary<object, string>`.

---

## Enum Extensions

Decorate an enum with `[Extend]` (or use `[assembly: Extend<TEnum>]` for external enums) to generate the following members via C# 14 **extension blocks**:

```csharp
[Extend]
[Flags]
public enum Status
{
    NotStarted,
    [Description("Transaction was stopped")]   Stopped,
    [Description("Transaction has been started")] Started,
    [Description("Transaction has been cancelled by user")] Cancelled,
    [Description("Transaction had an external failure")]    Failed
}
```

### Generated API

```csharp
// Static collections
ReadOnlySpan<Status> allValues = Status.Values;
ReadOnlySpan<string> allNames  = Status.Names;
ReadOnlySpan<string> allDescs  = Status.Descriptions;

// Instance helpers
string?  name = Status.Started.Name;         // "Started"
string?  desc = Status.Cancelled.Description; // "Transaction has been cancelled by user"

// Lookup
bool found = Status.TryGetValue("Cancelled", out Status s);   // true
bool named = Status.Started.TryGetName(out string? n);        // true, "Started"
bool descd = Status.Cancelled.TryGetDescription(out string? d); // true

// Existence checks
bool byInt  = Status.IsDefined(1);          // true
bool byName = Status.IsDefined("Failed");   // true
bool bad    = Status.IsDefined(99);         // false
```

---

## UnsafeAccessor Support

On **.NET 8+**, the generator automatically detects `UnsafeAccessorAttribute` availability and uses it to assign:

- `readonly` fields
- `init`-only properties (via their backing field)
- Nullable value-type backing fields

The accessor helper methods are emitted into a separate `MappingExtras` file and are not part of the public API.

```csharp
// Example of a generated unsafe accessor
[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "<Id>k__BackingField")]
extern static ref int? GetId(this UserDto _);
```

---

## Interface Mapping

To map **to an interface** while producing a **concrete implementation**, use the `IImplement<IInterface, Implementation>` marker:

```csharp
// TypeSet will treat IAppUser as the shape but instantiate MeAsUser
[assembly: Map<IImplement<IAppUser, MeAsUser>, User>]
```

---

## MappingKind

Controls which methods are generated for a mapping pair.

| Value | int | Methods generated |
|---|---|---|
| `Normal` | 1 | `ToXxx()` / `Copy()` |
| `Fill` | 2 | `Update()` |
| `All` | 3 | Both (default) |

```csharp
[assembly: Map<User, UserDto>(MappingKind.Normal)]   // no Update
[assembly: Map<User, UserDto>(MappingKind.Fill)]     // no ToUserDto/ToUser
```

---

## ApplyTo

Scopes an attribute to one side of the mapping relationship.

| Value | Meaning |
|---|---|
| `None` | No side suppressed (default) |
| `Source` | Applies when this type/member is the **source** |
| `Target` | Applies when this type/member is the **target** |
| `Both` | Applies in both roles |

Used on `[Map]`, `[IgnoreFor]`, and `[Ignore]` at the member level:

```csharp
// WindowsUser.Name maps to User.FullName only when WindowsUser is the source
[Map(nameof(User.FullName), ApplyTo.Source)]
public string Name { get; set; }
```

---

## Advanced Scenarios

### Recursive / Self-referential Types

Use `[Max(n)]` on the recursive member to cap depth. The generated code receives `depth` and `maxDepth` parameters with a guard at the top:

```csharp
public class User
{
    [Max(2)]
    public User? Supervisor { get; init; }
}
```

### Tuple ↔ Struct mapping

Value tuples map naturally to structs by field-name/position rules:

```csharp
// UserDto.MainRole is (int id, string name)
// User.MainRole is Role { int Id; string Name; }
// Generated: source.MainRole.ToRole() / role.ToTupleOfIntAndString()
```

### Numeric type widening / narrowing

```csharp
// User.Balance  is double?
// UserDto.TotalAmount is decimal
// Generated cast: target.TotalAmount = (decimal)(source.Balance ?? default!);
```

### Dictionary ↔ List of tuples

```csharp
// User.ExtendedProperties is Dictionary<object, string>
// UserDto.ExtendedProperties is List<(string id, string item)>
// Generated: source.ExtendedProperties.ToDictionaryOfObjectAndString()
//            source.ExtendedProperties.ToListOfTupleOfStringAndString()
```

---

## Generated Output Example

Given:

```csharp
[assembly: Map<User, UserDto>]
```

The generator emits `021_UserDto_User.map.g.cs`:

```csharp
#nullable enable
namespace SourceCrafter.Mappify;

public static partial class Mappings
{
    // ── UserDto self-copy ─────────────────────────────────────────────
    public static global::SourceCrafter.UnitTests.UserDto Copy(
        this global::SourceCrafter.UnitTests.UserDto source)
        => Update(new global::SourceCrafter.UnitTests.UserDto(), source);

    public static global::SourceCrafter.UnitTests.UserDto Update(
        this global::SourceCrafter.UnitTests.UserDto target,
             global::SourceCrafter.UnitTests.UserDto source, int __l = 0)
    {
        target.GetId() = source.Id;
        target.FullName = source.FullName;
        target.Age = source.Age;
        target.DateOfBirth = source.DateOfBirth;
        target.TotalAmount = source.TotalAmount;
        target.GetMainRole().Update(source.MainRole);
        target.ExtendedProperties.Update(source.ExtendedProperties);
        // ... remaining members
        return target;
    }

    // ── UserDto → User ────────────────────────────────────────────────
    public static global::SourceCrafter.UnitTests.User ToUser(
        this global::SourceCrafter.UnitTests.UserDto source)
        => Update(new global::SourceCrafter.UnitTests.User(), source);

    public static global::SourceCrafter.UnitTests.User Update(
        this global::SourceCrafter.UnitTests.User   target,
             global::SourceCrafter.UnitTests.UserDto source, int __l = 0)
    {
        target.GetId()  = source.Id;
        target.FullName = source.FullName;
        target.Age      = source.Age;
        target.Balance  = (double)source.TotalAmount;          // decimal → double
        target.GetMainRole().Update(source.MainRole);          // tuple → struct
        target.ExtendedProperties
              .Update(source.ExtendedProperties);              // List<tuple> → Dictionary
        // ...
        return target;
    }

    // ── User → UserDto ────────────────────────────────────────────────
    public static global::SourceCrafter.UnitTests.UserDto ToUserDto(
        this global::SourceCrafter.UnitTests.User source)
        => Update(new global::SourceCrafter.UnitTests.UserDto(), source);

    public static global::SourceCrafter.UnitTests.UserDto Update(
        this global::SourceCrafter.UnitTests.UserDto target,
             global::SourceCrafter.UnitTests.User    source, int __l = 0)
    {
        target.GetId()      = source.Id ?? default!;
        target.FullName     = source.FullName;
        target.TotalAmount  = (decimal)(source.Balance ?? default!); // double → decimal
        target.GetMainRole().Update(source.MainRole);
        // ...
        return target;
    }
}
```

---

## License

MIT © SourceCrafter
