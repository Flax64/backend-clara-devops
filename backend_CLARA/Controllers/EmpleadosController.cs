using backend_CLARA.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EmpleadosController : ControllerBase
    {
        private readonly string _connectionString = ConexionDB.Cadena;

        // --- 1. LEER TODOS ---
        [HttpGet]
        public IActionResult GetEmpleados()
        {
            try
            {
                List<UsuarioRead> empleados = new List<UsuarioRead>();
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"
                        SELECT 
                            u.id_Usuario, u.nombre_Usuario, u.apellido_P, u.apellido_M, 
                            u.email_Usuario, r.nombre AS Rol, u.telefono, 
                            DATE_FORMAT(u.fecha_Nacimiento, '%Y-%m-%d') AS FechaNacimiento, 
                            g.nombre AS Genero,
                            m.cedula_Profesional, m.especialidad,
                            e.nombre AS Estatus 
                        FROM usuarios u
                        INNER JOIN roles r ON u.id_Rol = r.id_Rol
                        INNER JOIN generos g ON u.id_Genero = g.id_Genero
                        INNER JOIN estatus e ON u.id_Estatus = e.id_Estatus
                        LEFT JOIN medicos m ON u.id_Usuario = m.id_Usuario
                        WHERE r.nombre != 'Paciente'";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            empleados.Add(new UsuarioRead
                            {
                                IdUsuario = reader.GetInt32(0),
                                Nombre = reader.GetString(1),
                                ApellidoPaterno = reader.GetString(2),
                                ApellidoMaterno = reader.IsDBNull(3) ? "" : reader.GetString(3),
                                Email = reader.GetString(4),
                                Rol = reader.GetString(5),
                                Telefono = reader.GetString(6),
                                FechaNacimiento = reader.GetString(7),
                                Genero = reader.GetString(8),
                                CedulaProfesional = reader.IsDBNull(9) ? "" : reader.GetString(9),
                                Especialidad = reader.IsDBNull(10) ? "" : reader.GetString(10),
                                Estatus = reader.GetString(11)
                            });
                        }
                    }
                }
                return Ok(empleados);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al obtener la lista de empleados. Detalles: " + ex.Message });
            }
        }

        // --- 2. CREAR EMPLEADO ---
        [HttpPost]
        public IActionResult CrearEmpleado([FromBody] UsuarioRequest request)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    string checkQuery = @"
                        SELECT u.id_Usuario, r.nombre 
                        FROM usuarios u 
                        INNER JOIN roles r ON u.id_Rol = r.id_Rol 
                        WHERE u.email_Usuario = @email";

                    using (MySqlCommand checkCmd = new MySqlCommand(checkQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@email", request.Email);
                        using (MySqlDataReader reader = checkCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                int idExistente = reader.GetInt32(0);
                                string rolExistente = reader.GetString(1);

                                if (rolExistente == "Paciente")
                                {
                                    return StatusCode(409, new
                                    {
                                        idUsuario = idExistente,
                                        error = "Este correo ya pertenece a un paciente registrado en el sistema.\n\n¿Deseas actualizar sus datos y convertirlo en empleado?"
                                    });
                                }
                                else
                                {
                                    return BadRequest(new { error = "Este correo ya está en uso por otro empleado y no está disponible." });
                                }
                            }
                        }
                    }

                    // GENERAMOS EL HASH ANTES DE INSERTAR
                    string hashPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);

                    string query = @"INSERT INTO usuarios 
                        (id_Estatus, id_Genero, id_Rol, nombre_Usuario, apellido_P, apellido_M, email_Usuario, password_Usuario, telefono, fecha_Nacimiento) 
                        VALUES (@idEstatus, @idGenero, @idRol, @nombre, @apPaterno, @apMaterno, @email, @password, @telefono, @fechaNac)";

                    using (MySqlCommand cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@idEstatus", request.IdEstatus);
                        cmd.Parameters.AddWithValue("@idGenero", request.IdGenero);
                        cmd.Parameters.AddWithValue("@idRol", request.IdRol);
                        cmd.Parameters.AddWithValue("@nombre", request.Nombre);
                        cmd.Parameters.AddWithValue("@apPaterno", request.ApellidoPaterno);
                        cmd.Parameters.AddWithValue("@apMaterno", string.IsNullOrEmpty(request.ApellidoMaterno) ? (object)DBNull.Value : request.ApellidoMaterno);
                        cmd.Parameters.AddWithValue("@email", request.Email);
                        cmd.Parameters.AddWithValue("@password", hashPassword); // Se inserta el Hash
                        cmd.Parameters.AddWithValue("@telefono", request.Telefono);
                        cmd.Parameters.AddWithValue("@fechaNac", request.FechaNacimiento);
                        cmd.ExecuteNonQuery();

                        long idNuevoUsuario = cmd.LastInsertedId;

                        if (!string.IsNullOrEmpty(request.CedulaProfesional) && !string.IsNullOrEmpty(request.Especialidad))
                        {
                            string queryMedico = "INSERT INTO medicos (id_Usuario, cedula_Profesional, especialidad) VALUES (@idUsu, @cedula, @especialidad)";
                            using (MySqlCommand cmdMed = new MySqlCommand(queryMedico, conn))
                            {
                                cmdMed.Parameters.AddWithValue("@idUsu", idNuevoUsuario);
                                cmdMed.Parameters.AddWithValue("@cedula", request.CedulaProfesional);
                                cmdMed.Parameters.AddWithValue("@especialidad", request.Especialidad);
                                cmdMed.ExecuteNonQuery();
                            }
                        }
                    }
                }
                return Ok(new { message = "Empleado creado exitosamente." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al intentar crear al empleado. Detalles: " + ex.Message });
            }
        }

        // --- 3. ACTUALIZAR EMPLEADO ---
        [HttpPut("{id}")]
        public IActionResult ActualizarEmpleado(int id, [FromBody] UsuarioRequest request)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    string checkEmailQuery = "SELECT COUNT(*) FROM usuarios WHERE email_Usuario = @email AND id_Usuario != @id";
                    using (MySqlCommand checkCmd = new MySqlCommand(checkEmailQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@email", request.Email);
                        checkCmd.Parameters.AddWithValue("@id", id);
                        if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                        {
                            return BadRequest(new { error = "El correo ingresado ya pertenece a otro usuario en el sistema." });
                        }
                    }

                    string queryUpdate = @"UPDATE usuarios SET 
                        id_Estatus = @idEstatus, id_Genero = @idGenero, id_Rol = @idRol, 
                        nombre_Usuario = @nombre, apellido_P = @apPaterno, apellido_M = @apMaterno, 
                        email_Usuario = @email, telefono = @telefono, fecha_Nacimiento = @fechaNac ";

                    // Si escribieron nueva contraseña, la incluimos
                    if (!string.IsNullOrWhiteSpace(request.Password))
                    {
                        queryUpdate += ", password_Usuario = @password ";
                    }

                    queryUpdate += " WHERE id_Usuario = @id";

                    using (MySqlCommand cmd = new MySqlCommand(queryUpdate, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@idEstatus", request.IdEstatus);
                        cmd.Parameters.AddWithValue("@idGenero", request.IdGenero);
                        cmd.Parameters.AddWithValue("@idRol", request.IdRol);
                        cmd.Parameters.AddWithValue("@nombre", request.Nombre);
                        cmd.Parameters.AddWithValue("@apPaterno", request.ApellidoPaterno);
                        cmd.Parameters.AddWithValue("@apMaterno", string.IsNullOrEmpty(request.ApellidoMaterno) ? (object)DBNull.Value : request.ApellidoMaterno);
                        cmd.Parameters.AddWithValue("@email", request.Email);
                        cmd.Parameters.AddWithValue("@telefono", request.Telefono);
                        cmd.Parameters.AddWithValue("@fechaNac", request.FechaNacimiento);

                        // GENERAMOS EL HASH DE LA NUEVA CONTRASEÑA
                        if (!string.IsNullOrWhiteSpace(request.Password))
                        {
                            string hashPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);
                            cmd.Parameters.AddWithValue("@password", hashPassword);
                        }

                        cmd.ExecuteNonQuery();
                    }

                    string queryBorrarPaciente = "DELETE FROM pacientes WHERE id_Usuario = @id";
                    using (MySqlCommand cmdBorrarPac = new MySqlCommand(queryBorrarPaciente, conn))
                    {
                        cmdBorrarPac.Parameters.AddWithValue("@id", id);
                        try { cmdBorrarPac.ExecuteNonQuery(); }
                        catch (MySqlException ex) { if (ex.Number != 1451) throw; }
                    }

                    if (!string.IsNullOrWhiteSpace(request.CedulaProfesional) && !string.IsNullOrWhiteSpace(request.Especialidad))
                    {
                        string checkMedicoQuery = "SELECT COUNT(*) FROM medicos WHERE id_Usuario = @id";
                        int existeMedico = 0;
                        using (MySqlCommand cmdCheck = new MySqlCommand(checkMedicoQuery, conn))
                        {
                            cmdCheck.Parameters.AddWithValue("@id", id);
                            existeMedico = Convert.ToInt32(cmdCheck.ExecuteScalar());
                        }

                        if (existeMedico > 0)
                        {
                            string queryUpdateMed = "UPDATE medicos SET cedula_Profesional = @cedula, especialidad = @esp WHERE id_Usuario = @id";
                            using (MySqlCommand cmdUpdMed = new MySqlCommand(queryUpdateMed, conn))
                            {
                                cmdUpdMed.Parameters.AddWithValue("@id", id);
                                cmdUpdMed.Parameters.AddWithValue("@cedula", request.CedulaProfesional);
                                cmdUpdMed.Parameters.AddWithValue("@esp", request.Especialidad);
                                cmdUpdMed.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            string queryInsertMed = "INSERT INTO medicos (id_Usuario, cedula_Profesional, especialidad) VALUES (@id, @cedula, @esp)";
                            using (MySqlCommand cmdInsMed = new MySqlCommand(queryInsertMed, conn))
                            {
                                cmdInsMed.Parameters.AddWithValue("@id", id);
                                cmdInsMed.Parameters.AddWithValue("@cedula", request.CedulaProfesional);
                                cmdInsMed.Parameters.AddWithValue("@esp", request.Especialidad);
                                cmdInsMed.ExecuteNonQuery();
                            }
                        }
                    }
                    else
                    {
                        int idMedicoTemp = 0;
                        using (var cmdBusca = new MySqlCommand("SELECT id_Medico FROM medicos WHERE id_Usuario = @id", conn))
                        {
                            cmdBusca.Parameters.AddWithValue("@id", id);
                            var res = cmdBusca.ExecuteScalar();
                            if (res != null) idMedicoTemp = Convert.ToInt32(res);
                        }

                        if (idMedicoTemp > 0)
                        {
                            using (var cmdDelHor = new MySqlCommand("DELETE FROM horarios WHERE id_Medico = @idMed", conn))
                            {
                                cmdDelHor.Parameters.AddWithValue("@idMed", idMedicoTemp);
                                cmdDelHor.ExecuteNonQuery();
                            }

                            string qCancelarCitas = @"
                                UPDATE citas 
                                SET id_Estatus = (SELECT id_Estatus FROM estatus WHERE nombre = 'Cancelada' LIMIT 1)
                                WHERE id_Medico = @idMed AND id_Estatus IN (SELECT id_Estatus FROM estatus WHERE nombre IN ('Pendiente', 'Confirmada'))";

                            using (var cmdCancCitas = new MySqlCommand(qCancelarCitas, conn))
                            {
                                cmdCancCitas.Parameters.AddWithValue("@idMed", idMedicoTemp);
                                cmdCancCitas.ExecuteNonQuery();
                            }

                            string queryBorrarMedico = "DELETE FROM medicos WHERE id_Usuario = @id";
                            using (MySqlCommand cmdDelMed = new MySqlCommand(queryBorrarMedico, conn))
                            {
                                cmdDelMed.Parameters.AddWithValue("@id", id);
                                try { cmdDelMed.ExecuteNonQuery(); }
                                catch (MySqlException ex) { if (ex.Number != 1451) throw; }
                            }
                        }
                    }
                }
                return Ok(new { message = "Empleado actualizado exitosamente." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al actualizar los datos del empleado. Detalles: " + ex.Message });
            }
        }

        // --- 4. ELIMINAR EMPLEADO ---
        [HttpDelete("{id}")]
        public IActionResult EliminarEmpleado(int id)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string queryBaja = @"
                        UPDATE usuarios 
                        SET id_Estatus = (SELECT id_Estatus FROM estatus WHERE nombre = 'Inactivo' LIMIT 1) 
                        WHERE id_Usuario = @id";

                    using (MySqlCommand cmd = new MySqlCommand(queryBaja, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        int filasAfectadas = cmd.ExecuteNonQuery();

                        if (filasAfectadas > 0)
                            return Ok(new { message = "Empleado dado de baja exitosamente." });
                        else
                            return NotFound(new { error = "No se encontró el empleado a dar de baja." });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error en el servidor al intentar dar de baja. Detalles: " + ex.Message });
            }
        }

        // --- EXTRA: OBTENER CATÁLOGOS ---
        [HttpGet("catalogos")]
        public IActionResult GetCatalogos()
        {
            try
            {
                CatalogosResponse respuesta = new CatalogosResponse();

                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    using (var cmd = new MySqlCommand("SELECT id_Rol, nombre FROM roles WHERE nombre != 'Paciente'", conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read()) { respuesta.Roles.Add(new CatalogoItem { Id = reader.GetInt32(0), Nombre = reader.GetString(1) }); }
                    }

                    using (var cmd = new MySqlCommand("SELECT id_Genero, nombre FROM generos", conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read()) { respuesta.Generos.Add(new CatalogoItem { Id = reader.GetInt32(0), Nombre = reader.GetString(1) }); }
                    }

                    string queryEstatus = "SELECT id_Estatus, nombre FROM estatus WHERE nombre IN ('Activo', 'Inactivo')";
                    using (var cmd = new MySqlCommand(queryEstatus, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read()) { respuesta.Estatus.Add(new CatalogoItem { Id = reader.GetInt32(0), Nombre = reader.GetString(1) }); }
                    }
                }
                return Ok(respuesta);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al obtener los catálogos. Detalles: " + ex.Message });
            }
        }
    }
}