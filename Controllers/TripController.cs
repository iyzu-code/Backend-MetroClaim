using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MetroClaim.Api.DTOs.Trip;
using MetroClaim.Api.Services.Interfaces;
using MetroClaim.Api.Utilities;

namespace MetroClaim.Api.Controllers;

[ApiController]
[Route("api/trips")]
[Authorize] // Secara default semua butuh login
public class TripController : ControllerBase
{
    private readonly ITripService _tripService;

    public TripController(ITripService tripService)
    {
        _tripService = tripService;
    }

    // =========================================================================
    // GET ENDPOINTS
    // =========================================================================

    [HttpGet("{id}")]
    public async Task<IActionResult> GetTripById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _tripService.GetTripByIdAsync(id, cancellationToken);
        return Ok(new ApiResponse<TripDetailDto>(result));
    }

    [HttpGet("manager")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> GetTripsCreatedByMe(CancellationToken cancellationToken)
    {
        int page = int.TryParse(Request.Headers["X-Page"], out var p) ? p : 1;
        int limit = int.TryParse(Request.Headers["X-Limit"], out var l) ? l : 10;

        var (result, totalCount) = await _tripService.GetTripsCreatedByMePagedAsync(page, limit, cancellationToken);
        Response.Headers.Add("X-Total-Count", totalCount.ToString());

        return Ok(new ApiResponse<IEnumerable<TripDetailDto>>(result));
    }

    [HttpGet("assigned")]
    [Authorize(Roles = "Employee,Manager")]
    public async Task<IActionResult> GetMyAssignedTrips(CancellationToken cancellationToken)
    {
        var result = await _tripService.GetMyAssignedTripsAsync(cancellationToken);
        return Ok(new ApiResponse<IEnumerable<TripDetailDto>>(result));
    }

    [HttpGet("assigned/{id}")]
    [Authorize(Roles = "Employee")]
    public async Task<IActionResult> GetMyAssignedTripReimbursement(Guid id, CancellationToken cancellationToken)
    {
        var result = await _tripService.GetTripReimbursementIdAsync(id, cancellationToken);
        return Ok(new ApiResponse<object>(result));
    }

    [HttpGet("finance")]
    [Authorize(Roles = "Finance")]
    public async Task<IActionResult> GetTripsForFinance(CancellationToken cancellationToken)
    {
        int page = int.TryParse(Request.Headers["X-Page"], out var p) ? p : 1;
        int limit = int.TryParse(Request.Headers["X-Limit"], out var l) ? l : 10;

        var (result, totalCount) = await _tripService.GetTripsForFinancePagedAsync(page, limit, cancellationToken);
        Response.Headers.Add("X-Total-Count", totalCount.ToString());
        
        return Ok(new ApiResponse<IEnumerable<TripDetailDto>>(result));
    }

    [HttpGet("finance/history")]
    [Authorize(Roles = "Finance")]
    public async Task<IActionResult> GetFinanceTripHistory(CancellationToken cancellationToken)
    {
        int page = int.TryParse(Request.Headers["X-Page"], out var p) ? p : 1;
        int limit = int.TryParse(Request.Headers["X-Limit"], out var l) ? l : 10;

        var (result, totalCount) = await _tripService.GetFinanceTripHistoryPagedAsync(page, limit, cancellationToken);
        Response.Headers.Add("X-Total-Count", totalCount.ToString());

        return Ok(new ApiResponse<IEnumerable<TripDetailDto>>(result));
    }

    // =========================================================================
    // MANAGER ACTIONS
    // =========================================================================

    [HttpPost]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> CreateTrip([FromBody] CreateTripRequestDto requestDto, CancellationToken cancellationToken)
    {
        await _tripService.CreateTripAsync(requestDto, cancellationToken);
        // Return 201 Created dengan lokasi resource baru
        return Ok(new ApiResponse<object>("trip created"));
    }

    [HttpPut("{id}/cancel")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> CancelTrip(Guid id, CancellationToken cancellationToken)
    {
        await _tripService.CancelTripAsync(id, cancellationToken);
        return Ok(new ApiResponse<object>("trip cancelled"));
    }

    [HttpPost("{id}/publish")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> PublishTrip(Guid id, CancellationToken cancellationToken)
    {
        await _tripService.PublishTripAsync(id, cancellationToken);
        return Ok(new ApiResponse<object>("trip confirmed"));
    }

    [HttpPut("{id}/close")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> CloseTrip(Guid id, CancellationToken cancellationToken)
    {
        await _tripService.CloseTripAsync(id, cancellationToken);
        return Ok(new ApiResponse<object>("trip closed"));
    }

    // =========================================================================
    // FINANCE ACTIONS
    // =========================================================================

    [HttpPut("{id}/finance-review")]
    [Authorize(Roles = "Finance")]
    public async Task<IActionResult> ReviewTrip(Guid id, [FromBody] FinanceReviewTripDto requestDto, CancellationToken cancellationToken)
    {
        await _tripService.ReviewTripByFinanceAsync(id, requestDto, cancellationToken);
        
        string statusMsg = requestDto.IsApproved ? "approved" : "rejected";
        return Ok(new ApiResponse<object>("trip cost set and approved by finance"));
    }
}