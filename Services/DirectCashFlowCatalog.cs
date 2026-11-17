using System.Globalization;
using System.Text;
using CotizadorInterno.Web.Models.Conciliacion;

namespace CotizadorInterno.Web.Services;

public static class DirectCashFlowCatalog
{
    // Stable keys use the template row as well as its printed code: 903 occurs twice.
    public static readonly IReadOnlyList<DirectCashFlowConcept> Concepts = [
        C(11,"101","CLIENTES CLOUD","Entrada","SERVICIOS"), C(12,"102","CLIENTES COPIERS","Entrada","SERVICIOS"),
        C(13,"103","CLIENTES OUTSOURCING","Entrada","SERVICIOS"), C(14,"104","CLIENTES BORIS","Entrada","SERVICIOS"), C(15,"105","CLIENTES HARDWARE","Entrada","SERVICIOS"),
        C(17,"201","REBATES MICROSOFT","Entrada","OTROS INGRESOS"), C(18,"202","DEVOLUCIONES","Entrada","OTROS INGRESOS"), C(19,"203","INCAPACIDADES","Entrada","OTROS INGRESOS"), C(20,"204","MOVIMIENTO A BOLSILLOS","Entrada","OTROS INGRESOS"), C(21,"205","OTROS","Entrada","OTROS INGRESOS"),
        C(25,"501","SEBASTIAN RUIZ","Salida","PAGO DE SOCIOS"), C(26,"502","ANGIE DAZA","Salida","PAGO DE SOCIOS"), C(27,"503","GERMAN RUIZ (JUAN CARLOS RUIZ)","Salida","PAGO DE SOCIOS"), C(28,"504","YOLANDA ROSERO (DARIO RAMIREZ)","Salida","PAGO DE SOCIOS"),
        C(30,"659","NOMINA","Salida","NÓMINA Y SEGURIDAD SOCIAL"), C(31,"660","LIQUIDACIONES","Salida","NÓMINA Y SEGURIDAD SOCIAL"), C(32,"661","PLANILLA SEGURIDAD SOCIAL","Salida","NÓMINA Y SEGURIDAD SOCIAL"), C(33,"662","PRIMAS + CESANTIAS + INTERESES DE CESANTIAS","Salida","NÓMINA Y SEGURIDAD SOCIAL"),
        C(35,"701","COMISIONES COMERCIALES","Salida","OTROS GASTOS DE PERSONAL"), C(36,"702","AUXILIOS","Salida","OTROS GASTOS DE PERSONAL"), C(37,"703","OTROS","Salida","OTROS GASTOS DE PERSONAL"),
        C(40,"801","INTCOMEX","Salida","PROVEEDORES"), C(41,"802","VARO ASESORES SAS","Salida","PROVEEDORES"), C(42,"803","GERMAN SOTO","Salida","PROVEEDORES"), C(43,"804","OTROS PROVEEDORES CLOUD","Salida","PROVEEDORES"), C(44,"805","PROVEEDORES COPIERS","Salida","PROVEEDORES"), C(45,"806","REEMBOLSO CAJA MENOR","Salida","PROVEEDORES"), C(46,"807","PAGO CON BOLSILLOS","Salida","PROVEEDORES"), C(47,"808","OTROS","Salida","PROVEEDORES"),
        C(49,"901","ALOJAMIENTO","Salida","GASTOS DE VIAJE"), C(50,"902","TIQUETES","Salida","GASTOS DE VIAJE"), C(51,"903","VIATICOS","Salida","GASTOS DE VIAJE"), C(52,"903","GASTOS DE REPRESENTACION","Salida","GASTOS DE VIAJE"),
        C(54,"1201","ARRIENDO OFICINA","Salida","ARRENDAMIENTO"), C(55,"1202","ARRIENDO BODEGA","Salida","ARRENDAMIENTO"), C(57,"1301","SEGUROS","Salida","SEGUROS"),
        C(59,"1401","CELULARES","Salida","SERVICIOS PÚBLICOS"), C(60,"1402","SERVICIOS DE INTERNET","Salida","SERVICIOS PÚBLICOS"), C(61,"1403","SERVICIO DE ENERGIA","Salida","SERVICIOS PÚBLICOS"), C(62,"1404","SERVICIO DE ACUEDUCTO","Salida","SERVICIOS PÚBLICOS"),
        C(64,"1501","RETENCIÓN EN LA FUENTE","Salida","IMPUESTOS"), C(65,"1502","RETENCIÓN DE ICA","Salida","IMPUESTOS"), C(66,"1503","IMPUESTO A LAS VENTAS","Salida","IMPUESTOS"), C(67,"1504","IMPUESTO DE INDUSTRIA Y COMERCIO","Salida","IMPUESTOS"),
        C(71,"2001","CIERRE CDT","Entrada","FINANCIACIÓN"), C(72,"2002","INTERESES CDT","Entrada","FINANCIACIÓN"), C(73,"2003","PRESTAMOS","Entrada","FINANCIACIÓN"),
        C(75,"2101","APERTURA CDT","Salida","FINANCIACIÓN"), C(76,"2102","PRÉSTAMOS BANCARIOS","Salida","FINANCIACIÓN"), C(77,"2110","TARJETA DE CRÉDITO SEBASTIÁN RUIZ","Salida","FINANCIACIÓN"), C(78,"2111","TARJETA DE CRÉDITO ANGIE DAZA","Salida","FINANCIACIÓN"), C(79,"2112","COMISIONES FINANCIERAS Y 4X1000","Salida","FINANCIACIÓN")
    ];
    public static DirectCashFlowConcept? Find(string? key) => Concepts.FirstOrDefault(x => x.Key == key);
    public static DirectCashFlowConcept Require(string key, string direction)
    {
        var concept = Find(key) ?? throw new InvalidOperationException("Selecciona un concepto válido del flujo de caja.");
        if (concept.Direction != direction) throw new InvalidOperationException("El concepto no corresponde a la dirección del registro.");
        return concept;
    }
    private static DirectCashFlowConcept C(int row, string code, string label, string direction, string group) => new($"{code}:{row}",code,label,direction,row,group);

    public static (string Key, string Reason) Suggest(ConciliacionCashFlowRowDto row)
    {
        var direction = row.EntryValue > 0 && row.ExitValue == 0 ? "Entrada" : row.ExitValue > 0 && row.EntryValue == 0 ? "Salida" : "";
        if (direction == "") return ("", "Revisar importe o dirección del registro.");
        if (row.SourceKind == "Traslado" || row.DetectedTypeKey == "traslado-interno") return ("", "Traslado entre cuentas propias: excluido del flujo consolidado.");
        var text = Normalize($"{row.Description} {row.Recipient} {row.Observations} {row.AccountName} {row.DocumentType}");
        (string,string) Pick(string code,string reason) => (Concepts.First(x=>x.Code==code&&x.Direction==direction).Key,reason);
        bool Has(params string[] terms) => terms.Any(text.Contains);
        if (direction=="Entrada")
        {
            if(Has("REBATE")) return Pick("201","Descripción de rebate.");
            if(Has("INCAPACIDAD")) return Pick("203","Descripción de incapacidad.");
            if(Has("DEVOLUC","REINTEGRO")) return Pick("202","Descripción de devolución o reintegro.");
            if(Has("CDT")&&Has("INTERES","RENDIMIENTO")) return Pick("2002","Intereses de CDT.");
            if(Has("CDT")) return Pick("2001","Cierre o retiro de CDT; revisar.");
            if(Has("PRESTAM","CREDITO BANCARIO")) return Pick("2003","Descripción de préstamo recibido.");
            if(Has("BOLSILLO")) return Pick("204","Descripción de bolsillo; revisar alcance del saldo.");
            if(Has("OUTSOURCING")) return Pick("103","Descripción identifica Outsourcing.");
            if(Has("BORIS")) return Pick("104","Descripción identifica Boris.");
            if(Has("HARDWARE")) return Pick("105","Descripción identifica Hardware.");
            var invoiceText=Normalize(row.Description);
            var copiersInvoice=System.Text.RegularExpressions.Regex.IsMatch(invoiceText,@"FV\s*-\s*1\s*-");
            var cloudInvoice=System.Text.RegularExpressions.Regex.IsMatch(invoiceText,@"FV\s*-\s*2\s*-");
            if(copiersInvoice&&cloudInvoice) return Pick("205","Cobro mezcla facturas FV-1 y FV-2. Revisar distribución antes del cierre.");
            if(copiersInvoice) return Pick("102","Factura FV-1 identificada; confirmar vertical del cobro.");
            if(cloudInvoice) return Pick("101","Factura FV-2 identificada; confirmar vertical del cobro.");
            if(row.DetectedTypeKey=="entrada-fe" || Has("FACTURA","FE-","PAGO CLIENTE"))
                return Pick(row.SourceFlow=="Copiers"?"102":"101","Cobro de cliente según tipo y cuenta de origen; revisar la vertical.");
            return Pick("205","Sin evidencia suficiente: otros ingresos provisional; revisar.");
        }
        if(Has("4X1000","4 X 1000","GMF","COMISION BANCARIA","COMISION FINANCIERA","CUOTA MANEJO","GRAVAMEN")) return Pick("2112","Comisión bancaria o gravamen.");
        if(Has("TARJETA")&&Has("CREDITO")) return Pick(Has("ANGIE")?"2111":"2110","Pago de tarjeta de crédito; confirmar titular.");
        if(Has("CDT")) return Pick("2101","Apertura de CDT.");
        if(Has("CREDITO BANCARIO","PRESTAMO BANCARIO","CUOTA CARRO")) return Pick("2102","Cuota o préstamo bancario; confirmar naturaleza.");
        if(Has("PRESTAM","ANTICIPO")) return Pick("703","Préstamo o anticipo a una persona; revisar clasificación.");
        if(Has("INTCOMEX")) return Pick("801","Proveedor Intcomex identificado.");
        if(Has("VARO")) return Pick("802","Proveedor Varo identificado.");
        if(Has("GERMAN SOTO")) return Pick("803","Proveedor Germán Soto identificado.");
        if(Has("SEGURIDAD SOCIAL","PLANILLA","APORTES EN LINEA","PILA ")) return Pick("661","Aportes de seguridad social.");
        if(Has("PRIMA","CESANTIA")) return Pick("662","Prestaciones sociales.");
        if(Has("LIQUIDACION")) return Pick("660","Liquidación de personal.");
        if(Has("NOMINA","SALARIO")) return Pick("659","Descripción de nómina.");
        if(Has("COMISION")) return Pick("701","Comisión; confirmar que corresponde a personal.");
        if(Has("AUXILIO")) return Pick("702","Auxilio de personal.");
        if(Has("CAJA MENOR")) return Pick("806","Reembolso de caja menor.");
        if(Has("BOLSILLO")) return Pick("807","Pago con bolsillo; revisar cuenta de origen.");
        if(Has("REEMBOLSO GASTOS")) return Pick("808","Reembolso de gastos: requiere revisar el soporte antes de asignar el concepto definitivo.");
        if(Has("SEBASTIAN RUIZ")) return Pick("501","Nombre de socio identificado; confirmar naturaleza del pago.");
        if(Has("ANGIE DAZA")) return Pick("502","Nombre de socio identificado; confirmar naturaleza del pago.");
        if(Has("GERMAN RUIZ","JUAN CARLOS RUIZ")) return Pick("503","Nombre de socio identificado.");
        if(Has("YOLANDA ROSERO","DARIO RAMIREZ")) return Pick("504","Nombre de socio identificado.");
        if(Has("HOTEL","ALOJAMIENTO")) return Pick("901","Descripción de alojamiento.");
        if(Has("TIQUETE","AVIANC","LATAM")) return Pick("902","Descripción de tiquetes.");
        if(Has("VIATIC")) return Pick("903","Descripción de viáticos.");
        if(Has("REPRESENTACION","EVENTO")) return ("903:52","Gastos de representación o evento; revisar soporte.");
        if(Has("ARRIENDO","ARRENDAMIENTO")) return Pick(Has("BODEGA")?"1202":"1201","Arriendo; confirmar inmueble.");
        if(Has("SEGURO","POLIZA")) return Pick("1301","Descripción de seguro.");
        if(Has("INTERNET")) return Pick("1402","Servicio de internet.");
        if(Has("CELULAR","MOVIL")) return Pick("1401","Servicio celular.");
        if(Has("ENERGIA","ENEL","CODENSA")) return Pick("1403","Servicio de energía.");
        if(Has("ACUEDUCTO")) return Pick("1404","Servicio de acueducto.");
        if(Has("RETEFUENTE","RETENCION EN LA FUENTE")) return Pick("1501","Retención en la fuente.");
        if(Has("RETEICA","RETENCION DE ICA")) return Pick("1502","Retención de ICA.");
        if(Has("IMPUESTO IVA","PAGO IVA","IMPUESTO A LAS VENTAS")) return Pick("1503","Pago de IVA.");
        if(Has("INDUSTRIA Y COMERCIO","IMPUESTO ICA")) return Pick("1504","Impuesto de industria y comercio.");
        if(row.DetectedTypeKey=="salida-fe") return Pick(row.SourceFlow=="Copiers"?"805":"804","Pago a proveedor según cuenta; revisar.");
        return Pick("808","Sin evidencia suficiente: otros egresos provisional; revisar.");
    }
    private static string Normalize(string s) => string.Concat(s.Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)).ToUpperInvariant();
}
