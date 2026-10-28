using Desarrollo.Domain.Common;

namespace Desarrollo.Domain.Work;

/// <summary>Posición de una tarjeta en el mapa de tareas (compartida por el proyecto).</summary>
public class TaskDiagramPosition : Entity
{
    public Guid ProjectId { get; set; }
    public Guid TaskId { get; set; }
    public WorkItem? Task { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>Cámara personal del usuario (desplazamiento y zoom).</summary>
public class TaskDiagramUserView : Entity
{
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public double ViewportX { get; set; }
    public double ViewportY { get; set; }
    public double Zoom { get; set; } = 1;
    public List<Guid> CollapsedTaskIds { get; set; } = new();
}
