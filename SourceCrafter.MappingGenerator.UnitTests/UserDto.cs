using SourceCrafter.Mappify.Attributes;

namespace SourceCrafter.UnitTests;

public partial class UserDto
{
    public int Id { get; init; }

    public string FullName { get; set; } = null!;

    public int Count { get; set; }

    public int Age { get; set; }

    // [Ignore]
    public string? Unwanted { get; set; }

    public DateTime DateOfBirth { get; set; }

    //public IEnumerable<UserDto?> Asignees = [];
    public (int id, string name) MainRole { get; set; }

    public decimal TotalAmount { get; set; }

    public List<UserDto> Subordinated { get; init; } = [];
    public List<(string id, string item)> ExtendedProperties { get; set; } = [];

    [Max(2)]
    public UserDto? Supervisor { get; init; }

    public IEnumerable<string> Phrases { get; set; } = Array.Empty<string>();

    public Status Status { get; }

    public Email? MainEmail { get; set; }

    public Phone? MainPhone { get; set; }

    public bool IsAvailable { get; set; }

    public Guid GlobalId { get; set; }

}
