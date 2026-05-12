using System.Data.Entity;


namespace CooperativaAPI.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext() : base("DefaultConnection") { }

        public DbSet<Persona> Personas { get; set; }
        public DbSet<Socio> Socios { get; set; }
        public DbSet<UsuarioSistema> Usuarios { get; set; }
        public DbSet<Rol> Roles { get; set; }
    }
}
