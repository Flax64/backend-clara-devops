using backend_CLARA.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MedicamentosController : ControllerBase
    {
        private readonly string _connectionString = "Server=localhost; Database=farmacia; Uid=root ; Pwd=KameHameH4!";

        // --- 1. LEER TODOS LOS MEDICAMENTOS ---
        [HttpGet]
        public IActionResult GetMedicamentos()
        {
            try
            {
                List<MedicamentoRead> medicamentos = new List<MedicamentoRead>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"
                        SELECT 
                            m.id_Medicamento, m.id_Estatus, e.nombre AS Estatus,
                            m.nombre_Medicamento, m.descripcion_Medicamento, 
                            m.precio_Medicamento, m.stock_Medicamento, 
                            m.concentracion_Valor, m.concentracion_Unidad
                        FROM MEDICAMENTOS m
                        INNER JOIN ESTATUS e ON m.id_Estatus = e.id_Estatus";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string nombrePuro = reader.GetString(3);
                            decimal valorConc = reader.GetDecimal(7);
                            string unidadConc = reader.GetString(8);

                            // Removemos decimales inútiles (ej. 500.00mg -> 500mg)
                            string valorBonito = (valorConc % 1 == 0) ? ((int)valorConc).ToString() : valorConc.ToString();

                            medicamentos.Add(new MedicamentoRead
                            {
                                IdMedicamento = reader.GetInt32(0),
                                IdEstatus = reader.GetInt32(1),
                                Estatus = reader.GetString(2),
                                Nombre = nombrePuro,
                                NombreCompleto = $"{nombrePuro} {valorBonito}{unidadConc}", // "Paracetamol 500mg"
                                Descripcion = reader.GetString(4),
                                Precio = reader.GetDecimal(5),
                                Stock = reader.GetInt32(6),
                                ConcentracionValor = valorConc,
                                ConcentracionUnidad = unidadConc
                            });
                        }
                    }
                }
                return Ok(medicamentos);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al obtener los medicamentos. Detalles: " + ex.Message });
            }
        }

        // --- 2. CREAR MEDICAMENTO (CON REACTIVACIÓN INTELIGENTE Y VALIDACIÓN ESTRICTA) ---
        [HttpPost]
        public IActionResult CrearMedicamento([FromBody] MedicamentoRequest request)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    // ✨ VALIDACIÓN BLINDADA: Ignora espacios extras y diferencias entre mayúsculas/minúsculas
                    string checkQuery = @"
                        SELECT m.id_Medicamento, e.nombre AS estatusNombre 
                        FROM MEDICAMENTOS m
                        INNER JOIN ESTATUS e ON m.id_Estatus = e.id_Estatus
                        WHERE TRIM(LOWER(m.nombre_Medicamento)) = TRIM(LOWER(@nombre)) 
                        AND m.concentracion_Valor = @valor 
                        AND TRIM(LOWER(m.concentracion_Unidad)) = TRIM(LOWER(@unidad))";

                    using (MySqlCommand checkCmd = new MySqlCommand(checkQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@nombre", request.Nombre);
                        checkCmd.Parameters.AddWithValue("@valor", request.ConcentracionValor);
                        checkCmd.Parameters.AddWithValue("@unidad", request.ConcentracionUnidad);

                        using (MySqlDataReader reader = checkCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                int idExistente = reader.GetInt32(0);
                                string estatusExistente = reader.GetString(1);

                                // Si existe y está ACTIVO, rechazamos.
                                if (estatusExistente != "Inactivo")
                                {
                                    return BadRequest(new { error = $"Ya existe un medicamento activo registrado como '{request.Nombre.Trim()}' de {request.ConcentracionValor}{request.ConcentracionUnidad.Trim()}." });
                                }

                                // Si existe pero está INACTIVO, cerramos el lector y lo REACTIVAMOS abajo.
                                reader.Close();

                                string reactivarQuery = @"
                                    UPDATE MEDICAMENTOS SET 
                                    id_Estatus = (SELECT id_Estatus FROM ESTATUS WHERE nombre = 'Activo' LIMIT 1),
                                    descripcion_Medicamento = @desc,
                                    precio_Medicamento = @precio
                                    WHERE id_Medicamento = @id";

                                using (MySqlCommand reactivarCmd = new MySqlCommand(reactivarQuery, conn))
                                {
                                    reactivarCmd.Parameters.AddWithValue("@id", idExistente);
                                    reactivarCmd.Parameters.AddWithValue("@desc", request.Descripcion.Trim());
                                    reactivarCmd.Parameters.AddWithValue("@precio", request.Precio);
                                    reactivarCmd.ExecuteNonQuery();
                                }

                                return Ok(new { message = "El medicamento existía en el historial como inactivo. Ha sido reactivado y sus datos fueron actualizados." });
                            }
                        }
                    }

                    // 2. SI NO EXISTE EN ABSOLUTO, LO CREAMOS NUEVO (Forzando stock a 0 y limpiando textos)
                    string insertQuery = @"
                        INSERT INTO MEDICAMENTOS 
                        (id_Estatus, nombre_Medicamento, descripcion_Medicamento, precio_Medicamento, stock_Medicamento, concentracion_Valor, concentracion_Unidad) 
                        VALUES ((SELECT id_Estatus FROM ESTATUS WHERE nombre = 'Activo' LIMIT 1), @nombre, @desc, @precio, 0, @valor, @unidad)";

                    using (MySqlCommand insertCmd = new MySqlCommand(insertQuery, conn))
                    {
                        // Usamos .Trim() al insertar para que la base de datos se mantenga limpia desde el origen
                        insertCmd.Parameters.AddWithValue("@nombre", request.Nombre.Trim());
                        insertCmd.Parameters.AddWithValue("@desc", request.Descripcion.Trim());
                        insertCmd.Parameters.AddWithValue("@precio", request.Precio);
                        insertCmd.Parameters.AddWithValue("@valor", request.ConcentracionValor);
                        insertCmd.Parameters.AddWithValue("@unidad", request.ConcentracionUnidad.Trim());

                        insertCmd.ExecuteNonQuery();
                    }
                }
                return Ok(new { message = "Medicamento registrado exitosamente en el catálogo." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al registrar el medicamento. Detalles: " + ex.Message });
            }
        }

        // --- 3. ACTUALIZAR MEDICAMENTO ---
        [HttpPut("{id}")]
        public IActionResult ActualizarMedicamento(int id, [FromBody] MedicamentoRequest request)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    // ✨ VALIDACIÓN BLINDADA: Ignora espacios en blanco extras y mayúsculas/minúsculas
                    string checkQuery = @"
                        SELECT COUNT(*) FROM MEDICAMENTOS 
                        WHERE TRIM(LOWER(nombre_Medicamento)) = TRIM(LOWER(@nombre)) 
                        AND concentracion_Valor = @valor 
                        AND TRIM(LOWER(concentracion_Unidad)) = TRIM(LOWER(@unidad)) 
                        AND id_Medicamento != @id";

                    using (MySqlCommand checkCmd = new MySqlCommand(checkQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@nombre", request.Nombre);
                        checkCmd.Parameters.AddWithValue("@valor", request.ConcentracionValor);
                        checkCmd.Parameters.AddWithValue("@unidad", request.ConcentracionUnidad);
                        checkCmd.Parameters.AddWithValue("@id", id);

                        if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                        {
                            return BadRequest(new { error = $"Ya existe otro medicamento registrado como '{request.Nombre}' de {request.ConcentracionValor}{request.ConcentracionUnidad}." });
                        }
                    }

                    // 2. ACTUALIZAR (Ignoramos el stock)
                    string query = @"
                        UPDATE MEDICAMENTOS SET 
                        id_Estatus = @idEstatus, 
                        nombre_Medicamento = @nombre, 
                        descripcion_Medicamento = @desc, 
                        precio_Medicamento = @precio, 
                        concentracion_Valor = @valor, 
                        concentracion_Unidad = @unidad 
                        WHERE id_Medicamento = @id";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@idEstatus", request.IdEstatus);
                        cmd.Parameters.AddWithValue("@nombre", request.Nombre.Trim());
                        cmd.Parameters.AddWithValue("@desc", request.Descripcion.Trim());
                        cmd.Parameters.AddWithValue("@precio", request.Precio);
                        cmd.Parameters.AddWithValue("@valor", request.ConcentracionValor);
                        cmd.Parameters.AddWithValue("@unidad", request.ConcentracionUnidad.Trim());

                        cmd.ExecuteNonQuery();
                    }
                }
                return Ok(new { message = "Medicamento actualizado exitosamente." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al actualizar el medicamento. Detalles: " + ex.Message });
            }
        }

        // --- 4. DAR DE BAJA (BORRADO LÓGICO) ---
        [HttpDelete("{id}")]
        public IActionResult EliminarMedicamento(int id)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    string queryBaja = @"
                        UPDATE MEDICAMENTOS 
                        SET id_Estatus = (SELECT id_Estatus FROM ESTATUS WHERE nombre = 'Inactivo' LIMIT 1) 
                        WHERE id_Medicamento = @id";

                    using (MySqlCommand cmd = new MySqlCommand(queryBaja, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        int filasAfectadas = cmd.ExecuteNonQuery();

                        if (filasAfectadas > 0)
                        {
                            return Ok(new { message = "Medicamento dado de baja exitosamente (Estatus: Inactivo)." });
                        }
                        else
                        {
                            return NotFound(new { error = "No se encontró el medicamento especificado." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al intentar dar de baja el medicamento. Detalles: " + ex.Message });
            }
        }

        // --- MÉTODO AUXILIAR ---
        private int ObtenerIdEstatusActivo(MySqlConnection conn)
        {
            string query = "SELECT id_Estatus FROM ESTATUS WHERE nombre = 'Activo' LIMIT 1";
            using (MySqlCommand cmd = new MySqlCommand(query, conn))
            {
                var result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : 1; // Asume 1 si por algo falla
            }
        }
    }
}