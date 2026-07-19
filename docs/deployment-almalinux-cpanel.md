# DNS Manager on AlmaLinux/cPanel

Production URL: `https://dnsmanager.federicod.com`

This document covers the production deployment. For product behavior and architecture, see [architecture.md](architecture.md). For Cloudflare credentials, permissions, validation, and rotation, see [cloudflare-account-token.md](cloudflare-account-token.md).

## Runtime

- Publish as framework-dependent `linux-x64` from `uy.federicod.dnsmanager/uy.federicod.dnsmanager.UI.csproj`; the server has ASP.NET Core Runtime 8 installed from AlmaLinux AppStream.
- Run under `systemd` as user `fdcom`. The original self-contained target is also valid, but production uses framework-dependent deployment to avoid large SCP transfers.
- Serve Kestrel on `127.0.0.1:5088` behind Apache reverse proxy.
- Redirect HTTP to HTTPS in the cPanel non-SSL vhost include, with an exception for `/.well-known/acme-challenge/` and `/.well-known/pki-validation/` so AutoSSL DCV can renew certificates.
- Set `Cache-Control: no-store` on the Apache include because cPanel nginx proxy caching can otherwise serve authenticated HTML after switching auth modes.
- Keep `dnsmanager.federicod.com` included in AutoSSL. If the certificate becomes self-signed, check `SSL get_autossl_excluded_domains` and remove `dnsmanager.federicod.com` from the excluded list before running `/usr/local/cpanel/bin/autossl_check --user fdcom`.
- Keep `Authentication__Provider=Entra` in production. Use `Test` only during controlled smoke tests.
- `appsettings.Development.json` is intentionally ignored by git and excluded from publish output; do not copy it to production.
- Use an Account Owned API Token for Cloudflare. Validate it with the account token endpoint and functional DNS operations; the user token verification endpoint is not valid for this credential type.

## Required Environment

Store production values in `/etc/dnsmanager/dnsmanager.env` with root-only permissions:

```ini
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5088
Authentication__Provider=Entra
# AzureAd__ClientSecret=...
Cloudflare__ApiKey=...
Cloudflare__UserName=token
ConnectionStrings__default=Server=localhost;Database=fdcom_dnsmanager;User ID=fdcom_dnsmanager;Password=...;SslMode=Preferred;TreatTinyAsBoolean=true;
```

`AzureAd__ClientSecret` is only required if the app registration is changed to a confidential client/auth-code flow. The deployed configuration currently challenges Entra with `response_type=id_token` for tenant `fi365.ort.edu.uy`.

`Cloudflare__ApiKey` is the secret value of the Account Owned API Token. Keep the environment file at mode `600`, owned by `root:root`, and follow [cloudflare-account-token.md](cloudflare-account-token.md) for every rotation. Do not commit the token or use `/user/tokens/verify` to validate it.

For temporary test auth only:

```ini
Authentication__Provider=Test
Authentication__Test__UserName=codex-test@fi365.ort.edu.uy
Authentication__Test__DisplayName=Codex Test User
```

Return `Authentication__Provider` to `Entra` before handing the service back.

## Database

Create the MySQL/MariaDB database and least-privilege user, then run:

```bash
mysql fdcom_dnsmanager < schema/mysql/001_create_dnsmanager.sql
```

The seed zones are `tda.lat`, `marketplace.uy`, and `therealcake.com`.

The production server reported MySQL `8.0.46` on 2026-07-19. The schema remains compatible with MariaDB through `MySqlConnector`. For an existing installation, apply incremental migrations in order; Hosted A/CNAME support requires:

```bash
mysql fdcom_dnsmanager < schema/mysql/002_add_hosted_record_type.sql
```

The migration is idempotent, adds `Domains.HostedRecordType`, backfills existing Hosted registrations to `A`, and leaves Delegated registrations with a null subtype.

## Hosted A/CNAME smoke test

Do not expose `Authentication__Provider=Test` through the public virtual host. Start a temporary instance on a different loopback port with a root-only copy of the environment file, override only the authentication provider and URL, and reach it through an SSH tunnel. Use unique A and CNAME names, verify Cloudflare and public DNS, delete both registrations through the application, and confirm that no DNS or database rows remain.

The public service must remain on `Authentication__Provider=Entra`. Remove the temporary environment file and transient unit after the test.

## Validation

```bash
systemctl status dnsmanager --no-pager
curl -i https://dnsmanager.federicod.com/healthz
curl -i https://federicod.com/
```

Protected routes must redirect to the Universidad ORT Entra tenant when `Authentication__Provider=Entra`.
