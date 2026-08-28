using System.Globalization;
using System.Net;
using System.Text;
using CotizadorInterno.Web.Models.Contracts;

namespace CotizadorInterno.Web.Services;

public static class ContractsDocumentBuilder
{
    private static readonly CultureInfo ColombianCulture = CultureInfo.GetCultureInfo("es-CO");

    public static ContractDocumentArtifact BuildContract(
        string consecutive,
        string orderNumber,
        ContractRutExtractionDto rut,
        ContractOfferExtractionDto offer,
        DateOnly contractDate,
        string signatureCity)
    {
        var title = $"CONTRATO MARCO DE ARRENDAMIENTO DE EQUIPOS DE IMPRESIÓN SUSCRITO ENTRE {Upper(rut.LegalName)} Y DIGITAL TECH COPIERS S.A.S.";
        var body = new StringBuilder();
        body.Append(BuildHeader(title, $"No. CONTRATO {consecutive}"));
        body.Append("<table class='summary'>");
        body.Append(SummaryRow("No. CONTRATO", consecutive));
        body.Append(SummaryRow("CLASE CONTRATO", "CONTRATO MARCO DE ARRENDAMIENTO DE EQUIPOS DE IMPRESIÓN."));
        body.Append(SummaryRow("OBJETO", "ARRENDAMIENTO DE IMPRESORAS Y/O EQUIPOS MULTIFUNCIONALES, CON INSTALACIÓN, MANTENIMIENTO, SOPORTE TÉCNICO, SUMINISTRO DE INSUMOS Y DEMÁS SERVICIOS DEFINIDOS EN CADA ORDEN DE SERVICIO."));
        body.Append(SummaryRow("CONTRATANTE", Upper(rut.LegalName)));
        body.Append(SummaryRow("NIT CONTRATANTE", FormatNit(rut.Nit, rut.VerificationDigit)));
        body.Append(SummaryRow("CONTRATISTA", "DIGITAL TECH COPIERS S.A.S."));
        body.Append(SummaryRow("NIT CONTRATISTA", "900.399.875-5"));
        body.Append("</table>");

        body.Append($"<p>Entre <strong>DIGITAL TECH COPIERS S.A.S.</strong> (en adelante, EL PROVEEDOR), sociedad legalmente constituida bajo las leyes de la República de Colombia, identificada con NIT 900.399.875-5, representada legalmente por Sebastian Ruiz Rosero, identificado con Cédula de Ciudadanía No. 1.032.470.548 de Bogotá D.C., con domicilio y dirección de notificación en Carrera 45A No. 95-37, Oficina 403, Bogotá D.C.; y por la otra parte <strong>{H(Upper(rut.LegalName))}</strong> (en adelante, EL CLIENTE), identificada con NIT {H(FormatNit(rut.Nit, rut.VerificationDigit))}, con domicilio principal en {H(FirstNonEmpty(rut.City, "Bogotá D.C."))}, representada legalmente por {H(rut.LegalRepresentativeName)}, identificado(a) con {H(FirstNonEmpty(rut.LegalRepresentativeId, "identificación registrada en el RUT"))}, con dirección de notificación en {H(FirstNonEmpty(rut.NotificationAddress, rut.MainAddress))}, hemos acordado celebrar el presente CONTRATO (en adelante, el CONTRATO), el cual se ejecutará mediante órdenes de servicio, órdenes de compra, cotizaciones u ofertas comerciales aceptadas por EL CLIENTE. EL PROVEEDOR y EL CLIENTE se denominarán conjuntamente LAS PARTES e individualmente LA PARTE.</p>");
        body.Append("<h2>CLÁUSULAS</h2>");

        foreach (var clause in BuildClauses(rut, offer, consecutive, orderNumber))
            body.Append($"<p><strong>{H(clause.Title)}:</strong> {H(clause.Text)}</p>");

        body.Append($"<p>En constancia, el presente CONTRATO se firma en {H(signatureCity)} a los {H(FormatLongDate(contractDate))} por ambas partes.</p>");
        body.Append("<h2>FIRMAS</h2>");
        body.Append(BuildSignatureTable(rut));

        body.Append("<div class='page-break'></div>");
        body.Append(BuildAnnexA());
        body.Append("<div class='page-break'></div>");
        body.Append(BuildAnnexB(consecutive));
        body.Append("<div class='page-break'></div>");
        body.Append(BuildOrderContent(consecutive, orderNumber, rut, offer, ContractOptionValues.OrderInitial, contractDate, offer.DurationMonths, offer.ExecutionAddress, offer.Summary));

        return BuildWordArtifact($"{consecutive}-Contrato-Copiers.doc", title, body.ToString());
    }

    public static ContractDocumentArtifact BuildServiceOrder(
        string consecutive,
        string orderNumber,
        ContractRutExtractionDto rut,
        ContractOfferExtractionDto offer,
        int orderType,
        DateOnly creationDate,
        int durationMonths,
        string executionAddress,
        string orderObject)
    {
        var content = BuildOrderContent(
            consecutive,
            orderNumber,
            rut,
            offer,
            orderType,
            creationDate,
            durationMonths,
            executionAddress,
            orderObject);
        return BuildWordArtifact($"{orderNumber}-Orden-Servicio.doc", $"Orden de servicio {orderNumber}", content);
    }

    public static ContractDocumentArtifact BuildDeliveryAct(
        string consecutive,
        string orderNumber,
        int actNumber,
        ContractRutExtractionDto rut,
        ContractOfferExtractionDto offer,
        string executionAddress)
    {
        var body = new StringBuilder();
        body.Append($"""
            <div class="act-header">
              <div class="brand">DIGITAL TECH<small>Es momento de transformar tu negocio</small></div>
              <div class="act-title">ACTA DE ENTREGA, INSTALACIÓN Y PUESTA EN FUNCIONAMIENTO<br><span>EQUIPO DE IMPRESIÓN</span> CM No. {H(consecutive)} y O.S No. {H(orderNumber)}.</div>
            </div>
            <p><strong>Documento integral del Contrato Marco No. {H(consecutive)} y de la Orden de Servicio No. {H(orderNumber)}.</strong> La fecha registrada en esta acta corresponde a la fecha efectiva de inicio de la Orden de Servicio.</p>
            <h2 class="act-section">1. IDENTIFICACIÓN DEL ACTA Y REFERENCIA CONTRACTUAL</h2>
            <table class="act-grid">
              <tr><th>No. de acta</th><td>{actNumber}</td><th>Ciudad</th><td>{H(FirstNonEmpty(rut.City, "Bogotá D.C."))}</td></tr>
              <tr><th>Contrato marco</th><td>{H(consecutive)}</td><th>Orden de servicio</th><td>{H(orderNumber)}</td></tr>
              <tr><th>Fecha efectiva</th><td>____ / ____ / ______</td><th>Hora de entrega</th><td></td></tr>
            </table>
            <h2 class="act-section">2. IDENTIFICACIÓN DE LAS PARTES</h2>
            <table class="act-grid">
              <tr><th>Proveedor</th><td>DIGITAL TECH COPIERS S.A.S.</td><th>NIT</th><td>900.399.875-5</td></tr>
              <tr><th>Dirección</th><td>Carrera 45A No. 95-37, Oficina 403, Bogotá D.C.</td><th>Contacto</th><td>Jhonatan Saldarriaga - 300 416 1294</td></tr>
              <tr><th>Cliente</th><td>{H(Upper(rut.LegalName))}</td><th>NIT</th><td>{H(FormatNit(rut.Nit, rut.VerificationDigit))}</td></tr>
              <tr><th>Lugar de instalación</th><td>{H(FirstNonEmpty(executionAddress, offer.ExecutionAddress, rut.MainAddress))}</td><th>Responsable cliente</th><td>{H(offer.ClientContact)}</td></tr>
            </table>
            <h2 class="act-section">3. IDENTIFICACIÓN DE LOS EQUIPOS ENTREGADOS</h2>
            {BuildActEquipmentTable(offer.EquipmentLines)}
            <h2 class="act-section">4. ACCESORIOS, CONSUMIBLES Y ELEMENTOS ENTREGADOS</h2>
            {BuildAccessoriesTable()}
            <h2 class="act-section">5. VERIFICACIÓN DE ENTREGA, INSTALACIÓN Y FUNCIONAMIENTO</h2>
            {BuildVerificationTable()}
            <div class="page-break"></div>
            <h2 class="act-section">6. PENDIENTES, NOVEDADES Y OBSERVACIONES</h2>
            <table class="notes"><tr><td></td></tr><tr><td></td></tr></table>
            <h2 class="act-section">7. CONSTANCIA DE ENTREGA Y ACEPTACIÓN</h2>
            <ol class="declarations">
              <li>EL CLIENTE declara haber recibido los equipos, accesorios y consumibles relacionados en el estado indicado en esta acta, y haber verificado su funcionamiento mediante las pruebas registradas.</li>
              <li>La firma de esta acta establece la fecha efectiva de inicio de la Orden de Servicio No. {H(orderNumber)} y del cobro del canon mensual.</li>
              <li>La entrega no transfiere la propiedad de los equipos. EL CLIENTE asume su custodia desde la entrega y hasta la devolución física a EL PROVEEDOR.</li>
              <li>Las novedades visibles, faltantes o inconformidades deberán quedar consignadas en esta acta. Las fallas técnicas posteriores se reportarán por los canales autorizados.</li>
              <li>Esta acta, sus anexos, registros técnicos y evidencias fotográficas forman parte integral del Contrato Marco y de la Orden de Servicio asociada.</li>
            </ol>
            <h2 class="act-section">8. FIRMAS DE ENTREGA Y RECIBO</h2>
            <p class="muted"><em>Las personas que suscriben declaran que la información consignada es correcta y que cuentan con autorización para entregar o recibir los equipos.</em></p>
            {BuildActSignatureTable()}
            """);

        return BuildWordArtifact($"{consecutive}-{orderNumber}-Acta-Entrega.doc", $"Acta de entrega {orderNumber}", body.ToString(), actStyle: true);
    }

    private static IReadOnlyList<(string Title, string Text)> BuildClauses(
        ContractRutExtractionDto rut,
        ContractOfferExtractionDto offer,
        string consecutive,
        string orderNumber)
    {
        var clientEmail = FirstNonEmpty(offer.BillingEmail, rut.Email, "el canal informado por EL CLIENTE");
        var clientAddress = FirstNonEmpty(rut.NotificationAddress, rut.MainAddress);
        return new (string, string)[]
        {
            ("CLÁUSULA PRIMERA. OBJETO", "EL PROVEEDOR se obliga a entregar en arrendamiento a EL CLIENTE impresoras, equipos multifuncionales y/o soluciones de impresión, incluyendo, cuando se indique en la respectiva Orden de Servicio, transporte, instalación, configuración inicial, mantenimiento preventivo y correctivo, soporte técnico, suministro de tóner, repuestos y servicios complementarios. Las características, cantidades, ubicaciones, volúmenes incluidos, precios y condiciones particulares se definirán en cada Orden de Servicio."),
            ("CLÁUSULA SEGUNDA. NATURALEZA DE CONTRATO MARCO Y ÓRDENES DE SERVICIO", "El presente documento constituye un contrato marco. La prestación específica de cada servicio se realizará mediante órdenes de servicio, cotizaciones, ofertas comerciales u órdenes de compra aceptadas por EL CLIENTE, en las cuales se indicarán, como mínimo, los equipos, cantidades, ubicación, fecha de inicio, duración, canon, consumos incluidos, valor de excedentes, forma de pago, alcance, exclusiones y condiciones particulares. Cada Orden de Servicio será vinculante para LAS PARTES y hará parte integral del CONTRATO."),
            ("CLÁUSULA TERCERA. PROPIEDAD Y NATURALEZA DEL ARRENDAMIENTO", "Los equipos entregados continuarán siendo de propiedad exclusiva de EL PROVEEDOR o de quien este indique. La entrega no transfiere dominio, propiedad, opción de compra ni derecho de disposición a favor de EL CLIENTE. EL CLIENTE no podrá vender, ceder, gravar, subarrendar, prestar, retirar placas o seriales, ni permitir la intervención de terceros sobre los equipos sin autorización previa y escrita de EL PROVEEDOR."),
            ("CLÁUSULA CUARTA. ALCANCE DEL SERVICIO", "EL PROVEEDOR prestará los servicios conforme a la modalidad aprobada en cada Orden de Servicio. El alcance podrá incluir instalación y puesta a punto, configuración de impresión y digitalización, capacitación básica, visitas de lectura de contadores, mantenimientos preventivos programados, mantenimientos correctivos, cambio temporal por equipo de respaldo y suministro de tóner. Los servicios no expresamente incluidos deberán ser cotizados y aprobados por escrito."),
            ("CLÁUSULA QUINTA. VIGENCIA", $"El presente CONTRATO MARCO iniciará en la fecha de firma por LAS PARTES y tendrá una duración inicial de {NumberWithWords(offer.DurationMonths, "mes", "meses")}. Cada Orden de Servicio tendrá la vigencia específica indicada en ella, la cual podrá iniciar en la fecha del acta de entrega e instalación del equipo o en la fecha definida por LAS PARTES por escrito. La terminación del CONTRATO MARCO no afectará las órdenes de servicio vigentes."),
            ("CLÁUSULA SEXTA. RENOVACIÓN", $"El CONTRATO y las órdenes de servicio podrán renovarse automáticamente por períodos sucesivos iguales al inicialmente pactado, salvo que cualquiera de LAS PARTES manifieste por escrito su decisión de no renovar con una antelación mínima de {NumberWithWords(offer.NonRenewalNoticeDays, "día calendario", "días calendario")}. El canon podrá actualizarse conforme al IPC anual, salvo condición diferente pactada en la Orden de Servicio."),
            ("CLÁUSULA SÉPTIMA. ADICIONES, RETIROS, REEMPLAZOS Y TRASLADOS", "Las adiciones o retiros de equipos, cambios de ubicación, ampliaciones de volumen o actividades IMAC deberán ser solicitados por el responsable autorizado de EL CLIENTE y aprobados por EL PROVEEDOR. Podrán documentarse mediante una nueva Orden de Servicio, otrosí, cotización, correo de aceptación u orden de compra."),
            ("CLÁUSULA OCTAVA. PRECIO, FACTURACIÓN Y FORMA DE PAGO", "El precio aplicable será el señalado en cada Orden de Servicio, oferta comercial, cotización u orden de compra aceptada por EL CLIENTE. El canon mensual se causará aun cuando el consumo real sea inferior al volumen mínimo contratado. Los consumos adicionales, repuestos no cubiertos, traslados, reparaciones por daño atribuible a EL CLIENTE y demás servicios no incluidos se facturarán de acuerdo con los valores vigentes o previamente aprobados."),
            ("CLÁUSULA NOVENA. RADICACIÓN DE FACTURAS Y CONTADORES", $"EL PROVEEDOR emitirá factura electrónica conforme a los requisitos legales y al procedimiento de radicación informado por EL CLIENTE. La factura será enviada a {clientEmail}. EL CLIENTE permitirá la lectura de contadores o remitirá la información solicitada. Si no se suministran oportunamente, EL PROVEEDOR podrá estimar el consumo con base en el promedio de los dos meses anteriores y realizar el ajuste posterior."),
            ("CLÁUSULA DÉCIMA. PAGO, MORA Y SUSPENSIÓN", $"EL CLIENTE pagará cada factura dentro de {NumberWithWords(offer.PaymentDays, "día calendario", "días calendario")} siguientes a su emisión o radicación válida. Vencido el plazo, EL PROVEEDOR podrá cobrar intereses de mora a la tasa máxima legal permitida y, después de cinco días calendario adicionales sin pago, suspender temporalmente los servicios sin que ello libere a EL CLIENTE de sus obligaciones."),
            ("CLÁUSULA DÉCIMO PRIMERA. TERMINACIÓN ANTICIPADA DE ÓRDENES DE SERVICIO", "Cuando EL CLIENTE termine unilateralmente una Orden de Servicio antes de su vencimiento, sin incumplimiento comprobado de EL PROVEEDOR, deberá pagar las facturas causadas, consumos pendientes, costos de retiro y una compensación equivalente a tres cánones mensuales por cada equipo contratado, dentro de los quince días calendario siguientes a la terminación."),
            ("CLÁUSULA DÉCIMO SEGUNDA. ENTREGA, INSTALACIÓN Y PUESTA A PUNTO", "EL PROVEEDOR realizará la entrega, instalación y configuración inicial en la dirección indicada en la Orden de Servicio, dentro del plazo comercial acordado y sujeto a disponibilidad, acceso, condiciones eléctricas, conectividad e información suministrada por EL CLIENTE. La entrega se documentará mediante acta que identifique equipo, serial, contador inicial, estado, accesorios, ubicación y fecha efectiva de inicio."),
            ("CLÁUSULA DÉCIMO TERCERA. MANTENIMIENTO PREVENTIVO", "Consiste en la ejecución programada de rutinas de limpieza, pruebas, diagnóstico, revisión, calibración y puesta a punto conforme a las recomendaciones del fabricante y al uso del equipo. La periodicidad será la indicada en la Orden de Servicio."),
            ("CLÁUSULA DÉCIMO CUARTA. MANTENIMIENTO CORRECTIVO Y TIEMPOS DE RESPUESTA", "EL PROVEEDOR realizará acciones razonables para diagnosticar y corregir fallas cubiertas por el servicio. Los tiempos se contabilizarán desde la recepción completa del reporte y serán los definidos en el Anexo de Niveles de Servicio."),
            ("CLÁUSULA DÉCIMO QUINTA. TÓNER, REPUESTOS E INSUMOS", "Cuando la Orden de Servicio lo incluya, EL PROVEEDOR suministrará tóner, repuestos y elementos técnicos necesarios para la operación normal. EL CLIENTE deberá solicitar reposición con mínimo tres días hábiles de anticipación y utilizar los insumos exclusivamente en los equipos objeto del CONTRATO. No se incluyen papel, grapas ni suministros de papelería."),
            ("CLÁUSULA DÉCIMO SEXTA. EQUIPO Y TÓNER DE RESPALDO", "Cuando se encuentre incluido en la Orden de Servicio, EL PROVEEDOR podrá disponer un equipo o tóner de respaldo para mantener la continuidad razonable de la operación. Los tóneres no utilizados deberán devolverse al finalizar la Orden de Servicio o serán facturados."),
            ("CLÁUSULA DÉCIMO SÉPTIMA. OBLIGACIONES DEL PROVEEDOR", "Entregar e instalar los equipos; asignar personal idóneo; prestar mantenimiento y soporte dentro del alcance contratado; suministrar consumibles y repuestos incluidos; documentar actividades técnicas; mantener reserva; informar diagnósticos y cargos; y cumplir las demás obligaciones derivadas del servicio."),
            ("CLÁUSULA DÉCIMO OCTAVA. OBLIGACIONES DEL CLIENTE", "Pagar oportunamente; custodiar y usar adecuadamente los equipos; permitir acceso del personal autorizado; suministrar contadores, información, contactos y autorizaciones; disponer condiciones eléctricas, ambientales y de conectividad; no trasladar ni intervenir sin autorización; informar daños o siniestros; y devolver equipos, accesorios y tóneres al finalizar."),
            ("CLÁUSULA DÉCIMO NOVENA. CUSTODIA, DAÑOS, PÉRDIDA, HURTO O SINIESTRO", "EL CLIENTE será responsable por la custodia desde la entrega hasta la devolución. Los daños por negligencia, uso indebido, líquidos, plagas, intervención de terceros, condiciones eléctricas deficientes o traslado no autorizado serán cotizados y facturados. En caso de pérdida, hurto o siniestro no cubierto, pagará el valor de reposición definido en la Orden de Servicio o el valor comercial razonable."),
            ("CLÁUSULA VIGÉSIMA. EXCLUSIONES Y RESTRICCIONES", "Salvo pacto escrito, no se incluyen papel, grapas, software no definido, adecuaciones eléctricas o de red, cableado, obras civiles, recuperación de información, reparación de equipos ajenos, daños por fuerza mayor, variaciones de voltaje, ausencia de polo a tierra, insumos no autorizados, intervención de terceros ni fallas de servicios externos."),
            ("CLÁUSULA VIGÉSIMO PRIMERA. CONDICIONES ELÉCTRICAS, AMBIENTALES Y DE CONECTIVIDAD", "EL CLIENTE mantendrá tomas reguladas, polo a tierra, protección contra variaciones, ventilación y un entorno libre de humedad o polvo excesivo. También proporcionará red, direcciones IP, credenciales, SMTP, permisos y recursos necesarios para las funciones solicitadas."),
            ("CLÁUSULA VIGÉSIMO SEGUNDA. NOTIFICACIONES", $"Para EL PROVEEDOR: Carrera 45A No. 95-37, Oficina 403, Bogotá D.C.; facturación cartera@digitaltechcolombia.com; contacto comercial Jhonatan Saldarriaga, jsaldarriaga@digitaltechcolombia.com, teléfono 3004161294. Para EL CLIENTE: {clientAddress}; correo {FirstNonEmpty(rut.Email, offer.BillingEmail)}; teléfono {rut.Phone}. Los cambios deberán informarse por escrito."),
            ("CLÁUSULA VIGÉSIMO TERCERA. LEGISLACIÓN APLICABLE Y DOMICILIO", $"El CONTRATO se regirá por las leyes de la República de Colombia. LAS PARTES acuerdan como domicilio contractual la ciudad de {FirstNonEmpty(rut.City, "Bogotá D.C.")}."),
            ("CLÁUSULA VIGÉSIMO CUARTA. MODIFICACIONES", "El CONTRATO solo podrá modificarse por escrito mediante otrosí. No obstante, cambios de cantidades, equipos, ubicaciones, volúmenes, precios, servicios o condiciones operativas podrán documentarse mediante nuevas órdenes de servicio, ofertas, cotizaciones, correos de aceptación u órdenes de compra aceptadas por EL CLIENTE."),
            ("CLÁUSULA VIGÉSIMO QUINTA. MÉRITO EJECUTIVO", "LAS PARTES reconocen que el CONTRATO, sus órdenes de servicio, facturas, actas, ofertas aceptadas, cotizaciones, órdenes de compra y documentos integrales prestan mérito ejecutivo para exigir obligaciones claras, expresas y exigibles. EL CLIENTE renuncia a requerimientos judiciales para ser constituido en mora."),
            ("CLÁUSULA VIGÉSIMO SEXTA. DOCUMENTOS INTEGRALES", "Forman parte integral las órdenes de servicio, ofertas comerciales, órdenes de compra, actas de entrega, aclaraciones, correos de aceptación, anexos técnicos, niveles de servicio y formularios de conocimiento de contraparte. En caso de contradicción prevalecerá el CONTRATO y, para condiciones específicas, la Orden de Servicio aceptada."),
            ("CLÁUSULA VIGÉSIMO SÉPTIMA. CUMPLIMIENTO, ORIGEN DE FONDOS, ANTICORRUPCIÓN Y SOBORNO", "LAS PARTES declaran que los recursos provienen de actividades lícitas y cumplirán las normas sobre prevención de lavado de activos, financiación del terrorismo, corrupción, soborno y listas restrictivas. Ninguna PARTE ofrecerá, solicitará o aceptará beneficios indebidos."),
            ("CLÁUSULA VIGÉSIMO OCTAVA. CONFIDENCIALIDAD Y TRATAMIENTO DE DATOS PERSONALES", "LAS PARTES no divulgarán ni utilizarán para fines distintos de la ejecución contractual información técnica, comercial, financiera, operativa o estratégica. Cumplirán la Ley 1581 de 2012, el Decreto 1074 de 2015 y demás normas aplicables."),
            ("CLÁUSULA VIGÉSIMO NOVENA. LIMITACIÓN DE RESPONSABILIDAD, FUERZA MAYOR Y ANEXOS", $"EL PROVEEDOR no responderá por daños indirectos, lucro cesante, interrupciones por conectividad, infraestructura del cliente, fallas eléctricas, terceros, uso indebido o eventos fuera de su control. Su responsabilidad total no excederá el valor pagado durante los doce meses anteriores al evento, salvo dolo o culpa grave. Forman parte integral el Anexo A, el Anexo B y la Orden de Servicio No. {orderNumber}."),
            ("CLÁUSULA TRIGÉSIMA. PERFECCIONAMIENTO, FIRMA FÍSICA Y ELECTRÓNICA", $"El CONTRATO se perfecciona con la firma física o electrónica de LAS PARTES. Las firmas confiables y verificables producirán los mismos efectos de la firma manuscrita. LAS PARTES declaran capacidad suficiente para obligar a las sociedades que representan. El número del presente contrato es {consecutive}.")
        };
    }

    private static string BuildAnnexA() => """
        <h1>ANEXO A - INSTALACIÓN, CUSTODIA Y SEGURIDAD OPERATIVA</h1>
        <p>Este Anexo establece las condiciones operativas aplicables a la entrega, instalación, intervención técnica, custodia, traslado y devolución de los equipos de impresión contratados mediante órdenes de servicio.</p>
        <h2>A.1. Entrega, instalación y acceso autorizado</h2>
        <p>La entrega se documentará mediante acta que indique marca, referencia, serial, contador inicial, accesorios, estado físico, ubicación y fecha de instalación.</p>
        <p>EL CLIENTE permitirá el acceso al personal identificado y autorizado por EL PROVEEDOR, sujeto a los protocolos de seguridad, ingreso y salud en el trabajo informados previamente.</p>
        <p>La configuración de impresión, digitalización, red o correo se realizará únicamente con la información, permisos y credenciales suministrados por EL CLIENTE. Las credenciales no serán conservadas más allá de lo necesario.</p>
        <h2>A.2. Custodia, uso y traslados</h2>
        <p>EL CLIENTE mantendrá el equipo en la ubicación autorizada, en condiciones eléctricas y ambientales adecuadas, y evitará la manipulación por personal no autorizado. Los traslados deberán ser informados previamente y podrán generar cargos.</p>
        <h2>A.3. Cierre, devolución y trazabilidad</h2>
        <p>Al finalizar la Orden de Servicio se realizará lectura final de contadores, inspección física, devolución de accesorios, equipos y tóneres de respaldo, y liquidación de consumos, daños o cargos pendientes. La devolución se entenderá cumplida con la recepción física y la suscripción del acta correspondiente.</p>
        """;

    private static string BuildAnnexB(string consecutive) => $"""
        <h1>ANEXO B - NIVELES DE SERVICIO (SLA)</h1>
        <p>Este Anexo define los niveles de atención aplicables a los equipos contratados bajo el Contrato Marco No. {H(consecutive)}. Los tiempos son objetivos de gestión y respuesta y dependen de repuestos, terceros, condiciones del sitio, acceso e información del cliente.</p>
        <table><thead><tr><th>Nivel</th><th>Alcance</th><th>Cobertura</th><th>Tiempo objetivo de respuesta</th></tr></thead><tbody>
          <tr><td>Crítico</td><td>Equipo fuera de servicio o falla que impide imprimir.</td><td>Diagnóstico remoto y atención presencial en Bogotá.</td><td>Contacto inicial de 1 a 2 horas. Atención presencial objetivo de 2 a 4 horas.</td></tr>
          <tr><td>Medio</td><td>Fallas parciales, calidad de impresión, escaneo o configuración.</td><td>Remoto o presencial según diagnóstico.</td><td>Respuesta objetivo de 4 a 6 horas hábiles.</td></tr>
          <tr><td>Programado</td><td>Suministro de tóner y mantenimiento preventivo.</td><td>Entrega o visita coordinada.</td><td>Tóner hasta 3 días hábiles. Preventivo según cronograma.</td></tr>
        </tbody></table>
        <h2>B.1. Canales y cobertura</h2>
        <p>Los reportes se recibirán por correo, llamada, mesa de ayuda o WhatsApp autorizado. Para zonas distintas de Bogotá, EL PROVEEDOR realizará diagnóstico inicial y coordinará la atención, recolección o traslado.</p>
        <table><thead><tr><th>Departamento</th><th>Nombre</th><th>Teléfono</th><th>Correo</th></tr></thead><tbody>
          <tr><td>Soporte TI</td><td>German Ruiz</td><td>3153625579</td><td>Germanruiz@digitaltechcolombia.com</td></tr>
          <tr><td>Soporte TI</td><td>Jeison Romero</td><td>3004159832</td><td>jromero@digitaltechcolombia.com</td></tr>
          <tr><td>Dirección Comercial</td><td>Angie Daza</td><td>3212565005</td><td>adaza@digitaltechcolombia.com</td></tr>
        </tbody></table>
        <h2>B.2. Condiciones de aplicación</h2>
        <p>Los tiempos dependen de que EL CLIENTE suministre acceso, información, fotografías, contadores, contactos, permisos y disponibilidad suficientes. No habrá incumplimiento por falta de repuestos, daños excluidos, fallas eléctricas o de conectividad, traslados no autorizados, mantenimientos programados, fuerza mayor o acciones de terceros.</p>
        """;

    private static string BuildOrderContent(
        string consecutive,
        string orderNumber,
        ContractRutExtractionDto rut,
        ContractOfferExtractionDto offer,
        int orderType,
        DateOnly creationDate,
        int durationMonths,
        string executionAddress,
        string orderObject)
    {
        var objectText = FirstNonEmpty(orderObject, BuildOrderObject(offer.EquipmentLines, orderType));
        var address = FirstNonEmpty(executionAddress, offer.ExecutionAddress, rut.MainAddress);
        var builder = new StringBuilder();
        builder.Append(BuildHeader($"ORDEN DE SERVICIO No. {orderNumber}", $"Contrato marco asociado {consecutive}"));
        builder.Append("<table class='summary'>");
        builder.Append(SummaryRow("CONTRATO MARCO ASOCIADO", consecutive));
        builder.Append(SummaryRow("CLIENTE", $"{Upper(rut.LegalName)} - NIT {FormatNit(rut.Nit, rut.VerificationDigit)}"));
        builder.Append(SummaryRow("PROVEEDOR", "DIGITAL TECH COPIERS S.A.S. - NIT 900.399.875-5"));
        builder.Append(SummaryRow("TIPO DE ORDEN", OrderTypeLabel(orderType)));
        builder.Append(SummaryRow("OBJETO DE LA ORDEN", Upper(objectText)));
        builder.Append(SummaryRow("FECHA DE CREACIÓN", creationDate.ToString("dd/MM/yyyy", ColombianCulture)));
        builder.Append(SummaryRow("FECHA DE INICIO", FirstNonEmpty(offer.StartCondition, "La fecha efectiva indicada en el acta de entrega e instalación.")));
        builder.Append(SummaryRow("DURACIÓN", $"{NumberWithWords(durationMonths, "mes", "meses")} contados desde la fecha efectiva de inicio."));
        builder.Append(SummaryRow("LUGAR DE EJECUCIÓN", Upper(address)));
        builder.Append("</table>");
        builder.Append("<h2>1. EQUIPO Y CONDICIONES ECONÓMICAS</h2>");
        builder.Append(BuildEquipmentTable(offer.EquipmentLines));
        builder.Append("<h2>2. CONDICIONES COMERCIALES PARTICULARES</h2>");
        builder.Append($"<p>Forma de pago: facturación mensual en pesos colombianos. EL CLIENTE pagará dentro de {H(NumberWithWords(offer.PaymentDays, "día calendario", "días calendario"))} siguientes a la emisión o radicación válida. El primer canon se causará desde la fecha efectiva del acta de entrega e instalación.</p>");
        builder.Append($"<p>Entrega y permanencia: los equipos serán entregados dentro de {H(NumberWithWords(offer.DeliveryBusinessDays, "día hábil", "días hábiles"))} posteriores a la aceptación de esta Orden, sujeto a disponibilidad y condiciones del sitio. La Orden podrá renovarse conforme al CONTRATO.</p>");
        builder.Append("<p>Valor de reposición: se determinará conforme a la Cláusula Décimo Novena del Contrato Marco, tomando como referencia un equipo de iguales o equivalentes características soportado mediante cotización de mercado.</p>");
        if (offer.SpecialConditions.Count > 0)
        {
            builder.Append("<ul>");
            foreach (var condition in offer.SpecialConditions.Where(static value => !string.IsNullOrWhiteSpace(value)))
                builder.Append($"<li>{H(condition)}</li>");
            builder.Append("</ul>");
        }
        builder.Append("<h2>3. SERVICIOS Y VALOR AGREGADO ASOCIADOS A LA ORDEN</h2>");
        builder.Append(BuildValueAddedTable(offer.ValueAddedServices));
        builder.Append("<h2>4. ACEPTACIÓN DE LA ORDEN DE SERVICIO</h2>");
        builder.Append($"<p>Con la firma de la presente Orden de Servicio, o con la emisión o aceptación de la orden de compra correspondiente, EL CLIENTE autoriza a EL PROVEEDOR a ejecutar lo relacionado y acepta las condiciones del Contrato Marco No. {H(consecutive)}, la oferta aprobada, la presente Orden y los anexos aplicables.</p>");
        builder.Append("<h2>FIRMAS DE LA ORDEN DE SERVICIO</h2>");
        builder.Append(BuildSignatureTable(rut));
        return builder.ToString();
    }

    private static string BuildEquipmentTable(IReadOnlyList<ContractEquipmentLineDto> lines)
    {
        var builder = new StringBuilder("<table><thead><tr><th>ÍTEM</th><th>EQUIPO / SERVICIO</th><th>CANTIDAD</th><th>VOLUMEN MENSUAL INCLUIDO</th><th>CANON MENSUAL</th><th>CLIC ADICIONAL</th></tr></thead><tbody>");
        var index = 0;
        foreach (var line in lines)
        {
            index++;
            var equipment = FirstNonEmpty(line.EquipmentOrService, string.Join(' ', new[] { line.Brand, line.Model }.Where(static value => !string.IsNullOrWhiteSpace(value))));
            var volumes = BuildVolumes(line);
            builder.Append($"<tr><td>{index}</td><td>{H(equipment)}</td><td class='number'>{line.Quantity}</td><td>{H(volumes)}</td><td class='number'>{H(FormatMoney(line.MonthlyFee, line.VatIncluded))}</td><td class='number'>{H(FormatMoney(line.AdditionalClickPrice, line.VatIncluded))}</td></tr>");
        }
        builder.Append("</tbody></table>");
        return builder.ToString();
    }

    private static string BuildValueAddedTable(IReadOnlyList<ContractValueAddedLineDto> services)
    {
        var rows = services.Count > 0 ? services : DefaultValueAddedServices();
        var builder = new StringBuilder("<table><thead><tr><th>Descripción</th><th>Alcance</th><th>Frecuencia / tiempo</th><th>Método de prestación</th></tr></thead><tbody>");
        foreach (var service in rows)
            builder.Append($"<tr><td>{H(service.Description)}</td><td>{H(service.Scope)}</td><td>{H(service.Frequency)}</td><td>{H(service.DeliveryMethod)}</td></tr>");
        builder.Append("</tbody></table>");
        return builder.ToString();
    }

    private static IReadOnlyList<ContractValueAddedLineDto> DefaultValueAddedServices() => new[]
    {
        new ContractValueAddedLineDto { Description = "Entrega e instalación", Scope = "Transporte, instalación, puesta a punto y capacitación básica.", Frequency = "Una vez al inicio.", DeliveryMethod = "Presencial." },
        new ContractValueAddedLineDto { Description = "Mantenimiento preventivo", Scope = "Limpieza, revisión, pruebas, calibración y puesta a punto.", Frequency = "Según cronograma.", DeliveryMethod = "Visita programada." },
        new ContractValueAddedLineDto { Description = "Mantenimiento correctivo", Scope = "Diagnóstico y reparación de fallas cubiertas.", Frequency = "Según reporte y SLA.", DeliveryMethod = "Remoto o presencial." },
        new ContractValueAddedLineDto { Description = "Soporte especializado", Scope = "Atención prioritaria y orientación funcional.", Frequency = "Durante la vigencia.", DeliveryMethod = "Correo, llamada, mesa de ayuda o visita." }
    };

    private static string BuildSignatureTable(ContractRutExtractionDto rut) => $"""
        <table class="signatures"><thead><tr><th>EL PROVEEDOR</th><th>EL CLIENTE</th></tr></thead><tbody><tr>
          <td><div class="signature-line"></div><strong>DIGITAL TECH COPIERS S.A.S.</strong><br>NIT 900.399.875-5<br>Representante Legal: Sebastian Ruiz Rosero<br>C.C. 1.032.470.548 de Bogotá D.C.</td>
          <td><div class="signature-line"></div><strong>{H(Upper(rut.LegalName))}</strong><br>NIT {H(FormatNit(rut.Nit, rut.VerificationDigit))}<br>Representante Legal: {H(rut.LegalRepresentativeName)}<br>Identificación: {H(rut.LegalRepresentativeId)}</td>
        </tr></tbody></table>
        """;

    private static string BuildActEquipmentTable(IReadOnlyList<ContractEquipmentLineDto> lines)
    {
        var builder = new StringBuilder("<table class='act-grid equipment'><thead><tr><th>Ítem</th><th>Equipo</th><th>Cant.</th><th>Marca / modelo</th><th>Serial / activo</th><th>Contadores iniciales</th><th>IP / MAC</th></tr></thead><tbody>");
        var index = 0;
        foreach (var line in lines)
        {
            index++;
            builder.Append($"<tr><td>{index}</td><td>{H(FirstNonEmpty(line.EquipmentOrService, line.ColorMode))}</td><td>{line.Quantity}</td><td>{H(string.Join(' ', new[] { line.Brand, line.Model }.Where(static value => !string.IsNullOrWhiteSpace(value))))}</td><td>Serial: __________________<br>Activo: __________________</td><td>Impresión: ______________<br>Digitalización: __________</td><td>IP: _____________________<br>MAC: ____________________</td></tr>");
        }
        builder.Append("</tbody></table>");
        return builder.ToString();
    }

    private static string BuildAccessoriesTable()
    {
        var items = new[] { "Cable de alimentación", "Cable de red / USB", "Bandejas y accesorios del equipo", "Tóner instalado", "Tóner de respaldo", "Otros: ___________________________" };
        var builder = new StringBuilder("<table class='check'><thead><tr><th>Ítem</th><th>Descripción</th><th>Sí</th><th>No</th><th>N/A</th><th>Estado / observaciones</th></tr></thead><tbody>");
        for (var index = 0; index < items.Length; index++)
            builder.Append($"<tr><td>{index + 1}</td><td>{H(items[index])}</td><td>[ ]</td><td>[ ]</td><td>[ ]</td><td></td></tr>");
        builder.Append("</tbody></table>");
        return builder.ToString();
    }

    private static string BuildVerificationTable()
    {
        var items = new[]
        {
            "Estado físico general conforme y sin daños visibles.", "Equipo instalado en la ubicación autorizada.",
            "Conexión eléctrica y protección de voltaje verificadas.", "Conectividad de red / IP configurada.",
            "Controladores de impresión instalados y probados.", "Prueba de impresión simple y dúplex satisfactoria.",
            "Prueba de escaneo satisfactoria.", "Configuración de correo/SMTP realizada, cuando aplica.",
            "Lecturas iniciales de contadores verificadas.", "Capacitación básica de uso impartida al responsable."
        };
        var builder = new StringBuilder("<table class='check'><thead><tr><th>Ítem</th><th>Verificación</th><th>Sí</th><th>No</th><th>N/A</th><th>Observaciones</th></tr></thead><tbody>");
        for (var index = 0; index < items.Length; index++)
            builder.Append($"<tr><td>{index + 1}</td><td>{H(items[index])}</td><td>[ ]</td><td>[ ]</td><td>[ ]</td><td></td></tr>");
        builder.Append("</tbody></table>");
        return builder.ToString();
    }

    private static string BuildActSignatureTable() => """
        <table class="signatures act-signatures"><thead><tr><th>ENTREGADO POR - EL PROVEEDOR</th><th>RECIBIDO POR - EL CLIENTE</th></tr></thead><tbody>
          <tr><td>Nombre: _______________________________<br>Cargo: ___________________________________<br>C.C.: ____________________________________<br>Fecha y hora: _____________________________<br>Correo / teléfono: _________________________<br><br>Firma: ___________________________________</td>
              <td>Nombre: _______________________________<br>Cargo: ___________________________________<br>C.C.: ____________________________________<br>Fecha y hora: _____________________________<br>Correo / teléfono: _________________________<br><br>Firma: ___________________________________</td></tr>
        </tbody></table>
        """;

    private static ContractDocumentArtifact BuildWordArtifact(string fileName, string title, string body, bool actStyle = false)
    {
        var html = $$"""
            <!DOCTYPE html>
            <html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:w="urn:schemas-microsoft-com:office:word" lang="es">
            <head>
              <meta charset="utf-8">
              <meta name="ProgId" content="Word.Document">
              <title>{{H(title)}}</title>
              <style>
                @page { size: 8.5in 11in; margin: .58in .58in .55in .58in; }
                body { font-family: "Times New Roman", serif; font-size: 10.5pt; line-height: 1.16; color: #1f1f1f; }
                h1 { font-size: 14pt; line-height: 1.05; color: #142b4a; margin: 0 0 12pt; text-transform: uppercase; }
                h2 { font-size: 11pt; color: #142b4a; margin: 12pt 0 5pt; page-break-after: avoid; }
                p { margin: 0 0 7pt; text-align: justify; }
                table { width: 100%; border-collapse: collapse; margin: 7pt 0 12pt; font-size: 9.5pt; page-break-inside: auto; }
                th, td { border: 1px solid #8b98a8; padding: 4pt 5pt; vertical-align: middle; }
                th { background: #142b4a; color: #fff; font-weight: bold; text-align: center; }
                .summary td:first-child { width: 36%; background: #eef3f8; color: #142b4a; font-weight: bold; }
                .document-header { border-bottom: 3px solid #00afc1; margin-bottom: 14pt; padding: 10pt 0 8pt; }
                .document-header .brand { color: #142b4a; font-family: Arial, sans-serif; font-size: 18pt; font-weight: bold; letter-spacing: -.5pt; }
                .document-header .subtitle { color: #00afc1; font: bold 9pt Arial, sans-serif; text-transform: uppercase; margin-top: 3pt; }
                .page-break { page-break-before: always; }
                .signatures td { height: 110pt; vertical-align: bottom; width: 50%; }
                .signature-line { border-top: 1px solid #333; width: 75%; margin: 0 0 5pt; }
                .number { text-align: right; white-space: nowrap; }
                .act-header { display: table; width: 100%; border-bottom: 3px solid #00afc1; margin-bottom: 8pt; padding-bottom: 7pt; }
                .act-header .brand, .act-header .act-title { display: table-cell; vertical-align: middle; }
                .act-header .brand { width: 32%; font: bold 17pt Arial, sans-serif; color: #142b4a; }
                .act-header .brand small { display: block; font: normal 8pt Arial, sans-serif; }
                .act-header .act-title { text-align: right; font: bold 11pt Arial, sans-serif; color: #142b4a; }
                .act-header .act-title span { color: #00afc1; }
                .act { font-family: Arial, sans-serif; font-size: 8pt; line-height: 1.03; }
                .act p { margin-bottom: 3pt; }
                .act table { font-size: 7.2pt; margin: 4pt 0 6pt; }
                .act th, .act td { padding: 2pt 3pt; }
                .act h2 { font-size: 8.5pt; margin: 6pt 0 3pt; }
                .act-section { border-bottom: 2px solid #00afc1; margin-top: 11pt; }
                .act-grid th { background: #eef3f8; color: #142b4a; text-align: left; }
                .act-grid td { min-height: 18pt; }
                .equipment { font-size: 8.5pt; }
                .check th { background: #142b4a; }
                .check td:nth-child(1), .check td:nth-child(3), .check td:nth-child(4), .check td:nth-child(5) { text-align: center; }
                .notes td { height: 26pt; }
                .declarations { margin-top: 2pt; padding-left: 20pt; }
                .declarations li { margin-bottom: 4pt; text-align: justify; }
                .muted { color: #667085; }
                .act-signatures { page-break-inside: avoid; }
                .act-signatures tr { page-break-inside: avoid; }
                .act-signatures td { height: 70pt; vertical-align: top; line-height: 1.2; }
              </style>
            </head>
            <body class="{{(actStyle ? "act" : "contract")}}">{{body}}</body>
            </html>
            """;
        return new ContractDocumentArtifact
        {
            FileName = fileName,
            ContentType = "application/msword",
            Content = Encoding.UTF8.GetBytes(html)
        };
    }

    private static string BuildHeader(string title, string subtitle) => $"""
        <div class="document-header"><div class="brand">DIGITAL TECH</div><h1>{H(title)}</h1><div class="subtitle">{H(subtitle)}</div></div>
        """;

    private static string SummaryRow(string label, string value) =>
        $"<tr><td>{H(label)}</td><td>{H(value)}</td></tr>";

    private static string BuildVolumes(ContractEquipmentLineDto line)
    {
        var parts = new List<string>();
        if (line.IncludedPrints > 0) parts.Add($"{line.IncludedPrints:N0} impresiones");
        if (line.IncludedScans > 0) parts.Add($"{line.IncludedScans:N0} digitalizaciones");
        return parts.Count == 0 ? line.Notes : string.Join(" y ", parts);
    }

    private static string FormatMoney(decimal value, bool vatIncluded)
    {
        if (value <= 0) return "Por definir";
        return $"${value.ToString("N0", ColombianCulture)} {(vatIncluded ? "IVA incluido" : "+ IVA")}";
    }

    private static string BuildOrderObject(IReadOnlyList<ContractEquipmentLineDto> lines, int orderType)
    {
        var label = OrderTypeLabel(orderType).ToUpperInvariant();
        var details = string.Join("; ", lines.Select(line => $"{line.Quantity} {FirstNonEmpty(line.EquipmentOrService, line.Brand + " " + line.Model)}"));
        return $"{label} DE {details}, CON LOS SERVICIOS ASOCIADOS DEFINIDOS EN ESTA ORDEN.";
    }

    private static string OrderTypeLabel(int value) => value switch
    {
        ContractOptionValues.OrderAddition => "Adición",
        ContractOptionValues.OrderRemoval => "Retiro",
        ContractOptionValues.OrderRelocation => "Traslado",
        ContractOptionValues.OrderReplacement => "Reemplazo",
        _ => "Orden inicial"
    };

    private static string NumberWithWords(int value, string singular, string plural)
    {
        var word = value switch
        {
            1 => "uno", 2 => "dos", 3 => "tres", 4 => "cuatro", 5 => "cinco", 6 => "seis",
            7 => "siete", 8 => "ocho", 9 => "nueve", 10 => "diez", 11 => "once", 12 => "doce",
            15 => "quince", 30 => "treinta", _ => value.ToString(CultureInfo.InvariantCulture)
        };
        return $"{word} ({value}) {(value == 1 ? singular : plural)}";
    }

    private static string FormatLongDate(DateOnly date)
    {
        var month = ColombianCulture.DateTimeFormat.GetMonthName(date.Month);
        return $"{date.Day} días del mes de {month} de {date.Year}";
    }

    private static string FormatNit(string nit, string verificationDigit)
    {
        var digits = new string((nit ?? "").Where(char.IsDigit).ToArray());
        var formatted = digits.Length > 0 && long.TryParse(digits, out var numeric)
            ? numeric.ToString("N0", ColombianCulture)
            : nit ?? "";
        return string.IsNullOrWhiteSpace(verificationDigit) ? formatted : $"{formatted}-{verificationDigit.Trim().TrimStart('-')}";
    }

    private static string Upper(string value) => (value ?? "").Trim().ToUpper(ColombianCulture);
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}
