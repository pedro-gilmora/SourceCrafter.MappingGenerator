//Testing utils

// Analyzer 

//Testing purpose

//using SourceCrafter.Mapping.Attributes;

using SourceCrafter.Mapifier.Attributes;
using SourceCrafter.Mapifier.UnitTests;
using SourceCrafter.UnitTests;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Xml;

namespace SourceCrafter.Mapifier.UnitTests.Models;

public partial class UserDto
{
    public int Id { get; init; }
    public string FullName { get; set; } = null!;

    public int Count { get; set; }

    public int Age { get; set; }

    [IgnoreBind]
    public string? Unwanted { get; set; }

    public DateTime DateOfBirth { get; set; }

    //public IEnumerable<UserDto?> Asignees = [];
    public (int id, string name) MainRole { get; set; }

    public decimal TotalAmount { get; set; }
    public Dictionary<string, string> ExtendedProperties { get; set; } = [];

    public UserDto? Supervisor { get; init; }
    public IEnumerable<string> Phrases { get; set; } = Array.Empty<string>();
    public Status Status { get; }
    public Email? MainEmail { get; set; }
    public Phone? MainPhone { get; set; }
    public bool IsAvailable { get; set; }
    public Guid GlobalId { get; set; }

}
