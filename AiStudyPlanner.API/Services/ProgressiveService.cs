using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AiStudyPlanner.API.Data;
using AiStudyPlanner.API.Interfaces;
using AiStudyPlanner.API.Models.DTOs;
using AiStudyPlanner.API.Models.Entities;
using AiStudyPlanner.API.Controllers;

namespace AiStudyPlanner.API.Services;

public class ProgressService : IProgressService
{
    private readonly AppDbContext  _db;
    private readonly IGeminiService _gemini;
    private readonly ILogger<ProgressService> _logger;

    // ── Scoring thresholds ─────────────────────────────────────────
    private const double PassThreshold     = 60.0;
    private const double CriticalThreshold = 40.0;
    private const double TargetScore       = 75.0;
    private const double ImprovingThreshold = 65.0;

    public ProgressService(
        AppDbContext             db,
        IGeminiService           gemini,
        ILogger<ProgressService> logger)
    {
        _db     = db;
        _gemini = gemini;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    // SUBMIT QUIZ
    // ══════════════════════════════════════════════════════════════
    public async Task<QuizResultResponseDto> SubmitQuizAsync(
        int userId, QuizSubmitDto dto)
    {
        // 1. Load topic with plan
        var topic = await _db.Topics
            .Include(t => t.StudyPlan)
            .FirstOrDefaultAsync(t => t.Id == dto.TopicId)
            ?? throw new KeyNotFoundException(
                $"Topic {dto.TopicId} not found.");

        // Verify topic belongs to this user
        if (topic.StudyPlan.UserId != userId)
            throw new UnauthorizedAccessException(
                "Topic does not belong to this user.");

        // 2. Compute score
        var scorePercent = dto.TotalQuestions > 0
            ? Math.Round(
                (double)dto.CorrectAnswers
                / dto.TotalQuestions * 100, 1)
            : 0.0;

        // 3. Get attempt number
        var attemptNumber = await _db.QuizResults
            .CountAsync(q =>
                q.TopicId == dto.TopicId
             && q.UserId  == userId) + 1;

        // 4. Parse quiz type
        var quizType = Enum.TryParse<QuizType>(
            dto.QuizType, ignoreCase: true, out var parsed)
            ? parsed
            : QuizType.TopicEnd;

        // 5. Get AI feedback from Gemini
        string aiFeedback;
        try
        {
            aiFeedback = await _gemini.GenerateQuizFeedbackAsync(
                topic.Name, dto.Answers);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to get AI feedback for topic {TopicId}",
                dto.TopicId);
            aiFeedback = GenerateFallbackFeedback(
                scorePercent, topic.Name);
        }

        // 6. Save quiz result
        var result = new QuizResult
        {
            UserId              = userId,
            TopicId             = dto.TopicId,
            QuizType            = quizType,
            TotalQuestions      = dto.TotalQuestions,
            CorrectAnswers      = dto.CorrectAnswers,
            WrongAnswers        = dto.WrongAnswers,
            SkippedQuestions    = dto.SkippedQuestions,
            ScorePercent        = scorePercent,
            TimeTakenSeconds    = dto.TimeTakenSeconds,
            AttemptNumber       = attemptNumber,
            IsPassed            = scorePercent >= PassThreshold,
            AiFeedback          = aiFeedback,
            AnswerBreakdownJson = dto.Answers.Any()
                ? JsonSerializer.Serialize(dto.Answers)
                : null,
            TakenAt             = DateTime.UtcNow
        };

        _db.QuizResults.Add(result);
        await _db.SaveChangesAsync();
        // Save result first so result.Id is populated
        // before weak area references it

        // 7. Update topic running average score
        var newAverage = await ComputeRunningAverageAsync(
            dto.TopicId, userId);

        topic.ScorePercent  = newAverage;
        topic.QuizAttempts++;
        topic.IsWeakArea    = newAverage < PassThreshold;
        topic.UpdatedAt     = DateTime.UtcNow;

        // Mark topic completed if passed
        if (result.IsPassed
         && topic.Status != TopicStatus.Completed)
        {
            topic.Status      = TopicStatus.Completed;
            topic.CompletedAt = DateTime.UtcNow;
        }

        // 8. Handle weak area logic
        var weakAreaFlagged   = false;
        var revisionScheduled = false;
        var suggestedTopics   = new List<string>();

        if (topic.IsWeakArea)
        {
            (weakAreaFlagged,
             revisionScheduled,
             suggestedTopics) = await HandleWeakAreaAsync(
                userId, topic, result);

            result.TriggeredWeakAreaFlag = weakAreaFlagged;
            result.TriggeredRevisionSlot = revisionScheduled;
        }
        else
        {
            // Score improved — try to resolve existing weak area
            await TryResolveWeakAreaAsync(
                userId, topic.Id, newAverage);
        }

        // 9. Update plan completion snapshot
        await UpdatePlanProgressAsync(topic.StudyPlan);

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Quiz submitted for topic {TopicId} by user {UserId}. " +
            "Score: {Score}%, Passed: {Passed}, WeakArea: {Weak}",
            dto.TopicId, userId,
            scorePercent, result.IsPassed, weakAreaFlagged);

        return new QuizResultResponseDto
        {
            QuizResultId            = result.Id,
            TopicId                 = topic.Id,
            TopicName               = topic.Name,
            ScorePercent            = scorePercent,
            CorrectAnswers          = dto.CorrectAnswers,
            TotalQuestions          = dto.TotalQuestions,
            IsPassed                = result.IsPassed,
            IsWeakAreaFlagged       = weakAreaFlagged,
            RevisionScheduled       = revisionScheduled,
            AiFeedback              = aiFeedback,
            SuggestedRevisionTopics = suggestedTopics,
            TakenAt                 = result.TakenAt
        };
    }

    // ══════════════════════════════════════════════════════════════
    // GET PROGRESS SUMMARY
    // ══════════════════════════════════════════════════════════════
    public async Task<ProgressSummaryDto> GetProgressSummaryAsync(
        int userId, int planId)
    {
        // Verify plan belongs to user
        var plan = await _db.StudyPlans
            .FirstOrDefaultAsync(p =>
                p.Id     == planId
             && p.UserId == userId)
            ?? throw new KeyNotFoundException(
                $"Plan {planId} not found.");

        var topics = await _db.Topics
            .Where(t => t.StudyPlanId == planId)
            .ToListAsync();

        var weakAreas = await _db.WeakAreas
            .Where(w => w.UserId == userId)
            .ToListAsync();

        var streak          = await ComputeStreakAsync(userId);
        var heatmapData     = await GetHeatmapDataAsync(userId, planId);
        var completedTopics = topics.Count(t =>
            t.Status == TopicStatus.Completed);

        return new ProgressSummaryDto
        {
            TotalTopics         = topics.Count,
            CompletedTopics     = completedTopics,
            CompletionPercent   = topics.Any()
                ? Math.Round(
                    (double)completedTopics
                    / topics.Count * 100, 1)
                : 0,
            ActiveWeakAreas     = weakAreas.Count(w =>
                w.Status == WeakAreaStatus.Active),
            ResolvedWeakAreas   = weakAreas.Count(w =>
                w.Status == WeakAreaStatus.Resolved),
            OverallAverageScore = topics
                .Any(t => t.QuizAttempts > 0)
                ? Math.Round(topics
                    .Where(t => t.QuizAttempts > 0)
                    .Average(t => t.ScorePercent), 1)
                : 0,
            CurrentStreak       = streak,
            SubjectBreakdown    = heatmapData
        };
    }

    // ══════════════════════════════════════════════════════════════
    // GET ACTIVE WEAK AREAS
    // ══════════════════════════════════════════════════════════════
    public async Task<List<WeakAreaDto>> GetActiveWeakAreasAsync(
        int userId)
    {
        var areas = await _db.WeakAreas
            .Where(w =>
                w.UserId == userId
             && w.Status == WeakAreaStatus.Active)
            .OrderByDescending(w => w.HeatmapIntensity)
            .ThenByDescending(w => w.DetectedAt)
            .ToListAsync();

        return areas.Select(MapToWeakAreaDto).ToList();
    }

    // ══════════════════════════════════════════════════════════════
    // GET ALL WEAK AREAS
    // ══════════════════════════════════════════════════════════════
    public async Task<List<WeakAreaDto>> GetAllWeakAreasAsync(
        int userId)
    {
        var areas = await _db.WeakAreas
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.DetectedAt)
            .ToListAsync();

        return areas.Select(MapToWeakAreaDto).ToList();
    }

    // ══════════════════════════════════════════════════════════════
    // GET SINGLE WEAK AREA
    // ══════════════════════════════════════════════════════════════
    public async Task<WeakAreaDto> GetWeakAreaAsync(int weakAreaId)
    {
        var area = await _db.WeakAreas
            .FindAsync(weakAreaId)
            ?? throw new KeyNotFoundException(
                $"Weak area {weakAreaId} not found.");

        return MapToWeakAreaDto(area);
    }

    // ══════════════════════════════════════════════════════════════
    // RESOLVE WEAK AREA
    // ══════════════════════════════════════════════════════════════
    public async Task ResolveWeakAreaAsync(
        int userId, int weakAreaId)
    {
        var area = await _db.WeakAreas
            .FirstOrDefaultAsync(w =>
                w.Id     == weakAreaId
             && w.UserId == userId)
            ?? throw new KeyNotFoundException(
                $"Weak area {weakAreaId} not found.");

        area.Status    = WeakAreaStatus.Ignored;
        area.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Weak area {WeakAreaId} manually resolved " +
            "by user {UserId}", weakAreaId, userId);
    }

    // ══════════════════════════════════════════════════════════════
    // GET HEATMAP DATA
    // ══════════════════════════════════════════════════════════════
    public async Task<List<SubjectScoreDto>> GetHeatmapDataAsync(
        int userId, int planId)
    {
        var topics = await _db.Topics
            .Where(t => t.StudyPlanId == planId)
            .ToListAsync();

        if (!topics.Any())
            return new List<SubjectScoreDto>();

        return topics
            .GroupBy(t => t.Subject)
            .Select(g =>
            {
                var attempted = g
                    .Where(t => t.QuizAttempts > 0)
                    .ToList();

                var avgScore = attempted.Any()
                    ? Math.Round(
                        attempted.Average(t => t.ScorePercent), 1)
                    : 0.0;

                return new SubjectScoreDto
                {
                    Subject          = g.Key,
                    TotalTopics      = g.Count(),
                    CompletedTopics  = g.Count(t =>
                        t.Status == TopicStatus.Completed),
                    WeakTopicsCount  = g.Count(t => t.IsWeakArea),
                    AverageScore     = avgScore,
                    HeatmapIntensity = ComputeSubjectIntensity(
                        avgScore, g.Count(t => t.IsWeakArea),
                        g.Count())
                };
            })
            .OrderBy(s => s.Subject)
            .ToList();
    }

    // ══════════════════════════════════════════════════════════════
    // GET WEAK AREA TOPIC NAMES
    // Used by ChatController to inject into RAG prompt
    // ══════════════════════════════════════════════════════════════
    public async Task<List<string>> GetWeakAreaTopicNamesAsync(
        int userId)
    {
        return await _db.WeakAreas
            .Where(w =>
                w.UserId == userId
             && w.Status == WeakAreaStatus.Active)
            .OrderByDescending(w => w.HeatmapIntensity)
            .Select(w => w.TopicName)
            .ToListAsync();
    }

    // ══════════════════════════════════════════════════════════════
    // UPDATE TOPIC SCORE
    // ══════════════════════════════════════════════════════════════
    public async Task UpdateTopicScoreAsync(
        int topicId, double newScore)
    {
        var topic = await _db.Topics.FindAsync(topicId);
        if (topic is null) return;

        topic.ScorePercent = newScore;
        topic.IsWeakArea   = newScore < PassThreshold;
        topic.UpdatedAt    = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }

    // ══════════════════════════════════════════════════════════════
    // GET QUIZ HISTORY
    // ══════════════════════════════════════════════════════════════
    public async Task<List<QuizHistoryItemDto>> GetQuizHistoryAsync(
        int userId, int topicId)
    {
        var results = await _db.QuizResults
            .Where(q =>
                q.UserId  == userId
             && q.TopicId == topicId)
            .OrderBy(q => q.AttemptNumber)
            .ToListAsync();

        return results.Select(q => new QuizHistoryItemDto
        {
            AttemptNumber    = q.AttemptNumber,
            ScorePercent     = Math.Round(q.ScorePercent, 1),
            CorrectAnswers   = q.CorrectAnswers,
            TotalQuestions   = q.TotalQuestions,
            IsPassed         = q.IsPassed,
            TimeTakenSeconds = q.TimeTakenSeconds,
            QuizType         = q.QuizType.ToString(),
            AiFeedback       = q.AiFeedback,
            TakenAt          = q.TakenAt
        }).ToList();
    }

    // ══════════════════════════════════════════════════════════════
    // GET STREAK
    // ══════════════════════════════════════════════════════════════
    public async Task<int> GetStreakAsync(int userId)
    {
        var dates = await _db.QuizResults
            .Where(q => q.UserId == userId)
            .Select(q => q.TakenAt.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync();

        var streak  = 0;
        var current = DateTime.UtcNow.Date;

        foreach (var date in dates)
        {
            if (date == current)
            {
                streak++;
                current = current.AddDays(-1);
            }
            else break;
        }

        return streak;
    }

    // ══════════════════════════════════════════════════════════════
    // GET PLAN STATS
    // ══════════════════════════════════════════════════════════════
    public async Task<PlanStatsDto> GetPlanStatsAsync(
        int userId, int planId)
    {
        var plan = await _db.StudyPlans
            .FirstOrDefaultAsync(p =>
                p.Id     == planId
             && p.UserId == userId)
            ?? throw new KeyNotFoundException(
                $"Plan {planId} not found.");

        var topics = await _db.Topics
            .Where(t => t.StudyPlanId == planId)
            .ToListAsync();

        var topicIds    = topics.Select(t => t.Id).ToList();

        var quizResults = await _db.QuizResults
            .Where(q =>
                q.UserId == userId
             && topicIds.Contains(q.TopicId))
            .ToListAsync();

        var weakAreas = await _db.WeakAreas
            .Where(w => w.UserId == userId)
            .ToListAsync();

        var completedCount   = topics.Count(t =>
            t.Status == TopicStatus.Completed);
        var totalMinutes     = topics
            .Where(t => t.Status == TopicStatus.Completed)
            .Sum(t => t.ActualMinutesSpent);
        var daysRemaining    = Math.Max(0,
            (int)(plan.EndDate - DateTime.UtcNow).TotalDays);
        var expectedByNow    = CalculateExpectedCompletion(plan);
        var isOnTrack        = completedCount >= expectedByNow;

        return new PlanStatsDto
        {
            TotalTopics         = topics.Count,
            CompletedTopics     = completedCount,
            PendingTopics       = topics.Count(t =>
                t.Status == TopicStatus.Pending),
            WeakTopics          = topics.Count(t => t.IsWeakArea),
            CriticalTopics      = topics.Count(t =>
                t.IsExamCritical),
            OverallAverageScore = topics
                .Any(t => t.QuizAttempts > 0)
                ? Math.Round(topics
                    .Where(t => t.QuizAttempts > 0)
                    .Average(t => t.ScorePercent), 1)
                : 0,
            CompletionPercent   = Math.Round(
                plan.CompletionPercent, 1),
            TotalMinutesStudied = totalMinutes,
            TotalQuizzesTaken   = quizResults.Count,
            TotalQuizzesPassed  = quizResults
                .Count(q => q.IsPassed),
            QuizPassRate        = quizResults.Any()
                ? Math.Round(
                    (double)quizResults.Count(q => q.IsPassed)
                    / quizResults.Count * 100, 1)
                : 0,
            ActiveWeakAreas     = weakAreas.Count(w =>
                w.Status == WeakAreaStatus.Active),
            ResolvedWeakAreas   = weakAreas.Count(w =>
                w.Status == WeakAreaStatus.Resolved),
            DaysRemaining       = daysRemaining,
            IsOnTrack           = isOnTrack
        };
    }

    // ══════════════════════════════════════════════════════════════
    // GET TODAY PROGRESS
    // ══════════════════════════════════════════════════════════════
    public async Task<TodayProgressDto> GetTodayProgressAsync(
        int userId, int planId)
    {
        var today = DateTime.UtcNow.Date;

        var todaysTopics = await _db.Topics
            .Include(t => t.StudyPlan)
            .Where(t =>
                t.StudyPlanId        == planId
             && t.StudyPlan.UserId   == userId
             && t.ScheduledDate.Date == today)
            .OrderBy(t => t.PriorityOrder)
            .ToListAsync();

        var completed        = todaysTopics
            .Where(t => t.Status == TopicStatus.Completed)
            .ToList();

        var remaining        = todaysTopics
            .Where(t => t.Status == TopicStatus.Pending
                     || t.Status == TopicStatus.InProgress)
            .ToList();

        var minutesStudied   = completed
            .Sum(t => t.ActualMinutesSpent);
        var minutesRemaining = remaining
            .Sum(t => t.EstimatedMinutes);

        return new TodayProgressDto
        {
            TotalTopicsToday      = todaysTopics.Count,
            CompletedToday        = completed.Count,
            RemainingToday        = remaining.Count,
            TodayCompletionPct    = todaysTopics.Any()
                ? Math.Round(
                    (double)completed.Count
                    / todaysTopics.Count * 100, 1)
                : 0,
            MinutesStudiedToday   = minutesStudied,
            MinutesRemainingToday = minutesRemaining,
            IsTodayComplete       = !remaining.Any()
                                 && todaysTopics.Any(),
            TodaysTopics          = todaysTopics
                .Select(MapToTopicSummary)
                .ToList()
        };
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — HANDLE WEAK AREA CREATION / UPDATE
    // ══════════════════════════════════════════════════════════════
    private async Task<(bool flagged,
                        bool scheduled,
                        List<string> suggestedTopics)>
        HandleWeakAreaAsync(
            int userId, Topic topic, QuizResult result)
    {
        var suggestedTopics = new List<string>();

        // Check if active weak area already exists for this topic
        var existing = await _db.WeakAreas
            .FirstOrDefaultAsync(w =>
                w.UserId  == userId
             && w.TopicId == topic.Id
             && w.Status  == WeakAreaStatus.Active);

        if (existing is not null)
        {
            // Update existing weak area
            existing.TotalFailedAttempts++;
            existing.CurrentScore     = result.ScorePercent;
            existing.HeatmapIntensity = Math.Min(5,
                existing.HeatmapIntensity + 1);
            existing.LastRevisionAt   = DateTime.UtcNow;
            existing.UpdatedAt        = DateTime.UtcNow;

            // Escalate severity if score keeps dropping
            if (result.ScorePercent < CriticalThreshold)
                existing.Severity = WeakAreaSeverity.Critical;

            return (true, false, suggestedTopics);
        }

        // Create new weak area
        var severity = result.ScorePercent < CriticalThreshold
            ? WeakAreaSeverity.Critical
            : WeakAreaSeverity.Moderate;

        // Get AI insight and recommendation
        string insight         = string.Empty;
        string recommendation  = string.Empty;

        try
        {
            var wrongAnswers = result.AnswerBreakdownJson is not null
                ? JsonSerializer
                    .Deserialize<List<QuizAnswerDetailDto>>(
                        result.AnswerBreakdownJson)
                    ?? new List<QuizAnswerDetailDto>()
                : new List<QuizAnswerDetailDto>();

            insight = await _gemini.GenerateWeakAreaInsightAsync(
                topic.Name,
                result.ScorePercent,
                wrongAnswers.Where(a => !a.IsCorrect).ToList());

            recommendation = await _gemini
                .GenerateWeakAreaRecommendationAsync(
                    topic.Name, topic.Subject);

            // Parse suggested topics from recommendation
            suggestedTopics = ExtractSuggestedTopics(recommendation);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to get AI insight for weak area " +
                "topic {TopicId}", topic.Id);

            insight        = $"Review {topic.Name} concepts carefully.";
            recommendation = "Re-read the chapter and attempt " +
                             "practice problems.";
        }

        var weakArea = new WeakArea
        {
            UserId                    = userId,
            TopicId                   = topic.Id,
            TopicName                 = topic.Name,
            Subject                   = topic.Subject,
            Severity                  = severity,
            ScoreAtDetection          = result.ScorePercent,
            CurrentScore              = result.ScorePercent,
            TargetScore               = TargetScore,
            QuizResultId              = result.Id,
            AttemptNumberAtDetection  = result.AttemptNumber,
            TotalFailedAttempts       = 1,
            AiInsight                 = insight,
            AiRecommendation          = recommendation,
            HeatmapIntensity          = severity ==
                WeakAreaSeverity.Critical ? 5 : 3,
            RevisionSlotsAdded        = 1,
            DetectedAt                = DateTime.UtcNow
        };

        _db.WeakAreas.Add(weakArea);
        topic.IsRevisionScheduled = true;

        _logger.LogInformation(
            "New weak area created for topic {TopicId} " +
            "({TopicName}), severity: {Severity}",
            topic.Id, topic.Name, severity);

        return (true, true, suggestedTopics);
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — TRY RESOLVE WEAK AREA ON IMPROVED SCORE
    // ══════════════════════════════════════════════════════════════
    private async Task TryResolveWeakAreaAsync(
        int userId, int topicId, double newScore)
    {
        var area = await _db.WeakAreas
            .FirstOrDefaultAsync(w =>
                w.UserId  == userId
             && w.TopicId == topicId
             && (w.Status == WeakAreaStatus.Active
              || w.Status == WeakAreaStatus.Resolving));

        if (area is null) return;

        area.CurrentScore           = newScore;
        area.RevisionSlotsCompleted++;
        area.LastRevisionAt         = DateTime.UtcNow;

        if (newScore >= ImprovingThreshold
         && newScore < (area.TargetScore ?? TargetScore))
        {
            // Score improved but not yet at target
            area.Status    = WeakAreaStatus.Resolving;
            area.Severity  = WeakAreaSeverity.Improving;
            area.HeatmapIntensity = Math.Max(1,
                area.HeatmapIntensity - 1);
        }

        if (newScore >= (area.TargetScore ?? TargetScore))
        {
            // Fully resolved
            area.Status      = WeakAreaStatus.Resolved;
            area.ResolvedAt  = DateTime.UtcNow;
            area.ResolvedNote =
                $"Scored {newScore:F1}% on attempt " +
                $"{area.RevisionSlotsCompleted + 1} — " +
                $"target of {area.TargetScore:F0}% reached.";
            area.HeatmapIntensity = 1;

            _logger.LogInformation(
                "Weak area resolved for topic {TopicId}. " +
                "Final score: {Score}%",
                topicId, newScore);
        }

        area.UpdatedAt = DateTime.UtcNow;
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — UPDATE PLAN PROGRESS SNAPSHOT
    // ══════════════════════════════════════════════════════════════
    private async Task UpdatePlanProgressAsync(StudyPlan plan)
    {
        var completed = await _db.Topics
            .CountAsync(t =>
                t.StudyPlanId == plan.Id
             && t.Status      == TopicStatus.Completed);

        plan.CompletedTopics   = completed;
        plan.CompletionPercent = plan.TotalTopics > 0
            ? Math.Round(
                (double)completed / plan.TotalTopics * 100, 1)
            : 0;
        plan.LastAccessedAt    = DateTime.UtcNow;

        // Auto-complete plan
        if (completed >= plan.TotalTopics
         && plan.TotalTopics > 0)
        {
            plan.Status    = PlanStatus.Completed;
            plan.UpdatedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Plan {PlanId} auto-completed. " +
                "All {Total} topics finished.",
                plan.Id, plan.TotalTopics);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — COMPUTE RUNNING AVERAGE SCORE FOR TOPIC
    // ══════════════════════════════════════════════════════════════
    private async Task<double> ComputeRunningAverageAsync(
        int topicId, int userId)
    {
        var scores = await _db.QuizResults
            .Where(q =>
                q.TopicId == topicId
             && q.UserId  == userId)
            .Select(q => q.ScorePercent)
            .ToListAsync();

        return scores.Any()
            ? Math.Round(scores.Average(), 1)
            : 0.0;
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — COMPUTE STUDY STREAK
    // ══════════════════════════════════════════════════════════════
    private async Task<int> ComputeStreakAsync(int userId)
    {
        var dates = await _db.QuizResults
            .Where(q => q.UserId == userId)
            .Select(q => q.TakenAt.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync();

        var streak  = 0;
        var current = DateTime.UtcNow.Date;

        foreach (var date in dates)
        {
            if (date == current)
            {
                streak++;
                current = current.AddDays(-1);
            }
            else break;
        }

        return streak;
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — COMPUTE SUBJECT HEATMAP INTENSITY (1–5)
    // ══════════════════════════════════════════════════════════════
    private static int ComputeSubjectIntensity(
        double avgScore,
        int    weakTopicsCount,
        int    totalTopics)
    {
        // No quiz attempts yet — neutral intensity
        if (avgScore == 0) return 1;

        // Boost intensity based on weak topic ratio
        var weakRatio = totalTopics > 0
            ? (double)weakTopicsCount / totalTopics
            : 0;

        var baseIntensity = avgScore switch
        {
            >= 80 => 1,
            >= 65 => 2,
            >= 50 => 3,
            >= 35 => 4,
            _     => 5
        };

        // Add 1 if more than 30% of topics are weak
        var boost = weakRatio > 0.3 ? 1 : 0;

        return Math.Min(5, baseIntensity + boost);
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — CALCULATE EXPECTED COMPLETION BY TODAY
    // ══════════════════════════════════════════════════════════════
    private static int CalculateExpectedCompletion(StudyPlan plan)
    {
        var totalDays   = (plan.EndDate - plan.StartDate).TotalDays;
        var elapsedDays = (DateTime.UtcNow - plan.StartDate).TotalDays;

        if (totalDays <= 0) return 0;

        var ratio = Math.Clamp(elapsedDays / totalDays, 0, 1);
        return (int)(plan.TotalTopics * ratio);
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — GENERATE FALLBACK FEEDBACK (no Gemini)
    // ══════════════════════════════════════════════════════════════
    private static string GenerateFallbackFeedback(
        double scorePercent, string topicName)
    {
        return scorePercent switch
        {
            >= 80 => $"Great work on {topicName}! " +
                     "You have a strong understanding of this topic.",
            >= 60 => $"Good effort on {topicName}. " +
                     "Review the questions you missed and try again.",
            >= 40 => $"You need more practice on {topicName}. " +
                     "Re-read the chapter and focus on core concepts.",
            _     => $"{topicName} needs significant revision. " +
                     "Start from the basics and work through " +
                     "examples step by step."
        };
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — EXTRACT SUGGESTED TOPICS FROM AI TEXT
    // ══════════════════════════════════════════════════════════════
    private static List<string> ExtractSuggestedTopics(
        string recommendation)
    {
        // Simple extraction — look for topic-like phrases
        // In production replace with structured Gemini JSON output
        var topics = new List<string>();

        if (string.IsNullOrWhiteSpace(recommendation))
            return topics;

        var sentences = recommendation.Split(
            new[] { '.', '!', '?' },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var sentence in sentences.Take(3))
        {
            var trimmed = sentence.Trim();
            if (trimmed.Length > 10 && trimmed.Length < 100)
                topics.Add(trimmed);
        }

        return topics;
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — MAP WEAK AREA ENTITY TO DTO
    // ══════════════════════════════════════════════════════════════
    private static WeakAreaDto MapToWeakAreaDto(WeakArea w) => new()
    {
        Id               = w.Id,
        TopicId          = w.TopicId,
        TopicName        = w.TopicName,
        Subject          = w.Subject,
        Severity         = w.Severity.ToString(),
        ScoreAtDetection = Math.Round(w.ScoreAtDetection, 1),
        CurrentScore     = Math.Round(w.CurrentScore, 1),
        TargetScore      = w.TargetScore,
        AiInsight        = w.AiInsight,
        AiRecommendation = w.AiRecommendation,
        HeatmapIntensity = w.HeatmapIntensity,
        Status           = w.Status.ToString(),
        DetectedAt       = w.DetectedAt,
        ResolvedAt       = w.ResolvedAt
    };

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — MAP TOPIC ENTITY TO SUMMARY DTO
    // ══════════════════════════════════════════════════════════════
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

    public async Task<WeakAreaDto?> GetWeakAreaByTopicAsync(int userId, int topicId)
    {
        var area = await _db.WeakAreas
            .FirstOrDefaultAsync(w =>
                w.UserId  == userId
            && w.TopicId == topicId
            && w.Status  == WeakAreaStatus.Active);

        return area is null ? null : MapToWeakAreaDto(area);
    }
    
}

