namespace uy.federicod.dnsmanager.logic.Models;

public sealed class DomainRegistrationRequest
{
    public string DomainName { get; init; } = string.Empty;
    public string ZoneId { get; init; } = string.Empty;
    public string ZoneName { get; init; } = string.Empty;
    public string DelegationType { get; init; } = "Hosted";
    public string? HostedRecordType { get; init; }
    public string? HostedTarget { get; init; }
    public IReadOnlyList<string> NameServers { get; init; } = [];
}
