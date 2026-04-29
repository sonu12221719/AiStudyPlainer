using System;

namespace AiStudyPlanner.API.Models.DTOs;
public class WeakAreaDto
{
    public int      Id               { get; set; }
    public int      TopicId          { get; set; }
    public string   TopicName        { get; set; } = string.Empty;
    public string   Subject          { get; set; } = string.Empty;
    public string   Severity         { get; set; } = string.Empty;
    public double   ScoreAtDetection { get; set; }
    public double   CurrentScore     { get; set; }
    public double?  TargetScore      { get; set; }
    public string?  AiInsight        { get; set; }
    public string?  AiRecommendation { get; set; }
    public int      HeatmapIntensity { get; set; }
    public string   Status           { get; set; } = string.Empty;
    public DateTime DetectedAt       { get; set; }
    public DateTime? ResolvedAt      { get; set; }
}
