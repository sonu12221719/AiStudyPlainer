using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AiStudyPlanner.API.Interfaces;
using AiStudyPlanner.API.Models.DTOs;
using System.Security.Claims;

namespace AiStudyPlanner.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProgressController : ControllerBase
{
    private readonly IProgressService _progressService;

    public ProgressController(IProgressService progressService)
    {
        _progressService = progressService;
    }

    // ─── POST api/progress/quiz/submit ─────────────────────────────
    /// <summary>
    /// Submit quiz answers after studying a topic.
    /// Triggers scoring, weak area detection and AI feedback.
    /// </summary>
    [HttpPost("quiz/submit")]
    [ProducesResponseType(typeof(QuizResultResponseDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto),      400)]
    [ProducesResponseType(typeof(ErrorResponseDto),      404)]
    public async Task<IActionResult> SubmitQuiz(
        [FromBody] QuizSubmitDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(BuildValidationError());

        if (dto.CorrectAnswers + dto.WrongAnswers
            + dto.SkippedQuestions != dto.TotalQuestions)
            return BadRequest(new ErrorResponseDto
            {
                Message =
                    "CorrectAnswers + WrongAnswers + " +
                    "SkippedQuestions must equal TotalQuestions."
            });

        if (dto.TimeTakenSeconds <= 0)
            return BadRequest(new ErrorResponseDto
            {
                Message = "TimeTakenSeconds must be greater than zero."
            });

        var userId = GetUserId();
        var result = await _progressService
            .SubmitQuizAsync(userId, dto);

        return Ok(result);
    }

    // ─── GET api/progress/summary/{planId} ─────────────────────────
    /// <summary>
    /// Get overall progress summary for a plan.
    /// Powers the main dashboard — completion %, streak, heatmap.
    /// </summary>
    [HttpGet("summary/{planId:int}")]
    [ProducesResponseType(typeof(ProgressSummaryDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto),   404)]
    public async Task<IActionResult> GetProgressSummary(int planId)
    {
        var userId  = GetUserId();
        var summary = await _progressService
            .GetProgressSummaryAsync(userId, planId);

        return Ok(summary);
    }

    // ─── GET api/progress/weak-areas ───────────────────────────────
    /// <summary>
    /// Get all active weak areas for the student.
    /// Powers the Weak Areas panel on the dashboard.
    /// </summary>
    [HttpGet("weak-areas")]
    [ProducesResponseType(typeof(List<WeakAreaDto>), 200)]
    public async Task<IActionResult> GetActiveWeakAreas()
    {
        var userId = GetUserId();
        var areas  = await _progressService
            .GetActiveWeakAreasAsync(userId);

        return Ok(areas);
    }

    // ─── GET api/progress/weak-areas/all ───────────────────────────
    /// <summary>
    /// Get all weak areas including resolved ones.
    /// Powers the "Topics you've mastered" history view.
    /// </summary>
    [HttpGet("weak-areas/all")]
    [ProducesResponseType(typeof(List<WeakAreaDto>), 200)]
    public async Task<IActionResult> GetAllWeakAreas()
    {
        var userId = GetUserId();
        var areas  = await _progressService
            .GetAllWeakAreasAsync(userId);

        return Ok(areas);
    }

    // ─── GET api/progress/weak-areas/{weakAreaId} ──────────────────
    /// <summary>
    /// Get a single weak area by ID.
    /// Used when student clicks a weak area card for full detail.
    /// </summary>
    [HttpGet("weak-areas/{weakAreaId:int}")]
    [ProducesResponseType(typeof(WeakAreaDto),      200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 404)]
    public async Task<IActionResult> GetWeakArea(int weakAreaId)
    {
        var area = await _progressService
            .GetWeakAreaAsync(weakAreaId);

        return Ok(area);
    }

    // ─── PUT api/progress/weak-areas/{weakAreaId}/resolve ──────────
    /// <summary>
    /// Manually dismiss a weak area.
    /// Student can mark it as ignored if they feel confident.
    /// </summary>
    [HttpPut("weak-areas/{weakAreaId:int}/resolve")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 404)]
    public async Task<IActionResult> ResolveWeakArea(int weakAreaId)
    {
        var userId = GetUserId();
        await _progressService.ResolveWeakAreaAsync(userId, weakAreaId);

        return Ok(new { message = "Weak area dismissed successfully." });
    }

    // ─── GET api/progress/heatmap/{planId} ─────────────────────────
    /// <summary>
    /// Get subject-wise score breakdown for heatmap rendering.
    /// Returns intensity 1-5 per subject for Angular heatmap cells.
    /// </summary>
    [HttpGet("heatmap/{planId:int}")]
    [ProducesResponseType(typeof(List<SubjectScoreDto>), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto),      404)]
    public async Task<IActionResult> GetHeatmapData(int planId)
    {
        var userId = GetUserId();
        var data   = await _progressService
            .GetHeatmapDataAsync(userId, planId);

        return Ok(data);
    }

    // ─── GET api/progress/quiz-history/{topicId} ───────────────────
    /// <summary>
    /// Get all quiz attempts for a specific topic.
    /// Powers the "attempt history" chart on the topic detail page.
    /// </summary>
    [HttpGet("quiz-history/{topicId:int}")]
    [ProducesResponseType(typeof(List<QuizHistoryItemDto>), 200)]
    public async Task<IActionResult> GetQuizHistory(int topicId)
    {
        var userId  = GetUserId();
        var history = await _progressService.GetQuizHistoryAsync(userId, topicId);

        return Ok(history);
    }

    // ─── GET api/progress/streak ───────────────────────────────────
    /// <summary>
    /// Get the student's current study streak in days.
    /// Shown on the dashboard as a motivational counter.
    /// </summary>
    [HttpGet("streak")]
    [ProducesResponseType(typeof(StreakDto), 200)]
    public async Task<IActionResult> GetStreak()
    {
        var userId = GetUserId();
        var streak = await _progressService.GetStreakAsync(userId);

        return Ok(new StreakDto
        {
            CurrentStreak  = streak,
            Message        = streak switch
            {
                0 => "Study today to start your streak!",
                1 => "1 day streak — keep going!",
                _ => $"{streak} day streak — great work!"
            }
        });
    }

    // ─── GET api/progress/stats/{planId} ───────────────────────────
    /// <summary>
    /// Get detailed stats breakdown for a plan.
    /// Powers the stats cards on the dashboard.
    /// </summary>
    [HttpGet("stats/{planId:int}")]
    [ProducesResponseType(typeof(PlanStatsDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 404)]
    public async Task<IActionResult> GetPlanStats(int planId)
    {
        var userId = GetUserId();
        var stats  = await _progressService
            .GetPlanStatsAsync(userId, planId);

        return Ok(stats);
    }

    // ─── GET api/progress/today/{planId} ───────────────────────────
    /// <summary>
    /// Get today's progress snapshot — topics done vs remaining.
    /// Shown at the top of the dashboard as a daily summary card.
    /// </summary>
    [HttpGet("today/{planId:int}")]
    [ProducesResponseType(typeof(TodayProgressDto), 200)]
    public async Task<IActionResult> GetTodayProgress(int planId)
    {
        var userId  = GetUserId();
        var progress = await _progressService
            .GetTodayProgressAsync(userId, planId);

        return Ok(progress);
    }

    // ─── Private helpers ───────────────────────────────────────────
    private int GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException(
                "User ID not found in token.");
        return int.Parse(claim);
    }

    private ErrorResponseDto BuildValidationError()
    {
        var errors = ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .ToList();

        return new ErrorResponseDto
        {
            Message = "Validation failed.",
            Errors  = errors
        };
    }
}

// ─── Controller-specific DTOs ──────────────────────────────────────
public class StreakDto
{
    public int    CurrentStreak { get; set; }
    public string Message       { get; set; } = string.Empty;
}

public class QuizHistoryItemDto
{
    public int      AttemptNumber    { get; set; }
    public double   ScorePercent     { get; set; }
    public int      CorrectAnswers   { get; set; }
    public int      TotalQuestions   { get; set; }
    public bool     IsPassed         { get; set; }
    public int      TimeTakenSeconds { get; set; }
    public string   QuizType         { get; set; } = string.Empty;
    public string?  AiFeedback       { get; set; }
    public DateTime TakenAt          { get; set; }
}

public class PlanStatsDto
{
    public int    TotalTopics          { get; set; }
    public int    CompletedTopics      { get; set; }
    public int    PendingTopics        { get; set; }
    public int    WeakTopics           { get; set; }
    public int    CriticalTopics       { get; set; }
    public double OverallAverageScore  { get; set; }
    public double CompletionPercent    { get; set; }
    public int    TotalMinutesStudied  { get; set; }
    public int    TotalQuizzesTaken    { get; set; }
    public int    TotalQuizzesPassed   { get; set; }
    public double QuizPassRate         { get; set; }
    public int    ActiveWeakAreas      { get; set; }
    public int    ResolvedWeakAreas    { get; set; }
    public int    DaysRemaining        { get; set; }
    public bool   IsOnTrack            { get; set; }
}

public class TodayProgressDto
{
    public int              TotalTopicsToday     { get; set; }
    public int              CompletedToday       { get; set; }
    public int              RemainingToday       { get; set; }
    public double           TodayCompletionPct   { get; set; }
    public int              MinutesStudiedToday  { get; set; }
    public int              MinutesRemainingToday { get; set; }
    public bool             IsTodayComplete      { get; set; }
    public List<TopicSummaryDto> TodaysTopics    { get; set; } = new();
}