# DNS Manager on AlmaLinux/cPanel

Production URL: `https://dnsmanager.federicod.com`

## Runtime

- Publish as framework-dependent `linux-x64` from `uy.federicod.dnsmanager/uy.federicod.dnsmanager.UI.csproj`; the server has ASP.NET Core Runtime 8 installed from AlmaLinux AppStream.
- Run under `systemd` as user `fdcom`. The original self-contained target is also valid, but production uses framework-dependent deployment to avoid large SCP transfers.
- Serve Kestrel on `127.0.0.1:5088` behind Apache reverse proxy.
- Redirect HTTP to HTTPS in the cPanel non-SSL vhost include, with an exception for `/.well-known/acme-challenge/` and `/.well-known/pki-validation/` so AutoSSL DCV can renew certificates.
- Set `Cache-Control: no-store` on the Apache include because cPanel nginx proxy caching can otherwise serve authenticated HTML after switching auth modes.
- Keep `dnsmanager.federicod.com` included in AutoSSL. If the certificate becomes self-signed, check `SSL get_autossl_excluded_domains` and remove `dnsmanager.federicod.com` from the excluded list before running `/usr/local/cpanel/bin/autossl_check --user fdcom`.
- Keep `Authentication__Provider=Entra` in production. Use `Test` only during controlled smoke tests.
- `appsettings.Development.json` is intentionally ignored by git and excluded from publish output; do not copy it to production.

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

For temporary test auth only:

```ini
Authentication__Provider=Test
Authentication__Test__UserName=codex-test@fi365.ort.edu.uy
Authentication__Test__DisplayName=Codex Test User
```

Return `Authentication__Provider` to `Entra` before handing the service back.

## Database

Create the MariaDB database and least-privilege user, then run:

```bash
mysql fdcom_dnsmanager < schema/mysql/001_create_dnsmanager.sql
```

The seed zones are `tda.lat`, `marketplace.uy`, and `therealcake.com`.

## Validation

```bash
systemctl status dnsmanager --no-pager
curl -i https://dnsmanager.federicod.com/healthz
curl -i https://federicod.com/
```

Protected routes must redirect to the Universidad ORT Entra tenant when `Authentication__Provider=Entra`.

## Federicod.net

`dnsmanager.federicod.net` cannot be issued by AutoSSL until `federicod.net` exists in DNS and belongs to the `fdcom` cPanel account. Current checks showed `federicod.net` and `dnsmanager.federicod.net` as NXDOMAIN, and cPanel rejected the domain because it could not determine nameservers.
