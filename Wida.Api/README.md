# Wida API

.NET 10 API using Entity Framework Core and PostgreSQL.

## Local setup

Install the .NET 10 SDK and use a locally installed or remote PostgreSQL server.

Run the commands below from the `Wida.Api` directory.

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

Inject `WidaDbContext` into endpoints or services to access PostgreSQL. Domain models and configurations live in `Wida.Dal`, alongside `Persistence/WidaDbContext.cs`. Create and apply a migration with:

```sh
dotnet tool restore
dotnet ef database update --project ../Wida.Dal --startup-project .
```

The included `InitialCreate` migration creates the `Documents` table and EF migration history table. Schema changes are applied explicitly with migrations, not automatically on application startup. After future model changes, create a migration with `dotnet ef migrations add <Name> --project ../Wida.Dal --startup-project .`, then apply it with the update command above.
