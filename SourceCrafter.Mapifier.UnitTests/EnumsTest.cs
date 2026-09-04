using SourceCrafter.Mapifier.Attributes;

//Testing utils
using Xunit;

// Analyzer 

//Testing purpose
using FluentAssertions;
using System.ComponentModel;
using FluentAssertions.Common;
using SourceCrafter.Mapifier.Constants;

[assembly: Extend<MappingKind>]

namespace SourceCrafter.Mapifier.UnitTests;

public class EnumsTest
{
    private const string 
        NotStartedDesc = "Not Started",
        StoppedDesc = "Transaction was stopped",
        StartedDesc = "Transaction has been started",
        CancelledDesc = "Transaction has been cancelled by user",
        Failure = "Transaction had an external failure";

    [Fact]
    public void TestEnums()
    {
        Status.Values.Should().BeEquivalentTo([Status.NotStarted, Status.Stopped, Status.Started, Status.Cancelled, Status.Failed]);

        Status.Descriptions.Should().BeEquivalentTo(NotStartedDesc, StoppedDesc, StartedDesc, CancelledDesc, Failure);

        Status.Names.Should().BeEquivalentTo("NotStarted", "Stopped", "Started", "Cancelled", "Failed");

        Status.Categories.Should().BeEquivalentTo("Good", "Bad");

        Status.Started.Name.Should().Be("Started");

        Status.Started.Category.Should().Be("Good");

        Status.Cancelled.TryGetCategory(out var category).Should().BeTrue();

        category.Should().Be("Bad");

        ((Status)6).TryGetCategory(out _).Should().BeFalse();

        Status.TryGetValue("Cancelled", out var status).Should().BeTrue();

        Status.Cancelled.Should().Be(status);

        Status.TryGetValue("Unknown", out _).Should().BeFalse();

        Status.IsDefined(1).Should().BeTrue();

        Status.IsDefined("Failed").Should().BeTrue();

        Status.IsDefined(5).Should().BeFalse();

        Status.IsDefined("Uknown").Should().BeFalse();

        Status.Started.TryGetName(out var name).Should().BeTrue();

        name.Should().Be("Started");

        ((Status)6).TryGetName(out _).Should().BeFalse();

        Status.Cancelled.TryGetDescription(out var desc).Should().BeTrue();

        desc.Should().Be(CancelledDesc);

        ((Status)6).TryGetDescription(out _).Should().BeFalse();
    }
    
    [Fact]
    public void TestAssemblyEnums()
    {
        MappingKind.Descriptions.Should().BeEquivalentTo("All", "Normal", "Fill");

        MappingKind.Names.Should().BeEquivalentTo("All", "Normal", "Fill");

        MappingKind.Fill.Name.Should().Be("Fill");

        MappingKind.TryGetValue("Fill", out var kind).Should().BeTrue();

        MappingKind.Fill.Should().Be(kind);

        MappingKind.TryGetValue("Unknown", out _).Should().BeFalse();

        MappingKind.IsDefined(1).Should().BeTrue();

        MappingKind.IsDefined("Fill").Should().BeTrue();

        MappingKind.IsDefined(5).Should().BeFalse();

        MappingKind.IsDefined("Uknown").Should().BeFalse();

        MappingKind.Fill.TryGetName(out var name).Should().BeTrue();

        name.Should().Be("Fill");

        ((MappingKind)6).TryGetName(out _).Should().BeFalse();

        MappingKind.Fill.TryGetDescription(out var desc).Should().BeTrue();

        desc.Should().Be("Fill");

        ((MappingKind)6).TryGetDescription(out _).Should().BeFalse();
    }
}

[Extend]
public enum Status
{
    [Category("Good")]
    NotStarted,
    [Description("Transaction was stopped")]
    Stopped,
    [Description("Transaction has been started")]
    Started,
    [Description("Transaction has been cancelled by user")]
    [Category("Bad")]
    Cancelled,
    [Description("Transaction had an external failure")]
    Failed
}