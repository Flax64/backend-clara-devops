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
                    // 1. Actualiza el SELECT para pedir la hora y ordenar por ella también
                    // 2. Agregamos e.nombre AS nombre_Estatus y el INNER JOIN
                    string query = @"SELECT v.id_Venta, v.fecha_Venta, v.hora_Venta, v.nombre_Cliente, CONCAT(u.nombre_Usuario, ' ', u.apellido_P) AS nombre_Vendedor, 
                                    v.total_Venta, m.nombre AS metodo_Pago, e.nombre AS nombre_Estatus
                                    FROM VENTAS v
                                    INNER JOIN USUARIOS u ON v.id_Usuario = u.id_Usuario
                                    INNER JOIN METODOS_PAGO m ON v.id_Metodo = m.id_Metodo
                                    INNER JOIN ESTATUS e ON v.id_Estatus = e.id_Estatus
                                    ORDER BY v.fecha_Venta DESC, v.hora_Venta DESC, v.id_Venta ASC ";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                // Protegemos el código por si las ventas viejas tienen la hora vacía (NULL)
                                string horaFormateada = "--:--";
                                if (reader["hora_Venta"] != DBNull.Value)
                                {
                                    // MySQL devuelve el tipo TIME como un TimeSpan en C#
                                    TimeSpan tiempo = (TimeSpan)reader["hora_Venta"];
                                    // Lo convertimos a una fecha temporal solo para sacarle el formato de 12 horas (AM/PM)
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
                    using (MySqlTransaction transaccion = conn.BeginTransaction())
                    {
                        try
                        {
                            // ✨ VERIFICAR QUE NO ESTÉ YA CANCELADA
                            int estatusActual = 0;
                            using (var cmdCheck = new MySqlCommand("SELECT id_Estatus FROM VENTAS WHERE id_Venta = @id", conn, transaccion))
                            {
                                cmdCheck.Parameters.AddWithValue("@id", id);
                                var result = cmdCheck.ExecuteScalar();
                                if (result == null) return NotFound(new { message = "Venta no encontrada." });
                                estatusActual = Convert.ToInt32(result);
                            }

                            int idEstatusCancelada = 4; 
                            if (estatusActual == idEstatusCancelada)
                                return BadRequest(new { message = "Esta venta ya se encuentra cancelada." });

                            // PASO 0: Ver si tenía consulta ligada
                            int? idConsultaRevertir = null;
                            using (MySqlCommand cmdGet = new MySqlCommand("SELECT id_Consulta FROM VENTAS WHERE id_Venta = @id", conn, transaccion))
                            {
                                cmdGet.Parameters.AddWithValue("@id", id);
                                object res = cmdGet.ExecuteScalar();
                                if (res != null && res != DBNull.Value) idConsultaRevertir = Convert.ToInt32(res);
                            }

                            // 1. Regresamos el stock al inventario
                            string queryDevolverStock = "UPDATE MEDICAMENTOS m INNER JOIN DETALLE_VENTA dv ON m.id_Medicamento = dv.id_Medicamento SET m.stock_Medicamento = m.stock_Medicamento + dv.cantidad WHERE dv.id_Venta = @id";
                            using (MySqlCommand cmdDevolver = new MySqlCommand(queryDevolverStock, conn, transaccion))
                            {
                                cmdDevolver.Parameters.AddWithValue("@id", id);
                                cmdDevolver.ExecuteNonQuery();
                            }

                            // 2. ✨ MAGIA: EN LUGAR DE BORRAR, ACTUALIZAMOS EL ESTATUS
                            string queryCancelarVenta = "UPDATE VENTAS SET id_Estatus = @estatus WHERE id_Venta = @id";
                            using (MySqlCommand cmdVenta = new MySqlCommand(queryCancelarVenta, conn, transaccion))
                            {
                                cmdVenta.Parameters.AddWithValue("@estatus", idEstatusCancelada);
                                cmdVenta.Parameters.AddWithValue("@id", id);
                                cmdVenta.ExecuteNonQuery();
                            }

                            // 3. Si tenía consulta, la regresamos a estado 5 (Pendiente)
                            if (idConsultaRevertir.HasValue)
                            {
                                string queryRevConsulta = "UPDATE CONSULTAS SET id_Estatus = 5 WHERE id_Consulta = @idConsulta";
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
                return StatusCode(500, new { message = "Error al intentar cancelar la venta.", error = ex.Message });
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
                            // ¡Cambiamos el id_Estatus a 5 (Completada) y agregamos id_Consulta!
                            string queryVenta = @"INSERT INTO VENTAS (id_Estatus, id_Consulta, id_Metodo, id_Usuario, fecha_Venta, hora_Venta, total_Venta, nombre_Cliente) 
                            VALUES (5, @idConsulta, @metodo, @usuario, CURDATE(), CURTIME(), @total, @cliente);
                            SELECT LAST_INSERT_ID();";

                            int nuevoIdVenta = 0;
                            using (MySqlCommand cmdVenta = new MySqlCommand(queryVenta, conn, transaccion))
                            {
                                // Si viene nulo, mandamos un DBNull a MySQL
                                cmdVenta.Parameters.AddWithValue("@idConsulta", request.IdConsulta.HasValue ? request.IdConsulta.Value : (object)DBNull.Value);
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
                            if (request.IdConsulta.HasValue)
                            {
                                string queryUpdateConsulta = "UPDATE CONSULTAS SET id_Estatus = 6 WHERE id_Consulta = @idConsulta";
                                using (MySqlCommand cmdConsulta = new MySqlCommand(queryUpdateConsulta, conn, transaccion))
                                {
                                    cmdConsulta.Parameters.AddWithValue("@idConsulta", request.IdConsulta.Value);
                                    cmdConsulta.ExecuteNonQuery();
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
                    string queryDetalle = @"SELECT dv.id_Medicamento, m.nombre_Medicamento, m.concentracion_Valor, m.concentracion_Unidad, 
                    SUM(dv.cantidad) AS cantidad, m.precio_Medicamento FROM DETALLE_VENTA dv
                    INNER JOIN MEDICAMENTOS m ON dv.id_Medicamento = m.id_Medicamento
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

        // =======================================================
        // GET: OBTENER PACIENTES CON CONSULTAS (Para el buscador)
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
                    // Unimos CONSULTAS -> CITAS -> PACIENTES -> USUARIOS para sacar el nombre real
                    string query = @"
                    SELECT c.id_Consulta, CONCAT(u.nombre_Usuario, ' ', u.apellido_P) AS nombre_Paciente 
                    FROM CONSULTAS c
                    INNER JOIN CITAS ci ON c.id_Cita = ci.id_Cita
                    INNER JOIN PACIENTES p ON ci.id_Paciente = p.id_Paciente
                    INNER JOIN USUARIOS u ON p.id_Usuario = u.id_Usuario
                    WHERE c.id_Estatus = 5 
                    AND ci.fecha_Cita >= DATE_SUB(CURDATE(), INTERVAL 7 DAY)
                    ORDER BY c.id_Consulta DESC LIMIT 50";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            listaConsultas.Add(new
                            {
                                IdConsulta = Convert.ToInt32(reader["id_Consulta"]),
                                // Lo mostramos así: "Juan Perez "
                                Nombre = reader["nombre_Paciente"].ToString()
                            });
                        }
                    }
                }
                return Ok(listaConsultas);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al cargar pacientes.", error = ex.Message });
            }
        }

        // =======================================================
        // GET: OBTENER LOS MEDICAMENTOS DE ESA RECETA
        // =======================================================
        // =======================================================
        // 2. GET: OBTENER LOS MEDICAMENTOS DE ESA RECETA (CORREGIDO)
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
                    // Ya no pedimos duración ni frecuencia para evitar que el programa choque con los textos
                    string query = @"
                SELECT dr.id_Medicamento, m.nombre_Medicamento, m.concentracion_Valor, m.concentracion_Unidad, 
                       m.precio_Medicamento, m.stock_Medicamento
                FROM DETALLE_RECETA dr
                INNER JOIN MEDICAMENTOS m ON dr.id_Medicamento = m.id_Medicamento
                WHERE dr.id_Consulta = @idConsulta";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@idConsulta", idConsulta);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                // Formateamos el nombre (Ej. Paracetamol (500mg))
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
                                    Cantidad = 1 // <--- SOLUCIÓN: Siempre mandamos 1 caja por defecto
                                });
                            }
                        }
                    }
                }
                return Ok(receta);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al cargar receta.", error = ex.Message });
            }
        }
    }
}
