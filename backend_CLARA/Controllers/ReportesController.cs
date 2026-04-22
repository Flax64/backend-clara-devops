using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReportesController : ControllerBase
    {
        private readonly string _connectionString = ConexionDB.Cadena;

        // 1. Obtener lista general de pacientes (Uniendo con la tabla USUARIOS)
        [HttpGet("expedientes")]
        public IActionResult GetExpedientes()
        {
            List<object> lista = new List<object>();
            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                // En tu DB, el nombre y apellidos están en la tabla USUARIOS
                string query = @"SELECT p.id_Paciente, u.nombre_Usuario, u.apellido_P, u.apellido_M, u.telefono, u.email_Usuario 
                                 FROM PACIENTES p
                                 INNER JOIN USUARIOS u ON p.id_Usuario = u.id_Usuario
                                 WHERE u.id_Estatus = 1";

                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lista.Add(new
                        {
                            id = reader["id_Paciente"],
                            nombreCompleto = $"{reader["nombre_Usuario"]} {reader["apellido_P"]} {reader["apellido_M"]}",
                            telefono = reader["telefono"],
                            correo = reader["email_Usuario"]
                        });
                    }
                }
            }
            return Ok(lista);
        }

        // 2. Obtener historial médico (Uniendo CITAS con CONSULTAS)
        [HttpGet("historial/{id}")]
        public IActionResult GetHistorialPaciente(int id)
        {
            List<object> historial = new List<object>();
            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                // En tu DB, el diagnóstico y peso están en la tabla CONSULTAS
                string query = @"SELECT ci.fecha_Cita, co.sintomas_Consulta, co.diagnostico_Consulta, co.peso, co.altura
                                 FROM CITAS ci
                                 INNER JOIN CONSULTAS co ON ci.id_Cita = co.id_Cita
                                 WHERE ci.id_Paciente = @id 
                                 ORDER BY ci.fecha_Cita DESC";

                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            historial.Add(new
                            {
                                // Estos nombres deben coincidir con tu clase FilaHistorial de VB.NET
                                Fecha = reader["fecha_Cita"],
                                Sintomas = reader["sintomas_Consulta"],
                                Diagnostico = reader["diagnostico_Consulta"],
                                Peso = reader["peso"],
                                Altura = reader["altura"]
                            });
                        }
                    }
                }
            }
            return Ok(historial);
        }
    }
}
