using System.Text;
using HvViewer.App.Events;
using HvViewer.Core.Export;
using Microsoft.Win32;

namespace HvViewer.App.Services;

/// <summary>Asks where to save with a Save dialog, then writes the results as CSV or HTML.</summary>
public sealed class ExportService : IExportService
{
    public string? Export(string title, string subtitle, IReadOnlyList<ExportRow> rows)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export events",
            Filter = "CSV file (*.csv)|*.csv|HTML page (*.html)|*.html",
            FileName = $"eviewer-events-{DateTime.Now:yyyyMMdd-HHmm}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog() != true) return null; // cancelled

        try
        {
            using var stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            if (dialog.FileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) ExportWriter.WriteHtml(writer, title, subtitle, rows);
            else ExportWriter.WriteCsv(writer, rows);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Could not save the file: " + ex.Message;
        }
    }
}
