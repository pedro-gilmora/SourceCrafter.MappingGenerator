//Testing utils

// Analyzer 
//using SourceCrafter.Mapping.Attributes;
//using SourceCrafter.Mapping.Constants;

//Testing purpose

using SourceCrafter.Mapifier.UnitTests;

namespace SourceCrafter.Mapifier.UnitTests.Models;

//[DefaultMap<User>]
public interface IUserPerson
{
    //[Map(nameof(WindowsUser.Name))]
    string FullName { get; set; }
    int Age { get; set; }
    //[Ignore(Ignore.Source)]
    string Unwanted { get; set; }
    DateTime DateOfBirth { get; set; }
    double Balance { get; set; }
    IEnumerable<IUserPerson> Asignees { get; set; }
    Role? MainRole { get; set; }
    Status Status { get; }
}
