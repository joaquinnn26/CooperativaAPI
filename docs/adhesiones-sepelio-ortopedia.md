# Instalación de adhesiones de Sepelio y Ortopedia

Rama de implementación: `Feature/Silvana`. Requiere integrar el frontend de esa misma rama. No se modificó `develop`.

1. Aplicar las migraciones existentes del proyecto, incluida `20261004_IntercambioFacturacion.sql`.
2. Ejecutar `CooperativaAPI/Data/20261016_AdhesionesBeneficios.sql` en la base del ambiente correspondiente. Es idempotente. No transforma contratos o solicitudes anteriores en adhesiones automáticamente.
3. Compilar la solución con MSBuild/Visual Studio y desplegar junto con el frontend.

La migración crea las adhesiones, personas cubiertas con sus DNI, historiales, pedidos de atención y conceptos de factura. Si Ortopedia no existe en el catálogo, la incorpora con cuota mensual de $1.000 y tipo Tarifa Fija Mensual. Si ya existe, conserva su configuración. Sepelio conserva su tarifa previa.

La adhesión es voluntaria. Sepelio admite titular, familiares o ambos, sin obligación de incluir a todo el grupo; cada persona elegida presenta DNI. Los hijos fuera de la localidad pueden declarar continuidad de adhesión, que debe verificar Administración. Ortopedia no pide DNI adicional. Activar cobertura y pedir prestaciones requiere socio activo con medidor registrado a su nombre, además de la conexión eléctrica asociada.

Rutas nuevas bajo `/api/adhesiones`:

| Método | Ruta | Acceso |
|---|---|---|
| GET | `/catalogo`, `/mias` | Socio titular |
| POST | `/` | Socio, conexión eléctrica propia activa |
| POST | `/{id}/corregir`, `/{id}/accion` | Socio titular; las acciones de revisión requieren gestión |
| GET | `/gestion` | Administrador o funciones de Atención/Beneficios autorizadas |
| GET | `/beneficiarios/{id}/dni` | Titular o gestor autorizado |
| POST | `/{id}/prestaciones` | Titular con adhesión y cobertura vigentes |
| POST | `/prestaciones/{id}/accion` | Gestor autorizado |

`POST /api/solicitudservicio` admite el objeto opcional `Adhesiones`, con los mismos campos del alta individual, para solicitar ambos trámites en una transacción. Los documentos del grupo familiar se incluyen en ese objeto; los requisitos generales de la conexión siguen utilizando el mecanismo anterior de carga.

La facturación extiende `/api/facturacion/intercambio/exportar/{yyyy-MM}` con `incluirAdhesiones=true`. El TXT `SOCIOAPP-2` agrega la columna `ConceptosJson` tanto a la exportación como a la respuesta contable. El snapshot exportado permite validar las cuotas sin usar tarifas modificadas posteriormente. Aplicar el lote guarda los conceptos dentro de la misma transacción y no los suma nuevamente al total. Se conserva el formato anterior para los circuitos existentes. El sistema contable debe adaptar su respuesta para devolver el nuevo detalle.

La exportación usa la última adhesión de cada tipo activada durante o antes del mes y no dada de baja antes de su comienzo. No calcula prorrateos ni impuestos. Rechaza períodos con varias mediciones por conexión cuando se solicitan cuotas para evitar cargos repetidos.

Ejecutar pruebas desde PowerShell: `powershell.exe -NoProfile -File tests/Run-AdhesionesTests.ps1`. Compilar primero la API. El ejecutable de prueba usa la conexión de Web.config y revierte sus fixtures; usar una base de pruebas.

Los datos personales de la factura usada como referencia no forman parte del código ni de los fixtures. No se editó la conexión local de Web.config como parte de este cambio.
