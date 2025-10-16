using MailTakeaway.Core.Models;
using MailTakeaway.Core.Services;
using System.Text.Json;
using System.Collections.Concurrent;

namespace MailTakeaway.Web;

public class EmailIndexService
{
    private Dictionary<string, EmailIndexEntry>? _emailIndex;
    private ParseStatistics? _statistics;
    private readonly ConcurrentDictionary<string, Dictionary<string, EmailAttachment>> _attachments = new();
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;
    private bool _isIndexing;
    private ArchiveProcessor? _currentProcessor;
    private const string CACHE_FILE = "email_index_cache.json";

    public EmailIndexService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task InitializeAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            var archivePath = _configuration["ArchivePath"] ?? "MailDump.tgz";
            
            // Make path relative to application directory if not absolute
            if (!Path.IsPathRooted(archivePath))
            {
                var appDir = AppContext.BaseDirectory;
                var possiblePaths = new[]
                {
                    Path.Combine(appDir, archivePath),
                    Path.Combine(Directory.GetCurrentDirectory(), archivePath),
                    Path.Combine(appDir, "..", "..", "..", archivePath),
                    Path.Combine(appDir, "..", "..", "..", "..", archivePath)
                };
                
                archivePath = possiblePaths.FirstOrDefault(File.Exists) ?? archivePath;
            }
            
            // Try loading from cache first
            if (File.Exists(CACHE_FILE))
            {
                var archiveModified = File.GetLastWriteTimeUtc(archivePath);
                var cacheModified = File.GetLastWriteTimeUtc(CACHE_FILE);
                
                if (cacheModified > archiveModified)
                {
                    Console.WriteLine("Loading email index from cache...");
                    try
                    {
                        var json = await File.ReadAllTextAsync(CACHE_FILE);
                        var cached = JsonSerializer.Deserialize<CachedIndex>(json);
                        if (cached != null)
                        {
                            _emailIndex = cached.Index.ToDictionary(e => e.MessageId, StringComparer.OrdinalIgnoreCase);
                            _statistics = cached.Statistics;
                            _isInitialized = true;
                            _isIndexing = false;
                            Console.WriteLine($"Loaded {_emailIndex.Count} messages from cache");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Cache load failed: {ex.Message}, rebuilding...");
                    }
                }
            }
            
            Console.WriteLine("Initializing email index...");
            Console.WriteLine($"Archive: {archivePath}");
            _isIndexing = true;

            var parser = new MboxParser(
                bodyPreviewLength: 200,
                progressInterval: 100,
                verboseLogging: false);

            _currentProcessor = new ArchiveProcessor(parser);
            (_emailIndex, _statistics) = await _currentProcessor.ProcessTarGzArchiveAsync(archivePath);

            // Extract and store attachments
            foreach (var entry in _emailIndex.Values.Where(e => e.AttachmentData != null && e.AttachmentData.Any()))
            {
                _attachments[entry.MessageId] = entry.AttachmentData!;
                // Clear from entry to save memory in cache
                entry.AttachmentData = null;
            }
            
            Console.WriteLine($"Stored attachments from {_attachments.Count} messages");

            // Save to cache
            try
            {
                var cached = new CachedIndex 
                { 
                    Index = _emailIndex.Values.ToList(),
                    Statistics = _statistics ?? new ParseStatistics()
                };
                var json = JsonSerializer.Serialize(cached, new JsonSerializerOptions { WriteIndented = false });
                await File.WriteAllTextAsync(CACHE_FILE, json);
                Console.WriteLine("Index cached to disk");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to cache index: {ex.Message}");
            }

            _isInitialized = true;
            _isIndexing = false;
            Console.WriteLine($"Email index initialized: {_emailIndex.Count} messages");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing email index: {ex.Message}");
            _isIndexing = false;
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public bool IsInitialized => _isInitialized;
    public bool IsIndexing => _isIndexing;
    
    public ParseStatistics? GetStatistics() => _statistics;
    
    public object GetProgress()
    {
        // During indexing, get live stats from processor if available
        var stats = _isIndexing && _currentProcessor != null ? _currentProcessor.GetCurrentStatistics() : _statistics;
        
        Console.WriteLine($"[GetProgress] _isIndexing={_isIndexing}, _currentProcessor={(_currentProcessor != null ? "exists" : "null")}, stats={stats?.TotalMessages ?? -1}");
        
        return new
        {
            IsIndexing = _isIndexing,
            IsInitialized = _isInitialized,
            TotalMessages = stats?.TotalMessages ?? 0,
            SuccessfullyParsed = stats?.SuccessfullyParsed ?? 0,
            ParseErrors = stats?.ParseErrors ?? 0,
            Duplicates = stats?.Duplicates ?? 0,
            ParseErrorMessages = stats?.ParseErrorMessages ?? new List<string>()
        };
    }

    public List<string> GetAllFolders()
    {
        if (_emailIndex == null) return new List<string>();
        
        return _emailIndex.Values
            .SelectMany(e => e.AllFolders)
            .Where(f => !string.IsNullOrEmpty(f))
            .Distinct()
            .OrderBy(f => f)
            .ToList();
    }

    public List<string> GetAllAttachmentTypes()
    {
        if (_emailIndex == null) return new List<string>();
        
        return _emailIndex.Values
            .Where(e => e.HasAttachments && e.AttachmentNames.Any())
            .SelectMany(e => e.AttachmentNames)
            .Select(name => Path.GetExtension(name).ToLowerInvariant())
            .Where(ext => !string.IsNullOrEmpty(ext))
            .Distinct()
            .OrderBy(ext => ext)
            .ToList();
    }

    public Dictionary<string, int> GetAttachmentTypeCounts()
    {
        if (_emailIndex == null) return new Dictionary<string, int>();
        
        return _emailIndex.Values
            .Where(e => e.HasAttachments && e.AttachmentNames.Any())
            .SelectMany(e => e.AttachmentNames)
            .Select(name => Path.GetExtension(name).ToLowerInvariant())
            .Where(ext => !string.IsNullOrEmpty(ext))
            .GroupBy(ext => ext)
            .ToDictionary(g => g.Key, g => g.Count())
            .OrderBy(kvp => kvp.Key)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public IEnumerable<EmailIndexEntry> Search(
        string? searchTerm = null,
        string? fromFilter = null,
        string? toFilter = null,
        string? folderFilter = null,
        List<string>? includeFolders = null,
        List<string>? excludeFolders = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        bool? hasAttachments = null,
        string? attachmentType = null,
        int skip = 0,
        int take = 50)
    {
        if (_emailIndex == null) return Enumerable.Empty<EmailIndexEntry>();

        var query = _emailIndex.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(e =>
                e.Subject.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                e.From.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                e.To.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                e.BodyPreview.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(fromFilter))
        {
            query = query.Where(e => e.From.Contains(fromFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(folderFilter))
        {
            query = query.Where(e => e.Folder.Contains(folderFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (includeFolders != null && includeFolders.Any())
        {
            query = query.Where(e => e.AllFolders.Any(f => includeFolders.Contains(f, StringComparer.OrdinalIgnoreCase)));
        }

        if (excludeFolders != null && excludeFolders.Any())
        {
            query = query.Where(e => !e.AllFolders.Any(f => excludeFolders.Contains(f, StringComparer.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(toFilter))
        {
            query = query.Where(e => e.To.Contains(toFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (dateFrom.HasValue)
        {
            query = query.Where(e => e.UtcDate.HasValue && e.UtcDate.Value >= dateFrom.Value);
        }

        if (dateTo.HasValue)
        {
            query = query.Where(e => e.UtcDate.HasValue && e.UtcDate.Value <= dateTo.Value);
        }

        if (hasAttachments.HasValue)
        {
            query = query.Where(e => e.HasAttachments == hasAttachments.Value);
        }

        if (!string.IsNullOrWhiteSpace(attachmentType))
        {
            query = query.Where(e => e.HasAttachments && 
                e.AttachmentNames.Any(name => Path.GetExtension(name).Equals(attachmentType, StringComparison.OrdinalIgnoreCase)));
        }

        return query
            .OrderByDescending(e => e.UtcDate)
            .Skip(skip)
            .Take(take);
    }

    public int GetTotalCount(
        string? searchTerm = null,
        string? fromFilter = null,
        string? toFilter = null,
        string? folderFilter = null,
        List<string>? includeFolders = null,
        List<string>? excludeFolders = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        bool? hasAttachments = null,
        string? attachmentType = null)
    {
        if (_emailIndex == null) return 0;

        var query = _emailIndex.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(e =>
                e.Subject.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                e.From.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                e.To.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                e.BodyPreview.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(fromFilter))
        {
            query = query.Where(e => e.From.Contains(fromFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(toFilter))
        {
            query = query.Where(e => e.To.Contains(toFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(folderFilter))
        {
            query = query.Where(e => e.Folder.Contains(folderFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (includeFolders != null && includeFolders.Any())
        {
            query = query.Where(e => e.AllFolders.Any(f => includeFolders.Contains(f, StringComparer.OrdinalIgnoreCase)));
        }

        if (excludeFolders != null && excludeFolders.Any())
        {
            query = query.Where(e => !e.AllFolders.Any(f => excludeFolders.Contains(f, StringComparer.OrdinalIgnoreCase)));
        }

        if (dateFrom.HasValue)
        {
            query = query.Where(e => e.UtcDate.HasValue && e.UtcDate.Value >= dateFrom.Value);
        }

        if (dateTo.HasValue)
        {
            query = query.Where(e => e.UtcDate.HasValue && e.UtcDate.Value <= dateTo.Value);
        }

        if (hasAttachments.HasValue)
        {
            query = query.Where(e => e.HasAttachments == hasAttachments.Value);
        }

        if (!string.IsNullOrWhiteSpace(attachmentType))
        {
            query = query.Where(e => e.HasAttachments && 
                e.AttachmentNames.Any(name => Path.GetExtension(name).Equals(attachmentType, StringComparison.OrdinalIgnoreCase)));
        }

        return query.Count();
    }

    public EmailIndexEntry? GetByMessageId(string messageId)
    {
        if (_emailIndex == null) return null;
        return _emailIndex.TryGetValue(messageId, out var entry) ? entry : null;
    }

    public List<EmailIndexEntry> GetThread(string messageId)
    {
        if (_emailIndex == null) return new List<EmailIndexEntry>();

        var thread = new List<EmailIndexEntry>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Get the message
        var current = GetByMessageId(messageId);
        if (current == null) return thread;

        // Find the root of the thread by following InReplyTo
        var attempts = 0;
        while (!string.IsNullOrEmpty(current.InReplyTo) && attempts++ < 100)
        {
            if (visited.Contains(current.MessageId)) break;
            visited.Add(current.MessageId);
            var parent = GetByMessageId(current.InReplyTo);
            if (parent == null) break;
            current = parent;
        }

        // Now collect all messages in the thread
        var root = current;
        thread.Add(root);
        visited.Clear();
        visited.Add(root.MessageId);

        // Recursively add all replies
        AddReplies(root.MessageId, thread, visited);

        return thread.OrderBy(e => e.UtcDate).ToList();
    }

    private void AddReplies(string messageId, List<EmailIndexEntry> thread, HashSet<string> visited)
    {
        if (_emailIndex == null) return;

        var replies = _emailIndex.Values
            .Where(e => !visited.Contains(e.MessageId) && 
                       (e.InReplyTo == messageId || e.References.Contains(messageId)))
            .ToList();

        foreach (var reply in replies)
        {
            visited.Add(reply.MessageId);
            thread.Add(reply);
            AddReplies(reply.MessageId, thread, visited);
        }
    }

    public EmailAttachment? GetAttachment(string messageId, string fileName)
    {
        if (_attachments.TryGetValue(messageId, out var messageAttachments))
        {
            if (messageAttachments.TryGetValue(fileName, out var attachment))
            {
                return attachment;
            }
        }
        return null;
    }

    private class CachedIndex
    {
        public List<EmailIndexEntry> Index { get; set; } = new();
        public ParseStatistics Statistics { get; set; } = new();
    }
}
