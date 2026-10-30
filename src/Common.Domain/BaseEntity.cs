namespace Common.Domain;

/// <summary>Base for domain entities with an internally generated, stable GUID identity.</summary>
public abstract class BaseEntity
{
    protected BaseEntity() => Id = Guid.NewGuid();

    public Guid Id { get; private set; }
}
