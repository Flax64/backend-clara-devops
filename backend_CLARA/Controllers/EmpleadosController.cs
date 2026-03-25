using backend_CLARA.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;

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
                            m.cedula_Profesional, m.especialidad
                        FROM USUARIOS u
                        INNER JOIN ROLES r ON u.id_Rol = r.id_Rol
                        INNER JOIN GENEROS g ON u.id_Genero = g.id_Genero
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
                                Especialidad = reader.IsDBNull(10) ? "" : reader.GetString(10)
                            });
                        }
                    }
                }
                return Ok(empleados);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al obtener empleados", error = ex.Message });
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

                    // ✨ 1. VERIFICAMOS SI EL CORREO YA EXISTE Y QUÉ ROL TIENE
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

                                // Si el correo existe y es de un paciente... (Mandamos código 409)
                                if (rolExistente == "Paciente")
                                {
                                    return StatusCode(409, new
                                    {
                                        idUsuario = idExistente,
                                        message = "Este correo ya pertenece a un paciente registrado en el sistema.\n\n¿Deseas actualizar sus datos y convertirlo en empleado?"
                                    });
                                }
                                else // Si existe y es cualquier otro rol (Mandamos código 400)
                                {
                                    return BadRequest(new { message = "Este correo ya está en uso por otro empleado y no está disponible." });
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
                        // Obtenemos el ID del usuario recién creado
                        long idNuevoUsuario = cmd.LastInsertedId;

                        // Si nos mandaron cédula, significa que es Médico, así que lo guardamos en su tabla
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
                return StatusCode(500, new { message = "Error al crear", error = ex.Message });
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

                    // 1. VALIDACIÓN DE CORREO: Revisar que el nuevo correo no lo tenga OTRO usuario
                    string checkEmailQuery = "SELECT COUNT(*) FROM USUARIOS WHERE email_Usuario = @email AND id_Usuario != @id";
                    using (MySqlCommand checkCmd = new MySqlCommand(checkEmailQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@email", request.Email);
                        checkCmd.Parameters.AddWithValue("@id", id);
                        if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                        {
                            return BadRequest(new { message = "El correo ingresado ya pertenece a otro usuario en el sistema." });
                        }
                    }

                    // 2. ACTUALIZAR DATOS PRINCIPALES
                    string queryUpdate = @"UPDATE USUARIOS SET 
                        id_Estatus = @idEstatus, id_Genero = @idGenero, id_Rol = @idRol, 
                        nombre_Usuario = @nombre, apellido_P = @apPaterno, apellido_M = @apMaterno, 
                        email_Usuario = @email, password_Usuario = @password, 
                        telefono = @telefono, fecha_Nacimiento = @fechaNac 
                        WHERE id_Usuario = @id";

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
                        cmd.Parameters.AddWithValue("@password", request.Password);
                        cmd.Parameters.AddWithValue("@telefono", request.Telefono);
                        cmd.Parameters.AddWithValue("@fechaNac", request.FechaNacimiento);
                        cmd.ExecuteNonQuery();
                    }

                    // 3. LIMPIEZA DE PACIENTES (Por si estamos convirtiendo un paciente a empleado)
                    string queryBorrarPaciente = "DELETE FROM PACIENTES WHERE id_Usuario = @id";
                    using (MySqlCommand cmdBorrarPac = new MySqlCommand(queryBorrarPaciente, conn))
                    {
                        cmdBorrarPac.Parameters.AddWithValue("@id", id);
                        try { cmdBorrarPac.ExecuteNonQuery(); }
                        catch (MySqlException ex) { if (ex.Number != 1451) throw; } // Ignoramos si tiene citas pasadas
                    }

                    // ✨ 4. GESTIÓN DEL MÉDICO (Ascenso, actualización o remoción)
                    if (!string.IsNullOrWhiteSpace(request.CedulaProfesional) && !string.IsNullOrWhiteSpace(request.Especialidad))
                    {
                        // 4.1 Primero verificamos si ya existe como médico
                        string checkMedicoQuery = "SELECT COUNT(*) FROM MEDICOS WHERE id_Usuario = @id";
                        int existeMedico = 0;
                        using (MySqlCommand cmdCheck = new MySqlCommand(checkMedicoQuery, conn))
                        {
                            cmdCheck.Parameters.AddWithValue("@id", id);
                            existeMedico = Convert.ToInt32(cmdCheck.ExecuteScalar());
                        }

                        // 4.2 Si existe, hacemos UPDATE tradicional
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
                        // 4.3 Si no existe (lo acaban de ascender a Médico), hacemos INSERT
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
                        // Si viene sin cédula (le cambiaron el rol a Administrador/Cajero), lo borramos de los médicos
                        string queryBorrarMedico = "DELETE FROM MEDICOS WHERE id_Usuario = @id";
                        using (MySqlCommand cmdDelMed = new MySqlCommand(queryBorrarMedico, conn))
                        {
                            cmdDelMed.Parameters.AddWithValue("@id", id);
                            try { cmdDelMed.ExecuteNonQuery(); }
                            catch (MySqlException ex) { if (ex.Number != 1451) throw; }
                        }
                    }
                }
                return Ok(new { message = "Empleado actualizado exitosamente." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al actualizar", error = ex.Message });
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

                    // 1. Si era médico, lo borramos primero de la tabla MEDICOS para que MySQL no se queje
                    string queryBorrarMedico = "DELETE FROM MEDICOS WHERE id_Usuario = @id";
                    using (MySqlCommand cmdMed = new MySqlCommand(queryBorrarMedico, conn))
                    {
                        cmdMed.Parameters.AddWithValue("@id", id);
                        cmdMed.ExecuteNonQuery();
                    }

                    // 2. Ahora sí, intentamos borrarlo de la tabla principal de USUARIOS
                    string queryBorrarUsuario = "DELETE FROM USUARIOS WHERE id_Usuario = @id";
                    using (MySqlCommand cmdUsu = new MySqlCommand(queryBorrarUsuario, conn))
                    {
                        cmdUsu.Parameters.AddWithValue("@id", id);
                        int filasAfectadas = cmdUsu.ExecuteNonQuery();

                        if (filasAfectadas > 0)
                        {
                            return Ok(new { message = "Empleado eliminado exitosamente." });
                        }
                        else
                        {
                            return NotFound(new { message = "No se encontró el empleado." });
                        }
                    }
                }
            }
            catch (MySqlException ex)
            {
                // El error 1451 significa que este empleado tiene historial (citas, recetas, ventas)
                if (ex.Number == 1451)
                {
                    return BadRequest(new { message = "No puedes eliminar a este empleado porque ya tiene historial registrado (citas, ventas, etc.). Por seguridad, te recomendamos editarlo y cambiar su Estatus a 'Inactivo'." });
                }
                return StatusCode(500, new { message = "Error de base de datos.", error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error en el servidor.", error = ex.Message });
            }
        }

        // --- EXTRA: OBTENER CATÁLOGOS PARA LLENAR LOS COMBOBOX ---
        [HttpGet("catalogos")]
        public IActionResult GetCatalogos()
        {
            try
            {
                // ✨ Instanciamos tu nuevo modelo de respuesta
                CatalogosResponse respuesta = new CatalogosResponse();

                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    // 1. Obtenemos Roles (Ignorando al 'Paciente')
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

                    // 2. Obtenemos Géneros
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

                    // 3. Obtenemos Estatus (Solo Activo e Inactivo)
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

                // Devolvemos el modelo fuertemente tipado
                return Ok(respuesta);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al obtener catálogos", error = ex.Message });
            }
        }
    }
}
