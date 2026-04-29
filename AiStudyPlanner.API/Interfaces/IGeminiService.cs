using System;
using AiStudyPlanner.API.Models.DTOs;

namespace AiStudyPlanner.API.Interfaces;

public interface IGeminiService
{
    // ── Content generation ─────────────────────────────────────────
    Task<string> GenerateContentAsync(string prompt);
    Task<string> GenerateStudyPlanAsync(string syllabusText, string examTarget, int totalDays, int dailyHours);
    Task<string> GenerateDailySummaryAsync(List<string> topicNames);

    // ── Chat / doubt solving ───────────────────────────────────────
    Task<string> AnswerDoubtAsync(string question, List<string> relevantChunks, List<string> weakAreas, string currentTopic, List<ChatHistoryItemDto> history);

    // ── Quiz feedback ──────────────────────────────────────────────
    Task<string> GenerateQuizFeedbackAsync(string topicName, List<QuizAnswerDetailDto> answers);

    Task<string> GenerateWeakAreaInsightAsync(string topicName, double scorePercent, List<QuizAnswerDetailDto> wrongAnswers);

    Task<string> GenerateWeakAreaRecommendationAsync(string topicName,
        string subject);

    // ── Embeddings (NEW — for RAG / Qdrant) ───────────────────────
    Task<float[]> GetEmbeddingAsync(string text);
}
