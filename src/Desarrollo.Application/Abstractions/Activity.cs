using Desarrollo.Application.Contracts;

namespace Desarrollo.Application.Abstractions;

public interface ILiveActivityService
{
    Task<ActivitySnapshotDto> GetAsync();
}
