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
        private readonly string _connectionString = "Server=localhost; Database=farmacia; Uid=root; Pwd=KameHameH4!";

        // =======================================================
        // 1. GET: LISTADO GENERAL (Para la tabla principal)
        // =======================================================
        [HttpGet("lista")]
        public IActionResult ObtenerLista()
        {
            List<object> lista = new List<object>();
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    // ✅ Corregido: Agregado c.id_Proveedor y alias para estatus
                    string query = @"SELECT c.id_Compra, c.id_Proveedor, p.nombre_Proveedor, 
                                            c.fecha_Compra, c.total_Compra, e.nombre AS nombre_Estatus 
                                     FROM COMPRAS c 
                                     INNER JOIN PROVEEDORES p ON c.id_Proveedor = p.id_Proveedor
                                     INNER JOIN ESTATUS e ON c.id_Estatus = e.id_Estatus
                                     ORDER BY c.id_Compra DESC";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            lista.Add(new
                            {
                                idCompra = r["id_Compra"],
                                idProveedor = r["id_Proveedor"],
                                proveedor = r["nombre_Proveedor"],
                                fecha = r["fecha_Compra"],
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
                return StatusCode(500, new { error = "Error al obtener lista: " + ex.Message });
            }
        }

        // =======================================================
        // 2. POST: REGISTRAR NUEVA COMPRA (Aumenta stock)
        // =======================================================
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
                        // 1. Cabecera
                        string insC = @"INSERT INTO COMPRAS (id_Estatus, id_Proveedor, fecha_Compra, hora_Compra, total_Compra) 
                                        VALUES (5, @p, CURDATE(), CURTIME(), @t); SELECT LAST_INSERT_ID();";

                        int idC = 0;
                        using (var cmd = new MySqlCommand(insC, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@p", request.IdProveedor);
                            cmd.Parameters.AddWithValue("@t", request.TotalCompra);
                            idC = Convert.ToInt32(cmd.ExecuteScalar());
                        }

                        // 2. Detalles y Actualización de Stock
                        foreach (var det in request.Detalles)
                        {
                            using (var cmdD = new MySqlCommand("INSERT INTO DETALLE_COMPRA (id_Compra, id_Medicamento, cantidad) VALUES (@idC, @idM, @cant)", conn, trans))
                            {
                                cmdD.Parameters.AddWithValue("@idC", idC);
                                cmdD.Parameters.AddWithValue("@idM", det.IdMedicamento);
                                cmdD.Parameters.AddWithValue("@cant", det.Cantidad);
                                cmdD.ExecuteNonQuery();
                            }

                            using (var cmdS = new MySqlCommand("UPDATE MEDICAMENTOS SET stock_Medicamento = stock_Medicamento + @cant WHERE id_Medicamento = @idM", conn, trans))
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
                        return StatusCode(500, new { error = ex.Message });
                    }
                }
            }
        }

        // =======================================================
        // 3. DELETE: ELIMINAR COMPRA (Revierte stock)
        // =======================================================
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
                        // 1. Obtener detalles para restar el stock antes de borrar
                        var viejos = new List<dynamic>();
                        using (var cmd = new MySqlCommand("SELECT id_Medicamento, cantidad FROM DETALLE_COMPRA WHERE id_Compra = @id", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            using (var r = cmd.ExecuteReader())
                            {
                                while (r.Read()) viejos.Add(new { idM = r.GetInt32(0), cant = r.GetInt32(1) });
                            }
                        }

                        foreach (var v in viejos)
                        {
                            using (var cmdUp = new MySqlCommand("UPDATE MEDICAMENTOS SET stock_Medicamento = stock_Medicamento - @c WHERE id_Medicamento = @m", conn, trans))
                            {
                                cmdUp.Parameters.AddWithValue("@c", v.cant);
                                cmdUp.Parameters.AddWithValue("@m", v.idM);
                                cmdUp.ExecuteNonQuery();
                            }
                        }

                        // 2. Borrar hjos (detalles) y luego padre (compra)
                        using (var cmdDelD = new MySqlCommand("DELETE FROM DETALLE_COMPRA WHERE id_Compra = @id", conn, trans))
                        {
                            cmdDelD.Parameters.AddWithValue("@id", id);
                            cmdDelD.ExecuteNonQuery();
                        }

                        using (var cmdDelC = new MySqlCommand("DELETE FROM COMPRAS WHERE id_Compra = @id", conn, trans))
                        {
                            cmdDelC.Parameters.AddWithValue("@id", id);
                            cmdDelC.ExecuteNonQuery();
                        }

                        trans.Commit();
                        return Ok(new { message = "Compra eliminada y stock revertido" });
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        return StatusCode(500, new { error = ex.Message });
                    }
                }
            }
        }

        // =======================================================
        // 4. GET: OBTENER UNA COMPRA (Para edición)
        // =======================================================
        [HttpGet("{id}")]
        public IActionResult ObtenerPorId(int id)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    // Cabecera
                    object cabecera = null;
                    using (var cmd = new MySqlCommand("SELECT id_Proveedor, total_Compra FROM COMPRAS WHERE id_Compra = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                                cabecera = new { idProveedor = r.GetInt32(0), totalCompra = r.GetDecimal(1) };
                        }
                    }

                    if (cabecera == null) return NotFound();

                    // Detalles
                    var detalles = new List<object>();
                    string sqlD = @"SELECT d.id_Medicamento, m.nombre_Medicamento, d.cantidad, m.precio_Medicamento 
                                    FROM DETALLE_COMPRA d 
                                    INNER JOIN MEDICAMENTOS m ON d.id_Medicamento = m.id_Medicamento 
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
                                    p_Unit = rD["precio_Medicamento"]
                                });
                            }
                        }
                    }
                    return Ok(new { idProveedor = ((dynamic)cabecera).idProveedor, detalles });
                }
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        // =======================================================
        // 5. PUT: ACTUALIZAR COMPRA (El proceso completo)
        // =======================================================
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
                        // A. Revertir stock viejo
                        var viejos = new List<dynamic>();
                        using (var cmd = new MySqlCommand("SELECT id_Medicamento, cantidad FROM DETALLE_COMPRA WHERE id_Compra = @id", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            using (var r = cmd.ExecuteReader())
                            {
                                while (r.Read()) viejos.Add(new { idM = r.GetInt32(0), cant = r.GetInt32(1) });
                            }
                        }
                        foreach (var v in viejos)
                        {
                            using (var cmdUp = new MySqlCommand("UPDATE MEDICAMENTOS SET stock_Medicamento = stock_Medicamento - @c WHERE id_Medicamento = @m", conn, trans))
                            {
                                cmdUp.Parameters.AddWithValue("@c", v.cant); cmdUp.Parameters.AddWithValue("@m", v.idM);
                                cmdUp.ExecuteNonQuery();
                            }
                        }

                        // B. Limpiar detalles y actualizar cabecera
                        using (var cmdDel = new MySqlCommand("DELETE FROM DETALLE_COMPRA WHERE id_Compra = @id", conn, trans))
                        {
                            cmdDel.Parameters.AddWithValue("@id", id); cmdDel.ExecuteNonQuery();
                        }
                        using (var cmdUpC = new MySqlCommand("UPDATE COMPRAS SET id_Proveedor = @p, total_Compra = @t WHERE id_Compra = @id", conn, trans))
                        {
                            cmdUpC.Parameters.AddWithValue("@p", request.IdProveedor);
                            cmdUpC.Parameters.AddWithValue("@t", request.TotalCompra);
                            cmdUpC.Parameters.AddWithValue("@id", id);
                            cmdUpC.ExecuteNonQuery();
                        }

                        // C. Insertar nuevos y sumar nuevo stock
                        foreach (var det in request.Detalles)
                        {
                            using (var cmdIns = new MySqlCommand("INSERT INTO DETALLE_COMPRA (id_Compra, id_Medicamento, cantidad) VALUES (@id, @m, @c)", conn, trans))
                            {
                                cmdIns.Parameters.AddWithValue("@id", id); cmdIns.Parameters.AddWithValue("@m", det.IdMedicamento);
                                cmdIns.Parameters.AddWithValue("@c", det.Cantidad); cmdIns.ExecuteNonQuery();
                            }
                            using (var cmdStock = new MySqlCommand("UPDATE MEDICAMENTOS SET stock_Medicamento = stock_Medicamento + @c WHERE id_Medicamento = @m", conn, trans))
                            {
                                cmdStock.Parameters.AddWithValue("@c", det.Cantidad); cmdStock.Parameters.AddWithValue("@m", det.IdMedicamento);
                                cmdStock.ExecuteNonQuery();
                            }
                        }

                        trans.Commit();
                        return Ok(new { message = "Actualizado con éxito" });
                    }
                    catch (Exception ex) { trans.Rollback(); return BadRequest(ex.Message); }
                }
            }
        }
    }
}