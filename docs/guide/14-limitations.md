# Limitations

- Registrations are validated from the `IServiceCollection`; handlers registered only in a third-party container
  (for example Autofac's `ContainerBuilder`) are reported as missing.
- Handler wrappers are cached per message type for the lifetime of the process, which keeps types of collectible
  `AssemblyLoadContext`s alive.
- The outbox of `Pheme.EntityFrameworkCore` is supported on PostgreSQL (Npgsql) only, the database its tests run on.
  The database is read from the provider of the context; another one fails the first write to the outbox and the
  first read of it with an error naming it. Its queries are plain EF Core LINQ, so supporting another database means
  running the outbox tests on it, not a new API. Storage other than EF Core can implement `IOutboxStore`.

---

Previous: [Configuration](13-configuration.md) · [Contents](../README.md)
