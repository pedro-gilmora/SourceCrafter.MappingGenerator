# Debug the source generator
# Usage: /command debug-generator

## Step 1: Enable Debugger.Launch()

Add to Generator.cs or Main.cs at the start of Execute method:
```csharp
if (!System.Diagnostics.Debugger.IsAttached)
{
    System.Diagnostics.Debugger.Launch();
}
```

## Step 2: Build with DEBUG_SG config

```bash
dotnet build SourceCrafter.Mappify/SourceCrafter.Mappify.csproj -c DebugSGen
```

Or modify test project to reference generator:
```bash
dotnet build SourceCrafter.MappingGenerator.UnitTests/SourceCrafter.MappingGenerator.UnitTests.csproj -c Debug
```

## Step 3: Attach debugger

When build triggers, Visual Studio/JIT debugger will prompt. Attach your debugger.

## Step 4: Set breakpoints

Set breakpoints in:
- Main.cs (Execute method)
- TypeMap.cs (Build methods)
- Mappers.cs (EmitSource methods)

## Step 5: Step through generation

Debugger will stop at Launch point. Step through to see how attributes are parsed and source is generated.

**IMPORTANT**: Remove Debugger.Launch() before committing!
