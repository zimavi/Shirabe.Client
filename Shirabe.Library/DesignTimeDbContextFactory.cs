using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shirabe.Library;

/// <summary>Only used by `dotnet ef migrations ...`; the application opens the database through <see cref="LocalLibrary.Open"/>.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LibraryDbContext>
{
    public LibraryDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
