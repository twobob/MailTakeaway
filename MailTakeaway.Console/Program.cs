using MailTakeaway.Core.Models;
using MailTakeaway.Core.Services;

namespace MailTakeaway.ConsoleApp;

class Program
{
    private const string DEFAULT_ARCHIVE_PATH = "MailDump.tgz";
    private const int MESSAGES_PER_PAGE = 1;
    
    private static List<EmailIndexEntry> _emails = new();
    private static int _currentPage = 0;
    private static string _searchTerm = "";
    private static string _folderFilter = "";

    static async Task<int> Main(string[] args)
    {
        try
        {
            Console.CursorVisible = false;
            
            var archivePath = args.Length > 0 ? args[0] : DEFAULT_ARCHIVE_PATH;
            if (!Path.IsPathRooted(archivePath))
            {
                archivePath = Path.Combine(Directory.GetCurrentDirectory(), archivePath);
            }

            ShowLoadingScreen(archivePath);

            var parser = new MboxParser(
                filters: new EmailFilterOptions(),
                bodyPreviewLength: 500,
                progressInterval: 50,
                verboseLogging: false,
                stopOnError: false);

            var archiveProcessor = new ArchiveProcessor(parser);
            var (index, stats) = await archiveProcessor.ProcessTarGzArchiveAsync(archivePath);

            _emails = index.Values.OrderByDescending(e => e.UtcDate).ToList();

            if (_emails.Count == 0)
            {
                Console.Clear();
                Console.WriteLine("No emails found in archive.");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return 0;
            }

            // Interactive browser
            await RunInteractiveBrowser(stats);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Clear();
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            return 1;
        }
        finally
        {
            Console.CursorVisible = true;
        }
    }

    private static void ShowLoadingScreen(string archivePath)
    {
        Console.Clear();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              Mail Takeaway - Email Browser                   ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"Loading: {Path.GetFileName(archivePath)}");
        Console.WriteLine("Please wait...");
        Console.WriteLine();
    }

    private static async Task RunInteractiveBrowser(ParseStatistics stats)
    {
        bool running = true;
        
        while (running)
        {
            var filteredEmails = GetFilteredEmails();
            
            if (filteredEmails.Count == 0)
            {
                ShowNoResults();
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Q)
                    break;
                if (key.Key == ConsoleKey.C)
                {
                    _searchTerm = "";
                    _folderFilter = "";
                    _currentPage = 0;
                }
                continue;
            }

            _currentPage = Math.Clamp(_currentPage, 0, filteredEmails.Count - 1);
            
            DisplayEmail(filteredEmails[_currentPage], _currentPage, filteredEmails.Count, stats);

            var input = Console.ReadKey(true);
            
            switch (input.Key)
            {
                case ConsoleKey.RightArrow:
                case ConsoleKey.N:
                case ConsoleKey.Spacebar:
                    if (_currentPage < filteredEmails.Count - 1)
                        _currentPage++;
                    break;
                    
                case ConsoleKey.LeftArrow:
                case ConsoleKey.P:
                case ConsoleKey.Backspace:
                    if (_currentPage > 0)
                        _currentPage--;
                    break;
                    
                case ConsoleKey.Home:
                    _currentPage = 0;
                    break;
                    
                case ConsoleKey.End:
                    _currentPage = filteredEmails.Count - 1;
                    break;
                    
                case ConsoleKey.PageDown:
                    _currentPage = Math.Min(_currentPage + 10, filteredEmails.Count - 1);
                    break;
                    
                case ConsoleKey.PageUp:
                    _currentPage = Math.Max(_currentPage - 10, 0);
                    break;
                    
                case ConsoleKey.S:
                    await SearchPrompt();
                    _currentPage = 0;
                    break;
                    
                case ConsoleKey.F:
                    await FolderPrompt();
                    _currentPage = 0;
                    break;
                    
                case ConsoleKey.C:
                    _searchTerm = "";
                    _folderFilter = "";
                    _currentPage = 0;
                    break;
                    
                case ConsoleKey.E:
                    await ExportEmail(filteredEmails[_currentPage]);
                    break;
                    
                case ConsoleKey.Escape:
                case ConsoleKey.Q:
                    running = false;
                    break;
            }
        }
    }

    private static List<EmailIndexEntry> GetFilteredEmails()
    {
        var filtered = _emails.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchTerm))
        {
            filtered = filtered.Where(e =>
                e.Subject.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase) ||
                e.From.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase) ||
                e.To.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase) ||
                e.BodyPreview.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_folderFilter))
        {
            filtered = filtered.Where(e => e.AllFolders.Any(f => 
                f.Contains(_folderFilter, StringComparison.OrdinalIgnoreCase)));
        }

        return filtered.ToList();
    }

    private static void DisplayEmail(EmailIndexEntry email, int index, int total, ParseStatistics stats)
    {
        Console.Clear();
        
        var width = Math.Min(Console.WindowWidth, 120);
        var separator = new string('═', width);
        
        Console.WriteLine("╔" + separator + "╗");
        Console.WriteLine($"║ Mail Takeaway - Email Browser ({stats.SuccessfullyParsed} total indexed)" + new string(' ', width - 60) + "║");
        Console.WriteLine("╚" + separator + "╝");
        Console.WriteLine();
        
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Message {index + 1} of {total}");
        Console.ResetColor();
        
        if (!string.IsNullOrWhiteSpace(_searchTerm))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"🔍 Search: \"{_searchTerm}\"");
            Console.ResetColor();
        }
        
        if (!string.IsNullOrWhiteSpace(_folderFilter))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"📁 Folder: \"{_folderFilter}\"");
            Console.ResetColor();
        }
        
        Console.WriteLine(new string('─', width));
        Console.WriteLine();
        
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("Subject: ");
        Console.ResetColor();
        Console.WriteLine(TruncateWithEllipsis(email.Subject, width - 9));
        
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("   From: ");
        Console.ResetColor();
        Console.WriteLine(TruncateWithEllipsis(email.From, width - 9));
        
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("     To: ");
        Console.ResetColor();
        Console.WriteLine(TruncateWithEllipsis(email.To, width - 9));
        
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("   Date: ");
        Console.ResetColor();
        Console.WriteLine(email.UtcDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A");
        
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(" Folder: ");
        Console.ResetColor();
        Console.WriteLine(string.Join(", ", email.AllFolders));
        
        if (email.HasAttachments)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write($"📎 {email.AttachmentCount} attachment(s): ");
            Console.ResetColor();
            Console.WriteLine(string.Join(", ", email.AttachmentNames.Take(5)) + 
                (email.AttachmentCount > 5 ? "..." : ""));
        }
        
        Console.WriteLine();
        Console.WriteLine(new string('─', width));
        Console.WriteLine();
        
        Console.ForegroundColor = ConsoleColor.White;
        var bodyLines = WrapText(email.HtmlBody ?? email.TextBody ?? email.BodyPreview, width);
        var maxLines = Console.WindowHeight - 25;
        foreach (var line in bodyLines.Take(maxLines))
        {
            Console.WriteLine(line);
        }
        Console.ResetColor();
        
        Console.WriteLine();
        Console.WriteLine(new string('─', width));
        
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Controls: [←/→] Next/Prev  [Home/End] First/Last  [PgUp/PgDn] Jump 10");
        Console.WriteLine("          [S] Search  [F] Filter Folder  [C] Clear Filters  [E] Export  [Q/ESC] Quit");
        Console.ResetColor();
    }

    private static void ShowNoResults()
    {
        Console.Clear();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              Mail Takeaway - Email Browser                   ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("No emails match your current filters.");
        Console.ResetColor();
        Console.WriteLine();
        if (!string.IsNullOrWhiteSpace(_searchTerm))
            Console.WriteLine($"Search: \"{_searchTerm}\"");
        if (!string.IsNullOrWhiteSpace(_folderFilter))
            Console.WriteLine($"Folder: \"{_folderFilter}\"");
        Console.WriteLine();
        Console.WriteLine("Press [C] to clear filters or [ESC] to quit.");
    }

    private static async Task SearchPrompt()
    {
        Console.Clear();
        Console.WriteLine("Enter search term (Subject/From/To/Body):");
        Console.CursorVisible = true;
        _searchTerm = Console.ReadLine() ?? "";
        Console.CursorVisible = false;
        await Task.CompletedTask;
    }

    private static async Task FolderPrompt()
    {
        Console.Clear();
        Console.WriteLine("Enter folder name to filter:");
        Console.CursorVisible = true;
        _folderFilter = Console.ReadLine() ?? "";
        Console.CursorVisible = false;
        await Task.CompletedTask;
    }

    private static async Task ExportEmail(EmailIndexEntry email)
    {
        var filename = $"email_{email.MessageId.Replace("<", "").Replace(">", "").Replace("@", "_at_")}.txt";
        var content = $"Subject: {email.Subject}\nFrom: {email.From}\nTo: {email.To}\nDate: {email.UtcDate}\n\n{email.TextBody ?? email.BodyPreview}";
        await File.WriteAllTextAsync(filename, content);
        
        Console.SetCursorPosition(0, Console.WindowHeight - 1);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"Exported to: {filename} - Press any key...");
        Console.ResetColor();
        Console.ReadKey(true);
    }

    private static string TruncateWithEllipsis(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text.Substring(0, maxLength - 3) + "...";
    }

    private static List<string> WrapText(string text, int width)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;

        var paragraphs = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length <= width)
            {
                lines.Add(paragraph);
            }
            else
            {
                var words = paragraph.Split(' ');
                var currentLine = "";
                
                foreach (var word in words)
                {
                    if ((currentLine + word).Length > width)
                    {
                        if (!string.IsNullOrEmpty(currentLine))
                            lines.Add(currentLine.TrimEnd());
                        currentLine = word + " ";
                    }
                    else
                    {
                        currentLine += word + " ";
                    }
                }
                
                if (!string.IsNullOrEmpty(currentLine))
                    lines.Add(currentLine.TrimEnd());
            }
        }
        
        return lines;
    }
}
