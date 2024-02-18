﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
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
            // Inicializa objetos
            Service service = new(configuration["Cloudflare:UserName"], configuration["Cloudflare:ApiKey"], configuration.GetConnectionString("default"));
            Domains domains = new Domains(service);

            // Prepara un AccountModel para buscar o crear el usuario
            AccountModel account = new AccountModel()
            {
                AccountId = User.Identity.Name,
                DisplayName = User.Claims.FirstOrDefault(c => c.Type == "name").Value
            };
            IPAddress iPAddress = new(0);
            string DelegationType = collection["DelegationType"];
            string zonaname = collection["zonename"];
            var zones = service.GetAvailableZonesAsync().Result;

            if (collection["Accept"] == "Accept")
            {
                // DomainName
                string domainname = collection["domainname"];
                // IPAddress si es Hosted
                
                if (DelegationType == "Hosted")
                {
                    try
                    {
                        iPAddress = IPAddress.Parse(collection["IPAddress"]);
                    }

                    catch (Exception ex)
                    {
                        // Si la IP no es válida, hay que avisarle.
                        ViewBag.Message = ex.Message;
                        SearchModel searchModel = new()
                        {
                            Domain = domainname,
                            Available = true,
                            ZoneId = configuration["Cloudflare:ZoneId"], // Estos valores son temporales
                            ZoneName = configuration["Cloudflare:ZoneName"]
                        };

                        return View(searchModel);
                    }
                }
                
                List<string> ns = [];
                if(DelegationType == "Delegated")
                {
                    foreach (string linea in collection["nameservers"].ToString().Split('\n'))
                    {
                        ns.Add(linea);
                    }
                }

                Dictionary<string, string> resultados = [];
                if (DelegationType == "Hosted")
                    resultados = domains.CreateAsync(domainname, zones[zonaname], 
                        DelegationType, account, service, HostIP: iPAddress);
                else
                    resultados = domains.CreateAsync(domainname, zones[zonaname],
                        DelegationType, account, service, NameServers: ns);
            }

            return RedirectToAction("My");
        }
        
        public async Task<IActionResult> RegisterAsync(string id, string zone)
        {
            string domainName = id.ToString().ToLower();
            Service service = new(configuration["Cloudflare:UserName"], configuration["Cloudflare:ApiKey"], configuration.GetConnectionString("default"));
            var zones = await service.GetAvailableZonesAsync();

            SearchModel model = await service.SearchDomainAsync(domainName, zones[zone].ToLower());

            return View(model);
        }

        public IActionResult My()
        {
            Service service = new(configuration["Cloudflare:UserName"], configuration["Cloudflare:ApiKey"], configuration.GetConnectionString("default"));
            Domains domains = new Domains(service);

            List<DomainModel> listOfDomains = [];

            listOfDomains = domains.GetDomains(User.Identity.Name);

            var zones = service.GetAvailableZonesByIdAsync().Result;
            foreach (DomainModel domain in listOfDomains)
            {
                domain.ZoneName = zones[domain.ZoneId];
            }

            return View(listOfDomains);
        }

        public ActionResult Edit(string id)
        {
            return View();
        }
    }
}
