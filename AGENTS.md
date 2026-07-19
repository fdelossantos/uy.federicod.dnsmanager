# Instrucciones para agentes

## Contexto obligatorio

Antes de modificar o desplegar este proyecto, leer:

1. `README.md`
2. `docs/architecture.md`
3. `docs/deployment-almalinux-cpanel.md`
4. `docs/cloudflare-account-token.md`

El hostname productivo canonico es `dnsmanager.federicod.com`. Mantener ese valor en documentacion, configuracion de proxy, Entra ID, pruebas y runbooks.

## Reglas de seguridad

- Produccion usa un **Account Owned API Token** de Cloudflare, no un token de usuario.
- Validar el token con `/accounts/{account_id}/tokens/verify` y despues con operaciones reales sobre zonas y registros desde el servidor.
- No considerar suficiente `/user/tokens/verify`; es el endpoint del tipo de token incorrecto para este proyecto.
- No imprimir, commitear ni incluir secretos en artefactos. Las huellas parciales son aceptables para comparar valores.
- Tratar el archivo secreto suministrado como valor literal: quitar solo BOM y whitespace exterior; no confundir el secreto con el nombre o ID del token.
- No reemplazar `/etc/dnsmanager/dnsmanager.env` sin backup fechado y permisos root-only.
- No dejar archivos temporales ni registros TXT de validacion despues de rotar un token.
- No dejar `Authentication__Provider=Test` activo al finalizar una prueba.

## Contratos del sistema

- Cloudflare es el estado DNS efectivo; MariaDB conserva zonas permitidas, cuentas, registraciones y metadatos.
- Las zonas ofrecidas deben provenir de la tabla `Zones` y limitarse al conjunto aprobado.
- Preservar la asociacion de las operaciones con la identidad Entra del usuario.
- Preservar ambos modos de registracion: `Hosted` y `Delegated`.
- Preservar paginacion al listar o eliminar registros de Cloudflare.
- El registro A base de un dominio `Hosted` no debe poder borrarse individualmente.
- La eliminacion de una registracion debe limpiar el nombre base, descendientes y metadatos relacionados.
- Mantener `UseAuthentication()` antes de `UseAuthorization()` y forwarded headers antes de logica dependiente del esquema HTTPS.

## Cierre de cambios

Como minimo:

```powershell
dotnet build uy.federicod.dnsmanager/uy.federicod.dnsmanager.UI.csproj
rg -n -i "federicod\." README.md AGENTS.md docs deploy uy.federicod.dnsmanager
git diff --check
```

Si hay despliegue, validar tambien:

- `systemctl status dnsmanager`;
- `https://dnsmanager.federicod.com/healthz` con estado `200`;
- redireccion de HTTP a HTTPS;
- una ruta protegida con `302` hacia Entra y callback `https://dnsmanager.federicod.com/signin-oidc`;
- autenticacion productiva restaurada a `Entra`.

No modificar ni revertir cambios locales ajenos. Cuando el pedido incluya dejar el trabajo sincronizado, commitear solamente archivos sin secretos y hacer push de la rama correspondiente.
