namespace backend_CLARA.Models
{
    public class RestablecerDirectoRequest
    {
        public string Token { get; set; } // El identificador mágico
        public string NuevaPassword { get; set; }
    }
}
