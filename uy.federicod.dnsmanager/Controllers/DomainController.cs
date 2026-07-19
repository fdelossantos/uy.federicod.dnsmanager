using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using uy.federicod.dnsmanager.Models;
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
        public async Task<IActionResult> Register(DomainRegistrationViewModel model)
        {
            string? accountId = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(accountId))
            {
                return Challenge();
            }

            model.DomainName = (model.DomainName ?? string.Empty).Trim().ToLowerInvariant();
            model.ZoneName = (model.ZoneName ?? string.Empty).Trim().ToLowerInvariant();

            var zones = await service.GetAvailableZonesAsync();
            var selectedZone = zones.FirstOrDefault(zone =>
                string.Equals(zone.Key, model.ZoneName, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(selectedZone.Key))
            {
                ModelState.AddModelError(nameof(model.ZoneName), "The selected zone is not available.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            SearchModel availability = await service.SearchDomainAsync(model.DomainName, selectedZone.Value);
            if (!availability.Available)
            {
                ModelState.AddModelError(string.Empty, availability.Message);
                return View(model);
            }

            var account = new AccountModel
            {
                AccountId = accountId,
                DisplayName = User.Claims.FirstOrDefault(claim => claim.Type == "name")?.Value ?? accountId
            };
            var request = new DomainRegistrationRequest
            {
                DomainName = model.DomainName,
                ZoneId = selectedZone.Value,
                ZoneName = selectedZone.Key,
                DelegationType = model.DelegationType,
                HostedRecordType = model.HostedRecordType,
                HostedTarget = model.HostedTarget,
                NameServers = model.GetNormalizedNameservers()
            };

            try
            {
                var domains = new Domains(service);
                var result = await domains.CreateAsync(request, account);
                if (!result.Ok)
                {
                    ModelState.AddModelError(string.Empty, result.Message);
                    return View(model);
                }

                TempData["Success"] = $"{model.DomainName}.{model.ZoneName} has been registered.";
                return RedirectToAction("My");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Domain registration failed for {DomainName}.{ZoneName}.",
                    model.DomainName,
                    model.ZoneName);
                ModelState.AddModelError(string.Empty, "The domain could not be registered. Try again.");
                return View(model);
            }
        }
        
        public async Task<IActionResult> RegisterAsync(string id, string zone)
        {
            string domainName = id.ToString().ToLower();
            var zones = await service.GetAvailableZonesAsync();

            SearchModel model = await service.SearchDomainAsync(domainName, zones[zone].ToLower());

            return View(new DomainRegistrationViewModel
            {
                DomainName = model.Domain,
                ZoneName = model.ZoneName,
                DelegationType = "Hosted",
                HostedRecordType = HostedRecordRules.AddressType
            });
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
        public async Task<IActionResult> AddHostedRecord(string ZoneName, string DomainName, string RecordType, string RecordName, string RecordContent, string RecordPriority)
        {
            var zonesByName = await service.GetAvailableZonesAsync();
            if (!zonesByName.TryGetValue(ZoneName, out var zoneId))
            {
                TempData["Error"] = "Zone not found.";
                return RedirectToAction("Manage", new { id = DomainName, zonename = ZoneName });
            }

            var domains = new Domains(service);
            var (ok, msg) = await domains.CreateHostedRecordAsync(zoneId, DomainName, RecordType, RecordName, RecordContent, User?.Identity?.Name, RecordPriority);
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
            var (ok, msg) = await domains.DeleteHostedRecordAsync(
                zoneId,
                DomainName,
                RecordId,
                User?.Identity?.Name ?? string.Empty);
            TempData[ok ? "Success" : "Error"] = msg;

            return RedirectToAction("Manage", new { id = DomainName, zonename = ZoneName });
        }
    }
}
