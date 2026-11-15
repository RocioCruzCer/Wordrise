using Npgsql;

namespace Wordrise.Web.Infrastructure;

/// <summary>
/// Uses ConnectionStrings:Default when present; otherwise converts Render's DATABASE_URL
/// (postgres://user:pass@host:port/db) into an Npgsql connection string.
/// </summary>
public static class ConnectionStringResolver
{
    public static string Resolve(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var url = configuration["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException(
                "No database configured. Set ConnectionStrings__Default or DATABASE_URL.");
        }

        return FromUrl(url);
    }

    internal static string FromUrl(string url)
    {
        var uri = new Uri(url);
        if (uri.Scheme is not ("postgres" or "postgresql"))
        {
            throw new InvalidOperationException("DATABASE_URL must use the postgres:// scheme.");
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            // Render internal URLs work without TLS; external URLs require it. Prefer covers both.
            SslMode = SslMode.Prefer,
            MaxPoolSize = 20,
            Timeout = 15,
            CommandTimeout = 30
        };

        return builder.ConnectionString;
    }
}
