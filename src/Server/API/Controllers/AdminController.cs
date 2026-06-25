using API.Application.Common;
using API.Application.Features.Admin;
using API.Application.Features.Admin.Commands;
using API.Application.Features.Admin.Queries;
using API.Application.Features.Chat;
using API.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.User;

namespace API.Web.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "IsAdmin")]
public sealed class AdminController(IAdminHandlers handlers, ILogger<AdminController> logger)
    : BaseController<AdminController>(logger)
{

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(CancellationToken ct)
        => Map(await handlers.GetUsers.HandleAsync(new GetUsersQuery(), ct));

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto, CancellationToken ct)
        => Map(await handlers.CreateUser.HandleAsync(new CreateUserCommand(dto), ct));

    [HttpPut("users/{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UserDto dto, CancellationToken ct)
        => Map(await handlers.UpdateUser.HandleAsync(new UpdateUserCommand(id, dto), ct));

    [HttpPost("users/{id:int}/toggle-ban")]
    public async Task<IActionResult> ToggleBan(int id, CancellationToken ct)
        => Map(await handlers.ToggleBan.HandleAsync(new ToggleBanCommand(id), ct));

    [HttpPost("users/{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordAdminDto dto, CancellationToken ct)
        => Map(await handlers.ResetPassword.HandleAsync(new ResetPasswordCommand(id, dto.NewPassword), ct));
}