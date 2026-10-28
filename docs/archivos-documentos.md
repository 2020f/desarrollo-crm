# Documentos con formato y carpetas de archivos

Actualización del 27 de octubre de 2026. Extiende el monolito existente: React/TypeScript, ASP.NET Core MVC, EF Core y SQL Server. No publica el CRM ni configura un túnel externo.

## Documentos

El editor permite escribir y organizar notas con negrita, cursiva, encabezados, párrafos, listas con viñetas, numeración, checklists y alineación. Deshacer y rehacer conservan la edición de la sesión. Tiptap 3.32.0 (paquetes abiertos con licencia MIT) guarda contenido estructurado JSON; los textos anteriores se abren como texto, sin interpretar HTML.

Cada documento pertenece a un proyecto y tiene una audiencia:

| Opción | Quién puede leer |
| --- | --- |
| Todas las personas del proyecto | Usuarios con membresía activa y permiso de lectura actual en ese proyecto. |
| Personas seleccionadas | Autor y lectores elegidos, siempre que conserven acceso al proyecto. |
| Solo yo | Únicamente el autor, mientras conserve acceso al proyecto. |

Los documentos nuevos comienzan privados. Los anteriores mantienen su audiencia original de proyecto. Un administrador no obtiene una excepción automática para notas privadas o compartidas con otras personas. Solo el autor puede cambiar la audiencia; editar el contenido también exige la capacidad correspondiente del proyecto. La audiencia de cada página es independiente de padres e hijos. Los archivos privados conservan su audiencia; los adjuntos contextuales siguen el acceso actual del documento.

El servidor aplica estas reglas al listado, detalle, historial, vínculos de archivos y avisos históricos de documentos. Valida destinatarios de la misma organización, activos y con lectura del proyecto. El listado no incluye cuerpos completos ni identificadores de páginas madre inaccesibles.

Guardar devuelve `rowVersion` y conserva una nueva revisión cuando cambia el contenido o su formato. Los cambios de audiencia tienen auditoría propia. Si otro usuario modificó el documento, la API responde 409 y la interfaz conserva el borrador para comparar y reintentar. El cambio de página, proyecto, ruta o cierre de sesión guarda los cambios pendientes; un error impide salir hasta guardar o descartar expresamente. Esto no constituye coedición simultánea ni recuperación de borradores tras un corte eléctrico.

El contenido se valida en servidor con un esquema de nodos y atributos admitidos. No admite HTML arbitrario, scripts, imágenes incrustadas o enlaces ejecutables. Las imágenes se gestionan desde Carpetas. El editor enriquecido se carga en un fragmento separado para no añadirlo al arranque de las demás pantallas.

## Carpetas

El menú **Carpetas**, ruta `/folders`, contiene dos opciones. La ruta anterior `/files` continúa funcionando.

| Carpeta | Alcance |
| --- | --- |
| Pública | Del proyecto seleccionado. Quienes tengan lectura actual pueden consultar y descargar. Subir requiere lectura y creación. |
| Privada | Archivos del propio usuario. Ningún otro usuario, incluido un administrador de organización, puede listarlos, previsualizarlos o descargarlos. |

“Pública” significa compartida dentro del proyecto: las descargas requieren una sesión autenticada. No se generan enlaces anónimos de Internet. Los archivos privados subidos desde Carpetas son personales y no dependen de un proyecto. Si una API vincula un archivo privado a un proyecto, también se exige conservar acceso a ese proyecto.

Se puede seleccionar o arrastrar un archivo, escribir una descripción, buscar, paginar, copiar su código, descargar, ampliar imágenes y retirar los archivos propios. La retirada es lógica: se bloquean futuras lecturas, pero se conservan los bytes para recuperación administrativa. No hay purga automática de archivos originales.

Cada archivo recibe un ID y un código estable `ARC-<GUID>`. El nombre físico usa identificadores generados de organización y archivo. SQL Server registra nombre original, tipo validado, tamaño, autor, audiencia, proyecto y ubicación interna. El código identifica el archivo; la audiencia registrada y los permisos actuales deciden el acceso. El navegador no recibe la ruta física.

Los adjuntos anteriores mantienen la audiencia de su registro de origen (`Context`). Tareas, documentos, proyectos, tickets y mensajes se revalidan al consultar o descargar. Los archivos antiguos sin vínculos solo se recuperan para su autor. Añadir un vínculo no convierte automáticamente un archivo privado en público.

## Formatos y almacenamiento

Máximo: **50 MB por archivo**, también comprobado durante la copia de bytes. Formatos admitidos: PNG, JPG/JPEG, GIF, WebP, PDF, TXT, Markdown UTF-8, CSV, ZIP, DOC/DOCX, XLS/XLSX y PPT/PPTX. Se rechazan nombres con rutas, archivos vacíos, extensiones activas no admitidas y firmas incompatibles. Los paquetes modernos de Office deben contener su estructura correspondiente y no incluir `vbaProject.bin`.

Las cargas se escriben primero en un área temporal, se validan y se trasladan a su nombre definitivo. Un fallo de copia o persistencia elimina los nuevos bytes incompletos. Esta validación no equivale a un análisis antivirus: el adaptador actual no incorpora un motor antivirus ni garantiza que un documento descargable sea inocuo.

Las vistas previas en línea solo sirven PNG/JPEG/GIF/WebP con tipo validado, `nosniff`, política restrictiva de contenido y `Cache-Control: private, no-store`. Word, Markdown, PDF y demás formatos se descargan como adjuntos; no se ejecutan ni se renderizan como HTML bajo el origen del CRM. SVG no se admite como nueva carga.

Por defecto, el adaptador guarda los bytes en `src/Desarrollo.Web/App_Data/files`, fuera de `wwwroot`. Para cambiar de disco o usar Ubuntu:

```text
Storage__RootPath=/var/lib/crm/files
DataProtection__KeyRingPath=/var/lib/crm/keys
```

La raíz debe ser absoluta y quedar fuera de `wwwroot`. Al cambiarla en una instalación existente, copiar primero los archivos conservando sus nombres y subdirectorios; cambiar la variable no mueve los originales. Las claves de protección de datos se persisten aparte para conservar las cookies entre recreaciones del contenedor.

## Docker en Ubuntu

Se incluyen `Dockerfile`, `compose.yaml` y `.env.docker.example`. El contenedor ejecuta la aplicación como usuario sin privilegios, escucha en 8080 y usa una carpeta del equipo anfitrión para archivos y claves. SQL Server conserva su propia unidad persistente; ningún seed se ejecuta al arrancar.

La secuencia vigente está en [DESPLIEGUE.md](../DESPLIEGUE.md): generar configuración privada, preparar directorios, construir imagen, iniciar SQL, migrar con credenciales de mantenimiento, crear el usuario de ejecución limitado, inicializar la primera cuenta y arrancar web. El registro público permanece cerrado por defecto en producción.

El puerto publicado se limita a `127.0.0.1:5080`. Un túnel Cloudflare ejecutado en el anfitrión puede apuntar a `http://localhost:5080`. El subdominio y el túnel se configuran aparte; esta actualización no los publica. Con HTTPS externo mantener `CRM_SECURE_COOKIES=true`. Para pruebas locales exclusivamente HTTP usar `false`. No configurar reglas que almacenen en caché las API o descargas autenticadas.

El ejemplo usa SQL Server Express; puede elegirse otra edición con su licencia apropiada. Developer se limita a desarrollo/pruebas. Las credenciales administrativas de SQL se usan solo en mantenimiento; web usa una cuenta independiente con lectura/escritura y sin permisos DDL.

Para detener los servicios conservando datos: `docker compose down`. No usar `down -v` para una instalación que deba conservar SQL. Las carpetas del anfitrión deben seguir existiendo y ser escribibles por el UID del contenedor.

## Copia y recuperación

Respaldar conjuntamente SQL Server, la carpeta completa de archivos y el directorio de claves, con acceso restringido. Para una copia coherente, detener escrituras del CRM, hacer backup SQL y copiar los directorios; conservar el mismo punto de recuperación. Restaurar SQL y archivos juntos, configurar las mismas raíces y comprobar descargas autorizadas antes de abrir acceso externo. Una copia solo de la imagen Docker no incluye los datos.

Los scripts `scripts/deploy/backup.sh` y `restore.sh` automatizan una copia coherente y una restauración en un destino vacío. Su uso, comprobaciones y límites están en [DESPLIEGUE.md](../DESPLIEGUE.md).

La migración `20261027222955_DocumentFileVisibility` añade columnas, índices y `DocumentReaders`. Conserva texto y versiones anteriores. La aplicación local se realiza tras backup `COPY_ONLY`, checksum y `RESTORE VERIFYONLY`, comparando las columnas originales de todas las tablas. Revertir el esquema elimina las nuevas reglas de audiencia: preferir restaurar una copia coherente antes que ejecutar una reversión improvisada.

## Contratos

- `GET /api/documents?projectId=...`, `GET /api/documents/{id}`, `GET /api/documents/{id}/versions`.
- `POST /api/documents`: `projectId`, `title`, `parentId`, `content`, `contentFormat`, `visibility` (0 proyecto, 1 seleccionadas, 2 privada), `readerIds`.
- `PUT /api/documents/{id}`: contenido y título, opciones de audiencia del autor y `rowVersion` obligatoria.
- `GET /api/files?visibility=Public|Private&projectId=...&search=...&page=1&pageSize=50`: resultado paginado y filtrado antes del conteo.
- `POST /api/files`: multipart `file`, `visibility`, `projectId` para pública y `description` opcional. Adjunto contextual: `entityType`, `entityId` y audiencia `Context`.
- `GET /api/files/{id}/download`, `GET /api/files/{id}/preview`, `DELETE /api/files/{id}`.
- Las mutaciones exigen antiforgery. Ninguna API devuelve entidades EF o nombres físicos de almacenamiento.

## Alcance restante del producto

Esta actualización cubre formato, audiencia y almacenamiento solicitados. La matriz general mantiene como pendientes otras capacidades de `crm.md`: comentarios y menciones de documentos, favoritos, búsqueda global de contenido, tablas del editor, imágenes dentro de las notas, restauración de versiones desde la interfaz, autoguardado periódico, versiones de archivos, cuotas agregadas y análisis antivirus. No se presentan como implementadas.

## Validación local

- `dotnet build Desarrollo.slnx --no-restore --verbosity minimal`: 0 errores y 0 advertencias.
- Batería SQL Server real: **80/80** correctas; incluye 17 casos de documentos y avisos, 15 de archivos/disco y las 48 regresiones anteriores. Resultado local: `.validation/results/documents-files.trx`.
- `npm --prefix src/Desarrollo.Web/ClientApp run test:frontend`: **19/19** correctas, con editor Tiptap real y Documentos en StrictMode.
- `npm --prefix src/Desarrollo.Web/ClientApp run test:assets`: **1/1** correcta. Comprueba que el entry de Razor y los imports diferidos resuelven al mismo módulo React, sin cargar una segunda instancia mediante un parámetro de versión.
- `npm --prefix src/Desarrollo.Web/ClientApp run build`: TypeScript y Vite correctos. El editor se carga aparte; queda el aviso de tamaño del fragmento principal de Vite, sin error de compilación.
- **15/15 comprobaciones reales de navegador** en una base acceptance aislada: formato/checklist tras recargar, privacidad inicial, cambios de audiencia, dos identidades, solo lectura, subida privada, descarga pública idéntica, búsqueda, rechazo de SVG, denegación de URL privada y escritorio/móvil a 390 px sin desbordamiento horizontal. [Recibo](evidence/content/ui-checks.json), [editor](evidence/content/document-rich-sharing.png), [audiencia](evidence/content/document-audience.png), [carpetas](evidence/content/folders-public.png).
- Normal, demo principal y acceptance recibieron la quinta migración con backup verificado y conservación exacta de las columnas originales de sus 50 tablas. Los reinicios conservaron las 11 tareas normales y las 6 de la demo principal. No se introdujeron datos demostrativos en la base normal.
- Docker Desktop con contenedores Linux: SQL Server Express 2025, migración explícita, cargas PNG privada, Markdown pública y DOCX real; hashes de descarga idénticos, preview PNG permitido y Markdown rechazado. Recrear el contenedor web conservó la cookie, los bytes, las claves y los tres metadatos SQL. El proyecto de prueba se detuvo conservando sus volúmenes y carpetas. Recibo local: `.validation/docker-docfiles.json`.

La prueba Docker se ejecutó en el equipo Windows mediante contenedores Linux. No certifica una instalación en el equipo Ubuntu del usuario ni un túnel de Cloudflare publicado. No se desplegó en Internet.

Referencias verificadas: [cargas seguras en ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/tutorials/razor-pages/uploading-files), [montajes persistentes de Docker](https://docs.docker.com/engine/storage/bind-mounts/) y [configuración de protección de datos](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).
