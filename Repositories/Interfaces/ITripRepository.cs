using MetroClaim.Api.Models;

namespace MetroClaim.Api.Repositories.Interfaces;

public interface ITripRepository : IRepository<Trip>
{
    Task<Trip?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken);
    
    Task<IEnumerable<Trip>> GetByManagerIdAsync(Guid managerId, CancellationToken cancellationToken);
    Task<(IEnumerable<Trip> Items, int TotalCount)> GetByManagerIdPagedAsync(Guid managerId, int page, int pageSize, CancellationToken cancellationToken);
    
    Task<IEnumerable<Trip>> GetByParticipantIdAsync(Guid userId, CancellationToken cancellationToken);
    
    Task<IEnumerable<Trip>> GetForFinanceAsync(CancellationToken cancellationToken);
    Task<(IEnumerable<Trip> Items, int TotalCount)> GetForFinancePagedAsync(int page, int pageSize, CancellationToken cancellationToken);
    
    Task<IEnumerable<Trip>> GetHistoryForFinanceAsync(CancellationToken cancellationToken);
    Task<(IEnumerable<Trip> Items, int TotalCount)> GetHistoryForFinancePagedAsync(int page, int pageSize, CancellationToken cancellationToken);
    
    Task<Trip?> GetByIdWithParticipantsAsync(Guid id, CancellationToken cancellationToken);
    Task<IEnumerable<Guid>> GetConflictingUserIdsAsync(IEnumerable<Guid> participantIds, DateTime startDate, DateTime endDate, Guid? excludeTripId, CancellationToken cancellationToken);

}
