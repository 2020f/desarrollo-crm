namespace Desarrollo.Application.Abstractions;

public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? OrganizationId { get; }
    string Name { get; }
    bool IsAuthenticated { get; }
}
