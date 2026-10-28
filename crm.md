OBJETIVO Y ALCANCE

Actúa como un agente de desarrollo full stack responsable de implementar una plataforma web completa para centralizar y organizar el desarrollo de productos de software. Construye software funcional, persistente, verificable y mantenible. El resultado debe permitir recibir solicitudes, priorizar trabajo, asignarlo, desarrollarlo, revisarlo, validarlo, entregarlo y conservar su evidencia.

El producto reúne proyectos, tareas, tarjetas amplias con checklists, documentación extensa, imágenes, calendario, usuarios por proyecto, menciones, notificaciones, chat interno, tickets, estados configurables, tablas dinámicas por rol, reportes y referencias al código. Puede mostrar clientes y contactos asociados a solicitudes. Un embudo comercial, campañas y facturación del propio servicio no forman parte del núcleo definido.

La unidad de ejecución es un registro de trabajo único. Su representación en tablero, tabla, calendario, búsqueda, panel personal y conversación debe conservar identidad, datos e historial. Evita copias divergentes de una tarea para cada vista.

El escenario inicial es un equipo que administra varios productos y proyectos, con colaboradores e invitados limitados. Prepara aislamiento por organización para una futura evolución SaaS, sin exigir planes comerciales para utilizar la primera versión.

Conserva todos los módulos y reglas descritos aquí. Organiza la ejecución por fases, pero no reduzcas silenciosamente el encargo a un tablero, un CRUD, una demostración visual o un frontend con datos fijos. Las ampliaciones identificadas expresamente como futuras deben permanecer documentadas en el backlog; no deben presentarse como funcionalidades ya construidas.

FORMA DE TRABAJO DEL AGENTE

Antes de modificar archivos, inspecciona el repositorio, las instrucciones aplicables, la solución existente, el estado de Git, las dependencias, las migraciones y las convenciones de código. Distingue cambios existentes del usuario de tu trabajo. Si ya existe una implementación aprovechable, extiéndela respetando sus decisiones compatibles con este encargo.

No borres ni reemplaces trabajo ajeno, bases de datos o configuraciones reales para facilitar la implementación. Evita operaciones destructivas y migraciones que pierdan datos sin un plan explícito y autorización cuando corresponda. No expongas secretos en terminales, logs, capturas, documentación o commits.

Resuelve autónomamente decisiones reversibles y rutinarias. Mantén un registro breve de arquitectura, alcance, avances, pruebas y bloqueos. Si una credencial o servicio externo falta, completa lo independiente, implementa la configuración necesaria y explica exactamente qué integración sigue bloqueada. No simules éxito ni inventes acceso.

Trabaja hasta completar el alcance comprometido y sus criterios de aceptación. Si el entorno impide terminarlo, entrega el estado real y los pasos pendientes. No publiques, despliegues sobre producción, envíes mensajes reales a terceros ni actives integraciones externas sin autorización para esa acción concreta.

STACK OBLIGATORIO Y SU INTERPRETACIÓN

Usa C# con ASP.NET Core MVC como aplicación anfitriona y backend; React con TypeScript como interfaz principal; Entity Framework Core como acceso a datos; SQL Server como base relacional. No sustituyas SQL Server por otra base ni React por Blazor o por una interfaz completa en Razor.

La interpretación exacta de MVC con React será esta: ASP.NET Core MVC sirve una vista Razor inicial que monta React y expone controladores HTTP que devuelven JSON. React administra navegación, pantallas, formularios, editor, tabla, tablero, calendario y paneles. Los controladores llaman a servicios de aplicación; el servidor ejecuta autorización, validaciones y reglas de negocio. Razor cumple la función de shell inicial, sin duplicar las mismas pantallas en dos tecnologías.

Mantén frontend y backend bajo el mismo origen para el despliegue principal. Configura rutas de React y acceso directo a URLs internas sin que el fallback del frontend oculte errores de API. Usa ASP.NET Core Identity, cookies seguras y protección antiforgery/CSRF en las operaciones que correspondan. Conserva consistencia entre peticiones HTTP y autenticación de SignalR.

Selecciona versiones estables y soportadas después de comprobar el entorno y la documentación oficial vigente. Fija las versiones y conserva archivos de bloqueo de dependencias. Justifica bibliotecas de editor, tabla, calendario y carga de archivos considerando mantenimiento, compatibilidad, accesibilidad y licencia. No utilices componentes comerciales de evaluación como dependencia oculta.

ARQUITECTURA DE LA SOLUCIÓN

Implementa un monolito modular con límites claros: Identidad, Organización, Proyectos, Trabajo, Documentación, Conversaciones, Archivos, Notificaciones, Planificación, Solicitudes, Entregas, Automatización e Integraciones. No introduzcas microservicios, múltiples bases o infraestructura adicional sin una necesidad concreta.

En un repositorio nuevo, una estructura apropiada es: Desarrollo.Domain para entidades y reglas; Desarrollo.Application para casos de uso y contratos; Desarrollo.Infrastructure para EF Core y adaptadores; Desarrollo.Web para MVC, endpoints, shell Razor, React y hubs; un trabajador de procesos persistentes, dentro del host o como proyecto separado según las necesidades. Añade proyectos de pruebas con responsabilidades claras. Adapta esta estructura si existe una equivalente válida.

Usa DTO específicos para entradas y salidas, validación en servidor y respuestas de error consistentes. No expongas entidades de EF directamente ni permitas asignación masiva de propiedades sensibles. Implementa paginación, búsqueda, orden y filtros del lado servidor en colecciones grandes. Centraliza políticas de acceso y evita reproducir reglas críticas únicamente en React.

Persiste primero los cambios y los eventos pendientes en una transacción. Un trabajador debe procesar recordatorios, notificaciones, correos, miniaturas, exportaciones y webhooks con reintentos e idempotencia. SignalR comunica novedades a la interfaz; SQL Server conserva mensajes, avisos, lecturas e historial. No dependas de memoria local para trabajos que deben sobrevivir reinicios.

DISEÑO VISUAL OBLIGATORIO

Implementa como apariencia predeterminada la paleta oscura de las imágenes de referencia. Fondo principal azul noche #0B1020; navegación lateral y barra superior #10172A; panel principal azul oscuro #172238; paneles secundarios en índigo #202441, ciruela #302439 y verde petróleo #152D35. Las tarjetas deben combinar estos tonos de manera deliberada, sin convertir toda la aplicación en una superficie uniforme.

Usa lavanda #9B8AFB para acciones principales y selección; turquesa #57D6C7 para avances y éxito; ámbar #EAC385 para avisos; coral únicamente para urgencia o error. Texto principal cercano a blanco #F0F3FC y secundario #A8B7D1. Conserva contraste suficiente, bordes discretos, iconos de línea, esquinas de aproximadamente 12 px y sombras suaves. Evita neón, brillos intensos, gradientes decorativos excesivos y texto diminuto.

Mantén tipografía, escala, espaciados, botones, estados, avatares y composición coherentes. Ofrece densidad cómoda y compacta. El modo claro permanece como preferencia secundaria de la propuesta, sin sustituir el tema oscuro solicitado. Implementa tokens de diseño para cambiar el tema sin duplicar componentes.

Las imágenes generadas son referencias visuales. Sus cantidades, fechas, nombres de ejemplo, porcentajes y estados no son datos reales ni especificaciones numéricas autoritativas. Resuelve inconsistencias de los mockups mediante las reglas funcionales de este prompt. No conviertas errores de texto de una imagen en requisitos. No uses imágenes completas de pantallas como sustituto de componentes interactivos.

NAVEGACIÓN Y PANTALLAS

Conserva una barra lateral clara con el nombre provisional Desarrollo, selector Equipo de desarrollo y accesos Inicio, Mi trabajo, Proyectos, Solicitudes, Calendario, Documentos, Chat, Reportes y Administración. La barra superior incluye buscador global, creación rápida, notificaciones y perfil. Adapta las opciones a los permisos reales del usuario.

Implementa estas veinte vistas principales, con rutas navegables y estados funcionales:

Inicio: prioridades personales, proyectos, revisiones, bloqueos y actividad.

Mi trabajo: asignadas, creadas, seguidas, revisiones, hoy, vencidas y próximas.

Proyectos: portafolio con filtros, estados, responsables, avance y próxima entrega.

Resumen de proyecto: objetivo, alcance, componentes, equipo, hitos, riesgos y actividad.

Backlog: orden de prioridad, épicas, ideas y asignación a ciclos.

Kanban: tarjetas, estados, responsables, checklist y bloqueos.

Detalle de tarea: contenido amplio, aceptación, relaciones, imágenes, comentarios y código.

Tabla dinámica: columnas, campos propios, filtros, edición y vistas por rol.

Calendario: tareas, reuniones, hitos, recurrencias y entregas.

Documentos: árbol de páginas, editor extenso, comentarios e historial.

Archivos: galería, búsqueda, previsualización, versiones y relaciones.

Chat: canales, hilos, menciones, adjuntos y conversión a tareas o decisiones.

Notificaciones: no leídas, menciones, revisiones, pospuestas y preferencias.

Tickets: recepción, clasificación, filtros, prioridad y responsable.

Detalle de ticket: reproducción, evidencia, notas internas y respuesta compartida.

Revisión y QA: cola, ambiente, pruebas, observaciones y aprobación o devolución.

Entregas: versiones, alcance, aceptación, ambientes y notas de publicación.

Reportes: flujo, calidad, entregas y acceso a sus registros de origen.

Administración: miembros, roles, campos, estados, automatizaciones e integraciones.

Inicio de sesión: acceso, recuperación y continuación de invitaciones.

Incluye los formularios, diálogos, ajustes y estados secundarios que estos recorridos requieren. Toda acción visible debe funcionar o indicar claramente una integración aún no configurada. Implementa carga, vacío, error, éxito, acceso denegado y desconexión; no llenes pantallas vacías con resultados inventados.

MODELO DE INFORMACIÓN Y PERSISTENCIA

Modela Organización, Usuario, MiembroOrganización, Producto, Proyecto, MiembroProyecto, Rol, Permiso, Componente, Épica, ElementoTrabajo, RelaciónTrabajo, Checklist, ItemChecklist, Estado, Transición, Ticket, Documento, VersiónDocumento, CampoPersonalizado, ValorCampo, VistaGuardada, Canal, MiembroCanal, Mensaje, Lectura, Adjunto, Mención, Notificación, PreferenciaNotificación, EventoCalendario, Hito, Ciclo, VersiónEntrega, ReferenciaRepositorio, Despliegue, Auditoría, ReglaAutomatización y EjecuciónAutomatización.

Agrupa o especializa entidades cuando mejore el diseño, conservando sus capacidades y relaciones. Define claves, restricciones, índices, integridad referencial, fechas de auditoría y control de concurrencia. Indexa según organización, proyecto, estado, responsable y vencimiento, basándote en consultas reales. No crees una tabla SQL física por cada tablero o vista del usuario.

Una tarea tiene identidad estable y código legible, como GOGO-142. Usa identificadores internos seguros y únicos, sin convertir la dificultad de adivinarlos en el control de acceso. Verifica que las relaciones entre registros pertenecen a ámbitos compatibles y autorizados.

Versiona el esquema con migraciones EF Core. Documenta cómo crearlas, revisarlas y aplicarlas. Separa el seed demostrativo del arranque normal y de producción. Los datos de ejemplo de GOGO Deliveri, Odontari o TEresuelvo solo se cargarán de forma explícita en desarrollo o demostración.

PRODUCTOS PROYECTOS Y ORGANIZACIÓN DEL TRABAJO

Una organización contiene miembros y productos; un producto evoluciona mediante proyectos. Un proyecto define iniciativa, objetivo, alcance, líder, miembros, fechas, prioridad, estado, riesgos y enlaces. Sus estados generales son Propuesto, Activo, En pausa, Finalizado y Archivado. Permite archivar y consultar historial sin eliminar el trabajo asociado.

Producto y épica serán opcionales para proyectos pequeños. El formulario rápido de una tarea no debe exigir completar una jerarquía innecesaria. Los componentes representan partes del software: Cliente, Rider, Comercio, API o Admin son ejemplos, no valores fijos obligatorios.

Relaciona proyectos cuando haya dependencias, respetando sus permisos. El portafolio debe mostrar próximos compromisos y avance con una definición visible. Una épica agrupa un resultado; una subtarea representa ejecución propia; una dependencia indica precedencia. Mantén estas relaciones diferenciadas.

Un ciclo o sprint organiza cuándo se planea trabajar; una versión agrupa qué se publicará junto. Permite Kanban continuo sin ciclos. El backlog mantiene orden, ideas no comprometidas y trabajo listo para ejecutar. Conserva cambios de alcance y de fechas para evitar que una reprogramación borre el compromiso original.

CUENTAS ROLES Y MEMBRESÍAS

Implementa inicio y cierre de sesión, recuperación de acceso, invitaciones con vencimiento, perfil, avatar, zona horaria, preferencias y gestión de sesiones. Permite activar o desactivar cuentas y exigir autenticación multifactor según la política. No distribuyas contraseñas fijas de administración; proporciona un mecanismo explícito y seguro de inicialización.

La pertenencia a la organización no concede acceso automático a todos los proyectos. Los proyectos serán privados por defecto. Una persona puede tener roles distintos en proyectos distintos. Conserva la autoría histórica al retirar o desactivar a alguien y ofrece reasignación de sus pendientes.

Roles iniciales: Administrador de organización, Líder de proyecto, Responsable de producto, Desarrollador o diseñador, QA o revisor, Cliente o invitado y Lector. Implementa roles como conjuntos de capacidades y permite configuración autorizada. Separar leer, comentar, crear, editar, asignar, cambiar estado, aprobar, exportar, archivar, eliminar, administrar miembros y configurar flujos.

El responsable de producto define requisitos y aceptación; el líder organiza el trabajo; desarrollo ejecuta; QA valida; invitados acceden solo a recursos compartidos. El administrador gestiona políticas; cualquier acceso excepcional a contenido restringido debe ser explícito y auditable. Impide que un usuario se conceda atribuciones que no puede administrar.

AUTORIZACIÓN EFECTIVA

Aplica autorización en servidor a cada consulta y operación. Distingue presentación, capacidad y alcance. Una vista por rol configura columnas y filtros; no sustituye los permisos sobre registros o campos. No envíes datos restringidos al navegador para esconderlos después con CSS.

Comprueba organización, membresía, recurso, sensibilidad y operación. Protege API, búsqueda, sugerencias, conteos, relaciones, descargas, miniaturas, exportaciones, menciones, notificaciones, trabajos en segundo plano y SignalR. Los filtros globales de EF pueden ayudar, pero no serán la única barrera.

Antes de crear una relación o compartir un archivo, valida ambos extremos y la audiencia resultante. Un enlace a un recurso privado no debe revelar título, contenido o miniatura a quien no puede acceder. Las exportaciones aplicarán los mismos permisos que las pantallas.

Al retirar permisos, bloquea nuevas operaciones, revoca lo correspondiente y elimina suscripciones activas. Revalida acceso al entregar avisos en cola, correos o eventos de tiempo real. No confíes en que pertenecer a un grupo SignalR implica autorización vigente. Registra cambios de acceso y denegaciones relevantes sin incluir secretos ni contenido privado innecesario.

TAREAS TARJETAS Y TIPOS DE TRABAJO

Implementa tarjetas con ID, título, responsable, estado, prioridad, vencimiento, bloqueo y progreso de checklist. Permite portada opcional. El detalle debe abrirse como panel lateral redimensionable y como página completa, conservando filtros, posición y contexto al cerrarse.

Campos del trabajo: tipo, título, descripción enriquecida, objetivo, contexto, proyecto, producto y componente; autor; responsable principal, colaboradores, revisores y seguidores; prioridad, inicio, vencimiento y estimación; ciclo, épica y versión; relaciones; aceptación, evidencias, comentarios y actividad; referencias a ramas, PR, commits, CI, pruebas y despliegues.

Usa un responsable principal y colaboradores explícitos. Tipos iniciales: funcionalidad, historia de usuario, tarea técnica, error, investigación y deuda técnica. Los formularios se adaptan al tipo. Un error necesita reproducción, esperado, observado, ambiente, versión afectada, severidad y evidencia. Una investigación necesita pregunta y conclusión.

Ofrece creación rápida con pocos campos, pero exige los datos definidos antes de iniciar desarrollo. Incluye editar, seguir, duplicar, archivar, restaurar desde papelera y cambios en lote autorizados. Informa el alcance de operaciones masivas y conserva historial de responsable, fechas, prioridad, estado y contenido relevante.

Las plantillas iniciales serán Funcionalidad, Error reproducible, Revisión de código, Validación QA y Publicación. Las plantillas deben poder reutilizar campos y checklists sin copiar identificadores ni relaciones privadas de forma incorrecta.

CHECKLISTS SUBTAREAS Y ACEPTACIÓN

Permite varias listas por tarea, elementos reordenables, casillas de verificación, responsable opcional y plantillas reutilizables. Muestra progreso como elementos resueltos sobre elementos totales. Conserva quién completó cada paso cuando sea necesario para la evidencia.

Convierte un paso en subtarea si necesita responsable, estado, fecha, conversación o seguimiento independiente. Mantén vínculo de origen y evita contar el mismo esfuerzo dos veces después de convertirlo. Una subtarea conserva sus datos y permisos dentro del contexto del proyecto.

Separa checklist, criterios de aceptación y validación. Completar cuatro casillas no debe aceptar, publicar ni cerrar automáticamente una tarea. Cada tipo de trabajo define qué evidencia y aprobación necesita. La interfaz debe explicar por qué una transición está impedida y permitir corregir la información faltante.

ESTADOS TRANSICIONES Y DEPENDENCIAS

Flujo inicial: Backlog, Lista para desarrollar, En desarrollo, En revisión, En pruebas, Lista para publicar y Publicada. Los trabajos que no se despliegan terminan en Aceptada. Conserva categorías internas estables: pendiente, en curso, finalizado y cancelado. Distingue cancelado, rechazado y duplicado de una entrega aceptada.

Permite renombrar y agregar estados por proyecto mediante configuración autorizada. Cada transición tendrá permisos, validaciones y condiciones. Antes de retirar un estado, migra sus elementos de forma explícita y valida referencias. Audita actor, fecha, estado anterior, nuevo estado y motivo cuando corresponda.

Bloqueada será una condición que puede coexistir con la etapa actual. Registra motivo, dependencia, fecha, responsable de resolverla y resolución. Impide dependencias circulares, incluidas las introducidas por edición. Distingue relación padre/subtarea, bloquea, bloqueada por, duplicada de y relacionada con.

Un rechazo en QA vuelve a desarrollo conservando observaciones y pruebas. Una reapertura registra motivo sin borrar aceptación o publicación anterior. Los cambios que afecten compromisos relacionados deben advertirse; no reprogrames silenciosamente otras fechas.

Trata PR integrado, QA aprobado y despliegue exitoso como eventos diferentes. Si una tarea tiene varios PR, no supongas que el primero integrado termina el trabajo. La publicación requiere aceptación y evidencia verificable de despliegue o confirmación manual autorizada.

TABLAS CAMPOS Y VISTAS CONFIGURABLES

Implementa tabla, tablero, lista, calendario y galería sobre los mismos registros. Ofrece filtros por proyecto, responsable, tipo, prioridad, etiqueta, fechas, estado y campos propios, con condiciones AND/OR acotadas y validadas. Añade ordenar, agrupar, ajustar anchuras, fijar columnas, mostrar subtareas y editar celdas.

Guarda vistas personales, compartidas por proyecto y plantillas iniciales por rol. Conserva columnas, orden, agrupación, densidad y filtros como configuración; no copies tareas. Filtros relativos como mis tareas, esta semana, sin responsable y bloqueadas deben evaluarse para el usuario y momento actuales.

Campos iniciales: texto, número, fecha, selección, selección múltiple, sí/no, usuario y URL. Cada campo tendrá ID estable, tipo, validación, opciones, obligatoriedad y permisos de lectura/edición. Los cambios de tipo deben previsualizar impacto y preservar valores que no puedan convertirse. Desactivar un campo no debe borrar datos silenciosamente.

Prepara vistas de Desarrollo, QA, Liderazgo y Cliente. QA destacará ambiente, versión, severidad y evidencia; Desarrollo destacará responsable, bloqueo y PR. Editar una vista compartida exige permiso. Los totales y agregaciones respetan acceso y explican qué representan.

Las relaciones configurables, agregaciones avanzadas y fórmulas pertenecen a la evolución prevista. El primer núcleo usa entidades estables y campos tipados, con límites de consulta y ordenamiento seguros. No aceptes SQL o expresiones ejecutables arbitrarias desde un campo o filtro.

DOCUMENTOS NOTAS Y DECISIONES

Construye una base de conocimiento por proyecto con páginas y subpáginas: visión, requisitos, reglas de negocio, arquitectura, acuerdos, instalación, manuales y notas de entrega. El editor soportará texto largo, encabezados, listas, tablas sencillas, código, enlaces, imágenes y checklists.

Implementa autoguardado visible, borradores, historial de versiones, restauración, comentarios, menciones, permisos por documento, etiquetas, favoritos, recientes y búsqueda de contenido. Vincula documentos con tareas, tickets, proyectos y decisiones. Las plantillas cubrirán requisitos, reuniones, decisiones técnicas, errores y entregas.

Registra decisiones con contexto, alternativas, decisión, responsable, fecha y consecuencias. Permite convertir acuerdos de una reunión en tareas manteniendo el enlace de origen. Compartir una página no deberá compartir automáticamente todos sus recursos relacionados.

Usa control de concurrencia: ante una versión desactualizada, conserva el borrador y permite resolver el conflicto. No sobrescribas trabajo ajeno de manera silenciosa. La coedición simultánea carácter por carácter será ampliación explícita; no presentes autoguardado como equivalente. Sanitiza contenido enriquecido y valida enlaces o incrustaciones para evitar ejecución de contenido arbitrario.

IMÁGENES ARCHIVOS Y GALERÍA

Implementa selección de archivos, arrastrar y soltar y pegado de imágenes del portapapeles. Muestra progreso, cancelación o reintento cuando proceda, errores comprensibles, miniaturas, ampliación, descripción, autor, fecha, tipo y tamaño. La galería permite buscar, filtrar y acceder al contexto del archivo.

Conserva versiones y originales cuando se sustituya evidencia. Relaciona archivos con tareas, documentos, mensajes y tickets sin duplicarlos innecesariamente. Cada vínculo conserva su contexto; ampliar audiencia exige permiso explícito. Miniaturas, previsualizaciones y descargas tienen la misma protección que el original.

Usa almacenamiento privado, preferiblemente Azure Blob mediante un adaptador, y metadatos en SQL Server. Para desarrollo puede existir almacenamiento local privado detrás de endpoints autorizados, nunca una carpeta pública que eluda permisos. No inventes credenciales cloud.

Valida extensión, tipo real, tamaño, nombre y límites por proyecto; usa nombres internos generados, cuarentena y análisis antes de servir contenido cuando corresponda. Rechaza tipos activos no soportados y sanitiza previews. Define retención, papelera y limpieza de huérfanos; borrar un enlace no equivale necesariamente a borrar el archivo compartido. Anotaciones, comparación de imágenes, OCR y transcripción quedan en el backlog de ampliaciones.

MENCIONES Y NOTIFICACIONES

Al escribir @, muestra búsqueda de miembros autorizados con nombre y avatar. Guarda el ID del destinatario, no solo texto. La mención enlaza al lugar exacto de la tarea, comentario, documento o mensaje y genera una notificación persistente.

Notifica únicamente una mención nueva o un cambio real de destinatario. Autoguardados, reintentos y ediciones ajenas a ella no reenviarán el aviso. Define una clave de deduplicación por evento y destinatario. Mencionar a alguien no concede acceso; impide destinatarios sin permiso o requiere compartir explícitamente mediante una acción autorizada.

Incluye eventos por asignación, mención, revisión solicitada, respuesta, vencimiento y resolución de bloqueos. Implementa bandeja con leídos, no leídos, posponer, marcar todo leído, filtro por proyecto y enlace al contexto. Ofrece preferencias por evento, canal y proyecto, silencio y horario de descanso.

El aviso en la aplicación será inmediato cuando exista conexión. El correo será configurable y opcional, con resúmenes periódicos. No envíes correos reales desde pruebas sin configuración y autorización. Persiste antes de emitir, recupera pendientes al reconectar y revalida permisos justo antes de entregar. Las notificaciones tampoco deben revelar fragmentos de recursos revocados.

CHAT INTERNO

Implementa canales por proyecto, mensajes persistentes, hilos, menciones, imágenes, adjuntos, búsqueda y estado de lectura. Comprueba acceso al canal en cada operación y suscripción. Conserva orden, paginación e identificación de mensajes para recuperar novedades al reconectar sin duplicados.

Cada mensaje permitirá Crear tarea, Vincular tarea y Registrar decisión. La nueva tarea conserva origen, responsable y fecha cuando se definan. Evita que un doble clic o reintento genere tareas duplicadas. Una decisión registrada debe poder encontrarse desde su documento y conversación de origen.

Muestra edición y aplica reglas claras de eliminación, retención e historial. Determina qué ocurre al retirar a un miembro. Mensajes directos y canales privados adicionales pertenecen a la siguiente etapa prevista; presencia y está escribiendo son complementos. Audio y videollamada quedan como ampliaciones independientes.

CALENDARIO Y CAPACIDAD

Incluye vistas mes, semana, día y agenda, con tareas, reuniones, hitos y publicaciones, filtros por proyecto y persona y acceso al registro original. Permite crear desde una fecha, reprogramar con permisos, conservar historial y avisar a afectados.

Distingue fechas sin hora, límites, periodos de ejecución y eventos con hora. Mantén instantes y zona horaria explícitos; permite America/Santo_Domingo y presentación según usuario. Una tarea de todo el día no debe moverse al día anterior por una conversión UTC.

Implementa recordatorios y recurrencias simples, con operaciones diferenciadas para una ocurrencia y toda la serie. Evita duplicar ocurrencias al reiniciar procesos. Registra excepciones y cancelaciones. Presenta hitos y versiones junto a su contexto.

La capacidad se calculará desde disponibilidad declarada y estimaciones compatibles. Define horas o puntos por proyecto; no los sumes como equivalentes. El tiempo real registrado será opcional. Cronograma, carga avanzada, exportación a calendario y sincronización externa permanecen como evolución prevista, con políticas de permisos y conflictos antes de activar sincronización bidireccional.

TICKETS Y SOLICITUDES

La bandeja recibe errores, mejoras, dudas y cambios. Recibir una solicitud no la aprueba automáticamente. La revisión inicial permite aceptar, rechazar, posponer, pedir información o relacionar un duplicado, conservando motivo y responsable de evaluación.

Campos: número, solicitante, origen, producto, proyecto cuando corresponda, categoría, severidad, prioridad, versión, ambiente, descripción, reproducción, evidencia, responsable de atención y fechas. Severidad describe impacto; prioridad indica orden. Separa notas internas de respuestas compartidas tanto en interfaz como en API, notificaciones y exportaciones.

Flujo inicial: Recibido, En evaluación, Esperando información, Aceptado, En ejecución, Resuelto y Cerrado. Rechazado, duplicado y reabierto tendrán tratamiento explícito. Un ticket puede vincular varias tareas y varios tickets compartir la misma solución sin duplicar ejecución.

Resolver requiere explicación y evidencia cuando corresponda. Integrar un PR no cierra el ticket por sí mismo. Conserva comunicación del solicitante, capturas, historial y versión que resolvió el problema. Los invitados solo consultan solicitudes y recursos compartidos. Los tiempos objetivo de soporte, horarios hábiles, pausas y escalamiento son configurables para la evolución de soporte formal, sin inventar compromisos de servicio.

REVISIÓN QA Y VERSIONES

Crea una cola de revisión por proyecto y usuario, con ambiente, versión, revisor, criterios, evidencias y resultado. Permite guardar revisión, aprobar o devolver con motivo. La aprobación necesita completar los criterios obligatorios y poseer la capacidad correspondiente; ocultar un botón no sustituye esa validación.

Guarda pruebas y observaciones históricas, incluidos intentos fallidos. Una nueva versión relevante del trabajo puede requerir nueva validación según la política. Muestra por separado estado del PR, estado de QA y publicación.

Las versiones de entrega incluyen nombre, alcance, fecha prevista, tareas, aceptación, ambientes, notas y despliegues. Distingue prueba de producción. Conserva evidencia, fecha, actor y resultado de publicación. Ofrece confirmación manual autorizada cuando no exista integración, registrando su origen; no inventes un despliegue exitoso.

AUTOMATIZACIONES E INTEGRACIONES

Implementa reglas iniciales de asignación, vencimiento, solicitud de revisión, bloqueo prolongado, integración de PR y publicación. Toda regla define evento, condición, acción, actor, alcance y estado. Registra ejecuciones, intentos, resultado y errores; limita recursión y evita bucles entre reglas.

Integra referencias de GitHub a repositorios, ramas, PR, commits/SHA, CI, revisiones y despliegues. Admite varios PR y repositorios por tarea. Proporciona enlaces manuales y un conector básico real configurable; no uses estados hardcodeados para aparentar conexión.

La plataforma gobierna tareas, responsables y aceptación; GitHub gobierna código, PR y evidencia técnica. No habilites sincronización bidireccional indiscriminada. Antes de ampliarla, define autoridad de campos, conflictos, borrados y reintentos. Mantén permisos mínimos para las credenciales y secretos fuera del código.

Valida autenticidad de webhooks, identifica entregas repetidas, procesa de forma idempotente y conserva errores recuperables. Verifica permisos y estado actual al ejecutar acciones diferidas. Evita aplicar eventos atrasados que hagan retroceder información incorrectamente.

Prepara API documentada, tokens limitados y revocables, webhooks salientes e importación CSV para ampliaciones. La exportación inicial de tareas y documentos debe respetar permisos y permitir recuperar información; una copia completa conserva IDs, relaciones y adjuntos, no únicamente las celdas visibles.

PANELES REPORTES Y BÚSQUEDA

Los paneles personal, de proyecto, desarrollo, QA y cliente mostrarán información autorizada. Cada cifra debe permitir abrir los registros que la explican. Prioriza pendientes vencidos, bloqueos, revisiones, elementos sin responsable y entregas en riesgo.

Mide trabajo en curso, edad de pendientes, tiempo de ciclo, tiempo hasta entrega, tiempo bloqueado, reaperturas y cumplimiento del compromiso. Define inicio y final de cada métrica, periodo, tipos incluidos y denominador. Usa mediana y percentiles cuando exista volumen suficiente, mostrando número de observaciones.

No cuentes cancelaciones como entregas, ni padres y subtareas dos veces. Explica si el porcentaje de una épica mide tareas aceptadas, esfuerzo o criterios. Conserva cambios de alcance. No conviertas cantidad de tickets cerrados en clasificación de personas.

La búsqueda global cubre títulos, contenido, comentarios, tickets, mensajes y nombres de archivos. Aplica autorización antes de devolver coincidencias, sugerencias o conteos. Empieza con búsqueda soportada por la base y añade motor especializado solo por una necesidad demostrable. Búsqueda semántica y OCR son ampliaciones.

EXPERIENCIA CALIDAD Y OPERACIÓN

Implementa teclado, foco visible, etiquetas, contraste y alternativa accesible a arrastrar. El estado se identifica por texto además de color. Conserva anchuras, filtros, densidad y preferencias. En móvil prioriza consulta, respuestas, capturas y estados; las configuraciones complejas pueden optimizarse para escritorio.

Gestiona reconexión, errores de validación, concurrencia y fallos de guardado sin perder borradores. Usa tokens de concurrencia o rowversion cuando corresponda, con respuestas de conflicto que la interfaz pueda resolver. Una actualización optimista de React debe revertir o conciliarse si el servidor la rechaza.

Incluye logs estructurados, métricas de latencia y errores, estado de colas, fallos de avisos, almacenamiento y alertas configurables. Audita acciones sensibles y distingue personas, automatizaciones e integraciones. Sanitiza entradas, limita uso abusivo y protege secretos y datos personales.

Documenta copias de base y archivos, cifrado, retención, papelera, restauración y recuperación de migraciones. Prueba una restauración real con vínculos íntegros. Para el piloto, toma como objetivos preliminares una pérdida máxima de 24 horas y recuperación de 4 horas, sujetos a validación de infraestructura.

Objetivo inicial de rendimiento: 50 sesiones concurrentes y 10 000 tareas; vistas habituales por debajo de 2 segundos en percentil 95 y avisos por debajo de 3 segundos bajo condiciones documentadas. Son metas a medir, no garantías ni resultados que puedas afirmar antes de ejecutar pruebas.

FASES DE EJECUCIÓN Y SEGUIMIENTO

Fase 0: inspección, decisiones de arquitectura, rutas, esquema, matriz de permisos, estados, tokens visuales y plan rastreable de requisitos. Valida un recorrido completo con un caso real antes de multiplicar pantallas.

Fase 1: identidad, organización, proyectos, miembros, tareas, subtareas, checklist, tabla, tablero, historial y búsqueda. Demuestra colaboración entre dos usuarios y aislamiento frente a un tercero.

Fase 2: documentos, imágenes, versiones, menciones, notificaciones, chat, calendario y campos simples. Demuestra persistencia, recuperación tras desconexión y conflictos de edición.

Fase 3: tickets, revisión QA, versiones, automatizaciones iniciales, referencias a GitHub, reportes básicos, exportación y recuperación. Demuestra una solicitud hasta su entrega, con evidencia.

Las fases 1 a 3 constituyen la primera versión completa. No termines después de una maqueta. Incorpora seguridad, pruebas y accesibilidad durante cada fase. Mantén una matriz requisito, implementación, evidencia y estado; todo requisito pendiente debe ser visible.

La fase de ampliación conserva cronograma, capacidad avanzada, métricas ampliadas, portal de clientes, integraciones y constructor de automatizaciones. La coedición avanzada, fórmulas generales, bases arbitrarias, calendarios bidireccionales, audio/video, apps nativas e IA siguen explícitamente identificadas como evolución futura. Una función futura no justifica recortar su equivalente básico obligatorio.

PRUEBAS Y CRITERIOS DE ACEPTACIÓN

Ejecuta pruebas útiles de reglas, integración con SQL Server y recorridos de interfaz. No sustituyas las verificaciones relevantes de SQL Server por una base con comportamiento distinto. Cubre permisos negativos además de casos exitosos. Registra comandos, entorno, resultados y limitaciones.

La primera versión debe demostrar todos estos puntos:

Un usuario de A no consulta, cambia, exporta ni descarga recursos privados de B manipulando URL, ID o API.

Retirar membresía corta nuevas operaciones y suscripciones, conservando autorías históricas.

Tabla, tablero, calendario y detalle editan el mismo registro sin divergencias.

Transiciones validan requisitos y permisos; QA conserva evidencia de rechazo.

Un checklist completo no acepta ni publica una tarea automáticamente.

Menciones nuevas generan un aviso durable; autoguardados y reintentos no duplican efectos.

Una mención sin acceso o un permiso revocado no filtra contenido mediante avisos pendientes.

Reinicio y reconexión recuperan mensajes, lecturas y notificaciones de forma consistente.

Dos ediciones concurrentes conservan borradores y permiten resolver el conflicto.

Archivos prohibidos o grandes se rechazan; original, miniatura y preview se autorizan.

Relacionar un archivo con otra audiencia requiere permiso para compartirlo.

Búsqueda, sugerencias, conteos y exportaciones excluyen recursos y campos restringidos.

Un ticket conserva solicitante, notas internas, respuestas y vínculos con ejecución y entrega.

PR integrado, QA aprobado y publicación permanecen diferenciados, incluso con varios PR.

Webhooks repetidos o reintentos generan un único efecto lógico y errores recuperables.

Dependencias circulares se rechazan y cambios de estados mantienen integridad.

Fechas sin hora y eventos recurrentes conservan significado entre zonas horarias.

Crear tareas desde chat mantiene origen y evita duplicados.

La restauración recupera base, archivos y relaciones dentro del objetivo validado.

Las veinte pantallas y sus acciones principales funcionan con datos persistentes y permisos.

Revisa visualmente las pantallas en escritorio y móvil usando los mockups como referencia de estilo. Verifica estados vacíos, errores y accesibilidad. No marques como superada una prueba no ejecutada. Detén pruebas adicionales cuando los riesgos concretos y criterios estén suficientemente cubiertos.

CASO INTEGRAL DE DEMOSTRACIÓN

Usa datos de prueba explícitos: en GOGO Deliveri, SOL-028 reporta que el total cambia al agregar extras. Adjunta una captura, versión y pasos. El líder determina severidad y prioridad y vincula GOGO-142, Validar extras del carrito, con sus requisitos y componentes.

Desarrollo divide subtareas si hace falta, menciona al revisor ante una duda y registra la decisión. Añade PR y evidencia de CI. QA prueba cantidades, extras, redondeo y descuentos, aprueba o devuelve con motivo. La entrega conserva versión, ambiente y evidencia. El ticket se resuelve con explicación compartida, sin mostrar notas internas.

Demuestra que desde la ficha se conoce quién pidió, priorizó, decidió, implementó, validó y entregó el cambio. Ejecuta también el recorrido con un invitado limitado y con un miembro cuyo acceso se revoca.

ENTREGABLES Y CIERRE DEL TRABAJO

Entrega código funcional, solución y dependencias fijadas, migraciones, configuración de ejemplo sin secretos, seed opcional, pruebas, evidencias y documentación. El README debe explicar requisitos, inicio local, build de React, ejecución MVC, conexión SQL Server, migraciones, inicialización segura, pruebas, almacenamiento, correo, GitHub y procesos persistentes.

Documenta arquitectura, entidades, políticas de acceso, estados, contratos HTTP, eventos, configuración de integraciones, backup, restauración y recuperación. Incluye inventario de bibliotecas y sus licencias. Mantén instrucciones reproducibles, sin depender de rutas personales o pasos omitidos.

Presenta un reporte final con funciones implementadas, validaciones realizadas, requisitos pendientes y bloqueos externos. Distingue integrado, probado localmente y desplegado. No declares el producto completo si falta un módulo obligatorio o un criterio esencial; tampoco declares producción disponible sin autorización y verificación.

Empieza inspeccionando el entorno, crea el plan rastreable y ejecuta la construcción. Conserva esta especificación como referencia durante toda la implementación y registra cualquier desviación necesaria con su motivo y efecto.

REFERENCIAS PARA VERIFICACIÓN TÉCNICA

Consulta las versiones vigentes de la documentación oficial al resolver detalles de implementación. Estas referencias orientan las verificaciones y no autorizan a copiar interfaces o sustituir los requisitos del encargo:

ASP.NET Core MVC: https://learn.microsoft.com/aspnet/core/mvc/overview
Autorización por recurso: https://learn.microsoft.com/aspnet/core/security/authorization/resource-based
Identity: https://learn.microsoft.com/aspnet/core/security/authentication/identity
Antiforgery: https://learn.microsoft.com/aspnet/core/security/anti-request-forgery
SignalR: https://learn.microsoft.com/aspnet/core/signalr/groups
EF Core concurrencia: https://learn.microsoft.com/ef/core/saving/concurrency
React: https://react.dev/learn
SQL Server: https://learn.microsoft.com/sql/sql-server/
Carga de archivos: https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html