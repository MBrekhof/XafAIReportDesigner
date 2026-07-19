#:package Microsoft.EntityFrameworkCore@8.0.18
#:package Npgsql.EntityFrameworkCore.PostgreSQL@8.0.11
#:property PublishAot=false

using Microsoft.EntityFrameworkCore;

var scratch = @"C:\Users\marti\AppData\Local\Temp\claude\C--projects-XafAIReportDesigner\77dafe7e-785d-4ecc-ae67-832a9096119f\scratchpad";
var cs = "Host=localhost;Port=5432;Database=xafaireportdesigner;Username=xaf;Password=xaf123";

var options = new DbContextOptionsBuilder<ReportDbContext>().UseNpgsql(cs).Options;
using var db = new ReportDbContext(options);
foreach (var row in db.Reports.AsNoTracking().ToList())
{
    Console.WriteLine($"{row.ID}  {row.DisplayName}  bytes={row.Content?.Length ?? 0}");
    if (args.Contains(row.DisplayName) && row.Content != null)
    {
        var path = Path.Combine(scratch, $"layout-{row.DisplayName}.xml");
        File.WriteAllBytes(path, row.Content);
        Console.WriteLine($"  -> {path}");
    }
}

public class ReportDbContext(DbContextOptions<ReportDbContext> options) : DbContext(options)
{
    public DbSet<ReportRow> Reports => Set<ReportRow>();
    protected override void OnModelCreating(ModelBuilder mb)
    {
        var e = mb.Entity<ReportRow>().ToTable("ReportDataV2");
        e.HasKey(r => r.ID);
        e.Property(r => r.ID).HasColumnName("ID");
        e.Property(r => r.DisplayName).HasColumnName("DisplayName");
        e.Property(r => r.Content).HasColumnName("Content");
    }
}

public class ReportRow
{
    public Guid ID { get; set; }
    public string? DisplayName { get; set; }
    public byte[]? Content { get; set; }
}
