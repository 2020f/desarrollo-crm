# Chat y notificaciones con actualización automática

Actualización local del 28 de octubre de 2026. Extiende el monolito existente sin añadir tablas, migraciones ni servicios externos.

## Comportamiento

Mientras el CRM permanece abierto y la sesión está autenticada, un monitor compartido consulta la actividad aproximadamente cada **3 segundos**. Espera a que termine la petición anterior antes de programar otra. El navegador puede espaciar los temporizadores en segundo plano; el intervalo no es una garantía de latencia máxima.

Los mensajes nuevos y las ediciones actualizan la conversación abierta sin recargar la aplicación ni borrar el borrador. Los directos existentes y sus no leídos aparecen en la lista de conversaciones. Las respuestas atrasadas de una conversación anterior se descartan al cambiar de chat. Enviar dos veces durante una petición pendiente no duplica esa petición; un fallo conserva el texto.

La campana y la página de notificaciones se actualizan con la misma observación. La página abierta vuelve a consultar también sus avisos leídos para aplicar los permisos actuales. El snapshot no marca mensajes ni notificaciones como leídos: el chat envía su operación de lectura separada cuando muestra la conversación y la página está visible.

La solución usa **consultas HTTP autorizadas**, no eventos push de SignalR. El hub anterior permanece disponible, pero no transporta estos cambios.

## Sonidos

La barra superior incluye **Activar sonido / Sonido activado**. Permite habilitar los tonos, escuchar una prueba al activarlos y silenciarlos. El navegador necesita una interacción de la persona para permitir audio; una interacción con la página intenta habilitarlo si la preferencia está activa. También se vuelve a intentar al interactuar si el dispositivo suspendió el audio.

La preferencia se conserva en ese navegador, separada por organización y usuario. Si el almacenamiento local está restringido, se mantiene durante la sesión. No se sincroniza entre dispositivos.

La primera carga establece una referencia silenciosa. Los IDs ya observados evitan repetir avisos; no suenan el historial, los mensajes propios ni los eventos recibidos mientras estaba silenciado al volver a activar el sonido. Se distingue el tono de mensaje del de notificación y se agrupan llegadas cercanas para evitar ráfagas. El último mensaje entrante se identifica por separado: una respuesta propia posterior no oculta ese aviso.

## Conexión y permisos

Una consulta que tarda más de 10 segundos se cancela y se reintenta. Recuperar conexión, volver a la ventana o hacer visible la pestaña solicita una comprobación inmediata. Cambiar de pantalla conserva el monitor y su referencia; salir de la sesión o cambiar de usuario/organización elimina el monitor anterior y descarta sus respuestas tardías.

Cada consulta valida cuenta, membresía y audiencia actual. El servicio reutiliza los listados autorizados de canales y notificaciones; no entrega cuerpos de mensajes ni textos de avisos en el snapshot. Las consultas ordinarias del chat siguen autorizadas en servidor. Una pérdida de acceso limpia los datos afectados y bloquea nuevas operaciones; una recuperación vuelve a consultar al servidor.

## Contrato e implementación

`GET /api/activity` requiere autenticación y responde con `Cache-Control` de no almacenamiento mediante la política del controlador. Devuelve:

| Campo | Contenido |
| --- | --- |
| `observedAt` | Instante UTC capturado antes de consultar los datos. |
| `channels[].id` | Canal autorizado para la sesión actual. |
| `channels[].latestMessageId`, `latestMessageAuthorId`, `latestMessageAt` | Identidad, autor y fecha UTC del último mensaje vigente. |
| `channels[].latestIncomingMessageId`, `latestIncomingMessageAt` | Último mensaje vigente de otro autor y su fecha UTC. |
| `channels[].changedAt` | Mayor fecha de creación/edición de los mensajes vigentes; detecta ediciones anteriores al último mensaje. |
| `channels[].unreadCount` | Mensajes ajenos posteriores a la última lectura del usuario. |
| `notifications.unreadCount` | Total real de avisos no leídos que la persona todavía puede consultar. |
| `notifications.items[]` | Hasta 100 IDs y fechas UTC de avisos no leídos autorizados. |

Los campos de último mensaje y cambio son nulos en canales vacíos. Los mensajes borrados y las respuestas cuyo hilo raíz ya no es válido se excluyen. El snapshot es de solo lectura.

`ILiveActivityService` y sus DTOs viven en Application; `LiveActivityService` consulta SQL Server desde Infrastructure; `ActivityController` expone el endpoint. `LiveActivityProvider` coordina las consultas y los sonidos en React; Chat, Notificaciones y Layout consumen ese estado.

## Validación y alcance

Validación local comunicada para esta revisión:

- **145/145 pruebas de backend con los fixtures de SQL Server real**, incluyendo nueve casos nuevos de actividad: audiencias, organizaciones ajenas, revocación, no leídos, lectura sin mutación, hilos/borrados, ediciones y límite de avisos con total real.
- **46/46 pruebas frontend**, incluyendo actualización de componentes, borradores, respuestas tardías, recuperación, revocación, sonidos y deduplicación.
- **1/1 comprobación de assets** y compilaciones .NET y TypeScript/Vite correctas. Vite conserva el aviso previo de tamaño del fragmento principal.
- Recorrido real en una base demo aislada: dos usuarios con sesiones independientes intercambiaron mensajes en ambos sentidos sin recargar la página receptora; el segundo mensaje conservó el borrador. Una asignación nueva apareció en la bandeja ya abierta y la campana volvió a cero al marcarla como leída. El control de sonido activó y silenció el audio en el navegador; las pruebas unitarias verifican los tonos, sin afirmar percepción auditiva en otro dispositivo. Escritorio y móvil a 390 px comprobados, sin desbordamiento horizontal y con conversación de 358 px de ancho. El host de prueba se detuvo conservando su base.

Evidencias locales, sin credenciales: `.validation/live-chat-validation.json`, `.validation/live-chat-browser.png`, `.validation/live-chat-draft-browser.png`, `.validation/live-notification-browser.png` y `.validation/live-chat-mobile.png`. El recibo privado de acceso de la demo permanece excluido de Git.

Los comandos reproducibles están en [tests/README.md](../tests/README.md). El checkout contiene también cambios previos de Carpetas, que no se atribuyen a esta función.

Esta revisión está validada localmente. Publicar el código en GitHub no actualiza por sí solo la instalación del CRM; el despliegue se realiza por separado siguiendo [DESPLIEGUE.md](../DESPLIEGUE.md). No incorpora notificaciones push del sistema operativo ni recepción con la aplicación cerrada. Las preferencias avanzadas de notificación, las cuotas/rendimiento medido y una futura entrega por SignalR siguen pendientes. Este cambio no representa la terminación de todos los requisitos de `crm.md`.
