namespace RestauranteComandas.Api.Models
{
    public class OrdenDetalle
    {
        public int Id { get; set; }

        public int OrdenId { get; set; }

        public Orden? Orden { get; set; }

        public int MenuItemId { get; set; }

        public MenuItem? MenuItem { get; set; }

        public int Cantidad { get; set; }

        public decimal PrecioUnitario { get; set; }

        public decimal Subtotal { get; set; }

        public string? DetallePersonalizado { get; set; }
    }
}