using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AiStudyPlanner.API.Interfaces;
using AiStudyPlanner.API.Models.DTOs;
using System.Security.Claims;

namespace AiStudyPlanner.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly IGeminiService   _gemini;
    private readonly IVectorService   _vector;
    private readonly IPlannerService  _planner;
    private readonly IProgressService _progress;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IGeminiService          gemini,
        IVectorService          vector,
        IPlannerService         planner,
        IProgressService        progress,
        ILogger<ChatController> logger)
    {
        _gemini   = gemini;
        _vector   = vector;
        _planner  = planner;
        _progress = progress;
        _logger   = logger;
    }

    // ─── POST api/chat/message ─────────────────────────────────────
    /// <summary>
    /// Send a doubt question.
    /// Retrieves relevant syllabus chunks from Qdrant (RAG),
    /// injects student context, and returns Gemini's answer.
    /// </summary>
    [HttpPost("message")]
    [ProducesResponseType(typeof(ChatResponseDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 400)]
    public async Task<IActionResult> SendMessage(
        [FromBody] ChatMessageDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(BuildValidationError());

        if (string.IsNullOrWhiteSpace(dto.Message))
            return BadRequest(new ErrorResponseDto
            {
                Message = "Message cannot be empty."
            });

        var userId = GetUserId();

        // ── Step 1: Retrieve relevant syllabus chunks from Qdrant ──
        List<string> relevantChunks;
        bool         usedVectorSearch;

        var vectorReady = await _vector.CollectionExistsAsync(userId);

        if (vectorReady)
        {
            // Use topic-filtered search if topicId is provided
            // otherwise fall back to full-syllabus search
            relevantChunks = dto.TopicId.HasValue
                ? await RetrieveByTopicAsync(
                    userId, dto.Message, dto.TopicId.Value)
                : await _vector.SearchSimilarChunksAsync(
                    userId, dto.Message, topK: 3);

            usedVectorSearch = true;

            _logger.LogInformation(
                "RAG: retrieved {Count} chunks for user {UserId}",
                relevantChunks.Count, userId);
        }
        else
        {
            // Qdrant not ready yet — indexing still in progress
            // Fall back to direct prompt without RAG context
            relevantChunks   = new List<string>();
            usedVectorSearch = false;

            _logger.LogWarning(
                "RAG: Qdrant collection not ready for user {UserId}. " +
                "Falling back to direct prompt.", userId);
        }

        // ── Step 2: Load student context from SQL ──────────────────
        var currentTopic = await _planner
            .GetTodaysTopicNameAsync(userId);

        var weakAreas = await _progress
            .GetWeakAreaTopicNamesAsync(userId);

        // ── Step 3: Answer the doubt via Gemini ────────────────────
        string answer;
        try
        {
            answer = await _gemini.AnswerDoubtAsync(
                question:       dto.Message,
                relevantChunks: relevantChunks,
                weakAreas:      weakAreas,
                currentTopic:   currentTopic,
                history:        dto.History);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Gemini AnswerDoubtAsync failed for user {UserId}",
                userId);

            return StatusCode(503, new ErrorResponseDto
            {
                Message =
                    "AI service is temporarily unavailable. " +
                    "Please try again in a moment."
            });
        }

        // ── Step 4: Extract source topic names from chunks ─────────
        var sourceTopics = ExtractSourceTopics(relevantChunks);

        _logger.LogInformation(
            "Chat response generated for user {UserId}. " +
            "UsedRAG: {UsedRAG}, Sources: {Sources}",
            userId, usedVectorSearch,
            string.Join(", ", sourceTopics));

        return Ok(new ChatResponseDto
        {
            Answer           = answer,
            RetrievedChunks  = relevantChunks,
            SourceTopics     = sourceTopics,
            UsedVectorSearch = usedVectorSearch,
            RespondedAt      = DateTime.UtcNow
        });
    }

    // ─── POST api/chat/explain ─────────────────────────────────────
    /// <summary>
    /// Ask Gemini to explain a specific topic in simple terms.
    /// No RAG — uses topic name directly as context.
    /// </summary>
    [HttpPost("explain")]
    [ProducesResponseType(typeof(ChatResponseDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 400)]
    public async Task<IActionResult> ExplainTopic(
        [FromBody] ExplainRequestDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(BuildValidationError());

        var userId = GetUserId();

        // Get relevant chunks for this specific topic
        var relevantChunks = new List<string>();
        var vectorReady    = await _vector.CollectionExistsAsync(userId);

        if (vectorReady)
        {
            relevantChunks = await _vector.SearchSimilarChunksAsync(
                userId,
                query: $"explain {dto.TopicName} concepts examples",
                topK:  4);
        }

        var weakAreas    = await _progress
            .GetWeakAreaTopicNamesAsync(userId);

        var prompt = BuildExplainPrompt(
            dto.TopicName, dto.DetailLevel, relevantChunks);

        string answer;
        try
        {
            answer = await _gemini.GenerateContentAsync(prompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Gemini explain failed for topic {TopicName}",
                dto.TopicName);

            return StatusCode(503, new ErrorResponseDto
            {
                Message = "AI service is temporarily unavailable."
            });
        }

        return Ok(new ChatResponseDto
        {
            Answer           = answer,
            RetrievedChunks  = relevantChunks,
            SourceTopics     = new List<string> { dto.TopicName },
            UsedVectorSearch = vectorReady,
            RespondedAt      = DateTime.UtcNow
        });
    }

    // ─── POST api/chat/summarise ───────────────────────────────────
    /// <summary>
    /// Generate a quick revision summary for a topic.
    /// Retrieves topic chunks from Qdrant and summarises them.
    /// </summary>
    [HttpPost("summarise")]
    [ProducesResponseType(typeof(ChatResponseDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 400)]
    public async Task<IActionResult> SummariseTopic(
        [FromBody] SummariseRequestDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(BuildValidationError());

        var userId      = GetUserId();
        var vectorReady = await _vector.CollectionExistsAsync(userId);

        var relevantChunks = new List<string>();

        if (vectorReady)
        {
            // Fetch more chunks for summarisation (topK = 5)
            relevantChunks = await _vector.SearchSimilarChunksAsync(
                userId,
                query: dto.TopicName,
                topK:  5);
        }

        var context = relevantChunks.Any()
            ? string.Join("\n\n", relevantChunks)
            : $"Topic: {dto.TopicName}";

        var prompt = $"""
            Summarise the following study content for quick revision.

            Content:
            {context}

            Rules:
            - Maximum {dto.MaxPoints} bullet points
            - Each point must be one clear sentence
            - Focus on exam-important concepts only
            - End with one memory tip or mnemonic if possible
            """;

        string answer;
        try
        {
            answer = await _gemini.GenerateContentAsync(prompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Gemini summarise failed for topic {TopicName}",
                dto.TopicName);

            return StatusCode(503, new ErrorResponseDto
            {
                Message = "AI service is temporarily unavailable."
            });
        }

        return Ok(new ChatResponseDto
        {
            Answer           = answer,
            RetrievedChunks  = relevantChunks,
            SourceTopics     = new List<string> { dto.TopicName },
            UsedVectorSearch = vectorReady,
            RespondedAt      = DateTime.UtcNow
        });
    }

    // ─── POST api/chat/generate-quiz ───────────────────────────────
    /// <summary>
    /// Ask Gemini to generate quiz questions for a topic.
    /// Retrieves topic chunks from Qdrant as source material.
    /// </summary>
    [HttpPost("generate-quiz")]
    [ProducesResponseType(typeof(GeneratedQuizDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 400)]
    public async Task<IActionResult> GenerateQuiz(
        [FromBody] GenerateQuizRequestDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(BuildValidationError());

        var userId      = GetUserId();
        var vectorReady = await _vector.CollectionExistsAsync(userId);

        var relevantChunks = new List<string>();

        if (vectorReady)
        {
            relevantChunks = await _vector.SearchSimilarChunksAsync(
                userId,
                query: dto.TopicName,
                topK:  5);
        }

        var context = relevantChunks.Any()
            ? string.Join("\n\n", relevantChunks)
            : $"Topic: {dto.TopicName}";

        var prompt = $$"""
            Generate {{dto.QuestionCount}} multiple choice questions
            about: {{dto.TopicName}}

            Source material:
            {context}

            Rules:
            - Difficulty: {dto.Difficulty} (easy/medium/hard)
            - Each question must have exactly 4 options (A, B, C, D)
            - Clearly mark the correct answer
            - Add a one-sentence explanation for the correct answer

            Return ONLY valid JSON array. No markdown. No explanation.
            Each item must follow this exact shape:
            {
              "questionNumber": 1,
              "questionText":   "Question here?",
              "options": {
                "A": "Option A",
                "B": "Option B",
                "C": "Option C",
                "D": "Option D"
              },
              "correctOption":  "A",
              "explanation":    "Because..."
            }
            """;

        string raw;
        try
        {
            raw = await _gemini.GenerateContentAsync(prompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Gemini generate-quiz failed for topic {TopicName}",
                dto.TopicName);

            return StatusCode(503, new ErrorResponseDto
            {
                Message = "AI service is temporarily unavailable."
            });
        }

        // Parse and return structured quiz
        var questions = ParseGeneratedQuiz(raw);

        return Ok(new GeneratedQuizDto
        {
            TopicName  = dto.TopicName,
            Questions  = questions,
            GeneratedAt = DateTime.UtcNow
        });
    }

    // ─── GET api/chat/history ──────────────────────────────────────
    /// <summary>
    /// Returns a reminder that chat history is managed
    /// client-side in Angular and sent with each request.
    /// </summary>
    [HttpGet("history")]
    [ProducesResponseType(200)]
    public IActionResult GetHistoryInfo()
    {
        return Ok(new
        {
            message =
                "Chat history is stateless on the server. " +
                "Send the last 6-8 messages in the 'history' " +
                "field of each ChatMessageDto request.",
            maxHistoryItems = 8
        });
    }

    // ─── Private helpers ───────────────────────────────────────────
    private async Task<List<string>> RetrieveByTopicAsync(
        int    userId,
        string query,
        int    topicId)
    {
        // Get topic name from DB for Qdrant filter
        var topic = await _planner.GetTopicAsync(topicId);

        if (topic is null)
            return await _vector.SearchSimilarChunksAsync(
                userId, query, topK: 3);

        // Try topic-filtered search first
        var chunks = await _vector.SearchByTopicAsync(
            userId,
            query,
            topicName: topic.Name,
            topK:      3);

        // Fall back to unfiltered if no topic chunks found
        if (!chunks.Any())
            chunks = await _vector.SearchSimilarChunksAsync(
                userId, query, topK: 3);

        return chunks;
    }

    private static string BuildExplainPrompt(
        string       topicName,
        string       detailLevel,
        List<string> chunks)
    {
        var context = chunks.Any()
            ? $"\n\nRelevant content:\n{string.Join("\n\n", chunks)}"
            : string.Empty;

        return detailLevel.ToLower() switch
        {
            "simple" => $"""
                Explain '{topicName}' as if teaching a beginner.
                {context}
                Use an analogy. Keep it under 150 words.
                """,

            "detailed" => $"""
                Give a thorough explanation of '{topicName}'.
                {context}
                Cover: definition, key concepts, examples,
                common mistakes, and exam tips.
                Use numbered sections.
                """,

            _ => $"""
                Explain '{topicName}' clearly in simple steps.
                {context}
                Include one example. Keep it under 200 words.
                """
        };
    }

    private static List<string> ExtractSourceTopics(
        List<string> chunks)
    {
        // In production, store topic tags in the chunk payload
        // and return them from SearchAsync.
        // For now return a single placeholder.
        return chunks.Any()
            ? new List<string> { "Syllabus content" }
            : new List<string>();
    }

    private static List<GeneratedQuestionDto> ParseGeneratedQuiz(
        string raw)
    {
        try
        {
            var cleaned = raw
                .Replace("```json", "")
                .Replace("```",     "")
                .Trim();

            return System.Text.Json.JsonSerializer
                .Deserialize<List<GeneratedQuestionDto>>(
                    cleaned,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    })
                ?? new List<GeneratedQuestionDto>();
        }
        catch (Exception)
        {
            // Return empty list if Gemini returned malformed JSON
            return new List<GeneratedQuestionDto>();
        }
    }

    private int GetUserId()
    {
        var claim = User.FindFirstValue(
            ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException(
                "User ID not found in token.");
        return int.Parse(claim);
    }

    private ErrorResponseDto BuildValidationError()
    {
        var errors = ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .ToList();

        return new ErrorResponseDto
        {
            Message = "Validation failed.",
            Errors  = errors
        };
    }
}

// ─── Controller-specific DTOs ──────────────────────────────────────

public class ExplainRequestDto
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string TopicName   { get; set; } = string.Empty;

    // "simple" | "medium" | "detailed"
    public string DetailLevel { get; set; } = "medium";
}

public class SummariseRequestDto
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string TopicName { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Range(3, 10)]
    public int MaxPoints { get; set; } = 5;
}

public class GenerateQuizRequestDto
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string TopicName     { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Range(3, 20)]
    public int    QuestionCount { get; set; } = 5;

    // "easy" | "medium" | "hard"
    public string Difficulty    { get; set; } = "medium";
}

public class GeneratedQuizDto
{
    public string                    TopicName   { get; set; }
        = string.Empty;
    public List<GeneratedQuestionDto> Questions  { get; set; }
        = new();
    public DateTime                  GeneratedAt { get; set; }
}

public class GeneratedQuestionDto
{
    public int                      QuestionNumber { get; set; }
    public string                   QuestionText   { get; set; }
        = string.Empty;
    public Dictionary<string,string> Options       { get; set; }
        = new();
    public string                   CorrectOption  { get; set; }
        = string.Empty;
    public string                   Explanation    { get; set; }
        = string.Empty;
}