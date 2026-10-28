using System.Diagnostics.CodeAnalysis;

namespace Files.Application.Abstractions;

/// <summary>An error the API layer translates into an RFC 9457 problem response.</summary>
public sealed record ServiceError(int Status, string Code, string Title, string? Detail = null);

/// <summary>Success value or a <see cref="ServiceError"/>; callers branch on <see cref="IsSuccess"/>.</summary>
[SuppressMessage("Design", "CA1000", Justification = "Static factories on a lightweight result type keep call sites concise.")]
public sealed record ServiceResult<T>(T? Value, ServiceError? Error)
{
    public bool IsSuccess => Error is null;

    public static ServiceResult<T> Ok(T value) => new(value, null);

    public static ServiceResult<T> Fail(ServiceError error) => new(default, error);

    public static ServiceResult<T> Fail(int status, string code, string title, string? detail = null) =>
        new(default, new ServiceError(status, code, title, detail));
}
