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

    // Heuristic intent detection keywords (kept minimal + safe).
    private static readonly string[] DomainKeywords =
    [
        "attendance",
        "present",
        "absent",
        "late",
        "course",
        "courses",
        "enroll",
        "enrolled",
        "enrollment",
        "semester",
        "session",
        "classes",
        "class",
        "timetable",
        "teacher",
        "teachers",
        "student",
        "students",
        "batch",
        "roll",
        "report",
        "percentage",
        "%"
    ];

    private static readonly string[] SmallTalkKeywords =
    [
        "hi",
        "hello",
        "hey",
        "good morning",
        "good afternoon",
        "good evening",
        "how are you",
        "what's up",
        "whats up",
        "thanks",
        "thank you",
        "bye"
    ];

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

        if (intent == HybridIntent.SmallTalk)
        {
            return await RunSmallTalkAsync(actor, request, cancellationToken);
        }

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

    private async Task<AttendanceHybridChatResponse> RunSmallTalkAsync(ActorContext actor, AttendanceHybridChatRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var kernel = CreateKernel(includeWritePlugin: false);
            var chat = kernel.GetRequiredService<IChatCompletionService>();

            var roleContext = actor.IsStudent ? "You are chatting with a STUDENT."
                            : actor.IsTeacher ? "You are chatting with a TEACHER."
                            : actor.IsAdmin ? "You are chatting with an ADMIN."
                            : "You are chatting with a user.";

            var history = BuildChatHistory(request.History);
            history.AddSystemMessage(
                "You are a friendly attendance assistant inside an academic management system.\n" +
                $"{roleContext}\n" +
                "Rules:\n" +
                "- This message is NOT a database query. Do NOT generate or mention SQL.\n" +
                "- Reply naturally and briefly.\n" +
                "- If the user asks for attendance/courses/teachers later, you can help with that.\n"
            );
            history.AddUserMessage(request.Message);

            var assistant = await chat.GetChatMessageContentAsync(history, new PromptExecutionSettings(), kernel, cancellationToken);
            return new AttendanceHybridChatResponse(true, "OK", AssistantMessage: assistant.Content ?? "Hello! How can I help you today?");
        }
        catch (Exception ex)
        {
            if (IsRateLimitError(ex))
            {
                _logger.LogWarning("AI rate limit reached (429).");
                return new AttendanceHybridChatResponse(false, "Your free AI limit has been reached for today. Please try again tomorrow.");
            }

            _logger.LogError(ex, "AI small-talk flow failed.");
            return new AttendanceHybridChatResponse(true, "OK", AssistantMessage: "Hi! How can I help you with attendance or courses?");
        }
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
            if (IsRateLimitError(ex))
            {
                _logger.LogWarning("AI rate limit reached (429).");
                return new AttendanceHybridChatResponse(false, "Your free AI limit has been reached for today. Please try again tomorrow.");
            }

            _logger.LogError(ex, "AI write flow failed.");
            return new AttendanceHybridChatResponse(false, "I encountered an issue while processing the attendance update. Please try again.");
        }
    }

    private async Task<AttendanceHybridChatResponse> RunSqlWriterAuditorFlowAsync(ActorContext actor, AttendanceHybridChatRequest request, CancellationToken cancellationToken)
    {
        try
        {
            // Retry loop for self-correction
            int maxRetries = 2;
            string? lastError = null;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                // Agent 1: SQL Writer (SELECT-only)
                var sql = await GenerateSelectSqlAsync(actor, request, lastError, cancellationToken);
                if (string.IsNullOrWhiteSpace(sql))
                {
                    return new AttendanceHybridChatResponse(false, "SQL generation failed.");
                }

                // Agent 2: Auditor (hard rules + optional LLM review)
                var audit = await AuditSqlAsync(actor, sql, cancellationToken);
                if (audit.Approved)
                {
                    // Success path
                    var rows = await ExecuteReadOnlySqlAsync(audit.ProposedSql!, audit.Parameters, cancellationToken);
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

                // If blocked, capture error for next iteration
                lastError = $"The previous SQL was rejected by safety policy: {audit.UserFacingMessage}\nRejected SQL: {sql}";
                
                // If this was the last attempt, fail
                if (attempt == maxRetries)
                {
                    return new AttendanceHybridChatResponse(
                        true,
                        "Blocked by SQL safety policy.",
                        AssistantMessage: audit.UserFacingMessage,
                        AuditDecision: audit.Decision,
                        ProposedSql: audit.ProposedSql
                    );
                }
            }

            return new AttendanceHybridChatResponse(false, "AI read flow failed after retries.");
        }
        catch (Exception ex)
        {
            if (IsRateLimitError(ex))
            {
                _logger.LogWarning("AI rate limit reached (429).");
                return new AttendanceHybridChatResponse(false, "Your free AI limit has been reached for today. Please try again tomorrow.");
            }

            _logger.LogError(ex, "AI read flow failed.");
            return new AttendanceHybridChatResponse(false, "I encountered an issue while retrieving the information. Please try again.");
        }
    }

    private static bool IsRateLimitError(Exception ex)
    {
        // Check for HttpOperationException from Semantic Kernel
        if (ex is HttpOperationException httpEx && (int)httpEx.StatusCode == 429)
        {
            return true;
        }

        // Check for standard HttpRequestException
        if (ex is System.Net.Http.HttpRequestException reqEx && reqEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        // Fallback: check message string
        var msg = ex.Message.ToLowerInvariant();
        if (msg.Contains("429") || msg.Contains("too many requests") || msg.Contains("quota exceeded"))
        {
            return true;
        }

        if (ex.InnerException != null)
        {
            return IsRateLimitError(ex.InnerException);
        }

        return false;
    }

    // Requirement #2: Define SqlWriterAgent instructions.
    private async Task<string> GenerateSelectSqlAsync(ActorContext actor, AttendanceHybridChatRequest request, string? lastError, CancellationToken cancellationToken)
    {
        var kernel = CreateKernel(includeWritePlugin: false);
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        var schema = GetAttendanceSchemaSummary(actor);
        var requiredFilters = GetRequiredSqlFilters(actor);
        var fewShotExamples = GetFewShotExamples(actor);

        // Tell the LLM who is asking
        var roleLabel = actor.IsStudent ? "STUDENT"
                      : actor.IsTeacher ? "TEACHER"
                      : actor.IsAdmin ? "ADMIN"
                      : "USER";

        var history = BuildChatHistory(request.History);
        history.AddSystemMessage(
            "You are SqlWriterAgent. You MUST output ONLY a single SQL SELECT statement for SQL Server.\n" +
            $"The current user is a {roleLabel}.\n" +
            "Rules:\n" +
            "- Output ONLY SQL text. No markdown, no explanations.\n" +
            "- Must be read-only: SELECT (optionally WITH CTE).\n" +
            $"- Always include TOP ({DefaultRowLimit}).\n" +
            "- Use parameters like @studentId, @teacherId, @dateFrom, @dateTo when needed.\n" +
            "- Only use attendance-domain tables listed below.\n" +
            $"- MUST satisfy these mandatory filters: {requiredFilters}\n" +
            "Allowed schema:\n" +
            schema + "\n\n" +
            "EXAMPLES (Follow these patterns):\n" +
            fewShotExamples
        );

        history.AddUserMessage(request.Message);

        if (!string.IsNullOrEmpty(lastError))
        {
            history.AddUserMessage($"FIX ERROR: {lastError}\n\nGenerate corrected SQL now:");
        }

        var settings = new PromptExecutionSettings();
        var assistant = await chat.GetChatMessageContentAsync(history, settings, kernel, cancellationToken);
        return ExtractSql(assistant.Content);
    }

    private static string GetFewShotExamples(ActorContext actor)
    {
        var sb = new StringBuilder();

        if (actor.IsStudent)
        {
            sb.AppendLine("User: \"Show my attendance for Web Engineering\"");
            sb.AppendLine("SQL: SELECT TOP (200) a.Status, s.SessionDate, c.CourseName FROM Attendance a JOIN Sessions s ON s.SessionId = a.SessionId JOIN CourseAssignments ca ON ca.AssignmentId = s.CourseAssignmentId JOIN Courses c ON c.CourseId = ca.CourseId WHERE a.StudentId = @studentId AND c.CourseName LIKE '%Web Engineering%' ORDER BY s.SessionDate DESC");
            sb.AppendLine();
            sb.AppendLine("User: \"Who is teaching Data Structures?\"");
            sb.AppendLine("SQL: SELECT TOP (200) t.FirstName, t.LastName, c.CourseName FROM Enrollments e JOIN CourseAssignments ca ON ca.CourseId = e.CourseId AND ca.BatchId = e.BatchId AND ca.SemesterId = e.SemesterId JOIN Teachers t ON t.TeacherId = ca.TeacherId JOIN Courses c ON c.CourseId = ca.CourseId WHERE e.StudentId = @studentId AND c.CourseName LIKE '%Data Structures%'");
        }
        else if (actor.IsTeacher)
        {
            sb.AppendLine("User: \"List my courses\"");
            sb.AppendLine("SQL: SELECT TOP (200) c.CourseName, c.CourseCode FROM CourseAssignments ca JOIN Courses c ON c.CourseId = ca.CourseId WHERE ca.TeacherId = @teacherId AND ca.IsActive = 1");
            sb.AppendLine();
            sb.AppendLine("User: \"Show students in Web Engineering\"");
            sb.AppendLine("SQL: SELECT TOP (200) s.FirstName, s.LastName, s.RollNumber FROM CourseAssignments ca JOIN Enrollments e ON e.CourseId = ca.CourseId AND e.BatchId = ca.BatchId AND e.SemesterId = ca.SemesterId JOIN Students s ON s.StudentId = e.StudentId JOIN Courses c ON c.CourseId = ca.CourseId WHERE ca.TeacherId = @teacherId AND c.CourseName LIKE '%Web Engineering%'");
        }
        else
        {
            sb.AppendLine("User: \"Count total students\"");
            sb.AppendLine("SQL: SELECT TOP (1) COUNT(*) as TotalStudents FROM Students WHERE IsActive = 1");
            sb.AppendLine();
            sb.AppendLine("User: \"Show recent attendance\"");
            sb.AppendLine("SQL: SELECT TOP (20) a.Status, s.SessionDate, st.FirstName, st.LastName FROM Attendance a JOIN Sessions s ON s.SessionId = a.SessionId JOIN Students st ON st.StudentId = a.StudentId ORDER BY s.SessionDate DESC");
        }

        return sb.ToString();
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
            var explanation = "I cannot process this request because it violates safety policies (e.g., unauthorized access or disallowed operations). Please ask differently.";
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

        // Determine user role for personalized responses
        var roleContext = actor.IsStudent ? "You are chatting with a STUDENT. Use 'you/your' when referring to their data (your courses, your attendance, your teachers)."
                        : actor.IsTeacher ? "You are chatting with a TEACHER. Use 'you/your' when referring to their data (your courses, your students, your sessions)."
                        : actor.IsAdmin ? "You are chatting with an ADMIN who has full system access."
                        : "You are chatting with a user.";

        var history = BuildChatHistory(request.History);
        history.AddSystemMessage(
            "You are a helpful attendance assistant. Summarize the query results in plain language.\n" +
            "Rules:\n" +
            "- Keep it short.\n" +
            "- If there are 0 rows, say so and suggest a follow-up filter.\n" +
            "- Do not output SQL.\n" +
            $"- {roleContext}\n"
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

        // Small-talk intent: greetings/pleasantries with no attendance-domain terms.
        // Keeps UX responsive and avoids running SQL for casual chat.
        var hasDomain = DomainKeywords.Any(k => m.Contains(k));
        var hasSmallTalk = SmallTalkKeywords.Any(k => m.Contains(k));
        if (hasSmallTalk && !hasDomain)
        {
            return HybridIntent.SmallTalk;
        }

        // Write intent: explicit verbs + attendance status terms.
        if (m.Contains("mark") && m.Contains("attendance")) return HybridIntent.Write;
        if (m.Contains("mark") && (m.Contains("present") || m.Contains("absent") || m.Contains("late"))) return HybridIntent.Write;
        if (m.Contains("set") && m.Contains("attendance")) return HybridIntent.Write;

        // Otherwise read intent.
        return HybridIntent.Read;
    }

    private static string GetAttendanceSchemaSummary(ActorContext actor)
    {
        // IMPORTANT: This is a static schema prompt for the AI pilot.
        // It is intentionally limited to the tables allowed by SqlSafetyAuditor.AllowedTables.
        // Keeping it static avoids querying INFORMATION_SCHEMA on every LLM call.

        var sb = new StringBuilder();

        sb.AppendLine("=== AI PILOT ALLOWED SCHEMA (SQL Server) ===");
        sb.AppendLine("Use ONLY the tables/columns listed below. Do NOT invent table names.");
        sb.AppendLine("You may use WITH (CTEs) for calculations, but CTEs must be built ONLY from these real tables.");
        sb.AppendLine();

        // These columns are aligned to the user's DB schema dump.
        sb.AppendLine("TABLES:");
        sb.AppendLine("- Attendance(AttendanceId, MarkedBy, SessionId, Status, StudentId)");
        sb.AppendLine("- Sessions(CourseAssignmentId, CreatedBy, EndTime, SessionDate, SessionId, StartTime)");
        sb.AppendLine("- Courses(CourseCode, CourseId, CourseName, CreditHours, IsActive)");
        sb.AppendLine("- CourseAssignments(AssignmentId, BatchId, CourseId, IsActive, SemesterId, TeacherId)");
        sb.AppendLine("- Enrollments(BatchId, CourseId, EnrollmentId, SemesterId, Status, StudentId)");
        sb.AppendLine("- Semesters(EndDate, IsActive, SemesterId, SemesterName, StartDate, Year)");
        sb.AppendLine("- Students(BatchId, FirstName, IsActive, LastName, RollNumber, StudentId, UserId)");
        sb.AppendLine("- Teachers(FirstName, IsActive, LastName, TeacherId, UserId)");
        sb.AppendLine("- Timetables(BatchId, IsActive, SemesterId, TimetableId)");
        sb.AppendLine("- TimetableSlots(CourseAssignmentId, DayOfWeek, EndTime, SlotId, StartTime, TimetableId)");
        sb.AppendLine();

        if (actor.IsStudent)
        {
            sb.AppendLine("ROLE: STUDENT");
            sb.AppendLine("- You can query your own data only.");
            sb.AppendLine("- You may see teacher names ONLY for the courses you are enrolled in.");
            sb.AppendLine();
            sb.AppendLine("STUDENT JOIN PATTERNS (must include @studentId):");
            sb.AppendLine("- Your courses: Enrollments e WHERE e.StudentId=@studentId JOIN Courses c ON c.CourseId=e.CourseId");
            sb.AppendLine("- Your teachers: Enrollments e WHERE e.StudentId=@studentId");
            sb.AppendLine("  JOIN CourseAssignments ca ON ca.CourseId=e.CourseId AND ca.BatchId=e.BatchId AND ca.SemesterId=e.SemesterId");
            sb.AppendLine("  JOIN Teachers t ON t.TeacherId=ca.TeacherId");
            sb.AppendLine("- Your attendance per course: Attendance a WHERE a.StudentId=@studentId");
            sb.AppendLine("  JOIN Sessions s ON s.SessionId=a.SessionId");
            sb.AppendLine("  JOIN CourseAssignments ca ON ca.AssignmentId=s.CourseAssignmentId");
            sb.AppendLine("  JOIN Courses c ON c.CourseId=ca.CourseId");
            sb.AppendLine("- Your timetable: TimetableSlots ts");
            sb.AppendLine("  JOIN CourseAssignments ca ON ca.AssignmentId=ts.CourseAssignmentId");
            sb.AppendLine("  JOIN Enrollments e ON e.CourseId=ca.CourseId AND e.BatchId=ca.BatchId AND e.SemesterId=ca.SemesterId");
            sb.AppendLine("  WHERE e.StudentId=@studentId");
            sb.AppendLine();
            sb.AppendLine("COMMON CALCULATIONS:");
            sb.AppendLine("- Attendance % per course: SUM(CASE WHEN a.Status='Present' THEN 1 ELSE 0 END) * 100.0 / NULLIF(COUNT(*), 0)");
            sb.AppendLine("- Remaining sessions (future): COUNT(*) WHERE s.SessionDate > CAST(GETDATE() AS date)");
        }
        else if (actor.IsTeacher)
        {
            sb.AppendLine("ROLE: TEACHER");
            sb.AppendLine("- You can query ONLY your own courses/students/sessions.");
            sb.AppendLine("- Always scope through CourseAssignments.TeacherId=@teacherId.");
            sb.AppendLine();
            sb.AppendLine("TEACHER JOIN PATTERNS (must include @teacherId):");
            sb.AppendLine("- Your courses: CourseAssignments ca WHERE ca.TeacherId=@teacherId JOIN Courses c ON c.CourseId=ca.CourseId");
            sb.AppendLine("- Your students: CourseAssignments ca WHERE ca.TeacherId=@teacherId");
            sb.AppendLine("  JOIN Enrollments e ON e.CourseId=ca.CourseId AND e.BatchId=ca.BatchId AND e.SemesterId=ca.SemesterId");
            sb.AppendLine("  JOIN Students s ON s.StudentId=e.StudentId");
            sb.AppendLine("- Attendance for your sessions: Attendance a JOIN Sessions se ON se.SessionId=a.SessionId");
            sb.AppendLine("  JOIN CourseAssignments ca ON ca.AssignmentId=se.CourseAssignmentId WHERE ca.TeacherId=@teacherId");
            sb.AppendLine("- Your timetable: TimetableSlots ts");
            sb.AppendLine("  JOIN CourseAssignments ca ON ca.AssignmentId=ts.CourseAssignmentId");
            sb.AppendLine("  WHERE ca.TeacherId=@teacherId");
        }
        else
        {
            sb.AppendLine("ROLE: ADMIN");
            sb.AppendLine("- Full access within these pilot tables.");
        }

        return sb.ToString();
    }

    private static string GetRequiredSqlFilters(ActorContext actor)
    {
        if (actor.IsStudent && actor.StudentId.HasValue)
        {
            return "CRITICAL RULES FOR STUDENT:\n" +
                   "- You MUST include WHERE with StudentId = @studentId (via Enrollments.StudentId or Attendance.StudentId).\n" +
                   "- You CAN see teacher names for YOUR enrolled courses by joining: Enrollments -> CourseAssignments -> Teachers.\n" +
                   "- You CANNOT see other students' data - only your own attendance, timetable, courses.\n" +
                   "- Always scope through Enrollments with @studentId to ensure proper access control.";
        }

        if (actor.IsTeacher && actor.TeacherId.HasValue)
        {
            return "CRITICAL RULES FOR TEACHER:\n" +
                   "- You MUST include WHERE CourseAssignments.TeacherId = @teacherId in EVERY query.\n" +
                   "- For Students: JOIN through CourseAssignments WHERE TeacherId = @teacherId -> Enrollments -> Students.\n" +
                   "- For Attendance: JOIN Sessions -> CourseAssignments WHERE TeacherId = @teacherId.\n" +
                   "- For Teachers table: Only query WHERE Teachers.TeacherId = @teacherId (your own record).\n" +
                   "- You CANNOT see other teachers' courses, students, or attendance data.\n" +
                   "- NEVER query Teachers table without TeacherId = @teacherId filter.";
        }

        return "Admin: full access to attendance domain tables.";
    }

    private async Task<IReadOnlyList<IDictionary<string, object?>>> ExecuteReadOnlySqlAsync(string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        // Important: Use the same DB connection string EF Core is using.
        // This avoids mismatches between appsettings.json and ApplicationDbContext.OnConfiguring hardcoded values.
        var connectionString = _db.Database.GetDbConnection().ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = _configuration.GetConnectionString("DefaultConnection")
                               ?? _configuration["ConnectionStrings:DefaultConnection"]
                               ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("DefaultConnection is not configured.");
        }

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        // Preflight: if the query references tables that don't exist in the current database,
        // return a clearer error than "Invalid object name".
        await EnsureTablesExistAsync(conn, sql, cancellationToken);

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

    private static async Task EnsureTablesExistAsync(SqlConnection conn, string sql, CancellationToken cancellationToken)
    {
        var lowered = (sql ?? string.Empty).ToLowerInvariant();
        var mentioned = SqlSafetyAuditor.ExtractTableLikeTokens(lowered);
        if (mentioned.Count == 0) return;

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var check = conn.CreateCommand())
        {
            check.CommandType = CommandType.Text;
            check.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='dbo'";
            await using var reader = await check.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                existing.Add(name);
            }
        }

        var missing = mentioned.Where(t => !existing.Contains(t)).ToList();
        if (missing.Count == 0) return;

        string dbName;
        await using (var dbCmd = conn.CreateCommand())
        {
            dbCmd.CommandType = CommandType.Text;
            dbCmd.CommandText = "SELECT DB_NAME()";
            dbName = (string?)await dbCmd.ExecuteScalarAsync(cancellationToken) ?? "(unknown)";
        }

        throw new InvalidOperationException(
            $"Database '{dbName}' is missing required tables for this query: {string.Join(", ", missing)}. " +
            "Check that your ConnectionStrings:DefaultConnection points to the AMS database and that the Attendance tables exist."
        );
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
        ,
        SmallTalk
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
            "enrollments",
            "timetableslots",
            "timetables"
        ];

        // Tables that students are NOT allowed to query (empty for now - all access is scoped via @studentId)
        private static readonly string[] StudentBlockedTables = [];

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

            // Extract CTE names - these are not real tables, just query aliases
            var cteNames = ExtractCteNames(lowered);

            foreach (var table in tableMentions)
            {
                // Skip CTE names - they're not real tables
                if (cteNames.Contains(table))
                {
                    continue;
                }

                if (!AllowedTables.Contains(table))
                {
                    issues.Add($"Table '{table}' is not allowed in the pilot.");
                }
            }

            // Students are blocked from querying certain tables
            if (actor.IsStudent)
            {
                foreach (var blocked in StudentBlockedTables)
                {
                    if (tableMentions.Contains(blocked))
                    {
                        issues.Add($"Students are not allowed to query the '{blocked}' table.");
                    }
                }
                // Note: Students CAN see teacher names for their enrolled courses
                // The @studentId filter through Enrollments scopes access appropriately
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

                    // Extra check: if querying Teachers table directly, must filter by TeacherId
                    if (tableMentions.Contains("teachers"))
                    {
                        // Must have teacherid = @teacherid pattern (teachers can only see their own record)
                        var hasTeacherIdFilter = lowered.Contains("teacherid = @teacherid")
                                                 || lowered.Contains("teacherid=@teacherid")
                                                 || lowered.Contains("teachers.teacherid = @teacherid")
                                                 || lowered.Contains("t.teacherid = @teacherid");
                        if (!hasTeacherIdFilter)
                        {
                            issues.Add("When querying Teachers table, teachers must filter by TeacherId = @teacherId (can only see own record).");
                        }
                    }
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

        public static HashSet<string> ExtractTableLikeTokens(string loweredSql)
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

        /// <summary>
        /// Extract CTE names from a WITH clause so we don't flag them as unknown tables.
        /// Pattern: WITH CteName AS (...), AnotherCte AS (...)
        /// </summary>
        public static HashSet<string> ExtractCteNames(string loweredSql)
        {
            var ctes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Match CTE definitions: "with name as" or ", name as"
            var ctePattern = new Regex(@"(?:with|,)\s+([a-z_][a-z0-9_]*)\s+as\s*\(", RegexOptions.IgnoreCase);
            var matches = ctePattern.Matches(loweredSql);

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    ctes.Add(match.Groups[1].Value);
                }
            }

            return ctes;
        }
    }
}
