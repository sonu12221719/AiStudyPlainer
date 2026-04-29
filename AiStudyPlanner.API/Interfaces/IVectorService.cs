using System;

namespace AiStudyPlanner.API.Interfaces;

public interface IVectorService
{
    // ── Indexing ───────────────────────────────────────────────────
    Task IndexSyllabusAsync(int userId, int studyPlanId, string fullText);

    Task ReIndexSyllabusAsync(int userId, int studyPlanId);
    // re-index from SQL without re-upload — used after model upgrade

    Task IndexSingleChunkAsync(int userId, int studyPlanId,
        string chunkText, int chunkIndex, string? topicName);
    // used for retry of failed individual chunks

    // ── Search ─────────────────────────────────────────────────────
    Task<List<string>> SearchSimilarChunksAsync(int userId,
        string query, int topK = 3);

    Task<List<string>> SearchByTopicAsync(int userId,
        string query, string topicName, int topK = 3);
    // filtered search — only returns chunks tagged with topicName

    // ── Maintenance ────────────────────────────────────────────────
    Task DeleteUserCollectionAsync(int userId);
    // called when student deletes their plan

    Task<bool> CollectionExistsAsync(int userId);
    // check before searching — avoids Qdrant error on missing collection

    Task<int> GetIndexedChunkCountAsync(int userId, int studyPlanId);
    // how many chunks are indexed — for status display

    Task RetryFailedChunksAsync(int userId, int studyPlanId);
    // picks up SyllabusChunks where IsIndexed = false and retries
}
