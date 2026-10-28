# Pruebas de Desarrollo

Ejecutar desde la raíz con .NET SDK 10 y SQL Server real:

```powershell
dotnet test Desarrollo.slnx --no-restore --verbosity minimal
```

La conexión predeterminada usa `localhost`, autenticación de Windows y certificado local de confianza. Para otra instancia puede definirse `DESARROLLO_TEST_CONNECTION_STRING` en el entorno; no guardar contraseñas en archivos del repositorio ni imprimir la variable. Ejemplo sin secretos:

```powershell
$env:DESARROLLO_TEST_CONNECTION_STRING = 'Server=localhost;Trusted_Connection=True;TrustServerCertificate=True'
dotnet test Desarrollo.slnx --no-restore
```

Cada fixture sustituye siempre el catálogo configurado por `Desarrollo_Test_<GUID>`. Migra esa base temporal y comprueba su nombre configurado y el resultado real de `DB_NAME()` antes de eliminarla. Nunca apunta al catálogo normal `Desarrollo`. La cuenta SQL necesita crear y eliminar bases de prueba.

`PermissionIntegrationTests` cubre aislamiento, lectura de proyectos públicos y estados iniciales. `TaskDiagramIntegrationTests` comprueba permisos de organización y proyecto, revocación y avisos, jerarquía sin ciclos incluso entre dos contextos simultáneos, estados y transiciones, asignación a destinatarios autorizados, versiones obsoletas de tarjetas y posiciones, lotes atómicos, posiciones iniciales, reintentos de creación, cámara personal, preservación de campos del mapa y archivado/restauración de ramas. Una prueba crea y elimina un trigger exclusivamente dentro de la base temporal para forzar un fallo de persistencia y verificar rollback real. `SqlServerTestDatabaseSafetyTests` comprueba las barreras que impiden seleccionar bases normales.

Validación local del 26 de octubre de 2026, 23:02 (America/Santo_Domingo), con .NET SDK 10.0.201 y SQL Server real: `dotnet build Desarrollo.slnx --no-restore --verbosity minimal` terminó con 0 advertencias y 0 errores. La ejecución siguiente recompiló y completó **34 pruebas correctas, 0 fallos y 0 omitidas**, incluidas las 26 pruebas del mapa:

```powershell
dotnet test Desarrollo.slnx --no-restore --logger 'trx;LogFileName=task-diagram.trx' --results-directory .validation/results --verbosity minimal
```

El recibo local queda en `.validation/results/task-diagram.trx`. Esta ejecución no presentó la restricción de autenticación del sandbox documentada durante el trabajo anterior; no se modificaron TLS ni credenciales de SQL Server. Los resultados deben corresponder a una compilación actual; `--no-build` sobre binarios anteriores no valida cambios nuevos. Las pruebas no sustituyen la revisión visual y los recorridos de interfaz.

## Regresiones de interacción del mapa

Desde la raíz, instalar las dependencias fijadas y ejecutar:

```powershell
npm --prefix src/Desarrollo.Web/ClientApp ci
npm --prefix src/Desarrollo.Web/ClientApp run test:diagram
```

Los cuatro casos de `tests/frontend` usan el ejecutor de pruebas integrado de Node.js, `jsdom` 26.1.0 como dependencia de desarrollo y el TypeScript ya utilizado por el frontend. Cada archivo ejecuta componentes reales en un DOM aislado. Los adaptadores del lienzo, las llamadas de API y la sesión se controlan dentro de las pruebas; no requieren servidor ni base de datos y no sustituyen los recorridos visuales de aceptación.

- Selección y centrado de una hija recién guardada durante una recarga demorada y eventos antiguos de selección.
- Guardado pendiente antes de navegar, historial, salida del documento y conservación de la pantalla ante errores.
- Bloqueo de navegación y controles durante el cierre de sesión y su refresco; recuperación tras fallo de red.
- Menú móvil, estado accesible, foco, recorrido con Tab, Escape, fondo y cierre por navegación o ampliación de pantalla.

Validación local con Node.js 24.14.1: **4 casos correctos, 0 fallos y 0 omitidos** mediante `npm run test:diagram`.

## Calendario integrado

`CalendarIntegrationTests` añade 14 casos contra SQL Server real: proyecciones sin copias, creación/cambio/eliminación de fechas, fuentes y periodos civiles, Santo Domingo y límites exclusivos, permisos negativos, revocación, vínculos, eventos manuales y avisos autorizados.

Validación del 27/10/2026: `dotnet build Desarrollo.slnx --no-restore --verbosity minimal` terminó con 0 errores y 0 advertencias; la batería completa finalizó con **48/48 pruebas correctas, 0 fallos y 0 omitidas**.

```powershell
dotnet test Desarrollo.slnx --no-restore --logger 'trx;LogFileName=calendar-integration.trx' --results-directory .validation/results --verbosity minimal
npm --prefix src/Desarrollo.Web/ClientApp run test:calendar
```

`tests/frontend/calendar.test.cjs` y `task-editor-date.test.cjs` ejecutan componentes reales con adaptadores DOM: fechas civiles, las cuatro vistas, navegación a la fuente, editor manual, errores/reintento y payload del vencimiento `29 → 30 → null`. Resultado: **6/6 correctas**; las cuatro regresiones del mapa también pasan. El recorrido real con teclado nativo y las capturas se describen en `docs/calendario-integrado.md`.

## Documentos y Carpetas

`DocumentIntegrationTests` cubre 17 casos SQL: tres audiencias, lectores válidos, acceso del autor, restricciones frente a administradores, versiones/avisos protegidos, revocación, concurrencia y contenido enriquecido validado. `FileIntegrationTests` añade 15 casos SQL y disco: privados y públicos, contextos autorizados, conteos/páginas, descarga/preview, revocación, archivos falsificados, rutas, copia interrumpida, fallo de base y retirada lógica sin perder bytes.

```powershell
dotnet test Desarrollo.slnx --no-restore --logger 'trx;LogFileName=documents-files.trx' --results-directory .validation/results --verbosity minimal
npm --prefix src/Desarrollo.Web/ClientApp run test:frontend
npm --prefix src/Desarrollo.Web/ClientApp run build
npm --prefix src/Desarrollo.Web/ClientApp run test:assets
```

Validación del 27/10/2026: **80/80 SQL**, **19/19 frontend** y **1/1 assets** correctas. Las pruebas DOM usan el editor Tiptap real; la regresión de assets requiere el bundle generado y compara sus imports con el entry Razor. El recorrido real suma 15 comprobaciones en navegador y evidencia escritorio/móvil, separado de las pruebas DOM. Docker conserva SQL, archivos y cookie al recrear el contenedor web. Detalles y límites en [archivos-documentos.md](../docs/archivos-documentos.md).

## Seguridad e inicialización del despliegue

Validación del 28/10/2026: compilación .NET con 0 errores y 0 advertencias; **136/136 pruebas, 0 fallos y 0 omitidas**, con los fixtures de SQL Server real y diez casos del proxy/antiforgery. Recibo local: `.validation/results/predeployment.trx`.

- `AccountDeploymentIntegrationTests`: primera cuenta solo en una base vacía, contraseñas de Identity, rollback de contraseña inválida, inicialización concurrente y sesión con membresía/cuenta activas.
- `TicketSecurityIntegrationTests`: acceso por organización/proyecto, tickets personales, capacidades de operación, asignaciones y vínculos válidos, notas internas, conteos, archivos y notificaciones sin filtraciones.
- `ChatSecurityIntegrationTests`: acceso actual al canal, retirada de miembros, privados/directos, edición de autor, paginación, raíces de hilo y conexión SignalR; otros casos comprueban avisos históricos de mensajes y canales.
- `ReverseProxySecurityTests`: middleware real y antiforgery con cookies seguras; HTTPS solo desde una IP de proxy explícita, rechazo de pares ajenos, límite de un salto, configuración vacía deshabilitada y rechazo de nombres/redes. No procesa `X-Forwarded-For` ni `X-Forwarded-Host`.

```powershell
dotnet build Desarrollo.slnx --no-restore --verbosity minimal
dotnet test Desarrollo.slnx --no-build --no-restore --logger 'trx;LogFileName=predeployment.trx' --results-directory .validation/results --verbosity minimal
npm --prefix src/Desarrollo.Web/ClientApp run test:security
```

Las siete pruebas DOM de autenticación y solicitudes comprueban registro cerrado/abierto, fallos de sesión, capacidades de lector, notas internas, conservación del borrador y respuestas tardías al cambiar de ticket. Usan componentes reales con API controlada; no equivalen a una prueba de navegador contra el servidor. La batería frontend completa también las incluye: **26/26 correctas** el 28/10/2026; build TypeScript/Vite y **1/1 regresión de assets** correctos. [Recibo sin credenciales](../docs/evidence/predeployment/validation.json).

GitHub Actions compila frontend/backend y ejecuta la batería contra un SQL Server Linux aislado. La contraseña de prueba se genera dentro de cada ejecución y no se guarda en el repositorio. Los comandos para probar instalación, respaldo y recuperación están en [DESPLIEGUE.md](../DESPLIEGUE.md).

## Actualización automática de chat y sonidos

Validación local del 28/10/2026: **145/145 pruebas de backend con los fixtures SQL Server reales**, **46/46 frontend**, **1/1 assets** y compilaciones .NET y TypeScript/Vite correctas. Vite mantiene el aviso previo de tamaño del fragmento principal. Dos sesiones reales de navegador verificaron intercambio en ambos sentidos sin recarga, borrador conservado, bandeja y contador automáticos, activación/silencio de audio y escritorio/móvil a 390 px sin desbordamiento. Recibo local sin credenciales: `.validation/live-chat-validation.json`. Estos resultados no acreditan un despliegue.

`LiveActivityIntegrationTests` añade nueve casos SQL sobre el snapshot autorizado: mensajes propios/ajenos, última lectura sin modificarla, organizaciones y chats privados, revocación de cuentas/membresías/roles, mensajes borrados/hilos inválidos, edición de un mensaje anterior y muestra limitada con total exacto de avisos. También comprueba que un último mensaje propio no oculte la identidad del último entrante.

`live-chat.test.cjs` usa componentes reales con HTTP/audio controlados para comprobar recepción sin recarga, conservación del borrador, respuestas tardías, envío pendiente, notificaciones, recuperación y deduplicación de sonidos. `sound.test.cjs` comprueba el adaptador de tonos y su ciclo de vida. Estas pruebas DOM no sustituyen dos sesiones reales de navegador ni confirman que un dispositivo emita audio audible.

```powershell
dotnet build Desarrollo.slnx --no-restore --verbosity minimal
dotnet test Desarrollo.slnx --no-build --no-restore --logger 'trx;LogFileName=live-chat.trx' --results-directory .validation/results --verbosity minimal
npm --prefix src/Desarrollo.Web/ClientApp run test:live
npm --prefix src/Desarrollo.Web/ClientApp run test:frontend
npm --prefix src/Desarrollo.Web/ClientApp run build
npm --prefix src/Desarrollo.Web/ClientApp run test:assets
```

El recibo SQL local es `.validation/results/live-chat.trx`. El cambio no añade migraciones. Detalles y límites en [chat-actualizacion-automatica.md](../docs/chat-actualizacion-automatica.md). La batería completa también incluye los cambios anteriores de Carpetas, conservados por separado del alcance de esta función.

## Demo explícita para validar la interfaz

Después de compilar backend y frontend:

```powershell
.\scripts\Start-TaskDiagramDemo.ps1
```

El script inicia un host oculto en un puerto libre desde 5017, usando exclusivamente `Desarrollo_DiagramDemo_<GUID>`. Las migraciones se habilitan solo al crear una demo nueva. Registra un equipo, proyecto Tienda demo y las cinco tarjetas CMP por HTTP autenticado y antiforgery; guarda un layout de tres columnas. No crea una cámara personal: la primera visita ajusta el encuadre al tamaño real de la pantalla. No usa el arranque normal como seed ni envía correos. El acceso temporal queda únicamente en `.validation/demo-session.json`, excluido de Git; la salida del script no muestra la contraseña. Puede usarse una instancia distinta mediante `DESARROLLO_DEMO_CONNECTION_STRING`; su catálogo siempre se sustituye por el nombre demo generado.

Una repetición mientras el mismo host está activo devuelve la sesión existente. Para reiniciar sin crear otra base ni duplicar tarjetas:

```powershell
.\scripts\Stop-TaskDiagramDemo.ps1
.\scripts\Start-TaskDiagramDemo.ps1 -Resume
```

Para una sesión adicional usar `-SessionName otro-demo -Port 5018` en Start y el mismo `-SessionName` en Stop. Stop conserva la base por defecto; `-RemoveDatabase` solicita la limpieza explícita de esa demo. Antes de detener comprueba PID, fecha de inicio y comando; antes de eliminar comprueba instancia, prefijo GUID y `DB_NAME()`. La demo principal y la base normal no se borran al limpiar otra sesión.

`-Resume` autentica y comprueba el acceso al proyecto y mapa existentes. No repite el seed ni aplica migraciones o valores iniciales: conserva títulos editados, jerarquía, posiciones, cámara y ramas contraídas. Si necesita actualizarse el esquema, se hace explícitamente antes de reanudar.

Verificación local del 26 de octubre de 2026, 23:06 (America/Santo_Domingo), en una demo adicional `script-check`: la creación produjo cinco tareas y ninguna fila de cámara personal. Después se modificaron un título, una nota, una posición y la cámara con una rama contraída. La reutilización del host activo y el ciclo detener/reanudar conservaron los mismos hashes SQL de `WorkItems`, `TaskDiagramPositions` y `TaskDiagramUserViews`, incluidos sus tokens de versión; la API confirmó los valores y las cinco tareas sin duplicados. Recibo local sin credenciales: `.validation/results/script-check.json`. El host adicional quedó detenido y su base demo se conservó.
