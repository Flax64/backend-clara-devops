using backend_CLARA.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RegistroController : ControllerBase
    {
        private readonly String _connectionString = ConexionDB.Cadena;

        [HttpPost("registar")]
        public IActionResult Registrar([FromBody] RegistroRequest request)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    // 1. Verificamos duplicidad
                    string checkQuery = "SELECT COUNT(*) FROM usuarios WHERE email_Usuario = @correo";
                    using (MySqlCommand checkCmd = new MySqlCommand(checkQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@correo", request.Email);
                        int existe = Convert.ToInt32(checkCmd.ExecuteScalar());

                        if (existe > 0)
                        {
                            return BadRequest(new { error = "Este correo ya está registrado en el sistema." });
                        }
                    }

                    // 2. Extraemos el rol de Paciente
                    string rolQuery = "SELECT id_Rol FROM roles WHERE nombre = 'Paciente'";
                    int idRol = 0;
                    if (request.IdRol == 0)
                    {
                        using (MySqlCommand rolCmd = new MySqlCommand(rolQuery, conn))
                        {
                            idRol = Convert.ToInt32(rolCmd.ExecuteScalar());
                        }
                    }

                    // ✨ 3. GENERAMOS EL HASH ANTES DE INSERTAR
                    string hashPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);

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
                        insertCmd.Parameters.AddWithValue("@password", hashPassword); // Insertamos el Hash
                        insertCmd.Parameters.AddWithValue("@telefono", request.Telefono);
                        insertCmd.Parameters.AddWithValue("@fechaNacimiento", request.FechaNacimiento);

                        int filasAfectadas = insertCmd.ExecuteNonQuery();

                        if (filasAfectadas > 0)
                        {
                            long idNuevoUsuario = insertCmd.LastInsertedId;
                            string insertPacienteQuery = "INSERT INTO pacientes (id_Usuario) VALUES (@idUsuario)";
                            using (MySqlCommand pacienteCmd = new MySqlCommand(insertPacienteQuery, conn))
                            {
                                pacienteCmd.Parameters.AddWithValue("@idUsuario", idNuevoUsuario);
                                pacienteCmd.ExecuteNonQuery();
                            }

                            return Ok(new { message = "Usuario registrado exitosamente." });
                        }
                        else
                        {
                            return BadRequest(new { error = "No se pudo completar el registro." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error en el servidor al intentar registrar. Detalles: " + ex.Message });
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
                return StatusCode(500, new { error = "Error al obtener la lista de géneros. Detalles: " + ex.Message });
            }
        }
    }
}