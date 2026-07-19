using System.Net;
using System.Net.Sockets;
using CloudFlare.Client.Api.Zones.DnsRecord;
using CloudFlare.Client.Enumerators;

namespace uy.federicod.dnsmanager.logic;

public static class HostedRecordRules
{
    public const string AddressType = "A";
    public const string CnameType = "CNAME";

    public static bool TryNormalizeRecordType(string? value, out string normalizedType)
    {
        normalizedType = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalizedType is AddressType or CnameType;
    }

    public static bool TryNormalizeTarget(
        string? recordType,
        string? value,
        string baseFqdn,
        out string normalizedTarget,
        out string errorMessage)
    {
        normalizedTarget = string.Empty;

        if (!TryNormalizeRecordType(recordType, out var normalizedType))
        {
            errorMessage = "Choose A or CNAME as the base record type.";
            return false;
        }

        if (normalizedType == AddressType)
        {
            if (!IPAddress.TryParse(value?.Trim(), out var address) ||
                address.AddressFamily != AddressFamily.InterNetwork)
            {
                errorMessage = "Enter a valid IPv4 address for the A record.";
                return false;
            }

            normalizedTarget = address.ToString();
            errorMessage = string.Empty;
            return true;
        }

        if (!TryNormalizeHostname(value, out normalizedTarget))
        {
            errorMessage = "Enter a valid fully qualified hostname without a scheme, port, or path.";
            return false;
        }

        if (string.Equals(normalizedTarget, TrimTrailingDot(baseFqdn), StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "A CNAME cannot point to its own hostname.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    public static bool TryNormalizeHostname(string? value, out string normalizedHostname)
    {
        normalizedHostname = TrimTrailingDot(value).ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalizedHostname) ||
            normalizedHostname.Length > 253 ||
            !normalizedHostname.Contains('.') ||
            normalizedHostname.Any(character => character > 127) ||
            IPAddress.TryParse(normalizedHostname, out _))
        {
            return false;
        }

        var labels = normalizedHostname.Split('.');
        return labels.All(label =>
            label.Length is >= 1 and <= 63 &&
            char.IsLetterOrDigit(label[0]) &&
            char.IsLetterOrDigit(label[^1]) &&
            label.All(character => char.IsLetterOrDigit(character) || character == '-'));
    }

    public static bool IsBaseRecord(string? recordType, string? recordName, string baseFqdn)
    {
        return TryNormalizeRecordType(recordType, out _) &&
               string.Equals(
                   TrimTrailingDot(recordName),
                   TrimTrailingDot(baseFqdn),
                   StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsWithinDomainTree(string? recordName, string baseFqdn)
    {
        string normalizedName = TrimTrailingDot(recordName);
        string normalizedBase = TrimTrailingDot(baseFqdn);
        return string.Equals(normalizedName, normalizedBase, StringComparison.OrdinalIgnoreCase) ||
               normalizedName.EndsWith($".{normalizedBase}", StringComparison.OrdinalIgnoreCase);
    }

    public static NewDnsRecord BuildBaseRecord(
        string domainName,
        string recordType,
        string normalizedTarget,
        string accountId)
    {
        if (!TryNormalizeRecordType(recordType, out var normalizedType))
        {
            throw new ArgumentException("Unsupported hosted record type.", nameof(recordType));
        }

        return new NewDnsRecord
        {
            Name = domainName,
            Content = normalizedTarget,
            Priority = 0,
            Proxied = false,
            Ttl = 1,
            Type = normalizedType == AddressType ? DnsRecordType.A : DnsRecordType.Cname,
            Comment = accountId
        };
    }

    private static string TrimTrailingDot(string? value)
    {
        return (value ?? string.Empty).Trim().TrimEnd('.');
    }
}
