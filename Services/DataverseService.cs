using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Calculator;
using CotizadorInterno.Web.Models.Nomina;
using CotizadorInterno.Web.Models.PortalProveedores;
using CotizadorInterno.Web.Models.Renovaciones;
using CotizadorInterno.Web.Services.Calculator;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService : IDataverseService
{
    private readonly IDownstreamApi _downstreamApi;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DataverseService> _logger;
    private readonly IQuoteCalculator _calculator;
    private readonly ConcurrentDictionary<string, string[]> _salesPerformanceNavigationPropertyCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _salesPerformancePrimaryNameFieldCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _entityPrimaryNameFieldCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
    private const string DefaultScenariosTableSetName = "cr07a_negocioscomercialeses";
    private const string DefaultScenariosTableName = "cr07a_negocioscomerciales";
    private const string DefaultSalesPerformanceEntityLogicalName = "cr07a_salesperformancerecord";
    private const string DefaultSalesPerformanceTableSetName = "cr07a_salesperformancerecords";
    private const string DefaultSalesPerformanceIdField = "cr07a_salesperformancerecordid";
    private const string DefaultSalesPerformanceClientLookupFilterField = "_cr07a_clientelookup_value";
    private const string DefaultSalesPerformanceRenewalDateField = "cr07a_fecharenovacion";
    private const string DefaultSalesPerformanceClientLookupLogicalName = "cr07a_clientelookup";
    private const string DefaultSalesPerformanceProductLookupLogicalName = "cr07a_producto";
    private const string DefaultSalesPerformanceQuantityField = "cr07a_quantity";
    private const string DefaultSalesPerformanceUnitSaleUsdField = "cr07a_valorventaunidadusd";
    private const string DefaultSupplierExpensesTableSetName = "cr07a_gastodelaempresas";
    private const string DefaultSupplierExpensesTableName = "cr07a_gastodelaempresa";
    private const string DefaultSupplierExpensesIdField = "cr07a_gastodelaempresaid";
    private const string DefaultSupplierExpensesDateField = "createdon";
    private const string DefaultSupplierExpensesDateFieldKind = "date-time";
    private const string DefaultScoresTableSetName = "cr07a_contractrecord1s";
    private const string DefaultScoresTableName = "cr07a_contractrecord1";
    private const string DefaultScoresIdField = "cr07a_contractrecord1id";
    private const string DefaultScoresContractStartDateField = "cr07a_contractstartdate";
    private const string DefaultScoresScoreField = "cr07a_score";
    private const string DefaultScoresDescriptionField = "cr07a_description";
    private const string DefaultScoresCommissionField = "cr07a_commission";
    private const string DefaultScoresClientField = "cr07a_cliente";
    private const string DefaultScoresSalesPersonField = "cr07a_vendedor";
    private const string DefaultScoresOfferField = "cr07a_oferta";
    private const string DefaultScoresVerifiedField = "cr07a_verificado";
    private const string DefaultScoresFirstContractField = "cr07a_esprimercontratoconelcliente";
    private const string DefaultScoresLineField = "cr07a_linea";
    private const string DefaultScoresVerticalField = "cr07a_vertical";
    private const string DefaultScoresAdditionalField = "cr07a_adicionales";
    private const string DefaultNominaEmployeeTableSetName = "cr07a_empleados";
    private const string DefaultNominaEmployeeTableName = "cr07a_empleado";
    private const string DefaultNominaEmployeeIdField = "cr07a_empleadoid";
    private const string DefaultNominaEmployeeNameField = "cr07a_nombrecompleto";
    private const string DefaultNominaEmployeeSalaryField = "cr07a_sueldomensual";
    private const string DefaultNominaEmployeeConnectivityAllowanceField = "cr07a_auxconectividad";
    private const string DefaultNominaEmployeeCommissionCapField = "cr07a_topecomisional";
    private const string DefaultNominaEmployeeCopiersFactorField = "cr07a_factorcopiers";
    private const string DefaultNominaEmployeeCloudFactorField = "cr07a_factorcloud";
    private const string DefaultNominaPayrollTableSetName = "cr07a_nominas";
    private const string DefaultNominaPayrollTableName = "cr07a_nomina";
    private const string DefaultNominaPayrollIdField = "cr07a_nominaid";
    private const string DefaultNominaPayrollNameField = "cr07a_name";
    private const string DefaultNominaPayrollEmployeeLookupField = "cr07a_idempleado";
    private const string DefaultNominaPayrollEmployeeLookupNavigationProperty = "cr07a_Nomina_cr07a_IDEmpleado_cr07a_Empleado";
    private const string DefaultNominaPayrollPaymentDateField = "cr07a_fechapago";
    private const string DefaultNominaPayrollSalaryBaseField = "cr07a_sueldobase";
    private const string DefaultNominaPayrollConnectivityAllowanceField = "cr07a_auxilio";
    private const string DefaultNominaPayrollBonusComplianceField = "cr07a_bonocumplimiento";
    private const string DefaultNominaPayrollCommissionsCopiersField = "cr07a_comisionescopiers";
    private const string DefaultNominaPayrollCommissionsCloudField = "cr07a_comisionescloud";
    private const string DefaultNominaPayrollCommissionsField = "cr07a_comisiones";
    private const string DefaultNominaPayrollGrossSalaryField = "cr07a_sueldobruto";
    private const string DefaultNominaPayrollHealthField = "cr07a_salud";
    private const string DefaultNominaPayrollPensionField = "cr07a_pension";
    private const string DefaultNominaPayrollOtherDeductionsField = "cr07a_otrasdeducciones";
    private const string DefaultNominaPayrollLoanField = "cr07a_prestamo";
    private const string DefaultNominaPayrollCuentaDeCobroField = "cr07a_cuentadecobro";
    private const string DefaultNominaPayrollWithholdingField = "cr07a_retencionenlafuentenomina";
    private const string DefaultNominaPayrollExternalWithholdingField = "cr07a_retencionenlafuenteexterno";
    private const string DefaultNominaPayrollNetAmountField = "cr07a_montopagado";
    private const string DefaultNominaPayrollNetCuentaDeCobroField = "cr07a_montopagadocuentadecobro";
    private const string DefaultNominaScoresEmployeeLookupField = "cr07a_comercial";
    private const string DefaultSalesPerformancePrimaryNameField = "cr07a_name";
    private const string DefaultSalesPerformanceBillingDayField = "cr07a_billingday";
    private const string DefaultSalesPerformanceHasVatField = "cr07a_sitieneiva";
    private const string DefaultSalesPerformanceAutoBillField = "cr07a_facturableautomatico";
    private const string DefaultSalesPerformanceProductLineField = "cr07a_productline";
    private const string DefaultSalesPerformanceContractTypeField = "cr07a_contracttype";
    private const string DefaultDashboardBillingTableLogicalName = "cr07a_facturacion";
    private const string DefaultDashboardBillingTableSetName = "cr07a_facturacions";
    private const string DefaultDashboardBillingIdField = "cr07a_facturacionid";
    private const string DefaultDashboardBillingPrimaryNameField = "cr07a_name";
    private const string DefaultDashboardBillingEmissionDateField = "cr07a_fechadeemision";
    private const string DefaultDashboardBillingEmissionDateFieldKind = "date-only";
    private const string DefaultDashboardBillingCompanyTaxIdField = "cr07a_nitempresa";
    private const string DefaultDashboardBillingInvoiceNumberField = "cr07a_name";
    private const string DefaultDashboardBillingClientField = "cr07a_clientenit";
    private const string DefaultDashboardBillingVerticalField = "cr07a_vertical";
    private const string DefaultDashboardBillingContractTypeField = "cr07a_tipocontrato";
    private const string DefaultDashboardBillingDueDateField = "cr07a_fechavencimiento";
    private const string DefaultDashboardBillingDueDateFieldKind = "date-only";
    private const string DefaultDashboardBillingTotalField = "cr07a_totalfactura";
    private const string DefaultDashboardBillingVatPercentField = "cr07a_iva";
    private const string DefaultDashboardBillingVatField = "cr07a_ivavalor";
    private const string DefaultDashboardBillingPublicUrlField = "cr07a_publicurl";
    private const string DefaultDashboardBillingPaymentDateField = "cr07a_fechadepago";
    private const string DefaultDashboardBillingPaymentDateFieldKind = "date-only";
    private const string DefaultDashboardBillingPaymentValueField = "cr07a_valorpago";
    private const string DefaultDashboardBillingReteIcaField = "cr07a_reteicavalor";
    private const string DefaultDashboardBillingRteIvaField = "cr07a_rteivavalor";
    private const string DefaultDashboardBillingRteFteField = "cr07a_rteftevalor";
    private const string DefaultDashboardBillingDifferenceField = "cr07a_diferencia";
    private const string DefaultDashboardCopiersTableLogicalName = "cr07a_productoscopiers";
    private const string DefaultDashboardCopiersTableSetName = "cr07a_productoscopiers";
    private const string DefaultDashboardCopiersIdField = "cr07a_productoscopiersid";
    private const string DefaultDashboardCopiersPrimaryNameField = "cr07a_producto";
    private const string DefaultDashboardCopiersQuantityField = "cr07a_cantidad";
    private const string DefaultDashboardCopiersProductField = "cr07a_producto";
    private const string DefaultDashboardCopiersUnitValueBeforeVatField = "cr07a_valorunidadantesdeiva";
    private const string DefaultDashboardCopiersBillingDayField = "cr07a_diadefacturacion";
    private const string DefaultDashboardCopiersIncludedOperationsField = "cr07a_operacionesincluidas";
    private const string DefaultDashboardCopiersClientField = "cr07a_cliente";
    private const string DefaultDashboardCopiersUnitValueWithVatField = "cr07a_valorunidadconiva";
    private const string DefaultDashboardCopiersTotalWithVatField = "cr07a_totalconiva";
    private const string DefaultSalesPerformanceClientCreateLookupLogicalName = "cr07a_clientelookup";
    private const string ClientsEntitySetName = "cr07a_clientes";
    private const string ProductsEntityLogicalName = "cr07a_precioscloud";
    private const string ProductsEntitySetName = "cr07a_preciosclouds";
    private const string ProductsIdField = "cr07a_precioscloudid";
    private const string ProductsDescriptionField = "cr07a_priceableitemdescription";
    private const string ProductsPurchasePriceField = "cr07a_purchaseprice";
    private const string ProductsSuggestedRetailPriceField = "cr07a_suggestedretailprice";
    private const string ProductsAceleradorField = "cr07a_acelerador";
    private const string FormattedValueAnnotationSuffix = "@OData.Community.Display.V1.FormattedValue";
    private static readonly string[] SalesPerformanceClientLookupFieldCandidates =
    {
        "_cr07a_clientelookup_value",
        "_cr07a_clienteid_value",
        "_cr07a_cliente_value"
    };
    private static readonly string[] SalesPerformanceProductLookupFieldCandidates =
    {
        "_cr07a_producto_value"
    };
    private readonly string _scenariosTableSetName;
    private readonly string _scenariosTableName;
    private readonly string _salesPerformanceTableSetName;
    private readonly string _salesPerformanceIdField;
    private readonly string _salesPerformanceClientLookupFilterField;
    private readonly string _salesPerformanceRenewalDateField;
    private readonly string _salesPerformanceClientLookupLogicalName;
    private readonly string _salesPerformanceProductLookupLogicalName;
    private readonly string _salesPerformancePrimaryNameField;
    private readonly string _salesPerformanceBillingDayField;
    private readonly string _salesPerformanceHasVatField;
    private readonly string _salesPerformanceAutoBillField;
    private readonly string _salesPerformanceProductLineField;
    private readonly string _salesPerformanceContractTypeField;
    private readonly string _supplierExpensesTableSetName;
    private readonly string _supplierExpensesTableName;
    private readonly string _supplierExpensesIdField;
    private readonly string _supplierExpensesDateField;
    private readonly string _supplierExpensesDateFieldKind;
    private readonly string _scoresTableSetName;
    private readonly string _scoresTableName;
    private readonly string _scoresIdField;
    private readonly string _scoresContractStartDateField;
    private readonly string _scoresScoreField;
    private readonly string _scoresDescriptionField;
    private readonly string _scoresCommissionField;
    private readonly string _scoresClientField;
    private readonly string _scoresSalesPersonField;
    private readonly string _scoresOfferField;
    private readonly string _scoresVerifiedField;
    private readonly string _scoresFirstContractField;
    private readonly string _scoresLineField;
    private readonly string _scoresVerticalField;
    private readonly string _scoresAdditionalField;
    private readonly string _scoresBillingNotificationFlowUrl;
    private readonly string _scoresBillingNotificationRecipientEmail;
    private readonly string _nominaEmployeeTableSetName;
    private readonly string _nominaEmployeeTableName;
    private readonly string _nominaEmployeeIdField;
    private readonly string _nominaEmployeeNameField;
    private readonly string _nominaEmployeeSalaryField;
    private readonly string _nominaEmployeeConnectivityAllowanceField;
    private readonly string _nominaEmployeeCommissionCapField;
    private readonly string _nominaEmployeeCopiersFactorField;
    private readonly string _nominaEmployeeCloudFactorField;
    private readonly string _nominaPayrollTableSetName;
    private readonly string _nominaPayrollTableName;
    private readonly string _nominaPayrollIdField;
    private readonly string _nominaPayrollNameField;
    private readonly string _nominaPayrollEmployeeLookupField;
    private readonly string _nominaPayrollEmployeeLookupNavigationProperty;
    private readonly string _nominaPayrollPaymentDateField;
    private readonly string _nominaPayrollSalaryBaseField;
    private readonly string _nominaPayrollConnectivityAllowanceField;
    private readonly string _nominaPayrollBonusComplianceField;
    private readonly string _nominaPayrollCommissionsCopiersField;
    private readonly string _nominaPayrollCommissionsCloudField;
    private readonly string _nominaPayrollCommissionsField;
    private readonly string _nominaPayrollGrossSalaryField;
    private readonly string _nominaPayrollHealthField;
    private readonly string _nominaPayrollPensionField;
    private readonly string _nominaPayrollOtherDeductionsField;
    private readonly string _nominaPayrollLoanField;
    private readonly string _nominaPayrollCuentaDeCobroField;
    private readonly string _nominaPayrollWithholdingField;
    private readonly string _nominaPayrollExternalWithholdingField;
    private readonly string _nominaPayrollNetAmountField;
    private readonly string _nominaPayrollNetCuentaDeCobroField;
    private readonly string _nominaScoresEmployeeLookupField;
    private readonly decimal _nominaHealthRate;
    private readonly decimal _nominaPensionRate;
    private readonly string _rhVacationApprovalFlowUrl;
    private readonly string _rhVacationRequestNotesField;
    private readonly string _rhVacationRequestFormatField;
    private readonly string _rhVacationRequestFormatFileNameField;
    private readonly string _rhCompanyName;
    private readonly string _rhCompanyNit;
    private readonly string _rhCompanyAddress;
    private readonly string _rhCompanyCity;
    private readonly string _dashboardBillingTableLogicalName;
    private readonly string _dashboardBillingTableSetName;
    private readonly string _dashboardBillingIdField;
    private readonly string _dashboardBillingPrimaryNameField;
    private readonly string _dashboardBillingEmissionDateField;
    private readonly string _dashboardBillingEmissionDateFieldKind;
    private readonly string _dashboardBillingCompanyTaxIdField;
    private readonly string _dashboardBillingInvoiceNumberField;
    private readonly string _dashboardBillingClientField;
    private readonly string _dashboardBillingVerticalField;
    private readonly string _dashboardBillingContractTypeField;
    private readonly string _dashboardBillingDueDateField;
    private readonly string _dashboardBillingDueDateFieldKind;
    private readonly string _dashboardBillingTotalField;
    private readonly string _dashboardBillingVatPercentField;
    private readonly string _dashboardBillingVatField;
    private readonly string _dashboardBillingPublicUrlField;
    private readonly string _dashboardBillingPaymentDateField;
    private readonly string _dashboardBillingPaymentDateFieldKind;
    private readonly string _dashboardBillingPaymentValueField;
    private readonly string _dashboardBillingReteIcaField;
    private readonly string _dashboardBillingRteIvaField;
    private readonly string _dashboardBillingRteFteField;
    private readonly string _dashboardBillingDifferenceField;
    private readonly string _dashboardCopiersTableLogicalName;
    private readonly string _dashboardCopiersTableSetName;
    private readonly string _dashboardCopiersIdField;
    private readonly string _dashboardCopiersPrimaryNameField;
    private readonly string _dashboardCopiersQuantityField;
    private readonly string _dashboardCopiersProductField;
    private readonly string _dashboardCopiersUnitValueBeforeVatField;
    private readonly string _dashboardCopiersBillingDayField;
    private readonly string _dashboardCopiersIncludedOperationsField;
    private readonly string _dashboardCopiersClientField;
    private readonly string _dashboardCopiersUnitValueWithVatField;
    private readonly string _dashboardCopiersTotalWithVatField;

    public DataverseService(
        IDownstreamApi downstreamApi,
        IHttpContextAccessor httpContextAccessor,
        IHttpClientFactory httpClientFactory,
        IQuoteCalculator calculator,
        IConfiguration configuration,
        IOptions<RhOptions> rhOptions,
        ILogger<DataverseService> logger)
    {
        _downstreamApi = downstreamApi;
        _httpContextAccessor = httpContextAccessor;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _calculator = calculator;
        var rh = rhOptions.Value;
        _scenariosTableSetName = configuration["Dataverse:ScenariosTableSetName"]
            ?? DefaultScenariosTableSetName;
        _scenariosTableName = configuration["Dataverse:ScenariosTableName"]
            ?? DefaultScenariosTableName;
        _salesPerformanceTableSetName = configuration["Dataverse:SalesPerformanceTableSetName"]
            ?? DefaultSalesPerformanceTableSetName;
        _salesPerformanceIdField = configuration["Dataverse:SalesPerformanceIdField"]
            ?? DefaultSalesPerformanceIdField;
        _salesPerformanceClientLookupFilterField = configuration["Dataverse:SalesPerformanceClientLookupFilterField"]
            ?? DefaultSalesPerformanceClientLookupFilterField;
        _salesPerformanceRenewalDateField = configuration["Dataverse:SalesPerformanceRenewalDateField"]
            ?? DefaultSalesPerformanceRenewalDateField;
        _salesPerformanceClientLookupLogicalName = configuration["Dataverse:SalesPerformanceClientLookupLogicalName"]
            ?? DefaultSalesPerformanceClientCreateLookupLogicalName;
        _salesPerformanceProductLookupLogicalName = configuration["Dataverse:SalesPerformanceProductLookupLogicalName"]
            ?? DefaultSalesPerformanceProductLookupLogicalName;
        _salesPerformancePrimaryNameField = configuration["Dataverse:SalesPerformancePrimaryNameField"]
            ?? DefaultSalesPerformancePrimaryNameField;
        _salesPerformanceBillingDayField = configuration["Dataverse:SalesPerformanceBillingDayField"]
            ?? DefaultSalesPerformanceBillingDayField;
        _salesPerformanceHasVatField = configuration["Dataverse:SalesPerformanceHasVatField"]
            ?? DefaultSalesPerformanceHasVatField;
        _salesPerformanceAutoBillField = configuration["Dataverse:SalesPerformanceAutoBillField"]
            ?? DefaultSalesPerformanceAutoBillField;
        _salesPerformanceProductLineField = configuration["Dataverse:SalesPerformanceProductLineField"]
            ?? DefaultSalesPerformanceProductLineField;
        _salesPerformanceContractTypeField = configuration["Dataverse:SalesPerformanceContractTypeField"]
            ?? DefaultSalesPerformanceContractTypeField;
        _supplierExpensesTableSetName = configuration["SupplierPortal:ExpensesTableSetName"]
            ?? DefaultSupplierExpensesTableSetName;
        _supplierExpensesTableName = configuration["SupplierPortal:ExpensesTableName"]
            ?? DefaultSupplierExpensesTableName;
        _supplierExpensesIdField = configuration["SupplierPortal:ExpensesIdField"]
            ?? DefaultSupplierExpensesIdField;
        _supplierExpensesDateField = configuration["SupplierPortal:ExpensesDateField"]
            ?? DefaultSupplierExpensesDateField;
        _supplierExpensesDateFieldKind = configuration["SupplierPortal:ExpensesDateFieldKind"]
            ?? DefaultSupplierExpensesDateFieldKind;
        _scoresTableSetName = configuration["Scores:TableSetName"]
            ?? DefaultScoresTableSetName;
        _scoresTableName = configuration["Scores:TableName"]
            ?? DefaultScoresTableName;
        _scoresIdField = configuration["Scores:IdField"]
            ?? DefaultScoresIdField;
        _scoresContractStartDateField = configuration["Scores:ContractStartDateField"]
            ?? DefaultScoresContractStartDateField;
        _scoresScoreField = configuration["Scores:ScoreField"]
            ?? DefaultScoresScoreField;
        _scoresDescriptionField = configuration["Scores:DescriptionField"]
            ?? DefaultScoresDescriptionField;
        _scoresCommissionField = configuration["Scores:CommissionField"]
            ?? DefaultScoresCommissionField;
        _scoresClientField = configuration["Scores:ClientField"]
            ?? DefaultScoresClientField;
        _scoresSalesPersonField = configuration["Scores:SalesPersonField"]
            ?? DefaultScoresSalesPersonField;
        _scoresOfferField = configuration["Scores:OfferField"]
            ?? DefaultScoresOfferField;
        _scoresVerifiedField = configuration["Scores:VerifiedField"]
            ?? DefaultScoresVerifiedField;
        _scoresFirstContractField = configuration["Scores:FirstContractField"]
            ?? DefaultScoresFirstContractField;
        _scoresLineField = configuration["Scores:LineField"]
            ?? DefaultScoresLineField;
        _scoresVerticalField = configuration["Scores:VerticalField"]
            ?? DefaultScoresVerticalField;
        _scoresAdditionalField = configuration["Scores:AdditionalField"]
            ?? DefaultScoresAdditionalField;
        _scoresBillingNotificationFlowUrl = configuration["Scores:BillingNotificationFlowUrl"] ?? "";
        _scoresBillingNotificationRecipientEmail = configuration["Scores:BillingNotificationRecipientEmail"] ?? "";
        _nominaEmployeeTableSetName = configuration["Nomina:EmployeeTableSetName"]
            ?? DefaultNominaEmployeeTableSetName;
        _nominaEmployeeTableName = configuration["Nomina:EmployeeTableName"]
            ?? DefaultNominaEmployeeTableName;
        _nominaEmployeeIdField = configuration["Nomina:EmployeeIdField"]
            ?? DefaultNominaEmployeeIdField;
        _nominaEmployeeNameField = configuration["Nomina:EmployeeNameField"]
            ?? DefaultNominaEmployeeNameField;
        _nominaEmployeeSalaryField = configuration["Nomina:EmployeeSalaryField"]
            ?? DefaultNominaEmployeeSalaryField;
        _nominaEmployeeConnectivityAllowanceField = configuration["Nomina:EmployeeConnectivityAllowanceField"]
            ?? DefaultNominaEmployeeConnectivityAllowanceField;
        _nominaEmployeeCommissionCapField = configuration["Nomina:EmployeeCommissionCapField"]
            ?? DefaultNominaEmployeeCommissionCapField;
        _nominaEmployeeCopiersFactorField = configuration["Nomina:EmployeeCopiersFactorField"]
            ?? DefaultNominaEmployeeCopiersFactorField;
        _nominaEmployeeCloudFactorField = configuration["Nomina:EmployeeCloudFactorField"]
            ?? DefaultNominaEmployeeCloudFactorField;
        _nominaPayrollTableSetName = configuration["Nomina:PayrollTableSetName"]
            ?? DefaultNominaPayrollTableSetName;
        _nominaPayrollTableName = configuration["Nomina:PayrollTableName"]
            ?? DefaultNominaPayrollTableName;
        _nominaPayrollIdField = configuration["Nomina:PayrollIdField"]
            ?? DefaultNominaPayrollIdField;
        _nominaPayrollNameField = configuration["Nomina:PayrollNameField"]
            ?? DefaultNominaPayrollNameField;
        _nominaPayrollEmployeeLookupField = configuration["Nomina:PayrollEmployeeLookupField"]
            ?? DefaultNominaPayrollEmployeeLookupField;
        _nominaPayrollEmployeeLookupNavigationProperty = configuration["Nomina:PayrollEmployeeLookupNavigationProperty"]
            ?? DefaultNominaPayrollEmployeeLookupNavigationProperty;
        _nominaPayrollPaymentDateField = configuration["Nomina:PayrollPaymentDateField"]
            ?? DefaultNominaPayrollPaymentDateField;
        _nominaPayrollSalaryBaseField = configuration["Nomina:PayrollSalaryBaseField"]
            ?? DefaultNominaPayrollSalaryBaseField;
        _nominaPayrollConnectivityAllowanceField = configuration["Nomina:PayrollConnectivityAllowanceField"]
            ?? DefaultNominaPayrollConnectivityAllowanceField;
        _nominaPayrollBonusComplianceField = configuration["Nomina:PayrollBonusComplianceField"]
            ?? DefaultNominaPayrollBonusComplianceField;
        _nominaPayrollCommissionsCopiersField = configuration["Nomina:PayrollCommissionsCopiersField"]
            ?? DefaultNominaPayrollCommissionsCopiersField;
        _nominaPayrollCommissionsCloudField = configuration["Nomina:PayrollCommissionsCloudField"]
            ?? DefaultNominaPayrollCommissionsCloudField;
        _nominaPayrollCommissionsField = configuration["Nomina:PayrollCommissionsField"]
            ?? DefaultNominaPayrollCommissionsField;
        _nominaPayrollGrossSalaryField = configuration["Nomina:PayrollGrossSalaryField"]
            ?? DefaultNominaPayrollGrossSalaryField;
        _nominaPayrollHealthField = configuration["Nomina:PayrollHealthField"]
            ?? DefaultNominaPayrollHealthField;
        _nominaPayrollPensionField = configuration["Nomina:PayrollPensionField"]
            ?? DefaultNominaPayrollPensionField;
        _nominaPayrollOtherDeductionsField = configuration["Nomina:PayrollOtherDeductionsField"]
            ?? DefaultNominaPayrollOtherDeductionsField;
        _nominaPayrollLoanField = configuration["Nomina:PayrollLoanField"]
            ?? DefaultNominaPayrollLoanField;
        _nominaPayrollCuentaDeCobroField = configuration["Nomina:PayrollCuentaDeCobroField"]
            ?? DefaultNominaPayrollCuentaDeCobroField;
        _nominaPayrollWithholdingField = configuration["Nomina:PayrollWithholdingField"]
            ?? DefaultNominaPayrollWithholdingField;
        _nominaPayrollExternalWithholdingField = configuration["Nomina:PayrollExternalWithholdingField"]
            ?? DefaultNominaPayrollExternalWithholdingField;
        _nominaPayrollNetAmountField = configuration["Nomina:PayrollNetAmountField"]
            ?? DefaultNominaPayrollNetAmountField;
        _nominaPayrollNetCuentaDeCobroField = configuration["Nomina:PayrollNetCuentaDeCobroField"]
            ?? DefaultNominaPayrollNetCuentaDeCobroField;
        _nominaScoresEmployeeLookupField = configuration["Nomina:ScoresEmployeeLookupField"]
            ?? DefaultNominaScoresEmployeeLookupField;
        _nominaHealthRate = NormalizeNominaRate(configuration["Nomina:HealthRate"], 0.04m);
        _nominaPensionRate = NormalizeNominaRate(configuration["Nomina:PensionRate"], 0.04m);
        _rhVacationApprovalFlowUrl = rh.VacationApprovalFlowUrl?.Trim() ?? "";
        _rhVacationRequestNotesField = rh.VacationRequestNotesField?.Trim() ?? "";
        _rhVacationRequestFormatField = rh.VacationRequestFormatField?.Trim() ?? "cr07a_formato";
        _rhVacationRequestFormatFileNameField = rh.VacationRequestFormatFileNameField?.Trim() ?? "cr07a_formato_name";
        _rhCompanyName = rh.CompanyName?.Trim() ?? "";
        _rhCompanyNit = rh.CompanyNit?.Trim() ?? "";
        _rhCompanyAddress = rh.CompanyAddress?.Trim() ?? "";
        _rhCompanyCity = rh.CompanyCity?.Trim() ?? "";
        _dashboardBillingTableLogicalName = configuration["Dashboard:BillingTableLogicalName"]
            ?? DefaultDashboardBillingTableLogicalName;
        _dashboardBillingTableSetName = configuration["Dashboard:BillingTableSetName"]
            ?? DefaultDashboardBillingTableSetName;
        _dashboardBillingIdField = configuration["Dashboard:BillingIdField"]
            ?? DefaultDashboardBillingIdField;
        _dashboardBillingPrimaryNameField = configuration["Dashboard:BillingPrimaryNameField"]
            ?? DefaultDashboardBillingPrimaryNameField;
        _dashboardBillingEmissionDateField = configuration["Dashboard:BillingEmissionDateField"]
            ?? DefaultDashboardBillingEmissionDateField;
        _dashboardBillingEmissionDateFieldKind = configuration["Dashboard:BillingEmissionDateFieldKind"]
            ?? DefaultDashboardBillingEmissionDateFieldKind;
        _dashboardBillingCompanyTaxIdField = configuration["Dashboard:BillingCompanyTaxIdField"]
            ?? DefaultDashboardBillingCompanyTaxIdField;
        _dashboardBillingInvoiceNumberField = configuration["Dashboard:BillingInvoiceNumberField"]
            ?? DefaultDashboardBillingInvoiceNumberField;
        _dashboardBillingClientField = configuration["Dashboard:BillingClientField"]
            ?? DefaultDashboardBillingClientField;
        _dashboardBillingVerticalField = configuration["Dashboard:BillingVerticalField"]
            ?? DefaultDashboardBillingVerticalField;
        _dashboardBillingContractTypeField = configuration["Dashboard:BillingContractTypeField"]
            ?? DefaultDashboardBillingContractTypeField;
        _dashboardBillingDueDateField = configuration["Dashboard:BillingDueDateField"]
            ?? DefaultDashboardBillingDueDateField;
        _dashboardBillingDueDateFieldKind = configuration["Dashboard:BillingDueDateFieldKind"]
            ?? DefaultDashboardBillingDueDateFieldKind;
        _dashboardBillingTotalField = configuration["Dashboard:BillingTotalField"]
            ?? DefaultDashboardBillingTotalField;
        _dashboardBillingVatPercentField = configuration["Dashboard:BillingVatPercentField"]
            ?? DefaultDashboardBillingVatPercentField;
        _dashboardBillingVatField = configuration["Dashboard:BillingVatField"]
            ?? DefaultDashboardBillingVatField;
        _dashboardBillingPublicUrlField = configuration["Dashboard:BillingPublicUrlField"]
            ?? DefaultDashboardBillingPublicUrlField;
        _dashboardBillingPaymentDateField = configuration["Dashboard:BillingPaymentDateField"]
            ?? DefaultDashboardBillingPaymentDateField;
        _dashboardBillingPaymentDateFieldKind = configuration["Dashboard:BillingPaymentDateFieldKind"]
            ?? DefaultDashboardBillingPaymentDateFieldKind;
        _dashboardBillingPaymentValueField = configuration["Dashboard:BillingPaymentValueField"]
            ?? DefaultDashboardBillingPaymentValueField;
        _dashboardBillingReteIcaField = configuration["Dashboard:BillingReteIcaField"]
            ?? DefaultDashboardBillingReteIcaField;
        _dashboardBillingRteIvaField = configuration["Dashboard:BillingRteIvaField"]
            ?? DefaultDashboardBillingRteIvaField;
        _dashboardBillingRteFteField = configuration["Dashboard:BillingRteFteField"]
            ?? DefaultDashboardBillingRteFteField;
        _dashboardBillingDifferenceField = configuration["Dashboard:BillingDifferenceField"]
            ?? DefaultDashboardBillingDifferenceField;
        _dashboardCopiersTableLogicalName = configuration["Dashboard:CopiersTableLogicalName"]
            ?? DefaultDashboardCopiersTableLogicalName;
        _dashboardCopiersTableSetName = configuration["Dashboard:CopiersTableSetName"]
            ?? DefaultDashboardCopiersTableSetName;
        _dashboardCopiersIdField = configuration["Dashboard:CopiersIdField"]
            ?? DefaultDashboardCopiersIdField;
        _dashboardCopiersPrimaryNameField = configuration["Dashboard:CopiersPrimaryNameField"]
            ?? DefaultDashboardCopiersPrimaryNameField;
        _dashboardCopiersQuantityField = configuration["Dashboard:CopiersQuantityField"]
            ?? DefaultDashboardCopiersQuantityField;
        _dashboardCopiersProductField = configuration["Dashboard:CopiersProductField"]
            ?? DefaultDashboardCopiersProductField;
        _dashboardCopiersUnitValueBeforeVatField = configuration["Dashboard:CopiersUnitValueBeforeVatField"]
            ?? DefaultDashboardCopiersUnitValueBeforeVatField;
        _dashboardCopiersBillingDayField = configuration["Dashboard:CopiersBillingDayField"]
            ?? DefaultDashboardCopiersBillingDayField;
        _dashboardCopiersIncludedOperationsField = configuration["Dashboard:CopiersIncludedOperationsField"]
            ?? DefaultDashboardCopiersIncludedOperationsField;
        _dashboardCopiersClientField = configuration["Dashboard:CopiersClientField"]
            ?? DefaultDashboardCopiersClientField;
        _dashboardCopiersUnitValueWithVatField = configuration["Dashboard:CopiersUnitValueWithVatField"]
            ?? DefaultDashboardCopiersUnitValueWithVatField;
        _dashboardCopiersTotalWithVatField = configuration["Dashboard:CopiersTotalWithVatField"]
            ?? DefaultDashboardCopiersTotalWithVatField;
    }

    public async Task<IReadOnlyList<ScenarioStoredDto>> GetScenariosForUserAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var currentUser = await GetCurrentUserAsync(ct);
        if (currentUser is null || string.IsNullOrWhiteSpace(currentUser.SystemUserId))
            return Array.Empty<ScenarioStoredDto>();

        var select = string.Join(",", new[]
        {
            "cr07a_scenarioid",
            "cr07a_scenarioname",
            "cr07a_dealtype",
            "cr07a_requiresproration",
            "cr07a_startdate",
            "cr07a_enddate",
            "cr07a_linesjson",
            "cr07a_lastresultjson"
        });

        var filter = $"cr07a_systemuserid eq '{EscapeOdataLiteral(currentUser.SystemUserId)}'";
        var relativeUrl = $"/api/data/v9.2/{_scenariosTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}";

        var json = await CallDataverseGetJsonAsync(relativeUrl, httpContext.User, ct);

        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("value");

        var list = new List<ScenarioStoredDto>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
        {
            var linesJson = item.TryGetProperty("cr07a_linesjson", out var linesProp)
                ? linesProp.GetString()
                : null;
            var resultJson = item.TryGetProperty("cr07a_lastresultjson", out var resultProp)
                ? resultProp.GetString()
                : null;

            list.Add(new ScenarioStoredDto
            {
                ScenarioId = item.TryGetProperty("cr07a_scenarioid", out var idProp) ? (idProp.GetString() ?? "") : "",
                ScenarioName = item.TryGetProperty("cr07a_scenarioname", out var nameProp) ? (nameProp.GetString() ?? "") : "",
                DealType = ReadInt(item, "cr07a_dealtype"),
                RequiresProration = ReadBool(item, "cr07a_requiresproration"),
                StartDate = item.TryGetProperty("cr07a_startdate", out var startProp) ? startProp.GetString() : null,
                EndDate = item.TryGetProperty("cr07a_enddate", out var endProp) ? endProp.GetString() : null,
                Lines = DeserializeJsonOrDefault<List<ScenarioLineInput>>(linesJson) ?? new List<ScenarioLineInput>(),
                LastResult = DeserializeJsonOrDefault<ScenarioResultSnapshot>(resultJson)
            });
        }

        return list;
    }

    public async Task UpsertScenarioAsync(ScenarioSaveRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var currentUser = await GetCurrentUserAsync(ct);
        if (currentUser is null || string.IsNullOrWhiteSpace(currentUser.SystemUserId))
            throw new InvalidOperationException("Usuario actual no disponible.");

        var recordId = await FindScenarioRecordIdAsync(request.ScenarioId, currentUser.SystemUserId, httpContext.User, ct);

        var payload = new Dictionary<string, object?>
        {
            ["cr07a_name"] = string.IsNullOrWhiteSpace(request.ScenarioName) ? "Escenario" : request.ScenarioName,
            ["cr07a_scenarioid"] = request.ScenarioId,
            ["cr07a_scenarioname"] = request.ScenarioName,
            ["cr07a_dealtype"] = request.DealType,
            ["cr07a_requiresproration"] = request.RequiresProration,
            ["cr07a_startdate"] = request.StartDate?.ToString("yyyy-MM-dd"),
            ["cr07a_enddate"] = request.EndDate?.ToString("yyyy-MM-dd"),
            ["cr07a_linesjson"] = JsonSerializer.Serialize(request.Lines ?? new List<ScenarioLineInput>()),
            ["cr07a_lastresultjson"] = request.LastResult is null ? null : JsonSerializer.Serialize(request.LastResult),
            ["cr07a_systemuserid"] = currentUser.SystemUserId,
            ["cr07a_displayname"] = currentUser.DisplayName,
            ["cr07a_email"] = currentUser.Email
        };

        if (string.IsNullOrWhiteSpace(recordId))
        {
            var relativeUrl = $"/api/data/v9.2/{_scenariosTableSetName}";
            await CallDataverseSendAsync(relativeUrl, "POST", payload, httpContext.User, ct);
            return;
        }

        var updateUrl = $"/api/data/v9.2/{_scenariosTableSetName}({recordId})";
        await CallDataverseSendAsync(updateUrl, "PATCH", payload, httpContext.User, ct);
    }
 public async Task DeleteScenarioAsync(string scenarioId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
            throw new ArgumentException("ScenarioId requerido.", nameof(scenarioId));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var currentUser = await GetCurrentUserAsync(ct);
        if (currentUser is null || string.IsNullOrWhiteSpace(currentUser.SystemUserId))
            throw new InvalidOperationException("Usuario actual no disponible.");

        var recordId = await FindScenarioRecordIdAsync(scenarioId, currentUser.SystemUserId, httpContext.User, ct);
        if (string.IsNullOrWhiteSpace(recordId))
            return;

        var deleteUrl = $"/api/data/v9.2/{_scenariosTableSetName}({recordId})";
        await CallDataverseDeleteAsync(deleteUrl, httpContext.User, ct);
    }

    public async Task<IReadOnlyList<ProductLookupItem>> SearchProductsAsync(string query, int top = 12, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        query = (query ?? "").Trim();
        if (query.Length < 2)
            return Array.Empty<ProductLookupItem>();

        var safeQuery = EscapeOdataLiteral(query);
        var select = BuildProductSelectClause();
        var filter = $"contains({ProductsDescriptionField},'{safeQuery}')";
        var relativeUrl = $"/api/data/v9.2/{ProductsEntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top={top}";

        var json = await CallDataverseGetJsonAsync(relativeUrl, httpContext.User, ct);

        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("value");

        var list = new List<ProductLookupItem>(Math.Min(arr.GetArrayLength(), top));
        foreach (var item in arr.EnumerateArray())
        {
            list.Add(ToProductLookupItem(item));
        }

        return list;
    }

    public async Task<ProductLookupItem> EnsureCalculatorProductAsync(ProductCreateInput input, CancellationToken ct = default)
    {
        if (input is null)
            throw new ArgumentNullException(nameof(input));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var description = (input.Description ?? "").Trim();
        if (description.Length < 2)
            throw new InvalidOperationException("El producto de Hardware debe tener un nombre valido.");

        var existing = await FindProductByExactDescriptionAsync(description, httpContext.User, ct);
        if (existing is not null)
            return existing;

        var primaryNameField = await ResolveEntityPrimaryNameFieldAsync(
            ProductsEntityLogicalName,
            ProductsDescriptionField,
            httpContext.User,
            ct);

        var payload = new Dictionary<string, object?>
        {
            [ProductsDescriptionField] = description,
            [ProductsPurchasePriceField] = RoundCurrency(Math.Max(input.PurchasePrice, 0m)),
            [ProductsSuggestedRetailPriceField] = RoundCurrency(Math.Max(input.SuggestedRetailPrice, 0m)),
            [ProductsAceleradorField] = RoundCurrency(Math.Max(input.Acelerador, 0m))
        };

        if (!payload.ContainsKey(primaryNameField))
            payload[primaryNameField] = description;

        var relativeUrl = $"/api/data/v9.2/{ProductsEntitySetName}?$select={BuildProductSelectClause()}";
        using var response = await SendDataversePayloadWithRepresentationAsync(
            relativeUrl,
            "POST",
            payload,
            httpContext.User,
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!string.IsNullOrWhiteSpace(body))
        {
            using var doc = JsonDocument.Parse(body);
            var created = ToProductLookupItem(doc.RootElement);
            if (!string.IsNullOrWhiteSpace(created.Id))
                return created;
        }

        var createdId = ExtractRhRecordId(response, body, ProductsIdField);
        if (!string.IsNullOrWhiteSpace(createdId))
        {
            return new ProductLookupItem
            {
                Id = createdId,
                Description = description,
                PurchasePrice = RoundCurrency(Math.Max(input.PurchasePrice, 0m)),
                SuggestedRetailPrice = RoundCurrency(Math.Max(input.SuggestedRetailPrice, 0m)),
                Acelerador = RoundCurrency(Math.Max(input.Acelerador, 0m))
            };
        }

        throw new InvalidOperationException("Dataverse creo el producto de Hardware, pero no devolvio el identificador.");
    }

    private async Task<ProductLookupItem?> FindProductByExactDescriptionAsync(
        string description,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var filter = $"{ProductsDescriptionField} eq '{EscapeOdataLiteral(description)}'";
        var relativeUrl = $"/api/data/v9.2/{ProductsEntitySetName}?$select={BuildProductSelectClause()}&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);

        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("value");
        if (value.GetArrayLength() == 0)
            return null;

        var item = ToProductLookupItem(value[0]);
        return string.IsNullOrWhiteSpace(item.Id) ? null : item;
    }

    private async Task<ProductLookupItem?> GetCalculatorProductByIdAsync(
        string productId,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!Guid.TryParse(productId, out var parsedId))
            return null;

        var filter = $"{ProductsIdField} eq {parsedId:D}";
        var relativeUrl = $"/api/data/v9.2/{ProductsEntitySetName}?$select={BuildProductSelectClause()}&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);

        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("value");
        if (value.GetArrayLength() == 0)
            return null;

        var item = ToProductLookupItem(value[0]);
        return string.IsNullOrWhiteSpace(item.Id) ? null : item;
    }

    private static string BuildProductSelectClause() =>
        string.Join(",", new[] 
        {
            ProductsDescriptionField,
            ProductsPurchasePriceField,
            ProductsSuggestedRetailPriceField,
            ProductsAceleradorField,
            ProductsIdField
        });

    private static ProductLookupItem ToProductLookupItem(JsonElement item) =>
        new()
        {
            Id = item.TryGetProperty(ProductsIdField, out var idProp) ? (idProp.GetString() ?? "") : "",
            Description = item.TryGetProperty(ProductsDescriptionField, out var descriptionProp) ? (descriptionProp.GetString() ?? "") : "",
            PurchasePrice = ReadDecimal(item, ProductsPurchasePriceField),
            SuggestedRetailPrice = ReadDecimal(item, ProductsSuggestedRetailPriceField),
            Acelerador = ReadDecimal(item, ProductsAceleradorField)
        };

    public async Task<IReadOnlyList<ClientLookupItem>> SearchClientsAsync(string query, int top = 12, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        query = (query ?? "").Trim();
        top = Math.Clamp(top, 1, 5000);

        var safeQuery = query.Replace("'", "''");
        var select = "cr07a_clienteid,cr07a_nombre";
        var filter = string.IsNullOrWhiteSpace(safeQuery)
            ? ""
            : $"&$filter={Uri.EscapeDataString($"contains(cr07a_nombre,'{safeQuery}')")}";
        var relativeUrl = $"/api/data/v9.2/cr07a_clientes?$select={select}{filter}&$orderby=cr07a_nombre asc&$top={top}";

        var json = await CallDataverseGetJsonAsync(relativeUrl, httpContext.User, ct);

        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("value");

        var list = new List<ClientLookupItem>(Math.Min(arr.GetArrayLength(), top));
        foreach (var item in arr.EnumerateArray())
        {
            list.Add(new ClientLookupItem
            {
                Id = item.TryGetProperty("cr07a_clienteid", out var idProp) ? (idProp.GetString() ?? "") : "",
                Name = item.TryGetProperty("cr07a_nombre", out var nameProp) ? (nameProp.GetString() ?? "") : ""
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<SystemUserLookupItem>> SearchSystemUsersAsync(
        string query,
        int top = 12,
        CancellationToken ct = default,
        bool includeAllWhenEmpty = false)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        query = (query ?? "").Trim();
        if (query.Length < 2 && !includeAllWhenEmpty)
            return Array.Empty<SystemUserLookupItem>();

        top = Math.Clamp(top, 1, 500);
        var filter = "isdisabled eq false";
        if (query.Length >= 2)
        {
            var safeQuery = EscapeOdataLiteral(query);
            filter += $" and (contains(fullname,'{safeQuery}') or contains(internalemailaddress,'{safeQuery}'))";
        }

        const string select = "systemuserid,fullname,internalemailaddress";
        var relativeUrl =
            $"/api/data/v9.2/systemusers?$select={select}" +
            $"&$filter={Uri.EscapeDataString(filter)}&$orderby=fullname asc&$top={top}";

        var json = await CallDataverseGetJsonAsync(relativeUrl, httpContext.User, ct);

        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("value");
        var list = new List<SystemUserLookupItem>(Math.Min(arr.GetArrayLength(), top));
        foreach (var item in arr.EnumerateArray())
        {
            var id = ReadString(item, "systemuserid");
            var name = ReadString(item, "fullname");
            var email = ReadString(item, "internalemailaddress");
            if (string.IsNullOrWhiteSpace(id))
                continue;

            list.Add(new SystemUserLookupItem
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(email) ? name : $"{name} ({email})",
                Email = email
            });
        }

        return list;
    }

    public async Task<SystemUserLookupItem?> GetSystemUserAsync(string systemUserId, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var normalizedUserId = NormalizeGuid(systemUserId, nameof(systemUserId));
        const string select = "systemuserid,fullname,internalemailaddress";
        var filter = $"systemuserid eq {normalizedUserId}";
        var relativeUrl =
            $"/api/data/v9.2/systemusers?$select={select}" +
            $"&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var json = await CallDataverseGetJsonAsync(relativeUrl, httpContext.User, ct);

        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("value");
        if (value.GetArrayLength() == 0)
            return null;

        var root = value[0];
        var id = ReadString(root, "systemuserid");
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var name = ReadString(root, "fullname");
        var email = ReadString(root, "internalemailaddress");
        return new SystemUserLookupItem
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(email) ? name : $"{name} ({email})",
            Email = email
        };
    }

    public async Task<IReadOnlyList<RenewalDateLookupItem>> SearchRenewalDatesByClientAsync(string clientId, int top = 250, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        if (!Guid.TryParse(clientId, out var clientGuid))
        {
            _logger.LogWarning("SearchRenewalDatesByClientAsync recibio un clientId invalido: {ClientId}", clientId);
            return Array.Empty<RenewalDateLookupItem>();
        }

        top = Math.Clamp(top, 1, 5000);
        var fallbackLookupFields = new[]
        {
            _salesPerformanceClientLookupFilterField,
            DefaultSalesPerformanceClientLookupFilterField,
            "_cr07a_clientelookup_value"
        }
        .Where(field => !string.IsNullOrWhiteSpace(field))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        List<RenewalDateLookupItem>? emptySuccessfulResult = null;
        Exception? lastError = null;

        foreach (var lookupField in fallbackLookupFields)
        {
            try
            {
                var results = await QueryRenewalDatesByClientAsync(clientGuid, lookupField, httpContext.User, top, ct);
                if (results.Count > 0)
                    return results;

                emptySuccessfulResult ??= results;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Fallo la consulta de fechas de renovacion para cliente {ClientId} usando lookup {LookupField}.",
                    clientId,
                    lookupField);
                lastError = ex;
            }
        }

        var scanResults = await ScanRenewalDatesByClientGuidAsync(clientGuid, httpContext.User, Math.Max(top, 1000), ct);
        if (scanResults.Count > 0)
            return scanResults;

        if (emptySuccessfulResult is not null)
        {
            _logger.LogInformation(
                "No se encontraron fechas de renovacion disponibles para cliente {ClientId}.",
                clientId);
            return emptySuccessfulResult;
        }

        throw new InvalidOperationException(
            "No se pudo consultar cr07a_salesperformancerecord para obtener fechas de renovacion del cliente seleccionado.",
            lastError);
    }

    public async Task<RenewalBoardDto> GetRenewalBoardAsync(RenewalPeriodFilter filter, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var filterParts = new List<string>
        {
            $"{_salesPerformanceRenewalDateField} ne null"
        };

        var periodFilter = BuildRenewalPeriodFilter(filter);
        if (!string.IsNullOrWhiteSpace(periodFilter))
        {
            filterParts.Add(periodFilter);
        }

        var relativeUrl = $"/api/data/v9.2/{_salesPerformanceTableSetName}?$filter={Uri.EscapeDataString(string.Join(" and ", filterParts))}&$orderby={_salesPerformanceRenewalDateField} asc";
        var rawRecords = await GetDataverseEntitiesAsync(relativeUrl, httpContext.User, ct, AddFormattedValueHeaders);

        var parsedRecords = rawRecords
            .Select(ParseRenewalRecord)
            .Where(item => item is not null)
            .Cast<RenewalRecordDto>()
            .ToList();

        var records = filter == RenewalPeriodFilter.AllPast
            ? parsedRecords
                .OrderByDescending(item => item.RenewalDateValue, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ProductName, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : parsedRecords
                .OrderBy(item => item.RenewalDateValue, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ClientName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ProductName, StringComparer.OrdinalIgnoreCase)
                .ToList();

        var groups = records
            .GroupBy(GetRenewalClientGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var orderedRecords = filter == RenewalPeriodFilter.AllPast
                    ? group
                        .OrderByDescending(item => item.RenewalDateValue, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(item => item.ProductName, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    : group
                        .OrderBy(item => item.RenewalDateValue, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(item => item.ProductName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                var first = orderedRecords[0];
                return new RenewalClientGroupDto
                {
                    ClientId = first.ClientId,
                    ClientName = first.ClientName,
                    RecordCount = orderedRecords.Count,
                    ContractValue = RoundCurrency(orderedRecords.Sum(item => item.ContractValue)),
                    Records = orderedRecords
                };
            })
            .ToList();

        groups = filter == RenewalPeriodFilter.AllPast
            ? groups
                .OrderByDescending(group => group.Records[0].RenewalDateValue, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.ClientName, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : groups
                .OrderBy(group => group.Records[0].RenewalDateValue, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.ClientName, StringComparer.OrdinalIgnoreCase)
                .ToList();

        return new RenewalBoardDto
        {
            Filter = filter.ToKey(),
            FilterLabel = filter.ToLabel(),
            ClientsCount = groups.Count,
            RecordsCount = records.Count,
            TotalContractValue = RoundCurrency(groups.Sum(group => group.ContractValue)),
            Groups = groups
        };
    }

    public async Task<int> UpdateRenewalRecordsAsync(IReadOnlyList<RenewalRecordUpdateItem> items, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        if (items is null)
            throw new ArgumentNullException(nameof(items));

        if (items.Count == 0)
            return 0;

        foreach (var item in items)
        {
            if (item is null)
                continue;

            var recordId = NormalizeGuid(item.RecordId, nameof(item.RecordId));
            var clientId = NormalizeGuid(item.ClientId, nameof(item.ClientId));
            var productId = NormalizeGuid(item.ProductId, nameof(item.ProductId));
            var originalClientId = NormalizeOptionalGuid(item.OriginalClientId);
            var originalProductId = NormalizeOptionalGuid(item.OriginalProductId);

            if (item.Quantity <= 0)
                throw new InvalidOperationException("La cantidad debe ser mayor a cero.");

            if (item.UnitSaleUsd < 0m)
                throw new InvalidOperationException("El valor venta unidad USD no puede ser negativo.");

            if (!TryParseDateOnly(item.RenewalDateValue, out var renewalDate))
                throw new InvalidOperationException("La fecha de renovacion no es valida.");

            var shouldUpdateClientLookup = !string.Equals(clientId, originalClientId, StringComparison.OrdinalIgnoreCase);
            var shouldUpdateProductLookup = !string.Equals(productId, originalProductId, StringComparison.OrdinalIgnoreCase);

            var basePayload = new Dictionary<string, object?>
            {
                [DefaultSalesPerformanceQuantityField] = item.Quantity,
                [DefaultSalesPerformanceUnitSaleUsdField] = item.UnitSaleUsd,
                [_salesPerformanceRenewalDateField] = renewalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            };

            var updateUrl = $"/api/data/v9.2/{_salesPerformanceTableSetName}({recordId})";
            if (!shouldUpdateClientLookup && !shouldUpdateProductLookup)
            {
                await CallDataverseSendAsync(updateUrl, "PATCH", basePayload, httpContext.User, ct);
                continue;
            }

            var clientLookupCandidates = shouldUpdateClientLookup
                ? await ResolveSalesPerformanceNavigationPropertyCandidatesAsync(
                    item.ClientLookupLogicalName,
                    BuildLookupLogicalNameCandidates(
                        item.ClientLookupLogicalName,
                        DeriveLookupLogicalName(_salesPerformanceClientLookupFilterField),
                        DefaultSalesPerformanceClientLookupLogicalName,
                        "cr07a_clientelookup",
                        "cr07a_cliente"),
                    httpContext.User,
                    ct)
                : new List<string?> { null };

            var productLookupCandidates = shouldUpdateProductLookup
                ? await ResolveSalesPerformanceNavigationPropertyCandidatesAsync(
                    item.ProductLookupLogicalName,
                    BuildLookupLogicalNameCandidates(
                        item.ProductLookupLogicalName,
                        DefaultSalesPerformanceProductLookupLogicalName,
                        "cr07a_producto"),
                    httpContext.User,
                    ct)
                : new List<string?> { null };

            Exception? lastError = null;
            var updated = false;
            foreach (var clientLookupLogicalName in clientLookupCandidates)
            {
                foreach (var productLookupLogicalName in productLookupCandidates)
                {
                    var payload = new Dictionary<string, object?>(basePayload);
                    if (!string.IsNullOrWhiteSpace(clientLookupLogicalName))
                    {
                        payload[$"{clientLookupLogicalName}@odata.bind"] = $"/{ClientsEntitySetName}({clientId})";
                    }

                    if (!string.IsNullOrWhiteSpace(productLookupLogicalName))
                    {
                        payload[$"{productLookupLogicalName}@odata.bind"] = $"/{ProductsEntitySetName}({productId})";
                    }

                    try
                    {
                        await CallDataverseSendAsync(updateUrl, "PATCH", payload, httpContext.User, ct);
                        updated = true;
                        break;
                    }
                    catch (InvalidOperationException ex)
                    {
                        lastError = ex;
                    }
                }

                if (updated)
                    break;
            }

            if (!updated)
                throw new InvalidOperationException("No se pudo actualizar la renovacion seleccionada en Dataverse.", lastError);
        }

        return items.Count;
    }

    public async Task<RenewalScenarioCreateResultDto> CreateRenewalScenarioAsync(
        IReadOnlyList<RenewalRecordUpdateItem> items,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        if (items is null)
            throw new ArgumentNullException(nameof(items));

        var selectedItems = items
            .Where(item => item is not null)
            .ToList();

        if (selectedItems.Count == 0)
            throw new InvalidOperationException("Debes seleccionar al menos una linea para crear el escenario.");

        var warnings = new List<string>();
        var productCache = new Dictionary<string, ProductLookupItem?>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<ScenarioLineInput>(selectedItems.Count);

        foreach (var item in selectedItems)
        {
            var productId = NormalizeGuid(item.ProductId, nameof(item.ProductId));
            if (!productCache.TryGetValue(productId, out var product))
            {
                product = await GetCalculatorProductByIdAsync(productId, httpContext.User, ct);
                productCache[productId] = product;
            }

            var productName = FirstNonEmpty(product?.Description, item.ProductName, productId);
            var costUnit = RoundCurrency(Math.Max(product?.PurchasePrice ?? 0m, 0m));
            var saleUnit = RoundCurrency(Math.Max(item.UnitSaleUsd, 0m));

            if (product is null)
            {
                warnings.Add($"No se pudo leer el producto {productName} para traer costo y acelerador.");
            }
            else if (costUnit <= 0m && saleUnit > 0m)
            {
                warnings.Add($"El producto {productName} no tiene costo configurado; revisa el margen en la calculadora.");
            }

            lines.Add(new ScenarioLineInput
            {
                BusinessType = (int)ResolveRenewalBusinessType(item),
                ProductId = productId,
                ProductDescription = productName,
                CostUnit = costUnit,
                MarginPercent = CalculateMarginPercentForSale(saleUnit, costUnit),
                ContractMonths = 12,
                Quantity = Math.Max(item.Quantity, 1),
                SuggestedRetailPrice = RoundCurrency(Math.Max(product?.SuggestedRetailPrice ?? 0m, 0m)),
                Acelerador = RoundCurrency(Math.Max(product?.Acelerador ?? 0m, 0m)),
                HasVat = item.HasVat
            });
        }

        var now = DateTimeOffset.UtcNow;
        var scenarioId = $"renovacion-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}".Substring(0, 38);
        var clientName = ResolveMostCommonValue(selectedItems.Select(item => item.ClientName), "Cliente");
        var scenarioName = $"Renovacion {clientName} {now:yyyyMMdd HHmm}";

        await UpsertScenarioAsync(new ScenarioSaveRequest
        {
            ScenarioId = scenarioId,
            ScenarioName = scenarioName,
            DealType = (int)DealType.Renovacion1,
            RequiresProration = false,
            Lines = lines,
            LastResult = null
        }, ct);

        return new RenewalScenarioCreateResultDto
        {
            ScenarioId = scenarioId,
            ScenarioName = scenarioName,
            Warnings = warnings
        };
    }

    public async Task<IReadOnlyList<SupplierProviderLookupItem>> GetSupplierCertificateProvidersAsync(
        DateOnly startDate,
        DateOnly endDate,
        string? searchTerm = null,
        CancellationToken ct = default)
    {
        var rows = await GetSupplierExpenseRowsAsync(startDate, endDate, ct);
        IEnumerable<SupplierProviderLookupItem> providers = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.SupplierName) || !string.IsNullOrWhiteSpace(row.SupplierNit))
            .GroupBy(GetSupplierProviderGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var representative = group
                    .OrderByDescending(item => !string.IsNullOrWhiteSpace(item.SupplierName))
                    .ThenByDescending(item => !string.IsNullOrWhiteSpace(item.SupplierNit))
                    .ThenBy(item => item.SupplierName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.SupplierNit, StringComparer.OrdinalIgnoreCase)
                    .First();

                return new SupplierProviderLookupItem
                {
                    Nit = representative.SupplierNit,
                    Name = representative.SupplierName
                };
            });

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            providers = providers.Where(item =>
                item.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || item.Nit.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return providers
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Nit, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SupplierCertificateSummaryDto> GetSupplierCertificateSummaryAsync(
        SupplierCertificateQuery query,
        CancellationToken ct = default)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));

        if (query.EndDate < query.StartDate)
            throw new InvalidOperationException("La fecha final no puede ser menor que la inicial.");

        if (query.CertificateTypes is null || query.CertificateTypes.Count == 0)
            throw new InvalidOperationException("Debes seleccionar al menos un tipo de certificado.");

        var rows = await GetSupplierExpenseRowsAsync(query.StartDate, query.EndDate, ct);
        var supplierNitKey = NormalizeSupplierTaxId(query.SupplierNit);
        var filteredRows = rows
            .Where(row => string.Equals(NormalizeSupplierTaxId(row.SupplierNit), supplierNitKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.ExpenseDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.TotalInvoices)
            .ToList();

        if (filteredRows.Count == 0 && !string.IsNullOrWhiteSpace(query.SupplierName))
        {
            filteredRows = rows
                .Where(row => string.Equals(row.SupplierName, query.SupplierName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(row => row.ExpenseDateValue, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.TotalInvoices)
                .ToList();
        }

        var certificateTypes = query.CertificateTypes
            .Distinct()
            .ToList();

        var supplierName = ResolveMostCommonValue(
            filteredRows.Select(row => row.SupplierName),
            query.SupplierName);
        var supplierNit = ResolveMostCommonValue(
            filteredRows.Select(row => row.SupplierNit),
            query.SupplierNit);

        return new SupplierCertificateSummaryDto
        {
            SupplierName = supplierName,
            SupplierNit = supplierNit,
            PeriodStartValue = query.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PeriodEndValue = query.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PeriodLabel = $"{query.StartDate:dd/MM/yyyy} al {query.EndDate:dd/MM/yyyy}",
            CertificateTypes = certificateTypes,
            CertificateTypesLabel = certificateTypes.ToSummaryLabel(),
            RecordsCount = filteredRows.Count,
            TotalInvoices = RoundCurrency(filteredRows.Sum(row => row.TotalInvoices)),
            TotalBase = RoundCurrency(filteredRows.Sum(row => row.TotalBase)),
            TotalReteFuente = certificateTypes.Contains(SupplierCertificateType.ReteFuente)
                ? RoundCurrency(filteredRows.Sum(row => row.TotalReteFuente))
                : 0m,
            TotalReteIca = certificateTypes.Contains(SupplierCertificateType.ReteIca)
                ? RoundCurrency(filteredRows.Sum(row => row.TotalReteIca))
                : 0m,
            Records = filteredRows
        };
    }

    public async Task<CurrentUserInfo?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        if (httpContext.Items.TryGetValue(CurrentUserCacheKey, out var cachedUser)
            && cachedUser is CurrentUserInfo currentUserInfo)
        {
            return currentUserInfo;
        }

        var currentUser = BuildCurrentUserInfoFromClaims(httpContext.User);

        JsonElement? userRecord = null;
        try
        {
            userRecord = await GetCurrentUserRecordAsync(httpContext.User, ct);
        }
        catch (Exception ex)
        {
            if (IsIncrementalConsentChallenge(ex))
                throw;

            AppendCurrentUserPermissionWarning(
                currentUser,
                "No fue posible consultar el system user actual en Dataverse.",
                ex);
        }

        if (userRecord is not null)
        {
            currentUser.SystemUserId = userRecord.Value.TryGetProperty("systemuserid", out var idProp) ? (idProp.GetString() ?? "") : "";
            currentUser.DisplayName = FirstNonEmpty(
                userRecord.Value.TryGetProperty("fullname", out var nameProp) ? nameProp.GetString() : null,
                currentUser.DisplayName);
            currentUser.Email = FirstNonEmpty(
                userRecord.Value.TryGetProperty("internalemailaddress", out var emailProp) ? emailProp.GetString() : null,
                currentUser.Email);
        }

        JsonElement? employeeRecord = null;
        if (!string.IsNullOrWhiteSpace(currentUser.SystemUserId))
        {
            try
            {
                employeeRecord = await GetCurrentEmployeeRecordAsync(currentUser.SystemUserId, httpContext.User, ct);
            }
            catch (Exception ex)
            {
                if (IsIncrementalConsentChallenge(ex))
                    throw;

                AppendCurrentUserPermissionWarning(
                    currentUser,
                    "No fue posible cargar el empleado actual en Dataverse usando el lookup de usuario.",
                    ex);
            }
        }

        if (employeeRecord is null && !string.IsNullOrWhiteSpace(currentUser.Email))
        {
            try
            {
                employeeRecord = await GetCurrentEmployeeRecordByEmailAsync(currentUser.Email, httpContext.User, ct);
            }
            catch (Exception ex)
            {
                if (IsIncrementalConsentChallenge(ex))
                    throw;

                AppendCurrentUserPermissionWarning(
                    currentUser,
                    "No fue posible cargar el empleado actual en Dataverse usando el correo.",
                    ex);
            }
        }

        if (employeeRecord is not null)
        {
            currentUser.EmployeeId = ReadString(employeeRecord.Value, _nominaEmployeeIdField);
            currentUser.EmployeeName = FirstNonEmpty(
                ReadString(employeeRecord.Value, EmployeeFullNameField),
                ReadString(employeeRecord.Value, _nominaEmployeeNameField));
            currentUser.EmployeeUserDisplayName = ReadString(employeeRecord.Value, $"{EmployeeUserLookupField}{FormattedValueAnnotationSuffix}");
            currentUser.EmployeeUserEmail = FirstNonEmpty(
                ReadString(employeeRecord.Value, EmployeeEmailField),
                currentUser.Email);
            currentUser.ModuleOptionValues = ReadMultiSelectOptionValues(employeeRecord.Value, EmployeeModulesField);
        }

        httpContext.Items[CurrentUserCacheKey] = currentUser;
        return currentUser;
    }

    private CurrentUserInfo BuildCurrentUserInfoFromClaims(ClaimsPrincipal user)
    {
        var givenName = user.FindFirstValue(ClaimTypes.GivenName);
        var surname = user.FindFirstValue(ClaimTypes.Surname);
        var fullName = string.Join(" ", new[] { givenName, surname }.Where(static part => !string.IsNullOrWhiteSpace(part)));

        return new CurrentUserInfo
        {
            DisplayName = FirstNonEmpty(
                user.FindFirstValue("name"),
                user.FindFirstValue(ClaimTypes.Name),
                string.IsNullOrWhiteSpace(fullName) ? null : fullName,
                user.GetDisplayName(),
                user.Identity?.Name),
            Email = FirstNonEmpty(
                user.FindFirstValue("preferred_username"),
                user.FindFirstValue(ClaimTypes.Upn),
                user.FindFirstValue(ClaimTypes.Email),
                user.Identity?.Name)
        };
    }

    private void AppendCurrentUserPermissionWarning(CurrentUserInfo currentUser, string context, Exception ex)
    {
        _logger.LogError(ex, "{Context}", context);

        var detail = $"{context} {SummarizeException(ex)}".Trim();
        if (string.IsNullOrWhiteSpace(currentUser.PermissionLoadWarning))
        {
            currentUser.PermissionLoadWarning = detail;
            return;
        }

        if (!currentUser.PermissionLoadWarning.Contains(detail, StringComparison.OrdinalIgnoreCase))
            currentUser.PermissionLoadWarning = $"{currentUser.PermissionLoadWarning}{Environment.NewLine}{Environment.NewLine}{detail}";
    }

    private static string SummarizeException(Exception ex)
    {
        if (ex is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
            ex = aggregate.InnerExceptions[0];

        return string.IsNullOrWhiteSpace(ex.Message)
            ? ex.GetType().Name
            : ex.Message.Trim();
    }

    private static bool IsIncrementalConsentChallenge(Exception ex)
    {
        if (ex is null)
            return false;

        if (ex is MicrosoftIdentityWebChallengeUserException or MsalUiRequiredException)
            return true;

        if (ex is AggregateException aggregate)
            return aggregate.InnerExceptions.Any(IsIncrementalConsentChallenge);

        return ex.InnerException is not null && IsIncrementalConsentChallenge(ex.InnerException);
    }

    private async Task<JsonElement?> GetCurrentUserRecordAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct)
    {
        var objectId = user.GetObjectId();
        if (string.IsNullOrWhiteSpace(objectId))
            return null;

        var select = "systemuserid,fullname,internalemailaddress";
        var filter = $"azureactivedirectoryobjectid eq {Guid.Parse(objectId):D}";
        var relativeUrl = $"/api/data/v9.2/systemusers?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=1";

        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);

        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("value");

        if (value.GetArrayLength() == 0)
            return null;

        return value[0].Clone();
    }

    private string BuildRenewalPeriodFilter(RenewalPeriodFilter filter)
    {
        if (filter == RenewalPeriodFilter.All)
            return "";

        if (filter == RenewalPeriodFilter.AllPast)
        {
            var currentDate = GetBogotaToday();
            return $"{_salesPerformanceRenewalDateField} lt {currentDate:yyyy-MM-dd}";
        }

        var today = GetBogotaToday();
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var targetMonthStart = filter switch
        {
            RenewalPeriodFilter.PreviousMonth => monthStart.AddMonths(-1),
            RenewalPeriodFilter.NextMonth => monthStart.AddMonths(1),
            _ => monthStart
        };

        var nextMonthStart = targetMonthStart.AddMonths(1);
        return $"{_salesPerformanceRenewalDateField} ge {targetMonthStart:yyyy-MM-dd} and {_salesPerformanceRenewalDateField} lt {nextMonthStart:yyyy-MM-dd}";
    }

    private static DateOnly GetBogotaToday()
    {
        var utcNow = DateTimeOffset.UtcNow;
        foreach (var timeZoneId in new[] { "SA Pacific Standard Time", "America/Bogota" })
        {
            try
            {
                var timezone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, timezone).DateTime);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return DateOnly.FromDateTime(utcNow.UtcDateTime);
    }

    private async Task<List<SupplierCertificateRecordDto>> GetSupplierExpenseRowsAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var select = string.Join(",", new[]
        {
            _supplierExpensesIdField,
            _supplierExpensesDateField,
            "cr07a_nombreemisor",
            "cr07a_nitemisor",
            "cr07a_total",
            "cr07a_totalantesdeiva",
            "cr07a_retefuente",
            "cr07a_reteica"
        });
        var filter = BuildSupplierExpenseDateFilter(startDate, endDate);
        var relativeUrl = $"/api/data/v9.2/{_supplierExpensesTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={_supplierExpensesDateField} asc";
        var rawRecords = await GetDataverseEntitiesAsync(relativeUrl, httpContext.User, ct);

        return rawRecords
            .Select(ParseSupplierExpenseRecord)
            .Where(item => item is not null)
            .Cast<SupplierCertificateRecordDto>()
            .ToList();
    }

    private string BuildSupplierExpenseDateFilter(DateOnly startDate, DateOnly endDate)
    {
        var endExclusive = endDate.AddDays(1);
        if (string.Equals(_supplierExpensesDateFieldKind, "date-only", StringComparison.OrdinalIgnoreCase))
        {
            return $"{_supplierExpensesDateField} ge {startDate:yyyy-MM-dd} and {_supplierExpensesDateField} lt {endExclusive:yyyy-MM-dd}";
        }

        var startDateTime = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var endDateTime = new DateTimeOffset(endExclusive.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return $"{_supplierExpensesDateField} ge {startDateTime:yyyy-MM-ddTHH:mm:ssZ} and {_supplierExpensesDateField} lt {endDateTime:yyyy-MM-ddTHH:mm:ssZ}";
    }

    private SupplierCertificateRecordDto? ParseSupplierExpenseRecord(JsonElement item)
    {
        var expenseDate = ReadDateOnly(item, _supplierExpensesDateField);
        var expenseDateValue = expenseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ?? ReadString(item, _supplierExpensesDateField);
        var supplierName = ReadString(item, "cr07a_nombreemisor").Trim();
        var supplierNit = ReadString(item, "cr07a_nitemisor").Trim();
        var recordId = ReadString(item, _supplierExpensesIdField);

        if (string.IsNullOrWhiteSpace(recordId))
        {
            recordId = $"{supplierNit}|{supplierName}|{expenseDateValue}";
        }

        return new SupplierCertificateRecordDto
        {
            RecordId = recordId,
            SupplierName = supplierName,
            SupplierNit = supplierNit,
            ExpenseDateValue = expenseDateValue,
            ExpenseDateDisplay = expenseDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? expenseDateValue,
            TotalInvoices = RoundCurrency(ReadDecimal(item, "cr07a_total") ?? 0m),
            TotalBase = RoundCurrency(ReadDecimal(item, "cr07a_totalantesdeiva") ?? 0m),
            TotalReteFuente = RoundCurrency(ReadDecimal(item, "cr07a_retefuente") ?? 0m),
            TotalReteIca = RoundCurrency(ReadDecimal(item, "cr07a_reteica") ?? 0m)
        };
    }

    private static string GetSupplierProviderGroupKey(SupplierCertificateRecordDto item)
    {
        var nitKey = NormalizeSupplierTaxId(item.SupplierNit);
        if (!string.IsNullOrWhiteSpace(nitKey))
            return $"nit:{nitKey}";

        return $"name:{item.SupplierName.Trim().ToLowerInvariant()}";
    }

    private static string ResolveMostCommonValue(IEnumerable<string> values, string? fallback)
    {
        var resolved = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .FirstOrDefault();

        return resolved ?? (fallback?.Trim() ?? "");
    }

    private static string NormalizeSupplierTaxId(string? nit)
    {
        if (string.IsNullOrWhiteSpace(nit))
            return "";

        return new string(nit
            .Trim()
            .Where(char.IsLetterOrDigit)
            .ToArray())
            .ToUpperInvariant();
    }

    private async Task<List<JsonElement>> GetDataverseEntitiesAsync(
        string relativeUrl,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct,
        Action<HttpRequestMessage>? customizeRequest = null)
    {
        const int maxPages = 50;
        var pageCount = 0;
        var items = new List<JsonElement>();
        string? nextRelativeUrl = relativeUrl;

        while (!string.IsNullOrWhiteSpace(nextRelativeUrl))
        {
            pageCount++;
            if (pageCount > maxPages)
                throw new InvalidOperationException("Se alcanzo el limite de paginas consultando registros de Dataverse.");

            var json = await CallDataverseGetJsonAsync(nextRelativeUrl, user, ct, customizeRequest);
            using var doc = JsonDocument.Parse(json);
            var value = doc.RootElement.GetProperty("value");
            foreach (var item in value.EnumerateArray())
            {
                items.Add(item.Clone());
            }

            nextRelativeUrl = doc.RootElement.TryGetProperty("@odata.nextLink", out var nextLinkProp)
                ? GetRelativeDataverseUrl(nextLinkProp.GetString())
                : null;
        }

        return items;
    }

    private static string? GetRelativeDataverseUrl(string? nextLink)
    {
        if (string.IsNullOrWhiteSpace(nextLink))
            return null;

        if (Uri.TryCreate(nextLink, UriKind.Absolute, out var absoluteUri))
            return $"{absoluteUri.AbsolutePath}{absoluteUri.Query}";

        return nextLink;
    }

    private static void AddFormattedValueHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("Prefer", $"odata.include-annotations=\"{FormattedValueAnnotationSuffix.TrimStart('@')}\"");
    }

    private RenewalRecordDto? ParseRenewalRecord(JsonElement item)
    {
        var recordId = ReadString(item, _salesPerformanceIdField);
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var renewalDate = ReadDateOnly(item, _salesPerformanceRenewalDateField);
        if (!renewalDate.HasValue)
            return null;

        var clientLookupProperty = DetectLookupValueProperty(item, SalesPerformanceClientLookupFieldCandidates, "cliente");
        var productLookupProperty = DetectLookupValueProperty(item, SalesPerformanceProductLookupFieldCandidates, "producto");

        var clientId = ReadString(item, clientLookupProperty);
        var productId = ReadString(item, productLookupProperty);
        var clientName = ReadLookupFormattedValue(item, clientLookupProperty);
        var productName = ReadLookupFormattedValue(item, productLookupProperty);
        var quantity = ReadIntFlexible(item, DefaultSalesPerformanceQuantityField);
        var unitSaleUsd = ReadDecimal(item, DefaultSalesPerformanceUnitSaleUsdField) ?? 0m;
        var productLineOptionValue = ReadOptionValue(item, _salesPerformanceProductLineField);
        var businessType = ResolveRenewalBusinessType(productLineOptionValue);
        var hasVat = ReadYesNoOptionFlexible(item, _salesPerformanceHasVatField);

        clientName = string.IsNullOrWhiteSpace(clientName) ? "Cliente sin asignar" : clientName.Trim();
        productName = string.IsNullOrWhiteSpace(productName) ? "Producto sin asignar" : productName.Trim();

        return new RenewalRecordDto
        {
            RecordId = recordId,
            ClientId = clientId,
            ClientName = clientName,
            ProductId = productId,
            ProductName = productName,
            ProductLineOptionValue = productLineOptionValue,
            BusinessType = (int)businessType,
            Quantity = quantity,
            UnitSaleUsd = RoundCurrency(unitSaleUsd),
            HasVat = hasVat,
            RenewalDateValue = renewalDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            RenewalDateDisplay = renewalDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ContractValue = RoundCurrency(quantity * unitSaleUsd * 12m),
            ClientLookupLogicalName = ResolveLookupLogicalName(
                DeriveLookupLogicalName(clientLookupProperty),
                DeriveLookupLogicalName(_salesPerformanceClientLookupFilterField),
                DefaultSalesPerformanceClientLookupLogicalName),
            ProductLookupLogicalName = ResolveLookupLogicalName(
                DeriveLookupLogicalName(productLookupProperty),
                DefaultSalesPerformanceProductLookupLogicalName,
                DefaultSalesPerformanceProductLookupLogicalName)
        };
    }

    private static string GetRenewalClientGroupKey(RenewalRecordDto item)
    {
        if (!string.IsNullOrWhiteSpace(item.ClientId))
            return $"id:{item.ClientId}";

        return $"name:{item.ClientName}";
    }

    private static BusinessType ResolveRenewalBusinessType(RenewalRecordUpdateItem item)
    {
        if (Enum.IsDefined(typeof(BusinessType), item.BusinessType))
            return (BusinessType)item.BusinessType;

        return ResolveRenewalBusinessType(item.ProductLineOptionValue);
    }

    private static BusinessType ResolveRenewalBusinessType(int productLineOptionValue) =>
        productLineOptionValue switch
        {
            645250000 => BusinessType.ModernWork,
            645250001 => BusinessType.Acronis,
            645250002 => BusinessType.Azure,
            645250003 => BusinessType.Copiers,
            645250007 => BusinessType.Perpetuo,
            645250004 => BusinessType.Hardware,
            _ => BusinessType.ModernWork
        };

    private static decimal CalculateMarginPercentForSale(decimal saleUnit, decimal costUnit)
    {
        if (costUnit <= 0m)
            return 0m;

        return Math.Round(((saleUnit / costUnit) - 1m) * 100m, 6, MidpointRounding.AwayFromZero);
    }

    private static string? DetectLookupValueProperty(JsonElement item, IEnumerable<string> candidates, string containsToken)
    {
        foreach (var candidate in candidates.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            if (item.TryGetProperty(candidate, out _))
                return candidate;
        }

        foreach (var property in item.EnumerateObject())
        {
            if (!property.Name.EndsWith("_value", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!property.Name.Contains(containsToken, StringComparison.OrdinalIgnoreCase))
                continue;

            return property.Name;
        }

        return null;
    }

    private static string ResolveLookupLogicalName(string? primary, string? secondary, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
            return primary.Trim();

        if (!string.IsNullOrWhiteSpace(secondary))
            return secondary.Trim();

        return fallback;
    }

    private static string? DeriveLookupLogicalName(string? lookupValuePropertyName)
    {
        if (string.IsNullOrWhiteSpace(lookupValuePropertyName))
            return null;

        var trimmed = lookupValuePropertyName.Trim();
        if (trimmed.StartsWith('_') && trimmed.EndsWith("_value", StringComparison.OrdinalIgnoreCase))
            return trimmed[1..^6];

        return trimmed;
    }

    private static string? ReadLookupFormattedValue(JsonElement item, string? lookupValuePropertyName)
    {
        if (string.IsNullOrWhiteSpace(lookupValuePropertyName))
            return null;

        var formattedPropertyName = $"{lookupValuePropertyName}{FormattedValueAnnotationSuffix}";
        return ReadString(item, formattedPropertyName);
    }

    private static string ReadString(JsonElement item, string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return "";

        if (!item.TryGetProperty(propertyName, out var property))
            return "";

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? "",
            JsonValueKind.Number => property.ToString(),
            _ => ""
        };
    }

    private static int ReadIntFlexible(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
            return 0;

        if (property.ValueKind == JsonValueKind.Number)
        {
            if (property.TryGetInt32(out var intValue))
                return intValue;

            if (property.TryGetDecimal(out var decimalValue))
                return (int)Math.Round(decimalValue, MidpointRounding.AwayFromZero);
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var raw = property.GetString();
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                return parsedInt;

            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedDecimal))
                return (int)Math.Round(parsedDecimal, MidpointRounding.AwayFromZero);
        }

        return 0;
    }

    private static bool TryParseDateOnly(string? raw, out DateOnly date)
    {
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date))
            return true;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            date = DateOnly.FromDateTime(dto.UtcDateTime);
            return true;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            date = DateOnly.FromDateTime(dt);
            return true;
        }

        date = default;
        return false;
    }

    private static string NormalizeGuid(string? raw, string paramName)
    {
        if (!Guid.TryParse(raw, out var parsed))
            throw new InvalidOperationException($"El valor de {paramName} no es valido.");

        return parsed.ToString("D");
    }

    private static string NormalizeOptionalGuid(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        return Guid.TryParse(raw, out var parsed) ? parsed.ToString("D") : "";
    }

    private static List<string?> BuildLookupLogicalNameCandidates(params string?[] candidates)
    {
        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string?>()
            .ToList();
    }

    private async Task<List<string?>> ResolveSalesPerformanceNavigationPropertyCandidatesAsync(
        string? attributeLogicalName,
        IEnumerable<string?> fallbackCandidates,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var fallbackList = BuildLookupLogicalNameCandidates(
            (fallbackCandidates ?? Array.Empty<string?>())
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .ToArray());

        var normalizedAttribute = attributeLogicalName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedAttribute))
            return fallbackList;

        var cacheKey = $"{DefaultSalesPerformanceEntityLogicalName}|{normalizedAttribute}";
        if (!_salesPerformanceNavigationPropertyCache.TryGetValue(cacheKey, out var metadataCandidates))
        {
            metadataCandidates = await LoadSalesPerformanceNavigationPropertyCandidatesAsync(normalizedAttribute, user, ct);
            _salesPerformanceNavigationPropertyCache[cacheKey] = metadataCandidates;
        }

        return BuildLookupLogicalNameCandidates(
            metadataCandidates
                .Cast<string?>()
                .Concat(fallbackList)
                .ToArray());
    }

    private async Task<string[]> LoadSalesPerformanceNavigationPropertyCandidatesAsync(
        string attributeLogicalName,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(DefaultSalesPerformanceEntityLogicalName)}')" +
                "?$select=LogicalName" +
                "&$expand=ManyToOneRelationships($select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,SchemaName)";
            var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("ManyToOneRelationships", out var relationships)
                || relationships.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return relationships
                .EnumerateArray()
                .Where(relationship => string.Equals(
                    ReadString(relationship, "ReferencingAttribute"),
                    attributeLogicalName,
                    StringComparison.OrdinalIgnoreCase))
                .Select(relationship => ReadString(relationship, "ReferencingEntityNavigationPropertyName"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "No fue posible consultar la metadata de Dataverse para resolver la navegacion del lookup {LookupAttribute} en sales performance.",
                attributeLogicalName);
            return Array.Empty<string>();
        }
    }

    private async Task<string> ResolveSalesPerformancePrimaryNameFieldAsync(
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var cacheKey = DefaultSalesPerformanceEntityLogicalName;
        if (_salesPerformancePrimaryNameFieldCache.TryGetValue(cacheKey, out var cachedField)
            && !string.IsNullOrWhiteSpace(cachedField))
        {
            return cachedField;
        }

        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(DefaultSalesPerformanceEntityLogicalName)}')" +
                "?$select=PrimaryNameAttribute";
            var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);

            using var doc = JsonDocument.Parse(json);
            var primaryNameAttribute = ReadString(doc.RootElement, "PrimaryNameAttribute").Trim();
            if (!string.IsNullOrWhiteSpace(primaryNameAttribute))
            {
                _salesPerformancePrimaryNameFieldCache[cacheKey] = primaryNameAttribute;
                return primaryNameAttribute;
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "No fue posible consultar la metadata de Dataverse para resolver el campo primario de sales performance.");
        }

        return _salesPerformancePrimaryNameField;
    }

    private async Task<string> ResolveEntityPrimaryNameFieldAsync(
        string logicalName,
        string fallbackField,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var cacheKey = logicalName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(cacheKey))
            return fallbackField;

        if (_entityPrimaryNameFieldCache.TryGetValue(cacheKey, out var cachedField)
            && !string.IsNullOrWhiteSpace(cachedField))
        {
            return cachedField;
        }

        try
        {
            var relativeUrl =
                $"/api/data/v9.2/EntityDefinitions(LogicalName='{EscapeOdataLiteral(cacheKey)}')" +
                "?$select=PrimaryNameAttribute";
            var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);

            using var doc = JsonDocument.Parse(json);
            var primaryNameAttribute = ReadString(doc.RootElement, "PrimaryNameAttribute").Trim();
            if (!string.IsNullOrWhiteSpace(primaryNameAttribute))
            {
                _entityPrimaryNameFieldCache[cacheKey] = primaryNameAttribute;
                return primaryNameAttribute;
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "No fue posible consultar la metadata de Dataverse para resolver el campo primario de {EntityLogicalName}.",
                cacheKey);
        }

        return fallbackField;
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal NormalizeNominaRate(string? rawValue, decimal fallback)
    {
        if (!decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return fallback;

        if (parsed <= 0m)
            return fallback;

        return parsed > 1m ? parsed / 100m : parsed;
    }

    private async Task<string> CallDataverseGetJsonAsync(
        string relativeUrl,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct,
        Action<HttpRequestMessage>? customizeRequest = null)
    {
        // En tu combinación de paquetes, esto devuelve HttpResponseMessage.
        var result = await _downstreamApi.CallApiForUserAsync(
            serviceName: "Dataverse",
            options =>
            {
                options.RelativePath = relativeUrl;
                options.HttpMethod = "GET";
                options.CustomizeHttpRequestMessage = customizeRequest;
            },
            user: user,
            cancellationToken: ct);

        if (result is not System.Net.Http.HttpResponseMessage resp)
            throw new InvalidOperationException($"Unexpected downstream response type: {result?.GetType().FullName ?? "null"}");

        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");

        return body;
    }
    private async Task<string> CallDataverseSendAsync(string relativeUrl, string method, object payload, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct)
    {
        var jsonPayload = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var result = await _downstreamApi.CallApiForUserAsync(
            serviceName: "Dataverse",
            options =>
            {
                options.RelativePath = relativeUrl;
                options.HttpMethod = method;
            },
            user: user,
            content: content,
            cancellationToken: ct);

        if (result is not System.Net.Http.HttpResponseMessage resp)
            throw new InvalidOperationException($"Unexpected downstream response type: {result?.GetType().FullName ?? "null"}");

        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");

        return body;
    }
 private async Task CallDataverseDeleteAsync(string relativeUrl, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct)
    {
        var result = await _downstreamApi.CallApiForUserAsync(
            serviceName: "Dataverse",
            options =>
            {
                options.RelativePath = relativeUrl;
                options.HttpMethod = "DELETE";
            },
            user: user,
            cancellationToken: ct);

        if (result is not System.Net.Http.HttpResponseMessage resp)
            throw new InvalidOperationException($"Unexpected downstream response type: {result?.GetType().FullName ?? "null"}");

        if (resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return;

        var body = await resp.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException($"Dataverse error {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");
    }

    private async Task<string?> FindScenarioRecordIdAsync(string scenarioId, string systemUserId, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(scenarioId) || string.IsNullOrWhiteSpace(systemUserId))
            return null;

        var select = $"{_scenariosTableName}id";
        var filter = $"cr07a_scenarioid eq '{EscapeOdataLiteral(scenarioId)}' and cr07a_systemuserid eq '{EscapeOdataLiteral(systemUserId)}'";
        var relativeUrl = $"/api/data/v9.2/{_scenariosTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=1";

        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);
        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("value");
        if (value.GetArrayLength() == 0)
            return null;

        var record = value[0];
        var idPropName = $"{_scenariosTableName}id";
        return record.TryGetProperty(idPropName, out var idProp) ? idProp.GetString() : null;
    }

    private static decimal? ReadDecimal(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;

        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetDecimal(out var d) ? d : null,
            JsonValueKind.String => decimal.TryParse(p.GetString(), out var d) ? d : null,
            _ => null
        };
    }
    private static int ReadInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return 0;

        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetInt32(out var v) ? v : 0,
            JsonValueKind.String => int.TryParse(p.GetString(), out var v) ? v : 0,
            _ => 0
        };
    }

    private static bool ReadBool(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return false;

        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(p.GetString(), out var v) && v,
            JsonValueKind.Number => p.TryGetInt32(out var v) && v != 0,
            _ => false
        };
    }

    private static DateOnly? ReadDateOnly(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String)
            return null;

        var raw = p.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dateOnly))
            return dateOnly;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            return DateOnly.FromDateTime(dto.UtcDateTime);

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return DateOnly.FromDateTime(dt);

        return null;
    }

    private async Task<List<RenewalDateLookupItem>> QueryRenewalDatesByClientAsync(
        Guid clientGuid,
        string lookupField,
        System.Security.Claims.ClaimsPrincipal user,
        int top,
        CancellationToken ct)
    {
        var select = $"{_salesPerformanceIdField},{_salesPerformanceRenewalDateField}";
        var filter = $"{lookupField} eq {clientGuid:D} and {_salesPerformanceRenewalDateField} ne null";
        var relativeUrl = $"/api/data/v9.2/{_salesPerformanceTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={_salesPerformanceRenewalDateField} asc&$top={top}";

        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);

        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("value");

        var list = new List<RenewalDateLookupItem>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
        {
            var renewalDate = ReadDateOnly(item, _salesPerformanceRenewalDateField);
            if (!renewalDate.HasValue)
                continue;

            list.Add(new RenewalDateLookupItem
            {
                RecordId = item.TryGetProperty(_salesPerformanceIdField, out var idProp) ? (idProp.GetString() ?? "") : "",
                DateValue = renewalDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DisplayDate = renewalDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            });
        }

        if (arr.GetArrayLength() > 0 && list.Count == 0)
        {
            _logger.LogWarning(
                "Se encontraron {RecordCount} registros para cliente {ClientId} con lookup {LookupField}, pero ninguno tenia una fecha valida en {RenewalDateField}.",
                arr.GetArrayLength(),
                clientGuid,
                lookupField,
                _salesPerformanceRenewalDateField);
        }
        else if (arr.GetArrayLength() > list.Count)
        {
            _logger.LogWarning(
                "Se omitieron {SkippedCount} registros sin fecha valida para cliente {ClientId} con lookup {LookupField}.",
                arr.GetArrayLength() - list.Count,
                clientGuid,
                lookupField);
        }

        return DistinctRenewalDates(list);
    }

    private async Task<List<RenewalDateLookupItem>> ScanRenewalDatesByClientGuidAsync(
        Guid clientGuid,
        System.Security.Claims.ClaimsPrincipal user,
        int top,
        CancellationToken ct)
    {
        var relativeUrl = $"/api/data/v9.2/{_salesPerformanceTableSetName}?$filter={Uri.EscapeDataString($"{_salesPerformanceRenewalDateField} ne null")}&$orderby={_salesPerformanceRenewalDateField} asc&$top={top}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct);

        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("value");

        var list = new List<RenewalDateLookupItem>();
        foreach (var item in arr.EnumerateArray())
        {
            if (!RecordContainsClientGuid(item, clientGuid))
                continue;

            var renewalDate = ReadDateOnly(item, _salesPerformanceRenewalDateField);
            if (!renewalDate.HasValue)
                continue;

            list.Add(new RenewalDateLookupItem
            {
                RecordId = item.TryGetProperty(_salesPerformanceIdField, out var idProp) ? (idProp.GetString() ?? "") : "",
                DateValue = renewalDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DisplayDate = renewalDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            });
        }

        if (list.Count == 0 && arr.GetArrayLength() > 0)
        {
            _logger.LogDebug(
                "La consulta de escaneo no encontro fechas parseables para cliente {ClientId} dentro de {RecordCount} registros revisados.",
                clientGuid,
                arr.GetArrayLength());
        }

        return DistinctRenewalDates(list);
    }

    private static bool RecordContainsClientGuid(JsonElement item, Guid clientGuid)
    {
        var clientGuidText = clientGuid.ToString("D");

        foreach (var property in item.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                continue;

            if (!property.Name.Contains("cliente", StringComparison.OrdinalIgnoreCase)
                && !property.Name.EndsWith("_value", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(property.Value.GetString(), clientGuidText, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static List<RenewalDateLookupItem> DistinctRenewalDates(IEnumerable<RenewalDateLookupItem> items)
    {
        return items
            .Where(item => !string.IsNullOrWhiteSpace(item.DateValue))
            .GroupBy(item => item.DateValue, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.DateValue, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static T? DeserializeJsonOrDefault<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string EscapeOdataLiteral(string value)
    {
        return (value ?? string.Empty).Replace("'", "''");
    }
}
