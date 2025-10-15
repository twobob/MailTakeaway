namespace MailTakeaway.Core.Models;

public class EmailIndexEntry
{
    public string MessageId { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTime? UtcDate { get; set; }
    public string MboxSource { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public List<string> AllFolders { get; set; } = new();
    public bool HasAttachments { get; set; }
    public int AttachmentCount { get; set; }
    public List<string> AttachmentNames { get; set; } = new();
    public Dictionary<string, EmailAttachment>? AttachmentData { get; set; }
    public string BodyPreview { get; set; } = string.Empty;
    public string? HtmlBody { get; set; }
    public string? TextBody { get; set; }
    public long RawSize { get; set; }
    public string InReplyTo { get; set; } = string.Empty;
    public List<string> References { get; set; } = new();
    
    public bool MatchesFilters(EmailFilterOptions filters)
    {
        if (!string.IsNullOrEmpty(filters.FromContains) && 
            !From.Contains(filters.FromContains, StringComparison.OrdinalIgnoreCase))
            return false;
            
        if (!string.IsNullOrEmpty(filters.ToContains) && 
            !To.Contains(filters.ToContains, StringComparison.OrdinalIgnoreCase))
            return false;
            
        if (!string.IsNullOrEmpty(filters.SubjectContains) && 
            !Subject.Contains(filters.SubjectContains, StringComparison.OrdinalIgnoreCase))
            return false;
            
        if (!string.IsNullOrEmpty(filters.FolderContains) && 
            !Folder.Contains(filters.FolderContains, StringComparison.OrdinalIgnoreCase))
            return false;
            
        if (filters.DateFrom.HasValue && UtcDate.HasValue && UtcDate.Value < filters.DateFrom.Value)
            return false;
            
        if (filters.DateTo.HasValue && UtcDate.HasValue && UtcDate.Value > filters.DateTo.Value)
            return false;
            
        if (filters.HasAttachmentsOnly && !HasAttachments)
            return false;
            
        return true;
    }
}
