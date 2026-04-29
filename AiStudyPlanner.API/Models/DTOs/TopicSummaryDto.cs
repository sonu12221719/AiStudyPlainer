using System;

namespace AiStudyPlanner.API.Models.DTOs;

public class TopicSummaryDto
{
    public int TopicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? Chapter { get; set; }
    public string? Description { get; set; }
    public int EstimatedMinutes { get; set; }
    public int DifficultyLevel { get; set; }
    public bool IsExamCritical { get; set; }
    public string Status { get; set; } = "Pending";
}
