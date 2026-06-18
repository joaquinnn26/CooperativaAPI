using System.Data.Entity;

namespace CooperativaAPI.Data
{
    public class AppDbContext : DbContext
    {
        static AppDbContext()
        {
            Database.SetInitializer<AppDbContext>(null);
        }

        public AppDbContext() : base("DefaultConnection") { }

        public DbSet<Empleado> Empleados { get; set; }
        public DbSet<Medidor> Medidores { get; set; }
        public DbSet<Medicion> Mediciones { get; set; }
        public DbSet<TipoLectura> TiposLectura { get; set; }
        public DbSet<RutaMedicion> RutasMedicion { get; set; }
        public DbSet<RutaMedicionDetalle> RutasMedicionDetalle { get; set; }
        public DbSet<Anuncio> Anuncios { get; set; }
        public DbSet<PrioridadAnuncio> PrioridadesAnuncio { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Empleado>().ToTable("Empleado").HasKey(x => x.idEmpleado);
            modelBuilder.Entity<Empleado>().Ignore(x => x.Cargo);
            modelBuilder.Entity<Empleado>().Ignore(x => x.Persona);
            modelBuilder.Entity<Empleado>().Ignore(x => x.UsuarioSistema);
            modelBuilder.Entity<Empleado>().Ignore(x => x.Medicion);
            modelBuilder.Entity<Empleado>().Ignore(x => x.HistorialEstadoSolicitud);
            modelBuilder.Entity<Empleado>().Ignore(x => x.SolicitudServicio);
            modelBuilder.Entity<Empleado>().Ignore(x => x.Factura);

            modelBuilder.Entity<Medidor>().ToTable("Medidor").HasKey(x => x.idMedidor);
            modelBuilder.Entity<Medidor>().Ignore(x => x.Medicion);
            modelBuilder.Entity<Medidor>().Ignore(x => x.ServicioSocio);
            modelBuilder.Entity<Medidor>().Ignore(x => x.TipoMedidor);

            modelBuilder.Entity<TipoLectura>().ToTable("TipoLectura").HasKey(x => x.idTipoLectura);
            modelBuilder.Entity<TipoLectura>().Ignore(x => x.Medicion);

            modelBuilder.Entity<Medicion>().ToTable("Medicion").HasKey(x => x.idMedicion);
            modelBuilder.Entity<Medicion>().Ignore(x => x.Empleado);
            modelBuilder.Entity<Medicion>().Ignore(x => x.ServicioSocio);
            modelBuilder.Entity<Medicion>().Ignore(x => x.TipoLectura);
            modelBuilder.Entity<Medicion>().Ignore(x => x.Medidor);

            modelBuilder.Entity<RutaMedicion>().ToTable("RutaMedicion").HasKey(x => x.idRutaMedicion);
            modelBuilder.Entity<RutaMedicion>().Ignore(x => x.Empleado);
            modelBuilder.Entity<RutaMedicion>()
                .Property(x => x.Nombre)
                .IsRequired()
                .HasMaxLength(120);
            modelBuilder.Entity<RutaMedicion>()
                .Property(x => x.Estado)
                .IsRequired()
                .HasMaxLength(20);
            modelBuilder.Entity<RutaMedicion>()
                .Property(x => x.FirmaMimeType)
                .HasMaxLength(50);
            modelBuilder.Entity<RutaMedicion>()
                .Property(x => x.Observacion)
                .HasMaxLength(500);

            modelBuilder.Entity<RutaMedicionDetalle>().ToTable("RutaMedicionDetalle").HasKey(x => x.idRutaMedicionDetalle);
            modelBuilder.Entity<RutaMedicionDetalle>().Ignore(x => x.Medidor);
            modelBuilder.Entity<RutaMedicionDetalle>().Ignore(x => x.Medicion);
            modelBuilder.Entity<RutaMedicionDetalle>()
                .Property(x => x.Estado)
                .IsRequired()
                .HasMaxLength(30);
            modelBuilder.Entity<RutaMedicionDetalle>()
                .Property(x => x.Observacion)
                .HasMaxLength(500);
            modelBuilder.Entity<RutaMedicionDetalle>()
                .Property(x => x.FotoMedidorUrl)
                .HasMaxLength(500);
            modelBuilder.Entity<RutaMedicionDetalle>()
                .Property(x => x.LecturaInformada)
                .HasPrecision(18, 2);
            modelBuilder.Entity<RutaMedicionDetalle>()
                .Property(x => x.Latitud)
                .HasPrecision(10, 7);
            modelBuilder.Entity<RutaMedicionDetalle>()
                .Property(x => x.Longitud)
                .HasPrecision(10, 7);
            modelBuilder.Entity<RutaMedicionDetalle>()
                .HasRequired(x => x.RutaMedicion)
                .WithMany(x => x.RutaMedicionDetalle)
                .HasForeignKey(x => x.idRutaMedicion)
                .WillCascadeOnDelete(false);

            modelBuilder.Entity<Anuncio>().ToTable("Anuncio").HasKey(x => x.idAnuncio);
            modelBuilder.Entity<Anuncio>()
                .Property(x => x.Titulo)
                .IsRequired()
                .HasMaxLength(120);
            modelBuilder.Entity<Anuncio>()
                .Property(x => x.Contenido)
                .IsRequired()
                .HasMaxLength(1000);
            modelBuilder.Entity<Anuncio>()
                .HasRequired(x => x.PrioridadAnuncio)
                .WithMany(x => x.Anuncios)
                .HasForeignKey(x => x.idPrioridadAnuncio)
                .WillCascadeOnDelete(false);

            modelBuilder.Entity<PrioridadAnuncio>().ToTable("PrioridadAnuncio").HasKey(x => x.idPrioridadAnuncio);
            modelBuilder.Entity<PrioridadAnuncio>()
                .Property(x => x.Nombre)
                .IsRequired()
                .HasMaxLength(20);
        }
    }
}
