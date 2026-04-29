using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AiStudyPlanner.API.Interfaces;
using AiStudyPlanner.API.Models.DTOs;
using System.Security.Claims;
using StudyPlanner.API.Controllers;

namespace AiStudyPlanner.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TopicsController : ControllerBase
{
    private readonly IPlannerService  _planner;
    private readonly IProgressService _progress;
    private readonly IVectorService   _vector;
    private readonly ILogger<TopicsController> _logger;

    public TopicsController(
        IPlannerService           planner,
        IProgressService          progress,
        IVectorService            vector,
        ILogger<TopicsController> logger)
    {
        _planner  = planner;
        _progress = progress;
        _vector   = vector;
        _logger   = logger;
    }

    // ─── GET api/topics/{topicId} ──────────────────────────────────
    /// <summary>
    /// Get a single topic by ID with full detail.
    /// </summary>
    [HttpGet("{topicId:int}")]
    [ProducesResponseType(typeof(TopicDetailDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 404)]
    public async Task<IActionResult> GetTopic(int topicId)
    {
        var topic = await _planner.GetTopicAsync(topicId);
        return Ok(topic);
    }

    // ─── GET api/topics/plan/{planId} ──────────────────────────────
    /// <summary>
    /// Get all topics for a plan grouped by subject.
    /// Powers the topic list view in Angular.
    /// </summary>
    [HttpGet("plan/{planId:int}")]
    [ProducesResponseType(typeof(TopicsBySubjectDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 404)]
    public async Task<IActionResult> GetTopicsByPlan(int planId)
    {
        var userId = GetUserId();
        var topics = await _planner
            .GetAllTopicsAsync(userId, planId);

        // Group by subject for the Angular accordion view
        var grouped = topics
            .GroupBy(t => t.Subject)
            .OrderBy(g => g.Key)
            .Select(g => new SubjectGroupDto
            {
                Subject     = g.Key,
                TotalTopics = g.Count(),
                Completed   = g.Count(t =>
                    t.Status == "Completed"),
                Topics      = g.ToList()
            })
            .ToList();

        return Ok(new TopicsBySubjectDto
        {
            PlanId   = planId,
            Subjects = grouped
        });
    }

    // ─── GET api/topics/plan/{planId}/weak ─────────────────────────
    /// <summary>
    /// Get all weak area topics for a plan.
    /// Powers the weak areas filter on the topic list.
    /// </summary>
    [HttpGet("plan/{planId:int}/weak")]
    [ProducesResponseType(typeof(List<TopicSummaryDto>), 200)]
    public async Task<IActionResult> GetWeakTopics(int planId)
    {
        var userId = GetUserId();
        var topics = await _planner
            .GetWeakTopicsAsync(userId, planId);

        return Ok(topics);
    }

    // ─── GET api/topics/plan/{planId}/exam-critical ────────────────
    /// <summary>
    /// Get all exam-critical topics for a plan.
    /// Powers the "Important Topics" filter.
    /// </summary>
    [HttpGet("plan/{planId:int}/exam-critical")]
    [ProducesResponseType(typeof(List<TopicSummaryDto>), 200)]
    public async Task<IActionResult> GetExamCriticalTopics(
        int planId)
    {
        var userId = GetUserId();
        var topics = await _planner
            .GetExamCriticalTopicsAsync(userId, planId);

        return Ok(topics);
    }

    // ─── GET api/topics/plan/{planId}/subject/{subject} ────────────
    /// <summary>
    /// Get all topics for a specific subject within a plan.
    /// </summary>
    [HttpGet("plan/{planId:int}/subject/{subject}")]
    [ProducesResponseType(typeof(List<TopicSummaryDto>), 200)]
    public async Task<IActionResult> GetTopicsBySubject(
        int planId, string subject)
    {
        var userId = GetUserId();
        var topics = await _planner
            .GetTopicsBySubjectAsync(userId, planId, subject);

        return Ok(topics);
    }

    // ─── GET api/topics/plan/{planId}/day/{dayNumber} ──────────────
    /// <summary>
    /// Get all topics for a specific day in the plan.
    /// </summary>
    [HttpGet("plan/{planId:int}/day/{dayNumber:int}")]
    [ProducesResponseType(typeof(DailyScheduleDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 404)]
    public async Task<IActionResult> GetTopicsByDay(
        int planId, int dayNumber)
    {
        var userId   = GetUserId();
        var schedule = await _planner
            .GetDayScheduleAsync(userId, planId, dayNumber);

        return Ok(schedule);
    }

    // ─── PUT api/topics/{topicId}/status ───────────────────────────
    /// <summary>
    /// Update the status of a topic.
    /// e.g. Pending → InProgress → Completed / Skipped
    /// </summary>
    [HttpPut("{topicId:int}/status")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 400)]
    [ProducesResponseType(typeof(ErrorResponseDto), 404)]
    public async Task<IActionResult> UpdateTopicStatus(
        int topicId,
        [FromBody] UpdateTopicStatusDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(BuildValidationError());

        var userId = GetUserId();
        await _planner.UpdateTopicStatusAsync(
            userId, topicId, dto.Status);

        return Ok(new
        {
            message = $"Topic status updated to {dto.Status}."
        });
    }

    // ─── PUT api/topics/{topicId}/complete ─────────────────────────
    /// <summary>
    /// Mark a topic as complete with time spent.
    /// Updates plan completion percentage.
    /// </summary>
    [HttpPut("{topicId:int}/complete")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 400)]
    [ProducesResponseType(typeof(ErrorResponseDto), 404)]
    public async Task<IActionResult> CompleteTopic(
        int topicId,
        [FromBody] CompleteTopicDto dto)
    {
        if (dto.MinutesSpent <= 0)
            return BadRequest(new ErrorResponseDto
            {
                Message = "MinutesSpent must be greater than zero."
            });

        var userId = GetUserId();
        await _planner.MarkTopicCompleteAsync(
            userId, topicId, dto.MinutesSpent);

        return Ok(new
        {
            message = "Topic marked as complete."
        });
    }

    // ─── PUT api/topics/{topicId}/skip ─────────────────────────────
    /// <summary>
    /// Skip a topic — marks it as Skipped and
    /// optionally reschedules it to a future day.
    /// </summary>
    [HttpPut("{topicId:int}/skip")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 404)]
    public async Task<IActionResult> SkipTopic(int topicId)
    {
        var userId = GetUserId();
        await _planner.SkipTopicAsync(userId, topicId);

        return Ok(new
        {
            message = "Topic skipped and rescheduled."
        });
    }

    // ─── GET api/topics/{topicId}/quiz-history ─────────────────────
    /// <summary>
    /// Get all quiz attempts for a topic.
    /// Powers the score trend chart on the topic detail page.
    /// </summary>
    [HttpGet("{topicId:int}/quiz-history")]
    [ProducesResponseType(typeof(List<QuizHistoryItemDto>), 200)]
    public async Task<IActionResult> GetQuizHistory(int topicId)
    {
        var userId  = GetUserId();
        var history = await _progress
            .GetQuizHistoryAsync(userId, topicId);

        return Ok(history);
    }

    // ─── GET api/topics/{topicId}/weak-area ────────────────────────
    /// <summary>
    /// Get the active weak area record for a topic if one exists.
    /// Shown on the topic detail page as an AI insight card.
    /// </summary>
    [HttpGet("{topicId:int}/weak-area")]
    [ProducesResponseType(typeof(WeakAreaDto), 200)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> GetTopicWeakArea(int topicId)
    {
        var userId  = GetUserId();
        var weakArea = await _progress
            .GetWeakAreaByTopicAsync(userId, topicId);

        // 204 No Content — topic has no active weak area
        if (weakArea is null)
            return NoContent();

        return Ok(weakArea);
    }

    // ─── GET api/topics/{topicId}/similar-chunks ───────────────────
    /// <summary>
    /// Get the top Qdrant chunks most similar to this topic.
    /// Used in dev/debug mode to verify RAG is working correctly.
    /// </summary>
    [HttpGet("{topicId:int}/similar-chunks")]
    [ProducesResponseType(typeof(SimilarChunksDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 404)]
    public async Task<IActionResult> GetSimilarChunks(int topicId)
    {
        var userId = GetUserId();
        var topic  = await _planner.GetTopicAsync(topicId);

        if (topic is null)
            return NotFound(new ErrorResponseDto
            {
                Message = $"Topic {topicId} not found."
            });

        var vectorReady = await _vector.CollectionExistsAsync(userId);

        if (!vectorReady)
            return Ok(new SimilarChunksDto
            {
                TopicId    = topicId,
                TopicName  = topic.Name,
                IsIndexed  = false,
                Chunks     = new List<string>(),
                Message    = "Syllabus not yet indexed into Qdrant."
            });

        var chunks = await _vector.SearchByTopicAsync(
            userId,
            query:     topic.Name,
            topicName: topic.Name,
            topK:      5);

        return Ok(new SimilarChunksDto
        {
            TopicId   = topicId,
            TopicName = topic.Name,
            IsIndexed = true,
            Chunks    = chunks,
            Message   = $"{chunks.Count} chunks found."
        });
    }

    // ─── GET api/topics/search ─────────────────────────────────────
    /// <summary>
    /// Search topics by name within a plan.
    /// Powers the search bar on the topics list page.
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(List<TopicSummaryDto>), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 400)]
    public async Task<IActionResult> SearchTopics(
        [FromQuery] int    planId,
        [FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(new ErrorResponseDto
            {
                Message = "Search query cannot be empty."
            });

        if (query.Length < 2)
            return BadRequest(new ErrorResponseDto
            {
                Message = "Search query must be at least 2 characters."
            });

        var userId = GetUserId();
        var topics = await _planner
            .SearchTopicsAsync(userId, planId, query);

        return Ok(topics);
    }

    // ─── Private helpers ───────────────────────────────────────────
    private int GetUserId()
    {
        var claim = User.FindFirstValue(
            ClaimTypes.NameIdentifier)
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

public class UpdateTopicStatusDto
{
    [System.ComponentModel.DataAnnotations.Required]
    public string Status { get; set; } = string.Empty;
    // "Pending" | "InProgress" | "Completed" | "Skipped" | "Revised"
}

public class TopicDetailDto : TopicSummaryDto
{
    public int       DayNumber         { get; set; }
    public DateTime  ScheduledDate     { get; set; }
    public int       QuizAttempts      { get; set; }
    public double    ScorePercent      { get; set; }
    public bool      IsWeakArea        { get; set; }
    public bool      IsRevisionScheduled { get; set; }
    public int       ActualMinutesSpent { get; set; }
    public DateTime? CompletedAt       { get; set; }
    public string?   QdrantTopicTag    { get; set; }
}

public class TopicsBySubjectDto
{
    public int                    PlanId   { get; set; }
    public List<SubjectGroupDto>  Subjects { get; set; } = new();
}

public class SubjectGroupDto
{
    public string Subject { get; set; } = string.Empty;
    public int TotalTopics { get; set; }
    public int Completed { get; set; }
    public List<TopicSummaryDto> Topics { get; set; } = new();
}

public class SimilarChunksDto
{
    public int          TopicId   { get; set; }
    public string       TopicName { get; set; } = string.Empty;
    public bool         IsIndexed { get; set; }
    public List<string> Chunks    { get; set; } = new();
    public string       Message   { get; set; } = string.Empty;
}