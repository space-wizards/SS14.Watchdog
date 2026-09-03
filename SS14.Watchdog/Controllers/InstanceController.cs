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

        /// <summary>
        /// Reverts the specified server instance to a previous version or a specified target version.
        /// Can be older or newer than the currently running version.
        /// Only supported for instances using Robust.CDN.
        /// </summary>
        /// <param name="authorization">
        /// The authorization header containing the credentials for the operation.
        /// </param>
        /// <param name="key">
        /// The unique identifier of the server instance to revert.
        /// </param>
        /// <param name="version">
        /// The optional target version to revert to. If null, the instance will revert
        /// to the version immediately before the currently running one.
        /// </param>
        /// <param name="immediate">
        /// A flag indicating whether the revert should occur immediately.
        /// Defaults to false.
        /// </param>
        /// <returns>
        /// An <see cref="IActionResult"/> representing the result of the revert operation.
        /// Returns a 400 status code if reverting is unsupported, the version does not exist,
        /// or no earlier version is available. Otherwise, returns a 200 status code
        /// with the resolved version and whether the revert was immediate.
        /// </returns>
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

        /// <summary>
        /// Retrieves a list of recent versions for a specified server instance, including their versions,
        /// timestamps, and whether each version matches the currently running version.
        /// </summary>
        /// <param name="authorization">
        /// The authorization header containing the credentials to authenticate the request.
        /// </param>
        /// <param name="key">
        /// The unique identifier of the server instance whose version history is being queried.
        /// </param>
        /// <returns>
        /// An <see cref="IActionResult"/> containing the list of recent versions if the operation succeeds.
        /// Each version includes the version string, its timestamp, and an indicator if it is the currently running version.
        /// Returns a 400 status code if the instance does not support version listing or if authentication fails.
        /// </returns>
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
