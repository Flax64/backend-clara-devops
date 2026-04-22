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
        [HttpGet("ventas")]
        public IActionResult GetReporteVentas(DateTime inicio, DateTime fin)
        {
            List<object> ventas = new List<object>();
            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                // Filtramos por el rango de fechas que mande el usuario
                string query = @"SELECT id_Venta, fecha_Venta, hora_Venta, nombre_Cliente, total_Venta 
                         FROM VENTAS 
                         WHERE fecha_Venta BETWEEN @inicio AND @fin 
                         AND id_Estatus = 1 
                         ORDER BY fecha_Venta DESC, hora_Venta DESC";

                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@inicio", inicio.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@fin", fin.ToString("yyyy-MM-dd"));

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ventas.Add(new
                            {
                                Folio = reader["id_Venta"],
                                Fecha = Convert.ToDateTime(reader["fecha_Venta"]).ToShortDateString(),
                                Hora = reader["hora_Venta"].ToString(),
                                Cliente = reader["nombre_Cliente"]?.ToString() ?? "Público General",
                                Total = reader["total_Venta"]
                            });
                        }
                    }
                }
            }
            return Ok(ventas);
        }
        [HttpGet("inventario")]
        public IActionResult GetReporteInventario()
        {
            List<object> inventario = new List<object>();
            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                // Traemos los datos de stock y precios de MEDICAMENTOS
                string query = @"SELECT id_Medicamento, nombre_Medicamento, stock_Medicamento, 
                                precio_Medicamento, concentracion_Valor, concentracion_Unidad 
                         FROM MEDICAMENTOS WHERE id_Estatus = 1 
                         ORDER BY stock_Medicamento ASC"; // Los que tienen menos stock salen primero

                using (MySqlCommand cmd = new MySqlCommand(query, conn))
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int stock = Convert.ToInt32(reader["stock_Medicamento"]);

                        inventario.Add(new
                        {
                            Id = reader["id_Medicamento"],
                            Nombre = reader["nombre_Medicamento"],
                            Concentracion = $"{reader["concentracion_Valor"]} {reader["concentracion_Unidad"]}",
                            Precio = reader["precio_Medicamento"],
                            Stock = stock,
                            // Lógica para ayudar al administrador a decidir compras
                            Alerta = stock <= 5 ? "REABASTECER" : "OK"
                        });
                    }
                }
            }
            return Ok(inventario);
        }
    }

}
