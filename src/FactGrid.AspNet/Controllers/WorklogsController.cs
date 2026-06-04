using Microsoft.AspNetCore.Mvc;

namespace FactGrid.AspNet.Controllers;

public class WorklogsController : Controller
{
    public IActionResult Index() => RedirectToAction("Detail", "Entity", new { entityName = "worklogs" });
}
