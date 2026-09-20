namespace RestauranteComandas.Api.Models
{
    public class Pago
    {
        public int Id { get; set; }

        public int OrdenId { get; set; }

        public Orden? Orden { get; set; }

        public string MetodoPago { get; set; } = string.Empty;

        public decimal Monto { get; set; }

        public string? Referencia { get; set; }

        public DateTime FechaPago { get; set; } = DateTime.UtcNow;

        public string EstadoPago { get; set; } = "Confirmado";

        public ICollection<PagoDetalle> Detalles { get; set; } = new List<PagoDetalle>();
    }
}