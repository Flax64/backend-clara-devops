using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReportesController : ControllerBase
    {
        private readonly string _connectionString = ConexionDB.Cadena;

        // 1. Obtener lista general de pacientes
        [HttpGet("expedientes")]
        public IActionResult GetExpedientes()
        {
            try
            {
                List<object> lista = new List<object>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    // Usamos el texto 'Activo' en lugar del número mágico 1
                    string query = @"SELECT p.id_Paciente, u.nombre_Usuario, u.apellido_P, u.apellido_M, u.telefono, u.email_Usuario 
                                     FROM pacientes p
                                     INNER JOIN usuarios u ON p.id_Usuario = u.id_Usuario
                                     INNER JOIN estatus e ON u.id_Estatus = e.id_Estatus
                                     WHERE e.nombre = 'Activo'";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            lista.Add(new
                            {
                                id = reader["id_Paciente"],
                                nombreCompleto = $"{reader["nombre_Usuario"]} {reader["apellido_P"]} {reader["apellido_M"]}".Trim(),
                                telefono = reader["telefono"]?.ToString() ?? "Sin teléfono",
                                correo = reader["email_Usuario"]?.ToString() ?? "Sin correo"
                            });
                        }
                    }
                }
                return Ok(lista);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al cargar expedientes: " + ex.Message });
            }
        }

        // 2. Obtener historial médico
        [HttpGet("historial/{id}")]
        public IActionResult GetHistorialPaciente(int id)
        {
            try
            {
                List<object> historial = new List<object>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"SELECT ci.fecha_Cita, co.sintomas_Consulta, co.diagnostico_Consulta, co.peso, co.altura
                                     FROM citas ci
                                     INNER JOIN consultas co ON ci.id_Cita = co.id_Cita
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
                                    Fecha = Convert.ToDateTime(reader["fecha_Cita"]).ToString("dd/MM/yyyy"),
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
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al cargar historial: " + ex.Message });
            }
        }

        // 3. Obtener reporte de ventas
        [HttpGet("ventas")]
        public IActionResult GetReporteVentas([FromQuery] DateTime inicio, [FromQuery] DateTime fin)
        {
            try
            {
                List<object> ventas = new List<object>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    // Modificamos el JOIN para incluir la tabla de usuarios y traer el nombre del vendedor
                    string query = @"SELECT v.id_Venta, v.fecha_Venta, v.hora_Venta, v.nombre_Cliente, v.total_Venta, 
                                            CONCAT(u.nombre_Usuario, ' ', u.apellido_P) AS nombre_Vendedor
                                     FROM ventas v
                                     INNER JOIN estatus e ON v.id_Estatus = e.id_Estatus
                                     INNER JOIN usuarios u ON v.id_Usuario = u.id_Usuario
                                     WHERE v.fecha_Venta BETWEEN @inicio AND @fin 
                                     AND e.nombre = 'Completada' 
                                     ORDER BY v.fecha_Venta DESC, v.hora_Venta DESC";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@inicio", inicio.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@fin", fin.ToString("yyyy-MM-dd"));

                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string horaFormateada = "--:--";
                                if (reader["hora_Venta"] != DBNull.Value)
                                {
                                    TimeSpan tiempo = (TimeSpan)reader["hora_Venta"];
                                    horaFormateada = new DateTime(tiempo.Ticks).ToString("hh:mm tt");
                                }

                                ventas.Add(new
                                {
                                    Folio = reader["id_Venta"],
                                    Fecha = Convert.ToDateTime(reader["fecha_Venta"]).ToString("dd/MM/yyyy"),
                                    Hora = horaFormateada,
                                    Cliente = reader["nombre_Cliente"]?.ToString() ?? "Público General",
                                    Vendedor = reader["nombre_Vendedor"].ToString(), // ✨ LO AGREGAMOS AL JSON
                                    Total = reader["total_Venta"]
                                });
                            }
                        }
                    }
                }
                return Ok(ventas);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al generar reporte de ventas: " + ex.Message });
            }
        }

        // 4. Obtener reporte de inventario
        [HttpGet("inventario")]
        public IActionResult GetReporteInventario()
        {
            try
            {
                List<object> inventario = new List<object>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    // Usamos estatus 'Activo' con INNER JOIN
                    string query = @"SELECT m.id_Medicamento, m.nombre_Medicamento, m.stock_Medicamento, 
                                            m.precio_Medicamento, m.concentracion_Valor, m.concentracion_Unidad 
                                     FROM medicamentos m
                                     INNER JOIN estatus e ON m.id_Estatus = e.id_Estatus
                                     WHERE e.nombre = 'Activo' 
                                     ORDER BY m.stock_Medicamento ASC";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int stock = Convert.ToInt32(reader["stock_Medicamento"]);

                            // Concatenación segura por si hay nulos en concentración
                            string concentracion = "";
                            if (reader["concentracion_Valor"] != DBNull.Value && reader["concentracion_Unidad"] != DBNull.Value)
                            {
                                decimal valor = Convert.ToDecimal(reader["concentracion_Valor"]);
                                concentracion = $" ({valor.ToString("0.##")}{reader["concentracion_Unidad"]})";
                            }

                            inventario.Add(new
                            {
                                Id = reader["id_Medicamento"],
                                Nombre = reader["nombre_Medicamento"].ToString() + concentracion,
                                Precio = reader["precio_Medicamento"],
                                Stock = stock,
                                Alerta = stock <= 5 ? "REABASTECER" : "OK"
                            });
                        }
                    }
                }
                return Ok(inventario);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al cargar inventario: " + ex.Message });
            }
        }
    }
}