using API.Application.Features.Status;
using API.Application.Features.Status.Commands;
using API.Application.Features.Status.Queries;
using API.Web.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.Online;

namespace API.Web.Controllers;

[ApiController]
[Route("api/status")]
[Authorize]
public sealed class StatusController(IStatusHandlers handlers, ILogger<StatusController> logger)
    : BaseController<StatusController>(logger)
{
    [HttpPost]
    public async Task<IActionResult> SetStatus([FromBody] SetStatusRequest request)
        => Map(await handlers.SetStatus.HandleAsync(new SetStatusCommand(GetCurrentUserId(),request.StatusType,request.Duration.Parse())));

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentStatus()
        => Map(await handlers.GetStatus.HandleAsync(new GetStatusQuery(GetCurrentUserId())));

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserStatus(int userId)
        => Map(await handlers.GetStatus.HandleAsync(new GetStatusQuery(userId)));
}