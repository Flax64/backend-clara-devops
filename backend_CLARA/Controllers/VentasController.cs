using backend_CLARA.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Transactions;

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VentasController : ControllerBase
    {
        private readonly string _connectionString = ConexionDB.Cadena;

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
                    string query = @"SELECT v.id_Venta, v.fecha_Venta, v.hora_Venta, v.nombre_Cliente, CONCAT(u.nombre_Usuario, ' ', u.apellido_P) AS nombre_Vendedor, 
                                    v.total_Venta, m.nombre AS metodo_Pago, e.nombre AS nombre_Estatus
                                    FROM ventas v
                                    INNER JOIN usuarios u ON v.id_Usuario = u.id_Usuario
                                    INNER JOIN metodos_pago m ON v.id_Metodo = m.id_Metodo
                                    INNER JOIN estatus e ON v.id_Estatus = e.id_Estatus
                                    ORDER BY v.fecha_Venta DESC, v.hora_Venta DESC, v.id_Venta ASC ";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
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

                                listaVentas.Add(new VentaDTO
                                {
                                    Id = Convert.ToInt32(reader["id_Venta"]),
                                    Fecha = Convert.ToDateTime(reader["fecha_Venta"]).ToString("dd/MM/yyyy"),
                                    Hora = horaFormateada,
                                    Cliente = reader["nombre_Cliente"].ToString(),
                                    Vendedor = reader["nombre_Vendedor"].ToString(),
                                    Total = Convert.ToDecimal(reader["total_Venta"]),
                                    Metodo = reader["metodo_Pago"].ToString(),
                                    Estatus = reader["nombre_Estatus"].ToString()
                                });
                            }
                        }
                    }
                }
                return Ok(listaVentas);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Ocurrió un problema al cargar la lista de ventas. Detalles: " + ex.Message });
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
                    using (MySqlTransaction transaccion = conn.BeginTransaction())
                    {
                        try
                        {
                            // REGLA: NO CANCELAR DESPUÉS DE 24 HORAS
                            using (var cmdFecha = new MySqlCommand("SELECT fecha_Venta FROM ventas WHERE id_Venta = @id", conn, transaccion))
                            {
                                cmdFecha.Parameters.AddWithValue("@id", id);
                                var resFecha = cmdFecha.ExecuteScalar();
                                if (resFecha == null) return NotFound(new { error = "La venta no existe." });

                                DateTime fechaVenta = Convert.ToDateTime(resFecha);
                                if ((DateTime.Today - fechaVenta.Date).TotalDays > 1)
                                {
                                    return BadRequest(new { error = "El corte de caja ya pasó. No se puede cancelar una venta después de 24 horas." });
                                }
                            }
                            int estatusActual = 0;
                            using (var cmdCheck = new MySqlCommand("SELECT id_Estatus FROM ventas WHERE id_Venta = @id", conn, transaccion))
                            {
                                cmdCheck.Parameters.AddWithValue("@id", id);
                                var result = cmdCheck.ExecuteScalar();
                                if (result == null) return NotFound(new { message = "Venta no encontrada." });
                                estatusActual = Convert.ToInt32(result);
                            }

                            // Obtenemos el ID de "Cancelada" dinámicamente
                            int idEstatusCancelada = 0;
                            using (var cmdCheckStatus = new MySqlCommand("SELECT id_Estatus FROM estatus WHERE nombre = 'Cancelada' LIMIT 1", conn, transaccion))
                            {
                                idEstatusCancelada = Convert.ToInt32(cmdCheckStatus.ExecuteScalar());
                            }

                            if (estatusActual == idEstatusCancelada)
                                return BadRequest(new { message = "Esta venta ya se encuentra cancelada." });

                            // PASO 0: Ver si tenía consulta ligada
                            int? idConsultaRevertir = null;
                            using (MySqlCommand cmdGet = new MySqlCommand("SELECT id_Consulta FROM ventas WHERE id_Venta = @id", conn, transaccion))
                            {
                                cmdGet.Parameters.AddWithValue("@id", id);
                                object res = cmdGet.ExecuteScalar();
                                if (res != null && res != DBNull.Value) idConsultaRevertir = Convert.ToInt32(res);
                            }

                            // 1. Regresamos el stock al inventario
                            string queryDevolverStock = "UPDATE medicamentos m INNER JOIN detalle_venta dv ON m.id_Medicamento = dv.id_Medicamento SET m.stock_Medicamento = m.stock_Medicamento + dv.cantidad WHERE dv.id_Venta = @id";
                            using (MySqlCommand cmdDevolver = new MySqlCommand(queryDevolverStock, conn, transaccion))
                            {
                                cmdDevolver.Parameters.AddWithValue("@id", id);
                                cmdDevolver.ExecuteNonQuery();
                            }

                            // 2. ACTUALIZAMOS EL ESTATUS A CANCELADA
                            string queryCancelarVenta = "UPDATE ventas SET id_Estatus = @estatus WHERE id_Venta = @id";
                            using (MySqlCommand cmdVenta = new MySqlCommand(queryCancelarVenta, conn, transaccion))
                            {
                                cmdVenta.Parameters.AddWithValue("@estatus", idEstatusCancelada);
                                cmdVenta.Parameters.AddWithValue("@id", id);
                                cmdVenta.ExecuteNonQuery();
                            }

                            // 3. CORRECCIÓN: Si tenía consulta, la regresamos a estado "Activo" para poder volver a surtirla
                            if (idConsultaRevertir.HasValue)
                            {
                                string queryRevConsulta = "UPDATE consultas SET id_Estatus = (SELECT id_Estatus FROM estatus WHERE nombre = 'Activo' LIMIT 1) WHERE id_Consulta = @idConsulta";
                                using (MySqlCommand cmdRev = new MySqlCommand(queryRevConsulta, conn, transaccion))
                                {
                                    cmdRev.Parameters.AddWithValue("@idConsulta", idConsultaRevertir.Value);
                                    cmdRev.ExecuteNonQuery();
                                }
                            }

                            transaccion.Commit();
                            return Ok(new { message = "Venta cancelada exitosamente y stock revertido." });
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
                return StatusCode(500, new { error = "Ocurrió un problema al intentar cancelar la venta. Detalles: " + ex.Message });
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
                    using (MySqlTransaction transaccion = conn.BeginTransaction())
                    {
                        try
                        {
                            // 1. Insertar la Venta principal
                            string queryVenta = @"INSERT INTO ventas (id_Estatus, id_Consulta, id_Metodo, id_Usuario, fecha_Venta, hora_Venta, total_Venta, nombre_Cliente) 
                            VALUES ((SELECT id_Estatus FROM estatus WHERE nombre = 'Completada' LIMIT 1), @idConsulta, @metodo, @usuario, CURDATE(), CURTIME(), @total, @cliente);
                            SELECT LAST_INSERT_ID();";

                            int nuevoIdVenta = 0;
                            using (MySqlCommand cmdVenta = new MySqlCommand(queryVenta, conn, transaccion))
                            {
                                cmdVenta.Parameters.AddWithValue("@idConsulta", request.IdConsulta.HasValue ? request.IdConsulta.Value : (object)DBNull.Value);
                                cmdVenta.Parameters.AddWithValue("@metodo", request.IdMetodoPago);
                                cmdVenta.Parameters.AddWithValue("@usuario", request.IdUsuario);
                                cmdVenta.Parameters.AddWithValue("@total", request.TotalVenta);
                                cmdVenta.Parameters.AddWithValue("@cliente", request.NombreCliente);

                                nuevoIdVenta = Convert.ToInt32(cmdVenta.ExecuteScalar());
                            }

                            // 2. Insertar los detalles y descontar stock
                            string queryDetalle = "INSERT INTO detalle_venta (id_Venta, id_Medicamento, cantidad) VALUES (@idVenta, @idMed, @cant)";
                            string queryDescontarStock = "UPDATE medicamentos SET stock_Medicamento = stock_Medicamento - @cant WHERE id_Medicamento = @idMed";

                            foreach (var item in request.Detalles)
                            {
                                using (MySqlCommand cmdDet = new MySqlCommand(queryDetalle, conn, transaccion))
                                {
                                    cmdDet.Parameters.AddWithValue("@idVenta", nuevoIdVenta);
                                    cmdDet.Parameters.AddWithValue("@idMed", item.IdMedicamento);
                                    cmdDet.Parameters.AddWithValue("@cant", item.Cantidad);
                                    cmdDet.ExecuteNonQuery();
                                }

                                using (MySqlCommand cmdStock = new MySqlCommand(queryDescontarStock, conn, transaccion))
                                {
                                    cmdStock.Parameters.AddWithValue("@cant", item.Cantidad);
                                    cmdStock.Parameters.AddWithValue("@idMed", item.IdMedicamento);
                                    cmdStock.ExecuteNonQuery();
                                }
                            }

                            // 3. Cambiamos la consulta a 'Surtido'
                            if (request.IdConsulta.HasValue)
                            {
                                string queryUpdateConsulta = "UPDATE consultas SET id_Estatus = (SELECT id_Estatus FROM estatus WHERE nombre = 'Surtido' LIMIT 1) WHERE id_Consulta = @idConsulta";
                                using (MySqlCommand cmdConsulta = new MySqlCommand(queryUpdateConsulta, conn, transaccion))
                                {
                                    cmdConsulta.Parameters.AddWithValue("@idConsulta", request.IdConsulta.Value);
                                    cmdConsulta.ExecuteNonQuery();
                                }
                            }

                            transaccion.Commit();
                            return Ok(new { message = "Venta registrada exitosamente.", idGenerado = nuevoIdVenta });
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
                return StatusCode(500, new { error = "Ocurrió un problema al guardar la venta. Detalles: " + ex.Message });
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

                    string queryVenta = "SELECT id_Venta, nombre_Cliente, id_Metodo FROM ventas WHERE id_Venta = @id";
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

                    string queryDetalle = @"SELECT dv.id_Medicamento, m.nombre_Medicamento, m.concentracion_Valor, m.concentracion_Unidad, 
                    SUM(dv.cantidad) AS cantidad, m.precio_Medicamento FROM detalle_venta dv
                    INNER JOIN medicamentos m ON dv.id_Medicamento = m.id_Medicamento
                    WHERE dv.id_Venta = @id
                    GROUP BY dv.id_Medicamento, m.nombre_Medicamento, m.concentracion_Valor, m.concentracion_Unidad, m.precio_Medicamento";

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
                                    decimal valorDecimal = Convert.ToDecimal(readerDet["concentracion_Valor"]);
                                    nombreMostrado = $"{nombreBase} ({valorDecimal.ToString("0.##")}{readerDet["concentracion_Unidad"]})";
                                }

                                int cant = Convert.ToInt32(readerDet["cantidad"]);
                                decimal precio = Convert.ToDecimal(readerDet["precio_Medicamento"]);

                                venta.Detalles.Add(new FilaCarritoDTO
                                {
                                    IdProducto = Convert.ToInt32(readerDet["id_Medicamento"]),
                                    Producto = nombreMostrado,
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
                return StatusCode(500, new { error = "No se pudo recuperar la información de esta venta. Detalles: " + ex.Message });
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
                            // REGLA: NO EDITAR DESPUÉS DE 24 HORAS
                            using (var cmdFecha = new MySqlCommand("SELECT fecha_Venta FROM ventas WHERE id_Venta = @id", conn, transaccion))
                            {
                                cmdFecha.Parameters.AddWithValue("@id", id);
                                var resFecha = cmdFecha.ExecuteScalar();
                                if (resFecha == null) return NotFound(new { error = "La venta no existe." });

                                DateTime fechaVenta = Convert.ToDateTime(resFecha);
                                if ((DateTime.Today - fechaVenta.Date).TotalDays > 1)
                                {
                                    return BadRequest(new { error = "Por motivos de auditoría, no se puede editar una venta después de 24 horas." });
                                }
                            }
                            string queryUpdate = "UPDATE ventas SET nombre_Cliente = @cliente, id_Metodo = @metodo, total_Venta = @total WHERE id_Venta = @id";
                            using (MySqlCommand cmd = new MySqlCommand(queryUpdate, conn, transaccion))
                            {
                                cmd.Parameters.AddWithValue("@cliente", request.NombreCliente);
                                cmd.Parameters.AddWithValue("@metodo", request.IdMetodoPago);
                                cmd.Parameters.AddWithValue("@total", request.TotalVenta);
                                cmd.Parameters.AddWithValue("@id", id);
                                cmd.ExecuteNonQuery();
                            }

                            string queryDevolverStock = "UPDATE medicamentos m INNER JOIN detalle_venta dv ON m.id_Medicamento = dv.id_Medicamento SET m.stock_Medicamento = m.stock_Medicamento + dv.cantidad WHERE dv.id_Venta = @id";
                            using (MySqlCommand cmdDevolver = new MySqlCommand(queryDevolverStock, conn, transaccion))
                            {
                                cmdDevolver.Parameters.AddWithValue("@id", id);
                                cmdDevolver.ExecuteNonQuery();
                            }

                            string queryBorrarDetalles = "DELETE FROM detalle_venta WHERE id_Venta = @id";
                            using (MySqlCommand cmdBorrar = new MySqlCommand(queryBorrarDetalles, conn, transaccion))
                            {
                                cmdBorrar.Parameters.AddWithValue("@id", id);
                                cmdBorrar.ExecuteNonQuery();
                            }

                            string queryInsertarDetalle = "INSERT INTO detalle_venta (id_Venta, id_Medicamento, cantidad) VALUES (@idVenta, @idMed, @cant)";
                            string queryDescontarNuevoStock = "UPDATE medicamentos SET stock_Medicamento = stock_Medicamento - @cant WHERE id_Medicamento = @idMed";

                            foreach (var item in request.Detalles)
                            {
                                using (MySqlCommand cmdIn = new MySqlCommand(queryInsertarDetalle, conn, transaccion))
                                {
                                    cmdIn.Parameters.AddWithValue("@idVenta", id);
                                    cmdIn.Parameters.AddWithValue("@idMed", item.IdMedicamento);
                                    cmdIn.Parameters.AddWithValue("@cant", item.Cantidad);
                                    cmdIn.ExecuteNonQuery();
                                }

                                using (MySqlCommand cmdStock = new MySqlCommand(queryDescontarNuevoStock, conn, transaccion))
                                {
                                    cmdStock.Parameters.AddWithValue("@cant", item.Cantidad);
                                    cmdStock.Parameters.AddWithValue("@idMed", item.IdMedicamento);
                                    cmdStock.ExecuteNonQuery();
                                }
                            }

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
                return StatusCode(500, new { error = "Ocurrió un problema al actualizar la venta. Detalles: " + ex.Message });
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
                    string query = "SELECT id_Metodo, nombre FROM metodos_pago";

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
                return StatusCode(500, new { error = "Error al cargar la lista de métodos de pago. Detalles: " + ex.Message });
            }
        }

        [HttpGet("medicamentos")]
        public IActionResult ObtenerMedicamentosBuscador()
        {
            try
            {
                List<object> catalogo = new List<object>();

                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = "SELECT id_Medicamento, nombre_Medicamento, concentracion_Valor, concentracion_Unidad, precio_Medicamento, stock_Medicamento FROM medicamentos WHERE stock_Medicamento > 0";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string nombreBase = reader["nombre_Medicamento"].ToString();
                            string nombreMostrado = nombreBase;

                            if (reader["concentracion_Valor"] != DBNull.Value && reader["concentracion_Unidad"] != DBNull.Value)
                            {
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
                return StatusCode(500, new { error = "Error al cargar el catálogo de productos. Detalles: " + ex.Message });
            }
        }

        // =======================================================
        // GET: OBTENER TODOS LOS PACIENTES Y BUSCAR SUS RECETAS
        // =======================================================
        [HttpGet("consultas-pendientes")]
        public IActionResult ObtenerConsultasPendientes()
        {
            try
            {
                List<object> listaConsultas = new List<object>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    // Traer a todos los pacientes y, si tienen receta reciente, anexa el ID (si no, pone 0)
                    string query = @"
                    SELECT 
                        CONCAT(u.nombre_Usuario, ' ', u.apellido_P) AS nombre_Paciente,
                        COALESCE((
                            SELECT c.id_Consulta
                            FROM consultas c
                            INNER JOIN citas ci ON c.id_Cita = ci.id_Cita
                            WHERE ci.id_Paciente = p.id_Paciente
                              AND c.id_Estatus = (SELECT id_Estatus FROM estatus WHERE nombre = 'Activo' LIMIT 1) 
                              AND ci.fecha_Cita >= DATE_SUB(CURDATE(), INTERVAL 7 DAY)
                            ORDER BY c.id_Consulta DESC LIMIT 1
                        ), 0) AS id_Consulta
                    FROM pacientes p
                    INNER JOIN usuarios u ON p.id_Usuario = u.id_Usuario
                    WHERE u.id_Estatus = (SELECT id_Estatus FROM estatus WHERE nombre = 'Activo' LIMIT 1)
                    ORDER BY nombre_Paciente ASC";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            listaConsultas.Add(new
                            {
                                IdConsulta = Convert.ToInt32(reader["id_Consulta"]),
                                Nombre = reader["nombre_Paciente"].ToString()
                            });
                        }
                    }
                }
                return Ok(listaConsultas);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al obtener la lista de pacientes. Detalles: " + ex.Message });
            }
        }

        // =======================================================
        // GET: OBTENER LOS MEDICAMENTOS DE ESA RECETA
        // =======================================================
        [HttpGet("receta/{idConsulta}")]
        public IActionResult ObtenerReceta(int idConsulta)
        {
            try
            {
                List<object> receta = new List<object>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"
                SELECT dr.id_Medicamento, m.nombre_Medicamento, m.concentracion_Valor, m.concentracion_Unidad, 
                       m.precio_Medicamento, m.stock_Medicamento
                FROM detalle_receta dr
                INNER JOIN medicamentos m ON dr.id_Medicamento = m.id_Medicamento
                WHERE dr.id_Consulta = @idConsulta";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@idConsulta", idConsulta);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string nombreMostrado = reader["nombre_Medicamento"].ToString();
                                if (reader["concentracion_Valor"] != DBNull.Value && reader["concentracion_Unidad"] != DBNull.Value)
                                {
                                    decimal valorDecimal = Convert.ToDecimal(reader["concentracion_Valor"]);
                                    nombreMostrado = $"{nombreMostrado} ({valorDecimal.ToString("0.##")}{reader["concentracion_Unidad"]})";
                                }

                                receta.Add(new
                                {
                                    Id = Convert.ToInt32(reader["id_Medicamento"]),
                                    Nombre = nombreMostrado,
                                    Precio = Convert.ToDecimal(reader["precio_Medicamento"]),
                                    Stock = Convert.ToInt32(reader["stock_Medicamento"]),
                                    Cantidad = 1
                                });
                            }
                        }
                    }
                }
                return Ok(receta);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al cargar los medicamentos de la receta. Detalles: " + ex.Message });
            }
        }
    }
}