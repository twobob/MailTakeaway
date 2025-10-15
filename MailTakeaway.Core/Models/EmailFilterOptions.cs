namespace MailTakeaway.Core.Models;

public class EmailFilterOptions
{
    public string FromContains { get; set; } = string.Empty;
    public string ToContains { get; set; } = string.Empty;
    public string SubjectContains { get; set; } = string.Empty;
    public string FolderContains { get; set; } = string.Empty;
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public bool HasAttachmentsOnly { get; set; }
}
