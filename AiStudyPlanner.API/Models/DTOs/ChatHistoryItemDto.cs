using System;
using System.ComponentModel.DataAnnotations;

namespace AiStudyPlanner.API.Models.DTOs;

public class ChatHistoryItemDto
{
    [Required]
    public string Role { get; set; } = string.Empty;
    // "user" or "assistant"

    [Required]
    public string Content { get; set; } = string.Empty;
}
