using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;
using SS14.Watchdog.Components.ServerManagement;
using SS14.Watchdog.Utility;

namespace SS14.Watchdog.Controllers
{
    /// <summary>
    ///     API intended to be used by the instances to communicate back to the watchdog.
    /// </summary>
    [ApiController]
    [Route("/server_api/{key}")]
    public class ServerApiController : ControllerBase
    {
        private readonly IServerManager _serverManager;

        public ServerApiController(IServerManager serverManager)
        {
            _serverManager = serverManager;
        }

        [HttpPost("ping")]
        public IActionResult Ping([FromHeader(Name = "Authorization")] string authorization, string key)
        {
            if (!TryAuthorize(authorization, key, out var failure, out var instance))
            {
                return failure;
            }

            instance.PingReceived();
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
            if (token != instance.Secret)
            {
                failure = Unauthorized();
                return false;
            }

            return true;
        }
    }
}