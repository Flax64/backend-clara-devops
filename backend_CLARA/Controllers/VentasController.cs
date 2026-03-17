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
                    ORDER BY v.fecha_Venta DESC, v.id_Venta ASC ";

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
        // Ruta DELETE para eliminar una venta por su ID
        [HttpDelete("{id}")]
        public IActionResult EliminarVenta(int id)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    // ⚠️ NOTA IMPORTANTE DE BASE DE DATOS: 
                    // Según tu diagrama, una Venta tiene "Detalles de Venta". 
                    // Para poder borrar la venta principal, primero debemos borrar sus detalles 
                    // (los medicamentos que se vendieron en ese ticket) para no violar la llave foránea.

                    // 1. ¡NUEVO!: Primero regresamos el stock al inventario antes de borrar los registros
                    string queryDevolverStock = "UPDATE MEDICAMENTOS m INNER JOIN DETALLE_VENTA dv ON m.id_Medicamento = dv.id_Medicamento SET m.stock_Medicamento = m.stock_Medicamento + dv.cantidad WHERE dv.id_Venta = @id";
                    using (MySqlCommand cmdDevolver = new MySqlCommand(queryDevolverStock, conn))
                    {
                        cmdDevolver.Parameters.AddWithValue("@id", id);
                        cmdDevolver.ExecuteNonQuery();
                    }

                    // 2. Ahora sí, borramos los detalles (los hijos)
                    string queryDetalles = "DELETE FROM DETALLE_VENTA WHERE id_Venta = @id";
                    using (MySqlCommand cmdDetalles = new MySqlCommand(queryDetalles, conn))
                    {
                        cmdDetalles.Parameters.AddWithValue("@id", id);
                        cmdDetalles.ExecuteNonQuery();
                    }

                    // 3. Ahora sí, borramos la venta principal (el padre)
                    string queryVenta = "DELETE FROM VENTAS WHERE id_Venta = @id";
                    using (MySqlCommand cmdVenta = new MySqlCommand(queryVenta, conn))
                    {
                        cmdVenta.Parameters.AddWithValue("@id", id);

                        int filasAfectadas = cmdVenta.ExecuteNonQuery();

                        // Verificamos si realmente se borró algo
                        if (filasAfectadas > 0)
                        {
                            return Ok(new { message = "Venta eliminada correctamente." });
                        }
                        else
                        {
                            // Si el ID no existía, devolvemos un error 404
                            return NotFound(new { message = "No se encontró la venta con ese ID." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al intentar eliminar la venta.", error = ex.Message });
            }
        }

        [HttpPost("crear")]
        public IActionResult CrearVenta([FromBody] Models.NuevaVentaRequest request)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    // Iniciar transacción (para que si falla el detalle, no se guarde la venta incompleta)
                    using (MySqlTransaction transaccion = conn.BeginTransaction())
                    {
                        try
                        {
                            // 1. Insertar la Venta principal
                            // Asumo que id_Estatus 1 es "Completada"
                            string queryVenta = @"INSERT INTO VENTAS (id_Estatus, id_Metodo, id_Usuario, fecha_Venta, total_Venta, nombre_Cliente) 
                                          VALUES (1, @metodo, @usuario, CURDATE(), @total, @cliente);
                                          SELECT LAST_INSERT_ID();"; // Pedimos el ID recién creado

                            int nuevoIdVenta = 0;
                            using (MySqlCommand cmdVenta = new MySqlCommand(queryVenta, conn, transaccion))
                            {
                                cmdVenta.Parameters.AddWithValue("@metodo", request.IdMetodoPago);
                                cmdVenta.Parameters.AddWithValue("@usuario", request.IdUsuario);
                                cmdVenta.Parameters.AddWithValue("@total", request.TotalVenta);
                                cmdVenta.Parameters.AddWithValue("@cliente", request.NombreCliente);

                                nuevoIdVenta = Convert.ToInt32(cmdVenta.ExecuteScalar());
                            }

                            // 2. Insertar los detalles (el carrito) y descontar stock
                            string queryDetalle = "INSERT INTO DETALLE_VENTA (id_Venta, id_Medicamento, cantidad) VALUES (@idVenta, @idMed, @cant)";

                            // ¡LA MAGIA DEL INVENTARIO!: Restamos de la tabla de medicamentos
                            string queryDescontarStock = "UPDATE MEDICAMENTOS SET stock_Medicamento = stock_Medicamento - @cant WHERE id_Medicamento = @idMed";

                            foreach (var item in request.Detalles)
                            {
                                // A) Insertamos el ticket
                                using (MySqlCommand cmdDet = new MySqlCommand(queryDetalle, conn, transaccion))
                                {
                                    cmdDet.Parameters.AddWithValue("@idVenta", nuevoIdVenta);
                                    cmdDet.Parameters.AddWithValue("@idMed", item.IdMedicamento);
                                    cmdDet.Parameters.AddWithValue("@cant", item.Cantidad);
                                    cmdDet.ExecuteNonQuery();
                                }

                                // B) Descontamos del inventario general
                                using (MySqlCommand cmdStock = new MySqlCommand(queryDescontarStock, conn, transaccion))
                                {
                                    cmdStock.Parameters.AddWithValue("@cant", item.Cantidad);
                                    cmdStock.Parameters.AddWithValue("@idMed", item.IdMedicamento);
                                    cmdStock.ExecuteNonQuery();
                                }
                            }

                            // Si todo salió bien, guardamos definitivamente (Commit)
                            transaccion.Commit();
                            return Ok(new { message = "Venta registrada exitosamente.", idGenerado = nuevoIdVenta });
                        }
                        catch (Exception)
                        {
                            transaccion.Rollback(); // Si hubo error, deshacemos todo para evitar basura en la BD
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al crear la venta.", error = ex.Message });
            }
        }

        // =======================================================
        // 1. GET: TRAER UNA VENTA ESPECÍFICA CON SU CARRITO
        // =======================================================
        [HttpGet("{id}")]
        public IActionResult ObtenerVentaPorId(int id)
        {
            try
            {
                VentaCompletaDTO venta = new VentaCompletaDTO();

                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    // A) Traer los datos principales de la venta
                    string queryVenta = "SELECT id_Venta, nombre_Cliente, id_Metodo FROM VENTAS WHERE id_Venta = @id";
                    using (MySqlCommand cmd = new MySqlCommand(queryVenta, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                venta.IdVenta = Convert.ToInt32(reader["id_Venta"]);
                                venta.NombreCliente = reader["nombre_Cliente"].ToString();
                                venta.IdMetodoPago = Convert.ToInt32(reader["id_Metodo"]);
                            }
                            else
                            {
                                return NotFound("Venta no encontrada.");
                            }
                        }
                    }

                    // B) Traer los medicamentos de esa venta (El Carrito)
                    // ¡NUEVO!: También pedimos la concentración aquí
                    string queryDetalle = @"
    SELECT 
        dv.id_Medicamento, 
        m.nombre_Medicamento, 
        m.concentracion_Valor, 
        m.concentracion_Unidad, 
        SUM(dv.cantidad) AS cantidad, 
        m.precio_Medicamento 
    FROM DETALLE_VENTA dv
    INNER JOIN MEDICAMENTOS m ON dv.id_Medicamento = m.id_Medicamento
    WHERE dv.id_Venta = @id
    GROUP BY 
        dv.id_Medicamento, 
        m.nombre_Medicamento, 
        m.concentracion_Valor, 
        m.concentracion_Unidad, 
        m.precio_Medicamento";

                    using (MySqlCommand cmdDet = new MySqlCommand(queryDetalle, conn))
                    {
                        cmdDet.Parameters.AddWithValue("@id", id);
                        using (MySqlDataReader readerDet = cmdDet.ExecuteReader())
                        {
                            while (readerDet.Read())
                            {
                                string nombreBase = readerDet["nombre_Medicamento"].ToString();
                                string nombreMostrado = nombreBase;

                                if (readerDet["concentracion_Valor"] != DBNull.Value && readerDet["concentracion_Unidad"] != DBNull.Value)
                                {
                                    nombreMostrado = $"{nombreBase} ({readerDet["concentracion_Valor"]}{readerDet["concentracion_Unidad"]})";
                                }

                                int cant = Convert.ToInt32(readerDet["cantidad"]);
                                decimal precio = Convert.ToDecimal(readerDet["precio_Medicamento"]);

                                venta.Detalles.Add(new FilaCarritoDTO
                                {
                                    IdProducto = Convert.ToInt32(readerDet["id_Medicamento"]),
                                    Producto = nombreMostrado, // Mandamos el nombre concatenado al carrito de VB
                                    Cant = cant,
                                    P_Unit = precio,
                                    Subtotal = cant * precio
                                });
                            }
                        }
                    }
                }
                return Ok(venta);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al obtener la venta.", error = ex.Message });
            }
        }

        // =======================================================
        // 2. PUT: ACTUALIZAR LA VENTA 
        // =======================================================
        [HttpPut("{id}")]
        public IActionResult ActualizarVenta(int id, [FromBody] Models.NuevaVentaRequest request)
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
                            // A) Actualizamos los datos principales (Cliente, Total, Método)
                            string queryUpdate = "UPDATE VENTAS SET nombre_Cliente = @cliente, id_Metodo = @metodo, total_Venta = @total WHERE id_Venta = @id";
                            using (MySqlCommand cmd = new MySqlCommand(queryUpdate, conn, transaccion))
                            {
                                cmd.Parameters.AddWithValue("@cliente", request.NombreCliente);
                                cmd.Parameters.AddWithValue("@metodo", request.IdMetodoPago);
                                cmd.Parameters.AddWithValue("@total", request.TotalVenta);
                                cmd.Parameters.AddWithValue("@id", id);
                                cmd.ExecuteNonQuery();
                            }

                            // B) ¡TRUCO!: Regresamos el stock viejo al inventario antes de borrar los detalles
                            string queryDevolverStock = "UPDATE MEDICAMENTOS m INNER JOIN DETALLE_VENTA dv ON m.id_Medicamento = dv.id_Medicamento SET m.stock_Medicamento = m.stock_Medicamento + dv.cantidad WHERE dv.id_Venta = @id";
                            using (MySqlCommand cmdDevolver = new MySqlCommand(queryDevolverStock, conn, transaccion))
                            {
                                cmdDevolver.Parameters.AddWithValue("@id", id);
                                cmdDevolver.ExecuteNonQuery();
                            }

                            // C) AHORA SÍ: Borramos los detalles viejos de la BD
                            string queryBorrarDetalles = "DELETE FROM DETALLE_VENTA WHERE id_Venta = @id";
                            using (MySqlCommand cmdBorrar = new MySqlCommand(queryBorrarDetalles, conn, transaccion))
                            {
                                cmdBorrar.Parameters.AddWithValue("@id", id);
                                cmdBorrar.ExecuteNonQuery();
                            }

                            // D) Insertamos los nuevos detalles y descontamos el stock nuevo
                            string queryInsertarDetalle = "INSERT INTO DETALLE_VENTA (id_Venta, id_Medicamento, cantidad) VALUES (@idVenta, @idMed, @cant)";
                            string queryDescontarNuevoStock = "UPDATE MEDICAMENTOS SET stock_Medicamento = stock_Medicamento - @cant WHERE id_Medicamento = @idMed";

                            foreach (var item in request.Detalles)
                            {
                                // 1. Insertamos el nuevo ticket
                                using (MySqlCommand cmdIn = new MySqlCommand(queryInsertarDetalle, conn, transaccion))
                                {
                                    cmdIn.Parameters.AddWithValue("@idVenta", id);
                                    cmdIn.Parameters.AddWithValue("@idMed", item.IdMedicamento);
                                    cmdIn.Parameters.AddWithValue("@cant", item.Cantidad);
                                    cmdIn.ExecuteNonQuery();
                                }

                                // 2. Descontamos lo nuevo del inventario
                                using (MySqlCommand cmdStock = new MySqlCommand(queryDescontarNuevoStock, conn, transaccion))
                                {
                                    cmdStock.Parameters.AddWithValue("@cant", item.Cantidad);
                                    cmdStock.Parameters.AddWithValue("@idMed", item.IdMedicamento);
                                    cmdStock.ExecuteNonQuery();
                                }
                            } // <--- AQUÍ TERMINA EL FOREACH

                            // E) ESTO VA AFUERA DEL FOREACH: Guardamos los cambios y respondemos
                            transaccion.Commit();
                            return Ok(new { message = "Venta actualizada correctamente." });
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
                return StatusCode(500, new { message = "Error al actualizar.", error = ex.Message });
            }
        }

        [HttpGet("metodos-pago")]
        public IActionResult ObtenerMetodosPago()
        {
            List<MetodoPagoDTO> metodos = new List<MetodoPagoDTO>();
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    // Traemos el ID y el Nombre de tu tabla METODOS_PAGO
                    string query = "SELECT id_Metodo, nombre FROM METODOS_PAGO";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                metodos.Add(new MetodoPagoDTO
                                {
                                    Id = Convert.ToInt32(reader["id_Metodo"]),
                                    Nombre = reader["nombre"].ToString()
                                });
                            }
                        }
                    }
                }
                return Ok(metodos);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al cargar métodos.", error = ex.Message });
            }
        }

        // Ruta GET para pedir el catálogo de medicamentos para el buscador
        [HttpGet("medicamentos")]
        public IActionResult ObtenerMedicamentosBuscador()
        {
            try
            {
                List<object> catalogo = new List<object>();

                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = "SELECT id_Medicamento, nombre_Medicamento, concentracion_Valor, concentracion_Unidad, precio_Medicamento, stock_Medicamento FROM MEDICAMENTOS WHERE stock_Medicamento > 0";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string nombreBase = reader["nombre_Medicamento"].ToString();
                            string nombreMostrado = nombreBase;

                            if (reader["concentracion_Valor"] != DBNull.Value && reader["concentracion_Unidad"] != DBNull.Value)
                            {
                                // TRUCO PARA QUITAR LOS CEROS: Convertimos a decimal y formateamos con "0.##"
                                decimal valorDecimal = Convert.ToDecimal(reader["concentracion_Valor"]);
                                string valorLimpio = valorDecimal.ToString("0.##");
                                string unidad = reader["concentracion_Unidad"].ToString();

                                nombreMostrado = $"{nombreBase} ({valorLimpio}{unidad})";
                            }

                            catalogo.Add(new
                            {
                                Id = Convert.ToInt32(reader["id_Medicamento"]),
                                Nombre = nombreMostrado,
                                Precio = Convert.ToDecimal(reader["precio_Medicamento"]),
                                Stock = Convert.ToInt32(reader["stock_Medicamento"])
                            });
                        }
                    }
                }
                return Ok(catalogo);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al cargar catálogo.", error = ex.Message });
            }
        }


    }
}
