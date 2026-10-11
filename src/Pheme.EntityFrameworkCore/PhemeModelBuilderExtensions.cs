using Pheme;

namespace Microsoft.EntityFrameworkCore
{
    /// <summary>
    /// Maps the tables of Pheme in the model of a <see cref="DbContext"/>.
    /// </summary>
    public static class PhemeModelBuilderExtensions
    {
        /// <summary>
        /// Maps the outbox, <see cref="OutboxMessage"/>, to the table <c>outbox_messages</c> in the default schema of the
        /// model. Call it in <c>OnModelCreating</c> of a context with <c>UseOutbox</c> in <c>AddPhemeDbContext</c>.
        /// </summary>
        /// <param name="modelBuilder">The model builder of the context.</param>
        /// <returns>The same model builder, for chaining.</returns>
        /// <remarks>
        /// The table belongs to Pheme: its key is <see cref="OutboxMessage.Id"/>, never generated, and it has an index
        /// on <see cref="OutboxMessage.ProcessedOnUtc"/>, <see cref="OutboxMessage.AttemptCount"/>,
        /// <see cref="OutboxMessage.OccurredOnUtc"/> and <see cref="OutboxMessage.NextAttemptOnUtc"/>, the columns of
        /// the due messages' query. The column names
        /// come from the model, so a naming convention of the application, such as snake case, applies to them; the
        /// table name is set explicitly. Create the table with the context's migrations.
        /// <para>
        /// The outbox is supported on PostgreSQL (Npgsql), the database its tests run on. On another database, read
        /// from the provider of the context, the first <c>Add</c> of a writer and the first use of the store throw an
        /// <see cref="System.InvalidOperationException"/> naming the provider.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="modelBuilder"/> is <see langword="null"/>.</exception>
        public static ModelBuilder AddPhemeOutbox(this ModelBuilder modelBuilder)
        {
            return AddPhemeOutbox(modelBuilder, "outbox_messages", schema: null);
        }

        /// <summary>
        /// Maps the outbox, <see cref="OutboxMessage"/>, to the given table. Call it in <c>OnModelCreating</c> of a
        /// context with <c>UseOutbox</c> in <c>AddPhemeDbContext</c>.
        /// </summary>
        /// <param name="modelBuilder">The model builder of the context.</param>
        /// <param name="table">The name of the table.</param>
        /// <param name="schema">The schema of the table; <see langword="null"/> for the default schema of the model.</param>
        /// <returns>The same model builder, for chaining.</returns>
        /// <remarks>Works like <see cref="AddPhemeOutbox(ModelBuilder)"/>.</remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="modelBuilder"/> or <paramref name="table"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException"><paramref name="table"/> or <paramref name="schema"/> is empty or white space.</exception>
        public static ModelBuilder AddPhemeOutbox(this ModelBuilder modelBuilder, string table, string? schema)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);
            ArgumentException.ThrowIfNullOrWhiteSpace(table);
            if (schema is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(schema);
            }

            modelBuilder.Entity<OutboxMessage>(message =>
            {
                message.ToTable(table, schema);
                message.HasKey(entity => entity.Id);
                message.Property(entity => entity.Id).ValueGeneratedNever();
                message.HasIndex(entity => new { entity.ProcessedOnUtc, entity.AttemptCount, entity.OccurredOnUtc, entity.NextAttemptOnUtc });
            });
            return modelBuilder;
        }
    }
}
