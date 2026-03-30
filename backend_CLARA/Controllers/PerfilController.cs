using backend_CLARA.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Org.BouncyCastle.Asn1.Ocsp;
using System;

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PerfilController : ControllerBase
    {
        private readonly String _connectionString = ConexionDB.Cadena;

        [HttpGet("{correo}")]
        public IActionResult ObtenerPerfil(string correo)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = "SELECT u.nombre_Usuario, u.apellido_P, u.apellido_M, " +
                        "u.telefono, u.fecha_Nacimiento, u.email_Usuario, g.nombre AS nombre_Genero " +
                        "FROM usuarios AS u " +
                        "INNER JOIN generos AS g ON g.id_Genero = u.id_Genero " +
                        "WHERE u.email_Usuario = @correo";
                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@correo", correo);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                PerfilResponse perfil = new PerfilResponse
                                {
                                    Nombre = reader["nombre_Usuario"].ToString(),
                                    ApellidoP = reader["apellido_P"].ToString(),
                                    ApellidoM = reader["apellido_M"].ToString(),
                                    Telefono = reader["telefono"].ToString(),
                                    FechaNacimiento = Convert.ToDateTime(reader["fecha_Nacimiento"]),
                                    Correo = reader["email_Usuario"].ToString(),
                                    Genero = reader["nombre_Genero"].ToString()
                                };
                                return Ok(perfil);
                            }
                            else
                            {
                                return NotFound(new { message = "Usuario no encontrado." });
                            }
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Ocurrió un problema al obtener el perfil. Detalles: " + ex.Message });
            }
        }

        [HttpPut("actualizar/{correo}")]
        public IActionResult ActualizarPerfil(string correo, [FromBody] PerfilUpdateRequest request)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    string query = @"
                UPDATE USUARIOS 
                SET nombre_Usuario = @nombre, 
                    apellido_P = @apellidoP, 
                    apellido_M = @apellidoM, 
                    telefono = @telefono, 
                    fecha_Nacimiento = @fechaNacimiento,
                    id_Genero = @idGenero
                WHERE email_Usuario = @correo";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@correo", correo);
                        cmd.Parameters.AddWithValue("@nombre", request.Nombre);
                        cmd.Parameters.AddWithValue("@apellidoP", request.ApellidoP);
                        cmd.Parameters.AddWithValue("@apellidoM", string.IsNullOrEmpty(request.ApellidoM) ? (object)DBNull.Value : request.ApellidoM);
                        cmd.Parameters.AddWithValue("@telefono", request.Telefono);
                        cmd.Parameters.AddWithValue("@fechaNacimiento", request.FechaNacimiento);
                        cmd.Parameters.AddWithValue("@idGenero", request.IdGenero);

                        int filasAfectadas = cmd.ExecuteNonQuery();

                        if (filasAfectadas > 0)
                        {
                            return Ok(new { message = "Perfil actualizado exitosamente." });
                        }
                        else
                        {
                            return NotFound(new { message = "Usuario no encontrado." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Ocurrió un problema al actualizar el perfil. Detalles: " + ex.Message });
            }
        }

        [HttpPut("cambiar-password/{correo}")]
        public IActionResult CambiarPassword(string correo, [FromBody] CambiarPasswordRequest request)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    // PASO A: Extraer la contraseña actual de la BD
                    string checkQuery = "SELECT password_Usuario FROM USUARIOS WHERE email_Usuario = @correo";
                    string passwordBD = "";

                    using (MySqlCommand checkCmd = new MySqlCommand(checkQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@correo", correo);
                        var result = checkCmd.ExecuteScalar();

                        if (result == null)
                        {
                            return NotFound(new { message = "Usuario no encontrado." });
                        }
                        passwordBD = result.ToString();
                    }

                    // PASO B: Validar si la contraseña actual coincide (HÍBRIDO)
                    bool isValid = false;
                    if (passwordBD.StartsWith("$2") && passwordBD.Length >= 59)
                    {
                        isValid = BCrypt.Net.BCrypt.Verify(request.PasswordActual, passwordBD);
                    }
                    else
                    {
                        isValid = (passwordBD == request.PasswordActual);
                    }

                    if (!isValid)
                    {
                        return BadRequest(new { message = "La contraseña actual es incorrecta. Intenta de nuevo." });
                    }

                    // PASO C: Encriptar y guardar
                    string newHash = BCrypt.Net.BCrypt.HashPassword(request.NuevaPassword);

                    string updateQuery = "UPDATE USUARIOS SET password_Usuario = @nuevaPassword WHERE email_Usuario = @correo";
                    using (MySqlCommand updateCmd = new MySqlCommand(updateQuery, conn))
                    {
                        updateCmd.Parameters.AddWithValue("@nuevaPassword", newHash);
                        updateCmd.Parameters.AddWithValue("@correo", correo);

                        int filas = updateCmd.ExecuteNonQuery();
                        if (filas > 0)
                        {
                            return Ok(new { message = "Contraseña actualizada exitosamente." });
                        }
                        else
                        {
                            return BadRequest(new { message = "No se pudo guardar la contraseña." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error interno del servidor. Detalles: " + ex.Message });
            }
        }

        // =======================================================
        // GET: SABER EL ROL, PERMISOS Y NOMBRE DEL USUARIO
        // =======================================================
        [HttpGet("rol/{correo}")]
        public IActionResult ObtenerRolUsuario(string correo)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string rolName = "";
                    string nombreCompleto = "";
                    List<string> permisosList = new List<string>();

                    // ✨ 1. Obtenemos el Rol y el Nombre del usuario al mismo tiempo
                    string queryRol = @"SELECT r.nombre, u.nombre_Usuario, u.apellido_P 
                                        FROM USUARIOS u 
                                        INNER JOIN ROLES r ON u.id_Rol = r.id_Rol 
                                        WHERE u.email_Usuario = @correo LIMIT 1";

                    using (MySqlCommand cmd = new MySqlCommand(queryRol, conn))
                    {
                        cmd.Parameters.AddWithValue("@correo", correo);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                rolName = reader.GetString(0);
                                nombreCompleto = $"{reader.GetString(1)} {reader.GetString(2)}";
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(rolName)) return NotFound(new { error = "Usuario no encontrado" });

                    // 2. Obtener la lista de Permisos dinámicos
                    string queryPermisos = @"SELECT p.nombre FROM USUARIOS u 
                                             INNER JOIN PERMISOS_has_ROLES phr ON u.id_Rol = phr.id_Rol 
                                             INNER JOIN PERMISOS p ON phr.id_Permiso = p.id_Permiso 
                                             WHERE u.email_Usuario = @correo";

                    using (MySqlCommand cmdP = new MySqlCommand(queryPermisos, conn))
                    {
                        cmdP.Parameters.AddWithValue("@correo", correo);
                        using (MySqlDataReader reader = cmdP.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                permisosList.Add(reader.GetString(0));
                            }
                        }
                    }

                    // ✨ 3. Devolvemos todo empaquetado
                    return Ok(new { rol = rolName, permisos = permisosList, nombre = nombreCompleto });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al obtener permisos. Detalles: " + ex.Message });
            }
        }
    }
}