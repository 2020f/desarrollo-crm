using Desarrollo.Domain.Common;
using Desarrollo.Domain.Organizations;
using Desarrollo.Domain.Projects;

namespace Desarrollo.Domain.Tickets;

public class Ticket : Entity
{
    public Guid OrganizationId { get; set; }
    public int Number { get; set; }
    public string Code { get; set; } = string.Empty;

    public Guid? RequesterId { get; set; }
    public string? RequesterName { get; set; }
    public TicketOrigin Origin { get; set; } = TicketOrigin.Manual;

    public Guid? ProductId { get; set; }
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public TicketCategory Category { get; set; } = TicketCategory.Error;
    public Severity Severity { get; set; } = Severity.Media;
    public Priority Priority { get; set; } = Priority.Media;

    public string? AffectedVersion { get; set; }
    public string? Environment { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ReproSteps { get; set; }

    public Guid? AssigneeId { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Recibido;

    public DateTime? AcceptedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? ResolutionExplanation { get; set; }
    public string? ResolvedVersion { get; set; }

    public string? RejectReason { get; set; }
    public string? DuplicateOfTicketId { get; set; }
    public string? ReopenReason { get; set; }

    public List<TicketComment> Comments { get; set; } = new();
    public List<TicketWorkItemLink> WorkItemLinks { get; set; } = new();
}

public class TicketComment : Entity
{
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }
    public Guid AuthorId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsInternal { get; set; }
    public bool IsDeleted { get; set; }
}

public class TicketWorkItemLink : Entity
{
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }
    public Guid WorkItemId { get; set; }
}
