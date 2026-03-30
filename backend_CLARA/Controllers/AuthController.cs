using backend_CLARA.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
// No necesitas agregar el "using BCrypt.Net" arriba, usaremos la ruta completa para evitar conflictos

namespace backend_CLARA.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly String _connectionString = ConexionDB.Cadena;

        // Nuestra memoria RAM: Diccionario que asocia un Token con el Estado de Recuperación
        private static Dictionary<string, Models.EstadoRecuperacion> _memoriaTemporal = new Dictionary<string, Models.EstadoRecuperacion>();

        // ====================================================================
        // LOGIN DE USUARIOS (CON MIGRACIÓN HÍBRIDA SILENCIOSA)
        // ====================================================================
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    int? idUsuario = null;
                    string dbPassword = null;
                    string estatusUsuario = null; // ✨ Variable para guardar el estatus

                    // ✨ 1. Extraemos los datos y cruzamos con la tabla ESTATUS
                    string Query = @"SELECT u.id_Usuario, u.password_Usuario, e.nombre 
                                     FROM usuarios u 
                                     INNER JOIN estatus e ON u.id_Estatus = e.id_Estatus 
                                     WHERE BINARY u.email_Usuario = @correo";

                    using (MySqlCommand cmd = new MySqlCommand(Query, conn))
                    {
                        cmd.Parameters.AddWithValue("@correo", request.Email);
                        using (MySqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                idUsuario = reader.GetInt32(0);
                                dbPassword = reader.GetString(1);
                                estatusUsuario = reader.GetString(2);
                            }
                        }
                    }

                    // ✨ NUEVO BLOQUEO: Si el estatus es inactivo, lo sacamos de inmediato
                    if (estatusUsuario == "Inactivo" || estatusUsuario == "INACTIVO" || estatusUsuario == "inactivo")
                    {
                        return Unauthorized(new { error = "Tu cuenta se encuentra inactiva. Contacta al administrador." });
                    }

                    // ✨ 2. Si el usuario existe y está activo, evaluamos la contraseña
                    if (idUsuario != null && dbPassword != null)
                    {
                        bool isValid = false;
                        bool needsUpgrade = false;

                        // Detectar si ya es un Hash de BCrypt (Típicamente miden 60 chars y empiezan con $2)
                        if (dbPassword.StartsWith("$2") && dbPassword.Length >= 59)
                        {
                            isValid = BCrypt.Net.BCrypt.Verify(request.Password, dbPassword);
                        }
                        else
                        {
                            // Si NO es un Hash, lo evaluamos como texto plano
                            if (dbPassword == request.Password)
                            {
                                isValid = true;
                                needsUpgrade = true; // Levantamos la bandera para migrarlo
                            }
                        }

                        // ✨ 3. Si la contraseña (plana o hash) fue correcta, lo dejamos entrar
                        if (isValid)
                        {
                            // Si entró con texto plano, la encriptamos de inmediato en la Base de Datos
                            if (needsUpgrade)
                            {
                                string newHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
                                string updateQuery = "UPDATE usuarios SET password_Usuario = @hash WHERE id_Usuario = @id";
                                using (MySqlCommand cmdUpd = new MySqlCommand(updateQuery, conn))
                                {
                                    cmdUpd.Parameters.AddWithValue("@hash", newHash);
                                    cmdUpd.Parameters.AddWithValue("@id", idUsuario.Value);
                                    cmdUpd.ExecuteNonQuery();
                                }
                            }
                            return Ok(new { message = "AUTORIZADO" });
                        }
                    }

                    // Si no existe el usuario o la contraseña fue mala
                    return Unauthorized(new { error = "Usuario o contraseña incorrectos." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error en el servidor al intentar iniciar sesión. Detalles: " + ex.Message });
            }
        }

        // ====================================================================
        // VISUAL BASIC PIDE EL ENLACE (PANTALLA 1)
        // ====================================================================
        [HttpPost("solicitar-enlace")]
        public IActionResult SolicitarEnlace([FromBody] SolicitarEnlaceRequest request)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string Query = "SELECT id_Usuario FROM usuarios WHERE email_Usuario = @correo";
                    using (MySqlCommand cmd = new MySqlCommand(Query, conn))
                    {
                        cmd.Parameters.AddWithValue("@correo", request.Correo);
                        var userId = cmd.ExecuteScalar();

                        if (userId == null)
                            return NotFound(new { error = "El correo proporcionado no existe en el sistema." });

                        string tokenMagico = Guid.NewGuid().ToString();

                        _memoriaTemporal[tokenMagico] = new Models.EstadoRecuperacion
                        {
                            Correo = request.Correo,
                            ClickConfirmado = false,
                            Expiracion = DateTime.Now.AddMinutes(10)
                        };

                        EnviarCorreoMagico(request.Correo, tokenMagico);

                        return Ok(new { Token = tokenMagico, message = "Enlace enviado correctamente." });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al procesar la solicitud de recuperación. Detalles: " + ex.Message });
            }
        }

        // ====================================================================
        // EL USUARIO LE DA CLIC AL CORREO (DESDE SU NAVEGADOR)
        // ====================================================================
        [HttpGet("verificar-clic")]
        public ContentResult VerificarClic(string t)
        {
            if (_memoriaTemporal.ContainsKey(t))
            {
                if (DateTime.Now > _memoriaTemporal[t].Expiracion)
                {
                    return Content("<h1>El enlace ha expirado</h1><p>Por favor solicita uno nuevo en la aplicación.</p>", "text/html");
                }
                _memoriaTemporal[t].ClickConfirmado = true;

                string html = @"
<!DOCTYPE html>
<html lang='es'>
<head>
    <meta charset='UTF-8'> 
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Validación Exitosa</title>
    <style>
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #e9ecef; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; }
        .tarjeta { background-color: white; padding: 40px; border-radius: 12px; box-shadow: 0 10px 20px rgba(0,0,0,0.1); text-align: center; max-width: 500px; border-top: 6px solid #2b5797; }
        h1 { color: #2b5797; margin-bottom: 10px; font-size: 28px; }
        p { color: #555; font-size: 16px; line-height: 1.6; }
        .icono { font-size: 60px; margin-bottom: 15px; }
    </style>
</head>
<body>
    <div class='tarjeta'>
        <div class='icono'>✅</div>
        <h1>¡Validación Exitosa!</h1>
        <p>Tu correo electrónico ha sido confirmado correctamente.</p>
        <p><strong>Ya puedes cerrar esta pestaña del navegador y regresar a la aplicación de la Clínica para crear tu nueva contraseña.</strong></p>
    </div>
</body>
</html>";
                return Content(html, "text/html", System.Text.Encoding.UTF8);
            }
            return Content("<h1>Enlace no válido o caducado.</h1>", "text/html");
        }

        // ====================================================================
        // VISUAL BASIC PREGUNTA CADA 3 SEGUNDOS "¿YA LE DIO CLIC?"
        // ====================================================================
        [HttpGet("estado-enlace/{token}")]
        public IActionResult RevisarEstadoEnlace(string token)
        {
            if (!_memoriaTemporal.ContainsKey(token))
                return BadRequest(new { error = "El enlace no existe o ya expiró." });

            bool yaConfirmo = _memoriaTemporal[token].ClickConfirmado;
            return Ok(new { Confirmado = yaConfirmo });
        }

        // ====================================================================
        // VISUAL BASIC MANDA LA NUEVA CONTRASEÑA (PANTALLA 2)
        // ====================================================================
        [HttpPost("restablecer-password")]
        public IActionResult RestablecerPassword([FromBody] Models.RestablecerDirectoRequest request)
        {
            if (!new Regex(@"^(?=.*[A-Z])(?=.*\d)[A-Za-z\d@$!%*?&]{8,}$").IsMatch(request.NuevaPassword))
                return BadRequest(new { error = "La contraseña proporcionada es demasiado débil." });

            if (!_memoriaTemporal.ContainsKey(request.Token) || !_memoriaTemporal[request.Token].ClickConfirmado)
                return Unauthorized(new { error = "Proceso no autorizado. Debes confirmar el enlace enviado a tu correo primero." });

            string correoSeguro = _memoriaTemporal[request.Token].Correo;

            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    // Extraemos la contraseña actual de la BD para asegurarnos de que no esté reciclando su contraseña
                    string queryPass = "SELECT password_Usuario FROM usuarios WHERE email_Usuario = @correo";
                    string dbPass = "";

                    using (MySqlCommand cmd = new MySqlCommand(queryPass, conn))
                    {
                        cmd.Parameters.AddWithValue("@correo", correoSeguro);
                        var result = cmd.ExecuteScalar();
                        if (result != null) dbPass = result.ToString();
                    }

                    // Hacemos validación híbrida por si estaba en texto plano o en hash
                    bool isSame = false;
                    if (dbPass.StartsWith("$2") && dbPass.Length >= 59)
                        isSame = BCrypt.Net.BCrypt.Verify(request.NuevaPassword, dbPass);
                    else
                        isSame = (dbPass == request.NuevaPassword);

                    if (isSame)
                    {
                        return BadRequest(new { error = "La nueva contraseña no puede ser igual a la anterior." });
                    }

                    // ✨ ENCRIPTAMOS ANTES DE ACTUALIZAR
                    string newHash = BCrypt.Net.BCrypt.HashPassword(request.NuevaPassword);

                    string queryUpdate = "UPDATE usuarios SET password_Usuario = @newpass WHERE email_Usuario = @correo";
                    using (MySqlCommand cmd = new MySqlCommand(queryUpdate, conn))
                    {
                        cmd.Parameters.AddWithValue("@newpass", newHash);
                        cmd.Parameters.AddWithValue("@correo", correoSeguro);
                        cmd.ExecuteNonQuery();
                    }

                    _memoriaTemporal.Remove(request.Token);
                    return Ok(new { message = "Contraseña restablecida exitosamente." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error interno al intentar restablecer la contraseña. Detalles: " + ex.Message });
            }
        }

        // ====================================================================
        // MÉTODO AUXILIAR PARA CORREOS 
        // ====================================================================
        private void EnviarCorreoMagico(string destinatario, string token)
        {
            string miCorreo = "farmacia4850@gmail.com";
            string passwordApp = "fzat yxzn kjby kmpe";

            string urlVerificacion = $"http://localhost:5132/api/auth/verificar-clic?t={token}";

            string cuerpoHtml = $@"
                <div style='font-family: Arial; text-align: center; padding: 20px;'>
                    <h2 style='color: #2b5797;'>Recuperación de Contraseña</h2>
                    <p>Haz clic en el siguiente botón para autorizar el cambio de tu contraseña:</p>
                    <a href='{urlVerificacion}' style='background-color:#2b5797; color:white; padding:15px 25px; text-decoration:none; border-radius:5px; font-weight:bold; display:inline-block; margin: 20px 0;'>CONFIRMAR Y CONTINUAR</a>
                    <p style='color: #888; font-size:12px;'>Este enlace expirará en 10 minutos.</p>
                </div>";

            using (MailMessage mail = new MailMessage())
            {
                mail.From = new MailAddress(miCorreo, "Sistema Clínico");
                mail.To.Add(destinatario);
                mail.Subject = "Autorizar Cambio de Contraseña";
                mail.Body = cuerpoHtml;
                mail.IsBodyHtml = true;

                using (SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587))
                {
                    smtp.Credentials = new NetworkCredential(miCorreo, passwordApp);
                    smtp.EnableSsl = true;
                    smtp.Send(mail);
                }
            }
        }
    }
}