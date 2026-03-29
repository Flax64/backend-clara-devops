using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using backend_CLARA.Models;

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HorariosController : ControllerBase
    {
        private readonly string _connectionString = "Server=localhost; Database=farmacia; Uid=root ; Pwd=KameHameH4!";

        // --- 1. LEER TODOS LOS HORARIOS (De médicos activos) ---
        [HttpGet]
        public IActionResult GetHorarios()
        {
            try
            {
                var horarios = new List<object>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    // ✨ Cruzamos HORARIOS con DIAS, MEDICOS y USUARIOS para traer todo legible
                    // Y le damos formato a las horas para que salgan como "08:00 AM" desde SQL
                    string query = @"
                        SELECT 
                            h.id_Horario, 
                            CONCAT(u.nombre_Usuario, ' ', u.apellido_P, ' ', IFNULL(u.apellido_M, '')) AS Medico,
                            d.nombre AS Dia,
                            TIME_FORMAT(h.hora_Entrada, '%h:%i %p') AS Entrada,
                            TIME_FORMAT(h.hora_Salida, '%h:%i %p') AS Salida
                        FROM HORARIOS h
                        INNER JOIN DIAS d ON h.id_Dia = d.id_Dia
                        INNER JOIN MEDICOS m ON h.id_Medico = m.id_Medico
                        INNER JOIN USUARIOS u ON m.id_Usuario = u.id_Usuario
                        INNER JOIN ESTATUS e ON u.id_Estatus = e.id_Estatus
                        WHERE e.nombre = 'Activo'
                        ORDER BY h.id_Horario ASC";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            horarios.Add(new
                            {
                                IdHorario = reader.GetInt32(0),
                                Medico = reader.GetString(1).Trim(),
                                Dia = reader.GetString(2),
                                Entrada = reader.GetString(3),
                                Salida = reader.GetString(4)
                            });
                        }
                    }
                }
                return Ok(horarios);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al obtener los horarios. Detalles: " + ex.Message });
            }
        }

        // --- 2. OBTENER MÉDICOS ACTIVOS (Para el ComboBox) ---
        [HttpGet("medicos")]
        public IActionResult GetMedicosActivos()
        {
            try
            {
                var lista = new List<object>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"
                        SELECT m.id_Medico, CONCAT('Dr. ', u.nombre_Usuario, ' ', u.apellido_P) AS Nombre 
                        FROM MEDICOS m 
                        INNER JOIN USUARIOS u ON m.id_Usuario = u.id_Usuario 
                        WHERE u.id_Estatus = (SELECT id_Estatus FROM ESTATUS WHERE nombre = 'Activo' LIMIT 1)";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read()) { lista.Add(new { Id = reader.GetInt32(0), Nombre = reader.GetString(1) }); }
                    }
                }
                return Ok(lista);
            }
            catch (Exception ex) { return StatusCode(500, new { error = "Error al obtener médicos: " + ex.Message }); }
        }

        // --- 3. OBTENER DÍAS (Para el ComboBox) ---
        [HttpGet("dias")]
        public IActionResult GetDias()
        {
            try
            {
                var lista = new List<object>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    using (MySqlCommand cmd = new MySqlCommand("SELECT id_Dia, nombre FROM DIAS ORDER BY id_Dia", conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read()) { lista.Add(new { Id = reader.GetInt32(0), Nombre = reader.GetString(1) }); }
                    }
                }
                return Ok(lista);
            }
            catch (Exception ex) { return StatusCode(500, new { error = "Error al obtener días: " + ex.Message }); }
        }

        // --- 4. ELIMINAR HORARIO (HARD DELETE REAL) ---
        [HttpDelete("{id}")]
        public IActionResult EliminarHorario(int id)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    // Aquí SÍ hacemos un DELETE real porque no hay id_Estatus
                    string query = "DELETE FROM HORARIOS WHERE id_Horario = @id";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        int afectadas = cmd.ExecuteNonQuery();

                        if (afectadas > 0)
                            return Ok(new { message = "El horario fue eliminado permanentemente del sistema." });
                        else
                            return NotFound(new { error = "No se encontró el horario especificado." });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al intentar eliminar el horario. Detalles: " + ex.Message });
            }
        }

        // --- 5. CREAR NUEVO HORARIO ---
        [HttpPost]
        public IActionResult CrearHorario([FromBody] HorarioRequest request)
        {
            try
            {
                TimeSpan entrada = TimeSpan.Parse(request.HoraEntrada);
                TimeSpan salida = TimeSpan.Parse(request.HoraSalida);

                // 1. Validar que la salida sea después de la entrada
                if (salida <= entrada)
                {
                    return BadRequest(new { error = "La hora de salida debe ser mayor a la hora de entrada." });
                }

                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    // ✨ 2. VALIDACIÓN ESTRICTA DE EMPALME GENERAL (Para cualquier médico)
                    // Buscamos si en ese mismo DÍA, existe algún horario cuyo rango choque con el nuevo
                    string queryEmpalme = @"
                        SELECT COUNT(*) FROM HORARIOS 
                        WHERE id_Dia = @dia 
                        AND (@entrada < hora_Salida AND @salida > hora_Entrada)";

                    using (MySqlCommand cmd = new MySqlCommand(queryEmpalme, conn))
                    {
                        cmd.Parameters.AddWithValue("@dia", request.IdDia);
                        cmd.Parameters.AddWithValue("@entrada", entrada);
                        cmd.Parameters.AddWithValue("@salida", salida);

                        if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                        {
                            // Si choca con alguien, bloqueamos el registro
                            return BadRequest(new { error = "Ese rango de horario ya está ocupado por un médico en este día. Por favor selecciona horas que no se empalmen con los turnos existentes." });
                        }
                    }

                    // 3. Insertar el nuevo horario si el consultorio está libre
                    string queryInsert = "INSERT INTO HORARIOS (id_Dia, id_Medico, hora_Entrada, hora_Salida) VALUES (@dia, @medico, @entrada, @salida)";
                    using (MySqlCommand cmd = new MySqlCommand(queryInsert, conn))
                    {
                        cmd.Parameters.AddWithValue("@dia", request.IdDia);
                        cmd.Parameters.AddWithValue("@medico", request.IdMedico);
                        cmd.Parameters.AddWithValue("@entrada", entrada);
                        cmd.Parameters.AddWithValue("@salida", salida);
                        cmd.ExecuteNonQuery();
                    }
                }
                return Ok(new { message = "Horario creado exitosamente." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al registrar el horario: " + ex.Message });
            }
        }
    }
}
