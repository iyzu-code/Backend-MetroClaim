using MetroClaim.Api.Models;

namespace MetroClaim.Api.Repositories.Interfaces;

public interface IReimbursementRepository : IRepository<Reimbursement>
{
    Task<IEnumerable<Reimbursement>> GetAllWithReferencesAsync(CancellationToken cancellationToken);
    Task<Reimbursement?> GetByIdReadOnlyAsync(Guid id, CancellationToken cancellationToken);
    Task<Reimbursement?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<IEnumerable<Reimbursement>> GetHistoryForManagerAsync(Guid managerId, CancellationToken cancellationToken);

    Task<IEnumerable<Reimbursement>> GetByUserIdWithDetailsAsync(Guid userId, CancellationToken cancellationToken);
    Task<(IEnumerable<Reimbursement> Items, int TotalCount)> GetByUserIdPagedAsync(Guid userId, int page, int pageSize, string? search, string? status, CancellationToken cancellationToken);
    
    Task<DTOs.Reimbursement.ReimbursementManagerRevisionSummary> GetManagerRevisionSummaryAsync(Guid managerId, CancellationToken cancellationToken);

    Task<(IEnumerable<Reimbursement> Items, int TotalCount)> GetPendingForFinanceAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<IEnumerable<Reimbursement>> GetHistoryForFinanceAsync(CancellationToken cancellationToken);

    Task<(IEnumerable<Reimbursement> Items, int TotalCount)> GetPendingForManagerAsync(Guid managerId, int page, int pageSize, CancellationToken cancellationToken);
}
