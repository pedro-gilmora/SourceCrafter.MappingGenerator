# Add member mapping customization
# Usage: /command customize-member-mapping

## Scenario 1: Manual member binding

On source or target property:
```csharp
[Map(nameof(CounterpartType.OtherPropertyName))]
public string MemberName { get; set; }
```

With direction scope:
```csharp
[Map(nameof(UserDto.FullName), ApplyTo.Source)]
public string Name { get; set; }
```

## Scenario 2: Ignore member

```csharp
[Ignore]
public string InternalSecret { get; set; }
```

## Scenario 3: Ignore for specific counterpart

```csharp
[IgnoreFor(nameof(UserDto.Supervisor))]
public User? Supervisor { get; init; }
```

With direction:
```csharp
[IgnoreFor(nameof(UserDto.Supervisor), ApplyTo.Source)]
public User? Supervisor { get; init; }
```

## Scenario 4: Limit recursion

```csharp
[Max(2)]
public Role MainRole { get; set; }
```

## After changes:

```bash
dotnet build SourceCrafter.MappingGenerator.slnx -c Debug
```
