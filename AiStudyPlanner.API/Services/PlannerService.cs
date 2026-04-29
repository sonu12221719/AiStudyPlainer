using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AiStudyPlanner.API.Data;
using AiStudyPlanner.API.Interfaces;
using AiStudyPlanner.API.Models.DTOs;
using AiStudyPlanner.API.Models.Entities;
using UglyToad.PdfPig;

namespace AiStudyPlanner.API.Services;

public class PlannerService : IPlannerService
{
    private readonly AppDbContext   _db;
    private readonly IGeminiService _gemini;
    private readonly IVectorService _vector;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PlannerService(
        AppDbContext   db,
        IGeminiService gemini,
        IVectorService vector)
    {
        _db     = db;
        _gemini = gemini;
        _vector = vector;
    }

    // ══════════════════════════════════════════════════════════════
    // GENERATE PLAN FROM SYLLABUS UPLOAD
    // ══════════════════════════════════════════════════════════════
    public async Task<StudyPlanResponseDto> GeneratePlanFromSyllabusAsync(int userId, SyllabusUploadDto dto)
    {
        // 1. Extract raw text from uploaded file
        var rawText  = await ExtractTextFromFileAsync(dto.File);
        var totalDays = (int)(dto.EndDate - dto.StartDate).TotalDays + 1;

        // 2. Ask Gemini to generate the study schedule
        var planJson = await _gemini.GenerateStudyPlanAsync(
            syllabusText: rawText,
            examTarget:   "Custom",
            totalDays:    totalDays,
            dailyHours:   dto.DailyStudyHours);

        // 3. Save plan + topics to SQL
        var planId = await PersistPlanAsync(
            userId:    userId,
            rawText:   rawText,
            fileName:  dto.File.FileName,
            title:     dto.PlanTitle?? $"My Plan — {DateTime.UtcNow:dd MMM yyyy}",
            examTarget: "Custom",
            source:    PlanSource.SyllabusUpload,
            startDate: dto.StartDate,
            endDate:   dto.EndDate,
            planJson:  planJson);

        return await BuildPlanResponseAsync(planId);
    }

    // ══════════════════════════════════════════════════════════════
    // GENERATE PLAN FROM EXAM PRESET
    // ══════════════════════════════════════════════════════════════
    public async Task<StudyPlanResponseDto> GeneratePlanFromPresetAsync(int userId, ExamPresetRequestDto dto)
    {
        var presetText = GetPresetSyllabus(dto.ExamName);
        var totalDays  = (int)(dto.EndDate - dto.StartDate).TotalDays + 1;

        var planJson = await _gemini.GenerateStudyPlanAsync(
            syllabusText: presetText,
            examTarget:   dto.ExamName,
            totalDays:    totalDays,
            dailyHours:   dto.DailyStudyHours);

        var planId = await PersistPlanAsync(
            userId:     userId,
            rawText:    presetText,
            fileName:   null,
            title:      $"{dto.ExamName} — {totalDays} Day Plan",
            examTarget: dto.ExamName,
            source:     PlanSource.ExamPreset,
            startDate:  dto.StartDate,
            endDate:    dto.EndDate,
            planJson:   planJson);

        return await BuildPlanResponseAsync(planId);
    }

    // ══════════════════════════════════════════════════════════════
    // GET PLAN
    // ══════════════════════════════════════════════════════════════
    public async Task<StudyPlanResponseDto> GetPlanAsync(
        int userId, int planId)
    {
        var exists = await _db.StudyPlans.AnyAsync(p => p.Id == planId && p.UserId == userId && p.Status != PlanStatus.Archived);

        if (!exists)
            throw new KeyNotFoundException(
                $"Plan {planId} not found for user {userId}.");

        return await BuildPlanResponseAsync(planId);
    }

    // ══════════════════════════════════════════════════════════════
    // GET ALL PLANS
    // ══════════════════════════════════════════════════════════════
    public async Task<List<StudyPlanResponseDto>> GetAllPlansAsync(int userId)
    {
        var planIds = await _db.StudyPlans
            .Where(p => p.UserId == userId
                     && p.Status == PlanStatus.Active)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => p.Id)
            .ToListAsync();

        var result = new List<StudyPlanResponseDto>();

        foreach (var id in planIds)
            result.Add(await BuildPlanResponseAsync(id));

        return result;
    }

    // ══════════════════════════════════════════════════════════════
    // GET TODAY'S SCHEDULE
    // ══════════════════════════════════════════════════════════════
    public async Task<DailyScheduleDto> GetTodaysScheduleAsync(
        int userId, int planId)
    {
        var today = DateTime.UtcNow.Date;

        var topics = await _db.Topics
            .Where(t => t.StudyPlanId  == planId
                     && t.ScheduledDate.Date == today
                     && t.Status != TopicStatus.Skipped)
            .OrderBy(t => t.PriorityOrder)
            .ToListAsync();

        // Verify plan belongs to user
        if (topics.Any())
        {
            var planBelongsToUser = await _db.StudyPlans
                .AnyAsync(p => p.Id     == planId
                            && p.UserId == userId);

            if (!planBelongsToUser)
                throw new UnauthorizedAccessException(
                    "Plan does not belong to this user.");
        }

        return new DailyScheduleDto
        {
            DayNumber    = topics.FirstOrDefault()?.DayNumber ?? 0,
            Date         = today,
            TotalMinutes = topics.Sum(t => t.EstimatedMinutes),
            Topics       = topics.Select(MapToTopicSummary).ToList()
        };
    }

    // ══════════════════════════════════════════════════════════════
    // GET SINGLE TOPIC
    // ══════════════════════════════════════════════════════════════
    public async Task<TopicSummaryDto> GetTopicAsync(int topicId)
    {
        var topic = await _db.Topics.FindAsync(topicId)
            ?? throw new KeyNotFoundException(
                $"Topic {topicId} not found.");

        return MapToTopicSummary(topic);
    }

    // ══════════════════════════════════════════════════════════════
    // GET TODAY'S TOPIC NAME (used by ChatController for RAG context)
    // ══════════════════════════════════════════════════════════════
    public async Task<string> GetTodaysTopicNameAsync(int userId)
    {
        var today = DateTime.UtcNow.Date;

        var topic = await _db.Topics
            .Include(t => t.StudyPlan)
            .Where(t => t.StudyPlan.UserId   == userId
                     && t.ScheduledDate.Date  == today
                     && t.Status              == TopicStatus.Pending)
            .OrderBy(t => t.PriorityOrder)
            .FirstOrDefaultAsync();

        return topic?.Name ?? "General revision";
    }

    // ══════════════════════════════════════════════════════════════
    // MARK TOPIC COMPLETE
    // ══════════════════════════════════════════════════════════════
    public async Task MarkTopicCompleteAsync(
        int userId, int topicId, int minutesSpent)
    {
        var topic = await _db.Topics
            .Include(t => t.StudyPlan)
            .FirstOrDefaultAsync(t => t.Id == topicId
                               && t.StudyPlan.UserId == userId)
            ?? throw new KeyNotFoundException(
                $"Topic {topicId} not found.");

        if (topic.Status == TopicStatus.Completed)
            throw new InvalidOperationException(
                "Topic is already marked as complete.");

        topic.Status             = TopicStatus.Completed;
        topic.CompletedAt        = DateTime.UtcNow;
        topic.ActualMinutesSpent = minutesSpent;

        // Update plan completion snapshot
        var plan = topic.StudyPlan;
        plan.CompletedTopics++;
        plan.CompletionPercent   = plan.TotalTopics > 0
            ? Math.Round(
                (double)plan.CompletedTopics / plan.TotalTopics * 100, 1)
            : 0;
        plan.LastAccessedAt      = DateTime.UtcNow;

        // Auto-complete plan if all topics done
        if (plan.CompletedTopics >= plan.TotalTopics)
            plan.Status = PlanStatus.Completed;

        await _db.SaveChangesAsync();
    }

    // ══════════════════════════════════════════════════════════════
    // SAVE SYLLABUS (called externally by PlannerController)
    // ══════════════════════════════════════════════════════════════
    public async Task<int> SaveSyllabusAsync(
        int      userId,
        string   rawText,
        string?  fileName,
        string   planTitle,
        DateTime startDate,
        DateTime endDate)
    {
        var plan = new StudyPlan
        {
            UserId           = userId,
            Title            = planTitle,
            RawSyllabusText  = rawText,
            SyllabusFileName = fileName,
            StartDate        = startDate,
            EndDate          = endDate,
            Status           = PlanStatus.Active,
            Source           = fileName != null
                ? PlanSource.SyllabusUpload
                : PlanSource.ExamPreset,
            IsVectorIndexed  = false,
            CreatedAt        = DateTime.UtcNow
        };

        _db.StudyPlans.Add(plan);
        await _db.SaveChangesAsync();
        return plan.Id;
    }

    // ══════════════════════════════════════════════════════════════
    // DELETE PLAN
    // ══════════════════════════════════════════════════════════════
    public async Task DeletePlanAsync(int userId, int planId)
    {
        var plan = await _db.StudyPlans
            .FirstOrDefaultAsync(p => p.Id     == planId
                                   && p.UserId == userId)
            ?? throw new KeyNotFoundException(
                $"Plan {planId} not found.");

        // Soft delete — keep for history
        plan.Status    = PlanStatus.Archived;
        plan.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }

    // ══════════════════════════════════════════════════════════════
    // REGENERATE PLAN
    // ══════════════════════════════════════════════════════════════
    public async Task<StudyPlanResponseDto> RegeneratePlanAsync(
        int userId, int planId)
    {
        var plan = await _db.StudyPlans
            .FirstOrDefaultAsync(p => p.Id     == planId
                                   && p.UserId == userId)
            ?? throw new KeyNotFoundException(
                $"Plan {planId} not found.");

        if (string.IsNullOrWhiteSpace(plan.RawSyllabusText))
            throw new InvalidOperationException(
                "Cannot regenerate — original syllabus text is missing.");

        // 1. Remove existing topics
        var oldTopics = _db.Topics
            .Where(t => t.StudyPlanId == planId);
        _db.Topics.RemoveRange(oldTopics);

        // 2. Reset plan progress counters
        plan.CompletedTopics   = 0;
        plan.CompletionPercent = 0;
        plan.Status            = PlanStatus.Active;
        plan.IsVectorIndexed   = false;
        plan.UpdatedAt         = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        // 3. Re-generate schedule via Gemini
        var planJson = await _gemini.GenerateStudyPlanAsync(
            syllabusText: plan.RawSyllabusText,
            examTarget:   plan.ExamTarget,
            totalDays:    plan.TotalDays,
            dailyHours:   4);

        // 4. Parse and save new topics
        var topics = ParseTopicsFromJson(
            planId, planJson, plan.StartDate);

        _db.Topics.AddRange(topics);

        plan.TotalTopics       = topics.Count;
        plan.GeneratedPlanJson = planJson;

        await _db.SaveChangesAsync();

        return await BuildPlanResponseAsync(planId);
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — PERSIST PLAN + TOPICS TO SQL
    // ══════════════════════════════════════════════════════════════
    private async Task<int> PersistPlanAsync(
        int          userId,
        string       rawText,
        string?      fileName,
        string       title,
        string       examTarget,
        PlanSource   source,
        DateTime     startDate,
        DateTime     endDate,
        string       planJson)
    {
        // 1. Save plan shell
        var plan = new StudyPlan
        {
            UserId           = userId,
            Title            = title,
            ExamTarget       = examTarget,
            Source           = source,
            RawSyllabusText  = rawText,
            SyllabusFileName = fileName,
            GeneratedPlanJson = planJson,
            StartDate        = startDate,
            EndDate          = endDate,
            Status           = PlanStatus.Active,
            IsVectorIndexed  = false,
            CreatedAt        = DateTime.UtcNow
        };

        _db.StudyPlans.Add(plan);
        await _db.SaveChangesAsync();
        // plan.Id is now populated by EF Core

        // 2. Parse Gemini JSON and save topics
        var topics = ParseTopicsFromJson(
            plan.Id, planJson, startDate);

        _db.Topics.AddRange(topics);

        plan.TotalTopics = topics.Count;
        await _db.SaveChangesAsync();

        return plan.Id;
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — PARSE GEMINI JSON INTO TOPIC ENTITIES
    // ══════════════════════════════════════════════════════════════
    private List<Topic> ParseTopicsFromJson(
        int      planId,
        string   planJson,
        DateTime startDate)
    {
        List<DailyScheduleJson> days;

        try
        {
            // Strip markdown code fences if Gemini wraps JSON in ```json
            var cleaned = planJson
                .Replace("```json", "")
                .Replace("```",     "")
                .Trim();

            days = JsonSerializer
                .Deserialize<List<DailyScheduleJson>>(
                    cleaned, JsonOptions)
                ?? new List<DailyScheduleJson>();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Gemini returned invalid JSON. " +
                $"Raw response: {planJson[..Math.Min(200, planJson.Length)]}",
                ex);
        }

        var topics = new List<Topic>();
        int order  = 0;

        foreach (var day in days)
        {
            foreach (var t in day.Topics ?? new List<TopicJson>())
            {
                topics.Add(new Topic
                {
                    StudyPlanId      = planId,
                    Name             = t.Name?.Trim()
                                       ?? "Unnamed Topic",
                    Subject          = t.Subject?.Trim()
                                       ?? "General",
                    Chapter          = t.Chapter?.Trim(),
                    Description      = t.Description?.Trim(),
                    DayNumber        = day.DayNumber,
                    ScheduledDate    = startDate
                        .AddDays(day.DayNumber - 1),
                    EstimatedMinutes = t.EstimatedMinutes > 0
                        ? t.EstimatedMinutes : 60,
                    DifficultyLevel  = Math.Clamp(
                        t.DifficultyLevel, 1, 3),
                    IsExamCritical   = t.IsExamCritical,
                    PriorityOrder    = order++,
                    QdrantTopicTag   = BuildTopicTag(t.Name),
                    Status           = TopicStatus.Pending,
                    CreatedAt        = DateTime.UtcNow
                });
            }
        }

        return topics;
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — BUILD RESPONSE DTO
    // ══════════════════════════════════════════════════════════════
    private async Task<StudyPlanResponseDto> BuildPlanResponseAsync(int planId)
    {
        var plan = await _db.StudyPlans.Include(p => p.Topics
            .OrderBy(t => t.DayNumber)
            .ThenBy(t => t.PriorityOrder))
            .FirstOrDefaultAsync(p => p.Id == planId)?? 
            throw new KeyNotFoundException($"Plan {planId} not found.");

        var schedule = plan.Topics
            .GroupBy(t => t.DayNumber)
            .OrderBy(g => g.Key)
            .Select(g => new DailyScheduleDto
            {
                DayNumber    = g.Key,
                Date         = g.First().ScheduledDate,
                TotalMinutes = g.Sum(t => t.EstimatedMinutes),
                Topics       = g.Select(MapToTopicSummary).ToList()
            })
            .ToList();

        return new StudyPlanResponseDto
        {
            PlanId          = plan.Id,
            Title           = plan.Title,
            ExamTarget      = plan.ExamTarget,
            StartDate       = plan.StartDate,
            EndDate         = plan.EndDate,
            TotalDays       = plan.TotalDays,
            TotalTopics     = plan.Topics.Count,
            IsVectorIndexed = plan.IsVectorIndexed,
            Schedule        = schedule
        };
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — EXTRACT TEXT FROM UPLOADED FILE
    // ══════════════════════════════════════════════════════════════
    private static async Task<string> ExtractTextFromFileAsync(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var document = PdfDocument.Open(stream);
        
        var extension = Path.GetExtension(file.FileName).ToLower();

        if (extension is ".txt" or ".md")
        {
            using var reader = new StreamReader(file.OpenReadStream());
            return await reader.ReadToEndAsync();
        }

        if (extension == ".pdf")
        {
            var pdf = PdfDocument.Open(file.OpenReadStream());
            
            return string.Join("\n", pdf.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));

            // throw new NotSupportedException(
            //     "PDF support requires PdfPig. " +
            //     "Run: dotnet add package PdfPig");
        }

        throw new NotSupportedException(
            $"File type '{extension}' is not supported.");
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — PRESET SYLLABUS TEXT
    // ══════════════════════════════════════════════════════════════
    private static string GetPresetSyllabus(string examName) =>
        examName.ToUpper() switch
        {
            "JEE" =>
                "Physics: Mechanics, Kinematics, Laws of Motion, " +
                "Work Energy Power, Rotational Motion, Gravitation, " +
                "Thermodynamics, Waves, Optics, Electrostatics, " +
                "Current Electricity, Magnetism, Modern Physics. " +
                "Chemistry: Physical Chemistry — Mole Concept, " +
                "Thermodynamics, Equilibrium, Electrochemistry. " +
                "Organic Chemistry — Hydrocarbons, Aldehydes, " +
                "Amines, Polymers. " +
                "Inorganic Chemistry — Periodic Table, " +
                "Chemical Bonding, Coordination Compounds. " +
                "Mathematics: Sets, Relations, Trigonometry, " +
                "Algebra, Coordinate Geometry, Calculus, " +
                "Vectors, Statistics, Probability.",

            "NEET" =>
                "Physics: Mechanics, Thermodynamics, Optics, " +
                "Electromagnetism, Modern Physics. " +
                "Chemistry: Physical, Organic, Inorganic Chemistry. " +
                "Biology: Cell Biology, Genetics, Evolution, " +
                "Human Physiology, Plant Physiology, " +
                "Reproduction, Ecology, Biotechnology.",

            "UPSC" =>
                "History: Ancient, Medieval, Modern Indian History, " +
                "World History, Art and Culture. " +
                "Geography: Physical, Indian, World Geography, " +
                "Environment and Ecology. " +
                "Polity: Indian Constitution, Governance, " +
                "Social Justice, International Relations. " +
                "Economics: Indian Economy, Economic Development, " +
                "Agriculture, Industry, Infrastructure. " +
                "Science and Technology: Space, Defence, IT, " +
                "Biotechnology, Current Affairs.",

            "GATE" =>
                "Engineering Mathematics: Linear Algebra, Calculus, " +
                "Differential Equations, Probability, Statistics. " +
                "Computer Science: Data Structures, Algorithms, " +
                "Operating Systems, DBMS, Computer Networks, " +
                "Theory of Computation, Compiler Design, " +
                "Digital Logic, Computer Organisation.",

            _ => examName
            // For unknown exams, pass the name itself —
            // Gemini will interpret it and generate relevant topics
        };

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — HELPERS
    // ══════════════════════════════════════════════════════════════
    private static string BuildTopicTag(string? topicName)
    {
        if (string.IsNullOrWhiteSpace(topicName))
            return "general";

        // Normalise to lowercase snake_case for Qdrant payload tag
        // e.g. "Newton's Laws of Motion" → "newtons_laws_of_motion"
        var tag = topicName
            .ToLower()
            .Replace("'", "")
            .Replace("-", "_")
            .Replace(" ", "_");

        // Trim to 50 chars max
        return tag.Length > 50 ? tag[..50] : tag;
    }

    private static TopicSummaryDto MapToTopicSummary(Topic t) => new()
    {
        TopicId          = t.Id,
        Name             = t.Name,
        Subject          = t.Subject,
        Chapter          = t.Chapter,
        Description      = t.Description,
        EstimatedMinutes = t.EstimatedMinutes,
        DifficultyLevel  = t.DifficultyLevel,
        IsExamCritical   = t.IsExamCritical,
        Status           = t.Status.ToString()
    };

    // ── Internal JSON shapes Gemini returns ───────────────────────
    private record DailyScheduleJson(
        int              DayNumber,
        List<TopicJson>? Topics);

    private record TopicJson(
        string?  Name,
        string?  Subject,
        string?  Chapter,
        string?  Description,
        int      EstimatedMinutes,
        int      DifficultyLevel,
        bool     IsExamCritical);


    // ─── Add to PlannerService.cs ──────────────────────────────────────

    // ── Get all topics for a plan ──────────────────────────────────────
    public async Task<List<TopicSummaryDto>> GetAllTopicsAsync(
        int userId, int planId)
    {
        var plan = await _db.StudyPlans
            .AnyAsync(p => p.Id == planId && p.UserId == userId);

        if (!plan)
            throw new KeyNotFoundException(
                $"Plan {planId} not found.");

        var topics = await _db.Topics
            .Where(t => t.StudyPlanId == planId)
            .OrderBy(t => t.DayNumber)
            .ThenBy(t => t.PriorityOrder)
            .ToListAsync();

        return topics.Select(MapToTopicSummary).ToList();
    }

    // ── Get weak topics ────────────────────────────────────────────────
    public async Task<List<TopicSummaryDto>> GetWeakTopicsAsync(
        int userId, int planId)
    {
        var topics = await _db.Topics
            .Include(t => t.StudyPlan)
            .Where(t => t.StudyPlanId      == planId
                    && t.StudyPlan.UserId == userId
                    && t.IsWeakArea)
            .OrderByDescending(t => t.DifficultyLevel)
            .ToListAsync();

        return topics.Select(MapToTopicSummary).ToList();
    }

    // ── Get exam critical topics ───────────────────────────────────────
    public async Task<List<TopicSummaryDto>> GetExamCriticalTopicsAsync(
        int userId, int planId)
    {
        var topics = await _db.Topics
            .Include(t => t.StudyPlan)
            .Where(t => t.StudyPlanId      == planId
                    && t.StudyPlan.UserId == userId
                    && t.IsExamCritical)
            .OrderBy(t => t.DayNumber)
            .ToListAsync();

        return topics.Select(MapToTopicSummary).ToList();
    }

    // ── Get topics by subject ──────────────────────────────────────────
    public async Task<List<TopicSummaryDto>> GetTopicsBySubjectAsync(
        int userId, int planId, string subject)
    {
        var topics = await _db.Topics
            .Include(t => t.StudyPlan)
            .Where(t => t.StudyPlanId      == planId
                    && t.StudyPlan.UserId == userId
                    && t.Subject          == subject)
            .OrderBy(t => t.DayNumber)
            .ToListAsync();

        return topics.Select(MapToTopicSummary).ToList();
    }

    // ── Get day schedule ───────────────────────────────────────────────
    public async Task<DailyScheduleDto> GetDayScheduleAsync(
        int userId, int planId, int dayNumber)
    {
        var topics = await _db.Topics
            .Include(t => t.StudyPlan)
            .Where(t => t.StudyPlanId      == planId
                    && t.StudyPlan.UserId == userId
                    && t.DayNumber        == dayNumber)
            .OrderBy(t => t.PriorityOrder)
            .ToListAsync();

        if (!topics.Any())
            throw new KeyNotFoundException(
                $"No topics found for day {dayNumber}.");

        return new DailyScheduleDto
        {
            DayNumber    = dayNumber,
            Date         = topics.First().ScheduledDate,
            TotalMinutes = topics.Sum(t => t.EstimatedMinutes),
            Topics       = topics.Select(MapToTopicSummary).ToList()
        };
    }

    // ── Update topic status ────────────────────────────────────────────
    public async Task UpdateTopicStatusAsync(
        int userId, int topicId, string status)
    {
        var topic = await _db.Topics
            .Include(t => t.StudyPlan)
            .FirstOrDefaultAsync(t =>
                t.Id               == topicId
            && t.StudyPlan.UserId == userId)
            ?? throw new KeyNotFoundException(
                $"Topic {topicId} not found.");

        if (!Enum.TryParse<TopicStatus>(
            status, ignoreCase: true, out var topicStatus))
            throw new ArgumentException(
                $"Invalid status '{status}'. " +
                "Valid values: Pending, InProgress, " +
                "Completed, Skipped, Revised.");

        topic.Status    = topicStatus;
        topic.UpdatedAt = DateTime.UtcNow;

        if (topicStatus == TopicStatus.Completed)
            topic.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }

    // ── Skip topic ─────────────────────────────────────────────────────
    public async Task SkipTopicAsync(int userId, int topicId)
    {
        var topic = await _db.Topics
            .Include(t => t.StudyPlan)
            .FirstOrDefaultAsync(t =>
                t.Id               == topicId
            && t.StudyPlan.UserId == userId)
            ?? throw new KeyNotFoundException(
                $"Topic {topicId} not found.");

        topic.Status    = TopicStatus.Skipped;
        topic.UpdatedAt = DateTime.UtcNow;

        // Find the next available day to reschedule
        var lastDay = await _db.Topics
            .Where(t => t.StudyPlanId == topic.StudyPlanId)
            .MaxAsync(t => t.DayNumber);

        // Add as a new topic on the last day + 1
        var rescheduled = new Topic
        {
            StudyPlanId      = topic.StudyPlanId,
            Name             = topic.Name,
            Subject          = topic.Subject,
            Chapter          = topic.Chapter,
            Description      = topic.Description,
            DayNumber        = lastDay + 1,
            ScheduledDate    = topic.StudyPlan.EndDate.AddDays(1),
            EstimatedMinutes = topic.EstimatedMinutes,
            DifficultyLevel  = topic.DifficultyLevel,
            IsExamCritical   = topic.IsExamCritical,
            PriorityOrder    = 0,
            QdrantTopicTag   = topic.QdrantTopicTag,
            Status           = TopicStatus.Pending,
            CreatedAt        = DateTime.UtcNow
        };

        _db.Topics.Add(rescheduled);
        await _db.SaveChangesAsync();
    }

    // ── Search topics ──────────────────────────────────────────────────
    public async Task<List<TopicSummaryDto>> SearchTopicsAsync(
        int userId, int planId, string query)
    {
        var lower  = query.ToLower().Trim();

        var topics = await _db.Topics
            .Include(t => t.StudyPlan)
            .Where(t => t.StudyPlanId      == planId
                    && t.StudyPlan.UserId == userId
                    && (t.Name.ToLower().Contains(lower)
                    || t.Subject.ToLower().Contains(lower)
                    || (t.Chapter != null
                    && t.Chapter.ToLower().Contains(lower))))
            .OrderBy(t => t.DayNumber)
            .Take(20) // cap results
            .ToListAsync();

        return topics.Select(MapToTopicSummary).ToList();
    }
}