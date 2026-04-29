using System;

namespace AiStudyPlanner.API.Models.DTOs;

public class ChatResponseDto
{
    public string Answer { get; set; } = string.Empty;

    public List<string> RetrievedChunks { get; set; } = new();
    // the top-K syllabus chunks used to answer (optional — for debug UI)

    public List<string> SourceTopics { get; set; } = new();
    // topic names the answer was grounded in

    public bool UsedVectorSearch { get; set; }
    // false if Qdrant had no indexed chunks yet — fell back to direct prompt

    public DateTime RespondedAt { get; set; } = DateTime.UtcNow;
}
