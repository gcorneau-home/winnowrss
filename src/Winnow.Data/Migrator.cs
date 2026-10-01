using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Winnow.Data;

/// <summary>Runs the embedded Migrations/NNNN_name.sql scripts newer than PRAGMA user_version.</summary>
internal static partial class Migrator
{
    public static void Migrate(SqliteConnection connection)
    {
        var current = connection.ExecuteScalar<long>("PRAGMA user_version;");

        foreach (var (version, sql) in LoadScripts().Where(s => s.Version > current).OrderBy(s => s.Version))
        {
            using var transaction = connection.BeginTransaction();
            connection.Execute(sql, transaction: transaction);
            connection.Execute($"PRAGMA user_version = {version};", transaction: transaction);
            transaction.Commit();
        }
    }

    internal static IEnumerable<(int Version, string Sql)> LoadScripts()
    {
        var assembly = typeof(Migrator).Assembly;
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var match = ScriptName().Match(name);
            if (!match.Success)
                continue;

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return (int.Parse(match.Groups[1].Value), reader.ReadToEnd());
        }
    }

    [GeneratedRegex(@"^Migrations\.(\d{4})_[\w-]+\.sql$")]
    private static partial Regex ScriptName();
}
