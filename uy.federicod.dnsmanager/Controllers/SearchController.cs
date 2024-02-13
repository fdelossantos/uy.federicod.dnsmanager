using Microsoft.AspNetCore.Mvc;
using uy.federicod.dnsmanager.logic;
using uy.federicod.dnsmanager.logic.Models;

namespace uy.federicod.dnsmanager.UI.Controllers
{
    public class SearchController : Controller
    {
        private readonly IConfiguration configuration;
        private readonly ILogger<SearchController> _logger;

        public SearchController(IConfiguration config, ILogger<SearchController> logger)
        {
            configuration = config;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        // POST: AdminController/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Index(IFormCollection collection)
        {
            string domainName = collection["domain"].ToString().ToLower();
            Service service = new(configuration["Cloudflare:UserName"], configuration["Cloudflare:ApiKey"], configuration.GetConnectionString("default"));

            SearchModel model = await service.SearchDomainAsync(domainName, configuration["Cloudflare:ZoneId"]);
            //SearchModel model = new()
            //{
            //    Available = true,
            //    Domain = domainName
            //};

            //ViewBag.DomainName = domainName;

            return View(model);
        }
    }
}
