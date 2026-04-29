using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiStudyPlanner.API.Interfaces;
using AiStudyPlanner.API.Models.DTOs;
using AiStudyPlanner.API.Models.DTOs.Gemini;

namespace AiStudyPlanner.API.Services;

public class GeminiService : IGeminiService
{
    private readonly HttpClient    _httpClient;
    private readonly IConfiguration _config;
    private readonly string        _apiKey;
    private readonly string        _model;
    private readonly string        _embeddingModel;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public GeminiService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient     = httpClient;
        _config         = config;
        _apiKey         = config["Gemini:ApiKey"]?? throw new InvalidOperationException("Gemini:ApiKey is missing from appsettings.json");
        _model          = config["Gemini:Model"]?? "gemini-2.0-flash";
        _embeddingModel = config["Gemini:EmbeddingModel"]?? "text-embedding-004";
    }

    // ══════════════════════════════════════════════════════════════
    // CORE — GENERATE CONTENT
    // ══════════════════════════════════════════════════════════════
    public async Task<string> GenerateContentAsync(string prompt)
    {
        var url  = BuildGenerateUrl();
        var body = BuildSingleTurnRequest(prompt);

        var response = await PostAsync<GeminiResponseDto>(url, body);

        return ExtractText(response)
            ?? throw new InvalidOperationException(
                "Gemini returned an empty response.");
    }

    // ══════════════════════════════════════════════════════════════
    // STUDY PLAN GENERATION
    // ══════════════════════════════════════════════════════════════
    public async Task<string> GenerateStudyPlanAsync(string syllabusText, string examTarget, int totalDays,int dailyHours)
    {
        var prompt = $$"""
            You are an expert academic study planner.

            Exam target     : {{examTarget}}
            Available days  : {{totalDays}}
            Daily study hours: {{dailyHours}}

            Syllabus: {{syllabusText}}

            Generate a complete day-by-day study plan.

            Rules:
            - Distribute topics evenly across all {{totalDays}} days
            - Each day should have topics totalling ~{{dailyHours * 60}} minutes
            - Order topics from foundational to advanced within each subject
            - Mark high-weightage exam topics as isExamCritical: true
            - Set difficultyLevel: 1 (easy), 2 (medium), or 3 (hard)

            Return ONLY a valid JSON array. No explanation. No markdown fences.
            Each element must follow this exact shape:
            {
              "dayNumber": 1,
              "topics": [
                {
                  "name": "Topic name",
                  "subject": "Subject name",
                  "chapter": "Chapter name or null",
                  "description": "One paragraph summary of what to study",
                  "estimatedMinutes": 60,
                  "difficultyLevel": 2,
                  "isExamCritical": false
                }
              ]
            }
            """;

        var url  = BuildGenerateUrl();
        var body = BuildSingleTurnRequest(prompt, temperature: 0.3);
        // Lower temperature for structured output — less creative variance

        var response = await PostAsync<GeminiResponseDto>(url, body);
        var raw = ExtractText(response)?? 
            throw new InvalidOperationException("Gemini returned empty plan.");

        // Strip markdown fences Gemini sometimes adds despite instructions
        return StripMarkdownFences(raw);
    }

    // ══════════════════════════════════════════════════════════════
    // DAILY SUMMARY
    // ══════════════════════════════════════════════════════════════
    public async Task<string> GenerateDailySummaryAsync(
        List<string> topicNames)
    {
        var topics = string.Join(", ", topicNames);

        var prompt = $"""
            A student is about to study these topics today: {topics}

            Write 2 sentences:
            1. What they will learn
            2. Why it matters for their exam

            Keep it encouraging and under 50 words total.
            """;

        return await GenerateContentAsync(prompt);
    }

    // ══════════════════════════════════════════════════════════════
    // DOUBT ANSWERING WITH RAG CONTEXT
    // ══════════════════════════════════════════════════════════════
    public async Task<string> AnswerDoubtAsync(
        string                  question,
        List<string>            relevantChunks,
        List<string>            weakAreas,
        string                  currentTopic,
        List<ChatHistoryItemDto> history)
    {
        var context  = relevantChunks.Any()
            ? string.Join("\n\n---\n\n", relevantChunks)
            : "No specific syllabus context available.";

        var weakness = weakAreas.Any()
            ? string.Join(", ", weakAreas)
            : "none identified yet";

        var systemPrompt = $"""
            You are a patient, friendly study tutor helping a student prepare for an exam.

            === Relevant syllabus content ===
            {context}
            =================================

            Student's current topic : {currentTopic}
            Student's weak areas    : {weakness}

            Instructions:
            - Answer clearly in simple numbered steps
            - Use examples where helpful
            - If the answer is in the syllabus content above, reference it directly
            - If the question is outside the syllabus, say so politely
            - Keep answers under 300 words
            """;

        var url  = BuildGenerateUrl();
        var body = BuildMultiTurnRequest(
            systemPrompt, question, history, temperature: 0.7);

        var response = await PostAsync<GeminiResponseDto>(url, body);

        return ExtractText(response)
            ?? "I could not generate an answer. Please try again.";
    }

    // ══════════════════════════════════════════════════════════════
    // QUIZ FEEDBACK
    // ══════════════════════════════════════════════════════════════
    public async Task<string> GenerateQuizFeedbackAsync(
        string                  topicName,
        List<QuizAnswerDetailDto> answers)
    {
        var wrongAnswers = answers
            .Where(a => !a.IsCorrect)
            .ToList();

        if (!wrongAnswers.Any())
            return "Perfect score! All answers were correct. " +
                   "Move on to the next topic with confidence.";

        var wrongSummary = string.Join("\n", wrongAnswers.Select(a =>
            $"Q: {a.QuestionText}\n" +
            $"   Student answered : {a.SelectedOption}\n" +
            $"   Correct answer   : {a.CorrectOption}"));

        var prompt = $"""
            A student completed a quiz on: {topicName}
            Score: {answers.Count(a => a.IsCorrect)}/{answers.Count}

            Questions answered incorrectly:
            {wrongSummary}

            Write 2-3 sentences of specific, encouraging feedback.
            - Name the exact concept gap
            - Do NOT repeat the questions back
            - End with one actionable tip
            """;

        return await GenerateContentAsync(prompt);
    }

    // ══════════════════════════════════════════════════════════════
    // WEAK AREA INSIGHT
    // ══════════════════════════════════════════════════════════════
    public async Task<string> GenerateWeakAreaInsightAsync(
        string                   topicName,
        double                   scorePercent,
        List<QuizAnswerDetailDto> wrongAnswers)
    {
        var wrongList = string.Join("\n", wrongAnswers.Select(a =>
            $"- {a.QuestionText} " +
            $"(answered: {a.SelectedOption}, " +
            $"correct: {a.CorrectOption})"));

        var prompt = $"""
            Topic   : {topicName}
            Score   : {scorePercent:F1}%
            Status  : Weak area detected

            Wrong answers:
            {wrongList}

            In exactly 1-2 sentences, diagnose the specific concept gap.
            Be precise — name the sub-concept, not just the topic name.
            Example format: "Student consistently misses questions about
            [specific sub-concept] — likely confusing it with [related concept]."
            """;

        return await GenerateContentAsync(prompt);
    }

    // ══════════════════════════════════════════════════════════════
    // WEAK AREA RECOMMENDATION
    // ══════════════════════════════════════════════════════════════
    public async Task<string> GenerateWeakAreaRecommendationAsync(
        string topicName,
        string subject)
    {
        var prompt = $"""
            A student is struggling with: {topicName} ({subject})

            Give a 2-step action plan:
            Step 1: Exactly what to re-read or review (be specific)
            Step 2: What type of practice problems to attempt

            Keep it under 3 sentences total.
            Do not use bullet points — write as two numbered sentences.
            """;

        return await GenerateContentAsync(prompt);
    }

    // ══════════════════════════════════════════════════════════════
    // EMBEDDINGS — for RAG / Qdrant vector search
    // ══════════════════════════════════════════════════════════════
    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<float>();

        var url = $"https://generativelanguage.googleapis.com/v1beta/" +
                  $"models/{_embeddingModel}:embedContent" +
                  $"?key={_apiKey}";

        var body = new EmbeddingRequestDto
        {
            Model   = $"models/{_embeddingModel}",
            Content = new EmbeddingContentDto
            {
                Parts = new List<PartDto>
                {
                    new() { Text = text }
                }
            }
        };

        var json     = JsonSerializer.Serialize(body, JsonOptions);
        var content  = new StringContent(
            json, Encoding.UTF8, "application/json");

        var httpResponse = await _httpClient.PostAsync(url, content);
        await EnsureSuccessAsync(httpResponse);

        var result = await httpResponse.Content
            .ReadFromJsonAsync<EmbeddingResponseDto>(JsonOptions);

        return result?.Embedding?.Values
            ?? Array.Empty<float>();
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — POST HELPER
    // ══════════════════════════════════════════════════════════════
    private async Task<T> PostAsync<T>(string url, object body)
    where T : class
    {
        var json    = JsonSerializer.Serialize(body, JsonOptions);
        
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var content  = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);

            // Handle 429 BEFORE EnsureSuccessAsync
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                if (attempt == 3)
                    throw new InvalidOperationException(
                        "Gemini rate limit exceeded after 3 retries. Try again later.");

                // _logger.LogWarning(
                //     "Gemini 429 rate limit hit. Waiting 60s... (attempt {Attempt}/3)", attempt);

                await Task.Delay(TimeSpan.FromSeconds(60));
                continue;
            }

            await EnsureSuccessAsync(response);

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialise Gemini response.");
        }

        throw new InvalidOperationException("Gemini request failed after 3 retries.");
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — BUILD URLS
    // ══════════════════════════════════════════════════════════════
    private string BuildGenerateUrl() =>
        $"https://generativelanguage.googleapis.com/v1beta/" +$"models/{_model}:generateContent" +$"?key={_apiKey}";

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — BUILD REQUEST BODIES
    // ══════════════════════════════════════════════════════════════
    private static GeminiRequestDto BuildSingleTurnRequest(
        string prompt,
        double temperature    = 0.7,
        int    maxOutputTokens = 2048)
    {
        return new GeminiRequestDto
        {
            Contents = new List<GeminiMessageDto>
            {
                new()
                {
                    Role  = "user",
                    Parts = new List<PartDto>
                    {
                        new() { Text = prompt }
                    }
                }
            },
            GenerationConfig = new GenerationConfigDto
            {
                Temperature     = temperature,
                MaxOutputTokens = maxOutputTokens,
                TopP            = 0.9
            }
        };
    }

    private static GeminiRequestDto BuildMultiTurnRequest(
        string                   systemPrompt,
        string                   userMessage,
        List<ChatHistoryItemDto> history,
        double                   temperature     = 0.7,
        int                      maxOutputTokens = 1024)
    {
        var contents = new List<GeminiMessageDto>();

        // Gemini does not have a dedicated system role —
        // inject system prompt as the first user turn
        contents.Add(new GeminiMessageDto
        {
            Role  = "user",
            Parts = new List<PartDto>
            {
                new() { Text = systemPrompt }
            }
        });

        // Add a model acknowledgement so Gemini
        // treats the system prompt as already accepted
        contents.Add(new GeminiMessageDto
        {
            Role  = "model",
            Parts = new List<PartDto>
            {
                new() { Text = "Understood. I am ready to help." }
            }
        });

        // Inject last 6 history turns (3 pairs)
        // to keep within context window limits
        foreach (var item in history.TakeLast(6))
        {
            // Gemini expects "user" or "model" — not "assistant"
            var role = item.Role.ToLower() == "assistant"
                ? "model"
                : "user";

            contents.Add(new GeminiMessageDto
            {
                Role  = role,
                Parts = new List<PartDto>
                {
                    new() { Text = item.Content }
                }
            });
        }

        // Add the current user message last
        contents.Add(new GeminiMessageDto
        {
            Role  = "user",
            Parts = new List<PartDto>
            {
                new() { Text = userMessage }
            }
        });

        return new GeminiRequestDto
        {
            Contents = contents,
            GenerationConfig = new GenerationConfigDto
            {
                Temperature     = temperature,
                MaxOutputTokens = maxOutputTokens,
                TopP            = 0.9
            }
        };
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — EXTRACT TEXT FROM RESPONSE
    // ══════════════════════════════════════════════════════════════
    private static string? ExtractText(GeminiResponseDto? response)
    {
        return response?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — ENSURE SUCCESS WITH READABLE ERROR
    // ══════════════════════════════════════════════════════════════
    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync();

        throw response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                new UnauthorizedAccessException(
                    "Gemini API key is invalid or missing. " +
                    "Check Gemini:ApiKey in appsettings.json."),

            System.Net.HttpStatusCode.TooManyRequests =>
                new InvalidOperationException(
                    "Gemini API rate limit reached. " +
                    "Wait a moment and try again."),

            System.Net.HttpStatusCode.BadRequest =>
                new InvalidOperationException(
                    $"Gemini rejected the request: {body}"),

            _ => new HttpRequestException(
                $"Gemini API error {(int)response.StatusCode}: {body}")
        };
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — STRIP MARKDOWN FENCES FROM JSON
    // ══════════════════════════════════════════════════════════════
    private static string StripMarkdownFences(string raw)
    {
        return raw
            .Replace("```json", "")
            .Replace("```",     "")
            .Trim();
    }
}