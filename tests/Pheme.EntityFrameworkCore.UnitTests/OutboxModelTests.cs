using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Pheme.EntityFrameworkCore.UnitTests
{
    public class OutboxModelTests
    {
        [Fact]
        public void AddPhemeOutbox_maps_the_table_outbox_messages_in_the_default_schema()
        {
            using var db = new OrderingDbContext(Npgsql<OrderingDbContext>());

            IEntityType outbox = db.Model.FindEntityType(typeof(OutboxMessage))!;

            Assert.Equal(("outbox_messages", "ordering"), (outbox.GetTableName(), outbox.GetSchema()));
            IProperty id = Assert.Single(outbox.FindPrimaryKey()!.Properties);
            Assert.Equal(nameof(OutboxMessage.Id), id.Name);
            Assert.Equal(ValueGenerated.Never, id.ValueGenerated);
            IIndex index = Assert.Single(outbox.GetIndexes());
            Assert.Equal(
                [nameof(OutboxMessage.ProcessedOnUtc), nameof(OutboxMessage.AttemptCount), nameof(OutboxMessage.OccurredOnUtc), nameof(OutboxMessage.NextAttemptOnUtc)],
                index.Properties.Select(property => property.Name));
            Assert.Equal(
                [nameof(OutboxMessage.Content), nameof(OutboxMessage.Source), nameof(OutboxMessage.Type)],
                outbox.GetProperties().Where(property => property.ClrType == typeof(string) && !property.IsNullable)
                    .Select(property => property.Name)
                    .Order(StringComparer.Ordinal));
        }

        [Fact]
        public void AddPhemeOutbox_maps_the_given_table_and_schema()
        {
            using var db = new ShippingDbContext(Npgsql<ShippingDbContext>());

            IEntityType outbox = db.Model.FindEntityType(typeof(OutboxMessage))!;

            Assert.Equal(("messages", "shipping_outbox"), (outbox.GetTableName(), outbox.GetSchema()));
        }

        [Fact]
        public void A_naming_convention_of_the_application_applies_to_the_columns_but_not_to_the_table()
        {
            using var db = new LowerCaseDbContext(Npgsql<LowerCaseDbContext>());

            IEntityType outbox = db.Model.FindEntityType(typeof(OutboxMessage))!;

            var table = StoreObjectIdentifier.Table("outbox_messages", schema: null);
            Assert.Equal("outbox_messages", outbox.GetTableName());
            Assert.Equal("occurredonutc", outbox.FindProperty(nameof(OutboxMessage.OccurredOnUtc))!.GetColumnName(table));
        }

        [Fact]
        public void Invalid_arguments_are_rejected()
        {
            var modelBuilder = new ModelBuilder();

            Assert.Throws<ArgumentNullException>("modelBuilder", () => ((ModelBuilder)null!).AddPhemeOutbox());
            Assert.Throws<ArgumentNullException>("modelBuilder", () => ((ModelBuilder)null!).AddPhemeOutbox("messages", null));
            Assert.Throws<ArgumentNullException>("table", () => modelBuilder.AddPhemeOutbox(null!, null));
            Assert.Throws<ArgumentException>("table", () => modelBuilder.AddPhemeOutbox(" ", null));
            Assert.Throws<ArgumentException>("schema", () => modelBuilder.AddPhemeOutbox("messages", ""));
        }

        private static DbContextOptions<TContext> Npgsql<TContext>()
            where TContext : DbContext
        {
            // The model needs no connection.
            return new DbContextOptionsBuilder<TContext>().UseNpgsql("Host=localhost").Options;
        }

        private sealed class LowerCaseDbContext(DbContextOptions<LowerCaseDbContext> options) : DbContext(options)
        {
            protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
            {
                configurationBuilder.Conventions.Add(_ => new LowerCaseColumnsConvention());
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.AddPhemeOutbox();
            }
        }

        /// <summary>
        /// A naming convention of an application, such as snake case from EFCore.NamingConventions.
        /// </summary>
        private sealed class LowerCaseColumnsConvention : IModelFinalizingConvention
        {
            public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
            {
                foreach (IConventionProperty property in modelBuilder.Metadata.GetEntityTypes().SelectMany(entityType => entityType.GetProperties()))
                {
                    property.Builder.HasColumnName(property.GetColumnName().ToLowerInvariant());
                }
            }
        }
    }
}
