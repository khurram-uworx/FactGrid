using EfMcp.AspNet.Services;

namespace EfMcp.Tests;

public class QueryValidationTests
{
    private QueryValidationService _service = null!;

    [SetUp]
    public void Setup()
    {
        _service = new QueryValidationService();
    }

    [Test]
    public void Valid_Select_Simple_Passes()
    {
        var (isValid, error) = _service.Validate("SELECT * FROM ResourceHours");
        Assert.That(isValid, Is.True);
        Assert.That(error, Is.Null);
    }

    [Test]
    public void Valid_Select_WithWhere_Passes()
    {
        var (isValid, error) = _service.Validate("SELECT ResourceName, Hours FROM ResourceHours WHERE Hours > 5");
        Assert.That(isValid, Is.True);
    }

    [Test]
    public void Valid_Select_WithJoin_Passes()
    {
        var (isValid, error) = _service.Validate("SELECT w.ResourceName, w.Hours FROM ResourceHours w JOIN OtherTable o ON w.Id = o.Id");
        Assert.That(isValid, Is.True);
    }

    [Test]
    public void Valid_Select_WithSubquery_Passes()
    {
        var (isValid, error) = _service.Validate("SELECT * FROM ResourceHours WHERE Id IN (SELECT Id FROM OtherTable)");
        Assert.That(isValid, Is.True);
    }

    [Test]
    public void Valid_Select_WithCte_Passes()
    {
        var (isValid, error) = _service.Validate("WITH cte AS (SELECT * FROM ResourceHours) SELECT * FROM cte");
        Assert.That(isValid, Is.True);
    }

    [Test]
    public void Valid_Select_WithLimit_Passes()
    {
        var (isValid, error) = _service.Validate("SELECT * FROM ResourceHours LIMIT 10");
        Assert.That(isValid, Is.True);
    }

    [Test]
    public void Invalid_Insert_Rejected()
    {
        var (isValid, error) = _service.Validate("INSERT INTO ResourceHours (ResourceName) VALUES ('Test')");
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("SELECT"));
    }

    [Test]
    public void Invalid_Update_Rejected()
    {
        var (isValid, error) = _service.Validate("UPDATE ResourceHours SET Hours = 10 WHERE Id = 1");
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("SELECT"));
    }

    [Test]
    public void Invalid_Delete_Rejected()
    {
        var (isValid, error) = _service.Validate("DELETE FROM ResourceHours WHERE Id = 1");
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("SELECT"));
    }

    [Test]
    public void Invalid_DropTable_Rejected()
    {
        var (isValid, error) = _service.Validate("DROP TABLE ResourceHours");
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("SELECT"));
    }

    [Test]
    public void Invalid_AlterTable_Rejected()
    {
        var (isValid, error) = _service.Validate("ALTER TABLE ResourceHours ADD COLUMN Test INT");
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("SELECT"));
    }

    [Test]
    public void Invalid_CreateTable_Rejected()
    {
        var (isValid, error) = _service.Validate("CREATE TABLE Test (Id INT)");
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("SELECT"));
    }

    [Test]
    public void Invalid_MultiStatement_Rejected()
    {
        var (isValid, error) = _service.Validate("SELECT * FROM ResourceHours; SELECT * FROM OtherTable");
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("single-statement"));
    }

    [Test]
    public void Invalid_MalformedSql_ReturnsError()
    {
        var (isValid, error) = _service.Validate("SELECT FROM WHERE");
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("Parse error"));
    }
}
