using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using MailTakeaway.Core.Models;

namespace MailTakeaway.Core.Services;

public class ExportService
{
    public async Task ExportToJsonAsync(
        Dictionary<string, EmailIndexEntry> index,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"--- Exporting index to JSON: {outputPath} ---");

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(index.Values, options);
        await File.WriteAllTextAsync(outputPath, json, Encoding.UTF8, cancellationToken);

        Console.WriteLine($"JSON export complete: {index.Count} messages exported");
        Console.WriteLine($"File: {Path.GetFullPath(outputPath)}");
        Console.WriteLine("------------------------------------");
        Console.WriteLine();
    }

    public async Task ExportToCsvAsync(
        Dictionary<string, EmailIndexEntry> index,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"--- Exporting index to CSV: {outputPath} ---");

        await using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
        await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        // Write headers
        csv.WriteField("MessageId");
        csv.WriteField("From");
        csv.WriteField("To");
        csv.WriteField("Subject");
        csv.WriteField("Date");
        csv.WriteField("Source");
        csv.WriteField("HasAttachments");
        csv.WriteField("AttachmentCount");
        csv.WriteField("AttachmentNames");
        csv.WriteField("RawSize");
        csv.WriteField("BodyPreview");
        await csv.NextRecordAsync();

        foreach (var entry in index.Values)
        {
            csv.WriteField(entry.MessageId);
            csv.WriteField(entry.From);
            csv.WriteField(entry.To);
            csv.WriteField(entry.Subject);
            csv.WriteField(entry.UtcDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "");
            csv.WriteField(entry.MboxSource);
            csv.WriteField(entry.HasAttachments);
            csv.WriteField(entry.AttachmentCount);
            csv.WriteField(string.Join("; ", entry.AttachmentNames));
            csv.WriteField(entry.RawSize);
            csv.WriteField(entry.BodyPreview);
            await csv.NextRecordAsync();
        }

        Console.WriteLine($"CSV export complete: {index.Count} messages exported");
        Console.WriteLine($"File: {Path.GetFullPath(outputPath)}");
        Console.WriteLine("------------------------------------");
        Console.WriteLine();
    }
}
