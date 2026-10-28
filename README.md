# Desarrollo

Plataforma para centralizar el desarrollo de productos de software: proyectos, tareas, tableros, documentación, chat, tickets, calendario, reportes y administración. Especificación completa en [`crm.md`](crm.md). Arquitectura y decisiones en [`docs/arquitectura.md`](docs/arquitectura.md); matriz rastreable de requisitos en [`docs/matriz-requisitos.md`](docs/matriz-requisitos.md).

**Despliegue en Ubuntu con Docker:** seguir [`DESPLIEGUE.md`](DESPLIEGUE.md), que incluye instalación, primera cuenta, almacenamiento persistente, túnel Cloudflare, actualización, respaldo y restauración. El código publicado conserva requisitos pendientes en la matriz; no representa la terminación de todo `crm.md`.

## Stack

- **Backend**: C# / ASP.NET Core MVC (net10.0). Razor sirve únicamente el shell que monta React; el resto son controladores JSON bajo `/api/*`.
- **Frontend**: React 18 + TypeScript (Vite), build estático a `wwwroot/dist`.
- **Datos**: Entity Framework Core 10 + **SQL Server** (no sustituir por SQLite/Postgres).

## Requisitos

- .NET SDK 10 instalado (los proyectos tienen como destino `net10.0`).
- Node.js 22 o posterior compatible con Vite y npm; CI usa Node.js 22.
- SQL Server local (o remoto). Ejemplo verificado con SQL Server 2025 en `localhost` (autenticación de Windows).
- `dotnet ef` (instalar con `dotnet tool install --global dotnet-ef`).

## Configuración

La cadena de conexión vive en `src/Desarrollo.Web/appsettings.json`:

```json
"ConnectionStrings": {
  "Default": "Server=localhost;Database=Desarrollo;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
}
```

No se usan secretos en el código. En producción, configurar `ConnectionStrings__Default` mediante variables de entorno privadas con un usuario SQL de ejecución limitado. Las migraciones usan una conexión de mantenimiento separada (`ConnectionStrings__Maintenance`); el contenedor web no recibe la contraseña de `sa`. Los secretos de desarrollo pueden configurarse con user-secrets.

## Inicio local

```powershell
# 1. Frontend (build de React -> wwwroot/dist)
cd src\Desarrollo.Web\ClientApp
npm ci
npm run build

# 2. Migraciones (aplicación explícita al catálogo configurado)
cd ../../..
dotnet ef database update --project src\Desarrollo.Infrastructure --startup-project src\Desarrollo.Web

# 3. Ejecutar la aplicación
dotnet run --project src\Desarrollo.Web --urls http://localhost:5007
```

Abrir `http://localhost:5007`. En `Development`, **Crear equipo** permite crear un usuario y su organización con los roles del sistema. En producción, el registro público está cerrado por defecto (`App__AllowRegistration=false`) y la primera cuenta se crea con el comando explícito `--bootstrap-admin-only`, descrito en la guía de despliegue. Este comando solo admite una base sin usuarios ni organizaciones. Después, Administración permite dar de alta al equipo. No existen contraseñas de administrador fijas.

## Migraciones

```powershell
dotnet ef migrations add Nombre --project src\Desarrollo.Infrastructure --startup-project src\Desarrollo.Web
dotnet ef database update --project src\Desarrollo.Infrastructure --startup-project src\Desarrollo.Web
```

- El DbContext de diseño (`AppDbContextFactory`) usa `DESARROLLO_CONNECTION_STRING` o el valor por defecto de `localhost`.
- El arranque normal no aplica migraciones. La opción `Database__ApplyMigrationsOnStartup=true` solo se admite explícitamente con entorno `Development`; el script de demo la limita a su base aislada.
- En Docker, `migrate` ejecuta `--migrate-only` con la conexión de mantenimiento; `sql-init` crea o remapea el usuario SQL de ejecución y le concede lectura/escritura sin permisos DDL. No usar el usuario de ejecución para aplicar migraciones.
- El seed **demostrativo** no se carga en el arranque normal. El comando implementado para el mapa es `scripts/Start-TaskDiagramDemo.ps1`; crea una base nueva y cinco tareas de referencia. `App:SeedDemo` no es un mecanismo implementado para cargar los otros proyectos de ejemplo.

## Pruebas

El módulo Mapa de tareas, sus contratos, reglas de concurrencia y demostración aislada están documentados en [docs/mapa-tareas.md](docs/mapa-tareas.md).

Las fechas de tareas, proyectos, épicas, ciclos, hitos y entregas se reflejan automáticamente en Calendario desde los registros originales. Reglas, permisos y validaciones en [docs/calendario-integrado.md](docs/calendario-integrado.md).

Chat y notificaciones consultan automáticamente la actividad autorizada mientras el CRM está abierto, aproximadamente cada tres segundos, con avisos de sonido configurables por usuario y navegador. Comportamiento, contrato y validación local en [docs/chat-actualizacion-automatica.md](docs/chat-actualizacion-automatica.md).

```powershell
dotnet test
```

`Desarrollo.Tests` incluye pruebas de integración contra **SQL Server real** (crea una base `Desarrollo_Test_*` temporal y la elimina al terminar), cubriendo el caso de permisos negativos (un usuario de otra organización no accede a un proyecto privado) y la generación de estados por defecto.

## Autenticación y autorización

- ASP.NET Core Identity (`AppUser`), cookies seguras, `SameSite=Lax`.
- Antiforgery: el frontend obtiene el token de `GET /api/account/antiforgery` y lo envía en la cabecera `X-XSRF-TOKEN` en toda operación mutante.
- La autorización se evalúa **en servidor** en cada consulta/operación (`IPermissionService`). Los proyectos son privados por defecto; los invitados solo ven recursos compartidos.
- No se oculta contenido restringido con CSS: las API devuelven solo lo autorizado.
- Cada petición autenticada revalida cuenta y membresía activas. Identity conserva los claims de organización al renovar la sesión. Login y registro tienen límites de intentos, además del bloqueo de contraseña de Identity.
- Detrás de Cloudflare, `ReverseProxy__KnownProxies__0` identifica la IP exacta del proxy local autorizado a reenviar el esquema HTTPS. Sin esa configuración no se procesan cabeceras reenviadas; no se confía globalmente en IP o host enviados por el cliente. La guía explica cómo obtener el gateway de Docker.
- Solicitudes separa permisos de lectura, edición, asignación, estado y comentarios; las notas internas solo se devuelven a lectores autorizados. Chat valida el acceso actual al canal en cada operación y al conectar SignalR. Las notificaciones históricas también se filtran con los permisos actuales.

## Almacenamiento de archivos

- **Carpetas** permite archivos públicos por proyecto y privados por usuario, con código único y descargas autorizadas. Los documentos ofrecen editor con formato y audiencias de proyecto, personas seleccionadas o solo autor.
- Almacenamiento local privado bajo `src/Desarrollo.Web/App_Data/files`, configurable mediante `Storage__RootPath`. No se sirve como carpeta web.
- Se incluyen Dockerfile y Compose para una carpeta persistente en Ubuntu. Operación e instalación en [DESPLIEGUE.md](DESPLIEGUE.md); reglas funcionales y límites en [docs/archivos-documentos.md](docs/archivos-documentos.md).

## Correo / integraciones

- No configurado por defecto. El correo es opcional y configurable; no se envían correos reales sin configuración y autorización.
- Referencias de GitHub: se almacenan como `RepositoryReferences`; el conector del producto está pendiente. Publicar el código de esta aplicación en GitHub no implementa ese conector.

## Procesos persistentes

`BackgroundWorker` (IHostedService) ejecuta trabajos con reintentos e idempotencia. Actualmente genera notificaciones de vencimiento (deduplicadas por `DedupKey`). SQL Server conserva mensajes, avisos, lecturas e historial; no se depende de memoria local.

## Estructura

```
src/Desarrollo.Domain            entidades, enums, reglas
src/Desarrollo.Application       DTOs, contratos, casos de uso
src/Desarrollo.Infrastructure    AppDbContext, configuraciones, migraciones, servicios
src/Desarrollo.Web               MVC host, controladores JSON, Identity, SignalR, worker, shell Razor
src/Desarrollo.Web/ClientApp     React + TypeScript (Vite)
tests/Desarrollo.Tests           pruebas de dominio e integración (SQL Server)
```
