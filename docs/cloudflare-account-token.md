# Token de cuenta de Cloudflare

## Contrato productivo

DNS Manager utiliza un **Account Owned API Token** de Cloudflare. Este detalle es importante: un token de cuenta se valida con:

```text
GET /client/v4/accounts/{account_id}/tokens/verify
```

No se debe usar `GET /client/v4/user/tokens/verify` para decidir que el token productivo es invalido. Ese endpoint corresponde a tokens de usuario y puede devolver `401 Invalid API Token` aunque el token de cuenta funcione correctamente.

El token actual se consume como Bearer token mediante `Cloudflare__ApiKey`. `Cloudflare__UserName=token` se conserva por compatibilidad con la configuracion existente, pero no participa en la autenticacion Bearer.

## Alcance requerido

El token debe tener el minimo alcance que permita:

- leer las zonas habilitadas;
- listar, crear y eliminar registros DNS;
- operar exclusivamente sobre `tda.lat`, `marketplace.uy` y `therealcake.com`.

En el panel de Cloudflare esto corresponde a permisos equivalentes a `Zone Read` y `DNS Edit`, aplicados solo a esas zonas. Si se configura Client IP Address Filtering, la IP publica de salida observada del servidor debe estar incluida como una regla CIDR `/32`. La IP debe confirmarse desde el propio servidor antes de crear o rotar el token:

```bash
curl -fsS https://api.ipify.org
```

El 2026-07-19 la salida productiva observada fue `51.81.34.181`, por lo que la regla usada fue `51.81.34.181/32`. Este dato puede cambiar y nunca debe reutilizarse sin ejecutar la comprobacion anterior desde el servidor.

La verificacion de token no prueba el filtro de IP. Para validar esa restriccion hay que ejecutar una operacion real de zonas o registros desde el servidor productivo.

## Ubicacion del secreto

El valor no se versiona. En produccion vive en:

```text
/etc/dnsmanager/dnsmanager.env
```

La entrada es:

```ini
Cloudflare__ApiKey=<secret>
```

Cloudflare muestra el valor secreto al crear o rotar el token. Ese valor no es el nombre descriptivo ni el token ID. Los agentes deben usar el contenido completo del archivo secreto suministrado, quitando solamente BOM y whitespace exterior; no deben intentar extraerlo por longitud, prefijo o etiquetas supuestas.

El archivo debe pertenecer a `root:root`, tener modo `600` y ser leido por la unidad `systemd` mediante `EnvironmentFile`. Los archivos temporales usados durante una rotacion tambien deben ser root-only y eliminarse al terminar.

No se debe:

- imprimir el token en consola, logs, diffs o respuestas de agentes;
- incluirlo en `appsettings.json`, scripts versionados o artefactos de publish;
- pasarlo como argumento visible de procesos si existe una alternativa razonable;
- reemplazar produccion antes de validar lectura y escritura desde el servidor.

Para comparar valores sin revelarlos se puede informar una huella SHA-256 parcial. La huella no sustituye la validacion contra Cloudflare.

## Validacion

Una rotacion se considera valida solamente cuando se comprueba desde el servidor:

1. `GET /accounts/{account_id}/tokens/verify` responde `200`, `success=true` y `status=active`.
2. `GET /zones?name={zone}` encuentra cada una de las tres zonas esperadas.
3. `GET /zones/{zone_id}/dns_records?per_page=1` responde correctamente para cada zona.
4. Se crea y elimina inmediatamente un TXT temporal en cada zona para comprobar `DNS Edit`.
5. No queda ningun registro ni archivo temporal despues de la prueba.

El TXT de prueba debe usar un nombre claramente efimero, por ejemplo `_dnsmanager-token-check-{timestamp}`, y contenido no sensible. Se debe conservar el record ID devuelto por Cloudflare para eliminar exactamente el registro creado.

## Rotacion segura

1. Crear un nuevo Account Owned API Token con el alcance e IP permitida descritos arriba.
2. Guardar el secreto fuera del repositorio en un archivo de una sola linea y permisos restrictivos.
3. Transferirlo al servidor a un archivo temporal root-only sin mostrar su contenido.
4. Ejecutar todas las pruebas de validacion anteriores usando el temporal.
5. Crear un backup fechado de `/etc/dnsmanager/dnsmanager.env`.
6. Reemplazar unicamente el valor de `Cloudflare__ApiKey`, preservando el resto del archivo y sus permisos.
7. Eliminar el temporal y ejecutar `systemctl restart dnsmanager`.
8. Verificar el servicio, el endpoint publico y el redirect de autenticacion.
9. Conservar el backup root-only durante la ventana de observacion y retirarlo despues segun la politica operativa del servidor.

Comprobaciones posteriores:

```bash
systemctl status dnsmanager --no-pager
curl -fsS http://127.0.0.1:5088/healthz
curl -i https://dnsmanager.federicod.com/healthz
```

La huella del valor instalado debe coincidir con la del valor validado. Si el servicio no vuelve a estar saludable, se restaura el backup y se investigan los logs antes de un nuevo intento.

## Diagnostico rapido

| Sintoma | Interpretacion y siguiente paso |
| --- | --- |
| `/user/tokens/verify` devuelve `401` | No prueba que este token de cuenta sea invalido; usar el endpoint de cuenta. |
| El endpoint de cuenta indica `disabled` o `expired` | Crear o reactivar un token antes de modificar produccion. |
| Verify funciona pero `/zones` falla desde el servidor | Revisar filtro de IP, recursos asignados y permiso de lectura de zona. |
| Se listan zonas pero no se puede crear TXT | Falta `DNS Edit` o la zona no esta incluida en el alcance. |
| API directa funciona pero la aplicacion falla | Comparar la huella instalada, reiniciar `systemd` y revisar `journalctl -u dnsmanager`. |
| Una zona esperada no aparece | No cambiar el seed a ciegas; comprobar que la zona pertenece a la cuenta y fue incluida en el token. |

## Referencias de Cloudflare

- [Verify Account Token](https://developers.cloudflare.com/api/resources/accounts/subresources/tokens/methods/verify/)
- [Restrict tokens](https://developers.cloudflare.com/fundamentals/api/how-to/restrict-tokens/)
- [Create an API token](https://developers.cloudflare.com/fundamentals/api/get-started/create-token/)
