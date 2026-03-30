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
        private readonly string _connectionString = "Server=localhost; Database=farmacia; Uid=root ; Pwd=KameHameH4!";

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
                        FROM USUARIOS u
                        INNER JOIN ROLES r ON u.id_Rol = r.id_Rol
                        INNER JOIN GENEROS g ON u.id_Genero = g.id_Genero
                        INNER JOIN ESTATUS e ON u.id_Estatus = e.id_Estatus
                        LEFT JOIN MEDICOS m ON u.id_Usuario = m.id_Usuario
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

        // --- 2. CREAR (CON VALIDACIÓN DE CORREO Y PACIENTE) ---
        [HttpPost]
        public IActionResult CrearEmpleado([FromBody] UsuarioRequest request)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    // 1. VERIFICAMOS SI EL CORREO YA EXISTE Y QUÉ ROL TIENE
                    string checkQuery = @"
                        SELECT u.id_Usuario, r.nombre 
                        FROM USUARIOS u 
                        INNER JOIN ROLES r ON u.id_Rol = r.id_Rol 
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

                    // 2. SI EL CORREO NO EXISTE, LO INSERTAMOS NORMALMENTE
                    string query = @"INSERT INTO USUARIOS 
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
                        cmd.Parameters.AddWithValue("@password", request.Password);
                        cmd.Parameters.AddWithValue("@telefono", request.Telefono);
                        cmd.Parameters.AddWithValue("@fechaNac", request.FechaNacimiento);
                        cmd.ExecuteNonQuery();

                        long idNuevoUsuario = cmd.LastInsertedId;

                        if (!string.IsNullOrEmpty(request.CedulaProfesional) && !string.IsNullOrEmpty(request.Especialidad))
                        {
                            string queryMedico = "INSERT INTO MEDICOS (id_Usuario, cedula_Profesional, especialidad) VALUES (@idUsu, @cedula, @especialidad)";
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

                    // 1. VALIDACIÓN DE CORREO
                    string checkEmailQuery = "SELECT COUNT(*) FROM USUARIOS WHERE email_Usuario = @email AND id_Usuario != @id";
                    using (MySqlCommand checkCmd = new MySqlCommand(checkEmailQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@email", request.Email);
                        checkCmd.Parameters.AddWithValue("@id", id);
                        if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                        {
                            return BadRequest(new { error = "El correo ingresado ya pertenece a otro usuario en el sistema." });
                        }
                    }

                    // 2. ACTUALIZAR DATOS PRINCIPALES
                    string queryUpdate = @"UPDATE USUARIOS SET 
                        id_Estatus = @idEstatus, id_Genero = @idGenero, id_Rol = @idRol, 
                        nombre_Usuario = @nombre, apellido_P = @apPaterno, apellido_M = @apMaterno, 
                        email_Usuario = @email, telefono = @telefono, fecha_Nacimiento = @fechaNac ";

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

                        if (!string.IsNullOrWhiteSpace(request.Password))
                        {
                            cmd.Parameters.AddWithValue("@password", request.Password);
                        }

                        cmd.ExecuteNonQuery();
                    }

                    // 3. LIMPIEZA DE PACIENTES
                    string queryBorrarPaciente = "DELETE FROM PACIENTES WHERE id_Usuario = @id";
                    using (MySqlCommand cmdBorrarPac = new MySqlCommand(queryBorrarPaciente, conn))
                    {
                        cmdBorrarPac.Parameters.AddWithValue("@id", id);
                        try { cmdBorrarPac.ExecuteNonQuery(); }
                        catch (MySqlException ex) { if (ex.Number != 1451) throw; }
                    }

                    // ✨ 4. GESTIÓN DEL MÉDICO (Ascenso, actualización o remoción inteligente)
                    if (!string.IsNullOrWhiteSpace(request.CedulaProfesional) && !string.IsNullOrWhiteSpace(request.Especialidad))
                    {
                        string checkMedicoQuery = "SELECT COUNT(*) FROM MEDICOS WHERE id_Usuario = @id";
                        int existeMedico = 0;
                        using (MySqlCommand cmdCheck = new MySqlCommand(checkMedicoQuery, conn))
                        {
                            cmdCheck.Parameters.AddWithValue("@id", id);
                            existeMedico = Convert.ToInt32(cmdCheck.ExecuteScalar());
                        }

                        if (existeMedico > 0)
                        {
                            string queryUpdateMed = "UPDATE MEDICOS SET cedula_Profesional = @cedula, especialidad = @esp WHERE id_Usuario = @id";
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
                            string queryInsertMed = "INSERT INTO MEDICOS (id_Usuario, cedula_Profesional, especialidad) VALUES (@id, @cedula, @esp)";
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
                        // ✨ 4.4 AQUÍ OCURRE LA MAGIA: Si le cambiaron el rol y ya no es médico
                        int idMedicoTemp = 0;
                        using (var cmdBusca = new MySqlCommand("SELECT id_Medico FROM MEDICOS WHERE id_Usuario = @id", conn))
                        {
                            cmdBusca.Parameters.AddWithValue("@id", id);
                            var res = cmdBusca.ExecuteScalar();
                            if (res != null) idMedicoTemp = Convert.ToInt32(res);
                        }

                        if (idMedicoTemp > 0)
                        {
                            // A. Borramos todos sus horarios
                            using (var cmdDelHor = new MySqlCommand("DELETE FROM HORARIOS WHERE id_Medico = @idMed", conn))
                            {
                                cmdDelHor.Parameters.AddWithValue("@idMed", idMedicoTemp);
                                cmdDelHor.ExecuteNonQuery();
                            }

                            // B. Cancelamos sus citas futuras/pendientes
                            string qCancelarCitas = @"
                                UPDATE CITAS 
                                SET id_Estatus = (SELECT id_Estatus FROM ESTATUS WHERE nombre = 'Cancelada' LIMIT 1)
                                WHERE id_Medico = @idMed AND id_Estatus IN (SELECT id_Estatus FROM ESTATUS WHERE nombre IN ('Pendiente', 'Confirmada'))";

                            using (var cmdCancCitas = new MySqlCommand(qCancelarCitas, conn))
                            {
                                cmdCancCitas.Parameters.AddWithValue("@idMed", idMedicoTemp);
                                cmdCancCitas.ExecuteNonQuery();
                            }

                            // C. Intentamos borrar el registro de MEDICOS
                            string queryBorrarMedico = "DELETE FROM MEDICOS WHERE id_Usuario = @id";
                            using (MySqlCommand cmdDelMed = new MySqlCommand(queryBorrarMedico, conn))
                            {
                                cmdDelMed.Parameters.AddWithValue("@id", id);
                                try
                                {
                                    cmdDelMed.ExecuteNonQuery();
                                }
                                catch (MySqlException ex)
                                {
                                    // Si da error 1451 (Tiene citas pasadas completadas o consultas), lo tragamos en silencio.
                                    // El usuario ya cambió de rol, por lo que no aparecerá en los combos de creación de citas,
                                    // pero su ID de médico sobrevive para no corromper la base de datos histórica.
                                    if (ex.Number != 1451) throw;
                                }
                            }
                        }
                    }
                }
                return Ok(new { message = "Empleado actualizado exitosamente. (Si se le removió el puesto de médico, sus horarios fueron borrados y citas futuras canceladas)." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al actualizar los datos del empleado. Detalles: " + ex.Message });
            }
        }

        // --- 4. ELIMINAR EMPLEADO (BORRADO LÓGICO / DAR DE BAJA) ---
        [HttpDelete("{id}")]
        public IActionResult EliminarEmpleado(int id)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    string queryBaja = @"
                        UPDATE USUARIOS 
                        SET id_Estatus = (SELECT id_Estatus FROM ESTATUS WHERE nombre = 'Inactivo' LIMIT 1) 
                        WHERE id_Usuario = @id";

                    using (MySqlCommand cmd = new MySqlCommand(queryBaja, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        int filasAfectadas = cmd.ExecuteNonQuery();

                        if (filasAfectadas > 0)
                        {
                            return Ok(new { message = "Empleado dado de baja exitosamente." });
                        }
                        else
                        {
                            return NotFound(new { error = "No se encontró el empleado a dar de baja." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error en el servidor al intentar dar de baja. Detalles: " + ex.Message });
            }
        }

        // --- EXTRA: OBTENER CATÁLOGOS PARA LLENAR LOS COMBOBOX ---
        [HttpGet("catalogos")]
        public IActionResult GetCatalogos()
        {
            try
            {
                CatalogosResponse respuesta = new CatalogosResponse();

                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    using (var cmd = new MySqlCommand("SELECT id_Rol, nombre FROM ROLES WHERE nombre != 'Paciente'", conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            respuesta.Roles.Add(new CatalogoItem
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.GetString(1)
                            });
                        }
                    }

                    using (var cmd = new MySqlCommand("SELECT id_Genero, nombre FROM GENEROS", conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            respuesta.Generos.Add(new CatalogoItem
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.GetString(1)
                            });
                        }
                    }

                    string queryEstatus = "SELECT id_Estatus, nombre FROM ESTATUS WHERE nombre IN ('Activo', 'Inactivo')";
                    using (var cmd = new MySqlCommand(queryEstatus, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            respuesta.Estatus.Add(new CatalogoItem
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.GetString(1)
                            });
                        }
                    }
                }
                return Ok(respuesta);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al obtener los catálogos del sistema. Detalles: " + ex.Message });
            }
        }
    }
}