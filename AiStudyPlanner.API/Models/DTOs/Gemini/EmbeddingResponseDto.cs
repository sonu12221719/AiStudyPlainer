using System;

namespace AiStudyPlanner.API.Models.DTOs;

public class EmbeddingResponseDto
{
    public EmbeddingObjectDto Embedding { get; set; } = null!;
}

public class EmbeddingObjectDto
{
    public float[] Values { get; set; } = Array.Empty<float>();
}
