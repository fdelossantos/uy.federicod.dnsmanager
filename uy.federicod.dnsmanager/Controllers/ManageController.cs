using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using uy.federicod.dnsmanager.logic;

namespace uy.federicod.dnsmanager.UI.Controllers
{
    public class ManageController : Controller
    {
        private readonly IConfiguration configuration;
        private readonly ILogger<DomainController> _logger;
        private readonly Service service;

        public ManageController(IConfiguration config, ILogger<DomainController> logger)
        {
            configuration = config;
            _logger = logger;
            service = new(configuration["Cloudflare:UserName"], configuration["Cloudflare:ApiKey"], configuration.GetConnectionString("default"));

        }

        public IActionResult Index()
        {
            return View();
        }
    }
}
