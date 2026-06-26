using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace uy.federicod.dnsmanager.Security;

public static class TestAuthenticationDefaults
{
    public const string AuthenticationScheme = "Test";
}

public sealed class TestAuthenticationOptions : AuthenticationSchemeOptions
{
    public string UserName { get; set; } = "codex-test@fi365.ort.edu.uy";
    public string DisplayName { get; set; } = "Codex Test User";
}

public sealed class TestAuthenticationHandler : AuthenticationHandler<TestAuthenticationOptions>
{
    public TestAuthenticationHandler(
        IOptionsMonitor<TestAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, Options.UserName),
            new Claim("name", Options.DisplayName),
            new Claim(ClaimTypes.Email, Options.UserName)
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
