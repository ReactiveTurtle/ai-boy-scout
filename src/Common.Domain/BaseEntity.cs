namespace Common.Domain;

/// <summary>Base for domain entities with an internally generated, stable GUID identity.</summary>
public abstract class BaseEntity
{
    protected BaseEntity() => Id = Guid.NewGuid();

    protected BaseEntity(Guid id) => Id = id == Guid.Empty
        ? throw new ArgumentException("Entity identity is required.", nameof(id))
        : id;

    public Guid Id { get; private set; }
}
