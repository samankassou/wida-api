# Wida API

.NET 10 API using Entity Framework Core and PostgreSQL.

## Local setup

Install the .NET 10 SDK and use a locally installed or remote PostgreSQL server.

1. Make sure PostgreSQL is running and create a database named `wida` with a login that can manage its tables, or use an existing database and login.
2. Configure the API connection using .NET User Secrets (replace the host, database, username, and password with your PostgreSQL settings):

   ```sh
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=wida;Username=wida;Password=YOUR_LOCAL_PASSWORD"
   ```

   User Secrets are loaded in the Development environment. In deployed environments, supply `ConnectionStrings__DefaultConnection` through your secret manager or environment.

3. Run the API:

   ```sh
   dotnet restore
   dotnet run
   ```

## Models and migrations

Inject `AppDbContext` into endpoints or services to access PostgreSQL. Add domain models and their `DbSet<T>` properties in `Data/AppDbContext.cs`, then create and apply a migration:

```sh
dotnet tool restore
dotnet ef migrations add InitialCreate
dotnet ef database update
```

There are no domain tables yet, so no initial migration is included. The weather forecast endpoint still generates sample data. Schema changes are applied explicitly with migrations, not automatically on application startup.
