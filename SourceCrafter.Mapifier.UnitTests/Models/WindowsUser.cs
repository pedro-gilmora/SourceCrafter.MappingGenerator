using SourceCrafter.Mapifier.Attributes;
using SourceCrafter.Mapifier.Constants;
using SourceCrafter.UnitTests;

namespace SourceCrafter.Mapifier.UnitTests.Models;

public partial class WindowsUser
{
    [Bind(nameof(User.FullName), IgnoreBind.Source)]
    [Bind(nameof(UserDto.FullName))]
    public string Name { get; set; } = null!;
    public bool IsAuthenticated { get; set; }
}
