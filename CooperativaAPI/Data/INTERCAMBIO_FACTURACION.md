# Intercambio de facturación con el sistema contable

SocioApp prepara el archivo de mediciones y recibe otro archivo del sistema contable. Esta implementación es un intercambio por archivos; no crea una conexión automática por API con el programa contable.

## Flujo

1. Un administrador solicita la exportación de un período. SocioApp registra un lote y guarda una copia de los datos exportados para tener trazabilidad.
2. Contabilidad procesa el TXT y devuelve un TXT con una factura por medición/conexión. Debe conservar las claves de correlación indicadas abajo.
3. SocioApp valida el archivo y guarda el lote y el resultado de cada fila. La validación no crea facturas.
4. Si todas las filas son válidas, un administrador confirma la aplicación del lote. Los códigos externos únicos evitan importar dos veces la misma factura.

## Archivo exportado

Endpoint: `GET /api/facturacion/intercambio/exportar/{periodo}?idServicio={idServicio}`. El filtro `idServicio` es opcional; si se omite se exportan juntos todos los servicios, y si se informa genera el archivo de un servicio. Requiere JWT y permisos de facturación (administrador, gerencia autorizada, encargado o personal con función `FACTURACION` en Administración o Internet). Responde como archivo `text/plain`, UTF-8, separado por tabulaciones. Los importes y lecturas usan punto decimal; las fechas usan `yyyy-MM-dd`.

Las primeras dos líneas son metadatos y encabezados. Las columnas exportadas son:

`IdLoteExportacion`, `IdMedicion`, `IdServicioSocio`, `NumeroSocio`, `IdServicio`, `Servicio`, `Periodo`, `FechaLectura`, `ValorLectura`, `ConsumoCalculado`.

## Archivo devuelto por contabilidad

Debe ser UTF-8 y separado por tabulaciones. En la muestra, `<TAB>` representa un carácter de tabulación real. Debe contener exactamente estas dos primeras líneas:

```text
FormatoVersion<TAB>SOCIOAPP-1
CodigoFacturaExterno<TAB>IdLoteExportacion<TAB>IdMedicion<TAB>IdServicioSocio<TAB>NumeroSocio<TAB>Periodo<TAB>Monto<TAB>FechaEmision<TAB>FechaVencimiento<TAB>LinkPago
```

Después se agrega una fila por factura. El sistema contable debe conservar sin cambios `IdLoteExportacion`, `IdMedicion`, `IdServicioSocio`, `NumeroSocio` y `Periodo` de la medición original. Debe completar:

- `CodigoFacturaExterno`: identificador estable y único de la factura en contabilidad.
- `Monto`: importe total, con hasta dos decimales.
- `FechaEmision` y `FechaVencimiento`: fechas ISO `yyyy-MM-dd`.
- `LinkPago`: URL de pago, o vacío si no corresponde.

La correlación usa `IdServicioSocio` y `IdMedicion`, y además comprueba socio y período. Así se evita asociar una factura a otra conexión histórica del mismo socio y servicio. No se usa el nombre visible del servicio como clave.

## Validar y aplicar

`POST /api/facturacion/intercambio/importaciones/validar` recibe JSON con el nombre y el contenido textual completo del archivo:

```json
{
  "NombreArchivo": "facturas_2026-09.txt",
  "Contenido": "FormatoVersion\tSOCIOAPP-1\n..."
}
```

La respuesta incluye `idLoteImportacion`, estado, cantidad de filas válidas y errores con número de fila. Si una fila tiene error, el lote queda `ConErrores` y no se puede aplicar. El contenido máximo es 20 MB. El mismo contenido no se puede recibir dos veces.

Tras corregir el archivo y validarlo de nuevo, se confirma mediante:

`POST /api/facturacion/intercambio/importaciones/{idLoteImportacion}/aplicar`

El administrador que aplica debe estar vinculado a un empleado, ya que la tabla actual de facturas exige guardar el empleado responsable. La operación inserta todas las facturas del lote en una transacción.

## Datos relacionados en SQL Server

- `Factura.idMedicion` y `Factura.idServicioSocio` relacionan la factura con la lectura que la originó y su conexión concreta socio-servicio. Claves foráneas compuestas comprueban que lectura y conexión coincidan y que `idServicioSocio`, `idSocio` e `idServicio` correspondan a la misma conexión. Los campos heredados se mantienen por compatibilidad con el modelo y endpoints actuales.
- `Factura.CodigoExternoContable` guarda el identificador de contabilidad y tiene índice único filtrado.
- `Factura.Periodo` guarda el período facturado.
- `LoteExportacionContable` y su detalle registran usuario, período, mediciones incluidas y snapshot exportado.
- `LoteImportacionFactura` y su detalle guardan hash del archivo, usuario, validación, errores y factura creada.
- `Factura.LinkPago`, `Pagada` y `FechaPago` conservan los campos actuales para el link y el estado de cobro.

## Confirmaciones necesarias al recibir la especificación de contabilidad

Antes de probar contra el sistema real, confirmar que puede leer y devolver las claves del formato, la codificación UTF-8, tabulaciones, el formato de período, si siempre emite una factura por medición, reglas de redondeo del monto y si el link de pago lo genera contabilidad o un proveedor externo. Si requiere otros nombres, columnas o delimitadores, se adapta el adaptador sin cambiar las claves relacionales internas.

Este alcance no consulta Mercado Pago ni confirma pagos automáticamente. El link se importa como dato; la acreditación seguirá requiriendo la lógica de pagos correspondiente cuando se definan sus credenciales y mecanismo de notificación.
