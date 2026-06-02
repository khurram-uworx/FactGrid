using SqlParser;
using SqlParser.Ast;
using SqlParser.Dialects;

namespace EfMcp.AspNet.Services;

public class QueryValidationService
{
    private static readonly GenericDialect Dialect = new();
    private readonly SqlQueryParser _parser = new();

    public ValidationResult Validate(string query)
    {
        try
        {
            var statements = _parser.Parse(query.AsSpan(), Dialect);

            if (statements.Count != 1)
                return Fail("Only single-statement queries are supported");

            if (statements[0] is not Statement.Select)
                return Fail("Only SELECT statements are supported");

            return Pass();
        }
        catch (Exception ex)
        {
            return Fail($"Parse error: {ex.Message}");
        }
    }

    public record ValidationResult(bool IsValid, string? Error)
    {
        public void Deconstruct(out bool IsValid, out string? Error)
        {
            IsValid = this.IsValid;
            Error = this.Error;
        }
    }

    private static ValidationResult Pass() => new(true, null);
    private static ValidationResult Fail(string error) => new(false, error);
}
