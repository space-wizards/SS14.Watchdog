using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SS14.Watchdog.Components.ServerManagement;
using SS14.Watchdog.Utility;
using SIOFile = System.IO.File;

namespace SS14.Watchdog.Controllers
{
    [ApiController]
    [Route("/instances/{key}")]
    public class InstanceController : ControllerBase
    {
        private readonly IServerManager _serverManager;

        public InstanceController(IServerManager serverManager)
        {
            _serverManager = serverManager;
        }

        [HttpGet]
        [Authorize(Policy = "BasicAuthentication")]
        public IActionResult Get([FromHeader(Name = "Authorization")] string authorization, [FromRoute] string key)
        {
            if (!TryAuthorize(authorization, key, out var failure, out var instance))
            {
                return failure;
            }

            return Ok(instance);
        }

        [HttpPost("restart")]
        [Authorize(Policy = "BasicAuthentication")]
        public async Task<IActionResult> Restart([FromHeader(Name = "Authorization")] string authorization, [FromRoute] string key)
        {
            if (!TryAuthorize(authorization, key, out var failure, out var instance))
            {
                return failure;
            }

            await instance.DoRestartCommandAsync();
            return Ok();
        }

        [HttpPost("stop")]
        [Authorize(Policy = "BasicAuthentication")]
        public async Task<IActionResult> Stop([FromHeader(Name = "Authorization")] string authorization, [FromRoute] string key)
        {
            if (!TryAuthorize(authorization, key, out var failure, out var instance))
            {
                return failure;
            }

            await instance.DoStopCommandAsync(new ServerInstanceStopCommand());
            return Ok();
        }

        [HttpPost("update")]
        [Authorize(Policy = "BasicAuthentication")]
        public IActionResult Update([FromHeader(Name = "Authorization")] string authorization, [FromRoute] string key)
        {
            if (!TryAuthorize(authorization, key, out var failure, out var instance))
            {
                return failure;
            }

            instance.HandleUpdateCheck();
            return Ok();
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
