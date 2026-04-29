using System;
using AiStudyPlanner.API.Controllers;
using AiStudyPlanner.API.Models.DTOs;

namespace AiStudyPlanner.API.Interfaces;

public interface IProgressService
{
    Task<QuizResultResponseDto> SubmitQuizAsync(int userId, QuizSubmitDto dto);

    Task<ProgressSummaryDto> GetProgressSummaryAsync(int userId, int planId);

    Task<List<WeakAreaDto>> GetActiveWeakAreasAsync(int userId);

    Task<List<WeakAreaDto>> GetAllWeakAreasAsync(int userId);

    Task<WeakAreaDto> GetWeakAreaAsync(int weakAreaId);

    Task ResolveWeakAreaAsync(int userId, int weakAreaId);

    Task<List<SubjectScoreDto>> GetHeatmapDataAsync(int userId, int planId);

    Task<List<string>> GetWeakAreaTopicNamesAsync(int userId);

    Task UpdateTopicScoreAsync(int topicId, double newScore);
    Task<List<QuizHistoryItemDto>> GetQuizHistoryAsync(
    int userId, int topicId);

    Task<int>          GetStreakAsync(int userId);
    Task<PlanStatsDto> GetPlanStatsAsync(int userId, int planId);
    Task<TodayProgressDto> GetTodayProgressAsync(int userId, int planId);
    Task<WeakAreaDto?> GetWeakAreaByTopicAsync(int userId, int topicId);
}
