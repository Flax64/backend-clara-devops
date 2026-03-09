namespace backend_CLARA.Models
{
    public class RegistroRequest
    {
        public string Nombre { get; set; }
        public string ApellidoPaterno { get; set; }
        public string ApellidoMaterno { get; set; }
        public DateTime FechaNacimiento { get; set; }
        public string Telefono { get; set; }
        public int IdGenero { get; set; }
        public int IdEstatus { get; set; }
        public int IdRol { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
    }
}
