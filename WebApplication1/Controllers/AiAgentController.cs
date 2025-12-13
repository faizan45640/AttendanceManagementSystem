using System.Security.Claims;
using AMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMS.Controllers;

[Authorize]
public sealed class AiAgentController : Controller
{
    private readonly IAttendanceHybridService _attendanceHybridService;

    public AiAgentController(IAttendanceHybridService attendanceHybridService)
    {
        _attendanceHybridService = attendanceHybridService;
    }

    [HttpGet]
    public IActionResult Chat()
    {
        ViewData["Title"] = "AI Agent Chat";
        return View();
    }

    public sealed record ChatRequest(string message, bool confirmed, ChatMessageDto[]? history);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send([FromBody] ChatRequest body, CancellationToken cancellationToken)
    {
        var req = new AttendanceHybridChatRequest(
            Message: body.message ?? string.Empty,
            Confirmed: body.confirmed,
            History: body.history
        );

        var result = await _attendanceHybridService.ChatAsync(User, req, cancellationToken);

        return Json(new
        {
            success = result.Success,
            message = result.Message,
            assistantMessage = result.AssistantMessage,
            requiresConfirmation = result.RequiresConfirmation,
            confirmationPrompt = result.ConfirmationPrompt,
            auditDecision = result.AuditDecision,
            proposedSql = result.ProposedSql,
            rowsPreview = result.RowsPreview
        });
    }
}
