namespace BookFlow.Application.Common.Models;

public class AppValidationException : Exception
{
    public IDictionary<string, string[]> Errors { get; }
    public AppValidationException(IDictionary<string, string[]> errors) : base("One or more validation failures have occurred.")
    {
        Errors = errors;
    }
    public AppValidationException(string property, string message)
        : this(new Dictionary<string, string[]> { [property] = new[] { message } }) { }
}
