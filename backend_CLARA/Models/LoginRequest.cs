namespace backend_CLARA.Models
{
    /// <summary>
    /// Clase que nos define los atributos necesarios para realizar el login de un usuario.
    /// </summary>

    public class LoginRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }
}

