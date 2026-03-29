using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;

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
    }
}