using DocuArchive.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DocuArchive.Infrastructure.Data;

/// <summary>Design-time factory for <c>dotnet ef</c> migrations (SQLite default dev provider).</summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=docuarchive.db")
            .Options;
        return new AppDbContext(options);
    }
}
