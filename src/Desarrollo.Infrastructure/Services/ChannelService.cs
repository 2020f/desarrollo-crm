using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Application.Contracts;
using Desarrollo.Domain;
using Desarrollo.Domain.Chat;
using Desarrollo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Infrastructure.Services;

public class ChannelService : IChannelService
{
    private const int MaximumMessageLength = 20_000;
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;

    public ChannelService(AppDbContext db, ICurrentUser user, IPermissionService permissions)
    {
        _db = db;
        _user = user;
        _permissions = permissions;
    }

    private Guid OrgId => _user.OrganizationId ?? throw new ForbiddenException();
    private Guid UserId => _user.UserId ?? throw new ForbiddenException();

    public async Task<List<ChannelDto>> ListAsync(Guid? projectId)
    {
        await EnsureActiveMemberAsync();
        if (projectId.HasValue) await EnsureProjectAccessAsync(projectId.Value, false);
        var channels = await _db.Channels.AsNoTracking().Include(c => c.Project)
            .Where(c => c.OrganizationId == OrgId && !c.IsArchived
                && (!projectId.HasValue || c.ProjectId == projectId)
                && (c.Type == ChannelType.Proyecto || _db.ChannelMembers.Any(m => m.ChannelId == c.Id && m.UserId == UserId)))
            .OrderBy(c => c.Name).ThenBy(c => c.Id).ToListAsync();
        var result = new List<ChannelDto>();
        foreach (var channel in channels)
        {
            if (!Enum.IsDefined(channel.Type) || (channel.Type == ChannelType.Proyecto && !channel.ProjectId.HasValue)) continue;
            if (channel.ProjectId.HasValue && (channel.Project is null || channel.Project.OrganizationId != OrgId
                || !await _permissions.CanAccessProjectAsync(channel.ProjectId.Value))) continue;
            if (channel.Type != ChannelType.Directo)
            {
                result.Add(channel.ToDto(true, null));
                continue;
            }
            var other = await (from member in _db.ChannelMembers.AsNoTracking()
                join organizationMember in _db.OrganizationMembers.AsNoTracking() on member.UserId equals organizationMember.UserId
                join user in _db.Users.AsNoTracking() on member.UserId equals user.Id
                where member.ChannelId == channel.Id && member.UserId != UserId
                    && organizationMember.OrganizationId == OrgId && organizationMember.IsActive && user.IsActive
                select new { member.UserId, organizationMember.Name }).FirstOrDefaultAsync();
            result.Add(new ChannelDto(channel.Id, other?.Name ?? "Chat directo", channel.Topic, channel.Type,
                channel.ProjectId, channel.Project?.Code, true, null, other?.UserId));
        }
        return result;
    }

    public async Task<ChannelDto> GetOrCreateDirectAsync(Guid userId)
    {
        await EnsureActiveMemberAsync();
        if (userId == UserId) throw new ValidationException("No puedes chatear contigo mismo.");
        var target = await (from member in _db.OrganizationMembers.AsNoTracking()
            join user in _db.Users.AsNoTracking() on member.UserId equals user.Id
            where member.OrganizationId == OrgId && member.UserId == userId && member.IsActive && user.IsActive
            select new { member.UserId, member.Name }).FirstOrDefaultAsync()
            ?? throw new ValidationException("El usuario no está disponible en esta organización.");
        var existing = await _db.Channels.AsNoTracking()
            .Where(c => c.OrganizationId == OrgId && c.Type == ChannelType.Directo && !c.IsArchived && c.ProjectId == null)
            .Where(c => _db.ChannelMembers.Any(m => m.ChannelId == c.Id && m.UserId == UserId)
                && _db.ChannelMembers.Any(m => m.ChannelId == c.Id && m.UserId == userId)
                && _db.ChannelMembers.Count(m => m.ChannelId == c.Id) == 2)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).FirstOrDefaultAsync();
        if (existing is not null)
            return new ChannelDto(existing.Id, target.Name, existing.Topic, ChannelType.Directo, null, null, true, null, userId);

        var channel = new Channel { OrganizationId = OrgId, Type = ChannelType.Directo, Name = "direct", CreatedBy = UserId };
        _db.Channels.Add(channel);
        _db.ChannelMembers.AddRange(new ChannelMember { ChannelId = channel.Id, UserId = UserId },
            new ChannelMember { ChannelId = channel.Id, UserId = userId });
        // Channel and membership are one atomic EF/SQL Server save.
        await _db.SaveChangesAsync();
        return new ChannelDto(channel.Id, target.Name, null, ChannelType.Directo, null, null, true, null, userId);
    }

    public async Task<ChannelDto> CreateAsync(Guid? projectId, string name, string? topic)
    {
        await EnsureActiveMemberAsync();
        if (projectId.HasValue)
        {
            await EnsureProjectAccessAsync(projectId.Value, true);
            await _permissions.EnsureProjectPermissionAsync(projectId.Value, Permission.Crear);
        }
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200 || name.Any(char.IsControl))
            throw new ValidationException("El nombre del canal debe tener entre 1 y 200 caracteres, sin saltos de línea.");
        if (topic is not null && (topic.Length > 1000 || topic.Contains('\0')))
            throw new ValidationException("El tema del canal admite hasta 1000 caracteres.");
        var channel = new Channel
        {
            OrganizationId = OrgId, ProjectId = projectId,
            Type = projectId.HasValue ? ChannelType.Proyecto : ChannelType.Privado,
            Name = name.Trim(), Topic = string.IsNullOrWhiteSpace(topic) ? null : topic.Trim(), CreatedBy = UserId,
        };
        _db.Channels.Add(channel);
        _db.ChannelMembers.Add(new ChannelMember { ChannelId = channel.Id, UserId = UserId });
        await _db.SaveChangesAsync();
        return channel.ToDto(true, null);
    }

    public async Task<Paged<MessageDto>> GetMessagesAsync(Guid channelId, int page, int pageSize)
    {
        await EnsureChannelAccessAsync(channelId, false);
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue) throw new ValidationException("La página solicitada supera el rango admitido.");
        var query = _db.Messages.AsNoTracking().Where(m => m.ChannelId == channelId && !m.IsDeleted
            && (!m.ThreadRootId.HasValue || _db.Messages.Any(root => root.Id == m.ThreadRootId
                && root.ChannelId == channelId && !root.IsDeleted && !root.ThreadRootId.HasValue)));
        var total = await query.CountAsync();
        var rows = await query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Skip((int)offset).Take(pageSize).ToListAsync();
        var names = await UserNames.ResolveAsync(_db, rows.Select(m => (Guid?)m.AuthorId));
        var dtos = rows.Select(m => m.ToDto(names)).OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToList();
        return new Paged<MessageDto>(dtos, total, page, pageSize);
    }

    public async Task<MessageDto> SendAsync(Guid channelId, string body, Guid? threadRootId)
    {
        var channel = await EnsureChannelAccessAsync(channelId, true);
        if (channel.ProjectId.HasValue) await _permissions.EnsureProjectPermissionAsync(channel.ProjectId.Value, Permission.Comentar);
        ValidateBody(body);
        if (threadRootId.HasValue) await EnsureThreadRootAsync(channelId, threadRootId.Value);
        var message = new Message { ChannelId = channelId, AuthorId = UserId, Body = body, ThreadRootId = threadRootId, CreatedBy = UserId };
        _db.Messages.Add(message); await _db.SaveChangesAsync();
        var names = await UserNames.ResolveAsync(_db, new[] { (Guid?)message.AuthorId });
        return message.ToDto(names);
    }

    public async Task<MessageDto> EditAsync(Guid messageId, string body)
    {
        await EnsureActiveMemberAsync();
        var message = await _db.Messages.FirstOrDefaultAsync(m => m.Id == messageId && !m.IsDeleted)
            ?? throw new NotFoundException("Mensaje no disponible.");
        var channel = await EnsureChannelAccessAsync(message.ChannelId, true);
        if (message.AuthorId != UserId) throw new ForbiddenException("Solo el autor puede editar el mensaje.");
        if (channel.ProjectId.HasValue) await _permissions.EnsureProjectPermissionAsync(channel.ProjectId.Value, Permission.Comentar);
        ValidateBody(body);
        if (message.ThreadRootId.HasValue) await EnsureThreadRootAsync(message.ChannelId, message.ThreadRootId.Value);
        message.Body = body; message.IsEdited = true; message.UpdatedBy = UserId; message.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        var names = await UserNames.ResolveAsync(_db, new[] { (Guid?)message.AuthorId });
        return message.ToDto(names);
    }

    public async Task MarkReadAsync(Guid channelId)
    {
        var channel = await EnsureChannelAccessAsync(channelId, false);
        var member = await _db.ChannelMembers.FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == UserId);
        if (member is null)
        {
            if (channel.Type != ChannelType.Proyecto) throw new ForbiddenException("No perteneces a este chat.");
            member = new ChannelMember { ChannelId = channelId, UserId = UserId };
            _db.ChannelMembers.Add(member);
        }
        member.LastReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private async Task EnsureActiveMemberAsync()
    {
        if (!_user.IsAuthenticated || !_user.UserId.HasValue || !_user.OrganizationId.HasValue
            || !await _db.Users.AsNoTracking().AnyAsync(u => u.Id == UserId && u.IsActive)
            || !await _db.OrganizationMembers.AsNoTracking().AnyAsync(m => m.OrganizationId == OrgId && m.UserId == UserId && m.IsActive))
            throw new ForbiddenException("La cuenta o membresía de la organización ya no está activa.");
    }

    private async Task EnsureProjectAccessAsync(Guid projectId, bool writing)
    {
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId && p.OrganizationId == OrgId && !p.IsDeleted)
            ?? throw new ForbiddenException("No puede acceder al proyecto de este canal.");
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Leer);
        if (writing && project.Status == ProjectStatus.Archivado) throw new ForbiddenException("El proyecto está archivado; su chat es de solo lectura.");
    }

    private async Task<Channel> EnsureChannelAccessAsync(Guid channelId, bool writing)
    {
        await EnsureActiveMemberAsync();
        var channel = await _db.Channels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId && !c.IsArchived)
            ?? throw new NotFoundException("Canal no disponible.");
        if (channel.OrganizationId != OrgId || !Enum.IsDefined(channel.Type)) throw new ForbiddenException();
        if (channel.Type == ChannelType.Proyecto && !channel.ProjectId.HasValue) throw new ForbiddenException();
        if (channel.Type != ChannelType.Proyecto
            && !await _db.ChannelMembers.AsNoTracking().AnyAsync(m => m.ChannelId == channelId && m.UserId == UserId))
            throw new ForbiddenException("No perteneces a este chat.");
        if (channel.ProjectId.HasValue) await EnsureProjectAccessAsync(channel.ProjectId.Value, writing);
        return channel;
    }

    private async Task EnsureThreadRootAsync(Guid channelId, Guid rootId)
    {
        if (!await _db.Messages.AsNoTracking().AnyAsync(m => m.Id == rootId && m.ChannelId == channelId && !m.IsDeleted && !m.ThreadRootId.HasValue))
            throw new ValidationException("El hilo debe partir de un mensaje vigente de este mismo canal.");
    }

    private static void ValidateBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > MaximumMessageLength || body.Contains('\0'))
            throw new ValidationException($"El mensaje debe contener texto y admite hasta {MaximumMessageLength} caracteres.");
    }
}
