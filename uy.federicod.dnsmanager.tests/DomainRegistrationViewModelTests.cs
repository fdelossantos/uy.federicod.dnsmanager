using System.ComponentModel.DataAnnotations;
using uy.federicod.dnsmanager.Models;

namespace uy.federicod.dnsmanager.tests;

public sealed class DomainRegistrationViewModelTests
{
    [Fact]
    public void Validate_AHostedRegistration_AcceptsIpv4()
    {
        var model = CreateHostedModel("A", "203.0.113.10");

        Assert.Empty(Validate(model));
    }

    [Fact]
    public void Validate_CnameHostedRegistration_AcceptsHostname()
    {
        var model = CreateHostedModel("CNAME", "app.platform.example");

        Assert.Empty(Validate(model));
    }

    [Fact]
    public void Validate_CnameHostedRegistration_RejectsSelfReference()
    {
        var model = CreateHostedModel("CNAME", "student.tda.lat");

        Assert.Contains(
            Validate(model),
            result => result.MemberNames.Contains(nameof(model.HostedTarget)));
    }

    [Fact]
    public void Validate_DelegatedRegistration_RequiresValidNameservers()
    {
        var model = new DomainRegistrationViewModel
        {
            DomainName = "student",
            ZoneName = "tda.lat",
            DelegationType = "Delegated",
            Nameservers = "not-a-fqdn"
        };

        Assert.Contains(
            Validate(model),
            result => result.MemberNames.Contains(nameof(model.Nameservers)));
    }

    [Fact]
    public void GetNormalizedNameservers_NormalizesAndRemovesDuplicates()
    {
        var model = new DomainRegistrationViewModel
        {
            Nameservers = "NS1.PROVIDER.EXAMPLE.\nns1.provider.example; ns2.provider.example"
        };

        Assert.Equal(
            ["ns1.provider.example", "ns2.provider.example"],
            model.GetNormalizedNameservers());
    }

    private static DomainRegistrationViewModel CreateHostedModel(string recordType, string target)
    {
        return new DomainRegistrationViewModel
        {
            DomainName = "student",
            ZoneName = "tda.lat",
            DelegationType = "Hosted",
            HostedRecordType = recordType,
            HostedTarget = target
        };
    }

    private static IReadOnlyList<ValidationResult> Validate(DomainRegistrationViewModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, true);
        return results;
    }
}
