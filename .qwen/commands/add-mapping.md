# Add a new mapping attribute
# Usage: /command add-mapping User UserDto

Search for existing mapping declarations:
```bash
grep_search "assembly: Map<" --glob "*.cs"
```

Add to appropriate file (commonly AssemblyInfo.cs or a dedicated Mappings file):
```csharp
[assembly: Map<$1, $2>]
```

Or decorate the target class directly:
```csharp
[Map<$1>]
public class $2 { }
```

Build to verify generator runs:
```bash
dotnet build SourceCrafter.MappingGenerator.slnx -c Debug
```
