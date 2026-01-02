using MetroClaim.Api.Data;
using MetroClaim.Api.Models;
using MetroClaim.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MetroClaim.Api.Repositories.Data;

public class ReimbursementRepository : Repository<Reimbursement>, IReimbursementRepository
{
    private readonly MetroClaimApiDbContext _context;
    public ReimbursementRepository(MetroClaimApiDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Reimbursement>> GetAllWithReferencesAsync(CancellationToken cancellationToken)
    {
        return await _context.Reimbursements
            .Include(r => r.User)
            .Include(r => r.Category)
            .Include(r => r.Trip)
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Reimbursement?> GetByIdReadOnlyAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Reimbursements
            .Include(r => r.User)
            .Include(r => r.Category)
            .Include(r => r.Trip)
            .Include(r => r.Items)
            .Include(r => r.ApprovalLogs)
                .ThenInclude(log => log.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<Reimbursement?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Reimbursements
            .Include(r => r.User)
            .Include(r => r.Category)
            .Include(r => r.Trip)
            .Include(r => r.Items)
            .Include(r => r.ApprovalLogs)
                .ThenInclude(log => log.User)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<(IEnumerable<Reimbursement> Items, int TotalCount)> GetPendingForManagerAsync(Guid managerId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _context.Reimbursements
            .Include(r => r.User)
            .Include(r => r.Category)
            .Include(r => r.Trip)
            .Include(r => r.Items)
            .Include(r => r.ApprovalLogs)
                .ThenInclude(l => l.User)
            .Where(r =>
                r.ReimbursementStatus == ReimbursementStatus.Pending &&
                r.User!.ManagerId == managerId
            )
            .Where(r => r.ApprovalLogs
                .OrderByDescending(l => l.CreatedAt)
                .FirstOrDefault()!.ApprovalLogStatus == ApprovalLogStatus.Submitted);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IEnumerable<Reimbursement>> GetHistoryForManagerAsync(Guid managerId, CancellationToken cancellationToken)
    {
        return await _context.Reimbursements
            .Include(r => r.User)
            .Include(r => r.Category)
            .Include(r => r.Trip)
            .Include(r => r.Trip)
            .Include(r => r.ApprovalLogs)
                .ThenInclude(l => l.User)
            .Where(r => r.User!.ManagerId == managerId)
            // .Where(r =>
            //     r.ReimbursementStatus != ReimbursementStatus.Pending ||
            //     (
            //         r.ReimbursementStatus == ReimbursementStatus.Pending &&
            //         r.ApprovalLogs.OrderByDescending(l => l.CreatedAt).FirstOrDefault()!.ApprovalLogStatus != ApprovalLogStatus.Submitted
            //     )
            // )
            .OrderByDescending(r => r.UpdatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Reimbursement>> GetByUserIdWithDetailsAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.Reimbursements
            .Include(r => r.User)
            .Include(r => r.Category)
            .Include(r => r.Trip)
            // .Include(r => r.Items)
            .Include(r => r.ApprovalLogs)
                .ThenInclude(l => l.User)
            .Where(r => r.UserId == userId)
            .Where(r =>
                r.TripId == null
                ||
                (r.Trip != null && (
                    r.Trip.TripStatus == TripStatus.Ongoing ||
                    r.Trip.TripStatus == TripStatus.Closed
                ))
            )
            .OrderByDescending(r => r.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<(IEnumerable<Reimbursement> Items, int TotalCount)> GetByUserIdPagedAsync(
        Guid userId, 
        int page, 
        int pageSize, 
        string? search, 
        string? status, 
        CancellationToken cancellationToken)
    {
        var query = _context.Reimbursements
            .Include(r => r.User)
            .Include(r => r.Category)
            .Include(r => r.Trip)
            .Include(r => r.ApprovalLogs)
                .ThenInclude(l => l.User)
            .Where(r => r.UserId == userId)
            .Where(r =>
                r.TripId == null ||
                (r.Trip != null && (
                    r.Trip.TripStatus == TripStatus.Ongoing ||
                    r.Trip.TripStatus == TripStatus.Closed
                ))
            );

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(r => r.Title != null && r.Title.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusLower = status.Trim().ToLower();

            if (statusLower == "ongoing")
            {
                query = query.Where(r => r.ApprovalLogs
                    .OrderByDescending(l => l.CreatedAt)
                    .FirstOrDefault()!.ApprovalLogStatus == ApprovalLogStatus.Submitted);
            }
            else if (statusLower == "draft")
            {
                query = query.Where(r => r.ApprovalLogs
                    .OrderByDescending(l => l.CreatedAt)
                    .FirstOrDefault()!.ApprovalLogStatus == ApprovalLogStatus.Drafted);
            }
            else if (statusLower == "revision")
            {
                query = query.Where(r => r.ApprovalLogs
                    .OrderByDescending(l => l.CreatedAt)
                    .FirstOrDefault()!.ApprovalLogStatus == ApprovalLogStatus.ManagerRevision);
            }
            else if (statusLower == "rejected")
            {
                query = query.Where(r => 
                    r.ApprovalLogs.OrderByDescending(l => l.CreatedAt).FirstOrDefault()!.ApprovalLogStatus == ApprovalLogStatus.ManagerRejected ||
                    r.ApprovalLogs.OrderByDescending(l => l.CreatedAt).FirstOrDefault()!.ApprovalLogStatus == ApprovalLogStatus.FinanceRejected
                );
            }
            else if (Enum.TryParse<ApprovalLogStatus>(status, true, out var logStatus))
            {
                query = query.Where(r => r.ApprovalLogs
                    .OrderByDescending(l => l.CreatedAt)
                    .FirstOrDefault()!.ApprovalLogStatus == logStatus);
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<DTOs.Reimbursement.ReimbursementManagerRevisionSummary> GetManagerRevisionSummaryAsync(Guid managerId, CancellationToken cancellationToken)
    {
        var pendingRevisionCount = await _context.Reimbursements
            .Where(r => r.User!.ManagerId == managerId)
            .Where(r => r.ApprovalLogs
                .OrderByDescending(l => l.CreatedAt)
                .FirstOrDefault()!.ApprovalLogStatus == ApprovalLogStatus.ManagerRevision)
            .CountAsync(cancellationToken);

        var finishRevisionCount = await _context.Reimbursements
            .Where(r => r.User!.ManagerId == managerId)
            .Where(r => 
                r.ApprovalLogs.OrderByDescending(l => l.CreatedAt).FirstOrDefault()!.ApprovalLogStatus != ApprovalLogStatus.ManagerRevision 
                &&
                r.ApprovalLogs.OrderByDescending(l => l.CreatedAt).Skip(1).FirstOrDefault()!.ApprovalLogStatus == ApprovalLogStatus.ManagerRevision
            )
            .CountAsync(cancellationToken);


        return new DTOs.Reimbursement.ReimbursementManagerRevisionSummary(pendingRevisionCount, finishRevisionCount);
    }

    public async Task<(IEnumerable<Reimbursement> Items, int TotalCount)> GetPendingForFinanceAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _context.Reimbursements
            .Include(r => r.User)
                .ThenInclude(u => u!.Account)
            .Include(r => r.Category)
            .Include(r => r.Trip)
            .Include(r => r.Items)
            .Include(r => r.ApprovalLogs)
                .ThenInclude(l => l.User)
            .Where(r =>
                r.ReimbursementStatus == ReimbursementStatus.Pending
            )
            .Where(r => r.ApprovalLogs
                .OrderByDescending(l => l.CreatedAt)
                .FirstOrDefault()!.ApprovalLogStatus == ApprovalLogStatus.ManagerApproved);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IEnumerable<Reimbursement>> GetHistoryForFinanceAsync(CancellationToken cancellationToken)
    {
        return await _context.Reimbursements
           .Include(r => r.User)
           .Include(r => r.Category)
           .Include(r => r.Trip)
           .Include(r => r.ApprovalLogs)
               .ThenInclude(l => l.User)
           .OrderByDescending(r => r.CreatedAt)
           .AsNoTracking()
           .ToListAsync(cancellationToken);
    }
}
