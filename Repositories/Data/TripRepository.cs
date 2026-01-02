using System.Data.Common;
using MetroClaim.Api.Data;
using MetroClaim.Api.Models;
using MetroClaim.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MetroClaim.Api.Repositories.Data;

public class TripRepository : Repository<Trip>, ITripRepository
{
    private readonly MetroClaimApiDbContext _context;
    public TripRepository(MetroClaimApiDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<Trip?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Trips
            .Include(t => t.User)
            .Include(t => t.Reimbursements)
                .ThenInclude(r => r.User)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Trip>> GetByManagerIdAsync(Guid managerId, CancellationToken cancellationToken)
    {
        return await _context.Trips
            .Include(t => t.User)
            .Include(t => t.Reimbursements)
                .ThenInclude(r => r.User)
            .Where(t => t.UserId == managerId)
            .OrderByDescending(t => t.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<(IEnumerable<Trip> Items, int TotalCount)> GetByManagerIdPagedAsync(Guid managerId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _context.Trips
            .Include(t => t.User)
            .Include(t => t.Reimbursements)
                .ThenInclude(r => r.User)
            .Where(t => t.UserId == managerId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IEnumerable<Trip>> GetByParticipantIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.Trips
            .Include(t => t.User)
            .Include(t => t.Reimbursements)
                .ThenInclude(r => r.User)
            .Where(t => t.Reimbursements.Any(r => r.UserId == userId))
            .OrderByDescending(t => t.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Trip>> GetForFinanceAsync(CancellationToken cancellationToken)
    {
        return await _context.Trips
            .Include(t => t.User)
            .Include(t => t.Reimbursements)
                .ThenInclude(r => r.User)
            .Where(t => t.TripStatus == TripStatus.ManagerSubmited)
            .OrderBy(t => t.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<(IEnumerable<Trip> Items, int TotalCount)> GetForFinancePagedAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _context.Trips
            .Include(t => t.User)
            .Include(t => t.Reimbursements)
                .ThenInclude(r => r.User)
            .Where(t => t.TripStatus == TripStatus.ManagerSubmited);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IEnumerable<Trip>> GetHistoryForFinanceAsync(CancellationToken cancellationToken)
    {
        return await _context.Trips
            .Include(t => t.User)
            .Include(t => t.Reimbursements)
                .ThenInclude(r => r.User)
            .OrderByDescending(t => t.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<(IEnumerable<Trip> Items, int TotalCount)> GetHistoryForFinancePagedAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _context.Trips
            .Include(t => t.User)
            .Include(t => t.Reimbursements)
                .ThenInclude(r => r.User);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<Trip?> GetByIdWithParticipantsAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Trips
            .Include(t => t.Reimbursements)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }
    
    public async Task<IEnumerable<Guid>> GetConflictingUserIdsAsync(IEnumerable<Guid> participantIds, DateTime startDate, DateTime endDate, Guid? excludeTripId, CancellationToken cancellationToken)
    {
        var query = _context.Trips
            .Where(t => 
                t.TripStatus != TripStatus.Canceled &&
                t.StartDate <= endDate && t.EndDate >= startDate
            );

        if (excludeTripId.HasValue)
        {
            query = query.Where(t => t.Id != excludeTripId.Value);
        }

        return await query
            .SelectMany(t => t.Reimbursements)
            .Where(r => participantIds.Contains(r.UserId))
            .Select(r => r.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
