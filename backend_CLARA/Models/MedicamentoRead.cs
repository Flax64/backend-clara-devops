namespace backend_CLARA.Models
{
    public class MedicamentoRead
    {
        public int IdMedicamento { get; set; }
        public int IdEstatus { get; set; }
        public string Estatus { get; set; }

        // Propiedad combinada para mostrar bonito en la tabla: "Paracetamol 500mg"
        public string NombreCompleto { get; set; }

        // Propiedades separadas para cuando el usuario le dé clic en "Editar" y llenes las cajas
        public string Nombre { get; set; }
        public string Descripcion { get; set; }
        public decimal Precio { get; set; }
        public int Stock { get; set; }
        public decimal ConcentracionValor { get; set; }
        public string ConcentracionUnidad { get; set; }
    }
}
