using CloudFlare.Client.Enumerators;
using uy.federicod.dnsmanager.logic;

namespace uy.federicod.dnsmanager.tests;

public sealed class HostedRecordRulesTests
{
    [Fact]
    public void TryNormalizeTarget_NormalizesIpv4Address()
    {
        bool valid = HostedRecordRules.TryNormalizeTarget(
            "A",
            " 203.0.113.10 ",
            "student.tda.lat",
            out var normalizedTarget,
            out var errorMessage);

        Assert.True(valid, errorMessage);
        Assert.Equal("203.0.113.10", normalizedTarget);
    }

    [Fact]
    public void TryNormalizeTarget_RejectsIpv6ForARecord()
    {
        bool valid = HostedRecordRules.TryNormalizeTarget(
            "A",
            "2001:db8::10",
            "student.tda.lat",
            out _,
            out var errorMessage);

        Assert.False(valid);
        Assert.Contains("IPv4", errorMessage);
    }

    [Fact]
    public void TryNormalizeTarget_NormalizesCnameHostname()
    {
        bool valid = HostedRecordRules.TryNormalizeTarget(
            "cname",
            " App.Platform.Example. ",
            "student.tda.lat",
            out var normalizedTarget,
            out var errorMessage);

        Assert.True(valid, errorMessage);
        Assert.Equal("app.platform.example", normalizedTarget);
    }

    [Theory]
    [InlineData("https://app.platform.example")]
    [InlineData("app.platform.example/path")]
    [InlineData("app.platform.example:443")]
    [InlineData("203.0.113.10")]
    public void TryNormalizeTarget_RejectsInvalidCnameTargets(string target)
    {
        bool valid = HostedRecordRules.TryNormalizeTarget(
            "CNAME",
            target,
            "student.tda.lat",
            out _,
            out _);

        Assert.False(valid);
    }

    [Fact]
    public void TryNormalizeTarget_RejectsCnameSelfReference()
    {
        bool valid = HostedRecordRules.TryNormalizeTarget(
            "CNAME",
            "STUDENT.TDA.LAT.",
            "student.tda.lat",
            out _,
            out var errorMessage);

        Assert.False(valid);
        Assert.Contains("own hostname", errorMessage);
    }

    [Theory]
    [InlineData("A", DnsRecordType.A, "203.0.113.10")]
    [InlineData("CNAME", DnsRecordType.Cname, "app.platform.example")]
    public void BuildBaseRecord_CreatesExpectedCloudflareRecord(
        string recordType,
        DnsRecordType expectedType,
        string target)
    {
        var record = HostedRecordRules.BuildBaseRecord(
            "student",
            recordType,
            target,
            "student@example.edu");

        Assert.Equal(expectedType, record.Type);
        Assert.Equal("student", record.Name);
        Assert.Equal(target, record.Content);
        Assert.False(record.Proxied);
        Assert.Equal(1, record.Ttl);
        Assert.Equal("student@example.edu", record.Comment);
    }

    [Theory]
    [InlineData("A", "student.tda.lat", true)]
    [InlineData("CNAME", "STUDENT.TDA.LAT.", true)]
    [InlineData("TXT", "student.tda.lat", false)]
    [InlineData("CNAME", "www.student.tda.lat", false)]
    public void IsBaseRecord_DetectsOnlyInitialAOrCname(
        string recordType,
        string recordName,
        bool expected)
    {
        bool actual = HostedRecordRules.IsBaseRecord(
            recordType,
            recordName,
            "student.tda.lat");

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("student.tda.lat", true)]
    [InlineData("www.student.tda.lat", true)]
    [InlineData("other.tda.lat", false)]
    [InlineData("notstudent.tda.lat", false)]
    public void IsWithinDomainTree_RestrictsRecordsToOwnedHostname(string recordName, bool expected)
    {
        bool actual = HostedRecordRules.IsWithinDomainTree(recordName, "student.tda.lat");

        Assert.Equal(expected, actual);
    }
}
