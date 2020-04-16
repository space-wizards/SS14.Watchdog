using Microsoft.AspNetCore.Mvc;

namespace SS14.Watchdog.Controllers
{
    [Controller]
    [Route("/watchdog")]
    public class WatchdogController : ControllerBase
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
    }
}