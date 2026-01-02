using MetroClaim.Api.DTOs.Reimbursement;
using MetroClaim.Api.Models;
using MetroClaim.Api.Services.Interfaces;
using MetroClaim.Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetroClaim.Api.Controllers;

[ApiController]
[Route("api/reimbursement")]
[Authorize]
public class ReimbursementController : ControllerBase
{
    private readonly IReimbursementService _reimbursementService;

    public ReimbursementController(IReimbursementService reimbursementService)
    {
        _reimbursementService = reimbursementService;
    }

    [HttpPost]
    [Authorize(Roles = "Employee")]
    public async Task<IActionResult> CreateReimbursement(ReimbursementCreateRequestDto requestDto, CancellationToken cancellationToken)
    {
        var reimbursement = await _reimbursementService.CreateReimbursementAsync(requestDto, cancellationToken);
        return Ok(new ApiResponse<ReimbursementDetailDto>(reimbursement));
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAllReimbursement(CancellationToken cancellationToken)
    {
        var reimbursements = await _reimbursementService.GetAllReimbursementsAsync(cancellationToken);
        return Ok(new ApiResponse<IEnumerable<ReimbursemenGetResponseDto>>(reimbursements));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetReimbursementById(Guid id, CancellationToken cancellationToken)
    {
        var reimbursement = await _reimbursementService.GetReimbursementByIdAsync(id, cancellationToken);
        return Ok(new ApiResponse<ReimbursementDetailDto>(reimbursement));
    }

    [HttpGet("manager")]
    [Authorize(Roles = "Manager")]

    public async Task<IActionResult> GetAllSubordinateReimbursement(CancellationToken cancellationToken)
    {
        int page = int.TryParse(Request.Headers["X-Page"], out var p) ? p : 1;
        int limit = int.TryParse(Request.Headers["X-Limit"], out var l) ? l : 10;

        var (reimbursements, totalCount) = await _reimbursementService.GetSubordinateReimbursementsAsync(page, limit, cancellationToken);
        
        Response.Headers.Add("X-Total-Count", totalCount.ToString());
        return Ok(new ApiResponse<IEnumerable<ReimbursementDetailDto>>(reimbursements));
    }

    [HttpGet("manager/history")]
    [Authorize(Roles = "Manager")]

    public async Task<IActionResult> GetManagerReimbursementHistory(CancellationToken cancellationToken)
    {
        var reimbursements = await _reimbursementService.GetManagerReimbursementHistoryAsync(cancellationToken);
        return Ok(new ApiResponse<IEnumerable<ReimbursemenGetResponseDto>>(reimbursements));
    }

    [HttpGet("manager/history/{page}")]
    [Authorize(Roles = "Manager")]

    public async Task<IActionResult> GetManagerReimbursementHistory(int page, CancellationToken cancellationToken)
    {
        var (reimbursements, totalPage) = await _reimbursementService.GetManagerReimbursementHistoryPageAsync(page, cancellationToken);
        Response.Headers.Add("X-Total-Pages",totalPage.ToString());
        return Ok(new ApiResponse<IEnumerable<ReimbursemenGetResponseDto>>(reimbursements));
    }

    [HttpGet("manager/revision-summary")]
    [Authorize(Roles = "Manager")]
    public async Task<IActionResult> GetManagerRevisionSummary(CancellationToken cancellationToken)
    {
        var summary = await _reimbursementService.GetManagerRevisionSummaryAsync(cancellationToken);
        return Ok(new ApiResponse<ReimbursementManagerRevisionSummary>(summary));
    }

    [HttpGet("finance")]
    [Authorize(Roles = "Finance")]

    public async Task<IActionResult> GetManagerApprovedReimbursement(CancellationToken cancellationToken)
    {
        int page = int.TryParse(Request.Headers["X-Page"], out var p) ? p : 1;
        int limit = int.TryParse(Request.Headers["X-Limit"], out var l) ? l : 10;

        var (reimbursements, totalCount) = await _reimbursementService.GetForFinanceAsync(page, limit, cancellationToken);
        
        Response.Headers.Add("X-Total-Count", totalCount.ToString());
        return Ok(new ApiResponse<IEnumerable<ReimbursementDetailDto>>(reimbursements));
    }

    [HttpGet("finance/history")]
    [Authorize(Roles = "Finance")]
    public async Task<IActionResult> GetFinanceReimbursementHistory(CancellationToken cancellationToken)
    {
        var reimbursements = await _reimbursementService.GetFinanceReimbursementHistoryAsync(cancellationToken);
        return Ok(new ApiResponse<IEnumerable<ReimbursemenGetResponseDto>>(reimbursements));
    }
    [HttpGet("finance/history/{page}")]
    [Authorize(Roles = "Finance")]
    public async Task<IActionResult> GetFinanceReimbursementHistory(int page, CancellationToken cancellationToken)
    {
        var (reimbursements,totalPage) = await _reimbursementService.GetFinanceReimbursementHistoryPageAsync(page, cancellationToken);
        Response.Headers.Add("X-Total-Pages",totalPage.ToString());
        return Ok(new ApiResponse<IEnumerable<ReimbursemenGetResponseDto>>(reimbursements));
    }

    [HttpGet("me")]
    [Authorize(Roles = "Employee")]
    public async Task<IActionResult> GetMyReimbursement(CancellationToken cancellationToken)
    {
        var reimbursements = await _reimbursementService.GetMyReimbursementsAsync(cancellationToken);
        return Ok(new ApiResponse<IEnumerable<ReimbursementDetailDto>>(reimbursements));
    }

    [HttpGet("me/{page}")]
    [Authorize(Roles = "Employee")]
    public async Task<IActionResult> GetMyReimbursementPaged(int page, [FromQuery] string? search, [FromQuery] string? status, CancellationToken cancellationToken)
    {
        var (reimbursements, totalPage) = await _reimbursementService.GetMyReimbursementsPageAsync(page, search, status, cancellationToken);
        
        Response.Headers.Add("X-Total-Pages",totalPage.ToString());
        return Ok(new ApiResponse<IEnumerable<ReimbursementDetailDto>>(reimbursements));
    }


    [HttpPatch("{id}")]
    [Authorize(Roles = "Manager,Finance")]

    public async Task<IActionResult> ReimbursementApproval(Guid id, ApprovalProcessDto requestDto, CancellationToken cancellationToken)
    {
        await _reimbursementService.ProcessApprovalAsync(id, requestDto, cancellationToken);
        return Ok(new ApiResponse<object>("reimbursement approval processed"));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Employee")]
    public async Task<IActionResult> UpdateReimbursement(Guid id, ReimbursementUpdateRequestDto requestDto, CancellationToken cancellationToken)
    {
        await _reimbursementService.UpdateReimbursementAsync(id, requestDto, cancellationToken);
        return Ok(new ApiResponse<object>("reimbursement updated"));
    }
}
