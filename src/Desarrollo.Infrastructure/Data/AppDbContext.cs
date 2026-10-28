using Desarrollo.Domain;
using Desarrollo.Domain.Calendar;
using Desarrollo.Domain.Chat;
using Desarrollo.Domain.Docs;
using Desarrollo.Domain.Files;
using Desarrollo.Domain.Integrations;
using Desarrollo.Domain.Notifications;
using Desarrollo.Domain.Organizations;
using Desarrollo.Domain.Projects;
using Desarrollo.Domain.Tickets;
using Desarrollo.Domain.Work;
using Desarrollo.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Infrastructure.Data;

public class AppDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<Role> OrganizationRoles => Set<Role>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Component> Components => Set<Component>();

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<ProjectDependency> ProjectDependencies => Set<ProjectDependency>();
    public DbSet<Epic> Epics => Set<Epic>();
    public DbSet<Cycle> Cycles => Set<Cycle>();
    public DbSet<Milestone> Milestones => Set<Milestone>();
    public DbSet<Release> Releases => Set<Release>();

    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<WorkRelation> WorkRelations => Set<WorkRelation>();
    public DbSet<WorkflowState> WorkflowStates => Set<WorkflowState>();
    public DbSet<Transition> Transitions => Set<Transition>();
    public DbSet<Checklist> Checklists => Set<Checklist>();
    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();
    public DbSet<WorkItemComment> WorkItemComments => Set<WorkItemComment>();
    public DbSet<CustomField> CustomFields => Set<CustomField>();
    public DbSet<WorkItemCustomFieldValue> WorkItemCustomFieldValues => Set<WorkItemCustomFieldValue>();
    public DbSet<SavedView> SavedViews => Set<SavedView>();
    public DbSet<TaskDiagramPosition> TaskDiagramPositions => Set<TaskDiagramPosition>();
    public DbSet<TaskDiagramUserView> TaskDiagramUserViews => Set<TaskDiagramUserView>();

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();
    public DbSet<TicketWorkItemLink> TicketWorkItemLinks => Set<TicketWorkItemLink>();

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<DocumentReader> DocumentReaders => Set<DocumentReader>();

    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelMember> ChannelMembers => Set<ChannelMember>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Mention> Mentions => Set<Mention>();

    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();

    public DbSet<RepositoryReference> RepositoryReferences => Set<RepositoryReference>();
    public DbSet<Deployment> Deployments => Set<Deployment>();
    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();
    public DbSet<AutomationExecution> AutomationExecutions => Set<AutomationExecution>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<AppUser>(e => e.ToTable("Users"));

        ConfigureOrganizations(b);
        ConfigureProjects(b);
        ConfigureWork(b);
        ConfigureTickets(b);
        ConfigureDocs(b);
        ConfigureChat(b);
        ConfigureFiles(b);
        ConfigureNotifications(b);
        ConfigureCalendar(b);
        ConfigureIntegrations(b);
    }

    private static void ConfigureOrganizations(ModelBuilder b)
    {
        b.Entity<Organization>(e =>
        {
            e.ToTable("Organizations");
            e.HasIndex(x => x.Slug).IsUnique();
        });

        b.Entity<OrganizationMember>(e =>
        {
            e.ToTable("OrganizationMembers");
            e.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Role>(e =>
        {
            e.ToTable("Roles");
            e.Property(x => x.Permissions).HasColumnType("nvarchar(max)");
        });

        b.Entity<Product>(e =>
        {
            e.ToTable("Products");
            e.HasIndex(x => x.OrganizationId);
        });

        b.Entity<Component>(e =>
        {
            e.ToTable("Components");
            e.HasOne(x => x.Product).WithMany(x => x.Components).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureProjects(ModelBuilder b)
    {
        b.Entity<Project>(e =>
        {
            e.ToTable("Projects");
            e.HasIndex(x => x.OrganizationId);
            e.HasIndex(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        b.Entity<ProjectMember>(e =>
        {
            e.ToTable("ProjectMembers");
            e.HasIndex(x => new { x.ProjectId, x.UserId }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ProjectDependency>(e =>
        {
            e.ToTable("ProjectDependencies");
            e.HasIndex(x => x.ProjectId);
        });

        b.Entity<Epic>(e => { e.ToTable("Epics"); e.HasIndex(x => x.ProjectId); });
        b.Entity<Cycle>(e => { e.ToTable("Cycles"); e.HasIndex(x => x.ProjectId); });
        b.Entity<Milestone>(e => { e.ToTable("Milestones"); e.HasIndex(x => x.ProjectId); });

        b.Entity<Release>(e =>
        {
            e.ToTable("Releases");
            e.HasIndex(x => x.ProjectId);
        });
    }

    private static void ConfigureWork(ModelBuilder b)
    {
        b.Entity<WorkItem>(e =>
        {
            e.ToTable("WorkItems");
            e.HasIndex(x => x.OrganizationId);
            e.HasIndex(x => new { x.ProjectId, x.StateId });
            e.HasIndex(x => x.AssigneeId);
            e.HasIndex(x => x.DueDate);
            e.HasIndex(x => new { x.ProjectId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.OrganizationId, x.AuthorId, x.CreationRequestId }).IsUnique()
                .HasFilter("[CreationRequestId] IS NOT NULL");
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Title).HasMaxLength(300);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentTaskId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ParentTaskId);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        b.Entity<TaskDiagramPosition>(e =>
        {
            e.ToTable("TaskDiagramPositions");
            e.HasIndex(x => x.TaskId).IsUnique();
            e.HasIndex(x => x.ProjectId);
            e.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Desarrollo.Domain.Projects.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<TaskDiagramUserView>(e =>
        {
            e.ToTable("TaskDiagramUserViews");
            e.HasIndex(x => new { x.ProjectId, x.UserId }).IsUnique();
            e.HasOne<Desarrollo.Domain.Projects.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<WorkRelation>(e =>
        {
            e.ToTable("WorkRelations");
            e.HasIndex(x => new { x.SourceId, x.TargetId, x.Type });
            e.HasOne(x => x.Source).WithMany(x => x.OutgoingRelations).HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Target).WithMany(x => x.IncomingRelations).HasForeignKey(x => x.TargetId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<WorkflowState>(e =>
        {
            e.ToTable("WorkflowStates");
            e.HasIndex(x => x.ProjectId);
        });

        b.Entity<Transition>(e =>
        {
            e.ToTable("Transitions");
            e.Property(x => x.RequiredPermissions).HasColumnType("nvarchar(max)");
            e.HasOne(x => x.FromState).WithMany().HasForeignKey(x => x.FromStateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ToState).WithMany().HasForeignKey(x => x.ToStateId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Checklist>(e =>
        {
            e.ToTable("Checklists");
            e.HasOne(x => x.WorkItem).WithMany(x => x.Checklists).HasForeignKey(x => x.WorkItemId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ChecklistItem>(e =>
        {
            e.ToTable("ChecklistItems");
            e.HasOne(x => x.Checklist).WithMany(x => x.Items).HasForeignKey(x => x.ChecklistId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<WorkItemComment>(e =>
        {
            e.ToTable("WorkItemComments");
            e.HasIndex(x => x.WorkItemId);
            e.HasOne(x => x.WorkItem).WithMany(x => x.Comments).HasForeignKey(x => x.WorkItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CustomField>(e =>
        {
            e.ToTable("CustomFields");
            e.Property(x => x.Options).HasColumnType("nvarchar(max)");
        });

        b.Entity<WorkItemCustomFieldValue>(e =>
        {
            e.ToTable("WorkItemCustomFieldValues");
            e.HasIndex(x => new { x.WorkItemId, x.CustomFieldId }).IsUnique();
            e.HasOne(x => x.WorkItem).WithMany(x => x.CustomFieldValues).HasForeignKey(x => x.WorkItemId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SavedView>(e => { e.ToTable("SavedViews"); });
    }

    private static void ConfigureTickets(ModelBuilder b)
    {
        b.Entity<Ticket>(e =>
        {
            e.ToTable("Tickets");
            e.HasIndex(x => x.OrganizationId);
            e.HasIndex(x => new { x.ProjectId, x.Number }).IsUnique();
            e.HasIndex(x => x.Status);
        });

        b.Entity<TicketComment>(e =>
        {
            e.ToTable("TicketComments");
            e.HasOne(x => x.Ticket).WithMany(x => x.Comments).HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TicketWorkItemLink>(e =>
        {
            e.ToTable("TicketWorkItemLinks");
            e.HasOne(x => x.Ticket).WithMany(x => x.WorkItemLinks).HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureDocs(ModelBuilder b)
    {
        b.Entity<Document>(e =>
        {
            e.ToTable("Documents");
            e.HasIndex(x => x.ProjectId);
            e.HasQueryFilter(x => !x.IsDeleted);
            e.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ContentFormat).HasMaxLength(16).HasDefaultValue("plain");
            e.HasIndex(x => new { x.ProjectId, x.Visibility, x.AuthorId });
        });

        b.Entity<DocumentVersion>(e =>
        {
            e.ToTable("DocumentVersions");
            e.Property(x => x.ContentFormat).HasMaxLength(16).HasDefaultValue("plain");
            e.HasOne(x => x.Document).WithMany(x => x.Versions).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DocumentReader>(e =>
        {
            e.ToTable("DocumentReaders");
            e.HasIndex(x => new { x.DocumentId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.Document).WithMany(x => x.Readers).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureChat(ModelBuilder b)
    {
        b.Entity<Channel>(e =>
        {
            e.ToTable("Channels");
            e.HasIndex(x => x.ProjectId);
        });

        b.Entity<ChannelMember>(e =>
        {
            e.ToTable("ChannelMembers");
            e.HasIndex(x => new { x.ChannelId, x.UserId }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Message>(e =>
        {
            e.ToTable("Messages");
            e.HasIndex(x => new { x.ChannelId, x.CreatedAt });
            e.HasOne(x => x.Channel).WithMany(x => x.Messages).HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ThreadRoot).WithMany(x => x.ThreadReplies).HasForeignKey(x => x.ThreadRootId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Mention>(e =>
        {
            e.ToTable("Mentions");
            e.HasIndex(x => x.TargetUserId);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });
    }

    private static void ConfigureFiles(ModelBuilder b)
    {
        b.Entity<StoredFile>(e =>
        {
            e.ToTable("StoredFiles");
            e.HasIndex(x => x.OrganizationId);
            e.HasIndex(x => new { x.OrganizationId, x.Visibility, x.ProjectId });
            e.HasIndex(x => new { x.OrganizationId, x.UploaderId });
            e.HasQueryFilter(x => !x.IsDeleted);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Attachment>(e =>
        {
            e.ToTable("Attachments");
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasOne(x => x.StoredFile).WithMany(x => x.Links).HasForeignKey(x => x.StoredFileId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureNotifications(ModelBuilder b)
    {
        b.Entity<Notification>(e =>
        {
            e.ToTable("Notifications");
            e.HasIndex(x => new { x.UserId, x.IsRead });
            e.HasIndex(x => new { x.UserId, x.DedupKey }).IsUnique();
        });

        b.Entity<NotificationPreference>(e =>
        {
            e.ToTable("NotificationPreferences");
            e.HasIndex(x => x.UserId);
        });
    }

    private static void ConfigureCalendar(ModelBuilder b)
    {
        b.Entity<CalendarEvent>(e =>
        {
            e.ToTable("CalendarEvents");
            e.HasIndex(x => new { x.OrganizationId, x.Start });
            e.HasIndex(x => x.ProjectId);
        });
    }

    private static void ConfigureIntegrations(ModelBuilder b)
    {
        b.Entity<RepositoryReference>(e =>
        {
            e.ToTable("RepositoryReferences");
            e.HasIndex(x => x.WorkItemId);
        });

        b.Entity<Deployment>(e =>
        {
            e.ToTable("Deployments");
            e.HasIndex(x => x.ReleaseId);
            e.HasOne(x => x.Release).WithMany(x => x.Deployments).HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AutomationRule>(e =>
        {
            e.ToTable("AutomationRules");
            e.HasIndex(x => x.OrganizationId);
        });

        b.Entity<AutomationExecution>(e =>
        {
            e.ToTable("AutomationExecutions");
            e.HasOne(x => x.AutomationRule).WithMany().HasForeignKey(x => x.AutomationRuleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasIndex(x => new { x.OrganizationId, x.EntityType, x.EntityId });
        });
    }
}
