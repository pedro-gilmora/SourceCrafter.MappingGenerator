using SourceCrafter.Mapifier;
using SourceCrafter.Mapifier.Attributes;
using SourceCrafter.Mapifier.UnitTests;
using SourceCrafter.Mapifier.UnitTests.Models;
using SourceCrafter.UnitTests;

[assembly:
    //Bind<(string, object)[], Dictionary<string, string>>,
    Bind<IImplement<IAppUser, AppUser>, MeAsUser>,
    Bind<User, UserDto>
]
