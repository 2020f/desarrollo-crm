# Desarrollo — Arquitectura y decisiones (Fase 0)

## Stack fijado
- C# / ASP.NET Core MVC (host + backend), React + TypeScript (frontend), EF Core (datos), SQL Server (relacional).
- Target framework: **net10.0** (LTS), EF Core 10. SQL Server 2025 local (`localhost`, Windows Auth).

## Interpretación MVC + React
- `Desarrollo.Web` sirve **una** vista Razor (`/` → `Home/Index`) que monta React.
- Todos los endpoints de dominio son controladores JSON bajo `/api/*`.
- React gestiona navegación, pantallas, editor, tabla, tablero, calendario y paneles.
- El servidor es dueño de autenticación, autorización, validación y reglas de negocio.

## Proyectos
| Proyecto | Responsabilidad |
|---|---|
| `Desarrollo.Domain` | Entidades, enums, value objects, reglas de dominio (sin dependencias). |
| `Desarrollo.Application` | DTOs, contratos (interfaces), casos de uso, resultados/errores. |
| `Desarrollo.Infrastructure` | `AppDbContext` (EF Core), configuraciones, migraciones, repositorios, adaptadores (blob, correo, GitHub), worker persistente. |
| `Desarrollo.Web` | Host ASP.NET Core, Identity, controladores JSON, shell Razor, SignalR hubs, `IHostedService`, archivos estáticos de React. |
| `Desarrollo.Tests` | Pruebas de dominio/reglas y de integración con SQL Server. |

## Estados de trabajo
- Flujo inicial (por proyecto, configurable): `Backlog`, `ListoParaDesarrollar`, `EnDesarrollo`, `EnRevision`, `EnPruebas`, `ListoParaPublicar`, `Publicada`; trabajo no desplegable termina en `Aceptada`.
- Categorías internas estables: `Pendiente`, `EnCurso`, `Finalizado`, `Cancelado`.
- `Bloqueada` es condición coexistente con la etapa (flag + motivo), no un estado.

## Matriz de permisos (capacidades)
Capacidades base: `Leer`, `Comentar`, `Crear`, `Editar`, `Asignar`, `CambiarEstado`, `Aprobar`, `Exportar`, `Archivar`, `Eliminar`, `AdministrarMiembros`, `ConfigurarFlujo`.
Roles iniciales:
| Rol | Capacidades clave |
|---|---|
| Administrador de organización | todas |
| Líder de proyecto | leer, comentar, crear, editar, asignar, cambiar estado, aprobar, exportar, archivar, administrar miembros, configurar flujo |
| Responsable de producto | leer, comentar, crear, editar, asignar, cambiar estado, aprobar, exportar |
| Desarrollador / diseñador | leer, comentar, crear, editar, cambiar estado |
| QA / revisor | leer, comentar, crear, editar, cambiar estado, aprobar |
| Cliente / invitado | leer (solo recursos compartidos), comentar |
| Lector | leer |

Reglas de autorización: se evalúan en servidor **en cada** consulta/operación (org → membresía → recurso → sensibilidad → operación). La pertenencia a la organización no otorga acceso automático a proyectos (privados por defecto). Los invitados solo acceden a recursos compartidos.

## Rutas (frontend SPA)
`/` inicio · `/my-work` mi trabajo · `/projects` · `/projects/:id` · `/projects/:id/backlog` · `/projects/:id/board` · `/tasks/:id` · `/projects/:id/table` · `/calendar` · `/docs` · `/files` · `/chat` · `/notifications` · `/tickets` · `/tickets/:id` · `/review` · `/releases` · `/reports` · `/admin` · `/login`.

## Tokens visuales (tema oscuro por defecto)
`--bg:#0B1020` · `--nav:#10172A` · `--panel:#172238` · `--panel-2:#202441` · `--panel-3:#302439` · `--panel-4:#152D35` · `--primary:#9B8AFB` · `--success:#57D6C7` · `--warn:#EAC385` · `--error:#E0788A` (coral, solo error) · `--text:#F0F3FC` · `--text-2:#A8B7D1`. Esquinas ~12px, iconos de línea, sombras suaves. Modo claro como preferencia secundaria.

## Seguridad / operación
- ASP.NET Core Identity + cookies seguras; antiforgery en operaciones mutantes; consistencia SignalR↔HTTP vía token/claims.
- DTOs en toda la frontera; sin exponer entidades EF; sin binding masivo de propiedades sensibles.
- Persistir primero (transacción), luego eventos; worker con reintentos e idempotencia para recordatorios, correos, miniaturas, exportaciones, webhooks.
- Filtros globales EF como ayuda, **no** como única barrera.

## Backlog (no construido en esta versión)
Co-edición avanzada, métricas ampliadas, portal de clientes, constructor de automatizaciones, cronograma/capacidad avanzada, fórmulas generales, calendarios bidireccionales, audio/video, apps nativas, IA, búsqueda semántica/OCR, anotación de imágenes.
