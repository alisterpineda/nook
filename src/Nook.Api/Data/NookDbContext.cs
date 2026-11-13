using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Nook.Api.Data;

/// <summary>Nook's database: Identity's user tables, without role tables, and Nook's own entities.</summary>
public class NookDbContext(DbContextOptions<NookDbContext> options)
    : IdentityUserContext<NookUser, Guid>(options)
{
    public DbSet<Library> Libraries => Set<Library>();

    public DbSet<Series> Series => Set<Series>();

    public DbSet<Work> Works => Set<Work>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<WorkContributor> WorkContributors => Set<WorkContributor>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<WorkExternalId> WorkExternalIds => Set<WorkExternalId>();

    public DbSet<Item> Items => Set<Item>();

    public DbSet<Part> Parts => Set<Part>();

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<JobStep> JobSteps => Set<JobStep>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every enum is stored as its member name, so new members need no migration.
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity names its tables explicitly, which the snake_case naming convention leaves alone.
        builder.Entity<NookUser>().ToTable("asp_net_users");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("asp_net_user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("asp_net_user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("asp_net_user_tokens");

        // The single library every install starts with. Seed values are fixed so the migration never changes.
        var seededAt = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);
        builder.Entity<Library>().HasData(new Library
        {
            Id = Library.DefaultId,
            Name = "Library",
            FolderName = "Library",
            CreatedAt = seededAt,
            UpdatedAt = seededAt,
        });

        builder.Entity<Work>(work =>
        {
            // A library or series that still has works cannot be deleted.
            work.HasOne<Library>().WithMany().HasForeignKey(w => w.LibraryId).OnDelete(DeleteBehavior.Restrict);
            work.HasOne<Series>().WithMany().HasForeignKey(w => w.SeriesId).OnDelete(DeleteBehavior.Restrict);
            work.HasIndex(w => w.SearchVector).HasMethod("GIN");

            // Contributor links go with their work.
            work.HasMany(w => w.Contributors).WithOne().HasForeignKey(c => c.WorkId).OnDelete(DeleteBehavior.Cascade);

            // Tag links go with either their work or their tag.
            work.HasMany(w => w.Tags).WithMany().UsingEntity(
                "WorkTag",
                link => link.HasOne(typeof(Tag)).WithMany().HasForeignKey("TagId").OnDelete(DeleteBehavior.Cascade),
                link => link.HasOne(typeof(Work)).WithMany().HasForeignKey("WorkId").OnDelete(DeleteBehavior.Cascade),
                link =>
                {
                    link.ToTable("work_tags");
                    link.HasKey("WorkId", "TagId");
                });

            // External ids go with their work.
            work.HasMany(w => w.ExternalIds).WithOne().HasForeignKey(e => e.WorkId).OnDelete(DeleteBehavior.Cascade);

            // Items go with their work.
            work.HasMany(w => w.Items).WithOne().HasForeignKey(i => i.WorkId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Part>(part =>
        {
            // Parts go with their item.
            part.HasOne<Item>().WithMany().HasForeignKey(p => p.ItemId).OnDelete(DeleteBehavior.Cascade);

            // Lookups by content find every part that holds it; the same file can sit in more than one item.
            part.HasIndex(p => p.Sha256);
        });

        builder.Entity<Job>(job =>
        {
            // Payload and result are stored as given; only the job's type reads them.
            job.Property(j => j.Payload).HasColumnType("jsonb");
            job.Property(j => j.Result).HasColumnType("jsonb");

            // A job that still has children cannot be deleted.
            job.HasOne<Job>().WithMany().HasForeignKey(j => j.ParentJobId).OnDelete(DeleteBehavior.Restrict);

            // Workers look for the oldest job in a status.
            job.HasIndex(j => new { j.Status, j.CreatedAt });
        });

        builder.Entity<JobStep>(step =>
        {
            // Steps go with their job.
            step.HasOne<Job>().WithMany().HasForeignKey(s => s.JobId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkExternalId>(externalId =>
        {
            // A work holds each scheme and value once; lookups by scheme and value find the works that hold it.
            externalId.HasIndex(e => new { e.WorkId, e.Scheme, e.Value }).IsUnique();
            externalId.HasIndex(e => new { e.Scheme, e.Value });
        });

        // Ingest finds people, series and tags by name key. Rows can share a key, so the indexes are not unique.
        var named = builder.Model.GetEntityTypes()
            .Where(type => typeof(NamedEntity).IsAssignableFrom(type.ClrType))
            .ToList();
        foreach (var type in named)
        {
            builder.Entity(type.ClrType).HasIndex(nameof(NamedEntity.NameKey));
        }

        builder.Entity<WorkContributor>(contributor =>
        {
            // A person who still contributes to a work cannot be deleted.
            contributor.HasOne<Person>().WithMany().HasForeignKey(c => c.PersonId).OnDelete(DeleteBehavior.Restrict);
        });

        // Nook sets every id itself, in the entity's constructor. Telling EF so makes it insert a new entity
        // it finds on a loaded one, such as an item added to a work, instead of treating it as an existing row.
        builder.Entity<NookUser>().Property(u => u.Id).ValueGeneratedNever();
        foreach (var entityType in builder.Model.GetEntityTypes().Where(t => t.ClrType.IsAssignableTo(typeof(Entity))).ToList())
        {
            builder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
        }
    }
}
