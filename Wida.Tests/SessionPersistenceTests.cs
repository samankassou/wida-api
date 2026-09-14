using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Wida.Api.Authentication;
using Wida.Dal.Persistence;

namespace Wida.Tests;

public sealed class SessionPersistenceTests
{
    [Fact]
    public void Database_keys_are_encrypted_and_a_new_process_provider_can_read_existing_sessions()
    {
        var path = Path.Combine(Path.GetTempPath(), "wida-keys-" + Guid.NewGuid() + ".db");
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Wida tests", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:PublicOrigin"] = "https://wida.test",
            ["Authentication:DataProtectionProvider"] = "Database",
            ["Authentication:DataProtectionCertificateBase64"] = Convert.ToBase64String(cert.Export(X509ContentType.Pfx, "test-password")),
            ["Authentication:DataProtectionCertificatePassword"] = "test-password"
        }).Build();
        ServiceProvider Provider()
        {
            var services = new ServiceCollection().AddLogging();
            services.AddDbContext<WidaDbContext>(o => o.UseSqlite("Data Source=" + path));
            services.AddWidaAuthentication(config, new Environment());
            return services.BuildServiceProvider();
        }
        try
        {
            string payload;
            using (var first = Provider())
            {
                using var scope = first.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WidaDbContext>();
                db.Database.EnsureCreated();
                payload = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("session-test").Protect("user-session");
                var xml = db.DataProtectionKeys.Single().Xml!;
                Assert.Contains("encryptedSecret", xml);
                Assert.DoesNotContain("<masterKey", xml);
            }
            using var second = Provider();
            Assert.Equal("user-session", second.GetRequiredService<IDataProtectionProvider>().CreateProtector("session-test").Unprotect(payload));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    private sealed class Environment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public string WebRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
