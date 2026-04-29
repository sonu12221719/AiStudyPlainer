using System;

namespace AiStudyPlanner.API.Models.DTOs.Gemini;

public class GeminiResponseDto
{
    public List<CandidateDto> Candidates { get; set; } = new();
}

public class CandidateDto
{
    public ContentDto Content { get; set; } = null!;
    public string? FinishReason { get; set; }
}

public class ContentDto
{
    public List<PartDto> Parts { get; set; } = new();
    public string Role { get; set; } = string.Empty;
}

public class PartDto
{
    public string Text { get; set; } = string.Empty;
}