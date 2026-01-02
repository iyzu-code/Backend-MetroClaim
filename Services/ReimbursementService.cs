using MetroClaim.Api.DTOs.ApprovalLog;
using MetroClaim.Api.DTOs.Reimbursement;
using MetroClaim.Api.Models;
using MetroClaim.Api.Repositories;
using MetroClaim.Api.Repositories.Interfaces;
using MetroClaim.Api.Services.Interfaces;

namespace MetroClaim.Api.Services;

public class ReimbursementService : IReimbursementService
{
    private readonly IReimbursementRepository _reimbursementRepository;
    private readonly IUserLimitRepository _userLimitRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ITripRepository _tripRepository;
    private readonly IApprovalLogRepository _approvalLogRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserContext _userContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReimbursementItemRepository _reimbursementItemRepository;
    private readonly Utilities.IEmailHandler _emailHandler;

    public ReimbursementService(
        IReimbursementRepository reimbursementRepository,
        IUserLimitRepository userLimitRepository,
        ICategoryRepository categoryRepository,
        ITripRepository tripRepository,
        IApprovalLogRepository approvalLogRepository,
        IReimbursementItemRepository reimbursementItemRepository,
        IUserRepository userRepository,
        IUserContext userContext,
        IUnitOfWork unitOfWork,
        Utilities.IEmailHandler emailHandler)
    {
        _reimbursementRepository = reimbursementRepository;
        _userLimitRepository = userLimitRepository;
        _categoryRepository = categoryRepository;
        _tripRepository = tripRepository;
        _approvalLogRepository = approvalLogRepository;
        _userRepository = userRepository;
        _userContext = userContext;
        _unitOfWork = unitOfWork;
        _reimbursementItemRepository = reimbursementItemRepository;
        _emailHandler = emailHandler;
    }

    public async Task<ReimbursementDetailDto> CreateReimbursementAsync(ReimbursementCreateRequestDto requestDto, CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.CurrentUserId;

        var userLimit = await _userLimitRepository.GetByUserAndCategoryAsync(currentUserId, requestDto.CategoryId, cancellationToken);

        if (userLimit is null)
        {
            throw new ArgumentException("You have not set up a limit for this category. Please create a user limit first.");
        }

        if (!requestDto.Items.Any())
        {
            throw new NullReferenceException("Reimbursement must have at least one item.");
        }

        decimal calculatedTotal = requestDto.Items.Sum(x => x.Amount);

        if (calculatedTotal <= 0)
        {
            throw new ArgumentException("Amount of total reimbursement must greater than 0");
        }

        decimal maxLimit = userLimit.Category!.Limit;
        decimal projectedUsage = userLimit.LimitUsed + calculatedTotal;

        if (projectedUsage > maxLimit)
        {
            decimal remaining = maxLimit - userLimit.LimitUsed;
            throw new ArgumentException($"Insufficient limit balance. Remaining: {remaining:N2}, Requested: {calculatedTotal:N2}");
        }

        userLimit.LimitUsed = projectedUsage;
        userLimit.UpdatedAt = DateTime.UtcNow;

        var reimbursementId = Guid.NewGuid();

        var reimbursement = new Reimbursement
        {
            Id = reimbursementId,
            UserId = currentUserId,
            CategoryId = requestDto.CategoryId,
            Title = requestDto.Title,
            Description = requestDto.Description,
            TotalAmount = calculatedTotal,
            ReimbursementStatus = ReimbursementStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var itemsList = requestDto.Items.Select(i => new ReimbursementItem
        {
            Id = Guid.NewGuid(),
            ReimbursementId = reimbursementId,
            Amount = i.Amount,
            DateOfExpense = i.DateOfExpense,
            Receipt = i.Receipt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }).ToList();

        var initialLog = new ApprovalLog
        {
            Id = Guid.NewGuid(),
            ReimbursementId = reimbursementId,
            UserId = currentUserId,
            ApprovalLogStatus = ApprovalLogStatus.Submitted,
            Comment = "Initial Submision",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        reimbursement.Items = itemsList;
        reimbursement.ApprovalLogs.Add(initialLog);

        await _unitOfWork.CommitTransactionAsync(async () =>
        {
            await _userLimitRepository.UpdateAsync(userLimit);

            await _reimbursementRepository.CreateAsync(reimbursement, cancellationToken);

        }, cancellationToken);

        // Notify Manager
        var currentUserWithDetails = await _userRepository.GetUserWithDetailsAsync(currentUserId, cancellationToken);
        if (currentUserWithDetails?.ManagerId != null)
        {
            var manager = await _userRepository.GetUserWithDetailsAsync(currentUserWithDetails.ManagerId.Value, cancellationToken);
            if (!string.IsNullOrEmpty(manager?.Account?.Email))
            {
                var subject = $"[MetroClaim] New Reimbursement Request from {currentUserWithDetails.FullName}";
                var body = $"<p>Dear {manager.FullName},</p>" +
                           $"<p>You have a new reimbursement request from <b>{currentUserWithDetails.FullName}</b>.</p>" +
                           $"<p>Title: {reimbursement.Title}<br>Total: Rp{reimbursement.TotalAmount}</p>" +
                           $"<p>Please review it in the system.</p>";

                try
                {
                    await _emailHandler.SendEmailAsync(new Utilities.EmailDto(manager.Account.Email, subject, body));
                }
                catch (Exception)
                {
                    // Fire and forget email failure shouldn't rollback transaction
                    // Logging here would be good practice
                }
            }
        }

        reimbursement.User = currentUserWithDetails ?? new User
        {
            Id = currentUserId,
            FullName = _userContext.CurrentName,
        };
        reimbursement.Category = userLimit.Category;

        initialLog.User = reimbursement.User;

        return MapToDetailDto(reimbursement);
    }

    public async Task<IEnumerable<ReimbursemenGetResponseDto>> GetAllReimbursementsAsync(CancellationToken cancellationToken)
    {
        var reimbursements = await _reimbursementRepository.GetAllWithReferencesAsync(cancellationToken);

        return reimbursements.Select(r => new ReimbursemenGetResponseDto(
            r.Id,
            r.User?.EmployeeId ?? "-",
            r.User?.FullName ?? "Unknown",
            r.Category?.Name ?? "-",
            r.Trip?.Title,
            r.Title ?? "",
            r.Description ?? "",
            r.TotalAmount,
            r.ReimbursementStatus.ToString(),
            r.CreatedAt,
            r.UpdatedAt
        ));
    }

    public async Task<ReimbursementDetailDto> GetReimbursementByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var reimbursement = await _reimbursementRepository.GetByIdReadOnlyAsync(id, cancellationToken);

        if (reimbursement is null)
        {
            throw new KeyNotFoundException($"Reimbursement with ID {id} not found.");
        }

        var currentUserId = _userContext.CurrentUserId;
        var isAdmin = _userContext.IsInRole("Admin");
        var isFinance = _userContext.IsInRole("Finance");
        var isManager = _userContext.IsInRole("Manager");

        var isOwner = reimbursement.UserId == currentUserId;

        var isSubordinate = isManager && (reimbursement.User?.ManagerId == currentUserId);

        if (!isAdmin && !isFinance && !isOwner && !isSubordinate)
        {
            throw new UnauthorizedAccessException("You represent not authorized to view this reimbursement.");
        }

        return MapToDetailDto(reimbursement);
    }

    public async Task<(IEnumerable<ReimbursementDetailDto> Items, int TotalCount)> GetSubordinateReimbursementsAsync(int page, int limit, CancellationToken cancellationToken)
    {
        var managerId = _userContext.CurrentUserId;

        if (!_userContext.IsInRole("Manager"))
        {
            throw new UnauthorizedAccessException("Access denied. Manager role required.");
        }

        var (reimbursements, totalCount) = await _reimbursementRepository.GetPendingForManagerAsync(managerId, page, limit, cancellationToken);

        return (reimbursements.Select(MapToDetailDto), totalCount);
    }


    public async Task<IEnumerable<ReimbursemenGetResponseDto>> GetManagerReimbursementHistoryAsync(CancellationToken cancellationToken)
    {
        var managerId = _userContext.CurrentUserId;

        if (!_userContext.IsInRole("Manager"))
        {
            throw new UnauthorizedAccessException("Access denied. Manager role required.");
        }

        var reimbursements = await _reimbursementRepository.GetHistoryForManagerAsync(managerId, cancellationToken);

        return reimbursements.Select(r => new ReimbursemenGetResponseDto(
            r.Id,
            r.User?.EmployeeId ?? "-",
            r.User?.FullName ?? "Unknown",
            r.Category?.Name ?? "-",
            r.Trip?.Title,
            r.Title ?? "",
            r.Description ?? "",
            r.TotalAmount,
            r.ReimbursementStatus.ToString(),
            r.CreatedAt,
            r.UpdatedAt
        ));
    }

    public async Task<ReimbursementManagerRevisionSummary> GetManagerRevisionSummaryAsync(CancellationToken cancellationToken)
    {
        var managerId = _userContext.CurrentUserId;
        return await _reimbursementRepository.GetManagerRevisionSummaryAsync(managerId, cancellationToken);
    }

    public async Task<IEnumerable<ReimbursementDetailDto>> GetMyReimbursementsAsync(CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.CurrentUserId;

        var reimbursements = await _reimbursementRepository.GetByUserIdWithDetailsAsync(currentUserId, cancellationToken);

        return reimbursements.Select(MapToDetailDto);
    }

    public async Task<(IEnumerable<ReimbursementDetailDto> Items, int TotalCount)> GetForFinanceAsync(int page, int limit, CancellationToken cancellationToken)
    {
        if (!_userContext.IsInRole("Finance"))
        {
            throw new UnauthorizedAccessException("Access denied. Finance role required.");
        }

        var (reimbursements, totalCount) = await _reimbursementRepository.GetPendingForFinanceAsync(page, limit, cancellationToken);

        return (reimbursements.Select(MapToDetailDto), totalCount);
    }

    public async Task<IEnumerable<ReimbursemenGetResponseDto>> GetFinanceReimbursementHistoryAsync(CancellationToken cancellationToken)
    {
        if (!_userContext.IsInRole("Finance"))
        {
            throw new UnauthorizedAccessException("Access denied. Finance role required.");
        }

        var reimbursements = await _reimbursementRepository.GetHistoryForFinanceAsync(cancellationToken);

        return reimbursements.Select(r => new ReimbursemenGetResponseDto(
            r.Id,
            r.User?.EmployeeId ?? "-",
            r.User?.FullName ?? "Unknown",
            r.Category?.Name ?? "-",
            r.Trip?.Title,
            r.Title ?? "",
            r.Description ?? "",
            r.TotalAmount,
            r.ReimbursementStatus.ToString(),
            r.CreatedAt,
            r.UpdatedAt
        ));
    }

    private ReimbursementDetailDto MapToDetailDto(Reimbursement r)
    {
        return new ReimbursementDetailDto(
            r.Id,
            r.User?.EmployeeId ?? "-",
            r.User?.FullName ?? "Unknown",
            r.Category?.Name ?? "-",
            r.Trip?.Title,
            r.Title ?? "",
            r.Description ?? "",
            r.TotalAmount,
            r.ReimbursementStatus.ToString(),
            r.CreatedAt,
            r.UpdatedAt,
            r.Items.Select(i => new ReimbursementItemDto(
                i.Id,
                i.Amount,
                i.DateOfExpense,
                i.Receipt
            )).ToList(),
            r.ApprovalLogs.OrderByDescending(l => l.CreatedAt)
                          .Select(l => new ApprovalLogDto(
                              l.Id,
                              l.User?.FullName ?? "System/Unknown",
                              l.ApprovalLogStatus.ToString(),
                              l.Comment,
                              l.CreatedAt
                          )).ToList()
        );
    }


    public async Task UpdateReimbursementAsync(Guid id, ReimbursementUpdateRequestDto requestDto, CancellationToken cancellationToken)
    {
        // =========================
        // 1. LOAD & VALIDATION
        // =========================
        var reimbursement = await _reimbursementRepository
            .GetByIdForUpdateAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Reimbursement {id} not found.");

        var currentUserId = _userContext.CurrentUserId;

        if (reimbursement.UserId != currentUserId)
            throw new UnauthorizedAccessException();

        // Validate Editable
        var lastLog = reimbursement.ApprovalLogs.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        bool isEditable = reimbursement.ReimbursementStatus == ReimbursementStatus.Pending &&
                          (lastLog == null ||
                           lastLog.ApprovalLogStatus == ApprovalLogStatus.Drafted ||
                           lastLog.ApprovalLogStatus == ApprovalLogStatus.ManagerRevision);

        if (!isEditable)
            throw new InvalidOperationException("Cannot edit reimbursement that is already submitted or processed.");

        // Validate Category
        // Category cannot be changed during update.
        Guid targetCategoryId = reimbursement.CategoryId;

        // Validate Limit
        decimal newTotal = requestDto.Items.Sum(x => x.Amount);

        if (reimbursement.TripId.HasValue)
        {
            // CASE A: Trip
            var trip = await _tripRepository.GetByIdWithParticipantsAsync(reimbursement.TripId.Value, cancellationToken);

            if (trip != null)
            {
                decimal currentTripUsage = trip.Reimbursements
                    .Where(r => r.Id != id && r.ReimbursementStatus != ReimbursementStatus.Rejected)
                    .Sum(r => r.TotalAmount);

                if ((currentTripUsage + newTotal) > trip.Cost)
                {
                    decimal remaining = trip.Cost - currentTripUsage;
                    throw new InvalidOperationException($"Trip budget exceeded. Remaining: {remaining:N2}");
                }
            }
        }
        else
        {
            // CASE B: Personal
            var userLimit = await _userLimitRepository.GetByUserAndCategoryAsync(reimbursement.UserId, targetCategoryId, cancellationToken);

            if (userLimit is null)
            {
                throw new InvalidOperationException($"You don't have a limit set for this category.");
            }

            decimal oldAmount = reimbursement.Items.Sum(x => x.Amount);
            decimal projectedLimitUsage = userLimit.LimitUsed - oldAmount + newTotal;

            if (projectedLimitUsage > userLimit.Category!.Limit)
            {
                throw new InvalidOperationException($"Personal category limit exceeded.");
            }

            // Direct Update
            userLimit.LimitUsed = projectedLimitUsage;
            userLimit.UpdatedAt = DateTime.UtcNow;
            await _userLimitRepository.UpdateAsync(userLimit);
        }

        // =========================
        // 2. PREPARE STATE CHANGES
        // =========================

        // Update header (tracked entity)
        reimbursement.Title = requestDto.Title;
        reimbursement.Description = requestDto.Description;
        reimbursement.TotalAmount = newTotal;
        reimbursement.UpdatedAt = DateTime.UtcNow;



        // Prepare delete
        var itemsToDelete = reimbursement.Items.ToList();

        // Prepare create
        var newItems = requestDto.Items.Select(dto => new ReimbursementItem
        {
            Id = Guid.NewGuid(),
            ReimbursementId = reimbursement.Id,
            Amount = dto.Amount,
            DateOfExpense = dto.DateOfExpense,
            Receipt = dto.Receipt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }).ToList();

        // Prepare log
        var log = new ApprovalLog
        {
            Id = Guid.NewGuid(),
            ReimbursementId = reimbursement.Id,
            UserId = currentUserId,
            ApprovalLogStatus = ApprovalLogStatus.Submitted,
            Comment = "Reimbursement updated by user.",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // =========================
        // 3. UOW = PURE PERSISTENCE
        // =========================
        await _unitOfWork.CommitTransactionAsync(async () =>
        {
            await _reimbursementItemRepository.DeleteRangeAsync(itemsToDelete);
            await _reimbursementItemRepository.CreateRangeAsync(newItems);
            await _approvalLogRepository.CreateAsync(log, cancellationToken);
        }, cancellationToken);

        // Notify Manager of Update
        var currentUserWithDetails = await _userRepository.GetUserWithDetailsAsync(currentUserId, cancellationToken);
        if (currentUserWithDetails?.ManagerId != null)
        {
            var manager = await _userRepository.GetUserWithDetailsAsync(currentUserWithDetails.ManagerId.Value, cancellationToken);
            if (!string.IsNullOrEmpty(manager?.Account?.Email))
            {
                var subject = $"[MetroClaim] Reimbursement Updated by {currentUserWithDetails.FullName}";
                var body = $"<p>Dear {manager.FullName},</p>" +
                           $"<p>The reimbursement request from <b>{currentUserWithDetails.FullName}</b> has been updated.</p>" +
                           $"<p>Title: {reimbursement.Title}<br>New Total: Rp{newTotal:N2}</p>" +
                           $"<p>Please review the changes in the system.</p>";

                try
                {
                    await _emailHandler.SendEmailAsync(new Utilities.EmailDto(manager.Account.Email, subject, body));
                }
                catch (Exception)
                {
                    // Fire and forget
                }
            }
        }
    }

    public async Task DeleteReimbursementAsync(Guid id, CancellationToken cancellationToken)
    {
        var reimbursement = await _reimbursementRepository.GetByIdForUpdateAsync(id, cancellationToken);
        // ... validation ...

        // Logic Refund (Modifikasi)
        UserLimit? userLimitToUpdate = null;

        // HANYA REFUND JIKA BUKAN TRIP
        if (reimbursement.TripId == null)
        {
            //Guid userId, Guid categoryId, CancellationToken cancellationToken
            var userLimit = await _userLimitRepository.GetByUserAndCategoryAsync(reimbursement.UserId, reimbursement.CategoryId, cancellationToken);
            if (userLimit != null)
            {
                userLimit.LimitUsed -= reimbursement.TotalAmount;
                if (userLimit.LimitUsed < 0) userLimit.LimitUsed = 0;
                userLimitToUpdate = userLimit;
            }
        }

        // Commit Transaction
        await _unitOfWork.CommitTransactionAsync(async () =>
        {
            if (userLimitToUpdate != null) await _userLimitRepository.UpdateAsync(userLimitToUpdate);
            await _reimbursementRepository.DeleteAsync(reimbursement);
        }, cancellationToken);
    }

    public async Task ProcessApprovalAsync(Guid id, ApprovalProcessDto dto, CancellationToken cancelationToken)
    {
        // =========================
        // 1. LOAD & VALIDATION
        // =========================
        var reimbursement = await _reimbursementRepository.GetByIdForUpdateAsync(id, cancelationToken);
        if (reimbursement is null)
            throw new KeyNotFoundException($"Reimbursement {id} not found.");

        var userId = _userContext.CurrentUserId;

        var lastLog = reimbursement.ApprovalLogs
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();

        var lastStatus = lastLog?.ApprovalLogStatus ?? ApprovalLogStatus.Drafted;

        // Role & Permission Checks
        var isManager = _userContext.IsInRole("Manager") &&
                        reimbursement.User?.ManagerId == userId;

        var isFinance = _userContext.IsInRole("Finance");

        if (!isManager && !isFinance)
            throw new UnauthorizedAccessException("You are not authorized.");

        ApprovalLogStatus newLogStatus;
        ReimbursementStatus newHeaderStatus;
        bool refundLimit = false;
        bool shouldAddDueReimbursement = false;

        if (isManager)
        {
            if (lastStatus != ApprovalLogStatus.Submitted)
                throw new InvalidOperationException("Manager cannot process this reimbursement.");

            switch (dto.Action)
            {
                case ApprovalAction.Approve:
                    newLogStatus = ApprovalLogStatus.ManagerApproved;
                    newHeaderStatus = ReimbursementStatus.Pending;
                    break;

                case ApprovalAction.Revise:
                    newLogStatus = ApprovalLogStatus.ManagerRevision;
                    newHeaderStatus = ReimbursementStatus.Pending;
                    break;

                case ApprovalAction.Reject:
                    newLogStatus = ApprovalLogStatus.ManagerRejected;
                    newHeaderStatus = ReimbursementStatus.Rejected;
                    refundLimit = true;
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        else  // FINANCE
        {
            if (lastStatus != ApprovalLogStatus.ManagerApproved)
                throw new InvalidOperationException("Finance only processes ManagerApproved reimbursements.");

            if (dto.Action == ApprovalAction.Revise)
                throw new InvalidOperationException("Finance cannot revise.");

            switch (dto.Action)
            {
                case ApprovalAction.Approve:
                    newLogStatus = ApprovalLogStatus.FinanceApproved;
                    newHeaderStatus = ReimbursementStatus.Approved;
                    shouldAddDueReimbursement = true;
                    break;

                case ApprovalAction.Reject:
                    newLogStatus = ApprovalLogStatus.FinanceRejected;
                    newHeaderStatus = ReimbursementStatus.Rejected;
                    refundLimit = true;
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // Validate Comment
        if ((dto.Action == ApprovalAction.Reject || dto.Action == ApprovalAction.Revise)
            && string.IsNullOrWhiteSpace(dto.Comment))
            throw new ArgumentException("Comment is required.");


        // =========================
        // 2. PREPARE STATE CHANGES
        // =========================

        // A. Load Secondary Entities if needed
        UserLimit? limitToUpdate = null;
        if (refundLimit)
        {
            limitToUpdate = await _userLimitRepository
                .GetByUserAndCategoryAsync(reimbursement.UserId, reimbursement.CategoryId, cancelationToken);
        }

        User? userToUpdate = null;
        if (shouldAddDueReimbursement)
        {
            userToUpdate = await _userRepository.GetByIdAsync(reimbursement.UserId, cancelationToken);
        }

        // B. Apply Updates (In-Memory)

        // Header
        reimbursement.ReimbursementStatus = newHeaderStatus;
        reimbursement.UpdatedAt = DateTime.UtcNow;

        // Limit
        if (limitToUpdate != null)
        {
            limitToUpdate.LimitUsed -= reimbursement.TotalAmount;
            if (limitToUpdate.LimitUsed < 0) limitToUpdate.LimitUsed = 0;
            limitToUpdate.UpdatedAt = DateTime.UtcNow;
        }

        // User DueReimbursement
        if (userToUpdate != null)
        {
            userToUpdate.DueReimbursement += reimbursement.TotalAmount;
            userToUpdate.UpdatedAt = DateTime.UtcNow;
        }

        // Log
        var newLog = new ApprovalLog
        {
            Id = Guid.NewGuid(),
            ReimbursementId = reimbursement.Id,
            UserId = userId,
            ApprovalLogStatus = newLogStatus,
            Comment = dto.Comment,
            CreatedAt = DateTime.UtcNow
        };

        // =========================
        // 3. UOW = PURE PERSISTENCE
        // =========================
        await _unitOfWork.CommitTransactionAsync(async () =>
        {
            if (limitToUpdate != null)
                await _userLimitRepository.UpdateAsync(limitToUpdate);

            if (userToUpdate != null)
                await _userRepository.UpdateAsync(userToUpdate);

            await _approvalLogRepository.CreateAsync(newLog, cancelationToken);
        }, cancelationToken);

        // =========================
        // 4. EMAIL NOTIFICATIONS
        // =========================
        try
        {
            var requester = await _userRepository.GetUserWithDetailsAsync(reimbursement.UserId, cancelationToken);
            var approver = await _userRepository.GetUserWithDetailsAsync(userId, cancelationToken);

            if (requester?.Account?.Email != null && approver != null)
            {
                string subject = "";
                string body = "";
                string approverName = approver.FullName ?? "Approver";
                string reimbursementTitle = reimbursement.Title;

                if (newLogStatus == ApprovalLogStatus.ManagerApproved)
                {
                    subject = $"[MetroClaim] Approved by Manager: {reimbursementTitle}";
                    body = $"<p>Dear {requester.FullName},</p>" +
                           $"<p>Your reimbursement <b>{reimbursementTitle}</b> has been approved by {approverName}.</p>" +
                           $"<p>It has been forwarded to Finance or processing.</p>";
                }
                else if (newLogStatus == ApprovalLogStatus.ManagerRevision)
                {
                    subject = $"[MetroClaim] Revision Requested: {reimbursementTitle}";
                    body = $"<p>Dear {requester.FullName},</p>" +
                           $"<p>Manager {approverName} requested a revision for <b>{reimbursementTitle}</b>.</p>" +
                           $"<p><b>Comment:</b> {dto.Comment}</p>" +
                           $"<p>Please update your request.</p>";
                }
                else if (newLogStatus == ApprovalLogStatus.ManagerRejected)
                {
                    subject = $"[MetroClaim] Rejected by Manager: {reimbursementTitle}";
                    body = $"<p>Dear {requester.FullName},</p>" +
                           $"<p>Your reimbursement <b>{reimbursementTitle}</b> was rejected by {approverName}.</p>" +
                           $"<p><b>Reason:</b> {dto.Comment}</p>";
                }
                else if (newLogStatus == ApprovalLogStatus.FinanceApproved)
                {
                    subject = $"[MetroClaim] Approved by Finance: {reimbursementTitle}";
                    body = $"<p>Dear {requester.FullName},</p>" +
                           $"<p>Good news! Your reimbursement <b>{reimbursementTitle}</b> has been approved by Finance ({approverName}).</p>" +
                           $"<p>The funds will be added to your due reimbursement.</p>";
                }
                else if (newLogStatus == ApprovalLogStatus.FinanceRejected)
                {
                    subject = $"[MetroClaim] Rejected by Finance: {reimbursementTitle}";
                    body = $"<p>Dear {requester.FullName},</p>" +
                           $"<p>Your reimbursement <b>{reimbursementTitle}</b> was rejected by Finance ({approverName}).</p>" +
                           $"<p><b>Reason:</b> {dto.Comment}</p>";
                }

                if (!string.IsNullOrEmpty(subject))
                {
                    await _emailHandler.SendEmailAsync(new Utilities.EmailDto(requester.Account.Email, subject, body));
                }
            }
        }
        catch (Exception)
        {
            // Fire and forget
        }
    }

    public async Task<(IEnumerable<ReimbursementDetailDto> Items, int TotalPages)> GetMyReimbursementsPageAsync(int page, string? search, string? status, CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.CurrentUserId;
        int itemsPerPage = 10;

        var (items, totalCount) = await _reimbursementRepository.GetByUserIdPagedAsync(
            currentUserId, 
            page, 
            itemsPerPage, 
            search, 
            status, 
            cancellationToken);

        var totalPages = (int)Math.Ceiling(totalCount / (double)itemsPerPage);

        return (items.Select(MapToDetailDto), totalPages);
    }


    public async Task<(IEnumerable<ReimbursemenGetResponseDto> Items, int TotalPages)> GetManagerReimbursementHistoryPageAsync(int page, CancellationToken cancellationToken)
    {
        var managerId = _userContext.CurrentUserId;
        var itemsPerPage = 10;

        if (!_userContext.IsInRole("Manager"))
        {
            throw new UnauthorizedAccessException("Access denied. Manager role required.");
        }

        var reimbursements = await _reimbursementRepository.GetHistoryForManagerAsync(managerId, cancellationToken);

        var totalPages = (int)Math.Ceiling(reimbursements.Count() / (double)itemsPerPage);
        var pagedReimbursements = reimbursements
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * itemsPerPage)
            .Take(10);

        var formated = pagedReimbursements.Select(r => new ReimbursemenGetResponseDto(
            r.Id,
            r.User?.EmployeeId ?? "-",
            r.User?.FullName ?? "Unknown",
            r.Category?.Name ?? "-",
            r.Trip?.Title,
            r.Title ?? "",
            r.Description ?? "",
            r.TotalAmount,
            r.ReimbursementStatus.ToString(),
            r.CreatedAt,
            r.UpdatedAt
        ));

        return (formated, totalPages);
    }

    public async Task<(IEnumerable<ReimbursemenGetResponseDto> Items, int TotalPages)> GetFinanceReimbursementHistoryPageAsync(int page, CancellationToken cancellationToken)
    {
        var itemsPerPage = 10;

        if (!_userContext.IsInRole("Finance"))
        {
            throw new UnauthorizedAccessException("Access denied. Finance role required.");
        }

        var reimbursements = await _reimbursementRepository.GetHistoryForFinanceAsync(cancellationToken);

        var totalPages = (int)Math.Ceiling(reimbursements.Count() / (double)itemsPerPage);
        var pagedReimbursements = reimbursements
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * itemsPerPage)
            .Take(10);

        var formated = pagedReimbursements.Select(r => new ReimbursemenGetResponseDto(
            r.Id,
            r.User?.EmployeeId ?? "-",
            r.User?.FullName ?? "Unknown",
            r.Category?.Name ?? "-",
            r.Trip?.Title,
            r.Title ?? "",
            r.Description ?? "",
            r.TotalAmount,
            r.ReimbursementStatus.ToString(),
            r.CreatedAt,
            r.UpdatedAt
        ));
        return (formated, totalPages);
    }
}
