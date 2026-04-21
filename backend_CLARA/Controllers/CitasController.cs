using backend_CLARA.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Mysqlx.Crud;
using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CitasController : ControllerBase
    {
        private readonly string _connectionString = ConexionDB.Cadena;

        [HttpGet]
        public IActionResult GetCitas([FromQuery] string correo)
        {
            try
            {
                List<CitaRead> citas = new List<CitaRead>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    string rolUsuario = "";
                    int idUsuarioLogeado = 0;

                    string autoCancelarQuery = @"
                        UPDATE citas SET id_Estatus = (SELECT id_Estatus FROM estatus WHERE nombre = 'Cancelada' LIMIT 1)
                        WHERE fecha_Cita < CURDATE() AND id_Estatus IN(SELECT id_Estatus 
                        FROM estatus WHERE nombre IN('Pendiente', 'Confirmada'))";
                    using (MySqlCommand cmdAutoCancelar = new MySqlCommand(autoCancelarQuery, conn))
                    {
                        cmdAutoCancelar.ExecuteNonQuery();
                    }

                        if (!string.IsNullOrEmpty(correo))
                    {
                        string rolQuery = @"SELECT r.nombre, u.id_Usuario 
                                            FROM usuarios u 
                                            INNER JOIN roles r ON u.id_Rol = r.id_Rol 
                                            WHERE u.email_Usuario = @correo LIMIT 1";
                        using (MySqlCommand cmdRol = new MySqlCommand(rolQuery, conn))
                        {
                            cmdRol.Parameters.AddWithValue("@correo", correo);
                            using (MySqlDataReader reader = cmdRol.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    rolUsuario = reader.GetString(0);
                                    idUsuarioLogeado = reader.GetInt32(1);
                                }
                            }
                        }
                    }

                    string query = @"
                        SELECT 
                            c.id_Cita, 
                            CONCAT(up.nombre_Usuario, ' ', up.apellido_P) AS Paciente,
                            CONCAT('Dr. ', um.nombre_Usuario, ' ', um.apellido_P) AS Medico,
                            DATE_FORMAT(c.fecha_Cita, '%d/%m/%Y') AS Fecha,
                            TIME_FORMAT(c.hora_Cita, '%h:%i %p') AS Hora,
                            e.nombre AS Estado
                        FROM citas c
                        INNER JOIN estatus e ON c.id_Estatus = e.id_Estatus
                        INNER JOIN pacientes p ON c.id_Paciente = p.id_Paciente
                        INNER JOIN usuarios up ON p.id_Usuario = up.id_Usuario
                        INNER JOIN medicos m ON c.id_Medico = m.id_Medico
                        INNER JOIN usuarios um ON m.id_Usuario = um.id_Usuario
                        WHERE 1=1 ";

                    if (rolUsuario == "Paciente")
                    {
                        query += " AND up.id_Usuario = @idUsuarioLogeado ";
                    }
                    else if (rolUsuario == "Médico" || rolUsuario == "Medico")
                    {
                        query += " AND um.id_Usuario = @idUsuarioLogeado ";
                    }

                    query += " ORDER BY c.fecha_Cita DESC, c.hora_Cita DESC";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        if (rolUsuario == "Paciente" || rolUsuario == "Médico" || rolUsuario == "Medico")
                        {
                            cmd.Parameters.AddWithValue("@idUsuarioLogeado", idUsuarioLogeado);
                        }

                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                citas.Add(new CitaRead
                                {
                                    IdCita = reader.GetInt32(0),
                                    Paciente = reader.GetString(1),
                                    Medico = reader.GetString(2),
                                    Fecha = reader.GetString(3),
                                    Hora = reader.GetString(4),
                                    Estado = reader.GetString(5)
                                });
                            }
                        }
                    }
                }
                return Ok(citas);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al obtener las citas. Detalles: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult CrearCita([FromBody] CitaRequest request)
        {
            try
            {
                DateTime fechaHoraCita = DateTime.Parse($"{request.Fecha} {request.Hora}");

                if (fechaHoraCita < DateTime.Now)
                {
                    return BadRequest(new { error = "No puedes agendar una cita en una fecha u hora que ya pasó." });
                }

                if (fechaHoraCita.Minute % 15 != 0)
                {
                    return BadRequest(new { error = "Las citas solo pueden agendarse en intervalos de 15 minutos (ej. 10:00, 10:15, 10:30)." });
                }

                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    string errorDisponibilidad = ValidarDisponibilidad(conn, request.IdMedico, request.Fecha, request.Hora, 0);
                    if (errorDisponibilidad != null)
                    {
                        return BadRequest(new { error = errorDisponibilidad });
                    }

                    int idPendiente = ObtenerIdEstatus(conn, "Pendiente");

                    string query = "INSERT INTO citas (id_Estatus, id_Paciente, id_Medico, fecha_Cita, hora_Cita) VALUES (@estatus, @paciente, @medico, @fecha, @hora)";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@estatus", idPendiente);
                        cmd.Parameters.AddWithValue("@paciente", request.IdPaciente);
                        cmd.Parameters.AddWithValue("@medico", request.IdMedico);
                        cmd.Parameters.AddWithValue("@fecha", request.Fecha);
                        cmd.Parameters.AddWithValue("@hora", request.Hora);
                        cmd.ExecuteNonQuery();
                    }
                }
                return Ok(new { message = "Cita agendada exitosamente (Estado: Pendiente)." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al registrar la cita. Detalles: " + ex.Message });
            }
        }

        [HttpPut("{id}")]
        public IActionResult ActualizarCita(int id, [FromBody] CitaRequest request)
        {
            try
            {
                DateTime fechaHoraCita = DateTime.Parse($"{request.Fecha} {request.Hora}");

                if (fechaHoraCita < DateTime.Now)
                    return BadRequest(new { error = "No puedes reprogramar una cita hacia el pasado." });

                if (fechaHoraCita.Minute % 15 != 0)
                    return BadRequest(new { error = "Las citas solo pueden agendarse en intervalos de 15 minutos." });

                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    int estatusActualId = 0;
                    string getEstatusQuery = "SELECT id_Estatus FROM citas WHERE id_Cita = @id";
                    using (MySqlCommand cmd = new MySqlCommand(getEstatusQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        var result = cmd.ExecuteScalar();
                        if (result == null) return NotFound(new { error = "Cita no encontrada." });
                        estatusActualId = Convert.ToInt32(result);
                    }

                    int idConfirmada = ObtenerIdEstatus(conn, "Confirmada");
                    int idPendiente = ObtenerIdEstatus(conn, "Pendiente");

                    if (estatusActualId == idConfirmada && request.IdEstatus == idPendiente)
                    {
                        return BadRequest(new { error = "Una cita que ya fue Confirmada no puede regresar a estado Pendiente." });
                    }

                    string errorDisponibilidad = ValidarDisponibilidad(conn, request.IdMedico, request.Fecha, request.Hora, id);
                    if (errorDisponibilidad != null)
                    {
                        return BadRequest(new { error = errorDisponibilidad });
                    }

                    string updateQuery = "UPDATE citas SET id_Paciente = @paciente, id_Medico = @medico, fecha_Cita = @fecha, hora_Cita = @hora, id_Estatus = @estatus WHERE id_Cita = @id";
                    using (MySqlCommand cmd = new MySqlCommand(updateQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@paciente", request.IdPaciente);
                        cmd.Parameters.AddWithValue("@medico", request.IdMedico);
                        cmd.Parameters.AddWithValue("@fecha", request.Fecha);
                        cmd.Parameters.AddWithValue("@hora", request.Hora);
                        cmd.Parameters.AddWithValue("@estatus", request.IdEstatus);
                        cmd.ExecuteNonQuery();
                    }
                }
                return Ok(new { message = "Cita actualizada correctamente." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al actualizar la cita. Detalles: " + ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public IActionResult CancelarCita(int id)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    int idCancelada = ObtenerIdEstatus(conn, "Cancelada");

                    string cancelQuery = "UPDATE citas SET id_Estatus = @estatus WHERE id_Cita = @id";
                    using (MySqlCommand cmd = new MySqlCommand(cancelQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@estatus", idCancelada);
                        cmd.Parameters.AddWithValue("@id", id);
                        int afectadas = cmd.ExecuteNonQuery();

                        if (afectadas > 0)
                            return Ok(new { message = "La cita ha sido cancelada exitosamente." });
                        else
                            return NotFound(new { error = "No se encontró la cita especificada." });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al intentar cancelar la cita. Detalles: " + ex.Message });
            }
        }

        [HttpPut("confirmar/{id}")]
        public IActionResult ConfirmarCita(int id)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    int idConfirmada = ObtenerIdEstatus(conn, "Confirmada");

                    string query = "UPDATE citas SET id_Estatus = @estatus WHERE id_Cita = @id";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@estatus", idConfirmada);
                        cmd.Parameters.AddWithValue("@id", id);
                        int afectadas = cmd.ExecuteNonQuery();

                        if (afectadas > 0)
                            return Ok(new { message = "La cita ha sido confirmada exitosamente." });
                        else
                            return NotFound(new { error = "No se encontró la cita especificada." });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al intentar confirmar la cita. Detalles: " + ex.Message });
            }
        }

        private int ObtenerIdEstatus(MySqlConnection conn, string nombreEstatus)
        {
            using (MySqlCommand cmd = new MySqlCommand("SELECT id_Estatus FROM estatus WHERE nombre = @nombre LIMIT 1", conn))
            {
                cmd.Parameters.AddWithValue("@nombre", nombreEstatus);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private string ValidarDisponibilidad(MySqlConnection conn, int idMedico, string fecha, string hora, int idCitaIgnorar)
        {
            DateTime fechaParsed = DateTime.Parse(fecha);
            int diaSemanaMySql = (int)fechaParsed.DayOfWeek + 1;

            string horarioQuery = @"
                SELECT COUNT(*) FROM horarios h
                INNER JOIN dias d ON h.id_Dia = d.id_Dia
                WHERE h.id_Medico = @medico 
                AND h.id_Dia = @dia
                AND @hora >= h.hora_Entrada 
                AND @hora < h.hora_Salida";

            using (MySqlCommand cmd = new MySqlCommand(horarioQuery, conn))
            {
                cmd.Parameters.AddWithValue("@medico", idMedico);
                cmd.Parameters.AddWithValue("@dia", diaSemanaMySql);
                cmd.Parameters.AddWithValue("@hora", hora);

                if (Convert.ToInt32(cmd.ExecuteScalar()) == 0)
                {
                    return "El médico seleccionado no tiene horario de atención en este día y hora.";
                }
            }

            string choqueQuery = @"
                SELECT COUNT(*) FROM citas 
                WHERE id_Medico = @medico 
                AND fecha_Cita = @fecha 
                AND hora_Cita = @hora 
                AND id_Cita != @idCita
                AND id_Estatus IN (SELECT id_Estatus FROM estatus WHERE nombre IN ('Pendiente', 'Confirmada'))";

            using (MySqlCommand cmd = new MySqlCommand(choqueQuery, conn))
            {
                cmd.Parameters.AddWithValue("@medico", idMedico);
                cmd.Parameters.AddWithValue("@fecha", fecha);
                cmd.Parameters.AddWithValue("@hora", hora);
                cmd.Parameters.AddWithValue("@idCita", idCitaIgnorar);

                if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                {
                    return "El horario seleccionado ya está ocupado por otra cita activa.";
                }
            }

            return null;
        }

        [HttpGet("{id}")]
        public IActionResult GetCitaById(int id)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"
                        SELECT c.id_Cita, c.id_Paciente, c.id_Medico, 
                               DATE_FORMAT(c.fecha_Cita, '%Y-%m-%d') as Fecha, 
                               TIME_FORMAT(c.hora_Cita, '%H:%i:%s') as Hora, 
                               e.nombre as Estado,
                               CONCAT('Dr. ', um.nombre_Usuario, ' ', um.apellido_P) AS MedicoNombre
                        FROM citas c
                        INNER JOIN estatus e ON c.id_Estatus = e.id_Estatus
                        INNER JOIN medicos m ON c.id_Medico = m.id_Medico
                        INNER JOIN usuarios um ON m.id_Usuario = um.id_Usuario
                        WHERE c.id_Cita = @id";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                return Ok(new
                                {
                                    IdCita = reader.GetInt32(0),
                                    IdPaciente = reader.GetInt32(1),
                                    IdMedico = reader.GetInt32(2),
                                    Fecha = reader.GetString(3),
                                    Hora = reader.GetString(4),
                                    Estado = reader.GetString(5),
                                    MedicoNombre = reader.GetString(6).Trim()
                                });
                            }
                        }
                    }
                }
                return NotFound(new { error = "La cita no existe." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al obtener la cita: " + ex.Message });
            }
        }

        [HttpGet("pacientes")]
        public IActionResult GetPacientesCombo([FromQuery] string correo)
        {
            try
            {
                var lista = new List<object>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string rolUsuario = "";
                    int idUsuarioLogeado = 0;

                    if (!string.IsNullOrEmpty(correo))
                    {
                        string rolQuery = @"SELECT r.nombre, u.id_Usuario FROM usuarios u INNER JOIN roles r ON u.id_Rol = r.id_Rol WHERE u.email_Usuario = @correo LIMIT 1";
                        using (MySqlCommand cmdRol = new MySqlCommand(rolQuery, conn))
                        {
                            cmdRol.Parameters.AddWithValue("@correo", correo);
                            using (MySqlDataReader reader = cmdRol.ExecuteReader())
                            {
                                if (reader.Read()) { rolUsuario = reader.GetString(0); idUsuarioLogeado = reader.GetInt32(1); }
                            }
                        }
                    }

                    string query = @"SELECT p.id_Paciente, CONCAT(u.nombre_Usuario, ' ', u.apellido_P, ' ', IFNULL(u.apellido_M, '')) AS Nombre FROM pacientes p INNER JOIN usuarios u ON p.id_Usuario = u.id_Usuario WHERE u.id_Estatus = (SELECT id_Estatus FROM estatus WHERE nombre = 'Activo' LIMIT 1)";
                    if (rolUsuario == "Paciente") query += " AND p.id_Usuario = @idUsuario";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        if (rolUsuario == "Paciente") cmd.Parameters.AddWithValue("@idUsuario", idUsuarioLogeado);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read()) { lista.Add(new { Id = reader.GetInt32(0), Nombre = reader.GetString(1).Trim() }); }
                        }
                    }
                }
                return Ok(lista);
            }
            catch (Exception ex) { return StatusCode(500, new { error = "Error al obtener pacientes: " + ex.Message }); }
        }

        [HttpGet("horas-disponibles")]
        public IActionResult GetHorasDisponibles([FromQuery] string fecha, [FromQuery] int? idCita = null)
        {
            try
            {
                DateTime fechaSeleccionada = DateTime.Parse(fecha);
                int diaSemana = (int)fechaSeleccionada.DayOfWeek + 1;

                List<string> horasGeneradas = new List<string>();

                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    string queryHorarios = "SELECT hora_Entrada, hora_Salida FROM horarios WHERE id_Dia = @dia";
                    List<Tuple<TimeSpan, TimeSpan>> rangosMedicos = new List<Tuple<TimeSpan, TimeSpan>>();

                    using (MySqlCommand cmdHorarios = new MySqlCommand(queryHorarios, conn))
                    {
                        cmdHorarios.Parameters.AddWithValue("@dia", diaSemana);
                        using (MySqlDataReader reader = cmdHorarios.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                rangosMedicos.Add(new Tuple<TimeSpan, TimeSpan>(reader.GetTimeSpan(0), reader.GetTimeSpan(1)));
                            }
                        }
                    }

                    if (rangosMedicos.Count == 0) return Ok(horasGeneradas);

                    foreach (var rango in rangosMedicos)
                    {
                        TimeSpan horaIteracion = rango.Item1;

                        while (horaIteracion.Add(TimeSpan.FromMinutes(15)) <= rango.Item2)
                        {
                            string horaString = horaIteracion.ToString(@"hh\:mm");
                            if (!horasGeneradas.Contains(horaString))
                            {
                                horasGeneradas.Add(horaString);
                            }
                            horaIteracion = horaIteracion.Add(TimeSpan.FromMinutes(15));
                        }
                    }

                    horasGeneradas.Sort();

                    string queryOcupadas = @"
                        SELECT TIME_FORMAT(hora_Cita, '%H:%i') 
                        FROM citas 
                        WHERE fecha_Cita = @fecha AND id_Estatus IN (SELECT id_Estatus FROM estatus WHERE nombre IN ('Pendiente', 'Confirmada'))";

                    if (idCita.HasValue && idCita.Value > 0)
                    {
                        queryOcupadas += " AND id_Cita != @idCita";
                    }

                    using (MySqlCommand cmdOcupadas = new MySqlCommand(queryOcupadas, conn))
                    {
                        cmdOcupadas.Parameters.AddWithValue("@fecha", fechaSeleccionada.ToString("yyyy-MM-dd"));
                        if (idCita.HasValue && idCita.Value > 0) cmdOcupadas.Parameters.AddWithValue("@idCita", idCita.Value);

                        using (MySqlDataReader reader = cmdOcupadas.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string horaOcupada = reader.GetString(0);
                                horasGeneradas.Remove(horaOcupada);
                            }
                        }
                    }
                }

                return Ok(horasGeneradas);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al calcular horas. Detalles: " + ex.Message });
            }
        }

        [HttpGet("medico-disponible")]
        public IActionResult GetMedicoDisponible([FromQuery] string fecha, [FromQuery] string hora, [FromQuery] int idCita = 0)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    DateTime fechaParsed = DateTime.Parse(fecha);
                    int diaSemanaMySql = (int)fechaParsed.DayOfWeek + 1;

                    string query = @"
                        SELECT m.id_Medico, CONCAT('Dr. ', u.nombre_Usuario, ' ', u.apellido_P) AS Nombre
                        FROM medicos m
                        INNER JOIN usuarios u ON m.id_Usuario = u.id_Usuario
                        INNER JOIN horarios h ON m.id_Medico = h.id_Medico
                        WHERE h.id_Dia = @dia
                          AND @hora >= h.hora_Entrada AND @hora < h.hora_Salida
                          AND u.id_Estatus = (SELECT id_Estatus FROM estatus WHERE nombre = 'Activo' LIMIT 1)
                          AND m.id_Medico NOT IN (
                              SELECT id_Medico FROM citas 
                              WHERE fecha_Cita = @fecha AND hora_Cita = @hora AND id_Cita != @idCita
                              AND id_Estatus IN (SELECT id_Estatus FROM estatus WHERE nombre IN ('Pendiente', 'Confirmada'))
                          )
                        LIMIT 1";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@dia", diaSemanaMySql);
                        cmd.Parameters.AddWithValue("@hora", hora);
                        cmd.Parameters.AddWithValue("@fecha", fecha);
                        cmd.Parameters.AddWithValue("@idCita", idCita);

                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read()) { return Ok(new { Id = reader.GetInt32(0), Nombre = reader.GetString(1).Trim() }); }
                        }
                    }
                }
                return NotFound(new { error = "No hay médicos disponibles para la fecha y hora seleccionada." });
            }
            catch (Exception ex) { return StatusCode(500, new { error = "Error al buscar médico: " + ex.Message }); }
        }

        [HttpGet("validar-huerfanas")]
        public IActionResult ValidarCitasHuerfanas()
        {
            try
            {
                List<string> alertas = new List<string>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    string query = @"
                        SELECT c.id_Cita, CONCAT(up.nombre_Usuario, ' ', up.apellido_P) AS Paciente, 
                               TIME_FORMAT(c.hora_Cita, '%h:%i %p') AS Hora, e.nombre AS Estado
                        FROM citas c
                        INNER JOIN estatus e ON c.id_Estatus = e.id_Estatus
                        INNER JOIN pacientes p ON c.id_Paciente = p.id_Paciente
                        INNER JOIN usuarios up ON p.id_Usuario = up.id_Usuario
                        WHERE e.nombre IN ('Pendiente', 'Confirmada')
                        AND NOT EXISTS (
                            SELECT 1 FROM horarios h
                            WHERE h.id_Medico = c.id_Medico
                            AND h.id_Dia = DAYOFWEEK(c.fecha_Cita)
                            AND c.hora_Cita >= h.hora_Entrada
                            AND c.hora_Cita < h.hora_Salida
                        )";

                    var citasHuerfanas = new List<dynamic>();
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            citasHuerfanas.Add(new
                            {
                                IdCita = reader.GetInt32(0),
                                Paciente = reader.GetString(1),
                                Hora = reader.GetString(2),
                                Estado = reader.GetString(3)
                            });
                        }
                    }

                    foreach (var cita in citasHuerfanas)
                    {
                        alertas.Add($"La hora de la cita de {cita.Paciente} a las {cita.Hora} no está disponible, favor de editarla y reagendarla.");

                        if (cita.Estado == "Confirmada")
                        {
                            string qUpdate = "UPDATE citas SET id_Estatus = (SELECT id_Estatus FROM estatus WHERE nombre = 'Pendiente') WHERE id_Cita = @id";
                            using (MySqlCommand cmdUpd = new MySqlCommand(qUpdate, conn))
                            {
                                cmdUpd.Parameters.AddWithValue("@id", cita.IdCita);
                                cmdUpd.ExecuteNonQuery();
                            }
                        }
                    }
                }
                return Ok(new { alertas });
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }
    }
}