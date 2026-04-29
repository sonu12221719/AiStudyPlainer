using System.Text;
using System.Text.RegularExpressions;
using AiStudyPlanner.API.Interfaces;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace AiStudyPlanner.API.Services;

public class FileParserService : IFileParserService
{
    private readonly ILogger<FileParserService> _logger;
    private readonly IWebHostEnvironment _env;

    // ── Supported file types ───────────────────────────────────────
    private static readonly string[] AllowedExtensions =
        { ".pdf", ".txt", ".md" };

    // ── Chunking defaults ──────────────────────────────────────────
    private const int DefaultChunkSize = 2000; // characters
    private const int DefaultOverlap   = 200;  // characters

    public FileParserService(ILogger<FileParserService> logger, IWebHostEnvironment env)
    {
        _logger = logger;
        _env    = env;
    }

    // ══════════════════════════════════════════════════════════════
    // EXTRACT TEXT FROM UPLOADED FILE
    // ══════════════════════════════════════════════════════════════
    public async Task<string> ExtractTextAsync(IFormFile file)
    {
        ValidateFile(file);

        var extension = Path
            .GetExtension(file.FileName)
            .ToLower();

        _logger.LogInformation(
            "Parsing file: {FileName} ({Size} bytes)",
            file.FileName, file.Length);

        var rawText = extension switch
        {
            ".txt" => await ExtractFromTxtAsync(file),
            ".md"  => await ExtractFromTxtAsync(file),
            ".pdf" => await ExtractFromPdfAsync(file),
            _      => throw new NotSupportedException(
                          $"File type '{extension}' is not supported.")
        };

        // Clean and normalise the extracted text
        var cleaned = CleanText(rawText);

        _logger.LogInformation(
            "Extracted {CharCount} characters from {FileName}",
            cleaned.Length, file.FileName);

        return cleaned;
    }

    // ══════════════════════════════════════════════════════════════
    // DETECT TOPICS FROM RAW TEXT
    // Scans for headings/sections and splits text into named chunks
    // ══════════════════════════════════════════════════════════════
    public List<DetectedTopic> DetectTopics(string fullText)
    {
        var topics  = new List<DetectedTopic>();
        var lines   = fullText.Split('\n');
        var buffer  = new StringBuilder();
        var heading = "Introduction";
        var index   = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (IsHeading(trimmed))
            {
                // Save previous section
                if (buffer.Length > 50) // ignore tiny fragments
                {
                    topics.Add(new DetectedTopic
                    {
                        Index       = index++,
                        Name        = NormaliseHeading(heading),
                        Content     = buffer.ToString().Trim(),
                        Subject     = DetectSubject(heading),
                        Chapter     = DetectChapter(heading),
                        WordCount   = CountWords(buffer.ToString())
                    });
                }

                heading = trimmed;
                buffer.Clear();
            }
            else
            {
                buffer.AppendLine(line);
            }
        }

        // Save the last section
        if (buffer.Length > 50)
        {
            topics.Add(new DetectedTopic
            {
                Index     = index,
                Name      = NormaliseHeading(heading),
                Content   = buffer.ToString().Trim(),
                Subject   = DetectSubject(heading),
                Chapter   = DetectChapter(heading),
                WordCount = CountWords(buffer.ToString())
            });
        }

        _logger.LogInformation(
            "Detected {Count} topic sections from syllabus",
            topics.Count);

        return topics;
    }

    // ══════════════════════════════════════════════════════════════
    // CHUNK TEXT FOR QDRANT INDEXING
    // Splits full text into overlapping chunks for vector embedding
    // ══════════════════════════════════════════════════════════════
    public List<TextChunk> ChunkText(
        string fullText,
        int    chunkSize = DefaultChunkSize,
        int    overlap   = DefaultOverlap)
    {
        if (string.IsNullOrWhiteSpace(fullText))
            return new List<TextChunk>();

        var chunks = new List<TextChunk>();
        var start  = 0;
        var index  = 0;

        while (start < fullText.Length)
        {
            var end  = Math.Min(start + chunkSize, fullText.Length);

            // Extend to next sentence boundary to avoid mid-sentence cuts
            if (end < fullText.Length)
                end = FindSentenceBoundary(fullText, end);

            var chunkText = fullText[start..end].Trim();

            if (!string.IsNullOrWhiteSpace(chunkText))
            {
                chunks.Add(new TextChunk
                {
                    Index       = index++,
                    Text        = chunkText,
                    StartOffset = start,
                    EndOffset   = end,
                    Length      = chunkText.Length
                });
            }

            // Move forward by chunkSize minus overlap
            start += chunkSize - overlap;
        }

        _logger.LogInformation(
            "Split text into {Count} chunks " +
            "(chunkSize={ChunkSize}, overlap={Overlap})",
            chunks.Count, chunkSize, overlap);

        return chunks;
    }

    // ══════════════════════════════════════════════════════════════
    // SAVE FILE TO DISK
    // ══════════════════════════════════════════════════════════════
    public async Task<string> SaveFileAsync(
        IFormFile file,
        string?   subFolder = null)
    {
        var uploadRoot = Path.Combine(
            _env.ContentRootPath, "Uploads",
            subFolder ?? string.Empty);

        Directory.CreateDirectory(uploadRoot);

        // Prefix with GUID to prevent filename collisions
        var safeFileName = $"{Guid.NewGuid()}_{SanitiseFileName(file.FileName)}";
        var fullPath     = Path.Combine(uploadRoot, safeFileName);

        await using var stream = new FileStream(
            fullPath, FileMode.Create, FileAccess.Write);

        await file.CopyToAsync(stream);

        _logger.LogInformation(
            "Saved file to: {Path}", fullPath);

        return fullPath;
    }

    // ══════════════════════════════════════════════════════════════
    // VALIDATE FILE
    // ══════════════════════════════════════════════════════════════
    public void ValidateFile(IFormFile file)
    {
        if (file is null || file.Length == 0)
            throw new ArgumentException(
                "No file provided or file is empty.");

        var extension = Path
            .GetExtension(file.FileName)
            .ToLower();

        if (!AllowedExtensions.Contains(extension))
            throw new NotSupportedException(
                $"File type '{extension}' is not supported. " +
                $"Please upload a .pdf or .txt file.");

        if (file.Length > 10 * 1024 * 1024) // 10 MB
            throw new InvalidOperationException(
                "File size exceeds the 10 MB limit.");

        // Basic MIME type check
        var allowedMimeTypes = new[]
        {
            "text/plain",
            "text/markdown",
            "application/pdf",
            "application/octet-stream" // some browsers send PDF as this
        };

        if (!allowedMimeTypes.Contains(
            file.ContentType.ToLower()))
        {
            _logger.LogWarning(
                "Unexpected MIME type: {MimeType} for file {FileName}",
                file.ContentType, file.FileName);
            // warn but don't block — MIME is unreliable from browsers
        }
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — EXTRACT FROM .TXT / .MD
    // ══════════════════════════════════════════════════════════════
    private static async Task<string> ExtractFromTxtAsync(
        IFormFile file)
    {
        // Detect encoding — fall back to UTF-8
        using var stream = file.OpenReadStream();
        using var reader = new StreamReader(
            stream,
            encoding:          Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize:        4096,
            leaveOpen:         false);

        return await reader.ReadToEndAsync();
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — EXTRACT FROM .PDF
    // ══════════════════════════════════════════════════════════════
    private async Task<string> ExtractFromPdfAsync(IFormFile file)
    {
        // ── Option A: PdfPig (recommended) ────────────────────────
        // Install: dotnet add package PdfPig
        //
        
        
        await using var stream = file.OpenReadStream();
        var memStream = new MemoryStream();
        await stream.CopyToAsync(memStream);
        memStream.Position = 0;
        
        using var pdf    = PdfDocument.Open(memStream);
        var pageTexts    = new List<string>();
        
        foreach (Page page in pdf.GetPages())
        {
            var words = page.GetWords();
            pageTexts.Add(string.Join(" ",
                words.Select(w => w.Text)));
        }
        
        return string.Join("\n\n", pageTexts);

        // ── Option B: iTextSharp ──────────────────────────────────
        // Install: dotnet add package itext7
        //
        // using iText.Kernel.Pdf;
        // using iText.Kernel.Pdf.Canvas.Parser;
        //
        // await using var stream = file.OpenReadStream();
        // var memStream = new MemoryStream();
        // await stream.CopyToAsync(memStream);
        // memStream.Position = 0;
        //
        // using var reader = new PdfReader(memStream);
        // using var doc    = new PdfDocument(reader);
        // var sb           = new StringBuilder();
        //
        // for (int i = 1; i <= doc.GetNumberOfPages(); i++)
        // {
        //     sb.AppendLine(PdfTextExtractor
        //         .GetTextFromPage(doc.GetPage(i)));
        // }
        //
        // return sb.ToString();

        // ── Placeholder until PdfPig is installed ─────────────────
        // _logger.LogWarning(
        //     "PDF extraction called but no PDF library is installed. " +
        //     "Add PdfPig: dotnet add package PdfPig");

        // await Task.CompletedTask;

        // throw new NotSupportedException(
        //     "PDF extraction requires PdfPig. " +
        //     "Run: dotnet add package PdfPig " +
        //     "then uncomment Option A in FileParserService.");
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — DETECT HEADING
    // ══════════════════════════════════════════════════════════════
    private static bool IsHeading(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        if (line.Length > 120)               return false;
        // Long lines are paragraphs, not headings

        // "Chapter 1", "Unit 4", "Section 3", "Topic 2", "Part A"
        if (Regex.IsMatch(line,
            @"^(chapter|unit|section|topic|part|module)\s+[\dIVXA-Z]",
            RegexOptions.IgnoreCase))
            return true;

        // Numbered: "1.", "2.1", "3.4.2", "1.2.3 Heading"
        if (Regex.IsMatch(line,
            @"^\d+(\.\d+)*\.?\s+[A-Z]"))
            return true;

        // Markdown heading: "# Heading", "## Sub-heading"
        if (Regex.IsMatch(line, @"^#{1,4}\s+\w"))
            return true;

        // ALL CAPS line (min 4 chars, at least one letter)
        if (line.Length >= 4
         && line.Length <= 80
         && line == line.ToUpper()
         && line.Any(char.IsLetter))
            return true;

        return false;
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — DETECT SUBJECT FROM HEADING
    // ══════════════════════════════════════════════════════════════
    private static string DetectSubject(string heading)
    {
        var lower = heading.ToLower();

        if (ContainsAny(lower,
            "physics", "mechanics", "thermodynamics",
            "optics", "waves", "electr"))
            return "Physics";

        if (ContainsAny(lower,
            "chemistry", "organic", "inorganic",
            "physical chemistry", "mol"))
            return "Chemistry";

        if (ContainsAny(lower,
            "biology", "botany", "zoology",
            "physiology", "genetics", "ecology"))
            return "Biology";

        if (ContainsAny(lower,
            "math", "algebra", "calculus",
            "geometry", "trigonometry", "statistics",
            "probability", "vector"))
            return "Mathematics";

        if (ContainsAny(lower,
            "history", "medieval", "ancient", "modern"))
            return "History";

        if (ContainsAny(lower,
            "geography", "environment", "ecology",
            "climate", "soil", "river"))
            return "Geography";

        if (ContainsAny(lower,
            "polity", "constitution", "governance",
            "parliament", "judiciary"))
            return "Polity";

        if (ContainsAny(lower,
            "economy", "economics", "gdp",
            "inflation", "trade", "fiscal"))
            return "Economics";

        if (ContainsAny(lower,
            "computer", "algorithm", "data structure",
            "network", "operating system", "database"))
            return "Computer Science";

        return "General";
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — DETECT CHAPTER FROM HEADING
    // ══════════════════════════════════════════════════════════════
    private static string? DetectChapter(string heading)
    {
        // Match "Chapter 3", "Ch. 4", "Unit 2"
        var match = Regex.Match(heading,
            @"(chapter|unit|ch\.?)\s*(\d+)",
            RegexOptions.IgnoreCase);

        if (match.Success)
            return $"Chapter {match.Groups[2].Value}";

        // Match numbered heading "3.2 Newton's Laws"
        var numMatch = Regex.Match(heading,
            @"^(\d+(\.\d+)*)\s+");

        if (numMatch.Success)
            return $"Section {numMatch.Groups[1].Value}";

        return null;
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — CLEAN EXTRACTED TEXT
    // ══════════════════════════════════════════════════════════════
    private static string CleanText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var sb = new StringBuilder(raw);

        // Remove null bytes and control characters (common in PDFs)
        for (int i = 0; i < sb.Length; i++)
        {
            var c = sb[i];
            if (c == '\0' || (char.IsControl(c)
                           && c != '\n'
                           && c != '\r'
                           && c != '\t'))
            {
                sb[i] = ' ';
            }
        }

        var text = sb.ToString();

        // Normalise line endings
        text = text.Replace("\r\n", "\n")
                   .Replace("\r",   "\n");

        // Collapse 3+ consecutive blank lines to 2
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        // Collapse multiple spaces to single space
        text = Regex.Replace(text, @"[ \t]+", " ");

        // Remove leading/trailing whitespace on each line
        var lines = text.Split('\n')
                        .Select(l => l.Trim());

        return string.Join("\n", lines).Trim();
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — FIND SENTENCE BOUNDARY
    // Extends chunk end to nearest sentence end (. ! ?)
    // to avoid cutting mid-sentence
    // ══════════════════════════════════════════════════════════════
    private static int FindSentenceBoundary(string text, int position)
    {
        // Look forward up to 200 chars for sentence end
        var lookAhead = Math.Min(position + 200, text.Length);

        for (int i = position; i < lookAhead; i++)
        {
            if (text[i] is '.' or '!' or '?')
            {
                // Make sure it is not a decimal point (e.g. 3.14)
                bool prevIsDigit = i > 0
                    && char.IsDigit(text[i - 1]);
                bool nextIsDigit = i < text.Length - 1
                    && char.IsDigit(text[i + 1]);

                if (!prevIsDigit && !nextIsDigit)
                    return i + 1;
            }
        }

        // No boundary found — return original position
        return position;
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — NORMALISE HEADING TEXT
    // ══════════════════════════════════════════════════════════════
    private static string NormaliseHeading(string heading)
    {
        // Strip markdown # characters
        heading = Regex.Replace(heading, @"^#+\s*", "").Trim();

        // Strip leading number "1.", "3.2." etc
        heading = Regex.Replace(heading,
            @"^\d+(\.\d+)*\.?\s+", "").Trim();

        // Title case
        if (heading.Length > 0)
            heading = char.ToUpper(heading[0])
                    + heading[1..].ToLower();

        return heading;
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — SANITISE FILE NAME
    // ══════════════════════════════════════════════════════════════
    private static string SanitiseFileName(string fileName)
    {
        // Remove characters not safe for file systems
        var invalid = Path.GetInvalidFileNameChars();
        var safe    = string.Join("_",
            fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));

        // Limit length
        return safe.Length > 100
            ? safe[..100]
            : safe;
    }

    // ══════════════════════════════════════════════════════════════
    // PRIVATE — HELPERS
    // ══════════════════════════════════════════════════════════════
    private static bool ContainsAny(string source, params string[] keywords)
        => keywords.Any(source.Contains);

    private static int CountWords(string text)
        => string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split(
                new[] { ' ', '\n', '\t' },
                StringSplitOptions.RemoveEmptyEntries).Length;
}

// ─── Supporting models returned by FileParserService ───────────────
public class DetectedTopic
{
    public int    Index     { get; set; }
    public string Name      { get; set; } = string.Empty;
    public string Content   { get; set; } = string.Empty;
    public string Subject   { get; set; } = string.Empty;
    public string? Chapter  { get; set; }
    public int    WordCount { get; set; }
}

public class TextChunk
{
    public int    Index       { get; set; }
    public string Text        { get; set; } = string.Empty;
    public int    StartOffset { get; set; }
    public int    EndOffset   { get; set; }
    public int    Length      { get; set; }
}