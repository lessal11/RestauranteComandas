using Microsoft.EntityFrameworkCore;
using RestauranteComandas.Api.Models;

namespace RestauranteComandas.Api.Data
{
    public class RestauranteDbContext : DbContext
    {
        public RestauranteDbContext(DbContextOptions<RestauranteDbContext> options)
            : base(options)
        {
        }

        public DbSet<Mesa> Mesas { get; set; }
        public DbSet<Usuario> Usuarios { get; set; }
        public DbSet<MenuItem> MenuItems { get; set; }
        public DbSet<Orden> Ordenes { get; set; }
        public DbSet<OrdenDetalle> OrdenDetalles { get; set; }
        public DbSet<Pago> Pagos { get; set; }
        public DbSet<PagoDetalle> PagoDetalles { get; set; }
        public DbSet<CajaSesion> CajaSesiones { get; set; }
        public DbSet<CajaConteoDenominacion> CajaConteoDenominaciones { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Mesa>()
                .HasIndex(m => m.Numero)
                .IsUnique();

            modelBuilder.Entity<Usuario>()
                .HasIndex(u => u.NombreUsuario)
                .IsUnique();

            modelBuilder.Entity<CajaSesion>()
                .HasIndex(c => c.Estado)
                .IsUnique()
                .HasFilter("\"Estado\" = 'Abierta'");

            modelBuilder.Entity<Orden>()
                .HasOne(o => o.Mesa)
                .WithMany(m => m.Ordenes)
                .HasForeignKey(o => o.MesaId);

            modelBuilder.Entity<Orden>()
                .HasOne(o => o.Usuario)
                .WithMany(u => u.Ordenes)
                .HasForeignKey(o => o.UsuarioId);

            modelBuilder.Entity<OrdenDetalle>()
                .HasOne(d => d.Orden)
                .WithMany(o => o.Detalles)
                .HasForeignKey(d => d.OrdenId);

            modelBuilder.Entity<OrdenDetalle>()
                .HasOne(d => d.MenuItem)
                .WithMany(m => m.OrdenDetalles)
                .HasForeignKey(d => d.MenuItemId);

            modelBuilder.Entity<Pago>()
                .HasOne(p => p.Orden)
                .WithMany(o => o.Pagos)
                .HasForeignKey(p => p.OrdenId);

            modelBuilder.Entity<PagoDetalle>()
                .HasOne(pd => pd.Pago)
                .WithMany(p => p.Detalles)
                .HasForeignKey(pd => pd.PagoId);

            modelBuilder.Entity<PagoDetalle>()
                .HasOne(pd => pd.OrdenDetalle)
                .WithMany(od => od.PagoDetalles)
                .HasForeignKey(pd => pd.OrdenDetalleId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MenuItem>()
                .Property(m => m.Precio)
                .HasPrecision(10, 2);

            modelBuilder.Entity<Orden>()
                .Property(o => o.Total)
                .HasPrecision(10, 2);

            modelBuilder.Entity<OrdenDetalle>()
                .Property(d => d.PrecioUnitario)
                .HasPrecision(10, 2);

            modelBuilder.Entity<OrdenDetalle>()
                .Property(d => d.Subtotal)
                .HasPrecision(10, 2);

            modelBuilder.Entity<Pago>()
                .Property(p => p.Monto)
                .HasPrecision(10, 2);

            modelBuilder.Entity<PagoDetalle>()
                .Property(pd => pd.PrecioUnitario)
                .HasPrecision(10, 2);

            modelBuilder.Entity<PagoDetalle>()
                .Property(pd => pd.Subtotal)
                .HasPrecision(10, 2);
            
            modelBuilder.Entity<CajaSesion>()
                .HasOne(c => c.UsuarioApertura)
                .WithMany()
                .HasForeignKey(c => c.UsuarioAperturaId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CajaSesion>()
                .HasOne(c => c.UsuarioCierre)
                .WithMany()
                .HasForeignKey(c => c.UsuarioCierreId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Pago>()
                .HasOne(p => p.CajaSesion)
                .WithMany(c => c.Pagos)
                .HasForeignKey(p => p.CajaSesionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Pago>()
                .HasOne(p => p.UsuarioRegistro)
                .WithMany()
                .HasForeignKey(p => p.UsuarioRegistroId)
                .OnDelete(DeleteBehavior.Restrict);

            // =============================================
            // PRECISIÓN - CAJA
            // =============================================

            modelBuilder.Entity<CajaSesion>()
                .Property(c => c.FondoInicial)
                .HasPrecision(10, 2);

            modelBuilder.Entity<CajaSesion>()
                .Property(c => c.EfectivoContado)
                .HasPrecision(10, 2);

            modelBuilder.Entity<CajaSesion>()
                .Property(c => c.TotalEfectivo)
                .HasPrecision(10, 2);

            modelBuilder.Entity<CajaSesion>()
                .Property(c => c.TotalTransferencia)
                .HasPrecision(10, 2);

            modelBuilder.Entity<CajaSesion>()
                .Property(c => c.TotalD1)
                .HasPrecision(10, 2);

            modelBuilder.Entity<CajaSesion>()
                .Property(c => c.TotalCobrado)
                .HasPrecision(10, 2);

            modelBuilder.Entity<CajaSesion>()
                .Property(c => c.EfectivoEsperado)
                .HasPrecision(10, 2);

            modelBuilder.Entity<CajaSesion>()
                .Property(c => c.Diferencia)
                .HasPrecision(10, 2);

            // =============================================
            // CONTEO DE DENOMINACIONES DE CAJA
            // =============================================

            modelBuilder.Entity<CajaConteoDenominacion>()
                .HasOne(d => d.CajaSesion)
                .WithMany(c => c.ConteoDenominaciones)
                .HasForeignKey(d => d.CajaSesionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CajaConteoDenominacion>()
                .HasIndex(d => new
                {
                    d.CajaSesionId,
                    d.Valor
                })
                .IsUnique();

            modelBuilder.Entity<CajaConteoDenominacion>()
                .Property(d => d.Valor)
                .HasPrecision(10, 2);

            modelBuilder.Entity<CajaConteoDenominacion>()
                .Property(d => d.Subtotal)
                .HasPrecision(10, 2);
        }
    }
}