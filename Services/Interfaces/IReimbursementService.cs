using MetroClaim.Api.DTOs.Reimbursement;
using MetroClaim.Api.Models;

namespace MetroClaim.Api.Services.Interfaces;

public interface IReimbursementService
{
    Task<IEnumerable<ReimbursemenGetResponseDto>> GetAllReimbursementsAsync(CancellationToken cancellationToken);

    Task<ReimbursementDetailDto> GetReimbursementByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IEnumerable<ReimbursementDetailDto>> GetMyReimbursementsAsync(CancellationToken cancellationToken);
    Task<(IEnumerable<ReimbursementDetailDto> Items, int TotalPages)> GetMyReimbursementsPageAsync(int page, string? search, string? status, CancellationToken cancellationToken);

    Task<(IEnumerable<ReimbursementDetailDto> Items, int TotalCount)> GetSubordinateReimbursementsAsync(int page, int limit, CancellationToken cancellationToken);

    Task<IEnumerable<ReimbursemenGetResponseDto>> GetManagerReimbursementHistoryAsync(CancellationToken cancellationToken);
    Task<(IEnumerable<ReimbursemenGetResponseDto> Items, int TotalPages)> GetManagerReimbursementHistoryPageAsync(int page, CancellationToken cancellationToken);
    Task<ReimbursementManagerRevisionSummary> GetManagerRevisionSummaryAsync(CancellationToken cancellationToken);

    Task<IEnumerable<ReimbursemenGetResponseDto>> GetFinanceReimbursementHistoryAsync(CancellationToken cancellationToken);
    Task<(IEnumerable<ReimbursemenGetResponseDto> Items, int TotalPages)> GetFinanceReimbursementHistoryPageAsync(int page, CancellationToken cancellationToken);
    Task<(IEnumerable<ReimbursementDetailDto> Items, int TotalCount)> GetForFinanceAsync(int page, int limit, CancellationToken cancellationToken);

    Task<ReimbursementDetailDto> CreateReimbursementAsync(ReimbursementCreateRequestDto requestDto, CancellationToken cancellationToken);

    Task UpdateReimbursementAsync(Guid id, ReimbursementUpdateRequestDto requestDto, CancellationToken cancellationToken);

    Task ProcessApprovalAsync(Guid id, ApprovalProcessDto requestDto, CancellationToken cancellationToken);

    Task DeleteReimbursementAsync(Guid id, CancellationToken cancellationToken);
}

