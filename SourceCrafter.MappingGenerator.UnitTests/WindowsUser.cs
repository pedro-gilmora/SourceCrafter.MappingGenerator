using SourceCrafter.Mappify.Attributes;
using SourceCrafter.Mappify;

namespace SourceCrafter.UnitTests;

public partial class WindowsUser
{
    [Map(nameof(User.FullName), ApplyTo.Source)]
    [Map(nameof(UserDto.FullName))]
    public string Name { get; set; } = null!;
    public bool IsAuthenticated { get; set; }
}
