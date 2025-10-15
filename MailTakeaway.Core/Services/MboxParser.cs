using System.Text;
using System.Text.RegularExpressions;
using MailTakeaway.Core.Models;
using MimeKit;

namespace MailTakeaway.Core.Services;

public partial class MboxParser
{
    private readonly EmailFilterOptions _filters;
    private readonly int _bodyPreviewLength;
    private readonly int _progressInterval;
    private readonly bool _verboseLogging;
    private readonly bool _stopOnError;
    private ParseStatistics? _currentStats;

    public MboxParser(
        EmailFilterOptions? filters = null,
        int bodyPreviewLength = 200,
        int progressInterval = 50,
        bool verboseLogging = false,
        bool stopOnError = false)
    {
        _filters = filters ?? new EmailFilterOptions();
        _bodyPreviewLength = bodyPreviewLength;
        _progressInterval = progressInterval;
        _verboseLogging = verboseLogging;
        _stopOnError = stopOnError;
    }

    public ParseStatistics? GetCurrentStatistics() => _currentStats;

    [GeneratedRegex(@"^From\s+\S+@\S+\s+\w+", RegexOptions.IgnoreCase)]
    private static partial Regex MboxFromLineRegex();

    [GeneratedRegex(@"^From\s+MAILER-DAEMON", RegexOptions.IgnoreCase)]
    private static partial Regex MboxMailerDaemonRegex();

    private static bool IsValidMboxFromLine(string line)
    {
        if (!line.StartsWith("From ", StringComparison.Ordinal))
            return false;

        if (line.Length < 6)
            return false;

        if (MboxFromLineRegex().IsMatch(line))
            return true;

        if (MboxMailerDaemonRegex().IsMatch(line))
            return true;

        if (line.StartsWith("From - ", StringComparison.Ordinal))
            return true;

        return false;
    }

    public async Task<(Dictionary<string, EmailIndexEntry> Index, ParseStatistics Stats)> ParseMboxStreamAsync(
        Stream stream,
        string sourceFileName,
        ParseStatistics? sharedStats = null,
        CancellationToken cancellationToken = default)
    {
        var index = new Dictionary<string, EmailIndexEntry>(StringComparer.OrdinalIgnoreCase);
        var stats = sharedStats ?? new ParseStatistics();
        _currentStats = stats; // Store for live access
        var messageCount = 0;
        var lineCount = 0L;
        var currentMessageBody = new StringBuilder();
        var inMessage = false;

        Console.WriteLine($"  Parsing: {Path.GetFileName(sourceFileName)}");

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break;
            
            lineCount++;

            if (IsValidMboxFromLine(line))
            {
                // Process previous message
                if (currentMessageBody.Length > 0 && inMessage)
                {
                    ProcessMessage(currentMessageBody.ToString(), sourceFileName, index, stats, ref messageCount);
                    currentMessageBody.Clear();
                }

                inMessage = true;
            }

            if (inMessage)
            {
                currentMessageBody.AppendLine(line);
            }
        }

        // Process final message
        if (currentMessageBody.Length > 0 && inMessage)
        {
            ProcessMessage(currentMessageBody.ToString(), sourceFileName, index, stats, ref messageCount);
        }

        Console.WriteLine($"\r  Completed: {messageCount} messages indexed from {Path.GetFileName(sourceFileName)} ({lineCount} lines)");
        return (index, stats);
    }

    private void ProcessMessage(
        string messageBody,
        string sourceFileName,
        Dictionary<string, EmailIndexEntry> index,
        ParseStatistics stats,
        ref int messageCount)
    {
        try
        {
            stats.TotalMessages++;
            if (stats.TotalMessages % 100 == 0)
            {
                Console.WriteLine($"[ProcessMessage] TotalMessages={stats.TotalMessages}");
            }

            var entry = ParseEmailMessage(messageBody, sourceFileName);

            if (string.IsNullOrEmpty(entry.MessageId))
            {
                var errorMsg = $"Skipped message without Message-ID at message {stats.TotalMessages} in {sourceFileName}";
                LogWarning(errorMsg);
                stats.ParseErrors++;
                stats.ParseErrorMessages.Add(errorMsg);
                return;
            }

            if (index.ContainsKey(entry.MessageId))
            {
                stats.Duplicates++;
                LogVerbose($"Duplicate Message-ID: {entry.MessageId}");
                return;
            }

            if (!entry.MatchesFilters(_filters))
            {
                stats.FilteredOut++;
                LogVerbose($"Filtered out: {entry.Subject}");
                return;
            }

            index.Add(entry.MessageId, entry);
            messageCount++;
            stats.SuccessfullyParsed++;

            if (messageCount % _progressInterval == 0)
            {
                Console.Write($"\r  Progress: {messageCount} messages indexed...");
            }
        }
        catch (Exception ex)
        {
            var errorMsg = $"Parse error at message {stats.TotalMessages} in {sourceFileName}: {ex.Message}";
            LogError(errorMsg);
            stats.ParseErrors++;
            stats.ParseErrorMessages.Add(errorMsg);
            if (_stopOnError)
                throw;
        }
    }

    private EmailIndexEntry ParseEmailMessage(string messageBody, string sourceFileName)
    {
        var entry = new EmailIndexEntry
        {
            MboxSource = Path.GetFileName(sourceFileName),
            RawSize = messageBody.Length
        };

        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(messageBody));
            var message = MimeMessage.Load(stream);

            entry.Subject = message.Subject ?? string.Empty;
            entry.From = message.From.ToString();
            entry.To = message.To.ToString();
            entry.MessageId = message.MessageId?.Trim('<', '>') ?? string.Empty;
            entry.UtcDate = message.Date.UtcDateTime;
            entry.InReplyTo = message.InReplyTo?.Trim('<', '>') ?? string.Empty;
            entry.References = message.References.Select(r => r.Trim('<', '>')).ToList();

            // Extract folder - try X-Gmail-Labels first, then fall back to path
            var gmailLabels = message.Headers["X-Gmail-Labels"];
            if (!string.IsNullOrEmpty(gmailLabels))
            {
                // Gmail labels are comma-separated
                var labels = gmailLabels.Split(',').Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();
                entry.AllFolders = labels;
                entry.Folder = labels.FirstOrDefault() ?? "Inbox";
            }
            else
            {
                // Fall back to extracting from path
                var pathParts = sourceFileName.Split('/', '\\');
                var folderName = pathParts.Length > 2 ? pathParts[^2] : "Root";
                entry.Folder = folderName;
                entry.AllFolders = new List<string> { folderName };
            }

            // Extract attachments
            var attachments = message.Attachments.ToList();
            entry.HasAttachments = attachments.Any();
            entry.AttachmentCount = attachments.Count;
            entry.AttachmentNames = attachments
                .Select(a => (a as MimePart)?.FileName ?? "unnamed")
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();
            
            // Store in AttachmentData property for later extraction
            entry.AttachmentData = ExtractAttachmentData(attachments);

            // Extract body preview
            entry.BodyPreview = ExtractBodyPreview(message);
            
            // Store full HTML and text bodies
            entry.HtmlBody = ProcessHtmlBodyWithInlineImages(message, attachments);
            entry.TextBody = message.TextBody;
        }
        catch (Exception ex)
        {
            LogVerbose($"MIME parsing error: {ex.Message}");
            throw;
        }

        return entry;
    }

    private string? ProcessHtmlBodyWithInlineImages(MimeMessage message, List<MimeEntity> attachments)
    {
        var htmlBody = message.HtmlBody;
        if (string.IsNullOrEmpty(htmlBody) || !attachments.Any())
            return htmlBody;

        // Build a map of Content-ID to attachment data
        var cidMap = new Dictionary<string, (string contentType, byte[] data)>();
        
        foreach (var attachment in attachments)
        {
            if (attachment is MimePart mimePart && mimePart.ContentId != null)
            {
                try
                {
                    using var memory = new MemoryStream();
                    mimePart.Content.DecodeTo(memory);
                    var contentId = mimePart.ContentId.Trim('<', '>');
                    var contentType = mimePart.ContentType?.MimeType ?? "application/octet-stream";
                    cidMap[contentId] = (contentType, memory.ToArray());
                }
                catch (Exception ex)
                {
                    LogVerbose($"Failed to process inline image {mimePart.ContentId}: {ex.Message}");
                }
            }
        }

        // Replace cid: references with data URIs
        foreach (var (contentId, (contentType, data)) in cidMap)
        {
            var cidReference = $"cid:{contentId}";
            var base64Data = Convert.ToBase64String(data);
            var dataUri = $"data:{contentType};base64,{base64Data}";
            htmlBody = htmlBody.Replace(cidReference, dataUri);
        }

        return htmlBody;
    }

    private string ExtractBodyPreview(MimeMessage message)
    {
        try
        {
            var textPart = message.TextBody;
            if (!string.IsNullOrEmpty(textPart))
            {
                textPart = Regex.Replace(textPart.Trim(), @"\s+", " ");
                return textPart.Length > _bodyPreviewLength
                    ? textPart[.._bodyPreviewLength] + "..."
                    : textPart;
            }

            var htmlPart = message.HtmlBody;
            if (!string.IsNullOrEmpty(htmlPart))
            {
                // Strip HTML tags for preview
                htmlPart = Regex.Replace(htmlPart, "<.*?>", string.Empty);
                htmlPart = Regex.Replace(htmlPart.Trim(), @"\s+", " ");
                return htmlPart.Length > _bodyPreviewLength
                    ? htmlPart[.._bodyPreviewLength] + "..."
                    : htmlPart;
            }

            return "[No text body]";
        }
        catch
        {
            return "[Body extraction failed]";
        }
    }

    private Dictionary<string, EmailAttachment> ExtractAttachmentData(List<MimeEntity> attachments)
    {
        var result = new Dictionary<string, EmailAttachment>();
        
        foreach (var attachment in attachments)
        {
            if (attachment is MimePart mimePart)
            {
                try
                {
                    var fileName = mimePart.FileName ?? $"unnamed_{Guid.NewGuid()}";
                    using var memory = new MemoryStream();
                    mimePart.Content.DecodeTo(memory);
                    
                    var emailAttachment = new EmailAttachment
                    {
                        FileName = fileName,
                        ContentType = mimePart.ContentType?.MimeType ?? "application/octet-stream",
                        Data = memory.ToArray(),
                        Size = memory.Length,
                        IsInline = mimePart.ContentDisposition?.Disposition == "inline"
                    };
                    
                    result[fileName] = emailAttachment;
                }
                catch (Exception ex)
                {
                    LogVerbose($"Failed to extract attachment {mimePart.FileName}: {ex.Message}");
                }
            }
        }
        
        return result;
    }

    private void LogVerbose(string message)
    {
        if (_verboseLogging)
            Console.WriteLine($"[VERBOSE] {message}");
    }

    private void LogWarning(string message)
    {
        Console.WriteLine($"[WARNING] {message}");
    }

    private void LogError(string message)
    {
        Console.WriteLine($"[ERROR] {message}");
    }
}
