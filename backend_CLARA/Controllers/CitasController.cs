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

        // --- 1. LEER TODAS LAS CITAS ---
        [HttpGet]
        public IActionResult GetCitas()
        {
            try
            {
                List<CitaRead> citas = new List<CitaRead>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
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
                        ORDER BY c.fecha_Cita DESC, c.hora_Cita DESC";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
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

                // 1. Validar que no sea en el pasado
                if (fechaHoraCita < DateTime.Now)
                {
                    return BadRequest(new { error = "No puedes agendar una cita en una fecha u hora que ya pasó." });
                }

                // 2. Validar intervalos de 15 minutos
                if (fechaHoraCita.Minute % 15 != 0)
                {
                    return BadRequest(new { error = "Las citas solo pueden agendarse en intervalos de 15 minutos (ej. 10:00, 10:15, 10:30)." });
                }

                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    // 3. Validar si el médico está disponible a esa hora y no está ocupado
                    string errorDisponibilidad = ValidarDisponibilidad(conn, request.IdMedico, request.Fecha, request.Hora, 0);
                    if (errorDisponibilidad != null)
                    {
                        return BadRequest(new { error = errorDisponibilidad });
                    }

                    // 4. Obtener el ID del estatus "Pendiente"
                    int idPendiente = ObtenerIdEstatus(conn, "Pendiente");

                    // 5. Insertar
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

                    // 1. Obtener estatus actual de la cita
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

                    // 2. REGLA DE NEGOCIO: No regresar de Confirmada a Pendiente
                    if (estatusActualId == idConfirmada && request.IdEstatus == idPendiente)
                    {
                        return BadRequest(new { error = "Una cita que ya fue Confirmada no puede regresar a estado Pendiente." });
                    }

                    // 3. Validar disponibilidad (ignorando esta misma cita)
                    string errorDisponibilidad = ValidarDisponibilidad(conn, request.IdMedico, request.Fecha, request.Hora, id);
                    if (errorDisponibilidad != null)
                    {
                        return BadRequest(new { error = errorDisponibilidad });
                    }

                    // 4. Actualizar
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

                    // Soft Delete -> Cambiar a Cancelada
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

        // ==========================================
        // MÉTODOS AUXILIARES PRIVADOS Y DE AYUDA
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
            // 1. Validar que el médico trabaje ese día y a esa hora
            // MySQL DAYOFWEEK: 1=Domingo, 2=Lunes... 7=Sábado
            // Asumiendo que tu tabla DIAS usa id_Dia 1=Domingo o 1=Lunes (Ajusta la lógica de días según tu BD)
            // Aquí haremos una consulta cruzada con HORARIOS
            DateTime fechaParsed = DateTime.Parse(fecha);
            int diaSemanaMySql = (int)fechaParsed.DayOfWeek + 1; // Convierte a formato de MySQL (1 a 7)

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

            // 2. Validar que no choque con otra cita (Pendiente o Confirmada)
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

            return null; // Todo en orden
        }
    }
}
