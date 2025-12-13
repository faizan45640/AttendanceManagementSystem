using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AMS.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// Semantic Kernel (LLM + tools)
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

// Google Gemini connector (via Semantic Kernel)
using Microsoft.SemanticKernel.Connectors.Google;

namespace AMS.Services;

public interface IAttendanceHybridService
{
    Task<AttendanceHybridChatResponse> ChatAsync(ClaimsPrincipal user, AttendanceHybridChatRequest request, CancellationToken cancellationToken);
}

public sealed record AttendanceHybridChatRequest(
    string Message,
    bool Confirmed,
    IReadOnlyList<ChatMessageDto>? History = null
);

public sealed record ChatMessageDto(string Role, string Content);

public sealed record AttendanceHybridChatResponse(
    bool Success,
    string Message,
    string? AssistantMessage = null,
    bool RequiresConfirmation = false,
    string? ConfirmationPrompt = null,
    string? AuditDecision = null,
    string? ProposedSql = null,
    IReadOnlyList<IDictionary<string, object?>>? RowsPreview = null
);

public sealed class AttendanceHybridService : IAttendanceHybridService
{
    private const int DefaultRowLimit = 200;

    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AttendanceHybridService> _logger;

    public AttendanceHybridService(ApplicationDbContext db, IConfiguration configuration, ILogger<AttendanceHybridService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    // Requirement #1: Nested plugin class with KernelFunction method.
    public sealed class TeacherActionsPlugin
    {
        private readonly ILogger _logger;

        public TeacherActionsPlugin(ILogger logger)
        {
            _logger = logger;
        }

        [KernelFunction]
        public Task<string> MarkAttendance(int studentId, string status)
        {
            // Mock DB save logic (pilot): validate inputs and pretend the save succeeded.
            var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
            if (studentId <= 0)
            {
                return Task.FromResult("Invalid studentId.");
            }

            if (normalized is not ("present" or "absent" or "late"))
            {
                return Task.FromResult("Invalid status. Use Present, Absent, or Late.");
            }

            _logger.LogInformation("[AI Pilot] MarkAttendance called for StudentId={StudentId}, Status={Status}", studentId, normalized);
            return Task.FromResult($"(Pilot) Attendance marked: StudentId={studentId}, Status={normalized}.");
        }
    }

    public async Task<AttendanceHybridChatResponse> ChatAsync(ClaimsPrincipal user, AttendanceHybridChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return new AttendanceHybridChatResponse(false, "Message is required.");
        }

        var actor = await ResolveActorAsync(user, cancellationToken);
        if (!actor.IsAuthenticated)
        {
            return new AttendanceHybridChatResponse(false, "Not authenticated.");
        }

        // Strategy selection (explicit classification is acceptable per requirements).
        var intent = ClassifyIntent(request.Message);

        if (intent == HybridIntent.Write)
        {
            if (!actor.IsTeacher)
            {
                return new AttendanceHybridChatResponse(false, "Only teachers can mark attendance via the pilot agent.");
            }

            if (!request.Confirmed)
            {
                return new AttendanceHybridChatResponse(
                    true,
                    "Confirmation required.",
                    AssistantMessage: "I can help mark attendance, but I need your confirmation first.",
                    RequiresConfirmation: true,
                    ConfirmationPrompt: "Confirm to proceed with the attendance change (pilot mode)."
                );
            }

            return await RunManagerAgentForWriteAsync(actor, request, cancellationToken);
        }

        // Read/query path
        return await RunSqlWriterAuditorFlowAsync(actor, request, cancellationToken);
    }

    private async Task<AttendanceHybridChatResponse> RunManagerAgentForWriteAsync(ActorContext actor, AttendanceHybridChatRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var kernel = CreateKernel(includeWritePlugin: true);
            kernel.Plugins.AddFromObject(new TeacherActionsPlugin(_logger), nameof(TeacherActionsPlugin));

            var chat = kernel.GetRequiredService<IChatCompletionService>();

            var history = BuildChatHistory(request.History);
            history.AddSystemMessage($"You are the Attendance Manager Agent for an academic management system.\n" +
                                     $"Rules:\n" +
                                     $"- You are operating for role: {(actor.IsTeacher ? "Teacher" : actor.IsStudent ? "Student" : actor.IsAdmin ? "Admin" : "User")}\n" +
                                     $"- You ONLY help with attendance-related tasks.\n" +
                                     $"- For write operations (marking attendance), call the tool TeacherActionsPlugin.MarkAttendance(studentId, status).\n" +
                                     $"- If the user request is missing studentId or status, ask a short clarifying question.\n" +
                                     $"- Do not output SQL in write mode.\n");

            history.AddUserMessage(request.Message);

            // Requirement #3: Auto-invoke kernel functions (SK API varies by version).
            // Newer Semantic Kernel uses FunctionChoiceBehavior.
            var settings = new PromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            var assistant = await chat.GetChatMessageContentAsync(history, settings, kernel, cancellationToken);

            return new AttendanceHybridChatResponse(true, "OK", AssistantMessage: assistant.Content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI write flow failed.");
            return new AttendanceHybridChatResponse(false, "AI write flow failed.");
        }
    }

    private async Task<AttendanceHybridChatResponse> RunSqlWriterAuditorFlowAsync(ActorContext actor, AttendanceHybridChatRequest request, CancellationToken cancellationToken)
    {
        try
        {
            // Agent 1: SQL Writer (SELECT-only)
            var sql = await GenerateSelectSqlAsync(actor, request, cancellationToken);
            if (string.IsNullOrWhiteSpace(sql))
            {
                return new AttendanceHybridChatResponse(false, "SQL generation failed.");
            }

            // Agent 2: Auditor (hard rules + optional LLM review)
            var audit = await AuditSqlAsync(actor, sql, cancellationToken);
            if (!audit.Approved)
            {
                return new AttendanceHybridChatResponse(
                    true,
                    "Blocked by SQL safety policy.",
                    AssistantMessage: audit.UserFacingMessage,
                    AuditDecision: audit.Decision,
                    ProposedSql: audit.ProposedSql
                );
            }

            // Execute (read-only)
            var rows = await ExecuteReadOnlySqlAsync(audit.ProposedSql!, audit.Parameters, cancellationToken);

            // Summarize results (optional, but keeps UX usable)
            var summary = await SummarizeResultsAsync(actor, request, audit.ProposedSql!, rows, cancellationToken);

            return new AttendanceHybridChatResponse(
                true,
                "OK",
                AssistantMessage: summary,
                AuditDecision: audit.Decision,
                ProposedSql: audit.ProposedSql,
                RowsPreview: rows.Take(20).ToList()
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI read flow failed.");
            return new AttendanceHybridChatResponse(false, "AI read flow failed.");
        }
    }

    // Requirement #2: Define SqlWriterAgent instructions.
    private async Task<string> GenerateSelectSqlAsync(ActorContext actor, AttendanceHybridChatRequest request, CancellationToken cancellationToken)
    {
        var kernel = CreateKernel(includeWritePlugin: false);
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        var schema = GetAttendanceSchemaSummary();
        var requiredFilters = GetRequiredSqlFilters(actor);

        var history = BuildChatHistory(request.History);
        history.AddSystemMessage(
            "You are SqlWriterAgent. You MUST output ONLY a single SQL SELECT statement for SQL Server.\n" +
            "Rules:\n" +
            "- Output ONLY SQL text. No markdown, no explanations.\n" +
            "- Must be read-only: SELECT (optionally WITH CTE).\n" +
            $"- Always include TOP ({DefaultRowLimit}).\n" +
            "- Use parameters like @studentId, @teacherId, @dateFrom, @dateTo when needed.\n" +
            "- Only use attendance-domain tables listed below.\n" +
            $"- MUST satisfy these mandatory filters: {requiredFilters}\n" +
            "Allowed schema:\n" +
            schema
        );

        history.AddUserMessage(request.Message);

        var settings = new PromptExecutionSettings();
        var assistant = await chat.GetChatMessageContentAsync(history, settings, kernel, cancellationToken);
        return ExtractSql(assistant.Content);
    }

    // Requirement #2: Define AuditorAgent instructions.
    private async Task<SqlAuditResult> AuditSqlAsync(ActorContext actor, string sql, CancellationToken cancellationToken)
    {
        // Deterministic hard rules first.
        var hard = SqlSafetyAuditor.Evaluate(actor, sql, DefaultRowLimit);
        if (!hard.Approved)
        {
            // Per pilot requirement: decision should be machine-checkable (SAFE / NOT_SAFE).
            // Keep a user-friendly explanation, but do not require LLM to produce it.
            var explanation = "NOT_SAFE\n" + string.Join("\n", hard.Issues.Select(i => "- " + i));
            return hard with { UserFacingMessage = explanation };
        }

        return hard;
    }

    private async Task<string> ExplainAuditFailureWithLlmAsync(ActorContext actor, string sql, IReadOnlyList<string> issues, CancellationToken cancellationToken)
    {
        try
        {
            var kernel = CreateKernel(includeWritePlugin: false);
            var chat = kernel.GetRequiredService<IChatCompletionService>();

            var history = new ChatHistory();
            history.AddSystemMessage(
                "You are AuditorAgent. You review SQL for safety and access control.\n" +
                "Rules:\n" +
                "- Never approve UPDATE/DELETE/INSERT/MERGE/DROP/ALTER/CREATE/TRUNCATE/EXEC.\n" +
                "- Ensure role-based scoping: Students must be filtered by StudentId; Teachers by TeacherId.\n" +
                "- Write a short explanation to the end-user describing why the query was blocked and what to ask instead.\n" +
                "- Do not output SQL.\n"
            );

            var sb = new StringBuilder();
            sb.AppendLine("The generated SQL was blocked by safety rules.");
            sb.AppendLine("Issues:");
            foreach (var issue in issues) sb.AppendLine("- " + issue);
            sb.AppendLine();
            sb.AppendLine("SQL:");
            sb.AppendLine(sql);

            history.AddUserMessage(sb.ToString());
            var assistant = await chat.GetChatMessageContentAsync(history, new PromptExecutionSettings(), kernel, cancellationToken);
            return assistant.Content ?? "Blocked by safety policy.";
        }
        catch
        {
            return "Blocked by safety policy.";
        }
    }

    private async Task<string> SummarizeResultsAsync(ActorContext actor, AttendanceHybridChatRequest request, string sql, IReadOnlyList<IDictionary<string, object?>> rows, CancellationToken cancellationToken)
    {
        var kernel = CreateKernel(includeWritePlugin: false);
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        var preview = rows.Take(20)
            .Select(r => string.Join(", ", r.Select(kv => $"{kv.Key}={kv.Value}")))
            .ToList();

        var history = BuildChatHistory(request.History);
        history.AddSystemMessage(
            "You are a helpful attendance assistant. Summarize the query results in plain language.\n" +
            "Rules:\n" +
            "- Keep it short.\n" +
            "- If there are 0 rows, say so and suggest a follow-up filter.\n" +
            "- Do not output SQL.\n"
        );

        history.AddUserMessage(
            "User question: " + request.Message + "\n\n" +
            "SQL (for context only, do not repeat):\n" + sql + "\n\n" +
            "Rows preview:\n" + string.Join("\n", preview)
        );

        var assistant = await chat.GetChatMessageContentAsync(history, new PromptExecutionSettings(), kernel, cancellationToken);
        return assistant.Content ?? "OK";
    }

    private Kernel CreateKernel(bool includeWritePlugin)
    {
        var modelId = _configuration["Gemini:ModelId"];
        if (string.IsNullOrWhiteSpace(modelId))
        {
            modelId = "gemini-2.0-flash";
        }

        var apiKey = _configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Gemini API key is missing. Set Gemini:ApiKey in appsettings.json (or user-secrets/environment variables).");
        }

        var builder = Kernel.CreateBuilder();

        // Requirement #4: Google Gemini config via AddGoogleAIGeminiChatCompletion (placeholder API key)
        builder.AddGoogleAIGeminiChatCompletion(modelId: modelId, apiKey: apiKey);

        return builder.Build();
    }

    private static ChatHistory BuildChatHistory(IReadOnlyList<ChatMessageDto>? history)
    {
        var chat = new ChatHistory();

        if (history == null) return chat;

        foreach (var msg in history.TakeLast(20))
        {
            var role = (msg.Role ?? string.Empty).Trim().ToLowerInvariant();
            if (role == "assistant") chat.AddAssistantMessage(msg.Content ?? string.Empty);
            else if (role == "system") chat.AddSystemMessage(msg.Content ?? string.Empty);
            else chat.AddUserMessage(msg.Content ?? string.Empty);
        }

        return chat;
    }

    private static string ExtractSql(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // If model returns code fences, strip them.
        var cleaned = text.Trim();
        cleaned = Regex.Replace(cleaned, "^```(sql)?\\s*", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, "```$", string.Empty, RegexOptions.IgnoreCase);
        return cleaned.Trim();
    }

    private static HybridIntent ClassifyIntent(string message)
    {
        var m = message.ToLowerInvariant();

        // Write intent: explicit verbs + attendance status terms.
        if (m.Contains("mark") && m.Contains("attendance")) return HybridIntent.Write;
        if (m.Contains("mark") && (m.Contains("present") || m.Contains("absent") || m.Contains("late"))) return HybridIntent.Write;
        if (m.Contains("set") && m.Contains("attendance")) return HybridIntent.Write;

        // Otherwise read intent.
        return HybridIntent.Read;
    }

    private static string GetAttendanceSchemaSummary()
    {
        // Keep this intentionally short + allowlisted.
        return string.Join("\n", new[]
        {
            "- Sessions(SessionId, CourseAssignmentId, SessionDate, StartTime)",
            "- Attendance(AttendanceId, SessionId, StudentId, Status)",
            "- Students(StudentId, UserId, FirstName, LastName, RollNumber, BatchId)",
            "- Teachers(TeacherId, UserId, FirstName, LastName)",
            "- CourseAssignments(AssignmentId, CourseId, TeacherId, SemesterId, BatchId)",
            "- Courses(CourseId, CourseName, CourseCode, CreditHours)",
            "- Semesters(SemesterId, SemesterName, StartDate, EndDate)",
            "- Enrollments(EnrollmentId, StudentId, CourseId, SemesterId, BatchId, Status)",
        });
    }

    private static string GetRequiredSqlFilters(ActorContext actor)
    {
        if (actor.IsStudent && actor.StudentId.HasValue)
        {
            return "Include WHERE Attendance.StudentId = @studentId (or Students.StudentId = @studentId).";
        }

        if (actor.IsTeacher && actor.TeacherId.HasValue)
        {
            return "Include WHERE CourseAssignments.TeacherId = @teacherId.";
        }

        return "Do not access outside attendance domain.";
    }

    private async Task<IReadOnlyList<IDictionary<string, object?>>> ExecuteReadOnlySqlAsync(string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection")
                               ?? _configuration["ConnectionStrings:DefaultConnection"]
                               ?? string.Empty;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("DefaultConnection is not configured.");
        }

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.Text;
        cmd.CommandText = sql;
        cmd.CommandTimeout = 30;

        foreach (var kv in parameters)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = kv.Key.StartsWith("@", StringComparison.Ordinal) ? kv.Key : "@" + kv.Key;
            p.Value = kv.Value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        var rows = new List<IDictionary<string, object?>>();
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }

        return rows;
    }

    private async Task<ActorContext> ResolveActorAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var userIdStr = user.FindFirstValue("UserId");
        int? userId = int.TryParse(userIdStr, out var parsed) ? parsed : null;

        var isTeacher = user.IsInRole("Teacher");
        var isStudent = user.IsInRole("Student");
        var isAdmin = user.IsInRole("Admin");

        int? teacherId = null;
        int? studentId = null;

        if (userId.HasValue)
        {
            if (isTeacher)
            {
                teacherId = await _db.Teachers
                    .Where(t => t.UserId == userId.Value)
                    .Select(t => (int?)t.TeacherId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (isStudent)
            {
                studentId = await _db.Students
                    .Where(s => s.UserId == userId.Value)
                    .Select(s => (int?)s.StudentId)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        return new ActorContext(user.Identity?.IsAuthenticated == true, userId, isTeacher, isStudent, isAdmin, teacherId, studentId);
    }

    private enum HybridIntent
    {
        Read,
        Write
    }

    private sealed record ActorContext(
        bool IsAuthenticated,
        int? UserId,
        bool IsTeacher,
        bool IsStudent,
        bool IsAdmin,
        int? TeacherId,
        int? StudentId
    );

    private sealed record SqlAuditResult(
        bool Approved,
        string Decision,
        string UserFacingMessage,
        string? ProposedSql,
        IReadOnlyDictionary<string, object?> Parameters,
        IReadOnlyList<string> Issues
    );

    private static class SqlSafetyAuditor
    {
        private static readonly Regex ForbiddenTokens = new("\\b(update|delete|insert|merge|drop|alter|create|truncate|exec|execute)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HasTop = new("\\btop\\s*(\\(|\\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] AllowedTables =
        [
            "sessions",
            "attendance",
            "students",
            "teachers",
            "courseassignments",
            "courses",
            "semesters",
            "enrollments"
        ];

        public static SqlAuditResult Evaluate(ActorContext actor, string sql, int requiredTop)
        {
            var issues = new List<string>();
            var trimmed = (sql ?? string.Empty).Trim();

            // Allow a single trailing semicolon (common in SQL) without treating it as multi-statement.
            // Block if there are multiple semicolons, or any semicolon not at the end.
            if (trimmed.Contains(';'))
            {
                var endTrimmed = trimmed.TrimEnd();
                var first = endTrimmed.IndexOf(';');
                var last = endTrimmed.LastIndexOf(';');

                var semicolonAtEndOnly = (first == last) && endTrimmed.EndsWith(";", StringComparison.Ordinal);
                if (semicolonAtEndOnly)
                {
                    endTrimmed = endTrimmed[..^1].TrimEnd();
                    trimmed = endTrimmed;
                }
                else
                {
                    issues.Add("Multiple statements are not allowed (unexpected ';').");
                }
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                issues.Add("Empty SQL.");
                return Deny(trimmed, issues);
            }

            if (ForbiddenTokens.IsMatch(trimmed))
            {
                issues.Add("Non-read-only SQL is not allowed (UPDATE/DELETE/INSERT/etc). Only SELECT.");
            }

            var startsOk = trimmed.StartsWith("select", StringComparison.OrdinalIgnoreCase)
                           || trimmed.StartsWith("with", StringComparison.OrdinalIgnoreCase);
            if (!startsOk)
            {
                issues.Add("SQL must start with SELECT (or WITH ... SELECT). ");
            }

            if (!HasTop.IsMatch(trimmed))
            {
                issues.Add($"SQL must include TOP ({requiredTop}).");
            }

            // Allowlist tables by basic token scan.
            var lowered = trimmed.ToLowerInvariant();
            var tableMentions = ExtractTableLikeTokens(lowered);
            foreach (var table in tableMentions)
            {
                if (!AllowedTables.Contains(table))
                {
                    issues.Add($"Table '{table}' is not allowed in the pilot.");
                }
            }

            var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (actor.IsStudent)
            {
                if (!actor.StudentId.HasValue)
                {
                    issues.Add("Could not resolve StudentId for this user.");
                }
                else
                {
                    // Must scope by StudentId.
                    if (!lowered.Contains("@studentid"))
                    {
                        issues.Add("Student queries must include @studentId filtering.");
                    }
                    parameters["@studentId"] = actor.StudentId.Value;
                }
            }

            if (actor.IsTeacher)
            {
                if (!actor.TeacherId.HasValue)
                {
                    issues.Add("Could not resolve TeacherId for this user.");
                }
                else
                {
                    if (!lowered.Contains("@teacherid"))
                    {
                        issues.Add("Teacher queries must include @teacherId filtering via CourseAssignments.TeacherId.");
                    }
                    parameters["@teacherId"] = actor.TeacherId.Value;
                }
            }

            if (issues.Count > 0)
            {
                return Deny(trimmed, issues);
            }

            return new SqlAuditResult(
                Approved: true,
                Decision: "SAFE",
                UserFacingMessage: "Approved.",
                ProposedSql: trimmed,
                Parameters: parameters,
                Issues: Array.Empty<string>()
            );
        }

        private static SqlAuditResult Deny(string sql, List<string> issues)
        {
            return new SqlAuditResult(
                Approved: false,
                Decision: "NOT_SAFE",
                UserFacingMessage: "Blocked by SQL safety policy.",
                ProposedSql: sql,
                Parameters: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
                Issues: issues
            );
        }

        private static HashSet<string> ExtractTableLikeTokens(string loweredSql)
        {
            // Naive extraction: look for FROM/JOIN <token>
            // This is intentionally strict and may reject complex queries; safe pilot tradeoff.
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tokens = Regex.Split(loweredSql, "\\s+");
            for (var i = 0; i < tokens.Length - 1; i++)
            {
                var t = tokens[i];
                if (t is "from" or "join")
                {
                    var next = tokens[i + 1]
                        .Trim()
                        .Trim('[', ']')
                        .Trim();

                    // Remove schema prefix.
                    if (next.Contains('.')) next = next.Split('.').Last();

                    // Remove aliases punctuation
                    next = Regex.Replace(next, "[^a-z0-9_]", string.Empty);
                    if (!string.IsNullOrWhiteSpace(next)) set.Add(next);
                }
            }
            return set;
        }
    }
}
