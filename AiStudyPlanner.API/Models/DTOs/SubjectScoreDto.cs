using System;

namespace AiStudyPlanner.API.Models.DTOs;

public class SubjectScoreDto
{
    public string Subject { get; set; } = string.Empty;
    public double AverageScore { get; set; }
    public int TotalTopics { get; set; }
    public int CompletedTopics { get; set; }
    public int WeakTopicsCount { get; set; }
    public int HeatmapIntensity { get; set; }
    // 1–5, drives CSS class on Angular heatmap cells
}
