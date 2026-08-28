using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services;

public static class DashboardAgentTableDirectory
{
    public static DashboardAgentTableDirectoryDto Build()
    {
        var seeds = new List<DashboardAgentTableDirectoryItemDto>
        {
            Table(
                "Calculator",
                "Productos y costos de cotizacion",
                "Productos Cloud",
                "cr07a_precioscloud",
                "cr07a_preciosclouds",
                "Catalogo de productos, costos de compra, precios sugeridos y aceleradores usados por la cotizacion y utilidad teorica.",
                "utility",
                Fields("cr07a_priceableitemdescription", "cr07a_purchaseprice", "cr07a_suggestedretailprice", "cr07a_acelerador"),
                Terms("producto", "productos", "precio", "costo", "catalogo", "cotizacion", "acelerador", "utilidad teorica", "cloud"),
                Related("cr07a_salesperformancerecord", "cr07a_facturacion", "cr07a_consumointcomex")),

            Table(
                "Clientes",
                "Clientes lookup y reportes",
                "Clientes",
                "cr07a_cliente",
                "cr07a_clientes",
                "Directorio maestro de clientes usado en cotizaciones, facturacion, copiers, soporte cloud, reportes y licenciamiento.",
                "generic",
                Fields("cr07a_nombre", "cr07a_nit", "cr07a_nombrepersonaacargo", "cr07a_correoelectronico"),
                Terms("cliente", "clientes", "nit", "empresa", "contacto", "responsable", "correo", "reporte"),
                Related("cr07a_facturacion", "cr07a_salesperformancerecord", "cr07a_ticket", "cr07a_consumointcomex", "cr07a_productoscopiers")),

            Table(
                "Calculator",
                "Escenarios guardados",
                "Escenarios comerciales",
                "cr07a_negocioscomerciales",
                "cr07a_negocioscomercialeses",
                "Escenarios de cotizacion guardados por usuario con lineas, resultado calculado, deal type y fechas de inicio/fin.",
                "generic",
                Fields("cr07a_name", "cr07a_scenarioid", "cr07a_scenarioname", "cr07a_dealtype", "cr07a_requiresproration", "cr07a_startdate", "cr07a_enddate", "cr07a_linesjson", "cr07a_lastresultjson", "cr07a_systemuserid", "cr07a_displayname", "cr07a_email"),
                Terms("escenario", "cotizacion", "quote", "deal", "lineas", "resultado", "usuario", "prorrata"),
                Related("cr07a_cliente", "cr07a_precioscloud")),

            Table(
                "Renovaciones",
                "Tablero y actualizacion de renovaciones",
                "Contratos y renovaciones",
                "cr07a_salesperformancerecord",
                "cr07a_salesperformancerecords",
                "Contratos comerciales, renovaciones, producto, linea, vertical, tipo de contrato, cliente, cantidad y valor de venta unitario en USD.",
                "business",
                Fields("cr07a_icpname", "cr07a_fecharenovacion", "cr07a_quantity", "cr07a_valorventaunidadusd", "cr07a_billingday", "cr07a_sitieneiva", "cr07a_facturableautomatico", "cr07a_productline", "cr07a_contracttype", "cr07a_clientelookup", "cr07a_producto"),
                Terms("contrato", "contratos", "renovacion", "renovaciones", "cliente", "producto", "vertical", "monthly", "prepaid", "facturable", "valor mensual", "valor anual"),
                Related("cr07a_cliente", "cr07a_precioscloud", "cr07a_facturacion", "cr07a_contractrecord1")),

            Table(
                "Puntajes",
                "Verificacion, cierre y movimientos",
                "Puntajes comerciales",
                "cr07a_contractrecord1",
                "cr07a_contractrecord1s",
                "Registros de scoring comercial, comisiones, cierre mensual, vendedor, cliente, oferta, linea, vertical y tipo de contrato.",
                "generic",
                Fields("cr07a_contractstartdate", "cr07a_score", "cr07a_aprovisionamientodetallelargo", "cr07a_description", "cr07a_commission", "cr07a_cliente", "cr07a_vendedor", "cr07a_oferta", "cr07a_verificado", "cr07a_esprimercontratoconelcliente", "cr07a_tipodecontrato", "cr07a_linea", "cr07a_vertical", "cr07a_adicionales"),
                Terms("puntaje", "score", "comision", "comisiones", "vendedor", "oferta", "verificado", "primer contrato", "cierre"),
                Related("cr07a_salesperformancerecord", "cr07a_cliente", "cr07a_empleado")),

            Table(
                "LiquidacionNominas/RH/Permissions",
                "Empleados base, permisos y usuarios",
                "Empleados",
                "cr07a_empleado",
                "cr07a_empleados",
                "Maestro de empleados con salario mensual, auxilios, factores comerciales, usuario, tipo de contrato, correo y modulos permitidos.",
                "payroll",
                Fields("cr07a_nombrecompleto", "cr07a_sueldomensual", "cr07a_auxconectividad", "cr07a_topecomisional", "cr07a_factorcopiers", "cr07a_factorcloud", "cr07a_usuario", "cr07a_tipocontrato", "cr07a_modulos", "cr07a_correo"),
                Terms("empleado", "empleados", "trabajador", "usuario", "correo", "salario", "sueldo", "modulos", "permisos", "nomina"),
                Related("cr07a_nomina", "cr07a_gastodelaempresa", "cr07a_cuentasdecobro", "systemuser")),

            Table(
                "LiquidacionNominas",
                "Nominas generadas",
                "Nomina",
                "cr07a_nomina",
                "cr07a_nominas",
                "Pagos de nomina generados por empleado, periodo y fecha de pago. Incluye base salarial, auxilio, dias, devengos, deducciones, comisiones y cuenta de cobro.",
                "payroll",
                Fields("cr07a_numerodenomina", "cr07a_name", "cr07a_idempleado", "cr07a_fechapago", "cr07a_sueldobase", "cr07a_auxilio", "cr07a_diasdelmes", "cr07a_diastrabajados", "cr07a_diasnotrabajados", "cr07a_motivodiasnotrabajados", "cr07a_valordiasnotrabajados", "cr07a_bonocumplimiento", "cr07a_comisionescopiers", "cr07a_comisionescloud", "cr07a_comisiones", "cr07a_sueldobruto", "cr07a_salud", "cr07a_pension", "cr07a_otrasdeducciones", "cr07a_prestamo", "cr07a_cuentadecobro", "cr07a_retencionenlafuentenomina", "cr07a_retencionenlafuenteexterno", "cr07a_montopagado", "cr07a_montopagadocuentadecobro"),
                Terms("nomina", "pago empleado", "salario", "sueldo", "devengado", "deduccion", "comision", "cuenta de cobro", "empleado", "pago"),
                Related("cr07a_empleado", "cr07a_gastodelaempresa", "cr07a_cuentasdecobro")),

            Table(
                "RH/GestionHumana",
                "Solicitudes de vacaciones",
                "Vacaciones",
                "cr07a_solicituddevacaciones",
                "cr07a_solicituddevacacioneses",
                "Solicitudes de vacaciones por empleado con fechas de inicio/fin, dias solicitados y formato adjunto.",
                "generic",
                Fields("cr07a_numerodesolicitud", "cr07a_idempleado", "cr07a_fechainicio", "cr07a_fechafin", "cr07a_cantidaddedias", "cr07a_formato", "cr07a_formato_name"),
                Terms("vacaciones", "solicitud", "dias", "empleado", "formato", "gestion humana", "rh"),
                Related("cr07a_empleado")),

            Table(
                "RH",
                "Incapacidades",
                "Incapacidades",
                "cr07a_incapacidad",
                "cr07a_incapacidads",
                "Incapacidades de empleados con fecha inicial, fecha final, motivo y adjunto.",
                "generic",
                Fields("cr07a_numerodeincapacidad", "cr07a_idempleado", "cr07a_fechainicio", "cr07a_fechafin", "cr07a_motivo", "cr07a_adjuntarincapacidad", "cr07a_adjuntarincapacidad_name"),
                Terms("incapacidad", "incapacidades", "empleado", "motivo", "ausencia", "rh"),
                Related("cr07a_empleado")),

            Table(
                "PortalProveedores/Conciliacion/P&L",
                "Gastos proveedores y empresa",
                "Gastos Digital Tech",
                "cr07a_gastodelaempresa",
                "cr07a_gastodelaempresas",
                "Gastos, compras, pagos a proveedores o terceros, documentos DIAN, clasificacion contable, IVA, retenciones, valores Cloud/Copiers y soporte para P&L.",
                "expenses",
                Fields("createdon", "cr07a_name", "cr07a_numerofactura", "cr07a_factura", "cr07a_fechaemision", "cr07a_fechadeemision", "cr07a_fechadepago", "cr07a_valorpago", "cr07a_total", "cr07a_totalfactura", "cr07a_totalantesdeiva", "cr07a_base", "cr07a_iva", "cr07a_ivavalor", "cr07a_retefuente", "cr07a_reteica", "cr07a_ica", "cr07a_nombreemisor", "cr07a_nitemisor", "cr07a_nombreproveedor", "cr07a_nitproveedor", "cr07a_nombrereceptor", "cr07a_nitreceptor", "cr07a_cloud", "cr07a_copiers", "cr07a_categoria", "cr07a_cuentacontablecodigo", "cr07a_cuentacontablenombre", "cr07a_estadoautomatizacion", "cr07a_motivorevision", "cr07a_fuenteautomatizacion", "cr07a_claveexcel", "cr07a_descripcion", "cr07a_concepto", "cr07a_detalle", "cr07a_observaciones"),
                Terms("gasto", "gastos", "compra", "compras", "proveedor", "proveedores", "beneficiario", "tercero", "emisor", "receptor", "pago proveedor", "nomina externa", "cuenta de cobro", "iva descontable", "retefuente", "rete ica", "p&l", "costo"),
                Related("cr07a_nomina", "cr07a_empleado", "cr07a_cuentasdecobro", "cr07a_movimientobancario", "cr07a_pnlmanualitem")),

            Table(
                "Dashboard/RegistroPagosClientes",
                "Facturacion y pagos",
                "Facturacion",
                "cr07a_facturacion",
                "cr07a_facturacions",
                "Facturas emitidas a clientes, cartera, vencimientos, pagos, retenciones, diferencia, vertical y tipo de contrato. Los importes analiticos se netean con las notas credito relacionadas.",
                "billing",
                Fields("cr07a_name", "cr07a_fechadeemision", "cr07a_nitempresa", "cr07a_clientenit", "cr07a_vertical", "cr07a_tipocontrato", "cr07a_fechavencimiento", "cr07a_totalfactura", "cr07a_iva", "cr07a_ivavalor", "cr07a_impuestovalor", "cr07a_publicurl", "cr07a_fechadepago", "cr07a_valorpago", "cr07a_reteica", "cr07a_reteicavalor", "cr07a_reteivavalor", "cr07a_rteivavalor", "cr07a_retefuentevalor", "cr07a_rteftevalor", "cr07a_diferencia"),
                Terms("factura", "facturas", "facturacion", "cliente", "cartera", "recaudo", "pago cliente", "pendiente", "vencida", "vencimiento", "retencion", "iva", "vertical", "tipo contrato"),
                Related("cr07a_cliente", "cr07a_siigonotacredito", "cr07a_salesperformancerecord", "cr07a_cruceflujocaja", "cr07a_movimientobancario", "cr07a_consumointcomex")),

            Table(
                "Dashboard/RegistroPagosClientes",
                "Facturacion y pagos",
                "Notas credito SIIGO",
                "cr07a_siigonotacredito",
                "cr07a_siigonotacreditos",
                "Notas credito aceptadas sincronizadas desde SIIGO, con la referencia segura a su factura, fecha, total e IVA.",
                "billing",
                Fields("cr07a_name", "cr07a_siigocreditnoteid", "cr07a_siigocreditnotename", "cr07a_siigocreditnotenumber", "cr07a_siigoinvoiceid", "cr07a_siigoinvoicename", "cr07a_siigoinvoicenumber", "cr07a_siigoinvoiceprefix", "cr07a_fechanotacredito", "cr07a_totalnotacredito", "cr07a_valorivanotacredito", "cr07a_clienteidentificacion", "cr07a_stampstatus", "cr07a_facturaciondataverseid", "cr07a_matchfacturacionpor", "cr07a_procesada"),
                Terms("nota credito", "notas credito", "devolucion", "anulacion", "facturacion neta", "iva nota credito", "saldo neto"),
                Related("cr07a_facturacion", "cr07a_cliente")),

            Table(
                "Dashboard/Copiers",
                "Lineas de producto copiers",
                "Lineas Copiers",
                "cr07a_productoscopiers",
                "cr07a_productoscopiers",
                "Lineas comerciales de Copiers por cliente, producto, cantidad, valores unitarios y operaciones incluidas.",
                "copiers",
                Fields("cr07a_producto", "cr07a_cantidad", "cr07a_valorunidadantesdeiva", "cr07a_diadefacturacion", "cr07a_operacionesincluidas", "cr07a_cliente", "cr07a_valorunidadconiva", "cr07a_totalconiva"),
                Terms("copiers", "copier", "impresion", "linea copiers", "operaciones", "cliente", "excedentes", "valor unidad"),
                Related("cr07a_cliente", "cr07a_equipo", "cr07a_contadoresmensualesequipo", "cr07a_asignacionequipolineacopiers")),

            Table(
                "Dashboard/Copiers",
                "Asignacion equipo-linea",
                "Asignacion equipo-linea Copiers",
                "cr07a_asignacionequipolineacopiers",
                "cr07a_asignacionequipolineacopierses",
                "Relacion entre cliente, linea de producto Copiers y equipo asignado.",
                "copiers",
                Fields("cr07a_name", "cr07a_cliente", "cr07a_lineaproductocopiers", "cr07a_equipo"),
                Terms("asignacion", "equipo", "linea", "copiers", "cliente"),
                Related("cr07a_productoscopiers", "cr07a_equipo", "cr07a_cliente")),

            Table(
                "Copiers",
                "Equipos",
                "Equipos Copiers",
                "cr07a_equipo",
                "cr07a_equipos",
                "Inventario de equipos Copiers, cliente actual, serial, categoria, referencia, ubicacion, valor comercial y estado.",
                "copiers",
                Fields("cr07a_nombredelequipo", "cr07a_cliente", "cr07a_serial", "cr07a_categoriadeequipo", "cr07a_referencia", "cr07a_observaciones", "cr07a_area", "cr07a_sede", "cr07a_valorcomercial", "cr07a_estadodelequipo"),
                Terms("equipo", "equipos", "copiers", "serial", "inventario", "ubicacion", "sede", "area", "estado"),
                Related("cr07a_cliente", "cr07a_movimientosequipos", "cr07a_mantenimiento", "cr07a_contadores")),

            Table(
                "Copiers",
                "Movimientos de equipos",
                "Movimientos Copiers",
                "cr07a_movimientosequipos",
                "cr07a_movimientosequiposes",
                "Historial de movimientos de equipos Copiers por equipo, cliente y fecha.",
                "copiers",
                Fields("cr07a_name", "cr07a_equipo", "cr07a_cliente", "cr07a_fecha"),
                Terms("movimiento", "movimientos", "equipo", "copiers", "cliente", "fecha"),
                Related("cr07a_equipo", "cr07a_cliente")),

            Table(
                "Copiers",
                "Mantenimientos",
                "Mantenimientos Copiers",
                "cr07a_mantenimiento",
                "cr07a_mantenimientos",
                "Mantenimientos de equipos Copiers con equipo, fecha, descripcion, cliente, tipo, estado y acta adjunta.",
                "copiers",
                Fields("cr07a_mantenimiento1", "cr07a_iddeequipo", "cr07a_fechademantenimiento", "cr07a_descripciondelmantenimiento", "cr07a_cliente", "cr07a_actadeentregadeservicio", "cr07a_actadeentregadeservicio_name", "cr07a_tipodemantenimiento", "cr07a_estadodelmantenimiento"),
                Terms("mantenimiento", "mantenimientos", "equipo", "copiers", "acta", "servicio", "cliente"),
                Related("cr07a_equipo", "cr07a_cliente")),

            Table(
                "Copiers",
                "Contadores formulario",
                "Contadores Copiers",
                "cr07a_contadores",
                "cr07a_contadoreses",
                "Lecturas de contadores de equipos Copiers, paginas copiadas, escaneos y soporte de pagina de estado.",
                "copiers",
                Fields("cr07a_equipo", "cr07a_fechadetomadecontador", "cr07a_maquina", "cr07a_contador", "cr07a_contadorescaner", "cr07a_paginadeestado", "cr07a_paginadeestado_name"),
                Terms("contador", "contadores", "lectura", "copias", "escaneos", "equipo", "copiers"),
                Related("cr07a_equipo", "cr07a_contadoresmensualesequipo")),

            Table(
                "Dashboard/Copiers",
                "Contadores mensuales lectura",
                "Contadores mensuales Copiers",
                "cr07a_contadoresmensualesequipo",
                "cr07a_contadoresmensualesequipos",
                "Lectura mensual consolidada de contadores por equipo, contador de paginas y paginas escaneadas.",
                "copiers",
                Fields("cr07a_dt_fechalectura", "cr07a_equipo", "cr07a_dt_contadorpaginas", "cr07a_dt_paginasescaneadas"),
                Terms("contador mensual", "lectura mensual", "copias", "escaneos", "excedentes", "copiers"),
                Related("cr07a_equipo", "cr07a_productoscopiers")),

            Table(
                "Copiers/Inventario",
                "Suministros",
                "Suministros Copiers",
                "cr07a_suministro",
                "cr07a_suministros",
                "Inventario de suministros Copiers con nombre, cantidad, fecha de compra y estado.",
                "copiers",
                Fields("cr07a_nombredelsuministro", "cr07a_cantidad", "cr07a_fechadecompra", "cr07a_estadodelsuministro"),
                Terms("suministro", "suministros", "inventario", "toner", "cantidad", "compra", "copiers"),
                Related("cr07a_facturasproveedorescopiers", "cr07a_entrega")),

            Table(
                "Copiers/Inventario",
                "Facturas proveedor copiers",
                "Facturas proveedor Copiers",
                "cr07a_facturasproveedorescopiers",
                "cr07a_facturasproveedorescopiers",
                "Facturas de proveedor asociadas a suministros Copiers, cantidades, valor unitario y aprobacion de ingreso.",
                "copiers",
                Fields("cr07a_name", "cr07a_suministro", "cr07a_cantidad", "cr07a_valorunitarioantesdeiva", "cr07a_aprobadoeingresado"),
                Terms("factura proveedor", "proveedor copiers", "suministro", "compra", "aprobado", "inventario"),
                Related("cr07a_suministro", "cr07a_gastodelaempresa")),

            Table(
                "Copiers",
                "Entregas",
                "Entregas Copiers",
                "cr07a_entrega",
                "cr07a_entregas",
                "Entregas de suministros a clientes con fecha, cantidad, estado y comprobante.",
                "copiers",
                Fields("cr07a_entrega1", "cr07a_iddecliente", "cr07a_iddesuministro", "cr07a_fechadeentrega", "cr07a_cantidadentregada", "cr07a_estadodeentrega", "cr07a_comprobantedeentrega", "cr07a_comprobantedeentrega_name"),
                Terms("entrega", "entregas", "suministro", "cliente", "comprobante", "copiers"),
                Related("cr07a_cliente", "cr07a_suministro")),

            Table(
                "Envios/Transportador",
                "Envios",
                "Envios",
                "cr07a_envio",
                "cr07a_envios",
                "Solicitudes y seguimiento de envios con origen, destino, cliente, contenido, receptor, estado, transportador, flete y acta.",
                "generic",
                Fields("cr07a_name", "cr07a_origen", "cr07a_destino", "cr07a_cliente", "cr07a_queseenvia", "cr07a_observaciones", "cr07a_quienrecibe", "cr07a_telefonorecibe", "cr07a_estado", "cr07a_fechaprogramada", "cr07a_transportador", "cr07a_valorflete", "cr07a_recogidaaprobada", "cr07a_actaentrega", "cr07a_actaentrega_name"),
                Terms("envio", "envios", "transportador", "flete", "recogida", "entrega", "cliente", "acta"),
                Related("cr07a_cliente")),

            Table(
                "SoporteCloud/Reportes",
                "Tickets",
                "Tickets soporte Cloud",
                "cr07a_ticket",
                "cr07a_tickets",
                "Tickets de soporte Cloud por cliente, fecha, estado, tipo, categoria, metodo, horas tomadas y solucion.",
                "support-cloud",
                Fields("cr07a_tituloticket", "cr07a_descripcion", "cr07a_fechacreacion", "cr07a_estado", "cr07a_tipo", "cr07a_cliente", "cr07a_categoria", "cr07a_horastomadas", "cr07a_metodo", "cr07a_solucion"),
                Terms("ticket", "tickets", "soporte", "cloud", "caso", "incidente", "cliente", "horas", "solucion"),
                Related("cr07a_cliente", "cr07a_m365generatedreport")),

            Table(
                "SoporteCloud",
                "Capacitaciones",
                "Capacitaciones Cloud",
                "cr07a_capacitacion",
                "cr07a_capacitacions",
                "Capacitaciones realizadas por cliente, fecha, duracion, asistentes, tema y propietario.",
                "support-cloud",
                Fields("cr07a_temacapacitacion", "cr07a_cliente", "cr07a_fecha", "cr07a_duracionhoras", "cr07a_cantidadasistentes", "cr07a_tema", "ownerid"),
                Terms("capacitacion", "capacitaciones", "entrenamiento", "cliente", "asistentes", "duracion", "tema", "soporte cloud"),
                Related("cr07a_cliente", "cr07a_capacitaciontema")),

            Table(
                "SoporteCloud/Encuestas",
                "Temas encuesta",
                "Temas de capacitacion",
                "cr07a_capacitaciontema",
                "cr07a_capacitaciontemas",
                "Temas configurables para encuestas y capacitaciones.",
                "support-cloud",
                Fields("cr07a_name", "cr07a_descripcion", "cr07a_activo"),
                Terms("tema", "temas", "encuesta", "capacitacion", "activo"),
                Related("cr07a_capacitacionpregunta", "cr07a_capacitacionsesion")),

            Table(
                "SoporteCloud/Encuestas",
                "Preguntas encuesta",
                "Preguntas de encuesta",
                "cr07a_capacitacionpregunta",
                "cr07a_capacitacionpreguntas",
                "Preguntas de encuesta asociadas a tema, componente, tipo de respuesta, orden y puntaje maximo.",
                "support-cloud",
                Fields("cr07a_name", "cr07a_tema", "cr07a_componente", "cr07a_tiporespuesta", "cr07a_pregunta", "cr07a_orden", "cr07a_puntajemaximo", "cr07a_activa"),
                Terms("pregunta", "preguntas", "encuesta", "puntaje", "componente", "respuesta"),
                Related("cr07a_capacitaciontema", "cr07a_capacitacionopcion", "cr07a_capacitacionrespuesta")),

            Table(
                "SoporteCloud/Encuestas",
                "Opciones encuesta",
                "Opciones de encuesta",
                "cr07a_capacitacionopcion",
                "cr07a_capacitacionopcions",
                "Opciones de respuesta para preguntas de encuesta, con puntos, orden y marca de correcta.",
                "support-cloud",
                Fields("cr07a_name", "cr07a_pregunta", "cr07a_opcion", "cr07a_escorrecta", "cr07a_puntos", "cr07a_orden", "cr07a_activa"),
                Terms("opcion", "opciones", "respuesta", "encuesta", "correcta", "puntos"),
                Related("cr07a_capacitacionpregunta")),

            Table(
                "SoporteCloud/Encuestas",
                "Sesiones encuesta",
                "Sesiones de encuesta",
                "cr07a_capacitacionsesion",
                "cr07a_capacitacionsesions",
                "Sesiones de encuesta por tema y cliente, con codigo publico, fecha, estado y cierre.",
                "support-cloud",
                Fields("cr07a_name", "cr07a_tema", "cr07a_cliente", "cr07a_fecha", "cr07a_codigo", "cr07a_estado", "cr07a_cerradaen"),
                Terms("sesion", "sesiones", "encuesta", "codigo", "cliente", "cerrada", "capacitacion"),
                Related("cr07a_capacitaciontema", "cr07a_capacitacionparticipante", "cr07a_capacitacionrespuesta", "cr07a_cliente")),

            Table(
                "SoporteCloud/EncuestasPublicas",
                "Participantes",
                "Participantes de encuesta",
                "cr07a_capacitacionparticipante",
                "cr07a_capacitacionparticipantes",
                "Participantes de encuestas publicas con email, empresa, puntaje, porcentaje y fecha de respuesta.",
                "support-cloud",
                Fields("cr07a_name", "cr07a_sesion", "cr07a_email", "cr07a_empresa", "cr07a_puntaje", "cr07a_puntajemaximo", "cr07a_porcentaje", "cr07a_respondidaen"),
                Terms("participante", "participantes", "encuesta", "puntaje", "porcentaje", "empresa", "email"),
                Related("cr07a_capacitacionsesion", "cr07a_capacitacionrespuesta")),

            Table(
                "SoporteCloud/EncuestasPublicas",
                "Respuestas",
                "Respuestas de encuesta",
                "cr07a_capacitacionrespuesta",
                "cr07a_capacitacionrespuestas",
                "Respuestas de participantes en encuestas, pregunta, opcion, componente, puntos, valor numerico y texto.",
                "support-cloud",
                Fields("cr07a_name", "cr07a_sesion", "cr07a_participante", "cr07a_pregunta", "cr07a_opcion", "cr07a_componente", "cr07a_puntos", "cr07a_puntajemaximo", "cr07a_correcta", "cr07a_valornumerico", "cr07a_respuestatexto", "cr07a_respondidaen"),
                Terms("respuesta", "respuestas", "encuesta", "participante", "puntaje", "texto", "valor numerico"),
                Related("cr07a_capacitacionsesion", "cr07a_capacitacionparticipante", "cr07a_capacitacionpregunta", "cr07a_capacitacionopcion")),

            Table(
                "SoporteCloud/M365",
                "Conexiones tenant",
                "Conexiones M365",
                "cr07a_m365tenantconnection",
                "cr07a_m365tenantconnections",
                "Conexiones tenant M365 por cliente, tenant id, estado de conexion, permisos, consentimiento, pruebas y errores.",
                "support-cloud",
                Fields("cr07a_name", "cr07a_cliente", "cr07a_clienteidinterno", "cr07a_tenantid", "cr07a_tenanthint", "cr07a_estadoconexion", "cr07a_fechaconexion", "cr07a_permisossolicitados", "cr07a_resultadoconsentimiento", "cr07a_adminconsent", "cr07a_scopeconsentido", "cr07a_error", "cr07a_errordescripcion", "cr07a_fechaultimaprueba", "cr07a_ultimapruebaexitosa", "cr07a_resultadoultimaprueba"),
                Terms("m365", "tenant", "conexion", "consentimiento", "permisos", "cliente", "seguridad", "graph"),
                Related("cr07a_cliente", "cr07a_m365securitysnapshot", "cr07a_m365generatedreport")),

            Table(
                "SoporteCloud/M365",
                "Snapshots seguridad",
                "Snapshots seguridad M365",
                "cr07a_m365securitysnapshot",
                "cr07a_m365securitysnapshots",
                "Snapshots de seguridad M365 por cliente y periodo: secure score, alertas, incidentes, recomendaciones y estado de consulta.",
                "support-cloud",
                Fields("cr07a_name", "cr07a_cliente", "cr07a_clienteidinterno", "cr07a_tenantid", "cr07a_periodo", "cr07a_securescoreactual", "cr07a_securescoremaximo", "cr07a_alertashigh", "cr07a_alertasmedium", "cr07a_alertaslow", "cr07a_incidentesactivos", "cr07a_incidentesresueltos", "cr07a_recomendacionestopjson", "cr07a_alertasjson", "cr07a_incidentesjson", "cr07a_fechaconsulta", "cr07a_estadoconsulta", "cr07a_errorconsulta"),
                Terms("m365", "seguridad", "secure score", "alertas", "incidentes", "recomendaciones", "cliente", "snapshot"),
                Related("cr07a_m365tenantconnection", "cr07a_cliente", "cr07a_m365generatedreport")),

            Table(
                "SoporteCloud/Reportes",
                "Reportes generados",
                "Reportes M365 generados",
                "cr07a_m365generatedreport",
                "cr07a_m365generatedreports",
                "Reportes HTML M365 generados por cliente y periodo, estado, fecha, version de prompt y errores.",
                "support-cloud",
                Fields("cr07a_name", "cr07a_cliente", "cr07a_clienteidinterno", "cr07a_periodo", "cr07a_htmlgenerado", "cr07a_estado", "cr07a_fechageneracion", "cr07a_promptversion", "cr07a_errores"),
                Terms("reporte", "reportes", "m365", "html", "periodo", "cliente", "prompt", "errores"),
                Related("cr07a_cliente", "cr07a_m365reportattachment", "cr07a_m365securitysnapshot")),

            Table(
                "SoporteCloud/Reportes",
                "Anexos reportes",
                "Anexos reportes M365",
                "cr07a_m365reportattachment",
                "cr07a_m365reportattachments",
                "Archivos anexos de reportes M365 con nombre, tipo, tamano y fecha de carga.",
                "support-cloud",
                Fields("cr07a_name", "cr07a_reporte", "cr07a_reporteidinterno", "cr07a_filename", "cr07a_contenttype", "cr07a_size", "cr07a_fechacarga"),
                Terms("anexo", "adjunto", "archivo", "reporte", "m365", "filename", "content type"),
                Related("cr07a_m365generatedreport")),

            Table(
                "Licenciamiento",
                "Consumo Intcomex",
                "Consumo Intcomex",
                "cr07a_consumointcomex",
                "cr07a_consumointcomexes",
                "Consumo de licenciamiento Cloud por account id, cliente, vendor, producto, mes, factura, cantidades, TRM, USD, pesos y tipo de contrato.",
                "licensing",
                Fields("cr07a_name", "cr07a_accountid", "cr07a_nombrecliente", "cr07a_vendor", "cr07a_producto", "cr07a_dias", "cr07a_mesconsumo", "cr07a_factura", "cr07a_valortotalusd", "cr07a_unidadusd", "cr07a_cantidad", "cr07a_trm", "cr07a_pesostotal", "cr07a_tipocontrato"),
                Terms("licenciamiento", "licencias", "intcomex", "consumo", "account id", "producto", "vendor", "cliente", "monthly", "prepaid", "trm", "usd", "pesos", "costo"),
                Related("cr07a_accountidicp", "cr07a_facturacion", "cr07a_precioscloud")),

            Table(
                "Licenciamiento",
                "Account ID ICP",
                "Account ID ICP",
                "cr07a_accountidicp",
                "cr07a_accountidicps",
                "Mapeo de account id de licenciamiento a cliente y grupo empresarial.",
                "licensing",
                Fields("cr07a_name", "cr07a_cliente", "cr07a_grupoempresarialid", "cr07a_grupoempresarialname"),
                Terms("account id", "cliente", "licenciamiento", "grupo empresarial", "icp"),
                Related("cr07a_consumointcomex", "cr07a_cliente", "cr07a_licenciamientoaccountmap")),

            Table(
                "CruceLicenciamiento",
                "Mapeo cuenta-costo",
                "Mapeo licenciamiento",
                "cr07a_licenciamientoaccountmap",
                "cr07a_licenciamientoaccountmaps",
                "Cruce entre account ids/cuentas origen de costo y account ids/clientes destino para licenciamiento.",
                "licensing",
                Fields("cr07a_name", "cr07a_sourceaccountid", "cr07a_sourceaccountname", "cr07a_sourceclientname", "cr07a_targetaccountid", "cr07a_targetaccountname", "cr07a_targetclientid", "cr07a_targetclientname", "cr07a_active", "cr07a_notes"),
                Terms("cruce", "mapeo", "licenciamiento", "account", "cuenta costo", "cliente destino"),
                Related("cr07a_consumointcomex", "cr07a_accountidicp", "cr07a_cliente")),

            Table(
                "CuentasCobro/Conciliacion",
                "Cuentas de cobro",
                "Cuentas de cobro",
                "cr07a_cuentasdecobro",
                "cr07a_cuentasdecobros",
                "Cuentas de cobro de receptores o terceros con valor total, retencion, valor pago, observaciones, fechas, adjunto e impresa.",
                "generic",
                Fields("cr07a_name", "cr07a_nombrereceptor", "cr07a_nitocedula", "cr07a_valortotal", "cr07a_retefuenteporcentaje", "cr07a_valorpago", "cr07a_rteftevalor", "cr07a_observaciones", "cr07a_fechadeemision", "cr07a_fechadepago", "cr07a_adjunto", "cr07a_adjunto_name", "cr07a_impresa", "cr07a_cuentacontablecodigo", "cr07a_cuentacontablenombre", "cr07a_estadoautomatizacion", "cr07a_motivorevision"),
                Terms("cuenta de cobro", "cuentas de cobro", "tercero", "receptor", "beneficiario", "pago", "retencion", "nomina externa", "honorarios"),
                Related("cr07a_nomina", "cr07a_empleado", "cr07a_gastodelaempresa", "cr07a_movimientobancario")),

            Table(
                "RebatesInversiones/P&L",
                "Items manuales P&L",
                "Items manuales P&L",
                "cr07a_pnlmanualitem",
                "cr07a_pnlmanualitems",
                "Registros manuales para P&L, como rebates e ingresos financieros, con tipo, fecha y valor.",
                "pnl",
                Fields("cr07a_name", "cr07a_tipo", "cr07a_fecha", "cr07a_valor"),
                Terms("p&l", "pnl", "rebate", "rebates", "inversiones", "ingresos financieros", "manual", "utilidad neta"),
                Related("cr07a_facturacion", "cr07a_gastodelaempresa")),

            Table(
                "PlanRio",
                "Entrenos y registro",
                "Plan Rio entrenos",
                "cr07a_planrioentreno",
                "cr07a_planrioentrenos",
                "Plan de entrenamiento personal con fecha, semana, fase, disciplina, sesion, objetivo, estado, duracion real y notas.",
                "generic",
                Fields("cr07a_name", "cr07a_fecha", "cr07a_dia", "cr07a_semanaplan", "cr07a_iniciodesemana", "cr07a_fase", "cr07a_disciplina", "cr07a_sesion", "cr07a_min", "cr07a_horas", "cr07a_volumenobjetivo", "cr07a_intensidadzona", "cr07a_detalle", "cr07a_nutricionhidratacion", "cr07a_objetivo", "cr07a_estado", "cr07a_duracionreal", "cr07a_distanciareal", "cr07a_fcpromedio", "cr07a_potenciapromedio", "cr07a_notas", "cr07a_origenhoja", "cr07a_filaorigen"),
                Terms("plan rio", "entreno", "entrenamiento", "disciplina", "sesion", "duracion", "distancia", "potencia")),

            Table(
                "Sistema",
                "Usuarios Dataverse",
                "Usuarios",
                "systemuser",
                "systemusers",
                "Usuarios de Dataverse con nombre, correo interno y Azure AD object id.",
                "generic",
                Fields("fullname", "internalemailaddress", "azureactivedirectoryobjectid"),
                Terms("usuario", "usuarios", "correo", "dataverse", "azure ad", "empleado"),
                Related("cr07a_empleado")),

            Table(
                "Tareas",
                "Tareas automaticas y manuales",
                "Tareas",
                "cr07a_tarea",
                "cr07a_tareas",
                "Tareas automaticas o manuales por modulo, tipo, responsable, estado, fecha limite, cierre, payload, email y adjunto de cierre.",
                "generic",
                Fields("cr07a_name", "cr07a_claveunica", "cr07a_estado", "cr07a_modulo", "cr07a_tipo", "cr07a_sourceid", "cr07a_responsableid", "cr07a_responsablecorreo", "cr07a_responsablenombre", "cr07a_creadoporid", "cr07a_creadoporcorreo", "cr07a_creadopornombre", "cr07a_fechalimite", "cr07a_fechacierre", "cr07a_cerradaporid", "cr07a_cerradaporcorreo", "cr07a_descripcion", "cr07a_actionurl", "cr07a_periodokey", "cr07a_totalpendientes", "cr07a_payloadjson", "cr07a_emailtablahtml", "cr07a_emailtablahtmlfull", "cr07a_esmanual", "cr07a_emailenviado", "cr07a_emailenviadoen", "cr07a_emailerror", "cr07a_comentariocierre", "cr07a_adjuntocierre"),
                Terms("tarea", "tareas", "pendiente", "responsable", "modulo", "fecha limite", "cierre", "automatico", "manual"),
                Related("systemuser", "cr07a_empleado")),

            Table(
                "FlujoCaja/Conciliacion",
                "Movimientos bancarios",
                "Movimientos flujo de caja",
                "cr07a_movimientobancario",
                "cr07a_movimientobancarios",
                "Movimientos bancarios importados desde flujo de caja SharePoint, con fecha, banco, descripcion, entrada, salida, referencia, estado, SIIGO y clasificacion contable.",
                "generic",
                Fields("cr07a_name", "cr07a_fecha", "cr07a_banco", "cr07a_descripcion", "cr07a_valorentrada", "cr07a_valorsalida", "cr07a_referencia", "cr07a_tipomovimiento", "cr07a_estado", "cr07a_siigodocumentid", "cr07a_siigodocumentname", "cr07a_cuentacontablecodigo", "cr07a_cuentacontablenombre", "cr07a_motivorevision", "cr07a_origenflujo", "cr07a_bancocuentacodigo", "cr07a_bancocuentanombre", "cr07a_destinatario", "cr07a_bancodestino", "cr07a_tipodocumento", "cr07a_observaciones", "cr07a_siigoestado", "cr07a_claveexterna", "cr07a_archivoorigen", "cr07a_tablaorigen", "cr07a_filaorigen", "cr07a_hashorigen"),
                Terms("flujo de caja", "movimiento bancario", "banco", "entrada", "salida", "recaudo", "pago", "conciliacion", "siigo"),
                Related("cr07a_cruceflujocaja", "cr07a_facturacion", "cr07a_gastodelaempresa", "cr07a_cuentasdecobro")),

            Table(
                "FlujoCaja",
                "Traslados internos",
                "Traslados flujo de caja",
                "cr07a_trasladointernoflujocaja",
                "cr07a_trasladointernoflujocajas",
                "Traslados internos del flujo de caja entre origen y destino, con entrada, salida, valor, destinatario y estado.",
                "generic",
                Fields("cr07a_name", "cr07a_fecha", "cr07a_origenflujo", "cr07a_flujodesde", "cr07a_flujohacia", "cr07a_entrada", "cr07a_salida", "cr07a_valor", "cr07a_descripcion", "cr07a_destinatario", "cr07a_bancodestino", "cr07a_tipodocumento", "cr07a_observaciones", "cr07a_estado", "cr07a_claveexterna", "cr07a_archivoorigen", "cr07a_tablaorigen", "cr07a_filaorigen", "cr07a_hashorigen"),
                Terms("traslado", "traslados", "flujo de caja", "banco", "entrada", "salida", "valor"),
                Related("cr07a_movimientobancario")),

            Table(
                "Conciliacion",
                "Cruce pagos clientes",
                "Cruce flujo de caja",
                "cr07a_cruceflujocaja",
                "cr07a_cruceflujocajas",
                "Cruces entre movimientos bancarios, facturas de clientes y borradores SIIGO, con diferencia, confianza, estado y valores de pago/retenciones.",
                "generic",
                Fields("cr07a_name", "cr07a_tipo", "cr07a_estado", "cr07a_confianza", "cr07a_motivo", "cr07a_diferencia", "cr07a_movimientobancarioid", "cr07a_movimientoclaveexterna", "cr07a_fechamovimiento", "cr07a_origenflujo", "cr07a_bancocuentacodigo", "cr07a_bancocuentanombre", "cr07a_descripcionmovimiento", "cr07a_valorentrada", "cr07a_facturacionid", "cr07a_facturanumero", "cr07a_cliente", "cr07a_valorfactura", "cr07a_valorpago", "cr07a_reteftevalor", "cr07a_reteicavalor", "cr07a_rteivavalor", "cr07a_jsonborradorsiigo", "cr07a_claveexterna", "cr07a_hashorigen"),
                Terms("cruce", "conciliacion", "recaudo", "pago cliente", "factura", "siigo", "flujo de caja", "retenciones", "diferencia"),
                Related("cr07a_movimientobancario", "cr07a_facturacion", "cr07a_cliente")),

            Table(
                "Hardware",
                "Ordenes y rentabilidad hardware",
                "Hardware",
                "cr07a_hardware",
                "cr07a_hardwares",
                "Ordenes de hardware, cliente, proveedor, valores de venta/costo, utilidad, margen, ODC, facturas, pagos a proveedor y documentos adjuntos.",
                "generic",
                Fields("cr07a_name", "cr07a_importkey", "cr07a_sourcefilename", "cr07a_sourcerownumber", "cr07a_cant", "cr07a_ventaunidad", "cr07a_precioventa", "cr07a_utilidad", "cr07a_valormargen", "cr07a_cliente", "cr07a_estado", "ownerid", "cr07a_costountproveedor", "cr07a_totalesproveedor", "cr07a_valorflete", "cr07a_noorden", "cr07a_proveedor", "cr07a_grupoproforma", "cr07a_nombreproforma", "cr07a_fechaodc", "cr07a_fechapagoaproveedor", "cr07a_fechaactadeentrega", "cr07a_numerodefactura", "cr07a_ordendecompra", "cr07a_ordendecompra_name", "cr07a_adjuntarproforma", "cr07a_adjuntarproforma_name", "cr07a_odcproveedor", "cr07a_odcproveedor_name", "cr07a_tipodocumentoproveedor", "cr07a_pagoaproveedor", "cr07a_pagoaproveedor_name", "cr07a_actadeentrega", "cr07a_actadeentrega_name", "modifiedon"),
                Terms("hardware", "orden", "odc", "proveedor", "cliente", "utilidad", "margen", "factura", "pago proveedor", "proforma", "acta entrega"),
                Related("cr07a_cliente", "cr07a_facturacion", "cr07a_gastodelaempresa"))
        };

        return new DashboardAgentTableDirectoryDto
        {
            Version = "2026-07-14.1",
            ColumnMode = "columnas de negocio usadas por la app, inferidas de servicios, dashboard, scripts de provision y pruebas de modulos",
            ScopeRules = new[]
            {
                "El agente solo responde preguntas que puedan resolverse con estas tablas o sus datos derivados.",
                "Si la pregunta no se relaciona con este directorio, debe rechazarla sin usar conocimiento externo.",
                "Si una pregunta coincide con varias tablas, debe consultar o mencionar todas las candidatas relevantes.",
                "Si una tabla candidata no tiene resolver de datos cargado en el contexto, debe decir que falta ese resolutor o esos registros."
            },
            Tables = MergeTables(seeds),
            Relationships = BuildRelationships()
        };
    }

    private static DashboardAgentTableDirectoryItemDto Table(
        string module,
        string feature,
        string label,
        string logicalName,
        string entitySetName,
        string description,
        string resolverKey,
        IReadOnlyList<string> fields,
        IReadOnlyList<string> terms,
        IReadOnlyList<string>? related = null,
        IReadOnlyList<string>? writable = null)
    {
        var usedColumns = DistinctClean(fields);
        return new DashboardAgentTableDirectoryItemDto
        {
            Module = module,
            Feature = feature,
            Label = label,
            LogicalName = logicalName,
            EntitySetName = entitySetName,
            ResolverKey = resolverKey,
            Description = description,
            BusinessTerms = DistinctClean(terms),
            UsedColumns = usedColumns,
            WritableColumns = DistinctClean(writable ?? Array.Empty<string>()),
            KeyColumns = usedColumns.Where(IsKeyColumn).ToArray(),
            DateColumns = usedColumns.Where(IsDateColumn).ToArray(),
            MoneyColumns = usedColumns.Where(IsMoneyColumn).ToArray(),
            TextColumns = usedColumns.Where(IsTextColumn).ToArray(),
            RelatedTables = DistinctClean(related ?? Array.Empty<string>())
        };
    }

    private static IReadOnlyList<DashboardAgentTableDirectoryItemDto> MergeTables(
        IReadOnlyList<DashboardAgentTableDirectoryItemDto> seeds)
    {
        return seeds
            .GroupBy(static table => table.LogicalName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var usedColumns = DistinctClean(group.SelectMany(static table => table.UsedColumns));
                var writableColumns = DistinctClean(group.SelectMany(static table => table.WritableColumns));
                return new DashboardAgentTableDirectoryItemDto
                {
                    Module = JoinDistinct(group.Select(static table => table.Module)),
                    Feature = JoinDistinct(group.Select(static table => table.Feature)),
                    Label = first.Label,
                    LogicalName = first.LogicalName,
                    EntitySetName = first.EntitySetName,
                    ResolverKey = first.ResolverKey,
                    Description = first.Description,
                    BusinessTerms = DistinctClean(group.SelectMany(static table => table.BusinessTerms)),
                    UsedColumns = usedColumns,
                    WritableColumns = writableColumns,
                    KeyColumns = usedColumns.Where(IsKeyColumn).ToArray(),
                    DateColumns = usedColumns.Where(IsDateColumn).ToArray(),
                    MoneyColumns = usedColumns.Where(IsMoneyColumn).ToArray(),
                    TextColumns = usedColumns.Where(IsTextColumn).ToArray(),
                    RelatedTables = DistinctClean(group.SelectMany(static table => table.RelatedTables))
                };
            })
            .OrderBy(static table => table.Module, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static table => table.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<DashboardAgentSemanticRelationshipDto> BuildRelationships()
    {
        return new[]
        {
            new DashboardAgentSemanticRelationshipDto
            {
                Topic = "Pagos a personas o empleados",
                Description = "Un pago a una persona puede aparecer como nomina, gasto de empresa o cuenta de cobro; usar empleados para identificar nombres y correos.",
                Tables = Fields("cr07a_empleado", "cr07a_nomina", "cr07a_gastodelaempresa", "cr07a_cuentasdecobro")
            },
            new DashboardAgentSemanticRelationshipDto
            {
                Topic = "Cartera y pagos de clientes",
                Description = "La cartera nace en facturacion neta de notas credito; los pagos pueden cruzarse con flujo de caja y conciliacion.",
                Tables = Fields("cr07a_cliente", "cr07a_facturacion", "cr07a_siigonotacredito", "cr07a_movimientobancario", "cr07a_cruceflujocaja")
            },
            new DashboardAgentSemanticRelationshipDto
            {
                Topic = "Utilidad Cloud y licenciamiento",
                Description = "La utilidad Cloud combina facturacion neta de notas credito, costos de licenciamiento Intcomex, catalogo de precios y mapeos de account id.",
                Tables = Fields("cr07a_facturacion", "cr07a_siigonotacredito", "cr07a_consumointcomex", "cr07a_precioscloud", "cr07a_accountidicp", "cr07a_licenciamientoaccountmap")
            },
            new DashboardAgentSemanticRelationshipDto
            {
                Topic = "P&L y rentabilidad",
                Description = "El P&L combina ingresos por facturacion neta de notas credito, gastos de empresa, clasificacion Cloud/Copiers e items manuales como rebates.",
                Tables = Fields("cr07a_facturacion", "cr07a_siigonotacredito", "cr07a_gastodelaempresa", "cr07a_pnlmanualitem", "cr07a_hardware")
            },
            new DashboardAgentSemanticRelationshipDto
            {
                Topic = "Cliente 360",
                Description = "Una pregunta por cliente puede tocar facturas, contratos, soporte, licenciamiento, copiers, reportes M365 y equipos.",
                Tables = Fields("cr07a_cliente", "cr07a_facturacion", "cr07a_salesperformancerecord", "cr07a_ticket", "cr07a_consumointcomex", "cr07a_productoscopiers", "cr07a_equipo", "cr07a_m365generatedreport")
            }
        };
    }

    private static string[] Fields(params string[] values) => values;

    private static string[] Terms(params string[] values) => values;

    private static string[] Related(params string[] values) => values;

    private static string[] DistinctClean(IEnumerable<string> values) =>
        values
            .Select(static value => (value ?? "").Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string JoinDistinct(IEnumerable<string> values) =>
        string.Join(" | ", DistinctClean(values));

    private static bool IsKeyColumn(string column)
    {
        var value = column.ToLowerInvariant();
        return value.Contains("id", StringComparison.Ordinal)
            || value.Contains("nit", StringComparison.Ordinal)
            || value.Contains("cliente", StringComparison.Ordinal)
            || value.Contains("empleado", StringComparison.Ordinal)
            || value.Contains("proveedor", StringComparison.Ordinal)
            || value.Contains("factura", StringComparison.Ordinal)
            || value.Contains("account", StringComparison.Ordinal)
            || value is "ownerid" or "systemuser";
    }

    private static bool IsDateColumn(string column)
    {
        var value = column.ToLowerInvariant();
        return value.Contains("fecha", StringComparison.Ordinal)
            || value.Contains("date", StringComparison.Ordinal)
            || value.Contains("createdon", StringComparison.Ordinal)
            || value.Contains("modifiedon", StringComparison.Ordinal)
            || value.Contains("periodo", StringComparison.Ordinal)
            || value.Contains("mesconsumo", StringComparison.Ordinal)
            || value.Contains("respondidaen", StringComparison.Ordinal)
            || value.Contains("cerradaen", StringComparison.Ordinal);
    }

    private static bool IsMoneyColumn(string column)
    {
        var value = column.ToLowerInvariant();
        return value.Contains("valor", StringComparison.Ordinal)
            || value.Contains("total", StringComparison.Ordinal)
            || value.Contains("price", StringComparison.Ordinal)
            || value.Contains("cost", StringComparison.Ordinal)
            || value.Contains("costo", StringComparison.Ordinal)
            || value.Contains("pago", StringComparison.Ordinal)
            || value.Contains("sueldo", StringComparison.Ordinal)
            || value.Contains("salario", StringComparison.Ordinal)
            || value.Contains("comision", StringComparison.Ordinal)
            || value.Contains("retencion", StringComparison.Ordinal)
            || value.Contains("rete", StringComparison.Ordinal)
            || value.Contains("iva", StringComparison.Ordinal)
            || value.Contains("trm", StringComparison.Ordinal)
            || value.Contains("usd", StringComparison.Ordinal)
            || value.Contains("pesos", StringComparison.Ordinal)
            || value.Contains("flete", StringComparison.Ordinal)
            || value.Contains("margen", StringComparison.Ordinal)
            || value.Contains("utilidad", StringComparison.Ordinal);
    }

    private static bool IsTextColumn(string column)
    {
        var value = column.ToLowerInvariant();
        return value.Contains("name", StringComparison.Ordinal)
            || value.Contains("nombre", StringComparison.Ordinal)
            || value.Contains("descripcion", StringComparison.Ordinal)
            || value.Contains("detalle", StringComparison.Ordinal)
            || value.Contains("observacion", StringComparison.Ordinal)
            || value.Contains("cliente", StringComparison.Ordinal)
            || value.Contains("empleado", StringComparison.Ordinal)
            || value.Contains("proveedor", StringComparison.Ordinal)
            || value.Contains("factura", StringComparison.Ordinal)
            || value.Contains("producto", StringComparison.Ordinal)
            || value.Contains("correo", StringComparison.Ordinal)
            || value.Contains("email", StringComparison.Ordinal)
            || value.Contains("estado", StringComparison.Ordinal)
            || value.Contains("tipo", StringComparison.Ordinal)
            || value.Contains("categoria", StringComparison.Ordinal)
            || value.Contains("titulo", StringComparison.Ordinal)
            || value.Contains("solucion", StringComparison.Ordinal)
            || value.Contains("motivo", StringComparison.Ordinal);
    }
}
