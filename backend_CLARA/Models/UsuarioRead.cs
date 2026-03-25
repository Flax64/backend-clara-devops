namespace backend_CLARA.Models
{
    public class UsuarioRead
    {
        public int IdUsuario { get; set; }
        public string Nombre { get; set; }
        public string ApellidoPaterno { get; set; }
        public string ApellidoMaterno { get; set; }
        public string Email { get; set; }
        public string Rol { get; set; }
        public string Telefono { get; set; }
        public string FechaNacimiento { get; set; }
        public string Genero { get; set; }
        public string CedulaProfesional { get; set; }
        public string Especialidad { get; set; }
    }
}
