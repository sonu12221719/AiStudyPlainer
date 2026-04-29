using System;

namespace AiStudyPlanner.API.Models.DTOs;

public class QuizResultResponseDto
{
    public int QuizResultId { get; set; }
    public int TopicId { get; set; }
    public string TopicName { get; set; } = string.Empty;
    public double ScorePercent { get; set; }
    public int CorrectAnswers { get; set; }
    public int TotalQuestions { get; set; }
    public bool IsPassed { get; set; }
    public bool IsWeakAreaFlagged { get; set; }
    public bool RevisionScheduled { get; set; }
    public string? AiFeedback { get; set; }
    public List<string> SuggestedRevisionTopics { get; set; } = new();
    public DateTime TakenAt { get; set; }
}
