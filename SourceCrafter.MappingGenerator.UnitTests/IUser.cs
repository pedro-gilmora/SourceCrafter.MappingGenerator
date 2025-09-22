//Testing utils

// Analyzer 
//using SourceCrafter.Mapping.Attributes;
//using SourceCrafter.Mapping.Constants;

//Testing purpose

//using SourceCrafter.Mappify.UnitTests;

using SourceCrafter.Mappify.Attributes;

using System.ComponentModel;

namespace SourceCrafter.UnitTests;

//[DefaultMap<User>]
public interface IUserPerson
{
    //[Map(nameof(WindowsUser.Name))]
    string FullName { get; set; }
    int Age { get; set; }
    // [Ignore]
    string Unwanted { get; set; }
    DateTime DateOfBirth { get; set; }
    double Balance { get; set; }
    IEnumerable<IUserPerson> Asignees { get; set; }
    Role? MainRole { get; set; }
    Status Status { get; }
}


[Extend]
[Flags]
public enum Status
{
    NotStarted,
    [Description("Transaction was stopped")]
    Stopped,
    [Description("Transaction has been started")]
    Started,
    [Description("Transaction has been cancelled by user")]
    Cancelled,
    [Description("Transaction had an external failure")]
    Failed
}