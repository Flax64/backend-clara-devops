using backend_CLARA.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ComprasController : ControllerBase
    {
        // Usamos tu misma cadena de conexión de Ventas
        private readonly string _connectionString = "Server=localhost; Database=farmacia; Uid=root; Pwd=KameHameH4!";

        [HttpPost("registrar")]
        public IActionResult RegistrarCompra([FromBody] NuevaCompraRequest request)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    using (MySqlTransaction transaccion = conn.BeginTransaction())
                    {
                        try
                        {
                            // 1. Insertar en la tabla COMPRAS
                            string queryCompra = @"INSERT INTO COMPRAS (id_Estatus, id_Proveedor, fecha_Compra, hora_Compra, total_Compra) 
                                                 VALUES (5, @idProv, CURDATE(), CURTIME(), @total);
                                                 SELECT LAST_INSERT_ID();";

                            int nuevoIdCompra = 0;
                            using (MySqlCommand cmd = new MySqlCommand(queryCompra, conn, transaccion))
                            {
                                cmd.Parameters.AddWithValue("@idProv", request.IdProveedor);
                                cmd.Parameters.AddWithValue("@total", request.TotalCompra);
                                nuevoIdCompra = Convert.ToInt32(cmd.ExecuteScalar());
                            }

                            // 2. Insertar detalles y SUMAR al stock de MEDICAMENTOS
                            string queryDetalle = "INSERT INTO DETALLE_COMPRA (id_Compra, id_Medicamento, cantidad) VALUES (@idCompra, @idMed, @cant)";
                            string querySumarStock = "UPDATE MEDICAMENTOS SET stock_Medicamento = stock_Medicamento + @cant WHERE id_Medicamento = @idMed";

                            foreach (var item in request.Detalles)
                            {
                                // A) Registrar cada medicamento en el detalle de la compra
                                using (MySqlCommand cmdDet = new MySqlCommand(queryDetalle, conn, transaccion))
                                {
                                    cmdDet.Parameters.AddWithValue("@idCompra", nuevoIdCompra);
                                    cmdDet.Parameters.AddWithValue("@idMed", item.IdMedicamento);
                                    cmdDet.Parameters.AddWithValue("@cant", item.Cantidad);
                                    cmdDet.ExecuteNonQuery();
                                }

                                // B) Aumentar el inventario físico
                                using (MySqlCommand cmdStock = new MySqlCommand(querySumarStock, conn, transaccion))
                                {
                                    cmdStock.Parameters.AddWithValue("@cant", item.Cantidad);
                                    cmdStock.Parameters.AddWithValue("@idMed", item.IdMedicamento);
                                    cmdStock.ExecuteNonQuery();
                                }
                            }

                            transaccion.Commit();
                            return Ok(new { message = "Compra registrada y stock actualizado con éxito.", id = nuevoIdCompra });
                        }
                        catch (Exception)
                        {
                            transaccion.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al procesar la compra.", error = ex.Message });
            }
        }
    }
}
