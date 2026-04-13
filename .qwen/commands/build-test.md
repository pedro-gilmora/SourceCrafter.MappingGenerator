# Build and test
dotnet build SourceCrafter.MappingGenerator.slnx -c Debug && dotnet test SourceCrafter.MappingGenerator.UnitTests/SourceCrafter.MappingGenerator.UnitTests.csproj -c Release --no-build
