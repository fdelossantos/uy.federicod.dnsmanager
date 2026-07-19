using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using uy.federicod.dnsmanager.logic;

namespace uy.federicod.dnsmanager.Models;

public sealed class DomainRegistrationViewModel : IValidatableObject
{
    [Required]
    [StringLength(63)]
    [RegularExpression(
        @"^(?!-)[A-Za-z0-9-]+(?<!-)$",
        ErrorMessage = "The subdomain can contain letters, numbers, and interior hyphens only.")]
    public string DomainName { get; set; } = string.Empty;

    [Required]
    public string ZoneName { get; set; } = string.Empty;

    [Required]
    public string DelegationType { get; set; } = "Hosted";

    public string HostedRecordType { get; set; } = HostedRecordRules.AddressType;

    public string? HostedTarget { get; set; }

    public string? Nameservers { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DelegationType == "Hosted")
        {
            if (!HostedRecordRules.TryNormalizeRecordType(HostedRecordType, out var normalizedType))
            {
                yield return new ValidationResult(
                    "Choose A or CNAME as the base record type.",
                    [nameof(HostedRecordType)]);
                yield break;
            }

            string baseFqdn = $"{DomainName}.{ZoneName}";
            if (!HostedRecordRules.TryNormalizeTarget(
                    normalizedType,
                    HostedTarget,
                    baseFqdn,
                    out _,
                    out var errorMessage))
            {
                yield return new ValidationResult(errorMessage, [nameof(HostedTarget)]);
            }

            yield break;
        }

        if (DelegationType == "Delegated")
        {
            var nameservers = GetNormalizedNameservers();
            if (nameservers.Count == 0)
            {
                yield return new ValidationResult(
                    "Enter at least one fully qualified name server.",
                    [nameof(Nameservers)]);
                yield break;
            }

            var invalidNameservers = nameservers
                .Where(nameserver => !HostedRecordRules.TryNormalizeHostname(nameserver, out _))
                .ToList();
            if (invalidNameservers.Count > 0)
            {
                yield return new ValidationResult(
                    $"Invalid name servers: {string.Join(", ", invalidNameservers)}.",
                    [nameof(Nameservers)]);
            }

            yield break;
        }

        yield return new ValidationResult(
            "Choose how DNS should be managed.",
            [nameof(DelegationType)]);
    }

    public IReadOnlyList<string> GetNormalizedNameservers()
    {
        return Regex.Split(Nameservers ?? string.Empty, @"[,;\s]+")
            .Select(value => value.Trim().TrimEnd('.').ToLowerInvariant())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
