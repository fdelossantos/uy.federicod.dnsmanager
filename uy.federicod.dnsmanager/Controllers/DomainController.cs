using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using System.Net;
using System.Threading.Tasks;
using uy.federicod.dnsmanager.logic;
using uy.federicod.dnsmanager.logic.Models;


namespace uy.federicod.dnsmanager.UI.Controllers
{
    public class DomainController : Controller
    {
        private readonly IConfiguration configuration;
        private readonly ILogger<DomainController> _logger;
        private readonly Service service;

        public DomainController(IConfiguration config, ILogger<DomainController> logger)
        {
            configuration = config;
            _logger = logger;
            service = new(configuration["Cloudflare:UserName"], configuration["Cloudflare:ApiKey"], configuration.GetConnectionString("default"));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Register(IFormCollection collection)
        {
            // Inicializa objetos
            
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
                            ZoneId = zones[zonaname],
                            ZoneName = zonaname
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
            var zones = await service.GetAvailableZonesAsync();

            SearchModel model = await service.SearchDomainAsync(domainName, zones[zone].ToLower());

            return View(model);
        }

        public IActionResult My()
        {
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

        public async Task<ActionResult> EditAsync(string id, string zonename)
        {
            Domains domains = new Domains(service);
            var zones = service.GetAvailableZonesAsync().Result;
            DomainModel domainModel = await domains.GetUserDomainAsync(id, zones[zonename], User.Identity.Name);
            domainModel.ZoneName = zonename;

            return View(domainModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DeleteAsync(string id, string zonename)
        {
            var zones = await service.GetAvailableZonesAsync(); // ZoneName -> ZoneId
            if (!zones.TryGetValue(zonename, out var zoneId))
            {
                TempData["Error"] = "Zone not found.";
                return RedirectToAction("My");
            }

            var domains = new Domains(service);
            var ok = await domains.DeleteUserDomainAsync(id, zoneId, zonename, User?.Identity?.Name);

            TempData[ok ? "Success" : "Error"] = ok ? "The domain has been deleted" : "Delete failed.";
            return RedirectToAction("My");
        }

        public async Task<ActionResult> ManageAsync(string id, string zonename)
        {
            Domains domains = new Domains(service);
            var zones = await service.GetAvailableZonesAsync();
            var zoneId = zones[zonename];

            DomainModel domainModel = await domains.GetUserDomainAsync(id, zoneId, User.Identity.Name);
            domainModel.ZoneName = zonename;

            //ViewBag.Records = await domains.GetRecordsAsync(id, zones[zonename]);
            ViewBag.Records = await domains.GetHostedRecordsAsync(id, zoneId, domainModel.AccountId);

            return View(domainModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddNameserver(NameserverChangeModel input)
        {
            if (string.IsNullOrWhiteSpace(input?.ZoneName) ||
                string.IsNullOrWhiteSpace(input?.DomainName) ||
                string.IsNullOrWhiteSpace(input?.Nameserver))
            {
                TempData["Error"] = "Nameserver is required.";
                return RedirectToAction("Manage", new { id = input?.DomainName, zonename = input?.ZoneName });
            }

            var zonesByName = await service.GetAvailableZonesAsync(); // ZoneName -> ZoneId
            if (!zonesByName.TryGetValue(input.ZoneName, out var zoneId))
            {
                TempData["Error"] = "Zone not found.";
                return RedirectToAction("Manage", new { id = input.DomainName, zonename = input.ZoneName });
            }

            var domains = new Domains(service);
            var (ok, msg) = await domains.AddNameserverAsync(input.DomainName, zoneId, input.Nameserver, User?.Identity?.Name);
            TempData[ok ? "Success" : "Error"] = msg;

            return RedirectToAction("Manage", new { id = input.DomainName, zonename = input.ZoneName });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveNameserver(NameserverChangeModel input)
        {
            if (string.IsNullOrWhiteSpace(input?.ZoneName) ||
                string.IsNullOrWhiteSpace(input?.DomainName) ||
                string.IsNullOrWhiteSpace(input?.Nameserver))
            {
                TempData["Error"] = "Nameserver is required.";
                return RedirectToAction("Manage", new { id = input?.DomainName, zonename = input?.ZoneName });
            }

            var zonesByName = await service.GetAvailableZonesAsync(); // ZoneName -> ZoneId
            if (!zonesByName.TryGetValue(input.ZoneName, out var zoneId))
            {
                TempData["Error"] = "Zone not found.";
                return RedirectToAction("Manage", new { id = input.DomainName, zonename = input.ZoneName });
            }

            var domains = new Domains(service);
            var (ok, msg) = await domains.RemoveNameserverAsync(input.DomainName, zoneId, input.Nameserver);
            TempData[ok ? "Success" : "Error"] = msg;

            return RedirectToAction("Manage", new { id = input.DomainName, zonename = input.ZoneName });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddHostedRecord(string ZoneName, string DomainName, string RecordType, string RecordName, string RecordContent)
        {
            var zonesByName = await service.GetAvailableZonesAsync();
            if (!zonesByName.TryGetValue(ZoneName, out var zoneId))
            {
                TempData["Error"] = "Zone not found.";
                return RedirectToAction("Manage", new { id = DomainName, zonename = ZoneName });
            }

            var domains = new Domains(service);
            var (ok, msg) = await domains.CreateHostedRecordAsync(zoneId, DomainName, RecordType, RecordName, RecordContent, User?.Identity?.Name);
            TempData[ok ? "Success" : "Error"] = msg;

            return RedirectToAction("Manage", new { id = DomainName, zonename = ZoneName });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteHostedRecord(string ZoneName, string DomainName, string RecordId)
        {
            var zonesByName = await service.GetAvailableZonesAsync();
            if (!zonesByName.TryGetValue(ZoneName, out var zoneId))
            {
                TempData["Error"] = "Zone not found.";
                return RedirectToAction("Manage", new { id = DomainName, zonename = ZoneName });
            }

            var domains = new Domains(service);
            var (ok, msg) = await domains.DeleteHostedRecordAsync(zoneId, DomainName, RecordId);
            TempData[ok ? "Success" : "Error"] = msg;

            return RedirectToAction("Manage", new { id = DomainName, zonename = ZoneName });
        }
    }
}
