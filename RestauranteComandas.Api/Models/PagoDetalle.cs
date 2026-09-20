namespace RestauranteComandas.Api.Models
{
    public class PagoDetalle
    {
        public int Id { get; set; }

        public int PagoId { get; set; }

        public Pago? Pago { get; set; }

        public int OrdenDetalleId { get; set; }

        public OrdenDetalle? OrdenDetalle { get; set; }

        public int Cantidad { get; set; }

        public decimal PrecioUnitario { get; set; }

        public decimal Subtotal { get; set; }
    }
}