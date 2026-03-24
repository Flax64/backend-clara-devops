using backend_CLARA.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Org.BouncyCastle.Asn1.Ocsp;

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PerfilController : ControllerBase
    {
        private readonly String _connectionString = "Server=localhost; Database=farmacia; Uid=root ; Pwd=KameHameH4!";
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
                return StatusCode(500, new { message = "Error al obtener perfil", error = ex.Message });
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

                    // Acomodé el string con un salto de línea limpio para que no se vea todo amontonado
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
                        // Parámetro de la URL
                        cmd.Parameters.AddWithValue("@correo", correo);

                        // ¡AQUÍ ESTÁN LOS PARÁMETROS QUE FALTABAN!
                        cmd.Parameters.AddWithValue("@nombre", request.Nombre);
                        cmd.Parameters.AddWithValue("@apellidoP", request.ApellidoP);

                        // Si el apellido materno viene vacío, lo mandamos como nulo a la BD
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
                return StatusCode(500, new { message = "Error al actualizar perfil", error = ex.Message });
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

                    // PASO A: Extraer la contraseña actual de la BD usando BINARY para que respete mayúsculas
                    string checkQuery = "SELECT password_Usuario FROM USUARIOS WHERE email_Usuario = @correo";
                    string passwordBD = "";

                    using (MySqlCommand checkCmd = new MySqlCommand(checkQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@correo", correo);
                        var result = checkCmd.ExecuteScalar();

                        if (result == null)
                        {
                            // Si el correo no existe en la BD
                            return NotFound(new { message = "Usuario no encontrado." });
                        }

                        passwordBD = result.ToString();
                    }

                    // PASO B: Validar si la contraseña actual coincide
                    if (passwordBD != request.PasswordActual)
                    {
                        return BadRequest(new { message = "La contraseña actual es incorrecta. Intenta de nuevo." });
                    }

                    // PASO C: Actualizar por la nueva contraseña
                    string updateQuery = "UPDATE USUARIOS SET password_Usuario = @nuevaPassword WHERE email_Usuario = @correo";
                    using (MySqlCommand updateCmd = new MySqlCommand(updateQuery, conn))
                    {
                        updateCmd.Parameters.AddWithValue("@nuevaPassword", request.NuevaPassword);
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
                return StatusCode(500, new { message = "Error interno del servidor", error = ex.Message });
            }
        }
    }
}
