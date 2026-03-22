using backend_CLARA.Models;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Collections;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace backend_CLARA.Controllers
{
    /// <summary>
    /// Controlador que se encarga de gestionar las peticiones relacionadas con la autenticación de los usuarios.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly String _connectionString = "Server=localhost; Database=farmacia; Uid=root ; Pwd=KameHameH4!";

        // Nuestra memoria RAM: Diccionario que asocia un Token con el Estado de Recuperación
        private static Dictionary<string, Models.EstadoRecuperacion> _memoriaTemporal = new Dictionary<string, Models.EstadoRecuperacion>();

        // ====================================================================
        // LOGIN DE USUARIOS
        // ====================================================================
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string Query = "SELECT COUNT(*) FROM usuarios WHERE email_Usuario = @correo AND BINARY password_Usuario = @password";
                    using (MySqlCommand cmd = new MySqlCommand(Query, conn))
                    {
                        cmd.Parameters.AddWithValue("@correo", request.Email);
                        cmd.Parameters.AddWithValue("@password", request.Password);

                        int count = Convert.ToInt32(cmd.ExecuteScalar());

                        if (count > 0)
                        {
                            return Ok(new { message = "AUTORIZADO" });
                        }
                        else
                        {
                            return Unauthorized(new { message = "NO AUTORIZADO" });
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al procesar la solicitud.", error = ex.Message });
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
                            return NotFound("El correo no existe.");

                        // Generamos un Token Único e indescifrable (Ej. "550e8400-e29b-41d4-a716-446655440000")
                        string tokenMagico = Guid.NewGuid().ToString();

                        // Lo guardamos en RAM indicando que aún NO le han dado clic (false)
                        _memoriaTemporal[tokenMagico] = new Models.EstadoRecuperacion
                        {
                            Correo = request.Correo,
                            ClickConfirmado = false,
                            Expiracion = DateTime.Now.AddMinutes(10) // Expira en 10 minutos
                        };

                        // Disparamos el correo real
                        EnviarCorreoMagico(request.Correo, tokenMagico);

                        // Le devolvemos a Visual Basic el Token para que pueda hacer sus preguntas periódicas (Polling)
                        return Ok(new { Token = tokenMagico, Mensaje = "Enlace enviado." });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al procesar la solicitud.", error = ex.Message });
            }
        }

        // ====================================================================
        // EL USUARIO LE DA CLIC AL CORREO (DESDE SU NAVEGADOR)
        // ====================================================================
        // Nota que es un método GET porque los enlaces web son peticiones GET
        [HttpGet("verificar-clic")]
        public ContentResult VerificarClic(string t)
        {
            // Buscamos el token 't' que venía en el enlace del correo
            if (_memoriaTemporal.ContainsKey(t))
            {
                if (DateTime.Now > _memoriaTemporal[t].Expiracion)
                {
                    return Content("<h1>El enlace ha expirado</h1><p>Por favor solicita uno nuevo en la aplicación.</p>", "text/html");
                }
                _memoriaTemporal[t].ClickConfirmado = true;
                // Le devolvemos una página web al usuario diciéndole que regrese al programa
                // Creamos un diseño HTML completo, centrado, con una tarjeta blanca y sombras
                string html = @"
<!DOCTYPE html>
<html lang='es'>
<head>
    <meta charset='UTF-8'> 
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Validación Exitosa</title>
    <style>
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background-color: #e9ecef; /* Fondo gris claro */
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
        }
        .tarjeta {
            background-color: white;
            padding: 40px;
            border-radius: 12px;
            box-shadow: 0 10px 20px rgba(0,0,0,0.1); /* Sombra elegante */
            text-align: center;
            max-width: 500px;
            border-top: 6px solid #2b5797; /* Línea azul del Tec/Clínica */
        }
        h1 {
            color: #2b5797;
            margin-bottom: 10px;
            font-size: 28px;
        }
        p {
            color: #555;
            font-size: 16px;
            line-height: 1.6;
        }
        .icono {
            font-size: 60px;
            margin-bottom: 15px;
        }
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
                // Nos aseguramos de decirle al navegador que va en UTF8
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
                return BadRequest("Token no existe o expiró");

            // Le respondemos a Visual Basic si el usuario ya visitó la página web o no
            bool yaConfirmo = _memoriaTemporal[token].ClickConfirmado;
            return Ok(new { Confirmado = yaConfirmo });
        }

        // ====================================================================
        // VISUAL BASIC MANDA LA NUEVA CONTRASEÑA (PANTALLA 2)
        // ====================================================================
        [HttpPost("restablecer-password")]
        public IActionResult RestablecerPassword([FromBody] Models.RestablecerDirectoRequest request)
        {
            // Validamos seguridad básica
            if (!new Regex(@"^(?=.*[A-Z])(?=.*\d)[A-Za-z\d@$!%*?&]{8,}$").IsMatch(request.NuevaPassword))
                return BadRequest("Contraseña débil.");

            if (!_memoriaTemporal.ContainsKey(request.Token) || !_memoriaTemporal[request.Token].ClickConfirmado)
                return Unauthorized("Proceso no autorizado o enlace expirado.");

            // Si llegamos aquí, recuperamos a qué correo pertenecía ese token
            string correoSeguro = _memoriaTemporal[request.Token].Correo;

            try
            {
                using (MySqlConnection conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    // Validar si el es la misma contraseña
                    string queryPass = "SELECT COUNT(*) FROM usuarios WHERE password_Usuario = @password and email_Usuario = @correo";
                    bool existePass = false;

                    using (MySqlCommand cmd = new MySqlCommand(queryPass, conn))
                    {
                        cmd.Parameters.AddWithValue("@password", request.NuevaPassword);
                        cmd.Parameters.AddWithValue("@correo", correoSeguro); // Identificamos de quién es
                        existePass = Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                    }

                    if (existePass)
                    {
                        return BadRequest("La nueva contraseña no puede ser igual a la anterior.");
                    }

                    string queryUpdate = "UPDATE usuarios SET password_Usuario = @newpass WHERE email_Usuario = @correo";
                    using (MySqlCommand cmd = new MySqlCommand(queryUpdate, conn))
                    {
                        cmd.Parameters.AddWithValue("@newpass", request.NuevaPassword);
                        cmd.Parameters.AddWithValue("@correo", correoSeguro);
                        cmd.ExecuteNonQuery();
                    }

                    // Destruimos el token de la memoria RAM para evitar que lo reutilicen
                    _memoriaTemporal.Remove(request.Token);

                    return Ok(new { message = "Contraseña restablecida exitosamente." });
                }
            } catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al procesar la solicitud.", error = ex.Message });
            }
        }

        // ====================================================================
        // MÉTODO AUXILIAR PARA CORREOS (Con un botón de Enlace Real)
        // ====================================================================
        private void EnviarCorreoMagico(string destinatario, string token)
        {
            string miCorreo = "farmacia4850@gmail.com";
            string passwordApp = "fzat yxzn kjby kmpe";

            // Construimos la URL mágica a la que el usuario le dará clic (Ruta 2)
            string urlVerificacion = $"http://DESKTOP-24HK526:5132/api/auth/verificar-clic?t={token}";

            string cuerpoHtml = $@"
                <div style='font-family: Arial; text-align: center; padding: 20px;'>
                    <h2 style='color: #2b5797;'>Recuperación de Contraseña</h2>
                    <p>Haz clic en el siguiente botón para autorizar el cambio de tu contraseña:</p>
                    <a href='{urlVerificacion}' style='background-color:#2b5797; color:white; padding:15px 25px; text-decoration:none; border-radius:5px; font-weight:bold; display:inline-block; margin: 20px 0;'>CONFIRMAR Y CONTINUAR</a>
                    <p style='color: #888; font-size:12px;'>Este enlace expirará en 10 minutos.</p>
                </div>";

            // Enviamos el correo con el enlace mágico
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
 

