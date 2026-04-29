using System;

namespace AiStudyPlanner.API.Models.DTOs;

public class StudyPlanResponseDto
{
    public int PlanId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ExamTarget { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TotalDays { get; set; }
    public int TotalTopics { get; set; }
    public bool IsVectorIndexed { get; set; }
    public List<DailyScheduleDto> Schedule { get; set; } = new();
}
