namespace BookFlow.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; set; } = GuidGenerator.NewSequential();
}

public interface ITenantEntity
{
    Guid TenantId { get; set; }
}

public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    Guid? UpdatedBy { get; set; }
}

public interface IHasRowVersion
{
    byte[] RowVersion { get; set; }
}

public abstract class TenantEntity : Entity, ITenantEntity, IAuditableEntity
{
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Marker: rows of this entity are never updated or deleted by application code.</summary>
public interface IAppendOnly { }

public static class GuidGenerator
{
    /// <summary>Time-ordered GUID (v7 layout) so clustered PK inserts stay sequential in SQL Server.</summary>
    public static Guid NewSequential()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        var ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // SQL Server orders uniqueidentifier by the last 6 bytes first; put the timestamp there.
        bytes[10] = (byte)(ms >> 40);
        bytes[11] = (byte)(ms >> 32);
        bytes[12] = (byte)(ms >> 24);
        bytes[13] = (byte)(ms >> 16);
        bytes[14] = (byte)(ms >> 8);
        bytes[15] = (byte)ms;
        return new Guid(bytes);
    }
}
