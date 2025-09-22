//Testing utils
using Xunit;

// Analyzer 

//Testing purpose
using FluentAssertions;
using System.ComponentModel;
using FluentAssertions.Common;
using SourceCrafter.Mappify.Attributes;
using SourceCrafter.Mappify;
using SourceCrafter.UnitTests;

[assembly: Extend<MappingKind>]

namespace SourceCrafter.Bindings.UnitTests;

public class EnumsTest
{
    private const string 
        NotStartedDesc = "Not Started",
        StoppedDesc = "Transaction was stopped",
        StartedDesc = "Transaction has been started",
        CancelledDesc = "Transaction has been cancelled by user",
        Failure = "Transaction had an external failure";

    [Fact]
    public void TestEnums_0()
    {
        MappingKind.Values.ToArray().Should().BeEquivalentTo([MappingKind.All, MappingKind.Normal, MappingKind.Fill]);

        MappingKind.Names.ToArray().Should().BeEquivalentTo([nameof(MappingKind.All), nameof(MappingKind.Normal), nameof(MappingKind.Fill)]);

        MappingKind.Fill.Name.Should().Be(nameof(MappingKind.Fill));

        MappingKind.TryGetValue(nameof(MappingKind.Fill), out var kind).Should().BeTrue();

        MappingKind.Fill.Should().Be(kind);

        MappingKind.TryGetValue("Unknown", out _).Should().BeFalse();

        MappingKind.IsDefined(1).Should().BeTrue();

        MappingKind.IsDefined(nameof(MappingKind.Fill)).Should().BeTrue();

        MappingKind.IsDefined(5).Should().BeFalse();

        MappingKind.IsDefined("Uknown").Should().BeFalse();

        MappingKind.Fill.TryGetName(out var name).Should().BeTrue();

        name.Should().Be(nameof(MappingKind.Fill));

        ((MappingKind)6).TryGetName(out _).Should().BeFalse();

        MappingKind.Fill.TryGetDescription(out var desc).Should().BeTrue();

        desc.Should().Be(nameof(MappingKind.Fill));

        ((MappingKind)6).TryGetDescription(out _).Should().BeFalse();
    }
    [Fact]
    public void TestEnums_1()
    {
        Status.Values.ToArray().Should().BeEquivalentTo([Status.NotStarted, Status.Stopped, Status.Started, Status.Cancelled, Status.Failed]);

        Status.Descriptions.ToArray().Should().BeEquivalentTo([NotStartedDesc, StoppedDesc, StartedDesc, CancelledDesc, Failure]);

        Status.Names.ToArray().Should().BeEquivalentTo([nameof(Status.NotStarted), nameof(Status.Stopped), nameof(Status.Started), nameof(Status.Cancelled), nameof(Status.Failed)]);

        Status.Started.Name.Should().Be(nameof(Status.Started));

        Status.TryGetValue(nameof(Status.Cancelled), out var status).Should().BeTrue();

        Status.Cancelled.Should().Be(status);

        Status.TryGetValue("Unknown", out _).Should().BeFalse();

        Status.IsDefined(1).Should().BeTrue();

        Status.IsDefined(nameof(Status.Failed)).Should().BeTrue();

        Status.IsDefined(5).Should().BeFalse();

        Status.IsDefined("Uknown").Should().BeFalse();

        Status.Started.TryGetName(out var name).Should().BeTrue();

        name.Should().Be(nameof(Status.Started));

        ((Status)6).TryGetName(out _).Should().BeFalse();

        Status.Cancelled.TryGetDescription(out var desc).Should().BeTrue();

        desc.Should().Be(CancelledDesc);

        ((Status)6).TryGetDescription(out _).Should().BeFalse();
    }
}