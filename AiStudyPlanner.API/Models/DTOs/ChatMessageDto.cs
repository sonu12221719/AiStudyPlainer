using System;
using System.ComponentModel.DataAnnotations;

namespace AiStudyPlanner.API.Models.DTOs;

public class ChatMessageDto
{
    [MinLength(1)]
    [MaxLength(2000)]
    public required string Message { get; set; } = string.Empty;

    public int? TopicId { get; set; }
    // optional — if provided, Qdrant search filters by this topic
    // for more focused RAG retrieval

    public int? StudyPlanId { get; set; }
    // which plan's vector collection to search in

    public List<ChatHistoryItemDto> History { get; set; } = new();
    // last N messages sent from Angular for multi-turn context
    // keep to last 6–8 messages to stay within Gemini context window
}
