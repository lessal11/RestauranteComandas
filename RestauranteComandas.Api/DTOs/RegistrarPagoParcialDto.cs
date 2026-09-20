namespace RestauranteComandas.Api.DTOs
{
    public class RegistrarPagoParcialDto
    {
        public int OrdenId { get; set; }

        public string MetodoPago { get; set; } = string.Empty;

        public string? Referencia { get; set; }

        public List<RegistrarPagoDetalleDto> Detalles { get; set; } = new();
    }
}