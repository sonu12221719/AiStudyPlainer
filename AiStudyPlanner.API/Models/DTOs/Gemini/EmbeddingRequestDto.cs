using System;

namespace AiStudyPlanner.API.Models.DTOs.Gemini;

public class EmbeddingRequestDto
{
    public string Model { get; set; } = "models/text-embedding-004";
    public EmbeddingContentDto Content { get; set; } = null!;
}

public class EmbeddingContentDto
{
    public List<PartDto> Parts { get; set; } = new();
}
