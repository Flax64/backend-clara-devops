using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;

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

        // 2. Obtener historial médico COMPLETO (Con Receta)
        [HttpGet("historial/{id}")]
        public IActionResult GetHistorialPaciente(int id)
        {
            try
            {
                // ✨ Usamos un Diccionario para agrupar las recetas dentro de su respectiva consulta
                var historialDict = new Dictionary<int, Dictionary<string, object>>();

                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    // ✨ Hacemos LEFT JOIN para incluir medicamentos y la tabla medicos/usuarios para saber quién lo atendió.
                    string query = @"SELECT 
                                        co.id_Consulta, 
                                        ci.fecha_Cita, 
                                        ci.hora_Cita,
                                        co.sintomas_Consulta, 
                                        co.diagnostico_Consulta, 
                                        co.observaciones_Consulta, 
                                        co.peso, 
                                        co.altura,
                                        CONCAT(u.nombre_Usuario, ' ', u.apellido_P) AS medico_Atendio,
                                        m.nombre_Medicamento, 
                                        m.concentracion_Valor, 
                                        m.concentracion_Unidad,
                                        dr.dosis, 
                                        dr.frecuencia, 
                                        dr.duracion
                                     FROM citas ci
                                     INNER JOIN consultas co ON ci.id_Cita = co.id_Cita
                                     INNER JOIN medicos med ON ci.id_Medico = med.id_Medico
                                     INNER JOIN usuarios u ON med.id_Usuario = u.id_Usuario
                                     LEFT JOIN detalle_receta dr ON co.id_Consulta = dr.id_Consulta
                                     LEFT JOIN medicamentos m ON dr.id_Medicamento = m.id_Medicamento
                                     WHERE ci.id_Paciente = @id 
                                     ORDER BY ci.fecha_Cita DESC, ci.hora_Cita DESC";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int idConsulta = Convert.ToInt32(reader["id_Consulta"]);

                                // 1. Si es la primera vez que leemos esta consulta, creamos su "expediente" principal
                                if (!historialDict.ContainsKey(idConsulta))
                                {
                                    string horaFormateada = "--:--";
                                    if (reader["hora_Cita"] != DBNull.Value)
                                    {
                                        TimeSpan tiempo = (TimeSpan)reader["hora_Cita"];
                                        horaFormateada = new DateTime(tiempo.Ticks).ToString("hh:mm tt");
                                    }

                                    historialDict[idConsulta] = new Dictionary<string, object>
                                    {
                                        { "IdConsulta", idConsulta },
                                        { "Fecha", Convert.ToDateTime(reader["fecha_Cita"]).ToString("dd/MM/yyyy") },
                                        { "Hora", horaFormateada },
                                        { "Medico", reader["medico_Atendio"].ToString() },
                                        { "Sintomas", reader["sintomas_Consulta"].ToString() },
                                        { "Diagnostico", reader["diagnostico_Consulta"].ToString() },
                                        { "Observaciones", reader["observaciones_Consulta"]?.ToString() ?? "Sin observaciones" },
                                        { "Peso", reader["peso"] },
                                        { "Altura", reader["altura"] },
                                        { "Receta", new List<object>() } // ✨ Creamos una lista vacía para guardar los medicamentos de esta consulta
                                    };
                                }

                                // 2. Verificamos si en esta fila viene un medicamento adjunto (Gracias al LEFT JOIN)
                                if (reader["nombre_Medicamento"] != DBNull.Value)
                                {
                                    decimal valor = Convert.ToDecimal(reader["concentracion_Valor"]);
                                    string concentracion = $"({valor:0.##}{reader["concentracion_Unidad"]})";

                                    // Obtenemos la lista de receta de esta consulta específica y le agregamos la medicina
                                    var recetaList = (List<object>)historialDict[idConsulta]["Receta"];
                                    recetaList.Add(new
                                    {
                                        Medicamento = $"{reader["nombre_Medicamento"]} {concentracion}",
                                        Dosis = reader["dosis"].ToString(),
                                        Frecuencia = reader["frecuencia"].ToString(),
                                        Duracion = reader["duracion"].ToString()
                                    });
                                }
                            }
                        }
                    }
                }

                // Convertimos el diccionario a una lista simple para enviarlo limpiamente al frontend
                var historialFinal = historialDict.Values.ToList();
                return Ok(historialFinal);
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