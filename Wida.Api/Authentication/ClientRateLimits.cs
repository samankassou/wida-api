using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace Wida.Api.Authentication;

public static class ClientRateLimits
{
    public static PartitionedRateLimiter<HttpContext> Create() =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (context.User.IsInRole("Admin")) return RateLimitPartition.GetNoLimiter("admin");
            var userId = context.User.Identity?.IsAuthenticated == true
                ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
            var identity = userId is not null ? "user:" + userId
                : "ip:" + Normalize(context.Connection.RemoteIpAddress)?.ToString();
            var read = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
            return RateLimitPartition.GetFixedWindowLimiter(identity + (read ? ":read" : ":write"), _ => new()
            {
                PermitLimit = read ? 120 : 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            });
        });

    internal static IPAddress? Normalize(IPAddress? address) =>
        address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address;
}

// Next.js forwards exactly one ingress-verified IP. Never trust headers from other peers.
public sealed class TrustedClientIp
{
    public const string Header = "X-Wida-Client-IP";
    public const string SecretHeader = "X-Wida-Proxy-Secret";
    private readonly byte[]? secretHash;
    private readonly HashSet<IPAddress> proxies;

    public TrustedClientIp(IConfiguration configuration, IWebHostEnvironment environment)
    {
        proxies = (configuration.GetSection("RateLimiting:TrustedProxies").Get<string[]>() ?? [])
            .Select(value => ClientRateLimits.Normalize(IPAddress.Parse(value))!).ToHashSet();
        var secret = configuration["Authentication:ProxySecret"];
        if (!string.IsNullOrEmpty(secret))
        {
            if (secret.Length < 32) throw new InvalidOperationException("Authentication:ProxySecret must contain at least 32 characters.");
            secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        }
        if (secretHash is null && proxies.Count == 0 && !environment.IsDevelopment())
            throw new InvalidOperationException("Configure RateLimiting:TrustedProxies with the Next.js proxy connection IPs.");
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (secretHash is not null || proxies.Count != 0)
        {
            var peer = ClientRateLimits.Normalize(context.Connection.RemoteIpAddress);
            var value = context.Request.Headers[Header].ToString();
            var authenticatedProxy = secretHash is not null
                ? CryptographicOperations.FixedTimeEquals(secretHash,
                    SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers[SecretHeader].ToString())))
                : peer is not null && proxies.Contains(peer);
            context.Request.Headers.Remove(SecretHeader);
            if (!authenticatedProxy)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            if (value.Contains(',') || !IPAddress.TryParse(value, out var client))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            context.Connection.RemoteIpAddress = ClientRateLimits.Normalize(client);
        }
        // Development without a proxy list uses the connection IP and ignores all IP headers.
        await next(context);
    }
}
