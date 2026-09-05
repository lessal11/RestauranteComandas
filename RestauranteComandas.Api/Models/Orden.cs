namespace RestauranteComandas.Api.Models
{
    public class Orden
    {
        public int Id { get; set; }

        public int MesaId { get; set; }

        public Mesa? Mesa { get; set; }

        public int UsuarioId { get; set; }

        public Usuario? Usuario { get; set; }

        public DateTime Fecha { get; set; } = DateTime.Now;

        public string Estado { get; set; } = "Pendiente";

        public decimal Total { get; set; }

        public ICollection<OrdenDetalle> Detalles { get; set; } = new List<OrdenDetalle>();

        public Pago? Pago { get; set; }
    }
}