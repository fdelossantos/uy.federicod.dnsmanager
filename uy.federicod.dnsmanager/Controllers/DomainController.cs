using Microsoft.AspNetCore.Mvc;
using System.Net;
using uy.federicod.dnsmanager.logic;
using uy.federicod.dnsmanager.logic.Models;

namespace uy.federicod.dnsmanager.UI.Controllers
{
    public class DomainController : Controller
    {
        private readonly IConfiguration configuration;
        private readonly ILogger<DomainController> _logger;

        public DomainController(IConfiguration config, ILogger<DomainController> logger)
        {
            configuration = config;
            _logger = logger;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Register(IFormCollection collection)
        {
            if (collection["Accept"] == "Accept")
            {
                IPAddress iPAddress = IPAddress.Parse(collection["IPAddress"]);
                Service service = new(configuration["Cloudflare:UserName"], configuration["Cloudflare:ApiKey"], configuration.GetConnectionString("default"));
                Domains domains = new Domains(service);
                domains.CreateAsync("", "", "", iPAddress);
            }

            return View();
        }
        public async Task<IActionResult> RegisterAsync(string id, string zone)
        {
            string domainName = id.ToString().ToLower();
            Service service = new(configuration["Cloudflare:UserName"], configuration["Cloudflare:ApiKey"], configuration.GetConnectionString("default"));
            var zones = await service.GetAvailableZonesAsync();

            SearchModel model = await service.SearchDomainAsync(domainName, zones[zone].ToLower());

            return View(model);
        }
    }
}
