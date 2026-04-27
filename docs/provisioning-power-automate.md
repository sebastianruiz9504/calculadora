# Flujo de aprobacion de aprovisionamiento

La calculadora envia la solicitud a Power Automate y queda esperando un callback. Si el flujo aprueba pero no hace el POST de vuelta, la app nunca puede marcar la solicitud como aprobada ni crear registros en `cr07a_hardware`.

## 1. Trigger HTTP

En el trigger "When an HTTP request is received", usa un schema que incluya al menos `requestId` y `approvalCallback`. El schema anterior no los tenia, por eso el flujo no tenia una URL ni un identificador para devolver la aprobacion.

```json
{
  "type": "object",
  "properties": {
    "requestId": { "type": "string" },
    "source": { "type": "string" },
    "businessId": { "type": "string" },
    "requester": {
      "type": "object",
      "properties": {
        "systemUserId": { "type": "string" },
        "displayName": { "type": "string" },
        "email": { "type": "string" }
      }
    },
    "cliente": {
      "type": "object",
      "properties": {
        "clienteId": { "type": "string" },
        "nombre": { "type": "string" }
      }
    },
    "aprovisionamiento": {
      "type": "object",
      "properties": {
        "fecha": { "type": "string" },
        "tipoContratoCode": { "type": "string" },
        "tipoContratoLabel": { "type": "string" }
      }
    },
    "scenario": {
      "type": "object",
      "properties": {
        "dealTypeValue": { "type": "integer" },
        "dealTypeLabel": { "type": "string" },
        "requiresProration": { "type": "boolean" },
        "startDate": { "type": "string" },
        "endDate": { "type": "string" }
      }
    },
    "resultado": {
      "type": "object",
      "properties": {
        "puntaje": { "type": "number" },
        "comision": { "type": "number" },
        "prorrateoDias": { "type": "integer" },
        "prorrateoFactor": { "type": "number" },
        "prorrateoTexto": { "type": "string" },
        "ventaMensualTotal": { "type": "number" },
        "ventaTotal": { "type": "number" },
        "ventaTotalAnual": { "type": "number" }
      }
    },
    "descriptionText": { "type": "string" },
    "lineItems": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "lineId": { "type": "string" },
          "productoId": { "type": "string" },
          "productoNombre": { "type": "string" },
          "cantidad": { "type": "integer" },
          "number": { "type": "integer" },
          "costoUnd": { "type": "integer" },
          "ventaUnd": { "type": "integer" },
          "margenPorcentaje": { "type": "integer" },
          "duracionMeses": { "type": "integer" },
          "ventaMensual": { "type": "integer" },
          "ventaTotal": { "type": "integer" },
          "tieneIva": { "type": "boolean" },
          "tipo": { "type": "string" },
          "requiereProrrateo": { "type": "boolean" },
          "inicio": { "type": "string" },
          "final": { "type": "string" }
        },
        "required": [
          "lineId",
          "productoId",
          "productoNombre",
          "cantidad",
          "number",
          "costoUnd",
          "ventaUnd",
          "margenPorcentaje",
          "duracionMeses",
          "ventaMensual",
          "ventaTotal",
          "tipo"
        ]
      }
    },
    "approvalCallback": {
      "type": "object",
      "properties": {
        "requestId": { "type": "string" },
        "callbackUrl": { "type": "string" },
        "statusUrl": { "type": "string" },
        "secretHeaderName": { "type": "string" }
      },
      "required": [
        "requestId",
        "callbackUrl",
        "statusUrl",
        "secretHeaderName"
      ]
    },
    "attachment": {
      "type": "object",
      "properties": {
        "fileName": { "type": "string" },
        "contentType": { "type": "string" },
        "base64": { "type": "string" }
      }
    }
  },
  "required": [
    "requestId",
    "businessId",
    "cliente",
    "aprovisionamiento",
    "resultado",
    "lineItems",
    "approvalCallback"
  ]
}
```

## 2. Accion de aprobacion

Agrega una accion "Start and wait for an approval" usando los datos del trigger. Para el detalle, puedes usar `descriptionText` o armar el texto con cliente, resultado y lineItems.

## 3. Callback a la app

Despues de la aprobacion, agrega una condicion por el outcome. En cada rama agrega una accion HTTP:

- Method: `POST`
- URI: `approvalCallback.callbackUrl`
- Headers:
- `Content-Type`: `application/json`
- `X-Calculator-Callback-Secret`: el mismo valor configurado en `Calculator:ProvisioningApprovalCallbackSecret`

Body de la rama aprobada:

```json
{
  "requestId": "@{triggerBody()?['requestId']}",
  "approved": true,
  "outcome": "approved",
  "comments": "",
  "approvalId": "",
  "respondedAtUtc": "@{utcNow()}",
  "approver": {
    "displayName": "",
    "email": ""
  }
}
```

Body de la rama rechazada:

```json
{
  "requestId": "@{triggerBody()?['requestId']}",
  "approved": false,
  "outcome": "rejected",
  "comments": "",
  "approvalId": "",
  "respondedAtUtc": "@{utcNow()}",
  "approver": {
    "displayName": "",
    "email": ""
  }
}
```

`comments`, `approvalId` y `approver` son opcionales para la app. Lo indispensable es `requestId`, `approved` y el header secreto.

## 4. Configuracion de la app

Configura estos valores:

```json
{
  "Calculator": {
    "ProvisioningRequestFlowUrl": "URL del trigger HTTP de Power Automate",
    "ProvisioningApprovalCallbackUrl": "URL publica HTTPS de /ProvisioningApproval/ApprovalCallback",
    "ProvisioningApprovalCallbackSecret": "mismo secreto usado en el header del flujo"
  }
}
```

Si pruebas localmente, `localhost` no sirve como callback para Power Automate. Usa una URL publica HTTPS, por ejemplo un tunel temporal, o prueba contra el ambiente desplegado.
