# Atención de fallas de medidores

## Registro y organización

1. El encargado registra una falla con un motivo activo del catálogo.
2. El medidor queda averiado. Se registra el evento y se abre una incidencia.
3. La conexión y la asignación al socio se mantienen mientras no se registre un retiro o reemplazo.
4. En la ficha, el encargado elige **Crear tarea para atender la falla**.
5. La API crea un trabajo vinculado a la incidencia y al medidor. Si está instalado, también vincula su conexión y socio actuales.
6. Si la incidencia ya tiene una tarea abierta, se devuelve esa tarea. La comprobación y creación se ejecutan en una transacción serializable.
7. Desde **Ver tarea**, el encargado asigna el trabajo a un operario. Este ve el motivo y los detalles de la detección.

## Reparación

- Para enviar el medidor a reparación debe estar retirado y en condición averiada o pendiente de revisión.
- El envío cambia su estado a **En reparación**, pero mantiene abierta la incidencia.
- Al recibirlo, el encargado registra una revisión. Si confirma que está operativo y su identidad está verificada, se resuelve la incidencia y vuelve a estar disponible.
- Una revisión con resultado averiado o pendiente de revisión mantiene abierta la incidencia.

## Reemplazo y baja

- El operario registra el retiro del equipo averiado, la lectura final, el equipo sustituto y su lectura inicial desde la tarea.
- El encargado revisa el resultado y confirma el cierre. El reemplazo se aplica en una transacción: si falla la instalación del sustituto, se revierte también el retiro y la resolución de la incidencia.
- La baja definitiva de un equipo retirado también resuelve su incidencia abierta y conserva el historial.

## Tarea e incidencia

Cerrar una tarea de diagnóstico sin una operación que resuelva la falla no cierra la incidencia. Se puede crear un nuevo trabajo de seguimiento cuando el anterior haya terminado. Los trabajos quedan vinculados mediante `IncidenciaMedidorTarea`.

Las tareas vinculadas exigen un resultado técnico antes de enviarse a revisión, aprobarse o cerrarse: revisión/reparación con condición confirmada, retiro o reemplazo del medidor de la incidencia. El operario puede indicar **Reparado y funcionando**, **Sigue averiado** o **Pendiente de revisión**. El estado se aplica cuando el encargado confirma el cierre. Guardar avances no cambia por sí solo la condición del equipo.

Si una tarea antigua ya se cerró sin este resultado, el encargado puede registrar **Confirmar revisión / reparación** desde la ficha del medidor, incluso si sigue instalado y está averiado. Confirmar una condición operativa registra el evento con su responsable y resuelve la incidencia. No se infiere la reparación a partir del texto de una tarea anterior.

La ficha separa el estado del equipo, el estado de la incidencia y el estado de su tarea. La API limita la creación de trabajos al encargado del área correspondiente.

## Historial anterior

No se reabren ni se convierten incidencias antiguas automáticamente. Los cierres anteriores de tipo `Reparacion` se muestran como **Registro histórico: envío a reparación**. Los eventos antiguos sin incidencia vinculada permanecen en Movimientos.

Migración: `backend-snippets/laboral/20261014_TareasIncidenciaMedidor.sql`. Requiere la tabla `IncidenciaMedidor` y no modifica eventos anteriores.
