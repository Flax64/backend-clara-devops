using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using backend_CLARA.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ComprasController : ControllerBase
    {
        private readonly string _connectionString = ConexionDB.Cadena;

        [HttpGet("lista")]
        public IActionResult ObtenerLista()
        {
            List<object> lista = new List<object>();
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"SELECT c.id_Compra, c.id_Proveedor, p.nombre_Proveedor, 
                                            c.fecha_Compra, c.hora_Compra, c.total_Compra, e.nombre AS nombre_Estatus 
                                     FROM compras c 
                                     INNER JOIN proveedores p ON c.id_Proveedor = p.id_Proveedor
                                     INNER JOIN estatus e ON c.id_Estatus = e.id_Estatus
                                     ORDER BY c.id_Compra DESC";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            TimeSpan ts = (TimeSpan)r["hora_Compra"];
                            string horaFormateada = DateTime.Today.Add(ts)
                                                    .ToString("hh:mm tt", System.Globalization.CultureInfo.InvariantCulture)
                                                    .ToLower()
                                                    .Replace("am", "a. m.")
                                                    .Replace("pm", "p. m.");
                            lista.Add(new
                            {
                                idCompra = r["id_Compra"],
                                idProveedor = r["id_Proveedor"],
                                proveedor = r["nombre_Proveedor"],
                                fecha = r["fecha_Compra"],
                                hora = horaFormateada,
                                total = r["total_Compra"],
                                estatus = r["nombre_Estatus"]
                            });
                        }
                    }
                }
                return Ok(lista);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Ocurrió un problema al cargar la lista de compras. Detalles: " + ex.Message });
            }
        }

        [HttpPost("registrar")]
        public IActionResult Registrar([FromBody] NuevaCompraRequest request)
        {
            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        string insC = @"INSERT INTO compras (id_Estatus, id_Proveedor, fecha_Compra, hora_Compra, total_Compra) 
                                        VALUES (5, @p, CURDATE(), CURTIME(), @t); SELECT LAST_INSERT_ID();";

                        int idC = 0;
                        using (var cmd = new MySqlCommand(insC, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@p", request.IdProveedor);
                            cmd.Parameters.AddWithValue("@t", request.TotalCompra);
                            idC = Convert.ToInt32(cmd.ExecuteScalar());
                        }

                        foreach (var det in request.Detalles)
                        {
                            using (var cmdD = new MySqlCommand("INSERT INTO detalle_compra (id_Compra, id_Medicamento, cantidad, precio_unitario) VALUES (@idC, @idM, @cant, @precio)", conn, trans))
                            {
                                cmdD.Parameters.AddWithValue("@idC", idC);
                                cmdD.Parameters.AddWithValue("@idM", det.IdMedicamento);
                                cmdD.Parameters.AddWithValue("@cant", det.Cantidad);
                                cmdD.Parameters.AddWithValue("@precio", det.PrecioUnitario);
                                cmdD.ExecuteNonQuery();
                            }

                            using (var cmdS = new MySqlCommand("UPDATE medicamentos SET stock_Medicamento = stock_Medicamento + @cant WHERE id_Medicamento = @idM", conn, trans))
                            {
                                cmdS.Parameters.AddWithValue("@cant", det.Cantidad);
                                cmdS.Parameters.AddWithValue("@idM", det.IdMedicamento);
                                cmdS.ExecuteNonQuery();
                            }
                        }

                        trans.Commit();
                        return Ok(new { message = "Compra registrada con éxito" });
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        return StatusCode(500, new { error = "Ocurrió un problema al registrar la compra. Detalles: " + ex.Message });
                    }
                }
            }
        }

        [HttpGet("{id}")]
        public IActionResult ObtenerPorId(int id)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    object cabecera = null;
                    using (var cmd = new MySqlCommand("SELECT id_Proveedor, total_Compra FROM compras WHERE id_Compra = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                                cabecera = new { idProveedor = r.GetInt32(0), totalCompra = r.GetDecimal(1) };
                        }
                    }

                    if (cabecera == null) return NotFound();

                    var detalles = new List<object>();
                    string sqlD = @"SELECT d.id_Medicamento, m.nombre_Medicamento, d.cantidad, d.precio_unitario 
                            FROM detalle_compra d 
                            INNER JOIN medicamentos m ON d.id_Medicamento = m.id_Medicamento 
                            WHERE d.id_Compra = @id";

                    using (var cmdD = new MySqlCommand(sqlD, conn))
                    {
                        cmdD.Parameters.AddWithValue("@id", id);
                        using (var rD = cmdD.ExecuteReader())
                        {
                            while (rD.Read())
                            {
                                detalles.Add(new
                                {
                                    idProducto = rD["id_Medicamento"],
                                    producto = rD["nombre_Medicamento"],
                                    cant = rD["cantidad"],
                                    p_Unit = rD["precio_unitario"]
                                });
                            }
                        }
                    }
                    return Ok(new { idProveedor = ((dynamic)cabecera).idProveedor, detalles });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "No se pudo recuperar la información de esta compra. Detalles: " + ex.Message });
            }
        }

        [HttpPut("{id}")]
        public IActionResult Actualizar(int id, [FromBody] NuevaCompraRequest request)
        {
            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        // REGLA 1: NO EDITAR DESPUÉS DE 1 DÍA
                        using (var cmdFecha = new MySqlCommand("SELECT fecha_Compra FROM compras WHERE id_Compra = @id", conn, trans))
                        {
                            cmdFecha.Parameters.AddWithValue("@id", id);
                            var resFecha = cmdFecha.ExecuteScalar();
                            if (resFecha == null) return NotFound(new { error = "La compra no existe." });

                            DateTime fechaCompra = Convert.ToDateTime(resFecha);
                            if ((DateTime.Today - fechaCompra.Date).TotalDays > 1)
                            {
                                return BadRequest(new { error = "No se puede editar una compra después de 24 horas de haber sido registrada." });
                            }
                        }

                        // Recuperar detalles viejos
                        var viejos = new List<dynamic>();
                        using (var cmd = new MySqlCommand("SELECT id_Medicamento, cantidad FROM detalle_compra WHERE id_Compra = @id", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            using (var r = cmd.ExecuteReader())
                            {
                                while (r.Read()) viejos.Add(new { idM = r.GetInt32(0), cant = r.GetInt32(1) });
                            }
                        }

                        // REGLA 2: VALIDAR SI HAY STOCK SUFICIENTE ANTES DE REVERTIR
                        foreach (var v in viejos)
                        {
                            using (var cmdVal = new MySqlCommand("SELECT stock_Medicamento, nombre_Medicamento FROM medicamentos WHERE id_Medicamento = @m", conn, trans))
                            {
                                cmdVal.Parameters.AddWithValue("@m", v.idM);
                                using (var reader = cmdVal.ExecuteReader())
                                {
                                    if (reader.Read())
                                    {
                                        int stockActual = reader.GetInt32(0);
                                        string nombreMed = reader.GetString(1);
                                        if (stockActual < v.cant)
                                        {
                                            return BadRequest(new { error = $"No se puede editar la compra. El medicamento '{nombreMed}' ya fue vendido y no hay stock suficiente para revertir esta factura." });
                                        }
                                    }
                                }
                            }
                        }

                        // Si pasó las validaciones, procedemos a revertir el stock normalmente
                        foreach (var v in viejos)
                        {
                            using (var cmdUp = new MySqlCommand("UPDATE medicamentos SET stock_Medicamento = stock_Medicamento - @c WHERE id_Medicamento = @m", conn, trans))
                            {
                                cmdUp.Parameters.AddWithValue("@c", v.cant);
                                cmdUp.Parameters.AddWithValue("@m", v.idM);
                                cmdUp.ExecuteNonQuery();
                            }
                        }

                        using (var cmdDel = new MySqlCommand("DELETE FROM detalle_compra WHERE id_Compra = @id", conn, trans))
                        {
                            cmdDel.Parameters.AddWithValue("@id", id); cmdDel.ExecuteNonQuery();
                        }

                        using (var cmdUpC = new MySqlCommand("UPDATE compras SET id_Proveedor = @p, total_Compra = @t WHERE id_Compra = @id", conn, trans))
                        {
                            cmdUpC.Parameters.AddWithValue("@p", request.IdProveedor);
                            cmdUpC.Parameters.AddWithValue("@t", request.TotalCompra);
                            cmdUpC.Parameters.AddWithValue("@id", id);
                            cmdUpC.ExecuteNonQuery();
                        }

                        foreach (var det in request.Detalles)
                        {
                            using (var cmdIns = new MySqlCommand("INSERT INTO detalle_compra (id_Compra, id_Medicamento, cantidad, precio_unitario) VALUES (@id, @m, @c, @p)", conn, trans))
                            {
                                cmdIns.Parameters.AddWithValue("@id", id);
                                cmdIns.Parameters.AddWithValue("@m", det.IdMedicamento);
                                cmdIns.Parameters.AddWithValue("@c", det.Cantidad);
                                cmdIns.Parameters.AddWithValue("@p", det.PrecioUnitario);
                                cmdIns.ExecuteNonQuery();
                            }
                            using (var cmdStock = new MySqlCommand("UPDATE medicamentos SET stock_Medicamento = stock_Medicamento + @c WHERE id_Medicamento = @m", conn, trans))
                            {
                                cmdStock.Parameters.AddWithValue("@c", det.Cantidad);
                                cmdStock.Parameters.AddWithValue("@m", det.IdMedicamento);
                                cmdStock.ExecuteNonQuery();
                            }
                        }

                        trans.Commit();
                        return Ok(new { message = "Actualizado con éxito" });
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        return StatusCode(500, new { error = "Ocurrió un problema al actualizar la compra. Detalles: " + ex.Message });
                    }
                }
            }
        }

        [HttpDelete("{id}")]
        public IActionResult Eliminar(int id)
        {
            using (MySqlConnection conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        int estatusActual = 0;
                        using (var cmdCheck = new MySqlCommand("SELECT id_Estatus FROM compras WHERE id_Compra = @id", conn, trans))
                        {
                            cmdCheck.Parameters.AddWithValue("@id", id);
                            var result = cmdCheck.ExecuteScalar();
                            if (result == null) return NotFound(new { error = "La compra no existe." });
                            estatusActual = Convert.ToInt32(result);
                        }

                        int idEstatusCancelada = 4;
                        if (estatusActual == idEstatusCancelada)
                            return BadRequest(new { error = "Esta compra ya se encuentra cancelada." });

                        // Obtener detalles de la compra a cancelar
                        var viejos = new List<dynamic>();
                        using (var cmd = new MySqlCommand("SELECT id_Medicamento, cantidad FROM detalle_compra WHERE id_Compra = @id", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            using (var r = cmd.ExecuteReader())
                            {
                                while (r.Read()) viejos.Add(new { idM = r.GetInt32(0), cant = r.GetInt32(1) });
                            }
                        }

                        // REGLA 2: VALIDAR SI HAY STOCK SUFICIENTE ANTES DE CANCELAR LA COMPRA
                        foreach (var v in viejos)
                        {
                            using (var cmdVal = new MySqlCommand("SELECT stock_Medicamento, nombre_Medicamento FROM medicamentos WHERE id_Medicamento = @m", conn, trans))
                            {
                                cmdVal.Parameters.AddWithValue("@m", v.idM);
                                using (var reader = cmdVal.ExecuteReader())
                                {
                                    if (reader.Read())
                                    {
                                        int stockActual = reader.GetInt32(0);
                                        string nombreMed = reader.GetString(1);
                                        if (stockActual < v.cant)
                                        {
                                            return BadRequest(new { error = $"No se puede cancelar esta compra. El medicamento '{nombreMed}' ya fue vendido y su stock no puede quedar en negativo." });
                                        }
                                    }
                                }
                            }
                        }

                        // Si pasó las validaciones, procedemos a descontar el stock
                        foreach (var v in viejos)
                        {
                            using (var cmdUp = new MySqlCommand("UPDATE medicamentos SET stock_Medicamento = stock_Medicamento - @c WHERE id_Medicamento = @m", conn, trans))
                            {
                                cmdUp.Parameters.AddWithValue("@c", v.cant);
                                cmdUp.Parameters.AddWithValue("@m", v.idM);
                                cmdUp.ExecuteNonQuery();
                            }
                        }

                        // Cambiar estatus a Cancelada
                        using (var cmdCancel = new MySqlCommand("UPDATE compras SET id_Estatus = @estatus WHERE id_Compra = @id", conn, trans))
                        {
                            cmdCancel.Parameters.AddWithValue("@estatus", idEstatusCancelada);
                            cmdCancel.Parameters.AddWithValue("@id", id);
                            cmdCancel.ExecuteNonQuery();
                        }

                        trans.Commit();
                        return Ok(new { message = "Compra cancelada y stock revertido" });
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        return StatusCode(500, new { error = "Ocurrió un problema al intentar cancelar la compra. Detalles: " + ex.Message });
                    }
                }
            }
        }

        [HttpGet("medicamentos")]
        public IActionResult ObtenerTodosLosMedicamentos()
        {
            List<object> lista = new List<object>();
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    string query = @"SELECT 
                                        m.id_Medicamento, 
                                        CONCAT(m.nombre_Medicamento, ' (', TRIM(TRAILING '.' FROM TRIM(TRAILING '0' FROM m.concentracion_Valor)), m.concentracion_Unidad, ')') AS nombreCompuesto, 
                                        COALESCE(
                                            (SELECT d.precio_unitario 
                                             FROM detalle_compra d 
                                             INNER JOIN compras c ON d.id_Compra = c.id_Compra 
                                             WHERE d.id_Medicamento = m.id_Medicamento 
                                             ORDER BY c.fecha_Compra DESC, c.id_Compra DESC 
                                             LIMIT 1), 
                                        0.00) AS ultimo_costo 
                                     FROM medicamentos m";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            lista.Add(new
                            {
                                Id = r["id_Medicamento"],
                                Nombre = r["nombreCompuesto"].ToString(),
                                Precio = r["ultimo_costo"]
                            });
                        }
                    }
                }
                return Ok(lista);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al cargar el catálogo de medicamentos. Detalles: " + ex.Message });
            }
        }
    }
}