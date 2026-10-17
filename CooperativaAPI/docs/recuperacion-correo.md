# Recuperacion de contrasenas por correo: etapa 1

El transporte SMTP de recuperacion se configura exclusivamente mediante variables de entorno del proceso de la API ASP.NET Web API (.NET Framework 4.7.2). No lee credenciales de Web.config, del frontend ni de archivos locales del repositorio. Esta etapa no configura ninguna cuenta ni envia correos de prueba.

## Configuracion privada requerida

| Variable | Uso |
| --- | --- |
| COSPRAL_RECOVERY_SMTP_HOST | Nombre del servidor SMTP, sin esquema ni puerto incrustado. |
| COSPRAL_RECOVERY_SMTP_PORT | Puerto del proveedor, entre 1 y 65535. |
| COSPRAL_RECOVERY_SMTP_FROM | Direccion remitente autorizada por el proveedor. |
| COSPRAL_RECOVERY_SMTP_USERNAME | Usuario de autenticacion SMTP. |
| COSPRAL_RECOVERY_SMTP_PASSWORD | Credencial SMTP privada; para Gmail, la contrasena de aplicacion cuando este disponible. |
| COSPRAL_RECOVERY_SMTP_ENABLE_TLS | Debe habilitarse con true; la configuracion sin TLS se rechaza. |
| COSPRAL_RECOVERY_FRONTEND_URL | URL base publica de SocioApp; HTTPS, salvo HTTP de loopback para desarrollo local. Sin credenciales, consulta ni fragmento. |

No se incluyen valores de credenciales ni se deben agregar al repositorio. Para desarrollo local, configurar las variables fuera del proyecto bajo la identidad que ejecuta Visual Studio/IIS Express y reiniciar ese proceso para que las herede. En IIS, provisionarlas para la identidad/proceso del pool; las variables del usuario interactivo no se heredan automaticamente. No hace falta modificar Web.config. Nunca usar variables VITE_* para secretos del backend ni pegar credenciales en scripts, terminales grabadas, capturas o logs.

Las variables de entorno evitan versionar los secretos, pero no son un almacen cifrado y pueden ser leidas por procesos con permisos suficientes. Restringir el acceso a la cuenta local y al servidor. No persistir credenciales en archivos .env de este proyecto. La contrasena se usa sin recortarla ni transformarla.

SmtpClient utiliza STARTTLS mediante EnableSsl; configurar un puerto compatible con STARTTLS y las instrucciones del proveedor. No se deshabilita la validacion de certificados. El timeout de envio es de diez segundos. Un proveedor institucional que admita usuario/contrasena con STARTTLS puede reemplazar Gmail mediante las mismas variables, sin cambiar la logica de recuperacion. OAuth o TLS implicito requieren otra implementacion de transporte.

## Comportamiento y privacidad

- Socio selecciona la cuenta personal. Empleado incluye los roles Empleado y Administrador, manteniendo la cuenta destino asociada al token.
- La configuracion se valida antes de buscar la cuenta: si falta o es invalida, todas las solicitudes con datos validos reciben HTTP 503 y el mismo mensaje publico de indisponibilidad.
- Con configuracion valida, se devuelve el mismo HTTP 200 y mensaje generico para cuentas inexistentes y para fallos durante la busqueda o entrega. No es confirmacion de entrega del correo.
- Un fallo de entrega elimina el token creado. El diagnostico interno emite solo COSPRAL_RECOVERY_REQUEST_FAILED, sin excepcion completa, destinatario, credenciales, token ni enlace. Si falta configuracion emite COSPRAL_RECOVERY_SMTP_UNAVAILABLE. Estos eventos dependen de los listeners de Trace que tenga el servidor; no se agrega un archivo de logs.
- No se devuelve resetToken al navegador. ExposePasswordResetToken no se utiliza, incluso si una configuracion externa intenta activarlo. El valor existente de Web.config no se modifica.
- Esta etapa no incorpora cola de correo, reintentos ni limites de solicitudes. La diferencia temporal del envio sincrono sigue siendo una limitacion; la igualdad de cuerpos y estados no garantiza eliminar todos los canales de enumeracion.

## Tokens y alcance de esta etapa

Se conserva el token criptografico de 32 bytes, su almacenamiento en MemoryCache por 30 minutos y el borrado despues de guardar la contrasena nueva. Reiniciar/reciclar la API pierde los tokens; varias instancias no comparten la cache. Otros tokens de la misma cuenta siguen vigentes y el consumo actual no es atomico. No se modifican login, cambio voluntario de contrasena ni sesiones.

La persistencia SQL, hash del token almacenado, consumo transaccional e invalidacion de otras solicitudes requieren una segunda etapa autorizada. No ejecutar migraciones para esta preparacion.

## Verificacion cuando se habilite el correo

1. Provisionar los parametros del proveedor fuera del repositorio, con la contrasena de aplicacion de Gmail o la credencial institucional autorizada; reiniciar la API.
2. Con cuentas de prueba, comprobar entrega y enlace para Socio y Empleado/Administrador; comprobar que recuperar una no cambie la otra.
3. Restablecer mediante el enlace y verificar inicio de sesion, token utilizado, token vencido y token asociado a otro email.
4. Probar configuracion incompleta y fallo de transporte, sin revelar existencia de cuentas ni registrar datos sensibles.
5. Verificar visualmente las etiquetas y los dos ojos independientes en navegador de escritorio y movil.

Las pruebas de envio y cambios de contrasena requieren autorizacion y cuentas de prueba. Hasta configurar el transporte, HTTP 503 es el resultado esperado; no activar exposicion de tokens como alternativa.