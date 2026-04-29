using System;

namespace AiStudyPlanner.API.Models.DTOs;

public class ProgressSummaryDto
{
    public int TotalTopics { get; set; }
    public int CompletedTopics { get; set; }
    public double CompletionPercent { get; set; }
    public int ActiveWeakAreas { get; set; }
    public int ResolvedWeakAreas { get; set; }
    public double OverallAverageScore { get; set; }
    public int CurrentStreak { get; set; }
    // consecutive days the student studied
    public List<SubjectScoreDto> SubjectBreakdown { get; set; } = new();
}
