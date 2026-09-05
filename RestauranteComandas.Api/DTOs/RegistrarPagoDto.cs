namespace RestauranteComandas.Api.DTOs
{
    public class RegistrarPagoDto
    {
        public int OrdenId { get; set; }

        public string MetodoPago { get; set; } = string.Empty;

        public decimal Monto { get; set; }

        public string? Referencia { get; set; }
    }
}