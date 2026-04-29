using System;

namespace AiStudyPlanner.API.Models.DTOs.Gemini;

public class GeminiRequestDto
{
    public List<GeminiMessageDto> Contents { get; set; } = new();
    public GenerationConfigDto? GenerationConfig { get; set; }
}

public class GeminiMessageDto
{
    public string Role { get; set; } = "user";
    public List<PartDto> Parts { get; set; } = new();
}

public class GenerationConfigDto
{
    public double Temperature { get; set; } = 0.7;
    public int MaxOutputTokens { get; set; } = 1024;
    public double TopP { get; set; } = 0.9;
}
