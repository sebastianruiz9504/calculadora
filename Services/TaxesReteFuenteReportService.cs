using System.Globalization;
using ClosedXML.Excel;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services;

public sealed class TaxesReteFuenteReportResult
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string PeriodLabel { get; set; } = "";
    public string DateRangeLabel { get; set; } = "";
    public string FileName { get; set; } = "";
    public byte[] ExcelContent { get; set; } = Array.Empty<byte>();
    public decimal AutoFuenteTotal { get; set; }
    public decimal ExpensesReteFuenteTotal { get; set; }
    public decimal TotalReteFuente { get; set; }
    public int AutoFuenteRows { get; set; }
    public int ExpensesRows { get; set; }
    public int CreditNoteRows { get; set; }
}

public interface ITaxesReteFuenteReportService
{
    Task<TaxesReteFuenteReportResult> BuildAsync(
        int year,
        int month,
        DateOnly? generatedDate = null,
        CancellationToken ct = default);

    Task<TaxesReteFuenteReportResult> BuildAsync(
        TaxesDashboardRequestDto request,
        DateOnly? generatedDate = null,
        CancellationToken ct = default);
}

public sealed class TaxesReteFuenteReportService : ITaxesReteFuenteReportService
{
    private readonly IDataverseService _dataverse;

    public TaxesReteFuenteReportService(IDataverseService dataverse)
    {
        _dataverse = dataverse;
    }

    public async Task<TaxesReteFuenteReportResult> BuildAsync(
        int year,
        int month,
        DateOnly? generatedDate = null,
        CancellationToken ct = default)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            throw new InvalidOperationException("El periodo de retefuente no es valido.");

        return await BuildAsync(
            new TaxesDashboardRequestDto
            {
                Year = year,
                Period = BillingPeriodKind.Month.ToKey(),
                Value = month,
                ReteFuenteYear = year,
                ReteFuenteMonth = month
            },
            generatedDate,
            ct);
    }

    public async Task<TaxesReteFuenteReportResult> BuildAsync(
        TaxesDashboardRequestDto request,
        DateOnly? generatedDate = null,
        CancellationToken ct = default)
    {
        var today = generatedDate ?? ResolveBogotaToday();
        request ??= new TaxesDashboardRequestDto();
        request.Year ??= today.Year;
        var dashboard = await _dataverse.GetTaxesDashboardAsync(request, ct);
        var section = dashboard.ReteFuente;
        var autoTable = FindTaxReportTable(section, "autofuente") ?? BuildDefaultAutoFuenteTable();
        var expensesTable = FindTaxReportTable(section, "retefuente-gastos") ?? BuildDefaultExpensesTable();
        var creditNotesTable = FindTaxReportTable(section, "notas-credito") ?? BuildDefaultCreditNotesTable();
        var content = BuildWorkbook(section, autoTable, expensesTable, creditNotesTable);
        var periodToken = BuildSafeFileName(FirstNonEmpty(section.Filter.ValueLabel, section.PeriodLabel, "retefuente"));

        return new TaxesReteFuenteReportResult
        {
            Year = section.Filter.Year,
            Month = section.Filter.Value,
            PeriodLabel = section.PeriodLabel,
            DateRangeLabel = section.DateRangeLabel,
            FileName = $"reporte-retefuente-{section.Filter.Year}-{periodToken}-{today:yyyyMMdd}.xlsx",
            ExcelContent = content,
            AutoFuenteTotal = autoTable.TotalAmountValue,
            ExpensesReteFuenteTotal = expensesTable.TotalAmountValue,
            TotalReteFuente = section.TotalValue,
            AutoFuenteRows = autoTable.Rows.Count,
            ExpensesRows = expensesTable.Rows.Count,
            CreditNoteRows = creditNotesTable.Rows.Count
        };
    }

    private static byte[] BuildWorkbook(
        TaxesSectionDto section,
        TaxReportTableDto autoTable,
        TaxReportTableDto expensesTable,
        TaxReportTableDto creditNotesTable)
    {
        using var workbook = new XLWorkbook();
        AddReteFuenteSummaryWorksheet(workbook, section, autoTable, expensesTable);
        AddTaxReportWorksheet(workbook, "Autofuente", autoTable);
        AddTaxReportWorksheet(workbook, "ReteFuente gastos", expensesTable);
        AddTaxReportWorksheet(workbook, "Notas credito", creditNotesTable);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static TaxReportTableDto? FindTaxReportTable(TaxesSectionDto section, string key) =>
        section.ReportDetails.Tables.FirstOrDefault(table => string.Equals(table.Key, key, StringComparison.OrdinalIgnoreCase));

    private static TaxReportTableDto BuildDefaultAutoFuenteTable() => new()
    {
        Label = "Autofuente",
        DateColumnLabel = "Fecha emision",
        NameColumnLabel = "Cliente",
        TotalColumnLabel = "Total factura",
        BaseColumnLabel = "Base antes de IVA",
        AmountColumnLabel = "Autofuente",
        ShowBaseColumn = true
    };

    private static TaxReportTableDto BuildDefaultExpensesTable() => new()
    {
        Label = "ReteFuente gastos",
        DateColumnLabel = "Fecha pago",
        NameColumnLabel = "Receptor",
        TotalColumnLabel = "Total factura",
        BaseColumnLabel = "Base antes de IVA",
        AmountColumnLabel = "ReteFuente",
        CategoryColumnLabel = "Tipo persona",
        ShowBaseColumn = true,
        ShowReteFuentePercentColumn = true,
        ShowReteIcaPercentColumn = true,
        ShowCategoryColumn = true
    };

    private static TaxReportTableDto BuildDefaultCreditNotesTable() => new()
    {
        Label = "Notas credito",
        DateColumnLabel = "Fecha creacion",
        DocumentColumnLabel = "Nota credito",
        NameColumnLabel = "Factura relacionada",
        CustomerIdentificationColumnLabel = "NIT cliente",
        TotalColumnLabel = "Total nota credito",
        AmountColumnLabel = "IVA nota credito",
        ShowCustomerIdentificationColumn = true
    };

    private static void AddReteFuenteSummaryWorksheet(
        XLWorkbook workbook,
        TaxesSectionDto section,
        TaxReportTableDto autoTable,
        TaxReportTableDto expensesTable)
    {
        var worksheet = workbook.Worksheets.Add("Resumen");

        worksheet.Cell(1, 1).Value = "Resumen Retefuente";
        worksheet.Cell(2, 1).Value = section.PeriodLabel;
        worksheet.Cell(2, 2).Value = section.DateRangeLabel;
        worksheet.Cell(4, 1).Value = "Concepto";
        worksheet.Cell(4, 2).Value = "Valor";
        worksheet.Cell(5, 1).Value = "Autofuente";
        worksheet.Cell(5, 2).Value = autoTable.TotalAmountValue;
        worksheet.Cell(6, 1).Value = "ReteFuente gastos";
        worksheet.Cell(6, 2).Value = expensesTable.TotalAmountValue;
        worksheet.Cell(7, 1).Value = "Total retefuente a pagar";
        worksheet.Cell(7, 2).Value = section.TotalValue;
        worksheet.Cell(9, 1).Value = "Formula";
        worksheet.Cell(9, 2).Value = "Autofuente + ReteFuente gastos";

        var usedRange = worksheet.Range(1, 1, 9, 2);
        usedRange.Style.Font.FontName = "Aptos";
        var titleRange = worksheet.Range(1, 1, 1, 2).Merge();
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 16;
        worksheet.Range(4, 1, 4, 2).Style.Font.Bold = true;
        worksheet.Range(4, 1, 4, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF3FF");
        worksheet.Range(7, 1, 7, 2).Style.Font.Bold = true;
        worksheet.Range(7, 1, 7, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F9FF");
        worksheet.Range(5, 2, 7, 2).Style.NumberFormat.Format = "$ #,##0";
        worksheet.Columns().AdjustToContents();
    }

    private static void AddTaxReportWorksheet(XLWorkbook workbook, string sheetName, TaxReportTableDto table)
    {
        var worksheet = workbook.Worksheets.Add(sheetName);
        var headers = new List<string>
        {
            table.DateColumnLabel,
            string.IsNullOrWhiteSpace(table.DocumentColumnLabel) ? "Numero factura" : table.DocumentColumnLabel,
            table.NameColumnLabel
        };

        if (table.ShowCustomerIdentificationColumn)
            headers.Add(string.IsNullOrWhiteSpace(table.CustomerIdentificationColumnLabel) ? "Identificacion" : table.CustomerIdentificationColumnLabel);

        if (table.ShowCategoryColumn)
            headers.Add(table.CategoryColumnLabel);

        headers.Add(table.TotalColumnLabel);

        if (table.ShowBaseColumn)
            headers.Add(table.BaseColumnLabel);

        headers.Add(table.AmountColumnLabel);

        if (table.ShowReteFuentePercentColumn)
            headers.Add("% rte fuente");

        if (table.ShowReteIcaPercentColumn)
            headers.Add("% rte ica");

        worksheet.Cell(1, 1).Value = table.Label;
        for (var index = 0; index < headers.Count; index++)
        {
            worksheet.Cell(3, index + 1).Value = headers[index];
        }

        var rowIndex = 4;
        foreach (var row in table.Rows)
        {
            var columnIndex = 1;
            worksheet.Cell(rowIndex, columnIndex++).Value = row.DateDisplay;
            worksheet.Cell(rowIndex, columnIndex++).Value = row.InvoiceNumber;
            worksheet.Cell(rowIndex, columnIndex++).Value = row.Name;

            if (table.ShowCustomerIdentificationColumn)
                worksheet.Cell(rowIndex, columnIndex++).Value = row.CustomerIdentification;

            if (table.ShowCategoryColumn)
                worksheet.Cell(rowIndex, columnIndex++).Value = row.Category;

            worksheet.Cell(rowIndex, columnIndex++).Value = row.TotalValue;

            if (table.ShowBaseColumn)
                worksheet.Cell(rowIndex, columnIndex++).Value = row.BaseValue;

            worksheet.Cell(rowIndex, columnIndex++).Value = row.AmountValue;

            if (table.ShowReteFuentePercentColumn)
                worksheet.Cell(rowIndex, columnIndex++).Value = row.ReteFuentePercent;

            if (table.ShowReteIcaPercentColumn)
                worksheet.Cell(rowIndex, columnIndex++).Value = row.ReteIcaPercent;

            rowIndex++;
        }

        var totalColumnIndex = 4
            + (table.ShowCustomerIdentificationColumn ? 1 : 0)
            + (table.ShowCategoryColumn ? 1 : 0);
        var amountColumnIndex = totalColumnIndex + (table.ShowBaseColumn ? 1 : 0) + 1;
        worksheet.Cell(rowIndex, 1).Value = "Total";
        worksheet.Cell(rowIndex, 2).Value = $"{table.Rows.Count:N0} registros";
        worksheet.Cell(rowIndex, totalColumnIndex).Value = table.TotalValue;

        if (table.ShowBaseColumn)
            worksheet.Cell(rowIndex, totalColumnIndex + 1).Value = table.TotalBaseValue;

        worksheet.Cell(rowIndex, amountColumnIndex).Value = table.TotalAmountValue;

        var usedRange = worksheet.Range(1, 1, rowIndex, headers.Count);
        usedRange.Style.Font.FontName = "Aptos";
        var titleRange = worksheet.Range(1, 1, 1, headers.Count).Merge();
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 16;
        worksheet.Range(3, 1, 3, headers.Count).Style.Font.Bold = true;
        worksheet.Range(3, 1, 3, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF3FF");
        worksheet.Range(rowIndex, 1, rowIndex, headers.Count).Style.Font.Bold = true;
        worksheet.Range(rowIndex, 1, rowIndex, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F9FF");
        worksheet.Range(4, totalColumnIndex, rowIndex, amountColumnIndex).Style.NumberFormat.Format = "$ #,##0";
        if (table.ShowReteFuentePercentColumn || table.ShowReteIcaPercentColumn)
            worksheet.Range(4, amountColumnIndex + 1, rowIndex, headers.Count).Style.NumberFormat.Format = "0.00\"%\"";
        worksheet.SheetView.FreezeRows(3);
        worksheet.Columns().AdjustToContents();
    }

    private static DateOnly ResolveBogotaToday()
    {
        var timeZone = MonthlyFinancialReconciliationHostedService.ResolveTimeZone("SA Pacific Standard Time");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string BuildSafeFileName(string? value)
    {
        var cleaned = string.Join("-", (value ?? "retefuente")
            .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        cleaned = cleaned
            .Replace(" ", "-", StringComparison.OrdinalIgnoreCase)
            .Trim('-');

        return string.IsNullOrWhiteSpace(cleaned)
            ? "retefuente"
            : cleaned.ToLower(CultureInfo.InvariantCulture);
    }
}
