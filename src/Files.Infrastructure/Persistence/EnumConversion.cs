using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Files.Infrastructure.Persistence;

/// <summary>
/// Snake-case conversions for PostgreSQL <c>text + CHECK</c> enumerations. C# enum members are
/// PascalCase; the database stores snake_case (e.g. <c>PendingUpload</c> ⇔ <c>pending_upload</c>).
/// </summary>
internal static class EnumConversion
{
    /// <summary>Builds a converter that stores a PascalCase enum as snake_case text.</summary>
    public static ValueConverter<TEnum, string> SnakeCase<TEnum>()
        where TEnum : struct, Enum =>
        new(
            value => value.ToString().ToSnakeCase(),
            value => Enum.Parse<TEnum>(value.ToPascalCase(), ignoreCase: false));

    public static string ToSnakeCase(this string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (i > 0 && char.IsUpper(current))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }

    public static string ToPascalCase(this string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var parts = value.Split('_');
        return string.Concat(parts.Select(Capitalize));
    }

    private static string Capitalize(string part) =>
        part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..];
}
