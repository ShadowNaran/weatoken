using Apiclase19.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Apiclase19.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Persona> Personas => Set<Persona>();
}
