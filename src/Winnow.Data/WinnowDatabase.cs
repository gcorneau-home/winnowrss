using System.Data;
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Winnow.Data;

/// <summary>Opens connections to the WinnowRSS SQLite database and keeps its schema up to date.</summary>
public sealed class WinnowDatabase
{
    private readonly string _connectionString;

    static WinnowDatabase()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset));
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset?));
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
    }

    public WinnowDatabase(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString) { ForeignKeys = true };
        _connectionString = builder.ToString();
    }

    public static WinnowDatabase ForFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return new WinnowDatabase(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    /// <summary>Applies pending migrations. Call once at startup.</summary>
    public void Migrate()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("PRAGMA journal_mode = WAL;");
        Migrator.Migrate(connection);
    }

    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        public override DateTimeOffset Parse(object value) =>
            DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }
}
