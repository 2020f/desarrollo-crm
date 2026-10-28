# Mapa de tareas

El mapa representa las mismas tareas que Tablero, Lista y ficha. La relación `ParentTaskId` expresa descomposición del trabajo; no reemplaza las dependencias de ejecución ni el tipo de trabajo.

## Alcance y arquitectura

- Ruta React: `/projects/:id/diagram`. React Flow dibuja tarjetas y conexiones; ASP.NET Core MVC autoriza y valida cada operación.
- Editor compartido `TaskEditorPanel`: creación raíz/hija, edición, notas, progreso, acento, prioridad, estado, responsable, fecha, aceptación y cambio explícito de madre. La ficha completa conserva checklist y comentarios.
- Las tarjetas muestran nombre propio y madre inmediata; la ficha y el editor muestran la ruta completa. Los códigos permanecen estables.
- La disposición es compartida por proyecto. Cámara y ramas contraídas son personales y se guardan en SQL Server.
- El progreso mostrado utiliza el porcentaje del checklist si tiene elementos; en su ausencia utiliza el progreso manual opcional. Estado, prioridad y progreso se guardan de forma independiente.
- Los datos de referencia son demostrativos y solo se crean mediante el script explícito descrito abajo.
- La navegación interna y el historial esperan las posiciones y cámara pendientes. Si falla el guardado, el mapa permanece montado y permite reintentar. Cerrar sesión bloquea la interacción hasta terminar el guardado y el cierre.
- En móvil, la navegación lateral se abre como menú accesible; el lienzo, los filtros y la alternativa Lista conservan el ancho disponible.

## Contratos HTTP

Todas las operaciones requieren sesión. Las mutaciones requieren token antiforgery. Las versiones son `rowversion` de SQL Server codificadas en base64.

| Operación | Ruta |
| --- | --- |
| Consultar mapa, permisos, estados, posiciones y vista personal | `GET /api/projects/{id}/task-diagram` |
| Consultar también las archivadas | `GET /api/projects/{id}/task-diagram?includeArchived=true` |
| Crear raíz | `POST /api/workitems` |
| Crear hija | `POST /api/workitems/{parentId}/children` |
| Editar tarea | `PUT /api/workitems/{id}` o `PATCH /api/workitems/{id}` |
| Cambiar madre explícitamente | `PATCH /api/workitems/{id}/parent` |
| Cambiar estado desde otras vistas | `POST /api/workitems/{id}/transition` |
| Guardar posiciones y obtener sus nuevas versiones | `PATCH /api/projects/{id}/task-diagram/positions` |
| Guardar cámara y ramas contraídas personales | `PUT /api/projects/{id}/task-diagram/me/viewport` |
| Revisar alcance de archivado/restauración | `GET /api/workitems/{id}/archive-preview?restore=false` |
| Archivar/restaurar | `POST /api/workitems/{id}/archive` o `/restore` |

Creación envía un `clientRequestId` UUID estable durante el intento para evitar duplicación por reintento. Edición, cambio de madre, transición y archivado envían `rowVersion`. Cada posición envía su propia versión; `null` indica que aún no se había persistido una posición. La cámara obtiene el usuario de la sesión, nunca de un ID enviado por el navegador.

Ante HTTP 409, el editor mantiene el borrador y permite comparar la versión guardada antes de preparar un reintento. Las posiciones locales tampoco se descartan: el usuario debe revisar el aviso y reintentar expresamente. Fallos de red se presentan con acciones de reintento.

## Permisos y reglas

| Capacidad | Comportamiento |
| --- | --- |
| Leer | Consultar tareas autorizadas y guardar la cámara personal |
| Crear | Crear raíces/hijas en el proyecto autorizado |
| Editar | Modificar campos, posiciones o madre |
| Asignar | Cambiar responsable |
| CambiarEstado | Cambiar estado respetando las reglas de transición |
| Archivar | Revisar alcance y archivar/restaurar ramas |

El administrador se limita a su organización. Los IDs de tareas, estados y posiciones se validan contra el proyecto de la operación. El backend impide madres de otro proyecto, autorreferencias y ciclos. Los cambios estructurales de un mismo proyecto se serializan dentro de una transacción SQL Server; la tarea y su posición inicial se guardan como una unidad.

Archivar una madre requiere revisar el alcance y elegir la rama completa, o cancelar para reasignar primero sus hijas desde el editor. La restauración es explícita. Arrastrar y usar flechas solo cambia coordenadas; las teclas de borrado del lienzo no eliminan tareas.

## Demostración local aislada

Requisitos: .NET SDK 10, Node.js con dependencias instaladas y SQL Server accesible mediante autenticación Windows o configuración explícita de demo. Compilar antes de iniciar:

```powershell
dotnet build Desarrollo.slnx --no-restore
npm --prefix src/Desarrollo.Web/ClientApp run build
./scripts/Start-TaskDiagramDemo.ps1
```

El script crea una base **nueva** `Desarrollo_DiagramDemo_<GUID>`, inicia un host limitado a localhost y crea el proyecto Tienda demo con CMP-01…CMP-05 y cuatro conexiones. No utiliza ni modifica la base normal `Desarrollo`. Las credenciales aleatorias se guardan únicamente en `.validation/demo-session.json`, ignorado por Git; no se imprimen en consola. El recibo incluye la URL y los datos necesarios para iniciar sesión localmente.

La demo utiliza los estados reales equivalentes del CRM: Backlog, En desarrollo y En revisión. No altera catálogos normales para copiar etiquetas de una imagen. La primera visita encuadra automáticamente las cinco tarjetas; no se impone una cámara fija. Ejecutar el script nuevamente reutiliza la sesión activa, sin duplicar datos. Para detenerla y reanudarla sin modificar su contenido:

```powershell
./scripts/Stop-TaskDiagramDemo.ps1
./scripts/Start-TaskDiagramDemo.ps1 -Resume
# Eliminación opcional y explícita, solo de la base demo verificada:
./scripts/Stop-TaskDiagramDemo.ps1 -RemoveDatabase
```

No ejecutar migraciones contra producción ni activar integraciones externas como parte de esta demostración.

## Verificación y trazabilidad

Validación local completada el **26 de octubre de 2026**, America/Santo_Domingo. La revisión utilizó SQL Server real, la demo aislada `acceptance`, un viewport de escritorio 1280×1000 y otro móvil 390×844. No se ejecutaron integraciones externas ni despliegues.

Comandos ejecutados:

```powershell
dotnet build Desarrollo.slnx --no-restore --verbosity minimal
dotnet test Desarrollo.slnx --no-restore --logger 'trx;LogFileName=task-diagram.trx' --results-directory .validation/results --verbosity minimal
npm --prefix src/Desarrollo.Web/ClientApp run build
npm --prefix src/Desarrollo.Web/ClientApp run test:diagram
```

Resultados: build .NET con **0 errores y 0 advertencias**; **34/34 pruebas** correctas, incluidas las **26 del mapa**, sin omitidas; TypeScript/Vite correcto; **4/4 regresiones del frontend** correctas con 50 aserciones. Vite informa un bundle superior a 500 kB; es una advertencia de tamaño, no un fallo de compilación. Los cuatro casos reproducibles residen en `tests/frontend` y cubren navegación, logout demorado, selección tras crear y menú móvil.

El recorrido de navegador dejó **13 comprobaciones aprobadas** en [ui-checks.json](evidence/task-diagram/ui-checks.json). Las pruebas de servidor cubren permisos, jerarquía, transacciones y concurrencia; el recorrido UI comprueba los flujos visibles, sin sustituir unas verificaciones por otras.

| Criterio de la especificación | Implementación | Verificación |
| --- | --- | --- |
| Cabecera, tarjetas, cuatro conexiones, estilos y textos | React Flow y demo explícita | Verificado: cinco tarjetas y cuatro enlaces; comparación visual con referencia |
| Crear hija desde cualquier nivel, código estable y títulos compuestos | Editor compartido y WorkItemService | Verificado en SQL y UI desde raíz, hija y subtarea; nueva tarjeta seleccionada y centrada |
| Posiciones persistentes y cámara personal | Entidades y endpoints independientes | Verificado: arrastre con conexión, coordenadas y cámara idénticas al volver de Lista; aislamiento personal en SQL |
| Filtros conservan contexto; colapsar conserva posiciones | Visibilidad derivada de IDs autorizados | Verificado: Urgente conserva Totales y sus dos antecesoras; contraer/expandir recupera las cinco tarjetas |
| Tablero, Lista, ficha y mapa comparten registros | Mismo WorkItem y editor común | Verificado: estado de CMP-02 en Lista/Tablero/ficha/mapa; renombrar madre actualiza Productos/Totales |
| Permisos negativos, ciclos y concurrencia | Validación de servidor y tokens | Verificado por pruebas SQL Server, incluidos cambios estructurales simultáneos |
| Errores conservan borrador; conflictos revisables | Estado local del editor y distribución | Verificado: conflicto 409 con comparación/reintento y servidor detenido sin perder borrador; navegación impedida hasta guardar |
| Archivar/restaurar muestra alcance | Preview y operación de rama | Verificado en SQL y UI: rama de dos tareas con confirmación explícita y restauración |
| Teclado, notas/títulos largos y pantalla pequeña | Controles accesibles, truncado y CSS adaptable | Verificado: nota extensa, editor de 390 px, foco cíclico, Escape, menú y Lista móvil |

Capturas: [escritorio](evidence/task-diagram/desktop.png), [móvil](evidence/task-diagram/mobile.png), [texto largo](evidence/task-diagram/long-content.png), [conflicto](evidence/task-diagram/conflict.png), [fallo del editor](evidence/task-diagram/editor-network-error.png) y [movimiento pendiente con servidor detenido](evidence/task-diagram/unsaved-network.png). La composición inicial mantiene las tres columnas y los acentos de la referencia; los estados conservan los nombres reales del CRM. El arrastre no cambia madres.

La prueba de red detuvo únicamente el host `acceptance`: el editor conservó el texto; sidebar, historial y logout conservaron el mapa y el movimiento pendiente. Reanudar y reintentar guardó ese movimiento. La demo principal y la base normal no se usaron para esos fallos. La demo de aceptación quedó detenida y su base conservada.

La base local normal `Desarrollo` recibió únicamente `20261027000502_TaskDiagramIntegrity`, precedida de backup SQL `COPY_ONLY, CHECKSUM` y `RESTORE VERIFYONLY`. La comparación de las columnas originales de **50 tablas y 89 filas** conservó conteos y SHA256 exactos, incluidas sus **7 tareas y 5 posiciones**. Las cuatro migraciones están aplicadas. Recibo local: `.validation/migration-task-diagram-integrity.json`; SQL revisado: `.validation/migration-task-diagram-integrity.sql`; TRX: `.validation/results/task-diagram.trx`.

`-Resume` también se verificó de forma aislada: no repite seed ni migraciones y conserva hashes de tareas, posiciones y vista personal. Recibo local: `.validation/results/script-check.json`.

El alcance está integrado y probado localmente. El CRM general conserva sus pendientes en `docs/matriz-requisitos.md`. Esta carpeta aún no tiene metadatos Git: no se dispone de rama, SHA ni diff y no se ha inicializado un repositorio como parte del cierre.

Referencias técnicas: [nodos personalizados de React Flow](https://reactflow.dev/learn/customization/custom-nodes), [cámara y restauración](https://reactflow.dev/examples/interaction/save-and-restore), [concurrencia de EF Core](https://learn.microsoft.com/en-us/ef/core/saving/concurrency), [transacciones de EF Core](https://learn.microsoft.com/en-us/ef/core/saving/transactions).

Este alcance completa el módulo del mapa. Los pendientes del resto de `crm.md` permanecen en la matriz general; no se consideran entregados por añadir esta vista.
