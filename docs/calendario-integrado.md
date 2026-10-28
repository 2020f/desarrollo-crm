# Calendario integrado con fechas del CRM

## Comportamiento

Asignar una fecha de planificación a un registro autorizado lo hace visible en Calendario. Cambiar la fecha mueve su representación y quitarla la retira. No es necesario crear otro evento ni ejecutar un proceso de sincronización: `GET /api/calendar` consulta los datos originales en cada carga.

| Origen | Fecha o periodo mostrado | Abrir desde calendario |
| --- | --- | --- |
| Tarea, hija o subtarea | Inicio y vencimiento; si existen ambos, periodo con último día incluido | Ficha `/tasks/{id}` |
| Proyecto | Inicio, vencimiento o periodo | Resumen del proyecto |
| Épica | Vencimiento | Backlog del proyecto |
| Ciclo activo | Periodo de inicio a fin | Resumen del proyecto |
| Hito | Vencimiento | Resumen del proyecto |
| Entrega | Fecha prevista y publicación real, diferenciadas | Vista Entregas |
| Evento o reunión manual | Inicio/fin propios, con hora o de todo el día | Editor del calendario |

Las proyecciones de otros módulos muestran su origen y tipo de fecha. Pulsarlas abre el registro original; no se editan ni eliminan como eventos independientes. Los campos que solo existen en entidades o API, pero todavía no tienen editor propio, no adquieren un CRUD nuevo con este cambio. La vista Entregas existente requiere seleccionar el proyecto correspondiente.

Las fechas técnicas de creación, modificación, auditoría y lectura no se incorporan como compromisos. Los registros archivados, borrados o cancelados y los ciclos inactivos no se proyectan. Las reuniones manuales siguen siendo eventos independientes; un vínculo no sincroniza sus campos con una tarea.

## Datos y permisos

No se añadieron tablas ni migraciones. `CalendarService` combina `CalendarEvents` con proyecciones de las entidades existentes. Los IDs de proyección se calculan de forma estable por tipo de origen, ID y clase de fecha. Reconsultar no inserta filas.

Una representación manual de todo el día sustituye a una proyección solo si tiene un vínculo explícito a esa fuente y coincide exactamente con su intervalo. Una reunión con otra hora o fecha sigue siendo un compromiso independiente.

Se comprueban organización, usuario activo, membresía activa y permiso de lectura del proyecto antes de devolver datos. Las tareas personales respetan la política de autor/administrador; los eventos personales se limitan al creador o destinatario. Crear, editar, trasladar o eliminar un evento manual comprueba permisos de servidor, vínculos y destinatarios. Los avisos y sus conteos también revalidan el acceso al evento y a su fuente después de una revocación.

`CalendarEventDto` incorpora `SourceType`, `SourceId`, `DateKind`, `SourceRoute`, `IsDerived`, `CanEdit` y `CanDelete`. Las proyecciones son de solo lectura y los permisos enviados a la interfaz no sustituyen las comprobaciones del servidor.

## Fechas y vistas

El cliente consulta `GET /api/calendar?start=<UTC>&end=<UTC>&timeZoneId=<zona IANA>`. El rango es positivo, con fin exclusivo, y admite hasta 366 días. Se utiliza la zona enviada, la del perfil si existe o `America/Santo_Domingo` como valor predeterminado.

- Las fechas sin hora conservan su día civil. Un vencimiento `2026-10-29` aparece el 29, sin convertirse al día anterior por UTC.
- Los intervalos de todo el día usan fin exclusivo en el contrato: un evento del 29 tiene inicio 29 y fin 30. El formulario presenta «Último día (incluido)» y permite elegir 29 en ambos campos.
- Los eventos con hora se almacenan y devuelven como instantes UTC, presentados en la zona del navegador.
- Mes y Agenda incluyen todas las clases de fecha. Semana y Día incorporan una banda «Todo el día» además de los eventos con hora.
- Existen filtros por proyecto y origen, carga visible, errores con reintento y actualización al volver a la pantalla o recuperar el foco.

## Validación del 27 de octubre de 2026

Se ejecutó en desarrollo local con ASP.NET Core/EF Core, SQL Server real y React/TypeScript. Los datos de prueba se crearon exclusivamente en bases temporales o en la demo aislada `acceptance`.

```powershell
dotnet build Desarrollo.slnx --no-restore --verbosity minimal
dotnet test Desarrollo.slnx --no-restore --logger 'trx;LogFileName=calendar-integration.trx' --results-directory .validation/results --verbosity minimal
npm --prefix src/Desarrollo.Web/ClientApp run build
npm --prefix src/Desarrollo.Web/ClientApp run test:calendar
npm --prefix src/Desarrollo.Web/ClientApp run test:diagram
```

Resultados:

- Build .NET: **0 errores y 0 advertencias**.
- SQL Server: **48/48 pruebas correctas**, sin omitidas; incluye **14 casos de calendario** y las 34 pruebas anteriores. TRX local: `.validation/results/calendar-integration.trx`.
- Frontend: TypeScript/Vite correcto; **6/6 casos de calendario/editor** y **4/4 regresiones del mapa**. Los tests versionables están en `tests/frontend`; los adaptadores DOM no sustituyen el recorrido real. Vite conserva una advertencia de tamaño del bundle superior a 500 kB.
- Navegador: **11 comprobaciones aprobadas** en [ui-checks.json](evidence/calendar/ui-checks.json). Se guardó el vencimiento de una tarea con el control nativo, se vio el 29 en las cuatro vistas, se cambió al 30 y se quitó. Se abrió la ficha original y se probaron eventos manuales, filtros y Agenda móvil de 390 px.
- API/SQL de esa demo: continúan 11 tareas y exactamente dos `CalendarEvents`, ambos creados manualmente durante la prueba; **ninguna copia automática**. Un evento de 09:00 en Santo Domingo se devolvió como 13:00 UTC. Recibo local: `.validation/results/calendar-api.json`.

Capturas: [Mes](evidence/calendar/task-due-month.png), [Semana](evidence/calendar/task-due-week.png), [Día](evidence/calendar/task-due-day.png), [evento manual de todo el día](evidence/calendar/manual-all-day.png) y [Agenda móvil](evidence/calendar/calendar-mobile-agenda.png). Las capturas del vencimiento muestran un estado intermedio del recorrido; la prueba terminó quitando esa fecha.

Los hosts locales se reiniciaron con migraciones automáticas desactivadas. La comparación de conteos y SHA256 de nueve tablas de planificación en cada base confirmó la conservación de los datos normales y de la demo principal: **11 tareas normales y seis de la demo principal** al actualizar. Recibo local: `.validation/calendar-reload.json`, estado `RestartedAndVerified`. La demo de aceptación quedó detenida y su base se conservó; el CRM normal sigue disponible en `http://localhost:5007`.

Este cierre cubre la integración de fechas y la autorización asociada. Recurrencias ejecutables, recordatorios y las demás funciones pendientes del CRM permanecen en la matriz general; no se declaran implementadas por este trabajo. La carpeta sigue sin metadatos Git, por lo que no hay rama, SHA ni commit atribuibles a esta entrega.
