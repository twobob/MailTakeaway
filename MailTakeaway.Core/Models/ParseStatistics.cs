namespace MailTakeaway.Core.Models;

public class ParseStatistics
{
    public int TotalMessages { get; set; }
    public int SuccessfullyParsed { get; set; }
    public int Duplicates { get; set; }
    public int FilteredOut { get; set; }
    public int ParseErrors { get; set; }
    public int MboxFilesProcessed { get; set; }
    public List<string> ParseErrorMessages { get; set; } = new();
    
    public void PrintSummary()
    {
        Console.WriteLine("=============================================================");
        Console.WriteLine("PROCESSING STATISTICS");
        Console.WriteLine("=============================================================");
        Console.WriteLine($"MBOX files processed:    {MboxFilesProcessed}");
        Console.WriteLine($"Total messages found:    {TotalMessages}");
        Console.WriteLine($"Successfully parsed:     {SuccessfullyParsed}");
        Console.WriteLine($"Duplicates skipped:      {Duplicates}");
        Console.WriteLine($"Filtered out:            {FilteredOut}");
        Console.WriteLine($"Parse errors:            {ParseErrors}");
        Console.WriteLine("=============================================================");
    }
}
