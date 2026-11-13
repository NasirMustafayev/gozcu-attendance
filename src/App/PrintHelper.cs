using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using GozcuStandalone.Reporting;

namespace GozcuStandalone.App;

public static class PrintHelper
{
    // Opens the Windows print dialog (which also offers "Microsoft Print to PDF").
    public static void Print(ReportTable report, IReadOnlyList<string[]> rows)
    {
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;

        bool wide = report.Headers.Count > 6;
        if (wide && dlg.PrintTicket is not null) dlg.PrintTicket.PageOrientation = PageOrientation.Landscape;

        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10.5,
            PagePadding = new Thickness(40),
            PageWidth = dlg.PrintableAreaWidth,
            PageHeight = dlg.PrintableAreaHeight,
            ColumnWidth = double.PositiveInfinity,
        };
        if (wide)
        {
            // The dialog reports portrait dimensions until the ticket is applied; swap for landscape.
            doc.PageWidth = Math.Max(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight);
            doc.PageHeight = Math.Min(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight);
        }

        doc.Blocks.Add(new Paragraph(new Run(report.Title)) { FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) });
        doc.Blocks.Add(new Paragraph(new Run(report.Subtitle)) { Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 12) });

        var table = new Table { CellSpacing = 0 };
        for (int i = 0; i < report.Headers.Count; i++) table.Columns.Add(new TableColumn());

        var head = new TableRowGroup();
        var headRow = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)) };
        foreach (var h in report.Headers)
            headRow.Cells.Add(new TableCell(new Paragraph(new Run(h)) { FontWeight = FontWeights.SemiBold }) { Padding = new Thickness(4, 4, 6, 4), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 0, 1) });
        head.Rows.Add(headRow);
        table.RowGroups.Add(head);

        var body = new TableRowGroup();
        foreach (var r in rows)
        {
            var row = new TableRow();
            foreach (var cell in r)
                row.Cells.Add(new TableCell(new Paragraph(new Run(cell))) { Padding = new Thickness(4, 3, 6, 3), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 0.5) });
            body.Rows.Add(row);
        }
        table.RowGroups.Add(body);
        doc.Blocks.Add(table);

        doc.Blocks.Add(new Paragraph(new Run(report.Footer)) { Foreground = Brushes.DimGray, Margin = new Thickness(0, 12, 0, 0) });
        doc.Blocks.Add(new Paragraph(new Run("Printed " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"))) { Foreground = Brushes.Gray, FontSize = 9 });

        dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, report.Title);
    }
}
