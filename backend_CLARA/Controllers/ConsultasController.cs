using backend_CLARA.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ConsultasController : ControllerBase
    {
        private readonly string _connectionString = "Server=localhost; Database=farmacia; Uid=root ; Pwd=KameHameH4!";

        // --- 1. OBTENER CITAS DISPONIBLES DEL DÍA (Que no tengan consulta) ---
        [HttpGet("citas-disponibles")]
        public IActionResult GetCitasDisponibles([FromQuery] string correoMedico)
        {
            try
            {
                var citas = new List<object>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    // Solo traemos citas Confirmadas de HOY que le pertenezcan al médico de la sesión
                    // Y la clave mágica: Que NO existan ya en la tabla CONSULTAS
                    string query = @"
                        SELECT c.id_Cita, 
                               CONCAT(u.nombre_Usuario, ' ', u.apellido_P) AS Paciente,
                               TIME_FORMAT(c.hora_Cita, '%h:%i %p') AS Hora
                        FROM CITAS c
                        INNER JOIN PACIENTES p ON c.id_Paciente = p.id_Paciente
                        INNER JOIN USUARIOS u ON p.id_Usuario = u.id_Usuario
                        INNER JOIN MEDICOS m ON c.id_Medico = m.id_Medico
                        INNER JOIN USUARIOS um ON m.id_Usuario = um.id_Usuario
                        INNER JOIN ESTATUS e ON c.id_Estatus = e.id_Estatus
                        WHERE e.nombre = 'Confirmada' 
                        AND c.fecha_Cita = CURDATE()
                        AND um.email_Usuario = @correo
                        AND c.id_Cita NOT IN (SELECT id_Cita FROM CONSULTAS)
                        ORDER BY c.hora_Cita ASC";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@correo", correoMedico);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                citas.Add(new
                                {
                                    IdCita = reader.GetInt32(0),
                                    TextoCombo = $"Cita #{reader.GetInt32(0)} - {reader.GetString(1)} - {reader.GetString(2)}"
                                });
                            }
                        }
                    }
                }
                return Ok(citas);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al cargar las citas. " + ex.Message });
            }
        }

        // --- 2. OBTENER MEDICAMENTOS ACTIVOS (Para recetar) ---
        [HttpGet("medicamentos")]
        public IActionResult GetMedicamentos()
        {
            try
            {
                var medicamentos = new List<object>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    // ✨ CAMBIO DE FORMATO: 
                    string query = @"
                        SELECT m.id_Medicamento, 
                               CONCAT(m.nombre_Medicamento, ' (', CAST(m.concentracion_Valor AS UNSIGNED), m.concentracion_Unidad, ')') AS NombreCompleto
                        FROM MEDICAMENTOS m
                        INNER JOIN ESTATUS e ON m.id_Estatus = e.id_Estatus
                        WHERE e.nombre = 'Activo' AND m.stock_Medicamento > 0";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            medicamentos.Add(new
                            {
                                IdMedicamento = reader.GetInt32(0),
                                Nombre = reader.GetString(1)
                            });
                        }
                    }
                }
                return Ok(medicamentos);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al cargar medicamentos. " + ex.Message });
            }
        }

        // --- 3. GUARDAR CONSULTA Y RECETA (TRANSACCIÓN SQL) ---
        [HttpPost]
        public IActionResult GuardarConsulta([FromBody] ConsultaRequest request)
        {
            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();

                // Iniciamos una transacción: Si la receta falla, la consulta se deshace automáticamente
                using (MySqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // A. Validar regla estricta: ¿La cita ya tiene consulta?
                        string checkCita = "SELECT COUNT(*) FROM CONSULTAS WHERE id_Cita = @idCita";
                        using (MySqlCommand cmdCheck = new MySqlCommand(checkCita, conn, transaction))
                        {
                            cmdCheck.Parameters.AddWithValue("@idCita", request.IdCita);
                            if (Convert.ToInt32(cmdCheck.ExecuteScalar()) > 0)
                            {
                                return BadRequest(new { error = "Esta cita ya tiene una consulta registrada." });
                            }
                        }

                        // B. Insertar la Consulta
                        string queryConsulta = @"
                            INSERT INTO CONSULTAS (id_Estatus, id_Cita, sintomas_Consulta, diagnostico_Consulta, observaciones_Consulta, peso, altura) 
                            VALUES ((SELECT id_Estatus FROM ESTATUS WHERE nombre = 'Activo' LIMIT 1), 
                                    @idCita, @sintomas, @diagnostico, @obs, @peso, @altura)";

                        long idNuevaConsulta = 0;
                        using (MySqlCommand cmdIns = new MySqlCommand(queryConsulta, conn, transaction))
                        {
                            cmdIns.Parameters.AddWithValue("@idCita", request.IdCita);
                            cmdIns.Parameters.AddWithValue("@sintomas", request.Sintomas);
                            cmdIns.Parameters.AddWithValue("@diagnostico", request.Diagnostico);
                            cmdIns.Parameters.AddWithValue("@obs", string.IsNullOrWhiteSpace(request.Observaciones) ? (object)DBNull.Value : request.Observaciones);
                            cmdIns.Parameters.AddWithValue("@peso", request.Peso);
                            cmdIns.Parameters.AddWithValue("@altura", request.Altura);
                            cmdIns.ExecuteNonQuery();

                            idNuevaConsulta = cmdIns.LastInsertedId;
                        }

                        // C. Insertar los Detalles de la Receta (Si hay medicamentos)
                        if (request.Receta != null && request.Receta.Count > 0)
                        {
                            string queryReceta = @"
                                INSERT INTO DETALLE_RECETA (id_Medicamento, id_Consulta, duracion, frecuencia, dosis) 
                                VALUES (@idMed, @idCons, @duracion, @frecuencia, @dosis)";

                            foreach (var item in request.Receta)
                            {
                                using (MySqlCommand cmdReceta = new MySqlCommand(queryReceta, conn, transaction))
                                {
                                    cmdReceta.Parameters.AddWithValue("@idMed", item.IdMedicamento);
                                    cmdReceta.Parameters.AddWithValue("@idCons", idNuevaConsulta);
                                    cmdReceta.Parameters.AddWithValue("@duracion", item.Duracion);
                                    cmdReceta.Parameters.AddWithValue("@frecuencia", item.Frecuencia);
                                    cmdReceta.Parameters.AddWithValue("@dosis", item.Dosis);
                                    cmdReceta.ExecuteNonQuery();
                                }
                            }
                        }

                        // D. Actualizar el Estatus de la Cita a "Completada" (Para sacarla de la agenda pendiente)
                        string updateCita = "UPDATE CITAS SET id_Estatus = (SELECT id_Estatus FROM ESTATUS WHERE nombre = 'Completada' LIMIT 1) WHERE id_Cita = @idCita";
                        using (MySqlCommand cmdUpdCita = new MySqlCommand(updateCita, conn, transaction))
                        {
                            cmdUpdCita.Parameters.AddWithValue("@idCita", request.IdCita);
                            cmdUpdCita.ExecuteNonQuery();
                        }

                        // ¡Todo salió bien! Aplicamos los cambios físicos a la base de datos
                        transaction.Commit();
                        return Ok(new { message = "Consulta y receta guardadas exitosamente." });
                    }
                    catch (Exception ex)
                    {
                        // Si algo explotó, deshacemos todos los cambios para no dejar datos corruptos
                        transaction.Rollback();
                        return StatusCode(500, new { error = "Error al guardar la consulta. Operación cancelada. Detalles: " + ex.Message });
                    }
                }
            }
        }
    }
}