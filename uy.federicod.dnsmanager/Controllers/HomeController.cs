using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using uy.federicod.dnsmanager.logic;
using uy.federicod.dnsmanager.Models;
using uy.federicod.dnsmanager.UI.Controllers;

namespace uy.federicod.dnsmanager.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration configuration;
        private readonly Service service;

        public HomeController(IConfiguration config, ILogger<HomeController> logger)
        {
            configuration = config;
            _logger = logger;
            service = new(configuration["Cloudflare:UserName"], configuration["Cloudflare:ApiKey"], configuration.GetConnectionString("default"));
        }

        [AllowAnonymous]
        public IActionResult Index()
        {
            //Dictionary<string,string> allzones = (Dictionary<string, string>)await service.GetAvailableZonesAsync();
            //ViewBag.allzones = allzones;

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
