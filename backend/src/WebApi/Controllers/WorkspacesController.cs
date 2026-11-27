using System.Security.Claims;
using DocuArchive.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DocuArchive.WebApi.Controllers;

internal static class ControllerUser
{
    internal static string Id(ControllerBase c) =>
        c.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? c.User.FindFirstValue("sub")
        ?? throw new UnauthorizedException("Missing user id claim.");
}

/// <summary>Workspace endpoints. Thin controller — logic lives in <see cref="WorkspaceService"/> (Application).</summary>
[ApiController]
[Route("api/workspaces")]
[Authorize]
[EnableRateLimiting("default")]
public class WorkspacesController : ControllerBase
{
    private readonly WorkspaceService _workspaces;
    public WorkspacesController(WorkspaceService workspaces) => _workspaces = workspaces;

    /// <summary>Lists only the workspaces the current user is a member of.</summary>
    [HttpGet]
    public async Task<ActionResult<List<WorkspaceDto>>> ListMine(CancellationToken ct)
        => Ok(await _workspaces.ListMineAsync(ControllerUser.Id(this), ct));

    /// <summary>Creates a workspace; the creator becomes its WorkspaceOwner.</summary>
    [HttpPost]
    public async Task<ActionResult<WorkspaceDto>> Create([FromBody] CreateWorkspaceRequest request, CancellationToken ct)
    {
        var workspace = await _workspaces.CreateAsync(ControllerUser.Id(this), request, ct);
        return CreatedAtAction(nameof(Get), new { workspaceId = workspace.Id }, workspace);
    }

    /// <summary>Gets one workspace (members only; non-members get 404).</summary>
    [HttpGet("{workspaceId:guid}")]
    public async Task<ActionResult<WorkspaceDto>> Get(Guid workspaceId, CancellationToken ct)
        => Ok(await _workspaces.GetAsync(ControllerUser.Id(this), workspaceId, ct));

    /// <summary>Lists workspace members.</summary>
    [HttpGet("{workspaceId:guid}/members")]
    public async Task<ActionResult<List<MemberDto>>> ListMembers(Guid workspaceId, CancellationToken ct)
        => Ok(await _workspaces.ListMembersAsync(ControllerUser.Id(this), workspaceId, ct));

    /// <summary>Adds a member. WorkspaceOwner / WorkspaceAdmin only.</summary>
    [HttpPost("{workspaceId:guid}/members")]
    public async Task<ActionResult<MemberDto>> AddMember(Guid workspaceId, [FromBody] AddMemberRequest request, CancellationToken ct)
    {
        var member = await _workspaces.AddMemberAsync(ControllerUser.Id(this), workspaceId, request, ct);
        return CreatedAtAction(nameof(ListMembers), new { workspaceId }, member);
    }

    /// <summary>Removes a member. WorkspaceOwner / WorkspaceAdmin only; the last owner cannot be removed.</summary>
    [HttpDelete("{workspaceId:guid}/members/{memberUserId}")]
    public async Task<IActionResult> RemoveMember(Guid workspaceId, string memberUserId, CancellationToken ct)
    {
        await _workspaces.RemoveMemberAsync(ControllerUser.Id(this), workspaceId, memberUserId, ct);
        return NoContent();
    }

    /// <summary>Changes a member's role. WorkspaceOwner / WorkspaceAdmin only; ownership cannot be granted or removed here.</summary>
    [HttpPatch("{workspaceId:guid}/members/{memberUserId}")]
    public async Task<ActionResult<MemberDto>> ChangeMemberRole(Guid workspaceId, string memberUserId, [FromBody] ChangeMemberRoleRequest request, CancellationToken ct)
        => Ok(await _workspaces.ChangeMemberRoleAsync(ControllerUser.Id(this), workspaceId, memberUserId, request.Role, ct));

    /// <summary>Deletes a workspace. WorkspaceOwner only.</summary>
    [HttpDelete("{workspaceId:guid}")]
    public async Task<IActionResult> Delete(Guid workspaceId, CancellationToken ct)
    {
        await _workspaces.DeleteAsync(ControllerUser.Id(this), workspaceId, ct);
        return NoContent();
    }
}
