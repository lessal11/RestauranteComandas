namespace RestauranteComandas.Api.Models
{
    public class CajaConteoDenominacion
    {
        public int Id { get; set; }

        public int CajaSesionId { get; set; }

        public CajaSesion? CajaSesion { get; set; }

        // Ejemplo: 0.25, 1.00, 5.00, 20.00
        public decimal Valor { get; set; }

        // Cantidad física contada
        public int Cantidad { get; set; }

        // Valor * Cantidad
        public decimal Subtotal { get; set; }
    }
}