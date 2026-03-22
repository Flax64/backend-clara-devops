using backend_CLARA.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Org.BouncyCastle.Asn1.Ocsp;
using System;
using System.Collections;

namespace backend_CLARA.Controllers
{
    /// <summary>
    /// Controlador que se encarga de gestionar las peticiones relacionadas con la autorizacion y registro de usuarios en el sistema. 
    /// Permite registrar nuevos usuarios y obtener información relacionada con los géneros disponibles para el registro.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class RegistroController : ControllerBase
    {
        private readonly String _connectionString = "Server=localhost; Database=farmacia; Uid=root ; Pwd=KameHameH4!";
        [HttpPost("registar")]
        public IActionResult Registrar([FromBody] RegistroRequest request)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    // 1. Primero verificamos si el correo ya existe para no duplicar
                    string checkQuery = "SELECT COUNT(*) FROM usuarios WHERE email_Usuario = @correo";
                    using (MySqlCommand checkCmd = new MySqlCommand(checkQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@correo", request.Email);
                        int existe = Convert.ToInt32(checkCmd.ExecuteScalar());

                        if (existe > 0)
                        {
                            return BadRequest(new { message = "Este correo ya está registrado en el sistema." });
                        }
                    }

                    // 2. Extraemos el rol del usuario (por defecto dejare el de paciente
                    string rolQuery = "SELECT id_Rol FROM roles WHERE nombre = 'Paciente'";
                    int idRol = 0;
                    if (request.IdRol == 0)
                    { 
                        using (MySqlCommand rolCmd = new MySqlCommand(rolQuery, conn))
                        {
                            idRol = Convert.ToInt32(rolCmd.ExecuteScalar());
                        }
                    }


                    // 3. Si no existe, procedemos a insertar el nuevo usuario
                    string insertQuery = "INSERT INTO usuarios (id_Genero, id_Estatus,id_Rol, nombre_Usuario, apellido_P, apellido_M, email_Usuario, password_Usuario, telefono, fecha_Nacimiento) " +
                        "VALUES (@idGenero, @idEstatus, @idRol, @nombre, @apellidoP, @apellidoM, @correo, @password, @telefono, @fechaNacimiento)";
                    using (MySqlCommand insertCmd = new MySqlCommand(insertQuery, conn))
                    {
                        insertCmd.Parameters.AddWithValue("@idGenero", request.IdGenero);
                        insertCmd.Parameters.AddWithValue("@idEstatus", request.IdEstatus);
                        insertCmd.Parameters.AddWithValue("@idRol", idRol);
                        insertCmd.Parameters.AddWithValue("@nombre", request.Nombre);
                        insertCmd.Parameters.AddWithValue("@apellidoP", request.ApellidoPaterno);
                        insertCmd.Parameters.AddWithValue("@apellidoM", request.ApellidoMaterno);
                        insertCmd.Parameters.AddWithValue("@correo", request.Email);
                        insertCmd.Parameters.AddWithValue("@password", request.Password);
                        insertCmd.Parameters.AddWithValue("@telefono", request.Telefono);
                        insertCmd.Parameters.AddWithValue("@fechaNacimiento", request.FechaNacimiento);

                        int filasAfectadas = insertCmd.ExecuteNonQuery();

                        if (filasAfectadas > 0)
                        {
                            return Ok(new { message = "Usuario registrado exitosamente." });
                        }
                        else
                        {
                            return BadRequest(new { message = "No se pudo completar el registro." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error en el servidor.", error = ex.Message });
            }
        }


        [HttpGet("generos")]
        public IActionResult ObtenerGeneros()
        {
            List<GeneroResponse> listaGeneros = new List<GeneroResponse>();

            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    // Asegúrate de que el nombre de la tabla y columnas coincidan con tu BD real
                    string query = "SELECT id_Genero, nombre FROM generos";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            listaGeneros.Add(new GeneroResponse
                            {
                                IdGenero = Convert.ToInt32(reader["id_Genero"]),
                                NombreGenero = reader["nombre"].ToString()
                            });
                        }
                    }
                }
                return Ok(listaGeneros);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al obtener géneros", error = ex.Message });
            }
        }
    }
}
