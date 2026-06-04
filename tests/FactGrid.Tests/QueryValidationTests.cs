using FactGrid.AspNet.Services;

namespace FactGrid.Tests;

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

    [Test]
    public void ValidateScoped_AllowedTable_Passes()
    {
        var (isValid, error) = _service.ValidateScoped("SELECT * FROM ResourceHours", "ResourceHours");
        Assert.That(isValid, Is.True);
        Assert.That(error, Is.Null);
    }

    [Test]
    public void ValidateScoped_OtherTable_Rejected()
    {
        var (isValid, error) = _service.ValidateScoped("SELECT * FROM OtherTable", "ResourceHours");
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("OtherTable"));
        Assert.That(error, Does.Contain("ResourceHours"));
    }

    [Test]
    public void ValidateScoped_JoinToOtherTable_Rejected()
    {
        var (isValid, error) = _service.ValidateScoped(
            "SELECT w.* FROM ResourceHours w JOIN OtherTable o ON w.Id = o.Id", "ResourceHours");
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("OtherTable"));
    }

    [Test]
    public void ValidateScoped_SubqueryToOtherTable_Rejected()
    {
        var (isValid, error) = _service.ValidateScoped(
            "SELECT * FROM ResourceHours WHERE Id IN (SELECT Id FROM OtherTable)", "ResourceHours");
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("OtherTable"));
    }

    [Test]
    public void ValidateScoped_CteReferencingAllowed_Passes()
    {
        var (isValid, error) = _service.ValidateScoped(
            "WITH cte AS (SELECT * FROM ResourceHours) SELECT * FROM cte", "ResourceHours");
        Assert.That(isValid, Is.True);
    }

    [Test]
    public void ValidateScoped_CteReferencingOther_Rejected()
    {
        var (isValid, error) = _service.ValidateScoped(
            "WITH cte AS (SELECT * FROM OtherTable) SELECT * FROM cte", "ResourceHours");
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("OtherTable"));
    }

    [Test]
    public void ValidateScoped_SelfJoin_Passes()
    {
        var (isValid, error) = _service.ValidateScoped(
            "SELECT a.* FROM ResourceHours a JOIN ResourceHours b ON a.Id = b.Id", "ResourceHours");
        Assert.That(isValid, Is.True);
    }

    [Test]
    public void ValidateScoped_DerivedTableWithAllowedSubquery_Passes()
    {
        var (isValid, error) = _service.ValidateScoped(
            "SELECT * FROM (SELECT * FROM ResourceHours) AS t", "ResourceHours");
        Assert.That(isValid, Is.True);
    }

    [Test]
    public void ValidateScoped_DerivedTableWithOther_Rejected()
    {
        var (isValid, error) = _service.ValidateScoped(
            "SELECT * FROM (SELECT * FROM OtherTable) AS t", "ResourceHours");
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("OtherTable"));
    }

    [Test]
    public void ValidateScoped_AliasedTable_Passes()
    {
        var (isValid, error) = _service.ValidateScoped(
            "SELECT rh.ResourceName FROM ResourceHours rh", "ResourceHours");
        Assert.That(isValid, Is.True);
    }

    [Test]
    public void ValidateScoped_ExistsSubqueryOther_Rejected()
    {
        var (isValid, error) = _service.ValidateScoped(
            "SELECT * FROM ResourceHours WHERE EXISTS (SELECT 1 FROM OtherTable)", "ResourceHours");
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("OtherTable"));
    }

    [Test]
    public void ValidateScoped_UnionOtherTable_Rejected()
    {
        var (isValid, error) = _service.ValidateScoped(
            "SELECT * FROM ResourceHours UNION ALL SELECT * FROM OtherTable", "ResourceHours");
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("OtherTable"));
    }

    [Test]
    public void ValidateScoped_NoFromClause_Passes()
    {
        var (isValid, error) = _service.ValidateScoped("SELECT 1", "ResourceHours");
        Assert.That(isValid, Is.True);
    }

    [TestCase("SELECT * FROM ResourceHours ORDER BY (SELECT Id FROM OtherTable)")]
    [TestCase("SELECT * FROM ResourceHours LIMIT (SELECT Id FROM OtherTable)")]
    [TestCase("SELECT * FROM ResourceHours OFFSET (SELECT Id FROM OtherTable)")]
    [TestCase("SELECT * FROM ResourceHours LIMIT 1 BY (SELECT Id FROM OtherTable)")]
    [TestCase("SELECT * FROM ResourceHours ORDER BY Id WITH FILL FROM (SELECT Id FROM OtherTable)")]
    [TestCase("SELECT * FROM ResourceHours ORDER BY Id WITH FILL INTERPOLATE (Id AS (SELECT Id FROM OtherTable))")]
    [TestCase("(SELECT * FROM ResourceHours) ORDER BY (SELECT Id FROM OtherTable)")]
    public void ValidateScoped_QueryLevelClauseWithOtherTableSubquery_Rejected(string query)
    {
        var (isValid, error) = _service.ValidateScoped(query, "ResourceHours");
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("OtherTable"));
    }

    [TestCase("SELECT * FROM ResourceHours ORDER BY (SELECT Id FROM ResourceHours)")]
    [TestCase("SELECT * FROM ResourceHours LIMIT 10")]
    [TestCase("SELECT * FROM ResourceHours OFFSET 10")]
    [TestCase("SELECT * FROM ResourceHours FETCH FIRST 10 ROWS ONLY")]
    [TestCase("SELECT * FROM ResourceHours LIMIT 1 BY Id")]
    [TestCase("SELECT * FROM ResourceHours ORDER BY Id WITH FILL FROM 1 TO 10 STEP 1")]
    [TestCase("SELECT * FROM ResourceHours ORDER BY Id WITH FILL INTERPOLATE (Id AS Id + 1)")]
    public void ValidateScoped_QueryLevelClauseWithAllowedTableOrLiteral_Passes(string query)
    {
        var (isValid, error) = _service.ValidateScoped(query, "ResourceHours");
        Assert.That(isValid, Is.True, error);
    }

    [Test]
    public void ValidateScoped_FetchWithSubqueryQuantity_ParserRejectsSyntax()
    {
        var (isValid, error) = _service.ValidateScoped(
            "SELECT * FROM ResourceHours FETCH FIRST (SELECT Id FROM OtherTable) ROWS ONLY",
            "ResourceHours");

        Assert.That(isValid, Is.False);
        Assert.That(error, Does.StartWith("Parse error:"));
    }

    [Test]
    public void ValidateTables_SingleAllowed_Passes()
    {
        var (isValid, error) = _service.ValidateTables("SELECT * FROM ResourceHours", ["ResourceHours"]);
        Assert.That(isValid, Is.True);
        Assert.That(error, Is.Null);
    }

    [Test]
    public void ValidateTables_MultipleAllowed_Passes()
    {
        var (isValid, error) = _service.ValidateTables(
            "SELECT w.* FROM ResourceHours w JOIN Expenses e ON w.Id = e.Id",
            ["ResourceHours", "Expenses"]);
        Assert.That(isValid, Is.True);
    }

    [Test]
    public void ValidateTables_NonAllowed_Rejected()
    {
        var (isValid, error) = _service.ValidateTables("SELECT * FROM AspNetUsers", ["ResourceHours", "Expenses"]);
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("AspNetUsers"));
        Assert.That(error, Does.Contain("ResourceHours"));
        Assert.That(error, Does.Contain("Expenses"));
    }

    [Test]
    public void ValidateTables_JoinSystemTable_Rejected()
    {
        var (isValid, error) = _service.ValidateTables(
            "SELECT w.* FROM ResourceHours w JOIN sqlite_master m ON w.Id = m.rowid",
            ["ResourceHours", "Expenses"]);
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("sqlite_master"));
    }

    [Test]
    public void ValidateTables_UnionNonAllowed_Rejected()
    {
        var (isValid, error) = _service.ValidateTables(
            "SELECT * FROM ResourceHours UNION ALL SELECT * FROM AspNetUsers",
            ["ResourceHours", "Expenses"]);
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("AspNetUsers"));
    }

    [Test]
    public void ValidateTables_SubqueryNonAllowed_Rejected()
    {
        var (isValid, error) = _service.ValidateTables(
            "SELECT * FROM ResourceHours WHERE Id IN (SELECT Id FROM AspNetUsers)",
            ["ResourceHours", "Expenses"]);
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("AspNetUsers"));
    }

    [Test]
    public void ValidateTables_CteWithNonAllowed_Rejected()
    {
        var (isValid, error) = _service.ValidateTables(
            "WITH cte AS (SELECT * FROM AspNetUsers) SELECT * FROM cte",
            ["ResourceHours", "Expenses"]);
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("AspNetUsers"));
    }

    [Test]
    public void ValidateTables_NoFromClause_Passes()
    {
        var (isValid, error) = _service.ValidateTables("SELECT 1", ["ResourceHours"]);
        Assert.That(isValid, Is.True);
    }

    [Test]
    public void ValidateTables_EmptyAllowed_RejectsAny()
    {
        var (isValid, error) = _service.ValidateTables("SELECT * FROM ResourceHours", []);
        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("ResourceHours"));
    }
}
