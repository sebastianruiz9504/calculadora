using ClosedXML.Excel;
using CotizadorInterno.Web.Models.Automation;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class DianSupplierDocumentMultiPeriodImportTests
{
    [Fact]
    public void ParserImportsMixedMonthsAndPayrollWithoutASelectedPeriod()
    {
        using var stream = BuildWorkbook();

        var read = DianSupplierDocumentImportService.ReadSource(stream, "dian-mixto.xlsx");

        Assert.Equal(4, read.RowsRead);
        Assert.Equal(3, read.Rows.Count);
        Assert.Single(read.Skipped);
        Assert.Contains(read.Rows, static row =>
            row.DocumentKind == "FacturaElectronica"
            && DianSupplierDocumentImportService.ResolveDocumentPeriod(row) == new DateOnly(2026, 7, 1));
        Assert.Contains(read.Rows, static row =>
            row.DocumentKind == "FacturaElectronica"
            && DianSupplierDocumentImportService.ResolveDocumentPeriod(row) == new DateOnly(2026, 8, 1));

        var payroll = Assert.Single(read.Rows, static row => row.DocumentKind == "NominaIndividual");
        Assert.Null(payroll.ReceptionDate);
        Assert.Equal(new DateOnly(2026, 8, 1), DianSupplierDocumentImportService.ResolveDocumentPeriod(payroll));
        Assert.Equal("Empleado Uno", payroll.SupplierName);
        Assert.Equal("10101010", payroll.SupplierNit);
        Assert.Contains("duplicado", read.Skipped[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParserImportsEmittedSupportDocumentsOnlyToDataverse()
    {
        using var stream = BuildSupportDocumentWorkbook();

        var read = DianSupplierDocumentImportService.ReadSource(stream, "dian-soportes.xlsx");

        Assert.Equal(3, read.RowsRead);
        var support = Assert.Single(read.Rows);
        Assert.Equal("DocumentoSoporte", support.DocumentKind);
        Assert.Equal("DSE-224", support.InvoiceNumber);
        Assert.Equal("700164226", support.SupplierNit);
        Assert.Equal("Francisco Javier Fernandez Perez", support.SupplierName);
        Assert.Equal("900399875", support.CompanyNit);
        Assert.Equal("DIGITAL TECH COPIERS S A S", support.CompanyName);
        Assert.Null(support.ReceptionDate);
        Assert.Equal(new DateOnly(2026, 8, 1), DianSupplierDocumentImportService.ResolveDocumentPeriod(support));
        Assert.True(DianSupplierDocumentImportService.IsDataverseOnlyDocument(support.DocumentKind));
        Assert.False(DianSupplierDocumentImportService.IsSiigoEligibleDocument(support));
        Assert.False(DataverseService.IsDianSupplierDocumentSiigoEligible(support));
        Assert.Equal(2, read.Skipped.Count);
        Assert.Contains(read.Skipped, static row =>
            row.DocumentType.Contains("Documento soporte", StringComparison.OrdinalIgnoreCase)
            && row.Reason.Contains("Emitidos", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(read.Skipped, static row =>
            row.DocumentType.Contains("Nota de ajuste", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DataverseHistoryIncludesOnlyEmittedSupportDocumentsAsDataverseOnly()
    {
        Assert.True(DataverseService.IsConciliacionDianDataverseOnlyDocument(
            new ConciliacionDianSupplierInvoiceRowDto
            {
                DocumentType = "Documento soporte con no obligados",
                DianGroup = "Emitido"
            }));
        Assert.False(DataverseService.IsConciliacionDianDataverseOnlyDocument(
            new ConciliacionDianSupplierInvoiceRowDto
            {
                DocumentType = "Documento soporte con no obligados",
                DianGroup = "Recibido"
            }));
        Assert.False(DataverseService.IsConciliacionDianDataverseOnlyDocument(
            new ConciliacionDianSupplierInvoiceRowDto
            {
                DocumentType = "Nota de ajuste del documento soporte",
                DianGroup = "Emitido"
            }));
    }

    [Fact]
    public void InvoiceAutomationAggregationPreservesBothPeriodsAndTotals()
    {
        var result = DianSupplierDocumentImportService.AggregateInvoiceAutomationResults(
        [
            new DianSupplierInvoiceAutomationResultDto
            {
                PeriodStart = new DateOnly(2026, 7, 1),
                PeriodEndExclusive = new DateOnly(2026, 8, 1),
                Completed = true,
                IsComplete = true,
                CanComplete = true,
                Eligible = 2,
                Created = 2
            },
            new DianSupplierInvoiceAutomationResultDto
            {
                PeriodStart = new DateOnly(2026, 8, 1),
                PeriodEndExclusive = new DateOnly(2026, 9, 1),
                Completed = true,
                IsComplete = true,
                CanComplete = true,
                Eligible = 3,
                AlreadyImported = 1
            }
        ]);

        Assert.Equal(new DateOnly(2026, 7, 1), result.PeriodStart);
        Assert.Equal(new DateOnly(2026, 9, 1), result.PeriodEndExclusive);
        Assert.Equal(5, result.Eligible);
        Assert.Equal(2, result.Created);
        Assert.Equal(1, result.AlreadyImported);
        Assert.True(result.Completed);
    }

    [Fact]
    public void HistoryTreatsPayrollAndSupportDocumentsAsDataverseOnlyAndShowsMixedPeriods()
    {
        var manifest = new DeduccionesIvaImportHistoryManifestDto
        {
            ImportId = "mixed-payroll",
            Year = 2026,
            Month = 7,
            Periods = ["2026-07", "2026-08"],
            ImportedAtUtc = new DateTimeOffset(2026, 8, 18, 15, 0, 0, TimeSpan.Zero),
            PayrollRows = 1,
            SupportDocumentRows = 1,
            ExternalKeys = ["payroll-1", "support-1"],
            Skipped =
            [
                new DianSupplierDocumentSkippedRowDto { RowNumber = 8, Reason = "Application response; no se importa." }
            ]
        };
        var rows = new[]
        {
            new ConciliacionDianSupplierInvoiceRowDto
            {
                RecordId = "payroll-row",
                ExcelKey = "payroll-1",
                DocumentType = "Nomina Individual",
                Stage = "proveedor",
                StageLabel = "Proveedor pendiente",
                StageTone = "warning"
            },
            new ConciliacionDianSupplierInvoiceRowDto
            {
                RecordId = "support-row",
                ExcelKey = "support-1",
                DocumentType = "Documento soporte con no obligados",
                DianGroup = "Emitido",
                Stage = "proveedor",
                StageLabel = "Proveedor pendiente",
                StageTone = "warning"
            }
        };

        var history = DeduccionesIvaImportHistoryService.BuildEntry(manifest, rows);

        Assert.Equal(1, history.PayrollRows);
        Assert.Equal(1, history.SupportDocumentRows);
        Assert.Equal(0, history.SiigoRows);
        Assert.Equal("Guardada en Dataverse", history.StatusLabel);
        Assert.Equal("julio 2026, agosto 2026", history.PeriodLabel);
        Assert.Single(history.Skipped);
        Assert.Equal(2, history.Documents.Count);
        var payroll = Assert.Single(history.Documents, static row => row.IsPayroll);
        Assert.False(payroll.NeedsRut);
        Assert.Equal("Guardada en Dataverse", payroll.StatusLabel);
        var support = Assert.Single(history.Documents, static row => row.IsSupportDocument);
        Assert.False(support.NeedsRut);
        Assert.Equal("Guardada en Dataverse", support.StatusLabel);
        Assert.Contains("no se envia a Siigo", support.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private static MemoryStream BuildWorkbook()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("DIAN");
        var headers = new[]
        {
            "Tipo de documento", "CUFE/CUDE", "Folio", "Prefijo", "Grupo",
            "Fecha emisión", "Fecha recepción", "NIT emisor", "Nombre emisor",
            "NIT receptor", "Nombre receptor", "IVA", "Total"
        };
        for (var column = 0; column < headers.Length; column++)
            sheet.Cell(1, column + 1).Value = headers[column];

        AddRow(sheet, 2, "Factura electrónica de Venta", "cufe-julio", "100", "FC", "Recibidos",
            new DateTime(2026, 7, 30), new DateTime(2026, 7, 31, 12, 0, 0),
            "900111222", "Proveedor Julio", "901000000", "Digital Tech", 190m, 1190m);
        AddRow(sheet, 3, "Factura electrónica de Venta", "cufe-agosto", "200", "FA", "Recibidos",
            new DateTime(2026, 7, 31), new DateTime(2026, 8, 1, 8, 0, 0),
            "900333444", "Proveedor Agosto", "901000000", "Digital Tech", 380m, 2380m);
        AddRow(sheet, 4, "Nomina Individual", "cude-nomina", "300", "NIE", "Emitidos",
            new DateTime(2026, 8, 15), null,
            "901000000", "Digital Tech Copiers S A S", "10101010", "Empleado Uno", 0m, 3500000m);
        AddRow(sheet, 5, "Factura electrónica de Venta", "cufe-julio", "100", "FC", "Recibidos",
            new DateTime(2026, 7, 30), new DateTime(2026, 7, 31, 12, 0, 0),
            "900111222", "Proveedor Julio", "901000000", "Digital Tech", 190m, 1190m);

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildSupportDocumentWorkbook()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("DIAN");
        var headers = new[]
        {
            "Tipo de documento", "CUFE/CUDE", "Folio", "Prefijo", "Grupo",
            "Fecha emisión", "Fecha recepción", "NIT emisor", "Nombre emisor",
            "NIT receptor", "Nombre receptor", "IVA", "Total"
        };
        for (var column = 0; column < headers.Length; column++)
            sheet.Cell(1, column + 1).Value = headers[column];

        AddRow(sheet, 2, "Documento soporte con no obligados", "cude-soporte-emitido", "224", "DSE", "Emitido",
            new DateTime(2026, 8, 14), null,
            "900399875", "DIGITAL TECH COPIERS S A S", "700164226", "Francisco Javier Fernandez Perez", 0m, 3800000m);
        AddRow(sheet, 3, "Documento soporte con no obligados", "cude-soporte-recibido", "225", "DSE", "Recibido",
            new DateTime(2026, 8, 22), new DateTime(2026, 8, 24, 4, 47, 40),
            "700164226", "Proveedor externo", "900399875", "DIGITAL TECH COPIERS S A S", 0m, 4936478m);
        AddRow(sheet, 4, "Nota de ajuste del documento soporte", "cude-nota-soporte", "1", "NAS", "Emitido",
            new DateTime(2026, 8, 23), null,
            "900399875", "DIGITAL TECH COPIERS S A S", "700164226", "Francisco Javier Fernandez Perez", 0m, 100000m);

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static void AddRow(
        IXLWorksheet sheet,
        int row,
        string documentType,
        string cufe,
        string folio,
        string prefix,
        string group,
        DateTime emissionDate,
        DateTime? receptionDate,
        string issuerNit,
        string issuerName,
        string recipientNit,
        string recipientName,
        decimal vat,
        decimal total)
    {
        var values = new object?[]
        {
            documentType, cufe, folio, prefix, group, emissionDate, receptionDate,
            issuerNit, issuerName, recipientNit, recipientName, vat, total
        };
        for (var column = 0; column < values.Length; column++)
        {
            if (values[column] is not null)
                sheet.Cell(row, column + 1).Value = XLCellValue.FromObject(values[column]);
        }
    }
}
