namespace Files.Domain.Enums;

/// <summary>Whether a file expires at a fixed instant or lives indefinitely.</summary>
public enum RetentionMode
{
    Finite,
    Indefinite,
}
