using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SS14.Watchdog.Components.ServerManagement;

namespace SS14.Watchdog.Controllers
{
    [ApiController]
    [Route("/watchdog")]
    public class WatchdogController(IServerManager serverManager) : ControllerBase
    {
        [HttpGet("padlock")]
        public IActionResult Padlock()
        {
            // https://discordapp.com/channels/484170914754330625/484170915253321734/699267442677121044
            return Content(@" --
|  |
####
####
");
        }

        [HttpGet("instances")]
        public async Task<IActionResult> Instances()
        {
            return Ok(serverManager.Instances);
        }
    }
}
