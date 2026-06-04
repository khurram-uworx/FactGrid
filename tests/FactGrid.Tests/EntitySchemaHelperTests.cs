using FactGrid.AspNet.Models;
using FactGrid.AspNet.Services;

namespace FactGrid.Tests;

public class EntitySchemaHelperTests
{
    [Test]
    public void GetColumns_UsesExplicitColumnTypeName()
    {
        var columns = EntitySchemaHelper.GetColumns(typeof(Worklog))
            .ToDictionary(column => column.Name);

        Assert.That(columns["Hours"].Type, Is.EqualTo("decimal(10,2)"));
        Assert.That(columns["WorkDate"].Type, Is.EqualTo("date"));
    }

    [Test]
    public void GetColumns_FallsBackToClrDisplayType()
    {
        var columns = EntitySchemaHelper.GetColumns(typeof(Worklog))
            .ToDictionary(column => column.Name);

        Assert.That(columns["ResourceName"].Type, Is.EqualTo("NVARCHAR"));
    }
}
