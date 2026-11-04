using ClosedXML.Excel;
using CotizadorInterno.Web.Models.Puntajes;

namespace CotizadorInterno.Web.Services.Puntajes;

public static class ScoreMonthCloseExcelBuilder
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // This is a rendering of the completed operation, not a new query or a new close.
    public static byte[] Build(ScoreMonthCloseExportRequest report)
    {
        using var workbook = new XLWorkbook();
        workbook.Style.Font.FontName = "Calibri";
        workbook.Style.Font.FontSize = 11;
        var summary = workbook.AddWorksheet("Resumen");
        var detail = workbook.AddWorksheet("Detalle por producto y cliente");
        var headers = new[]
        {
            "Periodo", "Cliente", "Producto", "Acción", "Resultado", "Líneas añadidas",
            "Cantidad anterior", "Cantidad del cambio", "Cantidad final", "Cambio realizado",
            "Estado final / datos del reporte", "Detalle / advertencias", "Tabla destino",
            "ID registro Cloud", "ID cliente", "ID producto", "ID aprovisionamiento", "ID línea", "ID cierre"
        };

        for (var column = 0; column < headers.Length; column++)
            detail.Cell(1, column + 1).Value = headers[column];

        for (var index = 0; index < report.Logs.Count; index++)
        {
            var entry = report.Logs[index];
            var row = index + 2;
            detail.Cell(row, 1).Value = report.PeriodKey;
            detail.Cell(row, 2).Value = entry.ClientName;
            detail.Cell(row, 3).Value = entry.ProductName;
            detail.Cell(row, 4).Value = ActionLabel(entry.Action);
            detail.Cell(row, 5).Value = LevelLabel(entry.Level);
            detail.Cell(row, 6).Value = entry.Action == "create" ? 1 : 0;
            SetQuantity(detail.Cell(row, 7), entry.PreviousQuantity);
            SetQuantity(detail.Cell(row, 8), entry.AppliedQuantity);
            SetQuantity(detail.Cell(row, 9), entry.FinalQuantity);
            // Explicit string values keep product names and error text from becoming formulas.
            detail.Cell(row, 10).Value = entry.Message;
            detail.Cell(row, 11).Value = entry.FinalState;
            detail.Cell(row, 12).Value = entry.Detail;
            detail.Cell(row, 13).Value = "cr07a_salesperformancerecord";
            detail.Cell(row, 14).Value = entry.SalesPerformanceRecordId;
            detail.Cell(row, 15).Value = entry.ClientId;
            detail.Cell(row, 16).Value = entry.ProductId;
            detail.Cell(row, 17).Value = entry.RecordId;
            detail.Cell(row, 18).Value = entry.LineKey;
            detail.Cell(row, 19).Value = report.BatchId;
            detail.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = LevelColor(entry.Level);
        }

        var detailRange = detail.Range(1, 1, report.Logs.Count + 1, headers.Length);
        detailRange.Style.Alignment.WrapText = true;
        detailRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        detailRange.SetAutoFilter();
        detail.Columns(1, headers.Length).Width = 24;
        detail.Column(1).Width = 12;
        detail.Columns(2, 3).Width = 34;
        detail.Columns(6, 9).Width = 18;
        detail.Columns(6, 9).Style.NumberFormat.Format = "#,##0";
        detail.Columns(10, 12).Width = 70;
        detail.Columns(13, 19).Width = 38;
        detail.Rows(2, report.Logs.Count + 1).Height = 90;
        StyleHeader(detail.Range(1, 1, 1, headers.Length));
        detail.Row(1).Height = 32;
        detail.SheetView.FreezeRows(1);
        detail.SheetView.FreezeColumns(3);

        summary.Column(1).Width = 34;
        summary.Columns(2, 4).Width = 28;
        summary.Range("A1:D1").Merge().Value = report.Operation == "undo"
            ? "Puntajes · Reversión del cierre mensual"
            : "Puntajes · Cierre mensual a Productos Cloud";
        StyleHeader(summary.Range("A1:D1"));
        summary.Row(1).Height = 32;
        SummaryText(summary, 3, "Periodo", $"{report.PeriodLabel} ({report.PeriodKey})");
        SummaryText(summary, 4, "ID cierre", report.BatchId);
        SummaryText(summary, 5, "Exportado (UTC)", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        SummaryText(summary, 6, "Resultado", report.Message);
        summary.Row(6).Height = 60;
        SummaryText(summary, 8, "Contenido", "Una fila por producto y cliente en el orden del reporte. Los incrementos modifican una línea existente; no cuentan como líneas añadidas.");
        SummaryText(summary, 9, "Cantidades", "Cantidad del cambio: unidades añadidas o retiradas en esta operación. Una celda vacía significa que el reporte no confirma esa cantidad.");
        SummaryText(summary, 10, "Exclusiones y errores", "El estado descriptivo de una línea excluida o ya cerrada es de referencia. Los errores conservan su detalle y no se cuentan como cambios confirmados.");
        summary.Rows(8, 10).Height = 50;

        summary.Cell("A12").Value = "Acción";
        summary.Cell("B12").Value = "Líneas del reporte";
        StyleHeader(summary.Range("A12:B12"));
        var summaryRow = 13;
        foreach (var group in report.Logs.GroupBy(entry => entry.Action))
        {
            summary.Cell(summaryRow, 1).Value = ActionLabel(group.Key);
            summary.Cell(summaryRow, 2).Value = group.Count();
            summaryRow++;
        }
        summary.Cell(summaryRow, 1).Value = "Total de líneas del reporte";
        summary.Cell(summaryRow, 2).Value = report.Logs.Count;
        summary.Range(summaryRow, 1, summaryRow, 2).Style.Font.Bold = true;

        summaryRow += 2;
        foreach (var level in new[] { "success", "warning", "error", "info" })
        {
            summary.Cell(summaryRow, 1).Value = LevelLabel(level);
            summary.Cell(summaryRow, 2).Value = report.Logs.Count(entry => entry.Level == level);
            summary.Range(summaryRow, 1, summaryRow, 2).Style.Fill.BackgroundColor = LevelColor(level);
            summaryRow++;
        }
        summary.RangeUsed()!.Style.Alignment.WrapText = true;
        summary.RangeUsed()!.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        summary.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void SetQuantity(IXLCell cell, int? value)
    {
        if (value.HasValue)
            cell.Value = value.Value;
    }

    private static void SummaryText(IXLWorksheet sheet, int row, string label, string value)
    {
        sheet.Cell(row, 1).Value = label;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Range(row, 2, row, 4).Merge().Value = value;
    }

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#15324F");
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Font.Bold = true;
    }

    private static XLColor LevelColor(string level) => XLColor.FromHtml(level switch
    {
        "success" => "#EDF9F2",
        "warning" => "#FFFBEB",
        "error" => "#FFF1F4",
        _ => "#EEF5FF"
    });

    private static string LevelLabel(string level) => level switch
    {
        "success" => "Correcto",
        "warning" => "Revisión manual pendiente",
        "error" => "Error",
        _ => "Informativo"
    };

    private static string ActionLabel(string action) => action switch
    {
        "create" => "Línea añadida",
        "increment" => "Cantidad incrementada",
        "excluded" => "Excluida",
        "already-closed" => "Ya estaba cerrada",
        "error" => "Error al cerrar",
        "undo-create" => "Línea eliminada al deshacer",
        "undo-increment" => "Cantidad restaurada",
        "undo-excluded" => "Exclusión liberada",
        "undo-error" => "Error al deshacer",
        _ => string.IsNullOrWhiteSpace(action) ? "Información" : action
    };
}
