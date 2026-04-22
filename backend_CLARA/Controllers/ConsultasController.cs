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
        private readonly string _connectionString = ConexionDB.Cadena;

        [HttpGet("citas-disponibles")]
        public IActionResult GetCitasDisponibles([FromQuery] string correoMedico)
        {
            try
            {
                var citas = new List<object>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    string query = @"
                        SELECT c.id_Cita, 
                               CONCAT(u.nombre_Usuario, ' ', u.apellido_P) AS Paciente,
                               TIME_FORMAT(c.hora_Cita, '%h:%i %p') AS Hora
                        FROM citas c
                        INNER JOIN pacientes p ON c.id_Paciente = p.id_Paciente
                        INNER JOIN usuarios u ON p.id_Usuario = u.id_Usuario
                        INNER JOIN medicos m ON c.id_Medico = m.id_Medico
                        INNER JOIN usuarios um ON m.id_Usuario = um.id_Usuario
                        INNER JOIN estatus e ON c.id_Estatus = e.id_Estatus
                        WHERE e.nombre = 'Confirmada' 
                        AND c.fecha_Cita = CURDATE()
                        AND um.email_Usuario = @correo
                        AND c.id_Cita NOT IN (SELECT id_Cita FROM consultas)
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

        [HttpGet("medicamentos")]
        public IActionResult GetMedicamentos()
        {
            try
            {
                var medicamentos = new List<object>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    string query = @"
                        SELECT m.id_Medicamento, 
                               CONCAT(m.nombre_Medicamento, ' (', CAST(m.concentracion_Valor AS UNSIGNED), m.concentracion_Unidad, ')') AS NombreCompleto
                        FROM medicamentos m
                        INNER JOIN estatus e ON m.id_Estatus = e.id_Estatus
                        WHERE e.nombre = 'Activo'";

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

        [HttpPost]
        public IActionResult GuardarConsulta([FromBody] ConsultaRequest request)
        {
            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();

                using (MySqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        string checkCita = "SELECT COUNT(*) FROM consultas WHERE id_Cita = @idCita";
                        using (MySqlCommand cmdCheck = new MySqlCommand(checkCita, conn, transaction))
                        {
                            cmdCheck.Parameters.AddWithValue("@idCita", request.IdCita);
                            if (Convert.ToInt32(cmdCheck.ExecuteScalar()) > 0)
                            {
                                return BadRequest(new { error = "Esta cita ya tiene una consulta registrada." });
                            }
                        }

                        string queryConsulta = @"
                            INSERT INTO consultas (id_Estatus, id_Cita, sintomas_Consulta, diagnostico_Consulta, observaciones_Consulta, peso, altura) 
                            VALUES ((SELECT id_Estatus FROM estatus WHERE nombre = 'Activo' LIMIT 1), 
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

                        if (request.Receta != null && request.Receta.Count > 0)
                        {
                            string queryReceta = @"
                                INSERT INTO detalle_receta (id_Medicamento, id_Consulta, duracion, frecuencia, dosis) 
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

                        string updateCita = "UPDATE citas SET id_Estatus = (SELECT id_Estatus FROM estatus WHERE nombre = 'Completada' LIMIT 1) WHERE id_Cita = @idCita";
                        using (MySqlCommand cmdUpdCita = new MySqlCommand(updateCita, conn, transaction))
                        {
                            cmdUpdCita.Parameters.AddWithValue("@idCita", request.IdCita);
                            cmdUpdCita.ExecuteNonQuery();
                        }

                        transaction.Commit();
                        return Ok(new { message = "Consulta y receta guardadas exitosamente." });
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return StatusCode(500, new { error = "Error al guardar la consulta. Operación cancelada. Detalles: " + ex.Message });
                    }
                }
            }
        }
    }
}