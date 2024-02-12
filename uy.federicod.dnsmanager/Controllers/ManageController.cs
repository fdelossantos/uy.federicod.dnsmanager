using Microsoft.AspNetCore.Mvc;

namespace uy.federicod.dnsmanager.UI.Controllers
{
    public class ManageController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
