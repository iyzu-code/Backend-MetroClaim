using MetroClaim.Api.DTOs.Trip;
using MetroClaim.Api.Models;
using MetroClaim.Api.Repositories;
using MetroClaim.Api.Repositories.Interfaces;
using MetroClaim.Api.Services.Interfaces;
using MetroClaim.Api.Utilities;

namespace MetroClaim.Api.Services;

public class TripService : ITripService
{
    private readonly ITripRepository _tripRepository;
    private readonly IReimbursementRepository _reimbursementRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserContext _userContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailHandler _emailHandler;

    public TripService(
        ITripRepository tripRepository,
        IReimbursementRepository reimbursementRepository,
        ICategoryRepository categoryRepository,
        IUserRepository userRepository,
        IUserContext userContext,
        IUnitOfWork unitOfWork,
        IEmailHandler emailHandler)
    {
        _tripRepository = tripRepository;
        _reimbursementRepository = reimbursementRepository;
        _categoryRepository = categoryRepository;
        _userRepository = userRepository;
        _userContext = userContext;
        _unitOfWork = unitOfWork;
        _emailHandler = emailHandler;
    }

    public async Task CreateTripAsync(CreateTripRequestDto requestDto, CancellationToken cancellationToken)
    {
        var managerId = _userContext.CurrentUserId;

        var categoryId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");

        var category = await _categoryRepository.GetByIdAsync(categoryId, cancellationToken);

        if (category is null)
        {
            throw new ArgumentException("Category not found.");
        }

        if (!requestDto.ParticipantIds.Any())
        {
            throw new ArgumentException("At least one participant is required.");
        }

        if (requestDto.ParticipantIds.Count != requestDto.ParticipantIds.Distinct().Count())
        {
            throw new ArgumentException("Duplicate participants detected in the request.");
        }

        var existingUsers = await _userRepository.GetUsersByIdsAsync(requestDto.ParticipantIds, cancellationToken);
        if (existingUsers.Count() != requestDto.ParticipantIds.Distinct().Count())
        {
            var foundIds = existingUsers.Select(u => u.Id).ToHashSet();
            var missingIds = requestDto.ParticipantIds.Where(id => !foundIds.Contains(id));
            throw new ArgumentException($"Participants not found: {string.Join(", ", missingIds)}");
        }

        if (requestDto.EndDate < requestDto.StartDate)
        {
            throw new ArgumentException("End date cannot be earlier than start date.");
        }

        if (requestDto.StartDate.Date < DateTime.UtcNow.Date)
        {
            throw new ArgumentException("Start date cannot be in the past.");
        }

        var conflictingUserIds = await _tripRepository.GetConflictingUserIdsAsync(
            requestDto.ParticipantIds,
            requestDto.StartDate,
            requestDto.EndDate,
            null,
            cancellationToken);

        if (conflictingUserIds.Any())
        {
            var conflictingUsers = await _userRepository.GetUsersByIdsAsync(conflictingUserIds, cancellationToken);
            var names = string.Join(", ", conflictingUsers.Select(u => u.FullName));
            throw new ArgumentException($"The following users have conflicting trips: {names}");
        }

        var tripId = Guid.NewGuid();

        var newTrip = new Trip
        {
            Id = tripId,
            UserId = managerId,
            Title = requestDto.Title,
            Description = requestDto.Description,
            Destination = requestDto.Destination,
            StartDate = requestDto.StartDate,
            EndDate = requestDto.EndDate,
            Cost = 0,
            TripStatus = TripStatus.ManagerSubmited,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var reimbursements = CreateReimbursementsForTrip(newTrip, categoryId, requestDto.ParticipantIds, DateTime.UtcNow);

        foreach (var r in reimbursements) newTrip.Reimbursements.Add(r);

        await _unitOfWork.CommitTransactionAsync(async () =>
        {
            await _tripRepository.CreateAsync(newTrip, cancellationToken);
        }, cancellationToken);
    }

    public async Task<TripDetailDto> GetTripByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdWithDetailsAsync(id, cancellationToken);
        if (trip is null) throw new ArgumentException($"Trip {id} not found.");

        var userId = _userContext.CurrentUserId;
        var isManager = trip.UserId == userId;
        var isParticipant = trip.Reimbursements.Any(r => r.UserId == userId);
        var isAdminOrFinance = _userContext.IsInRole("Admin") || _userContext.IsInRole("Finance");

        if (!isManager && !isParticipant && !isAdminOrFinance)
        {
            throw new ArgumentException("You are not authorized to view this trip.");
        }

        return MapToDetailDto(trip);
    }

    public async Task<IEnumerable<TripDetailDto>> GetTripsCreatedByMeAsync(CancellationToken cancellationToken)
    {
        var managerId = _userContext.CurrentUserId;
        var trips = await _tripRepository.GetByManagerIdAsync(managerId, cancellationToken);
        return trips.Select(MapToDetailDto);
    }

    public async Task<(IEnumerable<TripDetailDto> Items, int TotalCount)> GetTripsCreatedByMePagedAsync(int page, int limit, CancellationToken cancellationToken)
    {
        var managerId = _userContext.CurrentUserId;
        var (trips, totalCount) = await _tripRepository.GetByManagerIdPagedAsync(managerId, page, limit, cancellationToken);
        return (trips.Select(MapToDetailDto), totalCount);
    }

    public async Task<IEnumerable<TripDetailDto>> GetMyAssignedTripsAsync(CancellationToken cancellationToken)
    {
        var userId = _userContext.CurrentUserId;
        var trips = await _tripRepository.GetByParticipantIdAsync(userId, cancellationToken);
        return trips.Select(MapToDetailDto);
    }

    public async Task<IEnumerable<TripDetailDto>> GetTripsForFinanceAsync(CancellationToken cancellationToken)
    {
        if (!_userContext.IsInRole("Finance")) throw new UnauthorizedAccessException();
        var trips = await _tripRepository.GetForFinanceAsync(cancellationToken);
        return trips.Select(MapToDetailDto);
    }

    public async Task<(IEnumerable<TripDetailDto> Items, int TotalCount)> GetTripsForFinancePagedAsync(int page, int limit, CancellationToken cancellationToken)
    {
        if (!_userContext.IsInRole("Finance")) throw new UnauthorizedAccessException();
        var (trips, totalCount) = await _tripRepository.GetForFinancePagedAsync(page, limit, cancellationToken);
        return (trips.Select(MapToDetailDto), totalCount);
    }

    public async Task<IEnumerable<TripDetailDto>> GetFinanceTripHistoryAsync(CancellationToken cancellationToken)
    {
        if (!_userContext.IsInRole("Finance")) throw new UnauthorizedAccessException();
        var trips = await _tripRepository.GetHistoryForFinanceAsync(cancellationToken);
        return trips.Select(MapToDetailDto);
    }

    public async Task<(IEnumerable<TripDetailDto> Items, int TotalCount)> GetFinanceTripHistoryPagedAsync(int page, int limit, CancellationToken cancellationToken)
    {
        if (!_userContext.IsInRole("Finance")) throw new UnauthorizedAccessException();
        var (trips, totalCount) = await _tripRepository.GetHistoryForFinancePagedAsync(page, limit, cancellationToken);
        return (trips.Select(MapToDetailDto), totalCount);
    }

    public async Task<Guid> GetTripReimbursementIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = _userContext.CurrentUserId;
        var getAllReimbursement = await _reimbursementRepository.GetAllAsync(cancellationToken);
        var tripReimbursement = getAllReimbursement.FirstOrDefault(r => r.UserId == userId && r.TripId == id);

        return tripReimbursement.Id;
    }
    
    public async Task ReviewTripByFinanceAsync(Guid id, FinanceReviewTripDto requestDto, CancellationToken cancellationToken)
    {
        if (!_userContext.IsInRole("Finance")) throw new UnauthorizedAccessException();

        var trip = await _tripRepository.GetByIdAsync(id, cancellationToken);
        if (trip is null) throw new KeyNotFoundException("Trip not found.");

        if (trip.TripStatus != TripStatus.ManagerSubmited)
            throw new InvalidOperationException("Trip is not in submitted state.");

        if (requestDto.IsApproved)
        {
            trip.Cost = requestDto.AllocatedCost;
            trip.TripStatus = TripStatus.FinanceApproved;
        }
        else
        {
            trip.TripStatus = TripStatus.Canceled;
        }

        trip.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.CommitTransactionAsync(async () =>
        {
            await _tripRepository.UpdateAsync(trip);
        }, cancellationToken);

        // Notify Manager
        try
        {
            var manager = await _userRepository.GetUserWithDetailsAsync(trip.UserId, cancellationToken);
            if (!string.IsNullOrEmpty(manager?.Account?.Email))
            {
                string subject = "";
                string body = "";
                string title = trip.Title ?? "Trip Proposal";

                if (requestDto.IsApproved)
                {
                    subject = $"[MetroClaim] Trip Proposal Approved: {title}";
                    body = $"<p>Dear {manager.FullName},</p>" +
                           $"<p>Your trip proposal <b>{title}</b> has been approved by Finance.</p>" +
                           $"<p><b>Allocated Budget:</b> {requestDto.AllocatedCost:C}</p>" +
                           $"<p>You may now proceed with the trip arrangements.</p>";
                }
                else
                {
                    subject = $"[MetroClaim] Trip Proposal Rejected: {title}";
                    body = $"<p>Dear {manager.FullName},</p>" +
                           $"<p>Your trip proposal <b>{title}</b> has been rejected by Finance.</p>" +
                           $"<p>Status: Canceled</p>";
                }

                await _emailHandler.SendEmailAsync(new EmailDto(manager.Account.Email, subject, body));
            }
        }
        catch (Exception)
        {
            // Fire and forget
        }
    }

    public async Task PublishTripAsync(Guid id, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdWithDetailsAsync(id, cancellationToken);
        if (trip is null) throw new KeyNotFoundException("Trip not found.");

        if (trip.TripStatus != TripStatus.FinanceApproved)
            throw new InvalidOperationException("Trip must be approved by Finance first.");

        // Ubah Status
        trip.TripStatus = TripStatus.Ongoing;
        trip.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.CommitTransactionAsync(async () =>
        {
            await _tripRepository.UpdateAsync(trip);
        }, cancellationToken);

        // Notify Participants
        try
        {
            var participantIds = trip.Reimbursements.Select(r => r.UserId).Distinct().ToList();
            if (participantIds.Any())
            {
                var participants = await _userRepository.GetUsersByIdsAsync(participantIds, cancellationToken);
                foreach (var p in participants)
                {
                    if (!string.IsNullOrEmpty(p.Account?.Email))
                    {
                        var subject = $"[MetroClaim] Trip Confirmed: {trip.Title}";
                        var body = $"<p>Dear {p.FullName},</p>" +
                                   $"<p>The trip <b>{trip.Title}</b> to <b>{trip.Destination}</b> has been confirmed/published.</p>" +
                                   $"<p><b>Dates:</b> {trip.StartDate:dd MMM} - {trip.EndDate:dd MMM yyyy}</p>" +
                                   $"<p><b>Allocated Cost:</b> {trip.Cost:C}</p>" +
                                   $"<p>Please prepare accordingly.</p>";

                        await _emailHandler.SendEmailAsync(new EmailDto(p.Account.Email, subject, body));
                    }
                }
            }
        }
        catch (Exception)
        {
            // Fire and forget
        }
    }

    public async Task CloseTripAsync(Guid id, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(id, cancellationToken);
        if (trip is null) throw new KeyNotFoundException("Trip not found.");

        // Auth Check
        if (trip.UserId != _userContext.CurrentUserId && !_userContext.IsInRole("Admin"))
            throw new UnauthorizedAccessException("Only the Trip Manager can close this trip.");

        // Validation
        if (trip.TripStatus != TripStatus.Ongoing)
            throw new InvalidOperationException($"Cannot close trip. Current status is {trip.TripStatus}. Trip must be 'Ongoing' to be closed.");

        // Update Status
        trip.TripStatus = TripStatus.Closed;
        trip.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.CommitTransactionAsync(async () =>
        {
            await _tripRepository.UpdateAsync(trip);
        }, cancellationToken);
    }

    public async Task CancelTripAsync(Guid id, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(id, cancellationToken);
        if (trip is null) throw new KeyNotFoundException("Trip not found.");

        if (trip.UserId != _userContext.CurrentUserId && !_userContext.IsInRole("Admin"))
            throw new UnauthorizedAccessException();

        trip.TripStatus = TripStatus.Canceled;
        trip.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.CommitTransactionAsync(async () =>
        {
            await _tripRepository.UpdateAsync(trip);
        }, cancellationToken);
    }

    // =========================================================================
    // PRIVATE HELPERS
    // =========================================================================

    private List<Reimbursement> CreateReimbursementsForTrip(Trip trip, Guid categoryId, List<Guid> participantIds, DateTime now)
    {
        var list = new List<Reimbursement>();

        foreach (var userId in participantIds)
        {
            var reimbursementId = Guid.NewGuid();
            var reimbursement = new Reimbursement
            {
                Id = reimbursementId,
                UserId = userId,
                CategoryId = categoryId,
                TripId = trip.Id,
                Title = $"Business Trip Expense: {trip.Destination}",
                Description = "Auto-generated reimbursement for business trip. Please update with your expenses.",
                TotalAmount = 0,
                ReimbursementStatus = ReimbursementStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            reimbursement.ApprovalLogs.Add(new ApprovalLog
            {
                Id = Guid.NewGuid(),
                ReimbursementId = reimbursementId,
                UserId = trip.UserId,
                ApprovalLogStatus = ApprovalLogStatus.Drafted,
                Comment = "System auto generated from Trip",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            list.Add(reimbursement);
        }
        return list;
    }

    private TripDetailDto MapToDetailDto(Trip trip)
    {
        return new TripDetailDto(
            trip.Id,
            trip.Title ?? "-",
            trip.Description ?? "-",
            trip.Destination ?? "-",
            trip.StartDate,
            trip.EndDate,
            trip.Cost,
            trip.TripStatus.ToString(),
            trip.User?.FullName ?? "Unknown Manager",
            trip.CreatedAt,
            trip.Reimbursements.Select(r => new TripParticipantDto(
                r.UserId,
                r.User?.FullName ?? "Unknown Employee",
                r.ReimbursementStatus.ToString(),
                r.TotalAmount
            )).ToList()
        );
    }


}