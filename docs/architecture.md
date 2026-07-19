# Arquitectura y decisiones de diseno

## Objetivo

DNS Manager ofrece un laboratorio de DNS con nombres publicos reales. Los estudiantes pueden experimentar con resolucion, registros y delegacion sin asumir el costo ni la administracion de un dominio propio.

El recurso que se asigna a cada estudiante es un subdominio bajo una zona compartida. La aplicacion administra actualmente:

- `tda.lat`
- `marketplace.uy`
- `therealcake.com`

## Flujo funcional

1. El usuario inicia sesion con su identidad institucional de Entra ID.
2. Busca una etiqueta de subdominio en las zonas habilitadas.
3. La aplicacion consulta Cloudflare para determinar si ya existe un registro A o NS con ese nombre y controla el limite operativo de 200 registros por zona.
4. El usuario elige uno de dos modelos:
   - `Hosted`: el usuario elige un registro base A o CNAME y luego puede administrar registros A, CNAME, TXT y MX bajo el subdominio.
   - `Delegated`: se crean registros NS para entregar la resolucion del subdominio a nameservers indicados por el usuario.
5. MariaDB relaciona la registracion con `User.Identity.Name` y conserva metadatos de dominios, nameservers y registros.
6. Al eliminar una registracion, la aplicacion busca en Cloudflare el nombre base y sus descendientes, elimina esos registros y limpia los metadatos locales.

## Limites de responsabilidad

Cloudflare es la autoridad para el estado DNS publicado. MySQL/MariaDB no reemplaza a Cloudflare ni actua como servidor DNS: mantiene la lista de zonas que la aplicacion puede ofrecer y el contexto necesario para presentar y administrar las registraciones de los usuarios.

La identidad institucional se usa como identificador de cuenta. La interfaz enumera los dominios asociados a ese identificador y las operaciones deben conservar esa relacion al evolucionar el codigo.

## Decisiones de diseno

### Subdominios sobre zonas compartidas

Se eligieron subdominios en zonas existentes porque el objetivo es practicar DNS, no implementar un registrar. Esto mantiene el servicio gratuito para estudiantes y permite que los cambios sean visibles en Internet mediante DNS autoritativo real.

### Zonas habilitadas desde MariaDB

La tabla `Zones` contiene los Zone IDs y el indicador `Enabled`. La aplicacion no publica automaticamente todas las zonas accesibles con el token. Esta lista explicita limita el alcance funcional y permite habilitar o deshabilitar una zona sin modificar codigo.

### Dos modos de aprendizaje

`Hosted` permite practicar registros individuales desde la aplicacion. Su registro base puede ser A, para publicar una direccion IPv4, o CNAME, para apuntar a un hostname entregado por otra plataforma. `Delegated` permite practicar autoridad DNS y nameservers externos. Ambos modelos comparten la misma reserva de nombre, pero generan configuraciones distintas en Cloudflare.

El registro base A o CNAME creado para un dominio `Hosted` queda bloqueado contra borrado individual. Para retirarlo se elimina la registracion completa; esto evita dejar un dominio alojado sin su registro principal por accidente. Un CNAME base no puede coexistir con otros registros en el mismo nombre, aunque el usuario puede administrar registros en nombres descendientes.

### Cloudflare como estado efectivo y MariaDB como metadatos

Las lecturas de registros se contrastan con Cloudflare, incluyendo paginacion cuando corresponde. Las tablas locales permiten asociar recursos con usuarios y conservar metadatos, pero algunas sincronizaciones son deliberadamente `best-effort`: una falla local no debe presentarse como si hubiera revertido automaticamente un cambio que Cloudflare ya acepto.

La integracion usa `CloudFlare.Client` para operaciones habituales y REST directo para recorridos paginados, conteos y eliminacion de arboles de nombres. Cualquier refactor debe preservar paginacion y filtrado por el FQDN base.

### Token de cuenta con privilegio minimo

Produccion utiliza un **Account Owned API Token** limitado a lectura de zonas y edicion DNS sobre las tres zonas del laboratorio. Puede estar restringido por IP. El secreto se inyecta mediante configuracion externa y nunca forma parte del artefacto publicado. La gestion detallada esta en [cloudflare-account-token.md](cloudflare-account-token.md).

### Entra ID como frontera de acceso

La aplicacion aplica una politica global que exige usuario autenticado. En produccion utiliza OpenID Connect con el tenant `fi365.ort.edu.uy`; solamente la portada, la pagina de error y `/healthz` admiten acceso anonimo.

Existe un handler de autenticacion simple controlado por configuracion para smoke tests sin una cuenta universitaria. Es una herramienta operativa temporal, no una alternativa productiva.

### MVC renderizado en servidor

Se mantuvo ASP.NET Core MVC porque el producto consiste en formularios, validacion, acciones y vistas renderizadas en servidor. La separacion actual deja la orquestacion HTTP en controladores y las operaciones DNS/persistencia en el proyecto de logica.

### MySQL/MariaDB y SQL explicito

La aplicacion fue migrada desde SQL Server a una base compatible con MySQL/MariaDB para ejecutarse en AlmaLinux/cPanel. Se usa `MySqlConnector` con consultas parametrizadas, un esquema idempotente y migraciones incrementales bajo `schema/mysql/`. Produccion informo MySQL `8.0.46` el 2026-07-19; los scripts conservan compatibilidad con MariaDB. El proyecto historico `dnsmanagerdb` se conserva como referencia del origen, pero no define el esquema productivo actual.

### Kestrel detras de cPanel

Kestrel escucha solo en loopback y `systemd` mantiene el proceso. Apache/cPanel termina TLS, conserva el host y envia `X-Forwarded-Proto`; ASP.NET Core procesa forwarded headers antes de redireccion, autenticacion y generacion de URLs.

La redireccion HTTP a HTTPS se fuerza tambien en el virtual host publico. Las rutas ACME quedan exceptuadas para que AutoSSL pueda renovar el certificado sin intervencion.

## Restricciones a preservar

- El hostname productivo canonico es `dnsmanager.federicod.com`.
- La raiz `federicod.com` no aloja la aplicacion.
- Las zonas disponibles deben salir de `Zones` y no de una enumeracion irrestricta de la cuenta Cloudflare.
- El modo de autenticacion final de produccion debe ser siempre `Entra`.
- Los secretos deben permanecer fuera del repositorio y del publish output.
- La eliminacion de un dominio debe considerar tanto el FQDN base como sus registros descendientes.
- El limite de 200 registros por zona es una proteccion deliberada y no debe quitarse sin una decision operativa explicita.
