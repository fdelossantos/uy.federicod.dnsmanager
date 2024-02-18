using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using uy.federicod.dnsmanager.logic;
using uy.federicod.dnsmanager.logic.Models;

namespace uy.federicod.dnsmanager.UI.Controllers
{
    public class SearchController : Controller
    {
        private readonly IConfiguration configuration;
        private readonly ILogger<SearchController> _logger;
        private readonly Service service;

        public SearchController(IConfiguration config, ILogger<SearchController> logger)
        {
            configuration = config;
            _logger = logger;
            service = new(configuration["Cloudflare:UserName"], configuration["Cloudflare:ApiKey"], configuration.GetConnectionString("default"));
        }

        public async Task<IActionResult> IndexAsync()
        {
            Dictionary<string, string> allzones = (Dictionary<string, string>)await service.GetAvailableZonesAsync();
            ViewBag.allzones = allzones;

            return View();
        }

        // POST: AdminController/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> IndexAsync(IFormCollection collection)
        {
            string domainName = collection["domain"].ToString().ToLower();
            Service service = new(configuration["Cloudflare:UserName"], configuration["Cloudflare:ApiKey"], configuration.GetConnectionString("default"));
            List<SearchModel> searchModels = [];
            Dictionary<string, string> allzones = (Dictionary<string, string>)await service.GetAvailableZonesAsync();
            foreach(var zone in  allzones)
            {
                SearchModel model = await service.SearchDomainAsync(domainName, zone.Value);
                searchModels.Add(model);
            }

            return View("Found", searchModels);

            //if (model.Available)
            //    return View("Found", model);
            //else
            //    return View("NotAvailable", model);
        }

        public IActionResult Found() 
        { 
            return View(); 
        }

        public IActionResult NotAvailable() 
        { 
            return View(); 
        }
    }
}
