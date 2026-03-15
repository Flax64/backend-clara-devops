using backend_CLARA.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Collections.Generic;

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VentasController : ControllerBase
    {
        private readonly string _connectionString = "Server=localhost; Database=farmacia; Uid=root; Pwd=KameHameH4!";

        // Ruta GET para obtener todas las ventas
        [HttpGet("lista")]
        public IActionResult ObtenerVentas()
        {
            List<VentaDTO> listaVentas = new List<VentaDTO>();

            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    // ¡La consulta SQL adaptada exactamente a tu diagrama!
                    string query = @"
                    SELECT v.id_Venta, v.fecha_Venta, v.nombre_Cliente, CONCAT(u.nombre_Usuario, ' ', u.apellido_P) AS nombre_Vendedor, 
                    v.total_Venta, m.nombre AS metodo_Pago
                    FROM VENTAS v
                    INNER JOIN USUARIOS u ON v.id_Usuario = u.id_Usuario
                    INNER JOIN METODOS_PAGO m ON v.id_Metodo = m.id_Metodo
                    ORDER BY v.fecha_Venta DESC";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                listaVentas.Add(new VentaDTO
                                {
                                    Id = Convert.ToInt32(reader["id_Venta"]),
                                    // Como es tipo DATE, lo formateamos a Día/Mes/Año
                                    FechaHora = Convert.ToDateTime(reader["fecha_Venta"]).ToString("dd/MM/yyyy"),
                                    Cliente = reader["nombre_Cliente"].ToString(),
                                    Vendedor = reader["nombre_Vendedor"].ToString(),
                                    Total = Convert.ToDecimal(reader["total_Venta"]),
                                    Metodo = reader["metodo_Pago"].ToString()
                                });
                            }
                        }
                    }
                }
                return Ok(listaVentas);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al obtener las ventas.", error = ex.Message });
            }
        }
    }
}
