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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Mesa>()
                .HasIndex(m => m.Numero)
                .IsUnique();

            modelBuilder.Entity<Usuario>()
                .HasIndex(u => u.NombreUsuario)
                .IsUnique();

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
                .WithOne(o => o.Pago)
                .HasForeignKey<Pago>(p => p.OrdenId);

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
        }
    }
}