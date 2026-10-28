using Desarrollo.Domain.Common;
using Desarrollo.Domain.Projects;

namespace Desarrollo.Domain.Chat;

public class Channel : Entity
{
    public Guid OrganizationId { get; set; }
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public ChannelType Type { get; set; } = ChannelType.Proyecto;
    public string Name { get; set; } = string.Empty;
    public string? Topic { get; set; }
    public bool IsArchived { get; set; }
    public List<ChannelMember> Members { get; set; } = new();
    public List<Message> Messages { get; set; } = new();
}

public class ChannelMember : Entity
{
    public Guid ChannelId { get; set; }
    public Channel? Channel { get; set; }
    public Guid UserId { get; set; }
    public DateTime? LastReadAt { get; set; }
    public Guid? LastReadMessageId { get; set; }
}

public class Message : Entity
{
    public Guid ChannelId { get; set; }
    public Channel? Channel { get; set; }
    public Guid AuthorId { get; set; }
    public Guid? ThreadRootId { get; set; }
    public Message? ThreadRoot { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsEdited { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<Message> ThreadReplies { get; set; } = new();
}

public class Mention : Entity
{
    public Guid OrganizationId { get; set; }
    public Guid TargetUserId { get; set; }
    public Guid AuthorId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string? Context { get; set; }
}
