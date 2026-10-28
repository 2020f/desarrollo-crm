namespace Desarrollo.Domain;

public enum ProjectStatus { Propuesto = 0, Activo = 1, EnPausa = 2, Finalizado = 3, Archivado = 4 }

public enum Priority { Baja = 0, Media = 1, Alta = 2, Urgente = 3 }

public enum Severity { Baja = 0, Media = 1, Alta = 2, Critica = 3 }

public enum WorkItemType
{
    Funcionalidad = 0,
    HistoriaUsuario = 1,
    TareaTecnica = 2,
    Error = 3,
    Investigacion = 4,
    DeudaTecnica = 5,
}

public enum WorkItemCategory { Pendiente = 0, EnCurso = 1, Finalizado = 2, Cancelado = 3 }

public enum WorkRelationType { Padre = 0, Subtarea = 1, Bloquea = 2, BloqueadaPor = 3, DuplicadaDe = 4, RelacionadaCon = 5 }

public enum TicketStatus
{
    Recibido = 0,
    EnEvaluacion = 1,
    EsperandoInformacion = 2,
    Aceptado = 3,
    EnEjecucion = 4,
    Resuelto = 5,
    Cerrado = 6,
    Rechazado = 7,
    Duplicado = 8,
    Reabierto = 9,
}

public enum TicketCategory { Error = 0, Mejora = 1, Duda = 2, Cambio = 3 }

public enum TicketOrigin { Correo = 0, Portal = 1, Chat = 2, Manual = 3, Otro = 4 }

public enum ChannelType { Proyecto = 0, Directo = 1, Privado = 2 }

public enum NotificationEvent
{
    Asignacion = 0,
    Mencion = 1,
    RevisionSolicitada = 2,
    Respuesta = 3,
    Vencimiento = 4,
    BloqueoResuelto = 5,
    Comentario = 6,
}

public enum CalendarEventType { Tarea = 0, Reunion = 1, Hito = 2, Publicacion = 3 }

public enum RecurrenceFrequency { Diaria = 0, Semanal = 1, Mensual = 2, Anual = 3 }

public enum ReviewStatus { Pendiente = 0, Aprobado = 1, Devuelto = 2 }

public enum DeploymentEnvironment { Desarrollo = 0, Pruebas = 1, Staging = 2, Produccion = 3 }

public enum DeploymentResult { Pendiente = 0, Exitoso = 1, Fallido = 2 }

public enum CustomFieldType { Texto = 0, Numero = 1, Fecha = 2, Seleccion = 3, SeleccionMultiple = 4, SiNo = 5, Usuario = 6, Url = 7 }

public enum SavedViewType { Personal = 0, CompartidoProyecto = 1, PlantillaRol = 2 }

public enum Permission
{
    Leer = 0,
    Comentar = 1,
    Crear = 2,
    Editar = 3,
    Asignar = 4,
    CambiarEstado = 5,
    Aprobar = 6,
    Exportar = 7,
    Archivar = 8,
    Eliminar = 9,
    AdministrarMiembros = 10,
    ConfigurarFlujo = 11,
}

public enum AutomationEventType
{
    Asignacion = 0,
    Vencimiento = 1,
    SolicitudRevision = 2,
    BloqueoProlongado = 3,
    IntegracionPR = 4,
    Publicacion = 5,
}

public enum AutomationActionType
{
    Notificar = 0,
    Asignar = 1,
    CambiarEstado = 2,
    EstablecerFecha = 3,
    CrearTarea = 4,
}
