namespace BookFlow.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

public class NotFoundException : DomainException
{
    public string EntityName { get; }
    public object Key { get; }
    public NotFoundException(string entityName, object key) : base($"{entityName} '{key}' was not found.")
    {
        EntityName = entityName; Key = key;
    }
}

public class ConflictException : DomainException
{
    public string Code { get; }
    public ConflictException(string code, string message) : base(message) { Code = code; }
}

public class ForbiddenException : DomainException
{
    public ForbiddenException(string message = "You are not allowed to perform this action.") : base(message) { }
}

public class InsufficientStockException : ConflictException
{
    public IReadOnlyList<Guid> BookIds { get; }
    public InsufficientStockException(IReadOnlyList<Guid> bookIds)
        : base("insufficient-stock", "One or more books do not have enough stock on hand.")
    {
        BookIds = bookIds;
    }
}

public class BusinessRuleException : DomainException
{
    public string Code { get; }
    public BusinessRuleException(string code, string message) : base(message) { Code = code; }
}
