namespace backend_CLARA.Models
{
    public class CitaRequest
    {
        public int IdPaciente { get; set; }
        public int IdMedico { get; set; }
        public string Fecha { get; set; } // Formato yyyy-MM-dd
        public string Hora { get; set; }  // Formato HH:mm:ss
        public int IdEstatus { get; set; } // Solo se usará en el Update
    }
}
