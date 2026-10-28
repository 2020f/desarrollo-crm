# Despliegue del CRM en Ubuntu con Docker

Guía para una PC **Ubuntu 24.04 LTS de 64 bits, Intel/AMD x86-64**, con Docker Engine y un túnel de Cloudflare. Ubuntu Desktop sirve: la PC debe permanecer encendida, conectada y sin suspensión mientras se utilice el CRM.

La aplicación es un monolito ASP.NET Core 10 + React; SQL Server 2025 conserva los registros. Docker publica únicamente `127.0.0.1:5080`; el túnel publica el subdominio HTTPS. SQL Server no expone un puerto al host.

Esta guía prepara el despliegue. La publicación del código en GitHub y las pruebas locales no significan que tu PC Ubuntu o tu subdominio ya estén desplegados. El alcance funcional y los pendientes del producto están en [la matriz de requisitos](docs/matriz-requisitos.md).

## 1. Preparar la PC

- Instala Docker Engine y el plugin Compose siguiendo [la guía oficial para Ubuntu](https://docs.docker.com/engine/install/ubuntu/). Si ya utilizas Docker, conserva sus contenedores y volúmenes existentes.
- Instala las herramientas auxiliares:

```bash
sudo apt-get update
sudo apt-get install -y git python3 curl util-linux
sudo systemctl enable --now docker
sudo docker version
sudo docker compose version
uname -m
```

`uname -m` debe indicar `x86_64`. Las imágenes oficiales de SQL Server no admiten ARM como plataforma soportada; consulta [Microsoft](https://learn.microsoft.com/sql/linux/install-upgrade/quickstart-install-docker?view=sql-server-ver17). Reserva memoria para SQL Server, la aplicación y la compilación; comprueba `free -h` y espacio libre antes de construir en una PC que comparte otros servicios. No se han medido todavía los objetivos de 50 sesiones concurrentes o recuperación en 4 horas.

`CRM_SQL_EDITION=Express` es la opción incluida. Su capacidad está limitada por la edición. **Developer es exclusivamente para desarrollo/pruebas**, no para este uso publicado. Revisa [las ediciones y licencias de SQL Server](https://learn.microsoft.com/sql/sql-server/editions-and-components-of-sql-server-2025?view=sql-server-ver17) antes de cambiarlo.

## 2. Descargar el repositorio privado

Autoriza esta PC con una clave SSH propia de GitHub o una clave de despliegue de solo lectura del repositorio. No pongas tokens en la URL del repositorio, en scripts o en `.env`.

```bash
mkdir -p ~/apps
cd ~/apps
git clone git@github.com:2020f/desarrollo-crm.git
cd desarrollo-crm
git status --short
git rev-parse HEAD
```

Conserva el SHA completo de la versión que vas a instalar. El Dockerfile construye React y .NET dentro de Docker: Ubuntu no necesita tener Node ni el SDK .NET instalados por separado.

## 3. Crear carpetas y configuración privada

Los archivos y claves quedan en la PC, fuera del código y de la carpeta web:

| Ubicación | Contenido |
|---|---|
| `/srv/crm/files` | Originales con nombre interno generado; los permisos se verifican en la API. |
| `/srv/crm/keys` | Claves de ASP.NET Data Protection para cookies y tokens. |
| Volumen Docker `desarrollo-crm_sql-data` | SQL Server: usuarios, permisos, documentos, vínculos y metadatos de archivos. |
| `/srv/crm-backups` | Copias coherentes de SQL, archivos y claves. |

```bash
sudo install -d -m 711 /srv/crm
sudo install -d -m 700 -o 1654 -g 1654 /srv/crm/files /srv/crm/keys
sudo install -d -m 700 /srv/crm-backups

python3 scripts/deploy/create-env.py \
  --output .env \
  --data-dir /srv/crm \
  --email admin@tu-dominio.com \
  --name 'Tu nombre' \
  --organization 'Tu equipo'
```

Sustituye correo, nombre y equipo por los tuyos. El generador crea `.env` con permisos `600`, tres contraseñas distintas y aleatorias, y se niega a sobrescribir un archivo existente. Abre `.env` con un editor privado para guardar la contraseña inicial `CRM_BOOTSTRAP_PASSWORD` en tu gestor de contraseñas. No ejecutes `cat .env`, no lo subas a GitHub y no compartas la salida completa de `docker compose config` o `docker inspect`: pueden mostrar credenciales.

La plantilla de referencia sin secretos es [`.env.docker.example`](.env.docker.example). Mantén `CRM_SECURE_COOKIES=true` para el subdominio HTTPS. `--local-http` en el generador se reserva para una prueba local en loopback; no debe usarse al publicar en Internet.

Etiqueta la imagen con la versión exacta del código:

```bash
RELEASE_SHA="$(git rev-parse HEAD)"
sed -i "s/^CRM_IMAGE=.*/CRM_IMAGE=desarrollo-crm:${RELEASE_SHA}/" .env
sudo docker compose --env-file .env config --quiet
sudo docker compose --env-file .env build web
```

## 4. Instalar una base nueva e inicializar el administrador

Ejecuta en este orden, desde la raíz del repositorio:

```bash
sudo docker compose --env-file .env up -d --wait sql
SQL_CONTAINER="$(sudo docker compose --env-file .env ps -q sql)"
PROXY_IP="$(sudo docker inspect --format '{{range .NetworkSettings.Networks}}{{.Gateway}}{{end}}' "$SQL_CONTAINER")"
test -n "$PROXY_IP"
sed -i "s/^CRM_TRUSTED_PROXY_IP=.*/CRM_TRUSTED_PROXY_IP=${PROXY_IP}/" .env
sudo docker compose --env-file .env --profile maintenance run --rm migrate
sudo docker compose --env-file .env --profile maintenance run --rm sql-init
sudo docker compose --env-file .env --profile maintenance run --rm bootstrap
sudo docker compose --env-file .env up -d web
sudo docker compose --env-file .env ps
curl --fail http://127.0.0.1:5080/api/account/registration
```

La última petición debe devolver `{"enabled":false}`. El arranque normal no aplica migraciones ni inserta proyectos de ejemplo. No ejecutes el script de demo sobre esta instalación.

`CRM_TRUSTED_PROXY_IP` debe contener la IP exacta del gateway de la única red Compose del contenedor SQL. La aplicación solo admite `X-Forwarded-Proto` desde esa IP para reconocer el HTTPS que termina en Cloudflare; no confía en cabeceras de IP de visitante o de Host. Comprueba nuevamente el gateway si eliminas y recreas la red Docker; si cambia, actualiza `.env` y recrea `web` con `docker compose --env-file .env up -d --force-recreate web`. No uses `0.0.0.0`, rangos amplios ni IP de un cliente como proxy confiable.

Las credenciales están separadas: `migrate` y `sql-init` usan el acceso de mantenimiento; `web` y `bootstrap` usan `crm_runtime`, con lectura/escritura de datos y sin permisos para crear o modificar el esquema. La aplicación de producción rechaza conectarse como `sa`. El contenedor `web` no recibe la contraseña administrativa de SQL ni la de inicialización.

`bootstrap` solo funciona si no existen usuarios ni organizaciones. Crea una cuenta administrativa y el equipo de manera atómica; volverlo a ejecutar se rechaza y no restablece cuentas existentes. En una base restaurada, **omite bootstrap** y conserva las cuentas recuperadas.

El registro público queda cerrado en producción. El administrador da de alta al equipo desde Administración y asigna los permisos de proyecto. Guarda el acceso del administrador: la recuperación por correo y MFA todavía no están implementados. Conserva una segunda cuenta administrativa y comprueba su acceso antes de retirar el rol de la primera; aún no existe una protección contra quitar el último administrador. No cambies contraseñas SQL editando únicamente `.env`; el login ya persistido en SQL conserva su contraseña y necesita una rotación coordinada.

## 5. Conectar el subdominio mediante Cloudflare

Esta configuración supone `cloudflared` ejecutándose como servicio en la misma PC Ubuntu. En tu túnel existente agrega una aplicación publicada con:

| Campo | Valor |
|---|---|
| Hostname | Tu subdominio, por ejemplo `crm.tu-dominio.com` |
| Service type | HTTP |
| URL local | `localhost:5080` |

Consulta [la documentación oficial de aplicaciones publicadas](https://developers.cloudflare.com/cloudflare-one/networks/connectors/cloudflare-tunnel/routing-to-tunnel/). Conserva el token/credencial del túnel fuera del repositorio. Si ejecutas `cloudflared` dentro de otro contenedor, su `localhost` apunta a ese contenedor: debes adaptar su red y el destino; la configuración anterior corresponde al servicio instalado en el host.

Usa HTTPS desde el navegador y permite WebSockets para `/hubs/app`. Conserva `X-Forwarded-Proto: https` entre Cloudflare y el origen; la IP confiable del paso anterior permite que antiforgery reconozca esa conexión como segura. Configura Cloudflare para **omitir la caché del hostname del CRM**, especialmente `/api/*`, `/hubs/*`, descargas y HTML. No actives una regla de caché global para datos autenticados. La aplicación sirve sus assets con revalidación para evitar mezclar versiones.

La limitación de intentos de login usa la IP de conexión observada por ASP.NET. Con el túnel, varios usuarios pueden compartir esa IP; no se confía automáticamente en cabeceras reenviadas. Si necesitas reglas por visitante, configúralas también en Cloudflare. No habilites confianza indiscriminada en `X-Forwarded-For`.

Entra por `https://tu-subdominio/login` con el correo y la contraseña generada. Por HTTP local las cookies seguras no permiten completar el inicio de sesión; esto es esperado en la configuración publicada.

## 6. Comprobación después de instalar

1. Inicia sesión, crea un proyecto y una tarea; comprueba la tarea en tablero, lista y calendario cuando tenga fecha.
2. Crea un documento privado, uno para personas seleccionadas y otro compartido con el proyecto.
3. Sube un PNG, un Markdown y un Word `.docx` en Carpetas. Descárgalos y abre el PNG con la previsualización.
4. Usa otra cuenta con los permisos necesarios para comprobar el archivo público del proyecto. Esa cuenta no debe ver ni descargar el archivo privado del primer usuario, incluso con la URL directa.
5. Reinicia únicamente `web` y verifica que sigan los registros y las descargas:

```bash
sudo docker compose --env-file .env restart web
sudo docker compose --env-file .env logs --tail 100 web
```

Los límites actuales de documentos y tipos de archivo están en [Archivos y documentos](docs/archivos-documentos.md). «Público» significa compartido con lectores del proyecto; no es una URL anónima de Internet. El código único identifica el archivo, pero no sustituye autenticación y autorización.

## 7. Respaldo coherente

La copia necesita **SQL + archivos + claves del mismo momento**. Copiar solo `/srv/crm/files` pierde usuarios, permisos y vínculos; copiar solo SQL pierde originales. Los scripts de mantenimiento requieren `bash`, Python 3, Compose y acceso administrativo al Docker de esta instalación.

```bash
BACKUP_DIR="/srv/crm-backups/$(date -u +%Y%m%dT%H%M%SZ)"
sudo bash scripts/deploy/backup.sh \
  --env-file "$PWD/.env" \
  --output "$BACKUP_DIR"
```

El script obtiene un bloqueo de mantenimiento, detiene `web` si estaba activo, ejecuta `BACKUP DATABASE ... WITH COPY_ONLY,CHECKSUM` y `RESTORE VERIFYONLY`, archiva `files` y `keys`, calcula SHA-256 de los artefactos y vuelve a iniciar `web`. La interrupción dura lo que la copia; programa una ventana adecuada. No ejecutes otros escritores SQL o de archivos durante ella.

La carpeta de salida es nueva y privada. Contiene `database.bak`, `files-and-keys.tar.gz`, `metadata.json` y `manifest.json`. El manifiesto detecta corrupción, **no cifra la copia**. Las claves se guardan en el sistema de archivos, sin cifrado adicional por la aplicación; protege el disco y conserva las copias fuera de la PC en un destino cifrado con acceso limitado. Conserva también `.env` en tu gestor seguro, por separado.

Programa al menos una copia diaria y una prueba periódica de restauración en otra instalación. Define y verifica la retención según tu espacio; no se incluye borrado automático de copias. Los objetivos preliminares del producto son perder como máximo 24 horas y recuperar en 4 horas; debes medirlos con tus datos y tu PC.

## 8. Restaurar sin sobrescribir una instalación existente

Restaura primero en **otro proyecto Compose, otra carpeta persistente y otro puerto**, con SQL vacío. El script se niega a sobrescribir una base existente o carpetas `files/keys` con contenido. No uses `WITH REPLACE`, no borres la instalación original para probar y no ejecutes `docker compose down -v`.

Ejemplo, desde la misma versión del código que produjo el respaldo. Antes de ejecutar la secuencia, consulta `sourceCommit` en su `metadata.json`, selecciona ese SHA con Git y construye o conserva la imagen correspondiente. La recuperación se verifica inicialmente sin modificar el túnel público:

```bash
sudo install -d -m 711 /srv/crm-recovery
sudo install -d -m 700 -o 1654 -g 1654 /srv/crm-recovery/files /srv/crm-recovery/keys
python3 scripts/deploy/create-env.py \
  --output .env.recovery \
  --data-dir /srv/crm-recovery \
  --project desarrollo-crm-recovery \
  --port 5081 \
  --email admin@tu-dominio.com \
  --name 'Recuperación' \
  --organization 'Recuperación'
RECOVERY_SHA="$(git rev-parse HEAD)"
sed -i "s/^CRM_IMAGE=.*/CRM_IMAGE=desarrollo-crm:${RECOVERY_SHA}/" .env.recovery
sudo docker compose --env-file .env.recovery build web
sudo docker compose --env-file .env.recovery up -d --wait sql
SQL_CONTAINER="$(sudo docker compose --env-file .env.recovery ps -q sql)"
PROXY_IP="$(sudo docker inspect --format '{{range .NetworkSettings.Networks}}{{.Gateway}}{{end}}' "$SQL_CONTAINER")"
test -n "$PROXY_IP"
sed -i "s/^CRM_TRUSTED_PROXY_IP=.*/CRM_TRUSTED_PROXY_IP=${PROXY_IP}/" .env.recovery
sudo bash scripts/deploy/restore.sh \
  --env-file "$PWD/.env.recovery" \
  --backup /srv/crm-backups/FECHA_DE_LA_COPIA \
  --confirm-database Desarrollo
sudo docker compose --env-file .env.recovery --profile maintenance run --rm sql-init
sudo docker compose --env-file .env.recovery up -d web
```

Usa SQL Server de la misma versión principal o una versión compatible más nueva; no restaures una copia de 2025 en 2022. El nombre de base puede ser distinto si lo configuras explícitamente, pero `--confirm-database` debe coincidir.

La restauración verifica los hashes, rechaza rutas inseguras del archivo comprimido, restaura SQL con nuevos archivos físicos, ejecuta `DBCC CHECKDB` y recupera originales y claves con permisos privados. `sql-init` vuelve a asociar el usuario recuperado al login SQL de la instalación destino. **No ejecutes `migrate` ni `bootstrap` antes de restaurar**: crearían registros que impiden la restauración segura.

Prueba las cuentas existentes, la privacidad y descargas; compara el contenido original. Cambia el destino del túnel a `localhost:5081` solo después de validar y detener la antigua instalación para evitar escrituras divergentes. Conserva el origen detenido hasta completar la revisión.

## 9. Actualizaciones y vuelta a una versión anterior

1. Comprueba que `git status --short` esté vacío. Obtén el SHA completo aprobado con `git fetch origin`.
2. Crea el respaldo coherente anterior y guarda el SHA e imagen actuales. `metadata.json` registra ambos.
3. Detén `web`, selecciona el SHA y construye su imagen:

```bash
sudo docker compose --env-file .env stop web
git switch --detach SHA_COMPLETO_APROBADO
RELEASE_SHA="$(git rev-parse HEAD)"
sed -i "s/^CRM_IMAGE=.*/CRM_IMAGE=desarrollo-crm:${RELEASE_SHA}/" .env
sudo docker compose --env-file .env build web
sudo docker compose --env-file .env --profile maintenance run --rm migrate
sudo docker compose --env-file .env --profile maintenance run --rm sql-init
sudo docker compose --env-file .env up -d web
```

4. Repite las comprobaciones de login, permisos, calendario y descargas. En actualizaciones no se vuelve a inicializar el administrador.

Si cambia el esquema, no basta con arrancar un binario anterior. Recupera el respaldo coherente en un destino vacío con el código de su SHA, valida y cambia el túnel. Las escrituras posteriores al respaldo necesitarán conciliación; evita permitir cambios mientras decides recuperar. El repositorio no incorpora migraciones destructivas automáticas ni un proceso de downgrade certificado.

## 10. Operación y límites

- Conserva `.env`, el volumen SQL y `/srv/crm` al recrear contenedores. Evita `down -v`, `docker volume prune` y limpiezas generales.
- `restart: unless-stopped` permite recuperar los servicios con Docker; comprueba también el servicio del túnel después de reiniciar Ubuntu.
- El correo, GitHub como integración del CRM, búsqueda global, antivirus, cuotas agregadas y varias funciones de `crm.md` siguen pendientes o parciales; consulta la matriz. Tener el código publicado en GitHub no activa un conector dentro de la aplicación.
- El análisis antivirus no está integrado. El servidor valida los formatos soportados y el acceso, pero los archivos deben abrirse con herramientas actualizadas.
- Revisa espacio de disco, errores de `web`/`sql`, resultado de copias y capacidad de recuperación. No hay garantía de disponibilidad si la PC pierde electricidad, red o entra en suspensión.
- Para pruebas y comandos de desarrollo utiliza [README.md](README.md). Para el comportamiento exacto de Carpetas y el editor, utiliza [docs/archivos-documentos.md](docs/archivos-documentos.md).

## Validación de esta preparación

El 28/10/2026 se ejecutó un ensayo aislado con contenedores Linux en Docker Desktop y SQL Server Express 2025: instalación, cuenta inicial única, registro público cerrado, escritura con usuario SQL limitado y `CREATE TABLE` rechazado. Se respaldaron y restauraron SQL, archivos y claves en otro proyecto vacío; `RESTORE VERIFYONLY` y `DBCC CHECKDB` finalizaron correctamente. Se conservaron la nota privada, los tres originales PNG/Markdown/DOCX, sus hashes, las audiencias y una cookie de sesión existente. Los intentos de sobrescribir archivos o una base existente fueron rechazados.

Después del ajuste del proxy se reconstruyó la imagen final y se comprobó la emisión de tokens antiforgery, login con CSRF y lectura autenticada usando cookies `Secure` y `X-Forwarded-Proto: https` desde el gateway exacto. Sin ese esquema o desde un proxy ajeno no se emitió el token seguro. Se verificó que `web` no recibiera credenciales de mantenimiento o inicialización. Los contenedores y el helper temporal quedaron detenidos conservando los datos de prueba.

El alcance y los resultados están en [la evidencia del ensayo](docs/evidence/deployment/results.json). El ensayo no desplegó la PC Ubuntu de destino ni conectó un túnel real de Cloudflare. Esas comprobaciones se realizan al seguir esta guía en la PC donde se publicará.
