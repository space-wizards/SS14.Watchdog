using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SS14.Watchdog.Components.ServerManagement;
using SS14.Watchdog.Utility;
using SIOFile = System.IO.File;

namespace SS14.Watchdog.Controllers
{
    [Route("/instances/{key}")]
    [Controller]
    public class InstanceController : ControllerBase
    {
        private readonly IServerManager _serverManager;

        public InstanceController(IServerManager serverManager)
        {
            _serverManager = serverManager;
        }

        [HttpPost("restart")]
        public async Task<IActionResult> Restart([FromHeader(Name = "Authorization")] string authorization, string key)
        {
            if (!TryAuthorize(authorization, key, out var failure, out var instance))
            {
                return failure;
            }

            await instance.DoRestartCommandAsync();
            return Ok();
        }

        [HttpPost("stop")]
        public async Task<IActionResult> Stop([FromHeader(Name = "Authorization")] string authorization, string key)
        {
            if (!TryAuthorize(authorization, key, out var failure, out var instance))
            {
                return failure;
            }

            await instance.DoStopCommandAsync(new ServerInstanceStopCommand());
            return Ok();
        }

        [HttpPost("update")]
        public IActionResult Update([FromHeader(Name = "Authorization")] string authorization, string key)
        {
            if (!TryAuthorize(authorization, key, out var failure, out var instance))
            {
                return failure;
            }

            instance.HandleUpdateCheck();
            return Ok();
        }

        [HttpPost("revert")]
        public async Task<IActionResult> Revert(
            [FromHeader(Name = "Authorization")] string authorization,
            string key,
            [FromQuery] string? version,
            [FromQuery] bool immediate = false)
        {
            if (!TryAuthorize(authorization, key, out var failure, out var instance))
            {
                return failure;
            }

            var resolved = await instance.DoRevertCommandAsync(version, immediate);
            if (resolved == null)
            {
                return BadRequest(
                    "This instance does not support reverting, the specified version does not exist, or no earlier version is available.");
            }

            return Ok(new { version = resolved, immediate });
        }

        [HttpGet("versions")]
        public async Task<IActionResult> Versions(
            [FromHeader(Name = "Authorization")] string authorization,
            string key)
        {
            if (!TryAuthorize(authorization, key, out var failure, out var instance))
            {
                return failure;
            }

            var versions = await instance.GetRecentVersionsAsync(5);
            if (versions == null)
            {
                return BadRequest("This instance does not support version listing.");
            }

            return Ok(versions.Select(v => new
            {
                version = v.Version,
                time = v.Time,
                current = v.Version == instance.CurrentRevision
            }));
        }

        [NonAction]
        public bool TryAuthorize(string authorization,
            string key,
            [NotNullWhen(false)] out IActionResult? failure,
            [NotNullWhen(true)] out IServerInstance? instance)
        {
            instance = null;

            if (!AuthorizationUtility.TryParseBasicAuthentication(authorization, out failure, out var authKey,
                out var token))
            {
                return false;
            }

            if (authKey != key)
            {
                failure = Forbid();
                return false;
            }

            if (!_serverManager.TryGetInstance(key, out instance))
            {
                failure = NotFound();
                return false;
            }

            // TODO: we probably need constant-time comparisons for this?
            // Maybe?
            if (token != instance.ApiToken)
            {
                failure = Unauthorized();
                return false;
            }

            return true;
        }
    }
}
