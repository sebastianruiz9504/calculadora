using System.Text.Json;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class ConciliacionJournalPayloadTests
{
    [Fact]
    public void SiigoInvoiceDueUsesThePortfolioPrefixFromTheInvoiceName()
    {
        var resolved = SiigoService.TryResolveInvoiceDueReference(
            "FV-2-7683",
            7683,
            new[] { "2026-06-05" },
            out var prefix,
            out var consecutive,
            out var quote,
            out var dueDate,
            out var issue);

        Assert.True(resolved, issue);
        Assert.Equal("FV-2", prefix);
        Assert.Equal(7683, consecutive);
        Assert.Equal(1, quote);
        Assert.Equal("2026-06-05", dueDate);
        Assert.Empty(issue);
    }

    [Fact]
    public void SiigoInvoiceDueIsRejectedWhenTheExactDueDateIsMissing()
    {
        var resolved = SiigoService.TryResolveInvoiceDueReference(
            "FV-1-7359",
            7359,
            Array.Empty<string>(),
            out var prefix,
            out var consecutive,
            out var quote,
            out var dueDate,
            out var issue);

        Assert.False(resolved);
        Assert.Equal("FV-1", prefix);
        Assert.Equal(7359, consecutive);
        Assert.Equal(0, quote);
        Assert.Empty(dueDate);
        Assert.Contains("no devolvio la fecha", issue, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DataverseRteIvaUsesInvoiceVatInsteadOfAssumedNineteenPercent()
    {
        var value = DataverseService.CalculateRegistroPagoRteIvaValue(
            3_285_596.57m,
            524_591.05m,
            0.15m);

        Assert.Equal(78_688.66m, value);
    }

    [Fact]
    public void DocumentResolversRequireExactCc12AndCc17Codes()
    {
        var documents = new[]
        {
            new SiigoDocumentTypeLookupDto
            {
                Id = 99,
                Type = "CC",
                Code = "99",
                Name = "Comprobante de egreso",
                Active = true
            },
            new SiigoDocumentTypeLookupDto
            {
                Id = 12,
                Type = "CC",
                Code = "12",
                Name = "Comprobante de egreso",
                Active = true
            },
            new SiigoDocumentTypeLookupDto
            {
                Id = 98,
                Type = "CC",
                Code = "98",
                Name = "Comprobante de ingreso",
                Active = true
            },
            new SiigoDocumentTypeLookupDto
            {
                Id = 17,
                Type = "CC",
                Code = "17",
                Name = "Comprobante contable",
                Active = true
            }
        };

        Assert.Equal(12, ConciliacionController.ResolveExpenseJournalDocumentType(documents).Id);
        Assert.Equal(17, ConciliacionController.ResolveIncomeJournalDocumentType(documents).Id);

        Assert.Throws<InvalidOperationException>(() =>
            ConciliacionController.ResolveExpenseJournalDocumentType(new[] { documents[0] }));
        Assert.Throws<InvalidOperationException>(() =>
            ConciliacionController.ResolveIncomeJournalDocumentType(new[] { documents[2] }));
    }

    [Fact]
    public void FrozenExpressCc17UsesRealVatRteIvaAndCreditAdjustment()
    {
        var invoice = new ConciliacionSiigoOpenInvoiceDto
        {
            Id = "invoice-frozen",
            DataverseRecordId = Guid.NewGuid().ToString(),
            Name = "FV-1-7070",
            Prefix = "FV-1",
            Number = 7070,
            Total = 3_285_596.57m,
            Balance = 3_285_596.57m,
            TaxBase = 2_761_005.52m,
            Vat = 524_591.05m,
            DuePrefix = "FV-1",
            DueConsecutive = 7070,
            DueQuote = 1,
            DueDateValue = "2026-08-17",
            HasExactDueReference = true
        };
        var search = new ConciliacionSiigoOpenInvoiceSearchResultDto
        {
            Invoices = new[] { invoice },
            ReteFuenteOptions = new[]
            {
                new ConciliacionSiigoRetentionOptionDto
                {
                    TaxId = 4027,
                    Kind = "ReteFte",
                    Name = "ReteFuente 2.5%",
                    Rate = 2.5m
                }
            },
            RteIvaOptions = new[]
            {
                new ConciliacionSiigoRetentionOptionDto
                {
                    TaxId = 4099,
                    Kind = "RteIva",
                    Name = "RteIVA 15%",
                    Rate = 15m
                }
            }
        };
        var issues = new List<string>();
        var allocation = ConciliacionController.BuildClientPaymentAllocatedInvoice(
            new ConciliacionPaymentAllocationRequest
            {
                DocumentId = invoice.Id,
                DataverseRecordId = invoice.DataverseRecordId,
                AppliedValue = 3_137_883m,
                ReteFuenteTaxId = 4027,
                RteIvaTaxId = 4099
            },
            search,
            issues);

        Assert.NotNull(allocation);
        Assert.Empty(issues);
        Assert.Equal(3_285_596.57m, allocation!.GrossValue);
        Assert.Equal(-0.23m, allocation.AdjustmentValue);
        Assert.Equal(69_025.14m, allocation.Retentions.Single(item => item.Kind == "ReteFte").Value);
        Assert.Equal(78_688.66m, allocation.Retentions.Single(item => item.Kind == "RteIva").Value);

        var row = new ConciliacionCashFlowRowDto
        {
            MovementDateValue = "2026-07-17",
            EntryValue = 3_137_883m,
            BankAccountCode = "11100504",
            BankAccountName = "Banco",
            Description = "Pago Frozen Express"
        };
        var customer = new ConciliacionSiigoSupplierLookupDto
        {
            Id = "frozen-id",
            Identification = "900123456",
            BranchOffice = 1,
            Active = true
        };
        var payload = ConciliacionController.BuildClientInvoicePaymentJournalPayload(
            row,
            customer,
            new[] { allocation },
            new SiigoDocumentTypeLookupDto { Id = 17, Type = "CC", Code = "17", Active = true },
            issues);

        Assert.Empty(issues);
        using var document = JsonSerializer.SerializeToDocument(payload);
        var root = document.RootElement;
        Assert.Equal(17, root.GetProperty("document").GetProperty("id").GetInt32());
        AssertLine(root, "11100504", "Debit", 3_137_883m);
        AssertLine(root, "13551501", "Debit", 69_025.14m);
        AssertLine(root, "13551701", "Debit", 78_688.66m);
        AssertLine(root, "13050501", "Credit", 3_285_596.57m);
        AssertLine(root, "42958101", "Credit", 0.23m);
        Assert.Equal(3_285_596.80m, SumMovement(root, "Debit"));
        Assert.Equal(3_285_596.80m, SumMovement(root, "Credit"));

        var receivable = FindLine(root, "13050501");
        Assert.Equal("FV-1", receivable.GetProperty("due").GetProperty("prefix").GetString());
        Assert.Equal(7070, receivable.GetProperty("due").GetProperty("consecutive").GetInt32());
        Assert.Equal(1, receivable.GetProperty("due").GetProperty("quote").GetInt32());
        Assert.Equal("2026-08-17", receivable.GetProperty("due").GetProperty("date").GetString());
        Assert.NotEqual(row.MovementDateValue, receivable.GetProperty("due").GetProperty("date").GetString());
        Assert.All(root.GetProperty("items").EnumerateArray(), item =>
        {
            Assert.Equal("900123456", item.GetProperty("customer").GetProperty("identification").GetString());
            Assert.Equal(1, item.GetProperty("customer").GetProperty("branch_office").GetInt32());
        });
    }

    [Fact]
    public void PartialClientPaymentDoesNotCreateAutomaticAdjustment()
    {
        var invoice = new ConciliacionSiigoOpenInvoiceDto
        {
            Id = "partial",
            DataverseRecordId = Guid.NewGuid().ToString(),
            Name = "FV-1-8000",
            Balance = 10_000m,
            Total = 10_000m,
            TaxBase = 8_400m,
            Vat = 1_600m
        };
        var issues = new List<string>();

        var allocation = ConciliacionController.BuildClientPaymentAllocatedInvoice(
            new ConciliacionPaymentAllocationRequest
            {
                DocumentId = invoice.Id,
                DataverseRecordId = invoice.DataverseRecordId,
                AppliedValue = 5_000m
            },
            new ConciliacionSiigoOpenInvoiceSearchResultDto { Invoices = new[] { invoice } },
            issues);

        Assert.NotNull(allocation);
        Assert.Empty(issues);
        Assert.Equal(5_000m, allocation!.GrossValue);
        Assert.Equal(0m, allocation.AdjustmentValue);
    }

    [Fact]
    public void TriadaPaymentUsesTwoThousandPesoToleranceAndExactRetentions()
    {
        var invoice = new ConciliacionSiigoOpenInvoiceDto
        {
            Id = "triada-invoice",
            DataverseRecordId = Guid.NewGuid().ToString(),
            Name = "FV-1-7340",
            Prefix = "FV-1",
            Number = 7340,
            Total = 166_600m,
            Balance = 166_600m,
            TaxBase = 140_000m,
            Vat = 26_600m
        };
        var search = new ConciliacionSiigoOpenInvoiceSearchResultDto
        {
            Invoices = new[] { invoice },
            ReteFuenteOptions = new[]
            {
                new ConciliacionSiigoRetentionOptionDto
                {
                    TaxId = 4038,
                    Kind = "ReteFte",
                    Name = "ReteFuente 3.5%",
                    Rate = 3.5m
                }
            },
            ReteIcaOptions = new[]
            {
                new ConciliacionSiigoRetentionOptionDto
                {
                    TaxId = 4028,
                    Kind = "ReteIca",
                    Name = "ReteICA 11.04 x mil",
                    Rate = 11.04m
                }
            }
        };
        var issues = new List<string>();

        var allocation = ConciliacionController.BuildClientPaymentAllocatedInvoice(
            new ConciliacionPaymentAllocationRequest
            {
                DocumentId = invoice.Id,
                DataverseRecordId = invoice.DataverseRecordId,
                AppliedValue = 159_648m,
                ReteFuenteTaxId = 4038,
                ReteIcaTaxId = 4028
            },
            search,
            issues);

        Assert.NotNull(allocation);
        Assert.Empty(issues);
        Assert.Equal(4_900m, allocation!.Retentions.Single(item => item.Kind == "ReteFte").Value);
        Assert.Equal(1_545.60m, allocation.Retentions.Single(item => item.Kind == "ReteIca").Value);
        Assert.Equal(166_600m, allocation.GrossValue);
        Assert.Equal(506.40m, allocation.AdjustmentValue);

        ConciliacionController.ValidateClientInvoicePaymentTotal(
            159_648m,
            new[] { allocation },
            issues);

        Assert.Empty(issues);
        Assert.Equal(
            506.40m,
            ConciliacionController.CalculateClientPaymentJournalAdjustment(
                159_648m,
                allocation.GrossValue,
                allocation.Retentions.Sum(static item => item.Value)));
    }

    [Theory]
    [InlineData(8000, 2000)]
    [InlineData(12000, -2000)]
    public void ClientPaymentClosesInvoiceAtBothToleranceBoundaries(
        decimal paymentValue,
        decimal expectedAdjustment)
    {
        var invoice = new ConciliacionSiigoOpenInvoiceDto
        {
            Id = $"boundary-{paymentValue}",
            DataverseRecordId = Guid.NewGuid().ToString(),
            Name = "FV-1-BOUNDARY",
            Balance = 10_000m,
            Total = 10_000m,
            TaxBase = 10_000m
        };
        var issues = new List<string>();

        var allocation = ConciliacionController.BuildClientPaymentAllocatedInvoice(
            new ConciliacionPaymentAllocationRequest
            {
                DocumentId = invoice.Id,
                DataverseRecordId = invoice.DataverseRecordId,
                AppliedValue = paymentValue
            },
            new ConciliacionSiigoOpenInvoiceSearchResultDto { Invoices = new[] { invoice } },
            issues);

        Assert.NotNull(allocation);
        Assert.Empty(issues);
        Assert.Equal(10_000m, allocation!.GrossValue);
        Assert.Equal(expectedAdjustment, allocation.AdjustmentValue);
    }

    [Fact]
    public void ClientPaymentRejectsOverpaymentBeyondTolerance()
    {
        var invoice = new ConciliacionSiigoOpenInvoiceDto
        {
            Id = "over-tolerance",
            DataverseRecordId = Guid.NewGuid().ToString(),
            Name = "FV-1-OVER",
            Balance = 10_000m,
            Total = 10_000m,
            TaxBase = 10_000m
        };
        var issues = new List<string>();

        _ = ConciliacionController.BuildClientPaymentAllocatedInvoice(
            new ConciliacionPaymentAllocationRequest
            {
                DocumentId = invoice.Id,
                DataverseRecordId = invoice.DataverseRecordId,
                AppliedValue = 12_000.01m
            },
            new ConciliacionSiigoOpenInvoiceSearchResultDto { Invoices = new[] { invoice } },
            issues);

        Assert.Contains(issues, issue =>
            issue.Contains("superan su saldo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DataverseSavePrefersExactSiigoRetentionAmount()
    {
        Assert.Equal(
            4_900m,
            DataverseService.ResolveRegistroPagoRetentionValue(
                requestedValue: 4_900m,
                calculatedValue: 3_969m,
                label: "Rete FTE"));
        Assert.Equal(
            1_545.60m,
            DataverseService.ResolveRegistroPagoRetentionValue(
                requestedValue: 1_545.60m,
                calculatedValue: 1_251.94m,
                label: "Rete ICA"));
    }

    [Fact]
    public void DataversePaymentWritesRetentionRatesIntoWritableSourceColumns()
    {
        var payload = DataverseService.BuildRegistroPagoRetentionRatePayload(
            "cr07a_retefuentevalor",
            0.035m,
            "cr07a_reteica",
            11.04m,
            "cr07a_reteivavalor",
            0m);

        Assert.Equal(0.035m, payload["cr07a_retefuentevalor"]);
        Assert.Equal(11.04m, payload["cr07a_reteica"]);
        Assert.Equal(0m, payload["cr07a_reteivavalor"]);
        Assert.DoesNotContain("cr07a_rteftevalor", payload.Keys);
        Assert.DoesNotContain("cr07a_reteicavalor", payload.Keys);
        Assert.DoesNotContain("cr07a_rteivavalor", payload.Keys);
    }

    [Fact]
    public void MultipleClientInvoicePaymentsAreValidatedAgainstTheMovementAsOneTotal()
    {
        var firstInvoice = new ConciliacionSiigoOpenInvoiceDto
        {
            Id = "invoice-one",
            DataverseRecordId = Guid.NewGuid().ToString(),
            Name = "FV-1-9001",
            Balance = 120_000m,
            Total = 120_000m
        };
        var secondInvoice = new ConciliacionSiigoOpenInvoiceDto
        {
            Id = "invoice-two",
            DataverseRecordId = Guid.NewGuid().ToString(),
            Name = "FV-1-9002",
            Balance = 180_000m,
            Total = 180_000m
        };
        var search = new ConciliacionSiigoOpenInvoiceSearchResultDto
        {
            Invoices = new[] { firstInvoice, secondInvoice }
        };
        var issues = new List<string>();
        var allocations = new[]
        {
            ConciliacionController.BuildClientPaymentAllocatedInvoice(
                new ConciliacionPaymentAllocationRequest
                {
                    DocumentId = firstInvoice.Id,
                    DataverseRecordId = firstInvoice.DataverseRecordId,
                    AppliedValue = 120_000m
                },
                search,
                issues),
            ConciliacionController.BuildClientPaymentAllocatedInvoice(
                new ConciliacionPaymentAllocationRequest
                {
                    DocumentId = secondInvoice.Id,
                    DataverseRecordId = secondInvoice.DataverseRecordId,
                    AppliedValue = 180_000m
                },
                search,
                issues)
        };

        Assert.All(allocations, Assert.NotNull);
        Assert.Empty(issues);

        ConciliacionController.ValidateClientInvoicePaymentTotal(
            300_000m,
            allocations.Select(static allocation => allocation!).ToArray(),
            issues);

        Assert.Empty(issues);
    }

    [Fact]
    public void ClientPaymentJournalUsesTheExactCustomerOfEveryInvoice()
    {
        var customerOne = new ConciliacionSiigoSupplierLookupDto
        {
            Id = "customer-one",
            DisplayName = "Cliente Uno SAS",
            Identification = "900111222",
            BranchOffice = 0
        };
        var customerTwo = new ConciliacionSiigoSupplierLookupDto
        {
            Id = "customer-two",
            DisplayName = "Cliente Dos SAS",
            Identification = "900333444",
            BranchOffice = 1
        };
        var first = new ConciliacionController.AllocatedSiigoInvoice(
            new ConciliacionSiigoOpenInvoiceDto
            {
                Id = "invoice-one",
                Name = "FV-1-9101",
                Balance = 100_000m,
                SiigoBalance = 100_000m,
                DuePrefix = "FV-1",
                DueConsecutive = 9101,
                DueQuote = 1,
                DueDateValue = "2026-08-10",
                HasExactDueReference = true
            },
            100_000m,
            100_000m,
            0m,
            Array.Empty<ConciliacionController.AllocatedClientRetention>(),
            customerOne);
        var second = new ConciliacionController.AllocatedSiigoInvoice(
            new ConciliacionSiigoOpenInvoiceDto
            {
                Id = "invoice-two",
                Name = "FV-2-9202",
                Balance = 200_000m,
                SiigoBalance = 200_000m,
                DuePrefix = "FV-2",
                DueConsecutive = 9202,
                DueQuote = 1,
                DueDateValue = "2026-08-11",
                HasExactDueReference = true
            },
            200_000m,
            200_000m,
            0m,
            Array.Empty<ConciliacionController.AllocatedClientRetention>(),
            customerTwo);
        var issues = new List<string>();

        var payload = ConciliacionController.BuildClientInvoicePaymentJournalPayload(
            new ConciliacionCashFlowRowDto
            {
                MovementDateValue = "2026-08-18",
                EntryValue = 300_000m,
                BankAccountCode = "11100504",
                BankAccountName = "Bancolombia Cloud",
                SourceFlow = "Cloud"
            },
            customerOne,
            new[] { first, second },
            new SiigoDocumentTypeLookupDto
            {
                Id = 17,
                Type = "CC",
                Code = "17",
                Active = true
            },
            issues);

        Assert.Empty(issues);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var portfolioLines = json.RootElement.GetProperty("items")
            .EnumerateArray()
            .Where(item => item.GetProperty("account").GetProperty("code").GetString() == "13050501")
            .ToArray();
        Assert.Equal(2, portfolioLines.Length);
        Assert.Equal("900111222", portfolioLines[0].GetProperty("customer").GetProperty("identification").GetString());
        Assert.Equal("900333444", portfolioLines[1].GetProperty("customer").GetProperty("identification").GetString());
        Assert.Equal("FV-1", portfolioLines[0].GetProperty("due").GetProperty("prefix").GetString());
        Assert.Equal("FV-2", portfolioLines[1].GetProperty("due").GetProperty("prefix").GetString());
    }

    [Theory]
    [InlineData("Entrada")]
    [InlineData("Salida")]
    public void FinalConciliatedMovementsReloadAsValidated(string direction)
    {
        var row = new ConciliacionCashFlowRowDto
        {
            SourceKind = "Movimiento",
            Direction = direction,
            EntryValue = direction == "Entrada" ? 250_000m : 0m,
            ExitValue = direction == "Salida" ? 250_000m : 0m,
            DataverseStatus = "Conciliado",
            SiigoStatus = "Conciliado",
            SiigoDocumentId = "siigo-document-id"
        };

        DataverseService.CompleteConciliacionCashFlowRow(row, row.SiigoDocumentId, row.SiigoStatus);

        Assert.True(DataverseService.IsConciliacionCashFlowTerminal(row));
        Assert.True(DataverseService.IsConciliacionCashFlowFinal(row));
        Assert.Equal("Validada", row.ValidationStatus);
        Assert.Equal("success", row.ValidationTone);
        Assert.Equal("success", row.RegistrationTone);
    }

    [Theory]
    [InlineData("Entrada")]
    [InlineData("Salida")]
    public void EnviadoSiigoRemainsTerminalButPendingDataverseClose(string direction)
    {
        var row = new ConciliacionCashFlowRowDto
        {
            SourceKind = "Movimiento",
            Direction = direction,
            EntryValue = direction == "Entrada" ? 250_000m : 0m,
            ExitValue = direction == "Salida" ? 250_000m : 0m,
            DataverseStatus = "EnviadoSiigo",
            SiigoStatus = "EnviadoSiigo",
            SiigoDocumentId = "siigo-document-id"
        };

        DataverseService.CompleteConciliacionCashFlowRow(row, row.SiigoDocumentId, row.SiigoStatus);

        Assert.True(DataverseService.IsConciliacionCashFlowTerminal(row));
        Assert.False(DataverseService.IsConciliacionCashFlowFinal(row));
        Assert.Equal("Pendiente validar", row.ValidationStatus);
        Assert.Equal("warning", row.ValidationTone);
        Assert.Contains("pendiente de cierre", row.RegistrationStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("success", row.SiigoDocumentTone);
    }

    [Fact]
    public void ManualConciliationWithoutSiigoIdStillReloadsAsFinal()
    {
        var row = new ConciliacionCashFlowRowDto
        {
            SourceKind = "Movimiento",
            Direction = "Entrada",
            EntryValue = 250_000m,
            DataverseStatus = "Conciliado",
            SiigoStatus = "Conciliado"
        };

        DataverseService.CompleteConciliacionCashFlowRow(row, "", row.SiigoStatus);

        Assert.True(DataverseService.IsConciliacionCashFlowFinal(row));
        Assert.Equal("Validada", row.ValidationStatus);
        Assert.Contains("Siigo OK", row.RegistrationStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SuccessfulAccountingVoucherUsesFinalConciliatedStatus()
    {
        Assert.Equal(
            "Conciliado",
            DataverseService.ResolveConciliacionAccountingVoucherCompletionStatus(success: true));
        Assert.Equal(
            "ErrorSiigo",
            DataverseService.ResolveConciliacionAccountingVoucherCompletionStatus(success: false));
    }

    [Fact]
    public void ClientInvoicePaymentTotalRejectsAnIncompleteSelection()
    {
        var invoice = new ConciliacionSiigoOpenInvoiceDto
        {
            Id = "invoice-one",
            DataverseRecordId = Guid.NewGuid().ToString(),
            Name = "FV-1-9001",
            Balance = 120_000m,
            Total = 120_000m
        };
        var issues = new List<string>();
        var allocation = ConciliacionController.BuildClientPaymentAllocatedInvoice(
            new ConciliacionPaymentAllocationRequest
            {
                DocumentId = invoice.Id,
                DataverseRecordId = invoice.DataverseRecordId,
                AppliedValue = 120_000m
            },
            new ConciliacionSiigoOpenInvoiceSearchResultDto { Invoices = new[] { invoice } },
            issues);

        Assert.NotNull(allocation);
        Assert.Empty(issues);

        ConciliacionController.ValidateClientInvoicePaymentTotal(
            300_000m,
            new[] { allocation! },
            issues);

        Assert.Contains(issues, issue =>
            issue.Contains("pagos seleccionados", StringComparison.OrdinalIgnoreCase)
            && issue.Contains("movimiento bancario", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ClientPaymentAdjustmentAcceptsDataverseCurrencyPrecisionDrift()
    {
        Assert.True(ConciliacionController.ClientPaymentAdjustmentMatches(
            expectedAdjustment: -0.89m,
            savedDifference: -0.87m));
    }

    [Fact]
    public void ClientPaymentAdjustmentStillRejectsARealMismatch()
    {
        Assert.False(ConciliacionController.ClientPaymentAdjustmentMatches(
            expectedAdjustment: -0.89m,
            savedDifference: 0.50m));
    }

    [Theory]
    [InlineData(43057.10, 43057)]
    [InlineData(1545.60, 1546)]
    [InlineData(1660574.38, 1660574)]
    [InlineData(40075.80, 40075)]
    public void ClientPaymentConfirmationAcceptsDataverseWholePesoStorage(
        decimal expectedValue,
        decimal savedValue)
    {
        Assert.True(ConciliacionController.ClientPaymentDataverseValueMatches(
            expectedValue,
            savedValue));
    }

    [Fact]
    public void ClientPaymentConfirmationRejectsARealStoredValueMismatch()
    {
        Assert.False(ConciliacionController.ClientPaymentDataverseValueMatches(
            expectedValue: 40_075.80m,
            savedValue: 40_074m));
    }

    [Fact]
    public void ThreeInvoicePaymentBalancesTheSingleBankMovementWithOneAdjustment()
    {
        var invoices = new[]
        {
            new ConciliacionSiigoOpenInvoiceDto
            {
                Id = "invoice-7710",
                DataverseRecordId = Guid.NewGuid().ToString(),
                Name = "FV-2-7710",
                Prefix = "FV-2",
                Number = 7710,
                Total = 43_057.10m,
                Balance = 43_057.10m,
                DuePrefix = "FV-2",
                DueConsecutive = 7710,
                DueQuote = 1,
                DueDateValue = "2026-08-10",
                HasExactDueReference = true
            },
            new ConciliacionSiigoOpenInvoiceDto
            {
                Id = "invoice-7763",
                DataverseRecordId = Guid.NewGuid().ToString(),
                Name = "FV-2-7763",
                Prefix = "FV-2",
                Number = 7763,
                Total = 1_660_574.38m,
                Balance = 1_660_574.38m,
                DuePrefix = "FV-2",
                DueConsecutive = 7763,
                DueQuote = 1,
                DueDateValue = "2026-08-13",
                HasExactDueReference = true
            },
            new ConciliacionSiigoOpenInvoiceDto
            {
                Id = "invoice-7801",
                DataverseRecordId = Guid.NewGuid().ToString(),
                Name = "FV-2-7801",
                Prefix = "FV-2",
                Number = 7801,
                Total = 40_075.80m,
                Balance = 40_075.80m,
                DuePrefix = "FV-2",
                DueConsecutive = 7801,
                DueQuote = 1,
                DueDateValue = "2026-08-15",
                HasExactDueReference = true
            }
        };
        var search = new ConciliacionSiigoOpenInvoiceSearchResultDto { Invoices = invoices };
        var issues = new List<string>();
        var allocations = invoices.Select(invoice =>
            ConciliacionController.BuildClientPaymentAllocatedInvoice(
                new ConciliacionPaymentAllocationRequest
                {
                    DocumentId = invoice.Id,
                    DataverseRecordId = invoice.DataverseRecordId,
                    AppliedValue = invoice.Total
                },
                search,
                issues)!).ToArray();

        Assert.Empty(issues);
        ConciliacionController.ValidateClientInvoicePaymentTotal(
            1_743_707m,
            allocations,
            issues);

        Assert.Empty(issues);
        Assert.Equal(
            0.28m,
            ConciliacionController.CalculateClientPaymentJournalAdjustment(
                movementValue: 1_743_707m,
                grossValue: allocations.Sum(static item => item.GrossValue),
                retentionValue: 0m));

        var payload = ConciliacionController.BuildClientInvoicePaymentJournalPayload(
            new ConciliacionCashFlowRowDto
            {
                MovementDateValue = "2026-07-23",
                EntryValue = 1_743_707m,
                BankAccountCode = "11100504",
                BankAccountName = "Bancolombia"
            },
            new ConciliacionSiigoSupplierLookupDto
            {
                Id = "customer-hnc",
                Identification = "900137180",
                BranchOffice = 0,
                Active = true
            },
            allocations,
            new SiigoDocumentTypeLookupDto { Id = 17, Type = "CC", Code = "17", Active = true },
            issues);

        Assert.Empty(issues);
        using var document = JsonSerializer.SerializeToDocument(payload);
        var root = document.RootElement;
        AssertLine(root, "11100504", "Debit", 1_743_707m);
        AssertLine(root, "42958101", "Debit", 0.28m);
        Assert.Equal(1_743_707.28m, SumMovement(root, "Debit"));
        Assert.Equal(1_743_707.28m, SumMovement(root, "Credit"));
        Assert.Equal(
            3,
            root.GetProperty("items").EnumerateArray().Count(item =>
                item.GetProperty("account").GetProperty("code").GetString() == "13050501"));
    }

    [Fact]
    public void ClientPaymentIsBlockedWhenSiigoDidNotReturnTheExactExistingDue()
    {
        var issues = new List<string>();
        var invoice = new ConciliacionSiigoOpenInvoiceDto
        {
            Id = "invoice-without-due",
            Name = "FV-2-7683",
            Prefix = "CL",
            Number = 7683,
            Balance = 772_165.12m,
            DueReferenceIssue = "La factura FV-2-7683 no devolvio la fecha de su vencimiento en Siigo."
        };

        var payload = ConciliacionController.BuildClientInvoicePaymentJournalPayload(
            new ConciliacionCashFlowRowDto
            {
                MovementDateValue = "2026-06-05",
                EntryValue = 772_165.12m,
                BankAccountCode = "11100504"
            },
            new ConciliacionSiigoSupplierLookupDto
            {
                Identification = "900123456",
                Active = true
            },
            new[]
            {
                new ConciliacionController.AllocatedSiigoInvoice(
                    invoice,
                    772_165.12m,
                    772_165.12m,
                    0m,
                    Array.Empty<ConciliacionController.AllocatedClientRetention>())
            },
            new SiigoDocumentTypeLookupDto { Id = 17, Type = "CC", Code = "17", Active = true },
            issues);

        Assert.Contains(issues, issue => issue.Contains("no devolvio la fecha", StringComparison.OrdinalIgnoreCase));
        using var document = JsonSerializer.SerializeToDocument(payload);
        Assert.DoesNotContain(
            document.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("account").GetProperty("code").GetString() == "13050501");
    }

    [Fact]
    public void MolpartesPaymentBalancesBankRetentionsAdjustmentAndReceivable()
    {
        var invoice = new ConciliacionSiigoOpenInvoiceDto
        {
            Id = "molpartes-invoice",
            DataverseRecordId = Guid.NewGuid().ToString(),
            Name = "FV-2-7768",
            Prefix = "FV-2",
            Number = 7768,
            Total = 1_664_497.57m,
            Balance = 1_664_497.57m,
            TaxBase = 1_664_497.57m,
            Vat = 0m
        };
        var issues = new List<string>();
        var allocation = ConciliacionController.BuildClientPaymentAllocatedInvoice(
            new ConciliacionPaymentAllocationRequest
            {
                DocumentId = invoice.Id,
                DataverseRecordId = invoice.DataverseRecordId,
                AppliedValue = 1_587_865m,
                ReteFuenteTaxId = 4038,
                ReteIcaTaxId = 4028
            },
            new ConciliacionSiigoOpenInvoiceSearchResultDto
            {
                Invoices = new[] { invoice },
                ReteFuenteOptions = new[]
                {
                    new ConciliacionSiigoRetentionOptionDto
                    {
                        TaxId = 4038,
                        Kind = "ReteFte",
                        Name = "ReteFuente 3.5%",
                        Rate = 3.5m
                    }
                },
                ReteIcaOptions = new[]
                {
                    new ConciliacionSiigoRetentionOptionDto
                    {
                        TaxId = 4028,
                        Kind = "ReteIca",
                        Name = "ReteICA 11.04 x mil",
                        Rate = 11.04m
                    }
                }
            },
            issues);

        Assert.NotNull(allocation);
        Assert.Empty(issues);
        Assert.Equal(-0.89m, allocation!.AdjustmentValue);

        ConciliacionController.ValidateClientInvoicePaymentTotal(
            1_587_865m,
            new[] { allocation },
            issues);

        Assert.Empty(issues);
        Assert.Equal(
            -0.89m,
            ConciliacionController.CalculateClientPaymentJournalAdjustment(
                movementValue: 1_587_865m,
                grossValue: allocation.GrossValue,
                retentionValue: allocation.Retentions.Sum(static item => item.Value)));
    }

    [Fact]
    public void JournalAdjustmentAlsoBalancesAnAllowedBankRoundingDifference()
    {
        Assert.Equal(
            0.50m,
            ConciliacionController.CalculateClientPaymentJournalAdjustment(
                movementValue: 99.50m,
                grossValue: 100m,
                retentionValue: 0m));
    }

    [Fact]
    public void ElectrofisiatriaPaymentAllowsBankDifferenceWithinTwoThousandPesos()
    {
        var allocations = new[]
        {
            new ConciliacionController.AllocatedSiigoInvoice(
                new ConciliacionSiigoOpenInvoiceDto { Name = "FV-2-7709" },
                PaymentValue: 168_449m,
                GrossValue: 177_251.65m,
                AdjustmentValue: 0.33m,
                Retentions: new[]
                {
                    new ConciliacionController.AllocatedClientRetention(
                        "ReteFte", "ReteFuente 4%", 400, "23652503", 4m, 7_090.07m),
                    new ConciliacionController.AllocatedClientRetention(
                        "ReteIca", "ReteICA 9.66 x mil", 966, "23680501", 9.66m, 1_712.25m)
                }),
            new ConciliacionController.AllocatedSiigoInvoice(
                new ConciliacionSiigoOpenInvoiceDto { Name = "FV-1-7344" },
                PaymentValue: 1_903_358.60m,
                GrossValue: 1_986_246.85m,
                AdjustmentValue: 0m,
                Retentions: new[]
                {
                    new ConciliacionController.AllocatedClientRetention(
                        "ReteFte", "ReteFuente 4%", 400, "23652503", 4m, 66_764.60m),
                    new ConciliacionController.AllocatedClientRetention(
                        "ReteIca", "ReteICA 9.66 x mil", 966, "23680501", 9.66m, 16_123.65m)
                })
        };
        var issues = new List<string>();

        ConciliacionController.ValidateClientInvoicePaymentTotal(
            2_072_449m,
            allocations,
            issues);

        Assert.Empty(issues);
        Assert.Equal(
            -641.07m,
            ConciliacionController.CalculateClientPaymentJournalAdjustment(
                2_072_449m,
                allocations.Sum(static item => item.GrossValue),
                allocations.Sum(static item => item.Retentions.Sum(static retention => retention.Value))));
    }

    [Theory]
    [InlineData(2_000, false)]
    [InlineData(-2_000, false)]
    [InlineData(2_000.01, true)]
    [InlineData(-2_000.01, true)]
    public void ClientPaymentBankDifferenceUsesInclusiveTwoThousandPesoTolerance(
        decimal difference,
        bool shouldReject)
    {
        var movement = 100_000m;
        var payment = movement + difference;
        var issues = new List<string>();
        var allocation = new ConciliacionController.AllocatedSiigoInvoice(
            new ConciliacionSiigoOpenInvoiceDto { Name = "FV-TEST" },
            PaymentValue: payment,
            GrossValue: payment,
            AdjustmentValue: 0m,
            Retentions: Array.Empty<ConciliacionController.AllocatedClientRetention>());

        ConciliacionController.ValidateClientInvoicePaymentTotal(
            movement,
            new[] { allocation },
            issues);

        Assert.Equal(shouldReject, issues.Any(issue =>
            issue.Contains("tolerancia", StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.RequestTimeout)]
    [InlineData(System.Net.HttpStatusCode.TooManyRequests)]
    [InlineData(System.Net.HttpStatusCode.InternalServerError)]
    [InlineData(System.Net.HttpStatusCode.BadGateway)]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable)]
    [InlineData(System.Net.HttpStatusCode.GatewayTimeout)]
    public void SiigoReadRetriesTransientStatuses(System.Net.HttpStatusCode statusCode)
    {
        Assert.True(SiigoService.IsTransientReadStatus(statusCode));
    }

    [Fact]
    public void TransientSiigoDocumentQueryFailureUsesFriendlyUserMessage()
    {
        const string detail =
            "Siigo respondio 503: {\"Code\":\"document_query_service\",\"Message\":\"The Document query service is currently unavailable.\"}";

        Assert.True(ConciliacionController.IsTransientSiigoFailure(detail));
        Assert.Equal(
            ConciliacionController.TransientSiigoUserMessage,
            ConciliacionController.ResolveSiigoUserMessage(detail, "Error tecnico."));
        Assert.DoesNotContain("503", ConciliacionController.TransientSiigoUserMessage);
        Assert.Contains("Siigo", ConciliacionController.TransientSiigoUserMessage);
        Assert.Contains("vuelve a intentarlo", ConciliacionController.TransientSiigoUserMessage);
    }

    [Fact]
    public void SiigoPurchaseWriteCanRetryARejectedRateLimitWithoutIdempotencySupport()
    {
        Assert.True(SiigoService.CanRetryRateLimitedWrite(
            HttpMethod.Post,
            System.Net.HttpStatusCode.TooManyRequests,
            attempt: 0));
        Assert.False(SiigoService.CanRetryRateLimitedWrite(
            HttpMethod.Post,
            System.Net.HttpStatusCode.ServiceUnavailable,
            attempt: 0));
        Assert.False(SiigoService.CanRetryRateLimitedWrite(
            HttpMethod.Post,
            System.Net.HttpStatusCode.TooManyRequests,
            attempt: 3));
    }

    [Fact]
    public void DianPurchaseMovesToRutQueueWhenSiigoConfirmsSupplierDoesNotExist()
    {
        Assert.True(DianSupplierInvoiceAutomationService.IsMissingSupplierPurchaseFailure(
            new InvalidOperationException(
                "Siigo respondio 400: The supplier doesn't exist: 900077707")));
        Assert.False(DianSupplierInvoiceAutomationService.IsMissingSupplierPurchaseFailure(
            new InvalidOperationException(
                "Siigo respondio 400: La cuenta contable no existe.")));
    }

    [Fact]
    public void InternalTransfersStayBlueWhileSiigoPhaseIsPending()
    {
        var row = new ConciliacionCashFlowRowDto
        {
            RecordId = Guid.NewGuid().ToString(),
            SourceKind = "Traslado",
            Direction = "Entrada",
            EntryValue = 250_000m,
            DataverseStatus = "InternoNoSiigo",
            Description = "Traslado interno entre bancos"
        };

        DataverseService.CompleteConciliacionCashFlowRow(row, "", row.DataverseStatus);

        Assert.Equal("traslado-interno", row.DetectedTypeKey);
        Assert.Equal("info", row.DetectedTypeTone);
        Assert.Equal("info", row.ValidationTone);
        Assert.Equal("info", row.RegistrationTone);
        Assert.Equal("info", row.SiigoDocumentTone);
        Assert.Contains("Siigo pendiente", row.RegistrationStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("siguiente fase", row.SiigoDocumentStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InternalTransfersTurnGreenAfterSiigoConfirmsTheJournal()
    {
        var row = new ConciliacionCashFlowRowDto
        {
            RecordId = Guid.NewGuid().ToString(),
            SourceKind = "Traslado",
            Direction = "Entrada",
            EntryValue = 1_833_500m,
            DataverseStatus = "Conciliado",
            Description = "Traslado interno Copiers a Cloud"
        };

        DataverseService.CompleteConciliacionCashFlowRow(row, "", row.DataverseStatus);

        Assert.Equal("traslado-interno", row.DetectedTypeKey);
        Assert.Equal("success", row.ValidationTone);
        Assert.Equal("success", row.RegistrationTone);
        Assert.Equal("success", row.SiigoDocumentTone);
        Assert.Contains("Siigo OK", row.RegistrationStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enviado", row.SiigoPaymentStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InternalTransfersRemainPendingAfterSiigoUntilDataverseCloses()
    {
        var row = new ConciliacionCashFlowRowDto
        {
            RecordId = Guid.NewGuid().ToString(),
            SourceKind = "Traslado",
            Direction = "Entrada",
            EntryValue = 1_833_500m,
            DataverseStatus = "EnviadoSiigo",
            Description = "Traslado interno Copiers a Cloud"
        };

        DataverseService.CompleteConciliacionCashFlowRow(row, "", row.DataverseStatus);

        Assert.True(DataverseService.IsConciliacionCashFlowTerminal(row));
        Assert.False(DataverseService.IsConciliacionCashFlowFinal(row));
        Assert.Equal("warning", row.ValidationTone);
        Assert.Equal("warning", row.RegistrationTone);
        Assert.Equal("success", row.SiigoDocumentTone);
        Assert.Contains("pendiente de cierre", row.RegistrationStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportedBancolombiaTransferInfersTheCounterpartBankFromTheProductNumber()
    {
        var row = new ConciliacionCashFlowRowDto
        {
            RecordId = Guid.NewGuid().ToString(),
            SourceKind = "Movimiento",
            SourceFlow = "Cloud",
            Direction = "Entrada",
            EntryValue = 1_833_500m,
            BankAccountCode = "11100504",
            BankAccountName = "Bancolombia Cloud 8100",
            DataverseStatus = "Importado",
            Observations = "Transferencia de fondos por SUCURSAL VIRTUAL del producto 1 31-78797316"
        };

        DataverseService.CompleteConciliacionCashFlowRow(row, "", row.DataverseStatus);

        Assert.Equal("traslado-interno", row.DetectedTypeKey);
        Assert.Equal("11100505", row.AccountCode);
        Assert.Contains("Copiers 7316", row.AccountName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InternalTransferUsesCc17OriginCreditDestinationDebitAndBancolombiaThirdParty()
    {
        var payload = ConciliacionController.BuildInternalTransferAccountingVoucherPayload(
            new[]
            {
                new ConciliacionCashFlowRowDto
                {
                    SourceKind = "Traslado",
                    Direction = "Entrada",
                    EntryValue = 1_833_500m,
                    Amount = 1_833_500m,
                    MovementDateValue = "2026-07-17",
                    SourceFlow = "Cloud",
                    SourceRowNumber = 21,
                    BankAccountCode = "11100504",
                    BankAccountName = "Bancolombia Cloud 8100",
                    AccountCode = "11100505",
                    AccountName = "Bancolombia Copiers 7316",
                    Description = "Transferencia de fondos desde Copiers"
                }
            },
            new SiigoDocumentTypeLookupDto
            {
                Id = 17,
                Type = "CC",
                Code = "17",
                Name = "Comprobante de ingreso",
                Active = true
            },
            new DateOnly(2026, 7, 17),
            "Traslado interno Copiers a Cloud",
            new ConciliacionSiigoSupplierLookupDto
            {
                Identification = "890903938",
                Name = "BANCOLOMBIA S.A.",
                BranchOffice = 0,
                Active = true
            });

        using var document = JsonSerializer.SerializeToDocument(payload);
        var root = document.RootElement;
        Assert.Equal(17, root.GetProperty("document").GetProperty("id").GetInt32());
        AssertLine(root, "11100505", "Credit", 1_833_500m);
        AssertLine(root, "11100504", "Debit", 1_833_500m);
        Assert.All(
            root.GetProperty("items").EnumerateArray(),
            item => Assert.Equal(
                "890903938",
                item.GetProperty("customer").GetProperty("identification").GetString()));
        Assert.Equal(SumMovement(root, "Debit"), SumMovement(root, "Credit"));
    }

    [Fact]
    public void InternalTransferIgnoresStaleClientPaymentMatch()
    {
        Assert.False(DataverseService.ShouldApplyConciliacionClientPaymentMatch(
            new ConciliacionCashFlowRowDto
            {
                SourceKind = "Movimiento",
                DetectedTypeKey = "traslado-interno"
            }));
        Assert.False(DataverseService.ShouldApplyConciliacionClientPaymentMatch(
            new ConciliacionCashFlowRowDto
            {
                SourceKind = "Traslado",
                DetectedTypeKey = "traslado-interno"
            }));
        Assert.True(DataverseService.ShouldApplyConciliacionClientPaymentMatch(
            new ConciliacionCashFlowRowDto
            {
                SourceKind = "Movimiento",
                DetectedTypeKey = "entrada-fe"
            }));
    }

    [Fact]
    public void PendingReviewAppendsReasonWithoutReplacingExistingDescription()
    {
        var description = DataverseService.AppendConciliacionPendingReason(
            "Pago recibido sin referencia bancaria",
            "Falta confirmar las facturas con cartera.");

        Assert.Equal(
            "Pago recibido sin referencia bancaria\n[PENDIENTE] Falta confirmar las facturas con cartera.",
            description);
        Assert.Equal(
            description,
            DataverseService.AppendConciliacionPendingReason(
                description,
                "Falta confirmar las facturas con cartera."));
    }

    [Fact]
    public void PendingReviewStaysOrangeAndCannotUseAStaleClientMatch()
    {
        var row = new ConciliacionCashFlowRowDto
        {
            RecordId = Guid.NewGuid().ToString(),
            SourceKind = "Movimiento",
            Direction = "Entrada",
            EntryValue = 1_250_000m,
            DataverseStatus = "PendienteRevision",
            Description = "Pago por confirmar"
        };

        DataverseService.CompleteConciliacionCashFlowRow(row, "", "");

        Assert.True(DataverseService.IsConciliacionCashFlowPendingReview(row));
        Assert.False(DataverseService.ShouldApplyConciliacionClientPaymentMatch(row));
        Assert.Equal("warning", row.ValidationTone);
        Assert.Equal("warning", row.RegistrationTone);
        Assert.Equal("Pendiente por verificar", row.ValidationStatus);
        Assert.Contains("conciliacion pendiente", row.RegistrationStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AlhumSupplierPaymentBuildsCc12JournalWithSupplierOnEveryLine()
    {
        var purchase = new ConciliacionSiigoOpenPurchaseDto
        {
            Id = "purchase-alhum",
            Name = "FEDT-118600",
            ProviderInvoiceFullNumber = "FEDT-118600",
            SupplierIdentification = "804016305",
            Balance = 225_000m
        };
        var allocated = new ConciliacionController.AllocatedSiigoPurchase(
            purchase,
            new ConciliacionSupplierPaymentAllocationRequest
            {
                DocumentId = purchase.Id,
                DocumentName = purchase.Name,
                AppliedValue = 225_000m
            },
            225_000m,
            225_000m,
            Array.Empty<ConciliacionController.AllocatedSupplierRetention>());
        var issues = new List<string>();
        var payload = ConciliacionController.BuildSupplierPaymentJournalPayload(
            new ConciliacionCashFlowRowDto
            {
                MovementDateValue = "2026-07-17",
                ExitValue = 225_000m,
                BankAccountCode = "11100504",
                BankAccountName = "Banco"
            },
            new ConciliacionSupplierPaymentSendRequest
            {
                SupplierId = "alhum-id",
                SupplierIdentification = "804016305"
            },
            new ConciliacionSiigoSupplierLookupDto
            {
                Id = "alhum-id",
                Identification = "804016305",
                BranchOffice = 0,
                Active = true
            },
            new[] { allocated },
            new SiigoDocumentTypeLookupDto { Id = 12, Type = "CC", Code = "12", Active = true },
            issues);

        Assert.Empty(issues);
        using var document = JsonSerializer.SerializeToDocument(payload);
        var root = document.RootElement;
        Assert.Equal(12, root.GetProperty("document").GetProperty("id").GetInt32());
        AssertLine(root, "22050501", "Debit", 225_000m);
        AssertLine(root, "11100504", "Credit", 225_000m);
        Assert.Equal(225_000m, SumMovement(root, "Debit"));
        Assert.Equal(225_000m, SumMovement(root, "Credit"));

        var payable = FindLine(root, "22050501");
        Assert.Equal("FEDT", payable.GetProperty("due").GetProperty("prefix").GetString());
        Assert.Equal(118600, payable.GetProperty("due").GetProperty("consecutive").GetInt32());
        Assert.All(root.GetProperty("items").EnumerateArray(), item =>
            Assert.Equal("804016305", item.GetProperty("customer").GetProperty("identification").GetString()));
    }

    [Fact]
    public void SupplierPaymentRejectsRequestedNitThatDiffersFromResolvedSiigoSupplier()
    {
        var purchase = new ConciliacionSiigoOpenPurchaseDto
        {
            Id = "purchase-alhum",
            Name = "FEDT-118600",
            SupplierIdentification = "804016305",
            Balance = 225_000m
        };
        var allocated = new ConciliacionController.AllocatedSiigoPurchase(
            purchase,
            new ConciliacionSupplierPaymentAllocationRequest
            {
                DocumentId = purchase.Id,
                DocumentName = purchase.Name,
                AppliedValue = 225_000m
            },
            225_000m,
            225_000m,
            Array.Empty<ConciliacionController.AllocatedSupplierRetention>());
        var issues = new List<string>();
        var payload = ConciliacionController.BuildSupplierPaymentJournalPayload(
            new ConciliacionCashFlowRowDto
            {
                MovementDateValue = "2026-07-17",
                ExitValue = 225_000m,
                BankAccountCode = "11100504"
            },
            new ConciliacionSupplierPaymentSendRequest
            {
                SupplierId = "alhum-id",
                SupplierIdentification = "900399875"
            },
            new ConciliacionSiigoSupplierLookupDto
            {
                Id = "alhum-id",
                Identification = "804016305",
                BranchOffice = 0,
                Active = true
            },
            new[] { allocated },
            new SiigoDocumentTypeLookupDto { Id = 12, Type = "CC", Code = "12", Active = true },
            issues);

        Assert.Contains(issues, issue => issue.Contains("NIT solicitado no coincide", StringComparison.OrdinalIgnoreCase));
        using var document = JsonSerializer.SerializeToDocument(payload);
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            Assert.Equal("804016305", item.GetProperty("customer").GetProperty("identification").GetString());
        }
    }

    [Fact]
    public void EnelManualExpenseUsesSelectedThirdPartyAndNeverCompanyFallback()
    {
        var payload = ConciliacionController.BuildAccountingVoucherPayload(
            new ConciliacionCashFlowRowDto
            {
                Description = "Pago energia ENEL",
                ExitValue = 80_000m,
                Amount = 80_000m,
                BankAccountCode = "11100504",
                AccountCode = "51353001"
            },
            new SiigoDocumentTypeLookupDto { Id = 12, Type = "CC", Code = "12", Active = true },
            new DateOnly(2026, 7, 17),
            isEntry: false,
            new ConciliacionSiigoSupplierLookupDto
            {
                Id = "enel-id",
                Identification = "860063875",
                Name = "ENEL",
                BranchOffice = 0,
                Active = true
            });

        using var document = JsonSerializer.SerializeToDocument(payload);
        var root = document.RootElement;
        AssertLine(root, "51353001", "Debit", 80_000m);
        AssertLine(root, "11100504", "Credit", 80_000m);
        Assert.DoesNotContain("900399875", root.GetRawText(), StringComparison.Ordinal);
        Assert.All(root.GetProperty("items").EnumerateArray(), item =>
            Assert.Equal("860063875", item.GetProperty("customer").GetProperty("identification").GetString()));
    }

    [Fact]
    public void CuentaCobroCc12PostsEachRetentionOnItsApprovedAccount()
    {
        var row = new ConciliacionCuentaCobroRowDto
        {
            RecordId = Guid.NewGuid().ToString(),
            NitOCedula = "123456789",
            Receptor = "Prestador",
            FechaPagoValue = "2026-07-17",
            ValorTotal = 1_000_000m,
            ValorPago = 950_340m,
            BankAccountCode = "11100504",
            BankAccountName = "Banco",
            TotalesCuadran = true,
            Retentions = new[]
            {
                new ConciliacionCuentaCobroRetentionDto
                {
                    Kind = "ReteFuente",
                    Label = "ReteFuente 4%",
                    TaxId = 4026,
                    AccountCode = "23652503",
                    BaseValue = 1_000_000m,
                    Rate = 4m,
                    Value = 40_000m
                },
                new ConciliacionCuentaCobroRetentionDto
                {
                    Kind = "ReteICA",
                    Label = "ReteICA 9.66 por mil",
                    TaxId = 4030,
                    AccountCode = "23680501",
                    BaseValue = 1_000_000m,
                    Rate = 9.66m,
                    Value = 9_660m
                }
            }
        };
        var issues = new List<string>();
        var payload = ConciliacionController.BuildCuentaCobroPaymentReceiptPayload(
            row,
            new SiigoDocumentTypeLookupDto { Id = 12, Type = "CC", Code = "12", Active = true },
            "DS-123",
            issues);

        Assert.Empty(issues);
        using var document = JsonSerializer.SerializeToDocument(payload);
        var root = document.RootElement;
        AssertLine(root, "22050501", "Debit", 1_000_000m);
        AssertLine(root, "23652503", "Credit", 40_000m);
        AssertLine(root, "23680501", "Credit", 9_660m);
        AssertLine(root, "11100504", "Credit", 950_340m);
        Assert.Equal(1_000_000m, SumMovement(root, "Debit"));
        Assert.Equal(1_000_000m, SumMovement(root, "Credit"));
        Assert.Equal(4026, FindLine(root, "23652503").GetProperty("tax").GetProperty("id").GetInt32());
        Assert.Equal(4030, FindLine(root, "23680501").GetProperty("tax").GetProperty("id").GetInt32());
        Assert.All(root.GetProperty("items").EnumerateArray(), item =>
            Assert.Equal("123456789", item.GetProperty("customer").GetProperty("identification").GetString()));
    }

    [Fact]
    public void CuentaCobroSupportDocumentRequiresAnActiveDueSupplierCreditPaymentType()
    {
        var paymentTypes = new[]
        {
            new SiigoPaymentTypeLookupDto
            {
                Id = 1701,
                Name = "Efectivo",
                Type = "Documento soporte",
                Active = true,
                DueDate = false
            },
            new SiigoPaymentTypeLookupDto
            {
                Id = 1702,
                Name = "Credito clientes",
                Type = "Cliente",
                Active = true,
                DueDate = true
            },
            new SiigoPaymentTypeLookupDto
            {
                Id = 1703,
                Name = "Credito proveedores",
                Type = "Proveedor",
                Active = false,
                DueDate = true
            },
            new SiigoPaymentTypeLookupDto
            {
                Id = 1704,
                Name = "Crédito proveedores",
                Type = "Proveedor",
                Active = true,
                DueDate = true
            }
        };

        var selected = ConciliacionController.ResolveSupportDocumentPaymentType(paymentTypes);

        Assert.Equal(1704, selected.Id);
        Assert.Throws<InvalidOperationException>(() =>
            ConciliacionController.ResolveSupportDocumentPaymentType(paymentTypes[..3]));
        Assert.Throws<InvalidOperationException>(() =>
            ConciliacionController.ResolveSupportDocumentPaymentType(Array.Empty<SiigoPaymentTypeLookupDto>()));
    }

    [Fact]
    public void CuentaCobroExpenseRetentionsUseCatalogRatesValuesAndApprovedAccounts()
    {
        var issues = new List<string>();
        var retentions = ConciliacionController.ResolveCuentaCobroExpenseRetentions(
            new ConciliacionCuentaCobroExpenseSaveRequest
            {
                ValorTotal = 1_190_000m,
                ValorIva = 190_000m,
                Retentions = new[]
                {
                    new ConciliacionCuentaCobroRetentionDto
                    {
                        Kind = "ReteFuente",
                        TaxId = 4026,
                        AccountCode = "99999999",
                        BaseValue = 25m,
                        Rate = 99m,
                        Value = 1m
                    },
                    new ConciliacionCuentaCobroRetentionDto
                    {
                        Kind = "ReteIca",
                        TaxId = 4030,
                        AccountCode = "99999998",
                        BaseValue = 50m,
                        Rate = 88m,
                        Value = 2m
                    },
                    new ConciliacionCuentaCobroRetentionDto
                    {
                        Kind = "ReteIVA",
                        TaxId = 4040,
                        AccountCode = "99999997",
                        BaseValue = 75m,
                        Rate = 77m,
                        Value = 3m
                    }
                }
            },
            new[]
            {
                new SiigoTaxLookupDto { Id = 4026, Name = "ReteFuente 4%", Type = "ReteFte", Percentage = 4m, Active = true },
                new SiigoTaxLookupDto { Id = 4030, Name = "ReteICA 9.66 por mil", Type = "ReteIca", Percentage = 9.66m, Active = true },
                new SiigoTaxLookupDto { Id = 4040, Name = "Retencion IVA 15%", Type = "ReteIVA", Percentage = 15m, Active = true }
            },
            issues);

        Assert.Empty(issues);
        Assert.Collection(
            retentions,
            retention =>
            {
                Assert.Equal("ReteFuente", retention.Kind);
                Assert.Equal(4026, retention.TaxId);
                Assert.Equal("23652503", retention.AccountCode);
                Assert.Equal(1_000_000m, retention.BaseValue);
                Assert.Equal(4m, retention.Rate);
                Assert.Equal(40_000m, retention.Value);
            },
            retention =>
            {
                Assert.Equal("ReteICA", retention.Kind);
                Assert.Equal(4030, retention.TaxId);
                Assert.Equal("23680501", retention.AccountCode);
                Assert.Equal(1_000_000m, retention.BaseValue);
                Assert.Equal(9.66m, retention.Rate);
                Assert.Equal(9_660m, retention.Value);
            },
            retention =>
            {
                Assert.Equal("RteIVA", retention.Kind);
                Assert.Equal(4040, retention.TaxId);
                Assert.Equal("23670101", retention.AccountCode);
                Assert.Equal(190_000m, retention.BaseValue);
                Assert.Equal(15m, retention.Rate);
                Assert.Equal(28_500m, retention.Value);
            });
        Assert.Equal(78_160m, retentions.Sum(static retention => retention.Value));
    }

    [Fact]
    public void CuentaCobroRteIvaRequiresAnExplicitVatValue()
    {
        var issues = new List<string>();
        var retentions = ConciliacionController.ResolveCuentaCobroExpenseRetentions(
            new ConciliacionCuentaCobroExpenseSaveRequest
            {
                ValorTotal = 1_000_000m,
                ValorIva = 0m,
                Retentions = new[]
                {
                    new ConciliacionCuentaCobroRetentionDto { Kind = "RteIVA", TaxId = 4040 }
                }
            },
            new[]
            {
                new SiigoTaxLookupDto { Id = 4040, Name = "Retencion IVA 15%", Type = "ReteIVA", Percentage = 15m, Active = true }
            },
            issues);

        Assert.Empty(retentions);
        Assert.Contains(issues, issue => issue.Contains("no tiene valor IVA", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConciliacionIdempotencyKeyHashesTheWholeCanonicalIdentity()
    {
        var first = ConciliacionController.BuildSiigoIdempotencyKey(
            "cuenta-cobro-pago-bancolombia:2026-07-19:19db19e7-1111-2222-3333-444444444444");
        var second = ConciliacionController.BuildSiigoIdempotencyKey(
            "cuenta-cobro-pago-bancolombia:2026-07-19:4071a07c-5555-6666-7777-888888888888");

        Assert.Equal(30, first.Length);
        Assert.Equal(first, ConciliacionController.BuildSiigoIdempotencyKey(
            "cuenta-cobro-pago-bancolombia:2026-07-19:19db19e7-1111-2222-3333-444444444444"));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CuentaCobroExpenseRetentionsRejectInactiveAndWrongTaxKind()
    {
        var issues = new List<string>();
        var retentions = ConciliacionController.ResolveCuentaCobroExpenseRetentions(
            new ConciliacionCuentaCobroExpenseSaveRequest
            {
                ValorTotal = 1_000_000m,
                Retentions = new[]
                {
                    new ConciliacionCuentaCobroRetentionDto { Kind = "ReteFuente", TaxId = 5101 },
                    new ConciliacionCuentaCobroRetentionDto { Kind = "RteIVA", TaxId = 5102 }
                }
            },
            new[]
            {
                new SiigoTaxLookupDto { Id = 5101, Name = "ReteFuente 4%", Type = "ReteFte", Percentage = 4m, Active = false },
                new SiigoTaxLookupDto { Id = 5102, Name = "ReteICA 9.66 por mil", Type = "ReteIca", Percentage = 9.66m, Active = true }
            },
            issues);

        Assert.Empty(retentions);
        Assert.Contains(issues, issue => issue.Contains("no esta activo", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.Contains("no corresponde al tipo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CuentaCobroExpenseRetentionsRejectDuplicateKind()
    {
        var issues = new List<string>();
        var retentions = ConciliacionController.ResolveCuentaCobroExpenseRetentions(
            new ConciliacionCuentaCobroExpenseSaveRequest
            {
                ValorTotal = 1_000_000m,
                Retentions = new[]
                {
                    new ConciliacionCuentaCobroRetentionDto { Kind = "ReteFuente", TaxId = 5201 },
                    new ConciliacionCuentaCobroRetentionDto { Kind = "ReteFte", TaxId = 5202 }
                }
            },
            new[]
            {
                new SiigoTaxLookupDto { Id = 5201, Name = "ReteFuente 4%", Type = "ReteFte", Percentage = 4m, Active = true },
                new SiigoTaxLookupDto { Id = 5202, Name = "ReteFuente 3.5%", Type = "ReteFte", Percentage = 3.5m, Active = true }
            },
            issues);

        var retention = Assert.Single(retentions);
        Assert.Equal(5201, retention.TaxId);
        Assert.Equal(40_000m, retention.Value);
        Assert.Contains(issues, issue => issue.Contains("Solo puedes seleccionar una tarifa de ReteFuente", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CuentaCobroRetentionCatalogResolvesLegacyTaxAndApprovedAccounts()
    {
        var issues = new List<string>();
        var retentions = ConciliacionController.ValidateAndResolveCuentaCobroPaymentRetentions(
            new ConciliacionCuentaCobroRowDto
            {
                ValorTotal = 1_000_000m,
                ValorPago = 950_340m,
                BankAccountCode = "11100504",
                Retentions = new[]
                {
                    new ConciliacionCuentaCobroRetentionDto
                    {
                        Kind = "ReteFuente",
                        Label = "ReteFuente 4%",
                        BaseValue = 1_000_000m,
                        Rate = 4m,
                        Value = 40_000m
                    },
                    new ConciliacionCuentaCobroRetentionDto
                    {
                        Kind = "ReteICA",
                        Label = "ReteICA 9.66 por mil",
                        BaseValue = 1_000_000m,
                        Rate = 9.66m,
                        Value = 9_660m
                    }
                }
            },
            new[]
            {
                new SiigoTaxLookupDto { Id = 4026, Name = "ReteFuente 4%", Type = "ReteFte", Percentage = 4m, Active = true },
                new SiigoTaxLookupDto { Id = 4030, Name = "ReteICA 9.66", Type = "ReteIca", Percentage = 9.66m, Active = true }
            },
            new[] { "22050501", "11100504", "23652503", "23680501" },
            issues);

        Assert.Empty(issues);
        Assert.Collection(
            retentions,
            retention =>
            {
                Assert.Equal(4026, retention.TaxId);
                Assert.Equal("23652503", retention.AccountCode);
            },
            retention =>
            {
                Assert.Equal(4030, retention.TaxId);
                Assert.Equal("23680501", retention.AccountCode);
            });
    }

    [Fact]
    public void CuentaCobroRetentionCatalogRejectsInactiveTaxWrongAccountAndUnknownBank()
    {
        var issues = new List<string>();
        _ = ConciliacionController.ValidateAndResolveCuentaCobroPaymentRetentions(
            new ConciliacionCuentaCobroRowDto
            {
                ValorTotal = 1_000_000m,
                ValorPago = 960_000m,
                BankAccountCode = "11109999",
                Retentions = new[]
                {
                    new ConciliacionCuentaCobroRetentionDto
                    {
                        Kind = "ReteFuente",
                        Label = "ReteFuente 4%",
                        TaxId = 4026,
                        AccountCode = "23651503",
                        BaseValue = 1_000_000m,
                        Rate = 4m,
                        Value = 40_000m
                    }
                }
            },
            new[]
            {
                new SiigoTaxLookupDto { Id = 4026, Name = "ReteFuente 4%", Type = "ReteFte", Percentage = 4m, Active = false }
            },
            new[] { "22050501", "23652503" },
            issues);

        Assert.Contains(issues, issue => issue.Contains("esta inactivo", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.Contains("cuenta explicita", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.Contains("11109999", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CuentaCobroRetentionCatalogRejectsWrongTaxKindRateAndMissingAdjustmentAccount()
    {
        var issues = new List<string>();
        _ = ConciliacionController.ValidateAndResolveCuentaCobroPaymentRetentions(
            new ConciliacionCuentaCobroRowDto
            {
                ValorTotal = 1_000m,
                ValorPago = 849.77m,
                BankAccountCode = "11100504",
                Retentions = new[]
                {
                    new ConciliacionCuentaCobroRetentionDto
                    {
                        Kind = "RteIVA",
                        Label = "RteIVA 15%",
                        TaxId = 4030,
                        AccountCode = "23670101",
                        BaseValue = 1_000m,
                        Rate = 15m,
                        Value = 150m
                    }
                }
            },
            new[]
            {
                new SiigoTaxLookupDto { Id = 4030, Name = "ReteICA 9.66", Type = "ReteIca", Percentage = 9.66m, Active = true }
            },
            new[] { "22050501", "11100504", "23670101" },
            issues);

        Assert.Contains(issues, issue => issue.Contains("no corresponde al tipo", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.Contains("no coincide con el impuesto", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.Contains("42958101", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CuentaCobroRteIvaUsesApprovedAccountWhenClientOmitsIt()
    {
        var issues = new List<string>();
        var payload = ConciliacionController.BuildCuentaCobroPaymentReceiptPayload(
            new ConciliacionCuentaCobroRowDto
            {
                NitOCedula = "123456789",
                FechaPagoValue = "2026-07-17",
                ValorTotal = 1_000m,
                ValorPago = 850m,
                BankAccountCode = "11100504",
                Retentions = new[]
                {
                    new ConciliacionCuentaCobroRetentionDto
                    {
                        Kind = "RteIVA",
                        Label = "RteIVA",
                        BaseValue = 1_000m,
                        Rate = 15m,
                        Value = 150m
                    }
                }
            },
            new SiigoDocumentTypeLookupDto { Id = 12, Type = "CC", Code = "12", Active = true },
            "DS-123",
            issues);

        Assert.Empty(issues);
        using var document = JsonSerializer.SerializeToDocument(payload);
        AssertLine(document.RootElement, "23670101", "Credit", 150m);
    }

    private static void AssertLine(JsonElement root, string accountCode, string movement, decimal value)
    {
        var line = FindLine(root, accountCode);
        Assert.Equal(movement, line.GetProperty("account").GetProperty("movement").GetString());
        Assert.Equal(value, line.GetProperty("value").GetDecimal());
    }

    private static JsonElement FindLine(JsonElement root, string accountCode) =>
        root.GetProperty("items")
            .EnumerateArray()
            .Single(item => string.Equals(
                item.GetProperty("account").GetProperty("code").GetString(),
                accountCode,
                StringComparison.OrdinalIgnoreCase));

    private static decimal SumMovement(JsonElement root, string movement) =>
        root.GetProperty("items")
            .EnumerateArray()
            .Where(item => string.Equals(
                item.GetProperty("account").GetProperty("movement").GetString(),
                movement,
                StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.GetProperty("value").GetDecimal());
}
