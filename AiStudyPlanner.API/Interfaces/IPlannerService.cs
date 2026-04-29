using System;
using AiStudyPlanner.API.Models.DTOs;

namespace AiStudyPlanner.API.Interfaces;

public interface IPlannerService
{
    Task<StudyPlanResponseDto> GeneratePlanFromSyllabusAsync(int userId, SyllabusUploadDto dto);

    Task<StudyPlanResponseDto> GeneratePlanFromPresetAsync(int userId, ExamPresetRequestDto dto);

    Task<StudyPlanResponseDto> GetPlanAsync(int userId, int planId);

    Task<List<StudyPlanResponseDto>> GetAllPlansAsync(int userId);

    Task<DailyScheduleDto> GetTodaysScheduleAsync(int userId, int planId);

    Task<TopicSummaryDto> GetTopicAsync(int topicId);

    Task<string> GetTodaysTopicNameAsync(int userId);

    Task MarkTopicCompleteAsync(int userId, int topicId, int minutesSpent);

    Task<int> SaveSyllabusAsync(int userId, string rawText, string? fileName, string planTitle, DateTime startDate, DateTime endDate);

    Task DeletePlanAsync(int userId, int planId);

    Task<StudyPlanResponseDto> RegeneratePlanAsync(int userId, int planId);
    Task<List<TopicSummaryDto>> GetAllTopicsAsync(int userId, int planId);

    Task<List<TopicSummaryDto>> GetWeakTopicsAsync(int userId, int planId);

    Task<List<TopicSummaryDto>> GetExamCriticalTopicsAsync(int userId, int planId);

    Task<List<TopicSummaryDto>> GetTopicsBySubjectAsync(int userId, int planId, string subject);

    Task<DailyScheduleDto> GetDayScheduleAsync(int userId, int planId, int dayNumber);

    Task UpdateTopicStatusAsync(int userId, int topicId, string status);

    Task SkipTopicAsync(int userId, int topicId);

    Task<List<TopicSummaryDto>> SearchTopicsAsync(int userId, int planId, string query);
}
