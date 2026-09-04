//Testing utils

// Analyzer 

//Testing purpose

namespace SourceCrafter.Mapifier.UnitTests.Models;

public struct Role
{
    public int Id { get; set; }
    public string Name { get; set; }

    public Role(int id, string name) => (Id, Name) = (id, name);

    //public static explicit operator Role((int id, string name) role)
    //    => new() { Id = role.id, Name = role.name };

    //public static explicit operator Role?((int id, string name)? role)
    //    => role is { } a ? new() { Id = a.id, Name = a.name } : null;

    //public static explicit operator (int, string)(Role role)
    //    => (role.Id, role.Name);

    //public static explicit operator (int, string)?(Role? role)
    //	=> role is {} r ? (r.Id, r.Name) : null;
}