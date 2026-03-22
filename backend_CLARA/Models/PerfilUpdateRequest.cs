namespace backend_CLARA.Models
{
    public class PerfilUpdateRequest
    {
        public string Nombre { get; set; }
        public string ApellidoP { get; set; }
        public string ApellidoM { get; set; }
        public string Telefono { get; set; }
        public DateTime FechaNacimiento { get; set; }
        public int IdGenero { get; set; }
    }
}
