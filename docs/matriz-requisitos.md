# Matriz rastreable de requisitos

Estado: `OK` implementado y verificado · `PARCIAL` implementado en núcleo, con pendientes · `PENDIENTE` no iniciado · `BACKLOG` evolución futura.

| # | Requisito | Estado | Evidencia |
|---|---|---|---|
| R01 | Identidad: login/logout, registro, sesiones, roles de sistema | PARCIAL | Login/logout, registro configurable y cerrado en producción, inicialización administrativa explícita, cuentas/roles y revocación de sesión verificados. Faltan recuperación de acceso, invitaciones y MFA de crm.md. |
| R02 | Organización, productos, proyectos, miembros, roles, permisos | PARCIAL | Entidades + API org/proyecto y alta/baja/cambio de rol de miembros de proyecto; faltan invitaciones y gestión avanzada de organización |
| R03 | Tareas: tipos, checklist, comentarios, transiciones, bloqueo, relaciones | PARCIAL | WorkItem + checklist + relations (verificado) |
| R04 | Tablero / tabla / lista / calendario / galería sobre el mismo registro | PARCIAL | Board, Table, Calendar y Files funcionan sobre WorkItem/StoredFile |
| R05 | Detalle de tarea y editor lateral compartido | PARCIAL | `/tasks/:id` y `TaskEditorPanel`; editor compartido verificado con el mapa; no se certifican aquí los demás campos avanzados de crm.md |
| R06 | Búsqueda global autorizada | PENDIENTE | — |
| R07 | Documentos: listado, editor, versiones | PARCIAL | Editor Tiptap, listas/checklists/alineación, árbol básico, concurrencia y audiencia proyecto/seleccionada/privada; faltan comentarios/menciones, tablas y restauración de versiones en UI. Ver `archivos-documentos.md`. |
| R08 | Archivos: carga, descarga, previsualización, galería | PARCIAL | Carpetas pública por proyecto y privada personal, código único, búsqueda/paginación, preview autorizado y almacenamiento persistente configurable/Docker; faltan versiones, cuotas agregadas y análisis antivirus. Ver `archivos-documentos.md`. |
| R09 | Menciones @ y notificaciones con deduplicación | PARCIAL | Notificaciones + worker de vencimiento; faltan menciones |
| R10 | Chat: canales, mensajes | PARCIAL | Canales, mensajes persistentes y raíz de hilo validada por API; acceso actual, edición, canales privados y conexión SignalR cubiertos con SQL. Faltan recorrido de hilos completo en UI, menciones y convertir a tarea. |
| R11 | Calendario: Mes, Semana, Día, Agenda y fechas de registros originales | PARCIAL | `docs/calendario-integrado.md`: fechas de tareas/proyectos/épicas/ciclos/hitos/entregas, eventos manuales y permisos verificados; faltan recurrencias ejecutables y recordatorios |
| R12 | Tickets: creación, triage, notas internas vs respuestas | PARCIAL | `/api/tickets`: aislamiento, solicitante/responsable en tickets personales, capacidades por operación, notas internas protegidas, vínculos compatibles y notificaciones revocadas verificados. Falta flujo completo de soporte/entrega de crm.md. |
| R13 | Revisión QA: cola, aprobación/devolución | PARCIAL | Cola por estado + transiciones |
| R14 | Entregas/versiones | PARCIAL | Listado por proyecto |
| R15 | Reportes: flujo, calidad | PARCIAL | Snapshot por estado |
| R16 | Administración: miembros, roles, productos | PARCIAL | Alta de cuentas y cambio de rol de organización con validación del ámbito. Faltan ajustes avanzados de organización, flujos, campos y automatizaciones. |
| R17 | Automatizaciones iniciales | PARCIAL | Worker de vencimientos (idempotente); sin reglas configurables |
| R18 | Referencias GitHub | PENDIENTE | Entidad `RepositoryReference` sin conector |
| R19 | Autorización en servidor (casos negativos) | PARCIAL | Verificada en tareas/mapa, calendario, documentos, archivos, tickets, canales y notificaciones con SQL Server; cuenta/membresía se revalidan en sesión. No constituye certificación global de todos los módulos ni de rendimiento. |
| R20 | Campos personalizados | PENDIENTE | Entidades sin API/UI |
| R21 | Estados/transiciones configurables | PARCIAL | Estados por defecto + transición; falta configuración |
| R22 | Vistas guardadas | PENDIENTE | — |
| R23 | Worker persistente | PARCIAL | `BackgroundWorker` (notificaciones de vencimiento) |
| R24 | Exportación / API documentada | PENDIENTE | — |
| R25 | Mapa de tareas: jerarquía, lienzo persistente y edición compartida | OK | `docs/mapa-tareas.md`: pruebas SQL, regresiones frontend y recorrido real escritorio/móvil |

## Verificación realizada

- Migración `InitialCreate` aplicada contra **SQL Server 2025 local** (49 tablas).
- Recorrido verificado en navegador: registro → organización → proyecto → tablero → tarea (código `GOGO-1`) → transición de estado → comentario.
- Validación actual del módulo mapa (26/10/2026): build .NET correcto; **34/34 pruebas** correctas, incluidas 26 del mapa, contra SQL Server real; frontend compilado; **4/4 regresiones** correctas y 13 comprobaciones de navegador. Evidencia en `docs/mapa-tareas.md` y `docs/evidence/task-diagram`.
- Las cinco migraciones están aplicadas en la base local `Desarrollo`; `TaskDiagramIntegrity` y `DocumentFileVisibility` se aplicaron con backup SQL verificado y preservación exacta de sus datos originales. No es evidencia de despliegue ni de terminación del CRM completo.
- Calendario integrado (27/10/2026): **48/48 pruebas SQL Server**, incluidas 14 nuevas del calendario; **6/6 casos frontend del calendario/editor**, **4/4 regresiones del mapa** y **11 comprobaciones reales de navegador**. Sin nuevas tablas o migraciones; fechas derivadas de sus registros originales y datos normales preservados al actualizar. Evidencia en `docs/calendario-integrado.md`.
- Documentos y Carpetas (27/10/2026): **80/80 pruebas SQL Server**, **19/19 frontend**, **1/1 regresión de assets** y **15/15 comprobaciones reales de navegador**. Docker con SQL Server Express 2025 validó cargas y persistencia tras recrear web; base normal y demo preservadas. Evidencia y alcance exacto en `docs/archivos-documentos.md`.
- Preparación de despliegue (28/10/2026): build .NET sin errores ni advertencias y **136/136 pruebas**, con SQL Server real e inicialización de cuentas, revocación, solicitudes, canales, avisos históricos y diez casos del proxy/antiforgery; **26/26 frontend**, build TypeScript/Vite y **1/1 assets** correctos. [Recibo](evidence/predeployment/validation.json) e [instrucciones operativas](../DESPLIEGUE.md). La instalación en el equipo Ubuntu y la publicación del túnel se verifican por separado.

## Backlog (no construido)

Co-edición avanzada, métricas ampliadas, portal de clientes, constructor de automatizaciones, cronograma/capacidad avanzada, fórmulas generales, calendarios bidireccionales, audio/video, apps nativas, IA, búsqueda semántica/OCR, anotación de imágenes.
