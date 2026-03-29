using backend_CLARA.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CitasController : ControllerBase
    {
        private readonly string _connectionString = "Server=localhost; Database=farmacia; Uid=root ; Pwd=KameHameH4!";

        // --- 1. LEER TODAS LAS CITAS (CON FILTRO DE SEGURIDAD POR ROL) ---
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

                    // 1. Buscamos qué rol tiene el correo
                    if (!string.IsNullOrEmpty(correo))
                    {
                        string rolQuery = @"SELECT r.nombre, u.id_Usuario 
                                            FROM USUARIOS u 
                                            INNER JOIN ROLES r ON u.id_Rol = r.id_Rol 
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

                    // 2. Armamos la consulta base
                    string query = @"
                        SELECT 
                            c.id_Cita, 
                            CONCAT(up.nombre_Usuario, ' ', up.apellido_P) AS Paciente,
                            CONCAT('Dr. ', um.nombre_Usuario, ' ', um.apellido_P) AS Medico,
                            DATE_FORMAT(c.fecha_Cita, '%d/%m/%Y') AS Fecha,
                            TIME_FORMAT(c.hora_Cita, '%h:%i %p') AS Hora,
                            e.nombre AS Estado
                        FROM CITAS c
                        INNER JOIN ESTATUS e ON c.id_Estatus = e.id_Estatus
                        INNER JOIN PACIENTES p ON c.id_Paciente = p.id_Paciente
                        INNER JOIN USUARIOS up ON p.id_Usuario = up.id_Usuario
                        INNER JOIN MEDICOS m ON c.id_Medico = m.id_Medico
                        INNER JOIN USUARIOS um ON m.id_Usuario = um.id_Usuario
                        WHERE 1=1 ";

                    // ✨ REGLA DE SEGURIDAD: Si es paciente, filtramos solo sus citas
                    if (rolUsuario == "Paciente")
                    {
                        query += " AND up.id_Usuario = @idUsuarioLogeado ";
                    }

                    query += " ORDER BY c.fecha_Cita DESC, c.hora_Cita DESC";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        if (rolUsuario == "Paciente")
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

        // --- 2. CREAR CITA ---
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

                    string query = "INSERT INTO CITAS (id_Estatus, id_Paciente, id_Medico, fecha_Cita, hora_Cita) VALUES (@estatus, @paciente, @medico, @fecha, @hora)";
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

        // --- 3. ACTUALIZAR CITA ---
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
                    string getEstatusQuery = "SELECT id_Estatus FROM CITAS WHERE id_Cita = @id";
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

                    string updateQuery = "UPDATE CITAS SET id_Paciente = @paciente, id_Medico = @medico, fecha_Cita = @fecha, hora_Cita = @hora, id_Estatus = @estatus WHERE id_Cita = @id";
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

        // --- 4. ELIMINAR (CANCELAR) CITA ---
        [HttpDelete("{id}")]
        public IActionResult CancelarCita(int id)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    int idCancelada = ObtenerIdEstatus(conn, "Cancelada");

                    string cancelQuery = "UPDATE CITAS SET id_Estatus = @estatus WHERE id_Cita = @id";
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

        // --- 5. CONFIRMAR CITA (ACCIÓN RÁPIDA) ---
        [HttpPut("confirmar/{id}")]
        public IActionResult ConfirmarCita(int id)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    int idConfirmada = ObtenerIdEstatus(conn, "Confirmada");

                    string query = "UPDATE CITAS SET id_Estatus = @estatus WHERE id_Cita = @id";
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

        // ==========================================
        // MÉTODOS AUXILIARES PRIVADOS
        // ==========================================

        private int ObtenerIdEstatus(MySqlConnection conn, string nombreEstatus)
        {
            using (MySqlCommand cmd = new MySqlCommand("SELECT id_Estatus FROM ESTATUS WHERE nombre = @nombre LIMIT 1", conn))
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
                SELECT COUNT(*) FROM HORARIOS h
                INNER JOIN DIAS d ON h.id_Dia = d.id_Dia
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
                SELECT COUNT(*) FROM CITAS 
                WHERE id_Medico = @medico 
                AND fecha_Cita = @fecha 
                AND hora_Cita = @hora 
                AND id_Cita != @idCita
                AND id_Estatus IN (SELECT id_Estatus FROM ESTATUS WHERE nombre IN ('Pendiente', 'Confirmada'))";

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

        // ==========================================
        // ✨ NUEVOS ENDPOINTS INTELIGENTES
        // ==========================================

        // --- 1. OBTENER UNA CITA ESPECÍFICA (PARA EL UPDATE) ---
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
                        FROM CITAS c
                        INNER JOIN ESTATUS e ON c.id_Estatus = e.id_Estatus
                        INNER JOIN MEDICOS m ON c.id_Medico = m.id_Medico
                        INNER JOIN USUARIOS um ON m.id_Usuario = um.id_Usuario
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

        // --- 2. OBTENER PACIENTES (FILTRA POR ROL) ---
        [HttpGet("pacientes")]
        public IActionResult GetPacientesCombo([FromQuery] string correo)
        {
            // (MANTÉN EL MISMO CÓDIGO QUE YA TENÍAS EN ESTE ENDPOINT, NO CAMBIA NADA AQUÍ)
            // ... (Lo omito para no saturar, pero déjalo tal como lo tienes en tu código actual) ...
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
                        string rolQuery = @"SELECT r.nombre, u.id_Usuario FROM USUARIOS u INNER JOIN ROLES r ON u.id_Rol = r.id_Rol WHERE u.email_Usuario = @correo LIMIT 1";
                        using (MySqlCommand cmdRol = new MySqlCommand(rolQuery, conn))
                        {
                            cmdRol.Parameters.AddWithValue("@correo", correo);
                            using (MySqlDataReader reader = cmdRol.ExecuteReader())
                            {
                                if (reader.Read()) { rolUsuario = reader.GetString(0); idUsuarioLogeado = reader.GetInt32(1); }
                            }
                        }
                    }

                    string query = @"SELECT p.id_Paciente, CONCAT(u.nombre_Usuario, ' ', u.apellido_P, ' ', IFNULL(u.apellido_M, '')) AS Nombre FROM PACIENTES p INNER JOIN USUARIOS u ON p.id_Usuario = u.id_Usuario WHERE u.id_Estatus = (SELECT id_Estatus FROM ESTATUS WHERE nombre = 'Activo' LIMIT 1)";
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

        // --- 3. OBTENER HORAS DISPONIBLES (AHORA IGNORA LA CITA ACTUAL) ---
        [HttpGet("horas-disponibles")]
        public IActionResult GetHorasDisponibles([FromQuery] string fecha, [FromQuery] int idCita = 0)
        {
            try
            {
                DateTime fechaParsed = DateTime.Parse(fecha);
                int diaSemanaMySql = (int)fechaParsed.DayOfWeek + 1;
                List<string> horasDisponibles = new List<string>();

                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    string queryHorarios = "SELECT id_Medico, hora_Entrada, hora_Salida FROM HORARIOS WHERE id_Dia = @dia";
                    var horarios = new List<dynamic>();
                    using (MySqlCommand cmd = new MySqlCommand(queryHorarios, conn))
                    {
                        cmd.Parameters.AddWithValue("@dia", diaSemanaMySql);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read()) { horarios.Add(new { IdMedico = reader.GetInt32(0), Entrada = reader.GetTimeSpan(1), Salida = reader.GetTimeSpan(2) }); }
                        }
                    }

                    // ✨ MAGIA AQUÍ: Le decimos que NO tome en cuenta la cita que estamos editando (!= @idCita)
                    string queryCitas = "SELECT id_Medico, hora_Cita FROM CITAS WHERE fecha_Cita = @fecha AND id_Estatus IN (SELECT id_Estatus FROM ESTATUS WHERE nombre IN ('Pendiente', 'Confirmada')) AND id_Cita != @idCita";
                    var citas = new List<dynamic>();
                    using (MySqlCommand cmd = new MySqlCommand(queryCitas, conn))
                    {
                        cmd.Parameters.AddWithValue("@fecha", fecha);
                        cmd.Parameters.AddWithValue("@idCita", idCita);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read()) { citas.Add(new { IdMedico = reader.GetInt32(0), HoraCita = reader.GetTimeSpan(1) }); }
                        }
                    }

                    TimeSpan horaActual = new TimeSpan(8, 0, 0);
                    TimeSpan horaFin = new TimeSpan(20, 0, 0);
                    TimeSpan intervalo = new TimeSpan(0, 15, 0);

                    while (horaActual <= horaFin)
                    {
                        bool hayMedicoDisponible = false;
                        foreach (var h in horarios)
                        {
                            if (horaActual >= h.Entrada && horaActual < h.Salida)
                            {
                                bool tieneCita = false;
                                foreach (var c in citas) { if (c.IdMedico == h.IdMedico && c.HoraCita == horaActual) { tieneCita = true; break; } }
                                if (!tieneCita) { hayMedicoDisponible = true; break; }
                            }
                        }
                        if (hayMedicoDisponible) horasDisponibles.Add(horaActual.ToString(@"hh\:mm"));
                        horaActual = horaActual.Add(intervalo);
                    }
                }
                return Ok(horasDisponibles);
            }
            catch (Exception ex) { return StatusCode(500, new { error = "Error al calcular horas: " + ex.Message }); }
        }

        // --- 4. OBTENER MÉDICO DISPONIBLE (AHORA IGNORA LA CITA ACTUAL) ---
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

                    // ✨ MAGIA AQUÍ TAMBIÉN: Ignoramos la cita actual al buscar médico
                    string query = @"
                        SELECT m.id_Medico, CONCAT('Dr. ', u.nombre_Usuario, ' ', u.apellido_P) AS Nombre
                        FROM MEDICOS m
                        INNER JOIN USUARIOS u ON m.id_Usuario = u.id_Usuario
                        INNER JOIN HORARIOS h ON m.id_Medico = h.id_Medico
                        WHERE h.id_Dia = @dia
                          AND @hora >= h.hora_Entrada AND @hora < h.hora_Salida
                          AND u.id_Estatus = (SELECT id_Estatus FROM ESTATUS WHERE nombre = 'Activo' LIMIT 1)
                          AND m.id_Medico NOT IN (
                              SELECT id_Medico FROM CITAS 
                              WHERE fecha_Cita = @fecha AND hora_Cita = @hora AND id_Cita != @idCita
                              AND id_Estatus IN (SELECT id_Estatus FROM ESTATUS WHERE nombre IN ('Pendiente', 'Confirmada'))
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
    }
}