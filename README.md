# DNS Manager

DNS Manager es una aplicacion educativa para que estudiantes de la Universidad ORT Uruguay practiquen la publicacion y administracion de DNS sin tener que comprar un dominio. Cada usuario autenticado puede buscar y registrar un subdominio dentro de una de las zonas habilitadas por la institucion.

Produccion: `https://dnsmanager.federicod.com`

## Que permite hacer

- Autenticarse con el tenant Entra ID `fi365.ort.edu.uy`.
- Buscar un subdominio disponible en `tda.lat`, `marketplace.uy` o `therealcake.com`.
- Registrar un subdominio en modo `Hosted`, eligiendo un registro base A o CNAME administrado desde la aplicacion.
- Registrar un subdominio en modo `Delegated`, publicando registros NS hacia nameservers externos.
- Administrar registros A, CNAME, TXT y MX dentro de un subdominio alojado.
- Agregar o quitar nameservers en una delegacion.
- Eliminar el subdominio y sus registros asociados de Cloudflare y MariaDB.

La aplicacion no registra dominios ante un registrar. Entrega subdominios dentro de zonas compartidas que ya son administradas por Cloudflare.

## Arquitectura

| Componente | Implementacion |
| --- | --- |
| Interfaz | ASP.NET Core MVC sobre .NET 8, con vistas Razor |
| Identidad | Microsoft Entra ID mediante OpenID Connect y `Microsoft.Identity.Web` |
| Logica DNS | Proyecto `uy.federicod.dnsmanager.logic`, usando `CloudFlare.Client` y llamadas REST puntuales |
| DNS autoritativo | Cloudflare para las tres zonas habilitadas |
| Persistencia | MySQL/MariaDB mediante `MySqlConnector` y SQL parametrizado |
| Produccion | Kestrel en `127.0.0.1:5088`, administrado por `systemd` y publicado por Apache/cPanel |
| TLS | Let's Encrypt mediante AutoSSL, con redireccion publica permanente a HTTPS |

Cloudflare conserva el estado DNS efectivo. MySQL/MariaDB registra usuarios, zonas habilitadas, propiedad logica de los subdominios, el tipo de registro base Hosted y metadatos auxiliares. La lista de zonas ofrecidas por la interfaz sale de la tabla `Zones`, lo que evita exponer automaticamente cualquier otra zona visible para el token.

Las decisiones de arquitectura y sus motivos estan detalladas en [docs/architecture.md](docs/architecture.md).

## Estructura del repositorio

- `uy.federicod.dnsmanager/`: aplicacion MVC, autenticacion, controladores y vistas.
- `uy.federicod.dnsmanager.logic/`: reglas de dominios, acceso a Cloudflare y persistencia MariaDB.
- `schema/mysql/`: esquema, seed y migraciones reproducibles de la base productiva.
- `deploy/almalinux/`: unidad `systemd` e include de Apache usados como referencia.
- `docs/deployment-almalinux-cpanel.md`: despliegue y operacion de produccion.
- `docs/cloudflare-account-token.md`: permisos, validacion y rotacion del token de Cloudflare.
- `AGENTS.md`: contrato operativo para agentes que modifiquen o desplieguen el proyecto.

## Desarrollo

Requisitos:

- .NET SDK 8.
- MySQL/MariaDB con el esquema de `schema/mysql/001_create_dnsmanager.sql`.
- Configuracion local fuera del control de versiones, preferentemente mediante Secret Manager o `appsettings.Development.json` ignorado.

Compilar la aplicacion web:

```powershell
dotnet build uy.federicod.dnsmanager/uy.federicod.dnsmanager.UI.csproj
```

La solucion tambien conserva el proyecto historico de SQL Server. Para validar el runtime actual se debe compilar el `.csproj` web, ya que el `.sqlproj` puede requerir herramientas SSDT que no forman parte del entorno .NET normal.

## Seguridad y configuracion

- La autenticacion productiva siempre debe quedar en `Authentication__Provider=Entra`.
- `Authentication__Provider=Test` existe exclusivamente para pruebas controladas y debe deshabilitarse antes de terminar cualquier intervencion.
- Ningun token, password o client secret debe guardarse en el repositorio ni aparecer en logs o salidas de comandos.
- Produccion utiliza un **Account Owned API Token** de Cloudflare. No es un token de usuario; su procedimiento correcto esta en [docs/cloudflare-account-token.md](docs/cloudflare-account-token.md).
- El callback registrado en Entra ID debe ser `https://dnsmanager.federicod.com/signin-oidc`.

El endpoint anonimo `/healthz` sirve como liveness basico. Las rutas funcionales requieren autenticacion.
