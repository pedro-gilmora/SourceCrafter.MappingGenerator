//Testing utils
//Testing purpose
using FluentAssertions;

using Microsoft.CodeAnalysis.CSharp;
// Analyzer 
using SourceCrafter.Mappify;
using SourceCrafter.UnitTests;

using System.Runtime.CompilerServices;

using Xunit;

namespace SourceCrafter.Bindings.UnitTests
{

    public class TestImplicitMapper
    {

        //[Fact]
        //public static void GetSomeType()
        //{
        //    GetRootAndModel(@"",
        //        out var compilation,
        //        out _,
        //        out _
        //    );

        //    compilation.GetTypeByMetadataName("System.Span`1");
        //}

        //    [Fact]
        //    public static void ParseAssembly()
        //    {
        //        GetRootAndModel(@"using SourceCrafter.Binding.Attributes;
        //using SourceCrafter.UnitTests;

        //[assembly:
        //    Bind<WindowsUser, UserDto>,
        //    Bind<WindowsUser, User>,
        //    Bind<User, UserDto>]",
        //            out var compilation,
        //            out var root,
        //            out var model,
        //            typeof(User), 
        //            typeof(BindAttribute<,>)
        //        );


        //        StringBuilder code = new(@"namespace SourceCrafter.Mappings;

        //public static partial class Mappers
        //{");

        //        var assemblyAtributes = compilation.Assembly
        //            .GetAttributes()
        //            .Where(c => c.AttributeClass?.ToGlobalNonGenericNamespace() == "global::SourceCrafter.Binding.Attributes.BindAttribute")
        //            .Select(c => new MapInfo(new(c.AttributeClass!.TypeArguments[0]), new(c.AttributeClass!.TypeArguments[1]), MappingKind.All, ApplyOn.None))
        //            .ToImmutableArray();

        //        new Generator()
        //            .BuildCode(
        //                (string a, string b) => { },
        //                compilation,
        //                [],
        //                [],
        //                assemblyAtributes);

        //        code.Append("\n}");

        //        //new User().ToUserDto().Asignees!.ElementAt(0).

        //        //Trace.WriteLine(code.ToString());
        //    }


        //    [Fact]
        //    public static void ParseCode()
        //    {
        //        GetRootAndModel(
        //            "SourceCrafter.UnitTests.User user;",
        //            new[] {
        //                typeof(MapAttribute),
        //                typeof(Attribute),
        //                typeof(User)
        //            },
        //            out var compilation,
        //            out var root,
        //            out var semanticModel
        //            );

        //        foreach (var cls in root.DescendantNodes().OfType<VariableDeclarationSyntax>())
        //        {
        //            if (semanticModel.GetSymbolInfo(cls.Type).Symbol is not ITypeSymbol type) continue;
        //            StringBuilder code = new();
        //#if DEBUG
        //            string extra = "";
        //#endif
        ////            MappingGenerator.GetConverters(code, type.GetAttributes(), type, compilation, new HashSet<string>()
        ////#if DEBUG
        ////                , ref extra
        ////#endif
        ////                , false);

        //#if DEBUG
        //            code.Append($@"
        //}}
        ///*
        //Extras:
        //-------{extra}
        //*/
        //");

        //#else
        //            code.Append(@"
        //}");
        //#endif
        //        }
        //    }
        //#if DEBUG
        //    [Fact]
        //    public void TestExternalClass()
        //    {
        //#pragma warning disable CA1416 // Validar la compatibilidad de la plataforma
        //        var winUser = WindowsIdentity.GetCurrent()!.ToWindowsUser();
        //#pragma warning restore CA1416 // Validar la compatibilidad de la plataforma
        //        winUser.Should().NotBeNull();
        //        winUser.Name.Should().Be("DEVSTATION\\Pedro");
        //        winUser.IsAuthenticated.Should().BeTrue();
        //        //var winId = new WindowsUser { Name = "DEVSTATION\\Pedro" }.ToWindowsIdentity();
        //        //winId.Should().NotBeNull();
        //        //winId.Name?.Should().Be("DEVSTATION\\Pedro");
        //        //winId.IsAuthenticated.Should().BeTrue();
        //    }
        //#endif
        [Fact]
        public void TestClass()
        {
            DateTime today = DateTime.Today;
            (int, string)[] roles = [(0, "admin"), (1, "publisher")];

            var userDto = new UserDto
            {
                FullName = "Gil Mora, Pedro",
                Age = 32,
                DateOfBirth = today,
                Count = 5,
                TotalAmount = 45.6m,
                MainRole = roles[0],
                ExtendedProperties = { ( "A", "A" ), ("C", "D" ) }
            };

            var fromDto = userDto.ToUser();
            fromDto.Age.Should().Be(32);
            fromDto.FirstName.Should().Be("Pedro");
            fromDto.LastName.Should().Be("Gil Mora");
            fromDto.DateOfBirth.Should().Be(today);
            fromDto.Balance.Should().Be(45.6);
            fromDto.Count.Should().Be(5);
            fromDto.MainRole.Id.Should().Be(0);

            fromDto.MainRole.Name.Should().Be("admin");

            var userCopy = fromDto.Copy();

            userCopy.Age.Should().Be(32);
            userCopy.FirstName.Should().Be("Pedro");
            userCopy.LastName.Should().Be("Gil Mora");
            userCopy.DateOfBirth.Should().Be(today);
            userCopy.Balance.Should().Be(45.6);
            userCopy.Count.Should().Be(5);
            userCopy.MainRole.Id.Should().Be(0);
            userCopy.MainRole.Name.Should().Be("admin");

            userCopy.Count = 20;

            userCopy.MainRole = userCopy.MainRole with
            {
                Name = "supervisor"
            };

            fromDto.Update(userCopy);

            fromDto.Count.Should().Be(20);

            var fromModel = userCopy.ToUserDto();
            fromModel.Age.Should().Be(32);
            fromModel.FullName.Should().Be("Gil Mora, Pedro");
            fromModel.DateOfBirth.Should().Be(today);
            fromModel.TotalAmount.Should().Be(45.6m);
            fromModel.Count.Should().Be(20);
            fromModel.MainRole.id.Should().Be(0);
            fromModel.MainRole.name.Should().Be("supervisor");
        }

        //private static void GetRootAndModel(string code, out CSharpCompilation compilation, out SyntaxNode root, out SemanticModel model, params Type[] assemblies)
        //{
        //    SyntaxTree tree = CSharpSyntaxTree.ParseText(code);

        //    root = tree.GetRoot();

        //    compilation = CSharpCompilation
        //        .Create(
        //            "Temp",
        //            new[] { tree },
        //            assemblies
        //                .Select(a => a.Assembly.Location)
        //                .Append(typeof(object).Assembly.Location)
        //                .Distinct()
        //                .Select(r => MetadataReference.CreateFromFile(r))
        //                .ToImmutableArray());

        //    model = compilation.GetSemanticModel(tree);
        //}
    }
}
namespace SourceCrafter.Mappify
{
    public static partial class Mappings
    {
        static void Test()
        {
            //-Unsafe.As<IEmail, Email>(ref target.GetMainEmail())

        }
    }
}