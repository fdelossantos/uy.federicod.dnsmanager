using Microsoft.AspNetCore.Mvc;

namespace uy.federicod.dnsmanager.UI.Controllers
{
    public class AdminController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
