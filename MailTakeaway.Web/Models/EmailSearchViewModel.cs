using MailTakeaway.Core.Models;

namespace MailTakeaway.Web.Models;

public class EmailSearchViewModel
{
    public List<EmailIndexEntry> Emails { get; set; } = new();
    public string? SearchTerm { get; set; }
    public string? FromFilter { get; set; }
    public string? ToFilter { get; set; }
    public string? FolderFilter { get; set; }
    public List<string> AvailableFolders { get; set; } = new();
    public List<string> IncludeFolders { get; set; } = new();
    public List<string> ExcludeFolders { get; set; } = new();
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public bool? HasAttachments { get; set; }
    public List<string> AvailableAttachmentTypes { get; set; } = new();
    public Dictionary<string, int> AttachmentTypeCounts { get; set; } = new();
    public string? AttachmentType { get; set; }
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public ParseStatistics? Statistics { get; set; }
    public bool ThreadView { get; set; } = true;
}
