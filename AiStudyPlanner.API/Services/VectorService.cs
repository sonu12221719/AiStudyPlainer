using Microsoft.EntityFrameworkCore;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using AiStudyPlanner.API.Data;
using AiStudyPlanner.API.Interfaces;
using AiStudyPlanner.API.Models.Entities;

namespace AiStudyPlanner.API.Services;

public class VectorService : IVectorService
{
    private readonly QdrantClient _qdrant;
    private readonly IGeminiService _gemini;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<VectorService> _logger;

    // ── Constants ──────────────────────────────────────────────────
    private const int   VectorSize      = 768;   // text-embedding-004
    private const float ScoreThreshold  = 0.5f;  // min cosine similarity
    private const int   MaxRetryAttempts = 3;
    private const int   ChunkSize       = 2000;  // characters per chunk
    private const int   ChunkOverlap    = 200;   // overlap between chunks

    public VectorService(QdrantClient qdrant, IGeminiService gemini, AppDbContext db,IConfiguration config, ILogger<VectorService> logger)
    {
        _qdrant = qdrant;
        _gemini = gemini;
        _db     = db;
        _config = config;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    // INDEX FULL SYLLABUS
    // Chunks text → embeds each chunk → upserts into Qdrant
    // Saves each chunk record to SQL for audit + retry
    // ══════════════════════════════════════════════════════════════
    public async Task IndexSyllabusAsync(
        int    userId,
        int    studyPlanId,
        string fullText)
    {
        if (string.IsNullOrWhiteSpace(fullText))
        {
            _logger.LogWarning(
                "IndexSyllabusAsync called with empty text " +
                "for plan {PlanId}", studyPlanId);
            return;
        }

        var collection = CollectionName(userId);
        await EnsureCollectionAsync(collection);

        var chunks = SplitIntoChunks(fullText);

        _logger.LogInformation(
            "Indexing {ChunkCount} chunks for plan {PlanId} " +
            "into collection {Collection}",
            chunks.Count, studyPlanId, collection);

        // Remove any previously indexed chunks for this plan
        // (handles re-upload scenario)
        await DeletePlanChunksFromSqlAsync(studyPlanId);

        int successCount = 0;
        int failCount    = 0;

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];

            // 1. Save chunk record to SQL first
            //    so failed chunks are trackable for retry
            var chunkRecord = await SaveChunkToSqlAsync(
                userId, studyPlanId, chunk, i, collection);

            // 2. Embed + upsert into Qdrant
            var success = await IndexSingleChunkInternalAsync(
                chunkRecord, collection);

            if (success) successCount++;
            else         failCount++;
        }

        // 3. Mark plan as indexed if at least some chunks succeeded
        if (successCount > 0)
        {
            await MarkPlanAsIndexedAsync(studyPlanId);
        }

        _logger.LogInformation(
            "Indexing complete for plan {PlanId}. " +
            "Success: {Success}, Failed: {Failed}",
            studyPlanId, successCount, failCount);
    }

    // ══════════════════════════════════════════════════════════════
    // RE-INDEX FROM SQL
    // Reads RawSyllabusText from DB and re-indexes without re-upload
    // Called after embedding model upgrade or collection corruption
    // ══════════════════════════════════════════════════════════════
    public async Task ReIndexSyllabusAsync(
        int userId,
        int studyPlanId)
    {
        var plan = await _db.StudyPlans
            .FirstOrDefaultAsync(p => p.Id == studyPlanId)
            ?? throw new KeyNotFoundException(
                $"Plan {studyPlanId} not found.");

        if (string.IsNullOrWhiteSpace(plan.RawSyllabusText))
            throw new InvalidOperationException(
                $"Plan {studyPlanId} has no stored syllabus text " +
                "to re-index from.");

        _logger.LogInformation(
            "Re-indexing plan {PlanId} for user {UserId}",
            studyPlanId, userId);

        // Delete old Qdrant collection and SQL chunk records
        await DeleteUserCollectionAsync(userId);
        await DeletePlanChunksFromSqlAsync(studyPlanId);

        // Reset vector indexed flag
        plan.IsVectorIndexed = false;
        await _db.SaveChangesAsync();

        // Re-index from stored text
        await IndexSyllabusAsync(
            userId, studyPlanId, plan.RawSyllabusText);
    }

    // ══════════════════════════════════════════════════════════════
    // INDEX SINGLE CHUNK (called by retry job)
    // ══════════════════════════════════════════════════════════════
    public async Task IndexSingleChunkAsync(
        int     userId,
        int     studyPlanId,
        string  chunkText,
        int     chunkIndex,
        string? topicName)
    {
        var collection = CollectionName(userId);
        await EnsureCollectionAsync(collection);

        var pointId  = BuildPointId(studyPlanId, chunkIndex);

        // Find existing SQL record or create one
        var chunkRecord = await _db.SyllabusChunks
            .FirstOrDefaultAsync(c =>
                c.StudyPlanId == studyPlanId
             && c.ChunkIndex  == chunkIndex);

        if (chunkRecord is null)
        {
            chunkRecord = new SyllabusChunk
            {
                UserId             = userId,
                StudyPlanId        = studyPlanId,
                ChunkIndex         = chunkIndex,
                ChunkText          = chunkText,
                ChunkLength        = chunkText.Length,
                TopicName          = topicName,
                QdrantPointId      = pointId,
                QdrantCollection   = collection,
                EmbeddingModel     = "text-embedding-004",
                EmbeddingDimension = VectorSize,
                IndexAttempts      = 0,
                CreatedAt          = DateTime.UtcNow
            };

            _db.SyllabusChunks.Add(chunkRecord);
            await _db.SaveChangesAsync();
        }

        await IndexSingleChunkInternalAsync(chunkRecord, collection);
    }

    // ══════════════════════════════════════════════════════════════
    // SEARCH SIMILAR CHUNKS (full syllabus search)
    // ══════════════════════════════════════════════════════════════
    public async Task<List<string>> SearchSimilarChunksAsync(
        int    userId,
        string query,
        int    topK = 3)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<string>();

        var collection = CollectionName(userId);

        if (!await CollectionExistsAsync(userId))
        {
            _logger.LogWarning(
                "SearchSimilarChunksAsync: collection {Collection} " +
                "does not exist for user {UserId}",
                collection, userId);
            return new List<string>();
        }

        // 1. Embed the query
        var queryVector = await _gemini.GetEmbeddingAsync(query);

        if (queryVector.Length == 0)
        {
            _logger.LogWarning(
                "Empty embedding returned for query: {Query}", query);
            return new List<string>();
        }

        // 2. Search Qdrant
        var results = await _qdrant.SearchAsync(
            collectionName: collection,
            vector:         queryVector,
            limit:          (ulong)topK,
            scoreThreshold: ScoreThreshold);

        var chunks = results
            .OrderByDescending(r => r.Score)
            .Select(r => r.Payload["chunkText"].StringValue)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        // 3. Update retrieval stats in SQL
        await UpdateRetrievalStatsAsync(results);

        _logger.LogInformation(
            "SearchSimilarChunksAsync: returned {Count} chunks " +
            "for query '{Query}'",
            chunks.Count, query[..Math.Min(50, query.Length)]);

        return chunks;
    }

    // ══════════════════════════════════════════════════════════════
    // SEARCH BY TOPIC (filtered search)
    // Only returns chunks tagged with a specific topic name
    // Used when student asks doubt while on a specific topic
    // ══════════════════════════════════════════════════════════════
    public async Task<List<string>> SearchByTopicAsync(
        int    userId,
        string query,
        string topicName,
        int    topK = 3)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<string>();

        var collection = CollectionName(userId);

        if (!await CollectionExistsAsync(userId))
            return new List<string>();

        var queryVector = await _gemini.GetEmbeddingAsync(query);

        if (queryVector.Length == 0)
            return new List<string>();

        // Build Qdrant filter for specific topic
        var filter = new Filter
        {
            Must =
            {
                new Condition
                {
                    Field = new FieldCondition
                    {
                        Key   = "topicName",
                        Match = new Match
                        {
                            Text = topicName
                                .ToLower()
                                .Replace(" ", "_")
                        }
                    }
                }
            }
        };

        var results = await _qdrant.SearchAsync(
            collectionName: collection,
            vector:         queryVector,
            filter:         filter,
            limit:          (ulong)topK,
            scoreThreshold: ScoreThreshold);

        // Fall back to unfiltered search if no topic chunks found
        if (!results.Any())
        {
            _logger.LogInformation(
                "No topic-filtered results found for '{TopicName}'. " +
                "Falling back to unfiltered search.",
                topicName);

            return await SearchSimilarChunksAsync(userId, query, topK);
        }

        await UpdateRetrievalStatsAsync(results);

        return results
            .OrderByDescending(r => r.Score)
            .Select(r => r.Payload["chunkText"].StringValue)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
    }

    // ══════════════════════════════════════════════════════════════
    // DELETE USER COLLECTION
    // Called when student deletes their study plan
    // ══════════════════════════════════════════════════════════════
    public async Task DeleteUserCollectionAsync(int userId)
    {
        var collection = CollectionName(userId);

        if (!await CollectionExistsAsync(userId))
        {
            _logger.LogInformation(
                "DeleteUserCollectionAsync: collection {Collection} " +
                "does not exist — skipping",
                collection);
            return;
        }

        await _qdrant.DeleteCollectionAsync(collection);

        _logger.LogInformation(
            "Deleted Qdrant collection: {Collection}", collection);
    }

    // ══════════════════════════════════════════════════════════════
    // COLLECTION EXISTS CHECK
    // ══════════════════════════════════════════════════════════════
    public async Task<bool> CollectionExistsAsync(int userId)
    {
        try
        {
            var collections = await _qdrant.ListCollectionsAsync();
            return collections.Any(c => c == CollectionName(userId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to check Qdrant collection existence " +
                "for user {UserId}", userId);
            return false;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // GET INDEXED CHUNK COUNT
    // ══════════════════════════════════════════════════════════════
    public async Task<int> GetIndexedChunkCountAsync(
        int userId,
        int studyPlanId)
    {
        return await _db.SyllabusChunks
            .CountAsync(c =>
                c.UserId      == userId
             && c.StudyPlanId == studyPlanId
             && c.IsIndexed);
    }

    // ══════════════════════════════════════════════════════════════
    // RETRY FAILED CHUNKS
    // Picks up SyllabusChunks where IsIndexed = false
    // and IndexAttempts < MaxRetryAttempts
    // ══════════════════════════════════════════════════════════════
    public async Task RetryFailedChunksAsync(
        int userId,
        int studyPlanId)
    {
        var failedChunks = await _db.SyllabusChunks
            .Where(c =>
                c.UserId        == userId
             && c.StudyPlanId   == studyPlanId
             && !c.IsIndexed
             && c.IndexAttempts  < MaxRetryAttempts)
            .ToListAsync();

        if (!failedChunks.Any())
        {
            _logger.LogInformation(
                "RetryFailedChunksAsync: no failed chunks " +
                "found for plan {PlanId}", studyPlanId);
            return;
        }

        _logger.LogInformation(
            "Retrying {Count} failed chunks for plan {PlanId}",
            failedChunks.Count, studyPlanId);

        var collection = CollectionName(userId);
        await EnsureCollectionAsync(collection);

        int successCount = 0;

        foreach (var chunk in failedChunks)
        {
            chunk.IndexAttempts++;
            var success = await IndexSingleChunkInternalAsync(
                chunk, collection);

            if (success) successCount++;
        }

        // Re-check if plan should be marked indexed
        var totalChunks    = await _db.SyllabusChunks
            .CountAsync(c => c.StudyPlanId == studyPlanId);
        var indexedChunks  = await _db.SyllabusChunks
            .CountAsync(c => c.StudyPlanId == studyPlanId
                          && c.IsIndexed);

        if (indexedChunks == totalChunks && totalChunks > 0)
            await MarkPlanAsIndexedAsync(studyPlanId);

        _logger.LogInformation(
            "Retry complete for plan {PlanId}. " +
            "Recovered: {Count}/{Total}",
            studyPlanId, successCount, failedChunks.Count);
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — CORE CHUNK INDEX LOGIC
    // Used by IndexSyllabusAsync and RetryFailedChunksAsync
    // ══════════════════════════════════════════════════════════════
    private async Task<bool> IndexSingleChunkInternalAsync(
        SyllabusChunk chunkRecord,
        string        collection)
    {
        try
        {
            // 1. Get embedding from Gemini
            var vector = await _gemini
                .GetEmbeddingAsync(chunkRecord.ChunkText);

            if (vector.Length == 0)
                throw new InvalidOperationException(
                    "Gemini returned empty embedding vector.");

            // 2. Build Qdrant point
            var point = new PointStruct
            {
                Id      = new PointId
                {
                    Num = chunkRecord.QdrantPointId
                },
                Vectors = new Vectors
                {
                    Vector = new Vector
                    {
                        Data = { vector }
                    }
                },
                Payload =
                {
                    ["userId"]     = chunkRecord.UserId,
                    ["chunkText"]  = chunkRecord.ChunkText,
                    ["topicName"]  = chunkRecord.TopicName  ?? "",
                    ["subject"]    = chunkRecord.Subject    ?? "",
                    ["chapter"]    = chunkRecord.Chapter    ?? "",
                    ["chunkIndex"] = chunkRecord.ChunkIndex,
                    ["planId"]     = chunkRecord.StudyPlanId
                }
            };

            // 3. Upsert into Qdrant
            await _qdrant.UpsertAsync(collection, new[] { point });

            // 4. Update SQL record — mark as successfully indexed
            chunkRecord.IsIndexed           = true;
            chunkRecord.IndexedAt           = DateTime.UtcNow;
            chunkRecord.IndexErrorMessage   = null;
            await _db.SaveChangesAsync();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to index chunk {ChunkIndex} " +
                "for plan {PlanId}: {Message}",
                chunkRecord.ChunkIndex,
                chunkRecord.StudyPlanId,
                ex.Message);

            // Save error to SQL for debugging
            chunkRecord.IndexErrorMessage = ex.Message;
            await _db.SaveChangesAsync();

            return false;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — ENSURE QDRANT COLLECTION EXISTS
    // ══════════════════════════════════════════════════════════════
    private async Task EnsureCollectionAsync(string collection)
    {
        try
        {
            var collections = await _qdrant.ListCollectionsAsync();

            if (!collections.Any(c => c == collection))
            {
                await _qdrant.CreateCollectionAsync(
                    collection,
                    new VectorParams
                    {
                        Size     = VectorSize,
                        Distance = Distance.Cosine
                    });

                _logger.LogInformation(
                    "Created Qdrant collection: {Collection}",
                    collection);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to ensure Qdrant collection: {Collection}",
                collection);
            throw;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — SAVE CHUNK RECORD TO SQL
    // ══════════════════════════════════════════════════════════════
    private async Task<SyllabusChunk> SaveChunkToSqlAsync(
        int    userId,
        int    studyPlanId,
        string chunkText,
        int    chunkIndex,
        string collection)
    {
        var pointId = BuildPointId(studyPlanId, chunkIndex);

        var chunkRecord = new SyllabusChunk
        {
            UserId             = userId,
            StudyPlanId        = studyPlanId,
            ChunkIndex         = chunkIndex,
            ChunkText          = chunkText,
            ChunkLength        = chunkText.Length,
            QdrantPointId      = pointId,
            QdrantCollection   = collection,
            EmbeddingModel     = "text-embedding-004",
            EmbeddingDimension = VectorSize,
            IsIndexed          = false,
            IndexAttempts      = 1,
            CreatedAt          = DateTime.UtcNow
        };

        _db.SyllabusChunks.Add(chunkRecord);
        await _db.SaveChangesAsync();

        return chunkRecord;
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — UPDATE RETRIEVAL STATS AFTER SEARCH
    // ══════════════════════════════════════════════════════════════
    private async Task UpdateRetrievalStatsAsync(
        IReadOnlyList<ScoredPoint> results)
    {
        if (!results.Any()) return;

        var pointIds = results
            .Select(r => r.Id.Num)
            .ToList();

        var chunks = await _db.SyllabusChunks
            .Where(c => pointIds.Contains(c.QdrantPointId))
            .ToListAsync();

        foreach (var result in results)
        {
            var chunk = chunks.FirstOrDefault(
                c => c.QdrantPointId == result.Id.Num);

            if (chunk is null) continue;

            chunk.TimesRetrieved++;
            chunk.LastRetrievedAt    = DateTime.UtcNow;
            chunk.LastRetrievedScore = result.Score;
        }

        await _db.SaveChangesAsync();
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — DELETE PLAN CHUNKS FROM SQL
    // ══════════════════════════════════════════════════════════════
    private async Task DeletePlanChunksFromSqlAsync(int studyPlanId)
    {
        var existing = _db.SyllabusChunks
            .Where(c => c.StudyPlanId == studyPlanId);

        if (await existing.AnyAsync())
        {
            _db.SyllabusChunks.RemoveRange(existing);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Deleted existing SQL chunks for plan {PlanId}",
                studyPlanId);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — MARK PLAN AS VECTOR INDEXED
    // ══════════════════════════════════════════════════════════════
    private async Task MarkPlanAsIndexedAsync(int studyPlanId)
    {
        var plan = await _db.StudyPlans
            .FindAsync(studyPlanId);

        if (plan is null) return;

        plan.IsVectorIndexed = true;
        plan.UpdatedAt       = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Marked plan {PlanId} as vector indexed",
            studyPlanId);
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — SPLIT TEXT INTO OVERLAPPING CHUNKS
    // ══════════════════════════════════════════════════════════════
    private static List<string> SplitIntoChunks(
        string fullText,
        int    chunkSize = ChunkSize,
        int    overlap   = ChunkOverlap)
    {
        var chunks = new List<string>();
        var start  = 0;

        while (start < fullText.Length)
        {
            var end = Math.Min(start + chunkSize, fullText.Length);

            // Extend to next sentence boundary
            if (end < fullText.Length)
                end = FindSentenceBoundary(fullText, end);

            var chunk = fullText[start..end].Trim();

            if (!string.IsNullOrWhiteSpace(chunk))
                chunks.Add(chunk);

            start += chunkSize - overlap;
        }

        return chunks;
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — FIND SENTENCE BOUNDARY
    // Avoids splitting chunks mid-sentence
    // ══════════════════════════════════════════════════════════════
    private static int FindSentenceBoundary(
        string text,
        int    position)
    {
        var lookAhead = Math.Min(position + 200, text.Length);

        for (int i = position; i < lookAhead; i++)
        {
            if (text[i] is '.' or '!' or '?')
            {
                bool prevIsDigit = i > 0
                    && char.IsDigit(text[i - 1]);
                bool nextIsDigit = i < text.Length - 1
                    && char.IsDigit(text[i + 1]);

                if (!prevIsDigit && !nextIsDigit)
                    return i + 1;
            }
        }

        return position;
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — HELPERS
    // ══════════════════════════════════════════════════════════════
    private string CollectionName(int userId) =>
        $"{_config["Qdrant:CollectionPrefix"] ?? "syllabus_"}{userId}";

    private static ulong BuildPointId(int studyPlanId, int chunkIndex) =>
        (ulong)(studyPlanId * 10_000 + chunkIndex);
    // Deterministic — same plan + index always produces same Qdrant point ID
    // Max chunks per plan = 9999 before collision
    // For larger syllabi increase multiplier to 100_000
}