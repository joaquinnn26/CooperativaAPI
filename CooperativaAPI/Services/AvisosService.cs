using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;

namespace CooperativaAPI.Services
{
    // Internal only: callers supply a confirmed event and the transaction that owns it.
    internal static class AvisosService
    {
        internal const string InsertarPersonalSql = @"
INSERT INTO dbo.Aviso
    (idUsuarioSistema,Ambito,TipoAviso,Titulo,Mensaje,Prioridad,TipoOrigen,idOrigen,ClaveEvento,RequiereAccion)
SELECT u.idUsuarioSistema,N'Personal',@tipo,@titulo,@mensaje,@prioridad,@origen,@registro,@clave,@accion
FROM dbo.UsuarioSistema u
JOIN dbo.Rol r ON r.idRol=u.idRol
JOIN dbo.Socio s ON s.idSocio=u.idSocio
WHERE u.idSocio=@socio AND r.NombreRol=N'Socio' AND s.Activo=1
  AND NOT EXISTS (SELECT 1 FROM dbo.AltaSocio h WHERE h.idSocio=s.idSocio AND h.Estado<>N'Aprobada')
  AND NOT EXISTS (
      SELECT 1 FROM dbo.Aviso a WITH (UPDLOCK,HOLDLOCK)
      WHERE a.idUsuarioSistema=u.idUsuarioSistema AND a.ClaveEvento=@clave);";

        internal static int GenerarPersonal(SqlConnection connection, SqlTransaction transaction,
            int idSocio, AvisoPersonalNuevo aviso)
        {
            if (connection == null || connection.State != ConnectionState.Open || transaction == null
                || transaction.Connection != connection || transaction.IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("El aviso requiere la transaccion de la operacion de origen.");
            if (idSocio <= 0 || aviso == null) throw new ArgumentException("El aviso requiere un destinatario y un evento.");
            ValidarTexto(aviso.Tipo, 80);
            ValidarTexto(aviso.Titulo, 120);
            ValidarTexto(aviso.Mensaje, 1000);
            ValidarTexto(aviso.TipoOrigen, 60);
            ValidarTexto(aviso.IdOrigen, 80);
            ValidarTexto(aviso.ClaveEvento, 256);
            if (aviso.Prioridad < 1 || aviso.Prioridad > 3) throw new ArgumentException("Prioridad invalida.");

            using (var command = new SqlCommand(InsertarPersonalSql, connection, transaction))
            {
                command.Parameters.Add("@socio", SqlDbType.Int).Value = idSocio;
                command.Parameters.Add("@tipo", SqlDbType.NVarChar, 80).Value = aviso.Tipo;
                command.Parameters.Add("@titulo", SqlDbType.NVarChar, 120).Value = aviso.Titulo;
                command.Parameters.Add("@mensaje", SqlDbType.NVarChar, 1000).Value = aviso.Mensaje;
                command.Parameters.Add("@prioridad", SqlDbType.TinyInt).Value = aviso.Prioridad;
                command.Parameters.Add("@origen", SqlDbType.NVarChar, 60).Value = aviso.TipoOrigen;
                command.Parameters.Add("@registro", SqlDbType.NVarChar, 80).Value = aviso.IdOrigen;
                command.Parameters.Add("@clave", SqlDbType.NVarChar, 256).Value = aviso.ClaveEvento;
                command.Parameters.Add("@accion", SqlDbType.Bit).Value = aviso.RequiereAccion;
                return command.ExecuteNonQuery();
            }
        }

        private static void ValidarTexto(string texto, int longitud)
        {
            if (string.IsNullOrWhiteSpace(texto) || texto.Length > longitud)
                throw new ArgumentException("Los datos del aviso no son validos.");
        }

        internal static AvisoPersonalNuevo ParaTurno(int idTurno, long idAuditoria, int anterior, int nuevo,
            DateTime fecha, TimeSpan hora, DateTime? propuestaAnterior, TimeSpan? horaAnterior)
        {
            if (idTurno <= 0 || idAuditoria <= 0) throw new ArgumentException("El evento debe estar registrado.");
            // Repeating a decision or sending the same proposal is not a new event.
            if (anterior == nuevo && (nuevo != 3
                || (propuestaAnterior.HasValue && propuestaAnterior.Value.Date == fecha.Date && horaAnterior == hora)))
                return null;

            string tipo, titulo, mensaje;
            var horario = fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + " a las "
                + hora.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            switch (nuevo)
            {
                case 2:
                    tipo = "TurnoAprobado"; titulo = "Turno aprobado";
                    mensaje = "Tu turno para el " + horario + " fue aprobado.";
                    break;
                case 3:
                    tipo = "TurnoPropuesto"; titulo = "Nueva propuesta de horario";
                    mensaje = "La cooperativa propone atenderte el " + horario
                        + ". Ingresa a Mis turnos para consultar y aceptar la propuesta.";
                    break;
                case 4:
                    tipo = "TurnoRechazado"; titulo = "Turno rechazado";
                    mensaje = "Tu turno para el " + horario + " fue rechazado. Consulta el detalle en Mis turnos.";
                    break;
                case 5:
                    tipo = "TurnoCancelado"; titulo = "Turno cancelado";
                    mensaje = "Tu turno para el " + horario + " fue cancelado. Consulta el detalle en Mis turnos.";
                    break;
                default:
                    return null;
            }
            return new AvisoPersonalNuevo
            {
                Tipo = tipo, Titulo = titulo, Mensaje = mensaje, Prioridad = 2,
                TipoOrigen = "Turno", IdOrigen = idTurno.ToString(CultureInfo.InvariantCulture),
                ClaveEvento = "Turno:" + idTurno.ToString(CultureInfo.InvariantCulture)
                    + ":AuditoriaLaboral:" + idAuditoria.ToString(CultureInfo.InvariantCulture),
                RequiereAccion = nuevo == 3
            };
        }
    }

    internal sealed class AvisoPersonalNuevo
    {
        internal string Tipo, Titulo, Mensaje, TipoOrigen, IdOrigen, ClaveEvento;
        internal byte Prioridad;
        internal bool RequiereAccion;
    }
}