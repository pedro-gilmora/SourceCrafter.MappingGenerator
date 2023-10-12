﻿//Testing utils

// Analyzer 
using SourceCrafter.Mapping.Attributes;
using SourceCrafter.Mapping.Constants;

//Testing purpose

namespace SourceCrafter.UnitTests;

//[DefaultMap<User>]
public interface IUser
{
    [Map(nameof(WindowsUser.Name))]
    string FullName { get; set; }
    int Age { get; set; }

    [Ignore(Ignore.Source)]
    string? Unwanted { get; set; }
    DateTime DateOfBirth { get; set; }
    double Balance { get; set; }
    IUser[] Roles { get; set; }
    Role? MainRole { get; set; }
}
