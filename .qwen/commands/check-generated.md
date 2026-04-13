# Check generated source files from the Mappify generator
# Usage: /check-generated

## Step 1: Clean previous generated files

```bash
if (Test-Path "SourceCrafter.MappingGenerator.UnitTests/GeneratedFiles") {
    Remove-Item "SourceCrafter.MappingGenerator.UnitTests/GeneratedFiles" -Recurse -Force
}
```

## Step 2: Build with emit properties

```bash
dotnet build SourceCrafter.MappingGenerator.UnitTests/SourceCrafter.MappingGenerator.UnitTests.csproj -c Debug --no-incremental -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=GeneratedFiles
```

## Step 3: List generated files

```bash
Get-ChildItem "SourceCrafter.MappingGenerator.UnitTests/GeneratedFiles" -Recurse -Filter "*.cs" | Sort-Object LastWriteTime -Descending | Select-Object FullName
```
