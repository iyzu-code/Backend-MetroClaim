using MetroClaim.Api.DTOs.Trip;
using MetroClaim.Api.Models;

namespace MetroClaim.Api.Services.Interfaces;

public interface ITripService
{    
    Task<Guid> GetTripReimbursementIdAsync(Guid id, CancellationToken cancellationToken);
    Task<TripDetailDto> GetTripByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IEnumerable<TripDetailDto>> GetTripsCreatedByMeAsync(CancellationToken cancellationToken);
    Task<(IEnumerable<TripDetailDto> Items, int TotalCount)> GetTripsCreatedByMePagedAsync(int page, int limit, CancellationToken cancellationToken);
    
    Task<IEnumerable<TripDetailDto>> GetMyAssignedTripsAsync(CancellationToken cancellationToken);
    Task<IEnumerable<TripDetailDto>> GetTripsForFinanceAsync(CancellationToken cancellationToken);
    Task<(IEnumerable<TripDetailDto> Items, int TotalCount)> GetTripsForFinancePagedAsync(int page, int limit, CancellationToken cancellationToken);
    Task<IEnumerable<TripDetailDto>> GetFinanceTripHistoryAsync(CancellationToken cancellationToken);
    Task<(IEnumerable<TripDetailDto> Items, int TotalCount)> GetFinanceTripHistoryPagedAsync(int page, int limit, CancellationToken cancellationToken);

    // WRITE
    Task CreateTripAsync(CreateTripRequestDto requestDto, CancellationToken cancellationToken);
    Task CancelTripAsync(Guid id, CancellationToken cancellationToken);

    // WORKFLOW
    Task ReviewTripByFinanceAsync(Guid id, FinanceReviewTripDto requestDto, CancellationToken cancellationToken);
    Task PublishTripAsync(Guid id, CancellationToken cancellationToken);
    Task CloseTripAsync(Guid id, CancellationToken cancellationToken);

}
