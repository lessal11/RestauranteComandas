namespace RestauranteComandas.Api.Models
{
    public class CajaSesion
    {
        public int Id { get; set; }

        // =============================================
        // APERTURA
        // =============================================

        public int UsuarioAperturaId { get; set; }

        public Usuario? UsuarioApertura { get; set; }

        public DateTime FechaApertura { get; set; } =
            DateTime.UtcNow;

        public decimal FondoInicial { get; set; }

        public string Estado { get; set; } =
            "Abierta";


        // =============================================
        // CIERRE
        // =============================================

        public int? UsuarioCierreId { get; set; }

        public Usuario? UsuarioCierre { get; set; }

        public DateTime? FechaCierre { get; set; }

        public decimal? EfectivoContado { get; set; }


        // =============================================
        // TOTALES GUARDADOS AL CERRAR
        // =============================================

        public decimal? TotalEfectivo { get; set; }

        public decimal? TotalTransferencia { get; set; }

        public decimal? TotalD1 { get; set; }

        public decimal? TotalCobrado { get; set; }

        public decimal? EfectivoEsperado { get; set; }

        public decimal? Diferencia { get; set; }


        // =============================================
        // OBSERVACIÓN
        // =============================================

        public string? ObservacionCierre { get; set; }


        // =============================================
        // PAGOS DE ESTA SESIÓN
        // =============================================

        public ICollection<Pago> Pagos { get; set; } =
            new List<Pago>();
    }
}