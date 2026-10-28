namespace Files.Application.Storage;

/// <summary>
/// Pure staging-reserve arithmetic (ADR-0003 / FR-UP-014). The host must keep
/// <c>reserveBytes</c> free at all times; an additional allocation is accepted only when the
/// remaining free space after subtracting the reserve still covers it.
/// </summary>
public static class StagingCapacity
{
    /// <summary>
    /// Whether <paramref name="freeBytes"/> of free space can accommodate
    /// <paramref name="additionalBytes"/> while preserving the reserve.
    /// </summary>
    public static bool Allows(long freeBytes, long reserveBytes, long additionalBytes)
    {
        if (additionalBytes <= 0)
        {
            return true;
        }

        return freeBytes - reserveBytes >= additionalBytes;
    }

    /// <summary>The usable capacity after the reserve, floored at zero.</summary>
    public static long Usable(long freeBytes, long reserveBytes) =>
        Math.Max(0, freeBytes - reserveBytes);
}
