using System.Globalization;
using System.Text.RegularExpressions;
using BD_RGR.Api.Data;
using Npgsql;
using NpgsqlTypes;

namespace BD_RGR.Api.Reports;

internal sealed partial class ManualSelect(string role, List<ManualSelect.Token> tokens)
{
    private static readonly HashSet<string> Functions = ["count", "sum", "avg", "min", "max", "lower", "upper"];
    internal static readonly string[] Sources =
        ["route", "point", "trip", "cargo_transportation", "vehicle_mileage_log", "garage", "brand", "model",
         "passenger_vehicle", "truck", "auxiliary_vehicle", "vehicle"];

    private readonly List<NpgsqlParameter> _parameters = [];
    private int _index;
    private int _joins;
    private Token Current => tokens[_index];

    public static ReportQuery Parse(string? input, string role)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 8192)
            throw Invalid();

        var tokens = new List<Token>();
        var position = 0;
        while (position < input.Length)
        {
            var match = Lex().Match(input, position);
            if (!match.Success || match.Index != position || tokens.Count >= 384)
                throw Invalid();

            position += match.Length;
            if (match.Groups["space"].Success)
                continue;
            var kind = match.Groups["word"].Success ? "word" : match.Groups["text"].Success ? "text"
                : match.Groups["number"].Success ? "number" : "symbol";
            tokens.Add(new(kind, kind == "word" ? match.Value.ToLowerInvariant() : match.Value));
        }
        tokens.Add(new("end", ""));
        return new ManualSelect(role, tokens).Statement();
    }

    private ReportQuery Statement()
    {
        Expect("select");
        var distinct = Take("distinct") ? "DISTINCT " : "";
        var projection = List(() =>
        {
            var value = Expression();
            return Take("as") ? $"{value} AS {Identifier()}" : value;
        }, 24);

        var sql = $"SELECT {distinct}{projection}";
        if (Take("from"))
        {
            sql += $" FROM {Source()}";
            while (At("join") || At("inner") || At("left"))
            {
                if (++_joins > 6)
                    throw Invalid();
                var join = Take("left") ? "LEFT " : Take("inner") ? "INNER " : "";
                Expect("join");
                var source = Source();
                Expect("on");
                sql += $" {join}JOIN {source} ON {Predicate()}";
            }
        }
        if (Take("where"))
            sql += $" WHERE {Predicate()}";
        if (Take("group"))
        {
            Expect("by");
            sql += $" GROUP BY {List(() => Expression(false), 12)}";
        }
        if (Take("order"))
        {
            Expect("by");
            sql += $" ORDER BY {List(() =>
            {
                var value = Expression(false);
                return value + (Take("desc") ? " DESC" : Take("asc") ? " ASC" : "");
            }, 12)}";
        }

        var limit = SelectQueries.MaxRows + 1;
        if (Take("limit"))
        {
            if (Current.Kind != "number" || !int.TryParse(Current.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var requested))
                throw Invalid();
            _index++;
            limit = Math.Min(limit, requested);
        }
        Take(";");
        if (Current.Kind != "end")
            throw Invalid();
        return new($"{sql} LIMIT {limit.ToString(CultureInfo.InvariantCulture)}", _parameters);
    }

    private string Source()
    {
        var name = Word();
        if (Take("."))
        {
            if (name != "public")
                throw new DataFailure("ReportSourceDenied", 403);
            name = Word();
        }
        if (!Sources.Contains(name) || !ResourceCatalog.Can(role, name, 'R'))
            throw new DataFailure("ReportSourceDenied", 403);
        return $"public.{Quote(name)}" + (Take("as") ? $" AS {Identifier()}" : "");
    }

    private string Predicate()
    {
        var sql = Comparison();
        while (At("and") || At("or"))
        {
            var op = Current.Value;
            _index++;
            sql += $" {op} {Comparison()}";
        }
        return sql;
    }

    private string Comparison()
    {
        var left = Expression(false);
        if (Take("is"))
        {
            var negated = Take("not");
            Expect("null");
            return left + (negated ? " IS NOT NULL" : " IS NULL");
        }
        var op = Current;
        if (op.Kind is not ("symbol" or "word") ||
            op.Value is not ("=" or "<>" or "!=" or "<" or "<=" or ">" or ">=" or "like" or "ilike"))
            throw Invalid();
        _index++;
        return $"{left} {op.Value} {Expression(false)}";
    }

    private string Expression(bool allowFunction = true)
    {
        if (Current.Kind == "text")
        {
            var text = Current.Value;
            _index++;
            return Parameter(NpgsqlDbType.Unknown, text[1..^1].Replace("''", "'"));
        }
        if (Current.Kind == "number")
        {
            var number = Current.Value;
            _index++;
            if (int.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
                return Parameter(NpgsqlDbType.Integer, integer);
            if (decimal.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var decimalValue))
                return Parameter(NpgsqlDbType.Numeric, decimalValue);
            throw Invalid();
        }
        if (Take("null"))
            return "NULL";
        if (Take("true"))
            return "TRUE";
        if (Take("false"))
            return "FALSE";
        if (allowFunction && Take("*"))
            return "*";

        var name = Word();
        if (Take("("))
        {
            if (!allowFunction || !Functions.Contains(name))
                throw new DataFailure("ReportFunctionDenied", 400);
            var argument = name == "count" && Take("*") ? "*" : Expression(false);
            Expect(")");
            return $"pg_catalog.{Quote(name)}({argument})";
        }
        return Quote(name) + (Take(".") ? "." + Identifier() : "");
    }

    private string Parameter(NpgsqlDbType type, object value)
    {
        var name = $"p{_parameters.Count}";
        _parameters.Add(new NpgsqlParameter(name, type) { Value = value });
        return $"@{name}";
    }

    private string List(Func<string> read, int maximum)
    {
        var terms = new List<string> { read() };
        while (Take(","))
        {
            if (terms.Count >= maximum)
                throw Invalid();
            terms.Add(read());
        }
        return string.Join(", ", terms);
    }

    private string Identifier() => Quote(Word());

    private string Word()
    {
        if (Current.Kind != "word" || Current.Value.Length > 63)
            throw Invalid();
        return tokens[_index++].Value;
    }

    private static string Quote(string word) => $"\"{word}\"";
    private bool At(string value) => Current.Kind is "symbol" or "word" && Current.Value == value;
    private bool Take(string value)
    {
        if (!At(value))
            return false;
        _index++;
        return true;
    }

    private void Expect(string value)
    {
        if (!Take(value))
            throw Invalid();
    }

    private static DataFailure Invalid() => new("ReportInvalidSql");

    [GeneratedRegex("""\G(?:(?<space>[ \t\r\n]+)|(?<word>[a-zA-Z_][a-zA-Z_0-9]*)|(?<text>'(?:''|[^'\x00])*')|(?<number>-?[0-9]+(?:\.[0-9]+)?)|[(),.*;]|<=|>=|<>|!=|[=<>])""", RegexOptions.CultureInvariant)]
    private static partial Regex Lex();

    internal sealed record Token(string Kind, string Value);
}

internal sealed record ReportQuery(string Sql, List<NpgsqlParameter> Parameters);
