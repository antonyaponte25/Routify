using Microsoft.EntityFrameworkCore;
using Routify.Web.Helpers;
using Routify.Web.Models;

namespace Routify.Web.Data;

/// <summary>
/// Contexto de EF Core. El esquema real lo define el script Database/RoutifyDB.sql
/// (enfoque "script SQL como fuente de verdad"): no se usan migraciones.
/// </summary>
public class RoutifyDbContext : DbContext
{
    public RoutifyDbContext(DbContextOptions<RoutifyDbContext> options) : base(options) { }

    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<ZonaReparto> Zonas => Set<ZonaReparto>();
    public DbSet<EstadoPedido> Estados => Set<EstadoPedido>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<DetallePedido> Detalles => Set<DetallePedido>();
    public DbSet<HistorialEstado> Historial => Set<HistorialEstado>();
    public DbSet<Repartidor> Repartidores => Set<Repartidor>();
    public DbSet<Ruta> Rutas => Set<Ruta>();
    public DbSet<Parada> Paradas => Set<Parada>();
    public DbSet<PruebaEntrega> Pruebas => Set<PruebaEntrega>();
    public DbSet<UbicacionRepartidor> Ubicaciones => Set<UbicacionRepartidor>();
    public DbSet<Tracking> Enlaces => Set<Tracking>();
    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();
    public DbSet<Configuracion> Parametros => Set<Configuracion>();
    public DbSet<SincronizacionOffline> Sincronizaciones => Set<SincronizacionOffline>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Todas las fechas se guardan y leen como UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        // La columna "fecha" de rutas es de tipo date: se usa como DateTime sin conversión UTC.
        mb.Entity<Ruta>().Property(r => r.Fecha).HasConversion(
            v => v.Date,
            v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified));

        // Usuario 1 - 0..1 Repartidor
        mb.Entity<Repartidor>()
            .HasOne(r => r.Usuario)
            .WithOne(u => u.Repartidor)
            .HasForeignKey<Repartidor>(r => r.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<Repartidor>()
            .HasOne(r => r.Zona).WithMany().HasForeignKey(r => r.ZonaId).OnDelete(DeleteBehavior.Restrict);

        mb.Entity<Usuario>()
            .HasOne(u => u.Rol).WithMany(r => r.Usuarios).HasForeignKey(u => u.RolId).OnDelete(DeleteBehavior.Restrict);

        // Pedido
        mb.Entity<Pedido>(e =>
        {
            e.HasOne(p => p.Cliente).WithMany(c => c.Pedidos).HasForeignKey(p => p.ClienteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Estado).WithMany().HasForeignKey(p => p.EstadoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Zona).WithMany().HasForeignKey(p => p.ZonaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Repartidor).WithMany().HasForeignKey(p => p.RepartidorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Creador).WithMany().HasForeignKey(p => p.CreadoPor).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(p => p.Detalles).WithOne(d => d.Pedido).HasForeignKey(d => d.PedidoId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Historial).WithOne(h => h.Pedido).HasForeignKey(h => h.PedidoId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Enlaces).WithOne(t => t.Pedido).HasForeignKey(t => t.PedidoId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Notificaciones).WithOne(n => n.Pedido).HasForeignKey(n => n.PedidoId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Pruebas).WithOne(x => x.Pedido).HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Restrict);
        });

        // Historial: dos FK hacia estados_pedido
        mb.Entity<HistorialEstado>(e =>
        {
            e.HasOne(h => h.EstadoAnterior).WithMany().HasForeignKey(h => h.EstadoAnteriorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(h => h.EstadoNuevo).WithMany().HasForeignKey(h => h.EstadoNuevoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(h => h.Usuario).WithMany().HasForeignKey(h => h.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        // Rutas y paradas
        mb.Entity<Ruta>()
            .HasOne(r => r.Repartidor).WithMany(r => r.Rutas).HasForeignKey(r => r.RepartidorId).OnDelete(DeleteBehavior.Restrict);

        mb.Entity<Parada>(e =>
        {
            e.HasOne(p => p.Ruta).WithMany(r => r.Paradas).HasForeignKey(p => p.RutaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Pedido).WithMany().HasForeignKey(p => p.PedidoId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<UbicacionRepartidor>()
            .HasOne(u => u.Repartidor).WithMany().HasForeignKey(u => u.RepartidorId).OnDelete(DeleteBehavior.Restrict);
    }
}
