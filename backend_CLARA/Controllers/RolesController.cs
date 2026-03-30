using backend_CLARA.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RolesController : ControllerBase
    {
        private readonly String _connectionString = ConexionDB.Cadena;

        // 1. Obtener todos los roles
        [HttpGet]
        public IActionResult ObtenerRoles()
        {
            try
            {
                List<Rol> roles = new List<Rol>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string getQuery = @"
                        SELECT r.id_Rol, r.nombre, GROUP_CONCAT(p.nombre SEPARATOR ', ') as permisosAsignados
                        FROM ROLES r
                        LEFT JOIN permisos_has_roles phr ON r.id_Rol = phr.id_Rol
                        LEFT JOIN PERMISOS p ON phr.id_Permiso = p.id_Permiso
                        GROUP BY r.id_Rol, r.nombre";

                    using (MySqlCommand cmd = new MySqlCommand(getQuery, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            roles.Add(new Rol
                            {
                                IdRol = reader.GetInt32(0),
                                Nombre = reader.GetString(1),
                                Permisos = reader.IsDBNull(2) ? "Sin permisos" : reader.GetString(2)
                            });
                        }
                    }
                }
                return Ok(roles);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al obtener la lista de roles. Detalles: " + ex.Message });
            }
        }

        // 2. Obtener todos los permisos que existen en el sistema
        [HttpGet("permisos")]
        public IActionResult ObtenerTodosLosPermisos()
        {
            try
            {
                List<Permiso> permisos = new List<Permiso>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string getQuery = "SELECT id_Permiso, nombre FROM PERMISOS";
                    using (MySqlCommand cmd = new MySqlCommand(getQuery, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            permisos.Add(new Permiso
                            {
                                IdPermiso = reader.GetInt32(0),
                                Nombre = reader.GetString(1)
                            });
                        }
                    }
                }
                return Ok(permisos);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al obtener el catálogo de permisos. Detalles: " + ex.Message });
            }
        }

        // 3. Obtener solo los permisos asignados a un rol específico
        [HttpGet("{idRol}/permisos")]
        public IActionResult ObtenerPermisosPorRol(int idRol)
        {
            try
            {
                List<int> permisosIds = new List<int>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = "SELECT id_Permiso FROM permisos_has_roles WHERE id_Rol = @idRol";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@idRol", idRol);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                permisosIds.Add(reader.GetInt32(0));
                            }
                        }
                    }
                }
                return Ok(permisosIds);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al obtener los permisos asignados a este rol. Detalles: " + ex.Message });
            }
        }

        // 4. Actualizar los permisos de un rol
        [HttpPost("{idRol}/permisos")]
        public IActionResult ActualizarPermisos(int idRol, [FromBody] UpdatePermisosRequest request)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    using (MySqlTransaction transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            string deleteQuery = "DELETE FROM permisos_has_roles WHERE id_Rol = @idRol";
                            using (var cmdDelete = new MySqlCommand(deleteQuery, conn, transaction))
                            {
                                cmdDelete.Parameters.AddWithValue("@idRol", idRol);
                                cmdDelete.ExecuteNonQuery();
                            }

                            if (request.PermisosIds != null && request.PermisosIds.Count > 0)
                            {
                                string insertQuery = "INSERT INTO permisos_has_roles (id_Rol, id_Permiso) VALUES (@idRol, @idPermiso)";
                                using (var cmdInsert = new MySqlCommand(insertQuery, conn, transaction))
                                {
                                    foreach (int idPermiso in request.PermisosIds)
                                    {
                                        cmdInsert.Parameters.Clear();
                                        cmdInsert.Parameters.AddWithValue("@idRol", idRol);
                                        cmdInsert.Parameters.AddWithValue("@idPermiso", idPermiso);
                                        cmdInsert.ExecuteNonQuery();
                                    }
                                }
                            }

                            transaction.Commit();
                            return Ok(new { message = "Permisos actualizados correctamente." });
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            return StatusCode(500, new { error = "Error al guardar los nuevos permisos. Detalles: " + ex.Message });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error interno del servidor. Detalles: " + ex.Message });
            }
        }

        // 5. Crear rol
        [HttpPost]
        public IActionResult CrearRol([FromBody] RolNuevoRequest request)
        {
            try
            {
                long nuevoId = 0;
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string checkQuery = "SELECT COUNT(*) FROM ROLES WHERE nombre = @nombre";
                    using (MySqlCommand checkCmd = new MySqlCommand(checkQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@nombre", request.Nombre);
                        int count = Convert.ToInt32(checkCmd.ExecuteScalar());

                        if (count > 0)
                        {
                            return BadRequest(new { error = "Ya existe un rol con este nombre." });
                        }
                    }

                    string query = "INSERT INTO ROLES (nombre) VALUES (@nombre)";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@nombre", request.Nombre);
                        cmd.ExecuteNonQuery();
                        nuevoId = cmd.LastInsertedId;
                    }
                }
                return Ok(new { message = "Rol creado exitosamente.", idRol = nuevoId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al crear el rol. Detalles: " + ex.Message });
            }
        }

        // 6. Actualizar rol
        [HttpPut("{idRol}")]
        public IActionResult ActualizarRol(int idRol, [FromBody] RolNuevoRequest request)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string checkQuery = "SELECT COUNT(*) FROM ROLES WHERE nombre = @nombre AND id_Rol != @idRol";
                    using (MySqlCommand checkCmd = new MySqlCommand(checkQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@nombre", request.Nombre);
                        checkCmd.Parameters.AddWithValue("@idRol", idRol);
                        int count = Convert.ToInt32(checkCmd.ExecuteScalar());

                        if (count > 0)
                        {
                            return BadRequest(new { error = "Ya existe otro rol con este nombre." });
                        }
                    }

                    string query = "UPDATE ROLES SET nombre = @nombre WHERE id_Rol = @idRol";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@nombre", request.Nombre);
                        cmd.Parameters.AddWithValue("@idRol", idRol);
                        cmd.ExecuteNonQuery();
                    }
                }
                return Ok(new { message = "Rol actualizado correctamente." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al actualizar el rol. Detalles: " + ex.Message });
            }
        }

        // 7. Eliminar rol
        [HttpDelete("{idRol}")]
        public IActionResult EliminarRol(int idRol)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    using (MySqlTransaction transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            string checkQuery = "SELECT COUNT(*) FROM USUARIOS WHERE id_Rol = @idRol";
                            using (MySqlCommand checkCmd = new MySqlCommand(checkQuery, conn, transaction))
                            {
                                checkCmd.Parameters.AddWithValue("@idRol", idRol);
                                int countUsuarios = Convert.ToInt32(checkCmd.ExecuteScalar());

                                if (countUsuarios > 0)
                                {
                                    transaction.Rollback();
                                    return BadRequest(new { error = "No se puede eliminar el rol porque actualmente está asignado a uno o más usuarios." });
                                }
                            }

                            string deletePermisosQuery = "DELETE FROM permisos_has_roles WHERE id_Rol = @idRol";
                            using (MySqlCommand cmdPermisos = new MySqlCommand(deletePermisosQuery, conn, transaction))
                            {
                                cmdPermisos.Parameters.AddWithValue("@idRol", idRol);
                                cmdPermisos.ExecuteNonQuery();
                            }

                            string deleteRolQuery = "DELETE FROM ROLES WHERE id_Rol = @idRol";
                            using (MySqlCommand cmdRol = new MySqlCommand(deleteRolQuery, conn, transaction))
                            {
                                cmdRol.Parameters.AddWithValue("@idRol", idRol);
                                cmdRol.ExecuteNonQuery();
                            }

                            transaction.Commit();
                            return Ok(new { message = "Rol eliminado correctamente." });
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            return StatusCode(500, new { error = "Error interno al intentar eliminar el rol. Detalles: " + ex.Message });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error de conexión con la base de datos. Detalles: " + ex.Message });
            }
        }
    }
}