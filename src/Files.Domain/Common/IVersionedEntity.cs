namespace Files.Domain.Common;

/// <summary>
/// Marker for aggregates whose table carries an integer <c>version</c> column used as an EF
/// optimistic-concurrency token. The <see cref="Version"/> property is bumped by the unit of work
/// before each update.
/// </summary>
public interface IVersionedEntity
{
    int Version { get; set; }
}
