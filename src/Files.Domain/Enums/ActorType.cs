namespace Files.Domain.Enums;

/// <summary>Kind of actor recorded on an audit or outbox event.</summary>
public enum ActorType
{
    User,
    Integration,
    Admin,
    System,
}
