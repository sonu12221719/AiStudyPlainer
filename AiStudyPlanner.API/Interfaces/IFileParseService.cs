using AiStudyPlanner.API.Services;

namespace AiStudyPlanner.API.Interfaces;

public interface IFileParserService
{
    Task<string> ExtractTextAsync(IFormFile file);
    List<DetectedTopic> DetectTopics(string fullText);
    List<TextChunk> ChunkText(string fullText, int chunkSize = 2000, int overlap   = 200);
    Task<string> SaveFileAsync(IFormFile file, string? subFolder = null);
    void ValidateFile(IFormFile file);
}