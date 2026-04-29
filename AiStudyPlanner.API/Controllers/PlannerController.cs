using AiStudyPlanner.API.Controllers;
using AiStudyPlanner.API.Interfaces;
using AiStudyPlanner.API.Models.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace StudyPlanner.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PlannerController : ControllerBase
{
    private readonly IPlannerService  _plannerService;
    private readonly IVectorService   _vectorService;
    private readonly IFileParserService _fileParser;

    public PlannerController(
        IPlannerService     plannerService,
        IVectorService      vectorService,
        IFileParserService  fileParser)
    {
        _plannerService = plannerService;
        _vectorService  = vectorService;
        _fileParser     = fileParser;
    }

    // ─── POST api/planner/upload ───────────────────────────────────
    /// <summary>
    /// Upload a syllabus file → extract text → generate AI plan
    /// → index into Qdrant in background
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB max
    [ProducesResponseType(typeof(StudyPlanResponseDto), 201)]
    [ProducesResponseType(typeof(ErrorResponseDto),     400)]
    [ProducesResponseType(typeof(ErrorResponseDto),     415)]
    public async Task<IActionResult> UploadSyllabus([FromForm] SyllabusUploadDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(BuildValidationError());

        // 1. Validate file
        var fileError = ValidateFile(dto.File);
        if (fileError is not null)
            return fileError;

        var userId = GetUserId();

        // 2. Extract text from PDF or .txt
        var rawText = await _fileParser.ExtractTextAsync(dto.File);

        if (string.IsNullOrWhiteSpace(rawText))
            return BadRequest(new ErrorResponseDto
            {
                Message = "Could not extract any text from the uploaded file. Please check the file is not empty or scanned image."
            });

        // 3. Save plan shell + generate AI schedule via Gemini
        var plan = await _plannerService.GeneratePlanFromSyllabusAsync(userId, dto);

        // 4. Index syllabus into Qdrant in background
        //    Fire-and-forget — student gets their plan immediately,
        //    vector indexing completes in the background
        _ = Task.Run(async () =>
        {
            try
            {
                await _vectorService.IndexSyllabusAsync(
                    userId, plan.PlanId, rawText);
            }
            catch (Exception ex)
            {
                // Log silently — do not crash the main response
                Console.Error.WriteLine(
                    $"[VectorService] Indexing failed for " +
                    $"plan {plan.PlanId}: {ex.Message}");
            }
        });

        return CreatedAtAction(
            nameof(GetPlan),
            new { planId = plan.PlanId },
            plan);
    }

    // ─── POST api/planner/preset ───────────────────────────────────
    /// <summary>
    /// Pick a preset exam (JEE / NEET / UPSC)
    /// → generate AI plan → index preset syllabus into Qdrant
    /// </summary>
    [HttpPost("preset")]
    [ProducesResponseType(typeof(StudyPlanResponseDto), 201)]
    [ProducesResponseType(typeof(ErrorResponseDto),     400)]
    public async Task<IActionResult> CreateFromPreset(
        [FromBody] ExamPresetRequestDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(BuildValidationError());

        if (dto.StartDate >= dto.EndDate)
            return BadRequest(new ErrorResponseDto
            {
                Message = "End date must be after start date."
            });

        if ((dto.EndDate - dto.StartDate).TotalDays < 7)
            return BadRequest(new ErrorResponseDto
            {
                Message = "Plan must be at least 7 days long."
            });

        var userId = GetUserId();

        // 1. Generate plan from preset syllabus text
        var plan = await _plannerService.GeneratePlanFromPresetAsync(
            userId, dto);

        // 2. Get the preset syllabus text for indexing
        //    PlannerService stores it in StudyPlan.RawSyllabusText
        //    VectorService reads it directly — no need to pass it here
        _ = Task.Run(async () =>
        {
            try
            {
                await _vectorService.ReIndexSyllabusAsync(
                    userId, plan.PlanId);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[VectorService] Preset indexing failed for " +
                    $"plan {plan.PlanId}: {ex.Message}");
            }
        });

        return CreatedAtAction(
            nameof(GetPlan),
            new { planId = plan.PlanId },
            plan);
    }

    // ─── GET api/planner/plans ─────────────────────────────────────
    /// <summary>Get all active study plans for the logged-in student</summary>
    [HttpGet("plans")]
    [ProducesResponseType(typeof(List<StudyPlanResponseDto>), 200)]
    public async Task<IActionResult> GetAllPlans()
    {
        var userId = GetUserId();
        var plans  = await _plannerService.GetAllPlansAsync(userId);
        return Ok(plans);
    }

    // ─── GET api/planner/plans/{planId} ───────────────────────────
    /// <summary>Get a single study plan by ID</summary>
    [HttpGet("plans/{planId:int}")]
    [ProducesResponseType(typeof(StudyPlanResponseDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto),     404)]
    public async Task<IActionResult> GetPlan(int planId)
    {
        var userId = GetUserId();
        var plan   = await _plannerService.GetPlanAsync(userId, planId);
        return Ok(plan);
    }

    // ─── GET api/planner/plans/{planId}/today ─────────────────────
    /// <summary>Get today's schedule for a plan</summary>
    [HttpGet("plans/{planId:int}/today")]
    [ProducesResponseType(typeof(DailyScheduleDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 404)]
    public async Task<IActionResult> GetTodaysSchedule(int planId)
    {
        var userId   = GetUserId();
        var schedule = await _plannerService
            .GetTodaysScheduleAsync(userId, planId);
        return Ok(schedule);
    }

    // ─── GET api/planner/topics/{topicId} ─────────────────────────
    /// <summary>Get a single topic by ID</summary>
    [HttpGet("topics/{topicId:int}")]
    [ProducesResponseType(typeof(TopicSummaryDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 404)]
    public async Task<IActionResult> GetTopic(int topicId)
    {
        var topic = await _plannerService.GetTopicAsync(topicId);
        return Ok(topic);
    }

    // ─── PUT api/planner/topics/{topicId}/complete ────────────────
    /// <summary>Mark a topic as completed</summary>
    [HttpPut("topics/{topicId:int}/complete")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 400)]
    [ProducesResponseType(typeof(ErrorResponseDto), 404)]
    public async Task<IActionResult> MarkTopicComplete(
        int topicId, [FromBody] CompleteTopicDto dto)
    {
        if (dto.MinutesSpent <= 0)
            return BadRequest(new ErrorResponseDto
            {
                Message = "MinutesSpent must be greater than zero."
            });

        var userId = GetUserId();

        await _plannerService.MarkTopicCompleteAsync(
            userId, topicId, dto.MinutesSpent);

        return Ok(new { message = "Topic marked as complete." });
    }

    // ─── DELETE api/planner/plans/{planId} ────────────────────────
    /// <summary>
    /// Archive a plan and delete its Qdrant vector collection
    /// </summary>
    [HttpDelete("plans/{planId:int}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 404)]
    public async Task<IActionResult> DeletePlan(int planId)
    {
        var userId = GetUserId();

        // Delete SQL plan (cascades to topics, chunks)
        await _plannerService.DeletePlanAsync(userId, planId);

        // Delete Qdrant collection for this user
        await _vectorService.DeleteUserCollectionAsync(userId);

        return Ok(new { message = "Plan deleted successfully." });
    }

    // ─── POST api/planner/plans/{planId}/regenerate ───────────────
    /// <summary>
    /// Regenerate the AI plan from the same syllabus
    /// and re-index into Qdrant
    /// </summary>
    [HttpPost("plans/{planId:int}/regenerate")]
    [ProducesResponseType(typeof(StudyPlanResponseDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto),     404)]
    public async Task<IActionResult> RegeneratePlan(int planId)
    {
        var userId = GetUserId();

        // 1. Regenerate topics via Gemini
        var plan = await _plannerService.RegeneratePlanAsync(userId, planId);

        // 2. Re-index updated syllabus into Qdrant
        _ = Task.Run(async () =>
        {
            try
            {
                await _vectorService.ReIndexSyllabusAsync(userId, planId);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[VectorService] Re-index failed for " +
                    $"plan {planId}: {ex.Message}");
            }
        });

        return Ok(plan);
    }

    // ─── GET api/planner/plans/{planId}/index-status ──────────────
    /// <summary>
    /// Check how many chunks are indexed in Qdrant for this plan.
    /// Angular polls this after upload to show indexing progress.
    /// </summary>
    [HttpGet("plans/{planId:int}/index-status")]
    [ProducesResponseType(typeof(IndexStatusDto), 200)]
    public async Task<IActionResult> GetIndexStatus(int planId)
    {
        var userId       = GetUserId();
        var isReady      = await _vectorService.CollectionExistsAsync(userId);
        var indexedCount = await _vectorService
            .GetIndexedChunkCountAsync(userId, planId);

        return Ok(new IndexStatusDto
        {
            PlanId         = planId,
            IsIndexed      = isReady,
            IndexedChunks  = indexedCount,
            Message        = isReady
                ? $"Ready — {indexedCount} chunks indexed."
                : "Indexing in progress..."
        });
    }

    // ─── POST api/planner/plans/{planId}/retry-index ──────────────
    /// <summary>
    /// Retry indexing any chunks that failed during initial upload
    /// </summary>
    [HttpPost("plans/{planId:int}/retry-index")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 404)]
    public async Task<IActionResult> RetryIndex(int planId)
    {
        var userId = GetUserId();

        _ = Task.Run(async () =>
        {
            try
            {
                await _vectorService.RetryFailedChunksAsync(userId, planId);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[VectorService] Retry failed for " +
                    $"plan {planId}: {ex.Message}");
            }
        });

        return Ok(new
        {
            message = "Retry started in background."
        });
    }

    // ─── Private helpers ───────────────────────────────────────────
    private int GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)?? 
            throw new UnauthorizedAccessException("User ID not found in token.");
        return int.Parse(claim);
    }

    private IActionResult? ValidateFile(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new ErrorResponseDto
            {
                Message = "No file uploaded."
            });

        var allowedExtensions = new[] { ".pdf", ".txt", ".md" };
        var extension = Path.GetExtension(file.FileName).ToLower();

        if (!allowedExtensions.Contains(extension))
            return StatusCode(415, new ErrorResponseDto
            {
                Message = $"File type '{extension}' is not supported. " +
                          $"Please upload a .pdf or .txt file."
            });

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new ErrorResponseDto
            {
                Message = "File size exceeds 10 MB limit."
            });

        return null; // no error
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
public class CompleteTopicDto
{
    [System.ComponentModel.DataAnnotations.Range(1, 600)]
    public int MinutesSpent { get; set; }
}

public class IndexStatusDto
{
    public int    PlanId        { get; set; }
    public bool   IsIndexed     { get; set; }
    public int    IndexedChunks { get; set; }
    public string Message       { get; set; } = string.Empty;
}