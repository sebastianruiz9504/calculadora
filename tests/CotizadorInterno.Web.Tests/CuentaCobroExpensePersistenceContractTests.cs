using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CuentaCobroExpensePersistenceContractTests
{
    private static readonly string ProjectRoot = FindProjectRoot();

    [Fact]
    public void CashFlowOnlyCuentaCobroExposesEditorAndExpenseSaveContracts()
    {
        var controller = ReadProjectFile("Controllers", "ConciliacionController.cs");
        var models = ReadProjectFile("Models", "Conciliacion", "ConciliacionModels.cs");
        var serviceContract = ReadProjectFile("Services", "IDataverseService.cs");
        var persistence = ReadProjectFile("Services", "DataverseService.ConciliacionCuentasCobro.cs");
        var program = ReadProjectFile("Program.cs");

        Assert.Contains(
            "public async Task<IActionResult> OpenCuentaCobroExpenseEditor(",
            controller,
            StringComparison.Ordinal);
        Assert.Contains(
            "public async Task<IActionResult> SaveCuentaCobroExpense(",
            controller,
            StringComparison.Ordinal);
        Assert.Contains(
            "request.Retentions = ResolveCuentaCobroExpenseRetentions(request, taxes, issues);",
            controller,
            StringComparison.Ordinal);
        Assert.Contains(
            "_dataverse.SaveConciliacionCuentaCobroExpenseAsync(request, ct)",
            controller,
            StringComparison.Ordinal);
        Assert.Contains(
            "&& string.IsNullOrWhiteSpace(request.CashFlowRecordId)",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains(
            "&& string.IsNullOrWhiteSpace(request.CashFlowExternalKey)",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (!string.IsNullOrWhiteSpace(request.RecordId))",
            persistence,
            StringComparison.Ordinal);

        Assert.Contains(
            "public sealed class ConciliacionCuentaCobroExpenseSaveRequest",
            models,
            StringComparison.Ordinal);
        Assert.Contains("public string CashFlowRecordId { get; set; }", models, StringComparison.Ordinal);
        Assert.Contains("public string CashFlowExternalKey { get; set; }", models, StringComparison.Ordinal);
        Assert.Contains(
            "public IReadOnlyList<ConciliacionCuentaCobroRetentionDto> Retentions { get; set; }",
            models,
            StringComparison.Ordinal);

        Assert.Contains(
            "SaveConciliacionCuentaCobroExpenseAsync(ConciliacionCuentaCobroExpenseSaveRequest request",
            serviceContract,
            StringComparison.Ordinal);
        Assert.Contains(
            "AddScoped<IDataverseService, DataverseService>()",
            program,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CuentaCobroExpenseUsesSupplierExpensesAndIdempotentCashFlowKey()
    {
        var core = ReadProjectFile("Services", "DataverseService.cs");
        var persistence = ReadProjectFile("Services", "DataverseService.ConciliacionCuentasCobro.cs");
        var optimisticConcurrency = ReadProjectFile(
            "Services",
            "DataverseService.ExpenseAccountingRules.cs");

        Assert.Contains(
            "DefaultSupplierExpensesTableName = \"cr07a_gastodelaempresa\"",
            core,
            StringComparison.Ordinal);
        Assert.Contains(
            "DefaultSupplierExpensesTableSetName = \"cr07a_gastodelaempresas\"",
            core,
            StringComparison.Ordinal);
        Assert.Contains(
            "CuentaCobroExpenseAutomationSource = \"ConciliacionCuentaCobro\"",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains("_supplierExpensesTableName,", persistence, StringComparison.Ordinal);
        Assert.Contains(
            "ResolveTaxExpenseFieldMap(metadata, attributes)",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains(
            "SetAccountCatalogValue(payload, attributes, fields.PaymentValueField",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains(
            "SetAccountCatalogValue(payload, attributes, ConciliacionDianExcelKeyField",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains("CuentaCobroRetentionsJsonField,", persistence, StringComparison.Ordinal);
        Assert.Contains("CuentaCobroSiigoDocumentIdField,", persistence, StringComparison.Ordinal);
        Assert.Contains("CuentaCobroSiigoDocumentNameField,", persistence, StringComparison.Ordinal);
        Assert.Contains("CuentaCobroSiigoPaymentIdField,", persistence, StringComparison.Ordinal);
        Assert.Contains("CuentaCobroSiigoPaymentNameField,", persistence, StringComparison.Ordinal);
        Assert.Contains("CuentaCobroSiigoResponseField,", persistence, StringComparison.Ordinal);
        Assert.Contains("CuentaCobroSiigoPaymentResponseField", persistence, StringComparison.Ordinal);
        Assert.Contains(
            "HasActiveDianSupplierDocumentExcelKeyAsync(metadata.LogicalName, ct)",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains(
            "recordSource = $\"cashflow-record:{cashFlowRecordId}\";",
            persistence,
            StringComparison.Ordinal);

        Assert.Contains(
            "payload.Remove(ConciliacionDianExcelKeyField);",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains(
            "({ConciliacionDianExcelKeyField}='{alternateKey}')",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains("\"PATCH\"", persistence, StringComparison.Ordinal);
        Assert.Contains("\"If-None-Match\", \"*\"", persistence, StringComparison.Ordinal);
        Assert.Contains(
            "response.StatusCode == HttpStatusCode.PreconditionFailed",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains(
            "TryPatchExpenseAccountingRowAsync(",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"If-Match\", etag",
            optimisticConcurrency,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CuentaCobroSiigoSagaClaimsBeforePostingAndCheckpointsBeforePayment()
    {
        var controller = ReadProjectFile("Controllers", "ConciliacionController.cs");
        var action = SliceMethod(
            controller,
            "public async Task<IActionResult> SendCuentaCobroSupportDocumentToSiigo(",
            "public async Task<IActionResult> SendCuentaCobroSupportPaymentToSiigo(");

        var claim = action.IndexOf(
            "_dataverse.TryClaimConciliacionCuentaCobroSupportDocumentForSiigoAsync",
            StringComparison.Ordinal);
        var supportDocumentPost = action.IndexOf(
            "_siigo.CreatePurchaseSupportDocumentAsync",
            StringComparison.Ordinal);
        var durableDocumentCheckpoint = action.IndexOf(
            "stateOverride: CuentaCobroSupportDocumentPendingPaymentState",
            StringComparison.Ordinal);
        var paymentPost = action.IndexOf(
            "_siigo.CreateJournalAsync",
            StringComparison.Ordinal);

        Assert.True(claim >= 0, "Falta el claim atomico previo al documento soporte.");
        Assert.True(supportDocumentPost > claim, "El POST DS debe ocurrir despues del claim.");
        Assert.True(
            durableDocumentCheckpoint > supportDocumentPost,
            "El identificador DS debe guardarse despues de la respuesta Siigo.");
        Assert.True(
            paymentPost > durableDocumentCheckpoint,
            "El pago no debe enviarse antes del checkpoint durable del DS.");
        Assert.Contains(
            "var durableCheckpointCt = CancellationToken.None;",
            action,
            StringComparison.Ordinal);
        Assert.Contains(
            "stateOverride: CuentaCobroSupportDocumentVerificationState",
            action,
            StringComparison.Ordinal);
        Assert.Contains(
            "catch (InvalidOperationException ex) when (!IsAmbiguousSupplierCreateFailure(ex))",
            action,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CuentaCobroPreflightUsesAParseableSyntheticDueAndNoPaymentTypeFallback()
    {
        var controller = ReadProjectFile("Controllers", "ConciliacionController.cs");

        Assert.DoesNotContain("\"PREVALIDACION-DS\"", controller, StringComparison.Ordinal);
        Assert.Equal(
            2,
            CountOccurrences(controller, "supportDocumentName: \"PREVALIDACION-1\""));
        Assert.Contains(
            "var supportTypes = await _siigo.GetPaymentTypesAsync(\"DS\", ct);",
            controller,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "return await _siigo.GetPaymentTypesAsync(\"FC\", ct);",
            controller,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Id = 1726",
            SliceMethod(
                controller,
                "internal static SiigoPaymentTypeLookupDto ResolveSupportDocumentPaymentType(",
                "private static bool TryParseSiigoDueLabel("),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CuentaCobroHoldIsEnforcedByServiceViewAndEditor()
    {
        var controller = ReadProjectFile("Controllers", "ConciliacionController.cs");
        var persistence = ReadProjectFile("Services", "DataverseService.ConciliacionCuentasCobro.cs");
        var view = ReadProjectFile("Views", "Conciliacion", "Index.cshtml");
        var javascript = ReadProjectFile("wwwroot", "js", "conciliacion.js");
        var wizardActions = SliceMethod(
            javascript,
            "const updateCuentaCobroWizardActions = () => {",
            "const renderCuentaCobroWizardPayload = () => {");
        var paymentCore = SliceMethod(
            controller,
            "private async Task<IActionResult> SendCuentaCobroSupportPaymentToSiigoCoreAsync(",
            "private async Task<ConciliacionSiigoOpenInvoiceSearchResultDto> SearchClientOpenInvoicesForPaymentAsync(");
        var savedRecordGate = SliceMethod(
            wizardActions,
            "const canUseSavedRecord = hasRecord",
            "const actions = [");

        Assert.Contains(
            "CuentaCobroSupportDocumentProcessingState = \"ProcesandoDocumentoSoporteSiigo\"",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains(
            "CuentaCobroSupportDocumentVerificationState = \"VerificacionDocumentoSoporteSiigoPendiente\"",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains(
            "httpRequest => httpRequest.Headers.TryAddWithoutValidation(\"If-Match\", etag)",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains(
            "EnsureConciliacionCuentaCobroCashFlowLink(current, cashFlow);",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains(
            "var exactExpenseMatches = new CuentaCobroAutomationRow?[cashRows.Length];",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains(
            ".Where(row => !IsConciliacionCuentaCobroExpenseSource(row.RecordSource))",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains("if (!isExpense)", persistence, StringComparison.Ordinal);
        Assert.Contains(
            "El envio real exige el gasto canonico en cr07a_gastodelaempresa.",
            persistence,
            StringComparison.Ordinal);

        Assert.Contains("hasSupportDocumentWriteHold", view, StringComparison.Ordinal);
        Assert.Contains(
            "!hasSupportDocument && !hasSupportDocumentWriteHold",
            view,
            StringComparison.Ordinal);
        Assert.Contains("@if (!isLegacySupportRow)", view, StringComparison.Ordinal);
        Assert.Contains("data-concurrency-token=\"@row.ConcurrencyToken\"", view, StringComparison.Ordinal);

        Assert.Contains(
            "Los registros historicos no pueden crear un documento soporte real.",
            controller,
            StringComparison.Ordinal);
        Assert.Contains("var hasDocumentWriteHold =", paymentCore, StringComparison.Ordinal);
        Assert.Contains(
            "No se enviara el pago hasta resolverla explicitamente.",
            paymentCore,
            StringComparison.Ordinal);
        Assert.Contains(
            "La fecha de emision de la cuenta de cobro no puede ser posterior a la fecha de pago.",
            controller,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (emissionDate > paymentDate)",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains(
            "state.needsSiigoVerification",
            javascript,
            StringComparison.Ordinal);
        Assert.Contains(
            "state.siigoDocumentInProgress",
            javascript,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "state.isLocked",
            savedRecordGate,
            StringComparison.Ordinal);
        Assert.Contains(
            "(hasHistoricalAction ? historicalPayment : hasDocument && !hasPayment)",
            wizardActions,
            StringComparison.Ordinal);
        Assert.Contains("&& !state.isLegacy", wizardActions, StringComparison.Ordinal);
        Assert.Contains("&& !hasDocumentWriteHold", wizardActions, StringComparison.Ordinal);
        Assert.Contains(
            "No reintentes el envio.",
            javascript,
            StringComparison.Ordinal);
        Assert.Contains(
            "La fecha de emision no puede ser posterior a la fecha de pago.",
            javascript,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CuentaCobroIntegratedFlowPersistsAllocationBeforeCreatingAndPayingInSiigo()
    {
        var models = ReadProjectFile("Models", "Conciliacion", "ConciliacionModels.cs");
        var persistence = ReadProjectFile("Services", "DataverseService.ConciliacionCuentasCobro.cs");
        var javascript = ReadProjectFile("wwwroot", "js", "conciliacion.js");
        var integration = SliceMethod(
            javascript,
            "const registerCashFlowWizardCuentaCobroInSiigo = async () => {",
            "const accountingVoucherRowHost =");

        Assert.Contains("public decimal CloudValue { get; set; }", models, StringComparison.Ordinal);
        Assert.Contains("public decimal CopiersValue { get; set; }", models, StringComparison.Ordinal);
        Assert.Contains("public string CategoryValue { get; set; } = \"\";", models, StringComparison.Ordinal);
        Assert.Contains("Cloud y Copiers deben sumar la base del gasto sin IVA.", persistence, StringComparison.Ordinal);
        Assert.Contains("fields.CloudField", persistence, StringComparison.Ordinal);
        Assert.Contains("fields.CopiersField", persistence, StringComparison.Ordinal);
        Assert.Contains("DashboardExpenseCategoryField", persistence, StringComparison.Ordinal);
        Assert.Contains(
            "payment.ToString(\"0.00\", CultureInfo.InvariantCulture)",
            persistence,
            StringComparison.Ordinal);
        Assert.Contains(
            "fields.PaymentValueField,\n                null,",
            persistence.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.Contains("data-cnc-wizard-cuenta-complete", javascript, StringComparison.Ordinal);
        Assert.Contains("cloudValue: Number(form.cloudValue || 0)", javascript, StringComparison.Ordinal);
        Assert.Contains("copiersValue: Number(form.copiersValue || 0)", javascript, StringComparison.Ordinal);

        var saveIndex = integration.IndexOf("saveCashFlowWizardCuentaCobroExpense", StringComparison.Ordinal);
        var preflightIndex = integration.IndexOf("cuentaCobroPreflightUrl", StringComparison.Ordinal);
        var sendIndex = integration.IndexOf("cuentaCobroSendUrl", StringComparison.Ordinal);
        Assert.True(saveIndex >= 0 && preflightIndex > saveIndex && sendIndex > preflightIndex,
            "El flujo integrado debe guardar Dataverse, validar y luego crear documento soporte y pago en Siigo.");
    }

    [Fact]
    public void CuentaCobroRequiresSelectingAnActiveSiigoSupplierBeforeSavingOrPosting()
    {
        var controller = ReadProjectFile("Controllers", "ConciliacionController.cs");
        var models = ReadProjectFile("Models", "Conciliacion", "ConciliacionModels.cs");
        var persistence = ReadProjectFile("Services", "DataverseService.ConciliacionCuentasCobro.cs");
        var javascript = ReadProjectFile("wwwroot", "js", "conciliacion.js");

        Assert.Contains("data-cnc-wizard-cuenta-supplier-query", javascript, StringComparison.Ordinal);
        Assert.Contains("searchCuentaCobroWizardSuppliers", javascript, StringComparison.Ordinal);
        Assert.Contains("siigoSupplierSearchUrl", javascript, StringComparison.Ordinal);
        Assert.Contains(
            "Crealo o activalo primero en Siigo y vuelve a buscarlo aqui.",
            javascript,
            StringComparison.Ordinal);
        Assert.Contains(
            "Busca y selecciona el proveedor activo en Siigo.",
            javascript,
            StringComparison.Ordinal);
        Assert.Contains("siigoSupplierId: String(state.supplier?.id || \"\")", javascript, StringComparison.Ordinal);
        Assert.Contains("public string SiigoSupplierIdentification { get; set; }", models, StringComparison.Ordinal);
        Assert.Contains("ResolveCuentaCobroSelectedSiigoSupplierAsync(request, supplierIssues, ct)", controller, StringComparison.Ordinal);
        Assert.Contains("SearchCustomersAsync(selectedIdentification, top: 50, ct)", controller, StringComparison.Ordinal);
        Assert.Contains("DianSupplierDocumentSiigoSupplierIdField", persistence, StringComparison.Ordinal);
        Assert.Contains("Selecciona un proveedor activo de Siigo antes de guardar la cuenta de cobro.", persistence, StringComparison.Ordinal);
        Assert.Contains("Selecciona el proveedor real de Siigo antes de crear el documento soporte.", controller, StringComparison.Ordinal);
    }


    private static string ReadProjectFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([ProjectRoot, .. parts]));

    private static string SliceMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No se encontro el inicio esperado: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"No se encontro el final esperado: {endMarker}");
        return source[start..end];
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = source.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CotizadorInterno.Web.csproj")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "No se encontro la raiz del proyecto CotizadorInterno.Web.");
    }
}
