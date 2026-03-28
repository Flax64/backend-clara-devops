namespace backend_CLARA.Models
{
    public class CitaRead
    {
        public int IdCita { get; set; }
        public string Paciente { get; set; }
        public string Medico { get; set; }
        public string Fecha { get; set; } // Formato dd/MM/yyyy
        public string Hora { get; set; }  // Formato hh:mm tt (AM/PM)
        public string Estado { get; set; }
    }
}
