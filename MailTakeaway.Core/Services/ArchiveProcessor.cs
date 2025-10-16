using MailTakeaway.Core.Models;
using SharpCompress.Readers;
using SharpCompress.Readers.Tar;
using System.IO.Compression;

namespace MailTakeaway.Core.Services;

public class ArchiveProcessor
{
    private readonly MboxParser _parser;
    private ParseStatistics? _currentStats;

    public ArchiveProcessor(MboxParser parser)
    {
        _parser = parser;
    }

    public ParseStatistics? GetCurrentStatistics() => _currentStats;

    public async Task<(Dictionary<string, EmailIndexEntry> Index, ParseStatistics Stats)> ProcessTarGzArchiveAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"Archive not found: {archivePath}");
        }

        var combinedIndex = new Dictionary<string, EmailIndexEntry>(StringComparer.OrdinalIgnoreCase);
        var combinedStats = new ParseStatistics();
        _currentStats = combinedStats; // Store for live access

        var fileInfo = new FileInfo(archivePath);
        Console.WriteLine("=============================================================");
        Console.WriteLine("MBOX Email Indexer - .NET Edition");
        Console.WriteLine("=============================================================");
        Console.WriteLine($"Archive: {archivePath}");
        Console.WriteLine($"Archive size: {fileInfo.Length:N0} bytes");
        Console.WriteLine();
        Console.WriteLine("--- Scanning archive for .mbox files ---");

        await using var fileStream = File.OpenRead(archivePath);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        
        Console.WriteLine("Decompressing and reading tar archive...");
        
        using var reader = ReaderFactory.Open(gzipStream);
        
        var mboxFiles = new List<(string Path, MemoryStream Data)>();
        
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory) continue;
            
            var key = reader.Entry.Key;
            if (string.IsNullOrEmpty(key)) continue;
            
            if (key.EndsWith(".mbox", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Found: {key} ({reader.Entry.Size:N0} bytes)");
                
                var memStream = new MemoryStream();
                reader.WriteEntryTo(memStream);
                memStream.Seek(0, SeekOrigin.Begin);
                mboxFiles.Add((key, memStream));
            }
        }

        Console.WriteLine($"Found {mboxFiles.Count} .mbox files");
        Console.WriteLine();

        foreach (var (path, stream) in mboxFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            combinedStats.MboxFilesProcessed++;
            Console.WriteLine($"[{combinedStats.MboxFilesProcessed}] Processing: {path}");
            Console.WriteLine($"  Size: {stream.Length:N0} bytes");

            // Pass combinedStats so parser updates it directly
            var (index, stats) = await _parser.ParseMboxStreamAsync(
                stream,
                path,
                combinedStats,
                cancellationToken);

            // Merge results
            foreach (var kvp in index)
            {
                if (!combinedIndex.ContainsKey(kvp.Key))
                {
                    combinedIndex.Add(kvp.Key, kvp.Value);
                }
            }

            stream.Dispose();
            Console.WriteLine();
        }
        
        _currentStats = combinedStats; // Back to combined at the end

        return (combinedIndex, combinedStats);
    }
}
