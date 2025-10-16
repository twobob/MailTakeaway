using Microsoft.AspNetCore.Mvc;
using MailTakeaway.Web.Models;
using MailTakeaway.Core.Models;

namespace MailTakeaway.Web.Controllers;

public class HomeController : Controller
{
    private readonly EmailIndexService _indexService;
    private readonly IHostApplicationLifetime _lifetime;

    public HomeController(EmailIndexService indexService, IHostApplicationLifetime lifetime)
    {
        _indexService = indexService;
        _lifetime = lifetime;
    }

    public IActionResult Index(
        string? search,
        string? from,
        string? to,
        string? folder,
        string? includeFolders,
        string? excludeFolders,
        DateTime? dateFrom,
        DateTime? dateTo,
        bool? hasAttachments,
        string? attachmentType,
        int page = 1,
        int pageSize = 10,
        bool threadView = false)
    {
        if (!_indexService.IsInitialized)
        {
            return View("Loading");
        }

        var includeFolderList = string.IsNullOrEmpty(includeFolders) 
            ? null 
            : includeFolders.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()).ToList();
        var excludeFolderList = string.IsNullOrEmpty(excludeFolders) 
            ? null 
            : excludeFolders.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()).ToList();

        var skip = (page - 1) * pageSize;
        var emails = _indexService.Search(search, from, to, folder, includeFolderList, excludeFolderList, dateFrom, dateTo, hasAttachments, attachmentType, skip, pageSize);
        var totalCount = _indexService.GetTotalCount(search, from, to, folder, includeFolderList, excludeFolderList, dateFrom, dateTo, hasAttachments, attachmentType);

        // Group by thread if thread view is enabled
        var emailList = emails.ToList();
        var displayEmails = new List<EmailIndexEntry>();
        var processedMessageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (threadView)
        {
            foreach (var email in emailList)
            {
                if (processedMessageIds.Contains(email.MessageId)) continue;
                
                // Find root of thread
                var threadRoot = email;
                var current = email;
                while (!string.IsNullOrEmpty(current.InReplyTo))
                {
                    var parent = _indexService.GetByMessageId(current.InReplyTo);
                    if (parent == null) break;
                    threadRoot = parent;
                    current = parent;
                }
                
                if (!processedMessageIds.Contains(threadRoot.MessageId))
                {
                    displayEmails.Add(threadRoot);
                    var thread = _indexService.GetThread(threadRoot.MessageId);
                    foreach (var msg in thread)
                    {
                        processedMessageIds.Add(msg.MessageId);
                    }
                }
            }
        }
        else
        {
            displayEmails = emailList;
        }

        var model = new EmailSearchViewModel
        {
            Emails = displayEmails,
            SearchTerm = search,
            FromFilter = from,
            ToFilter = to,
            FolderFilter = folder,
            AvailableFolders = _indexService.GetAllFolders(),
            IncludeFolders = includeFolderList ?? new List<string>(),
            ExcludeFolders = excludeFolderList ?? new List<string>(),
            AvailableAttachmentTypes = _indexService.GetAllAttachmentTypes(),
            AttachmentTypeCounts = _indexService.GetAttachmentTypeCounts(),
            AttachmentType = attachmentType,
            DateFrom = dateFrom,
            DateTo = dateTo,
            HasAttachments = hasAttachments,
            CurrentPage = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            Statistics = _indexService.GetStatistics(),
            ThreadView = threadView
        };

        return View(model);
    }

    public IActionResult Details(string id)
    {
        if (!_indexService.IsInitialized)
        {
            return View("Loading");
        }

        var email = _indexService.GetByMessageId(id);
        if (email == null)
        {
            return NotFound();
        }

        return View(email);
    }

    public IActionResult Statistics()
    {
        if (!_indexService.IsInitialized)
        {
            return View("Loading");
        }

        return View(_indexService.GetStatistics());
    }

    public IActionResult Thread(string id)
    {
        if (!_indexService.IsInitialized)
        {
            return View("Loading");
        }

        var thread = _indexService.GetThread(id);
        if (!thread.Any())
        {
            return NotFound();
        }

        return View(thread);
    }

    public IActionResult Errors()
    {
        if (!_indexService.IsInitialized)
        {
            return View("Loading");
        }

        return View(_indexService.GetStatistics());
    }

    public IActionResult Progress()
    {
        var progress = _indexService.GetProgress();
        Console.WriteLine($"[Progress] IsIndexing={((dynamic)progress).IsIndexing}, IsInitialized={((dynamic)progress).IsInitialized}");
        return Json(progress);
    }

    [HttpPost]
    public IActionResult Stop()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            _lifetime.StopApplication();
        });
        
        return Content("Server is stopping...");
    }

    [HttpPost]
    public IActionResult ClearCache()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(500); // Give time for response to be sent
            
            // Delete cache file
            var cacheFile = "email_index_cache.json";
            try
            {
                if (System.IO.File.Exists(cacheFile))
                {
                    System.IO.File.Delete(cacheFile);
                    Console.WriteLine($"Cache file deleted: {cacheFile}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to delete cache: {ex.Message}");
            }
            
            // Create a PowerShell script to restart the server
            var scriptPath = Path.Combine(Path.GetTempPath(), "restart_mailtakeaway.ps1");
            var projectPath = Directory.GetCurrentDirectory();
            
            // Write PowerShell script that waits, then restarts
            var scriptContent = $@"
Start-Sleep -Seconds 3
Set-Location '{projectPath}'
Start-Process powershell -WindowStyle Hidden -ArgumentList '-NoProfile', '-Command', ""Set-Location '{projectPath}'; dotnet run""
";
            
            try
            {
                System.IO.File.WriteAllText(scriptPath, scriptContent);
                
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                
                System.Diagnostics.Process.Start(startInfo);
                Console.WriteLine($"Restart script created and executed: {scriptPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create/start restart script: {ex.Message}");
            }
            
            _lifetime.StopApplication();
        });
        
        return Content("Clearing cache and restarting server...");
    }

    [HttpPost]
    public IActionResult Restart()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(500); // Give time for response to be sent
            
            // Create a PowerShell script to restart the server
            var scriptPath = Path.Combine(Path.GetTempPath(), "restart_mailtakeaway.ps1");
            var projectPath = Directory.GetCurrentDirectory();
            
            // Write PowerShell script that waits, then restarts
            var scriptContent = $@"
Start-Sleep -Seconds 3
Set-Location '{projectPath}'
Start-Process powershell -WindowStyle Hidden -ArgumentList '-NoProfile', '-Command', ""Set-Location '{projectPath}'; dotnet run""
";
            
            try
            {
                System.IO.File.WriteAllText(scriptPath, scriptContent);
                
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                
                System.Diagnostics.Process.Start(startInfo);
                Console.WriteLine($"Restart script created and executed: {scriptPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create/start restart script: {ex.Message}");
            }
            
            _lifetime.StopApplication();
        });
        
        return Content("Server is restarting...");
    }

    public IActionResult Attachment(string messageId, string fileName)
    {
        if (!_indexService.IsInitialized)
        {
            return NotFound("Index not initialized");
        }

        var attachment = _indexService.GetAttachment(messageId, fileName);
        if (attachment == null)
        {
            return NotFound($"Attachment not found: {fileName}");
        }

        // Determine if we should display inline or force download
        var isViewable = IsViewableType(attachment.ContentType, fileName);
        var contentDisposition = isViewable ? "inline" : "attachment";
        
        Response.Headers["Content-Disposition"] = $"{contentDisposition}; filename=\"{fileName}\"";
        return File(attachment.Data, attachment.ContentType);
    }

    private static bool IsViewableType(string contentType, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        
        // Images
        if (contentType.StartsWith("image/") || new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg", ".webp" }.Contains(ext))
            return true;
            
        // PDF
        if (contentType == "application/pdf" || ext == ".pdf")
            return true;
            
        // Text files
        if (contentType.StartsWith("text/") || new[] { ".txt", ".cs", ".c", ".cpp", ".java", ".js", ".html", ".xml", ".json", ".css", ".md" }.Contains(ext))
            return true;
            
        // Audio
        if (contentType.StartsWith("audio/") || new[] { ".mp3", ".wav", ".ogg", ".m4a", ".aac" }.Contains(ext))
            return true;
            
        return false;
    }
}
