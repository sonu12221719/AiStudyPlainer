using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiStudyPlanner.API.Models.Entities;

public class SyllabusChunk
{
    // ─── Primary Key ───────────────────────────────────────────────
    [Key]
    public int Id { get; set; }

    // ─── Foreign Keys ──────────────────────────────────────────────
    [Required]
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [Required]
    public int StudyPlanId { get; set; }

    [ForeignKey(nameof(StudyPlanId))]
    public StudyPlan StudyPlan { get; set; } = null!;

    // ─── Chunk Identity ────────────────────────────────────────────
    public int ChunkIndex { get; set; }
    // position of this chunk within the full syllabus (0-based)
    // used to reconstruct reading order if needed

    [Required]
    public string ChunkText { get; set; } = string.Empty;
    // the actual ~500-token text slice stored in SQL
    // mirrored in Qdrant as the vector payload

    public int ChunkLength { get; set; }
    // character count — used to verify chunking consistency

    // ─── Topic Association ─────────────────────────────────────────
    [MaxLength(200)]
    public string? TopicName { get; set; }
    // which topic this chunk belongs to — if Gemini could detect it
    // stored as Qdrant payload tag for filtered vector search

    [MaxLength(100)]
    public string? Subject { get; set; }
    // e.g. "Physics", "Chemistry" — for subject-scoped RAG searches

    [MaxLength(100)]
    public string? Chapter { get; set; }
    // e.g. "Chapter 3 - Dynamics" — extracted during parsing

    // ─── Qdrant Sync State ─────────────────────────────────────────
    public bool IsIndexed { get; set; } = false;
    // true once this chunk's vector is successfully stored in Qdrant

    public ulong QdrantPointId { get; set; }
    // the exact point ID used in Qdrant — needed for updates/deletes
    // computed as: StudyPlanId * 10000 + ChunkIndex

    [MaxLength(100)]
    public string? QdrantCollection { get; set; }
    // e.g. "syllabus_42" — the Qdrant collection this chunk lives in
    // stored here so we can clean up Qdrant on plan deletion

    public DateTime? IndexedAt { get; set; }
    // when this chunk was last successfully upserted into Qdrant

    public int IndexAttempts { get; set; } = 0;
    // how many times indexing was attempted — for retry/debug

    public string? IndexErrorMessage { get; set; }
    // last error from Qdrant if indexing failed — for diagnostics

    // ─── Embedding Metadata ────────────────────────────────────────
    [MaxLength(100)]
    public string EmbeddingModel { get; set; } = "text-embedding-004";
    // which Gemini model generated the embedding
    // stored so you know if a chunk needs re-indexing after model upgrade

    public int EmbeddingDimension { get; set; } = 768;
    // vector size — 768 for text-embedding-004

    // ─── RAG Usage Tracking ────────────────────────────────────────
    public int TimesRetrieved { get; set; } = 0;
    // how many times this chunk was returned by a Qdrant search
    // useful for analytics: which parts of syllabus get asked about most

    public DateTime? LastRetrievedAt { get; set; }
    // when this chunk was last surfaced in a chat answer

    [Column(TypeName = "REAL")]
    public double? LastRetrievedScore { get; set; }
    // cosine similarity score from the last retrieval (0.0–1.0)
    // useful for tuning the score threshold in SearchSimilarChunksAsync

    // ─── Timestamps ────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // ─── Computed Helpers (not mapped to DB) ───────────────────────
    [NotMapped]
    public bool IsStale =>
        IndexedAt.HasValue &&
        IndexedAt.Value < DateTime.UtcNow.AddDays(-30);
    // true if the chunk was indexed more than 30 days ago
    // trigger for re-indexing if embedding model is updated

    [NotMapped]
    public bool HasIndexFailed =>
        !IsIndexed && IndexAttempts > 0;
    // true if at least one indexing attempt was made but failed

    [NotMapped]
    public string ShortPreview =>
        ChunkText.Length > 120
            ? ChunkText[..120] + "..."
            : ChunkText;
    // used in admin/debug views to preview chunk content
}