using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Logging;
using SS14.Watchdog.Components.ServerManagement;
using SS14.Watchdog.Configuration.Updates;
using SS14.Watchdog.Utility;

namespace SS14.Watchdog.Components.Updates
{
    public class UpdateProviderGit : UpdateProvider
    {
        private readonly ServerInstance _serverInstance;
        private readonly string _baseUrl;
        private readonly string _branch;
        private readonly ILogger<UpdateProviderGit> _logger;
        private readonly string _repoPath;
        
        public UpdateProviderGit(ServerInstance serverInstanceInstance, UpdateProviderGitConfiguration configuration, ILogger<UpdateProviderGit> logger)
        {
            _serverInstance = serverInstanceInstance;
            _baseUrl = configuration.BaseUrl;
            _branch = configuration.Branch;
            _logger = logger;
            _repoPath = Path.Combine(_serverInstance.InstanceDir, "source");
        }
        
        public override Task<bool> CheckForUpdateAsync(string? currentVersion, CancellationToken cancel = default)
        {
            if (currentVersion == null)
                return Task.FromResult(true);

            var logMessage = "";
            var update = false;
            
            using (var repository = new Repository(_repoPath))
            {
                var remote = repository.Network.Remotes["origin"];
                var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
                Commands.Fetch(repository, remote.Name, refSpecs, null, logMessage);
                
                var localBranch = repository.Branches[_branch];
                var remoteBranch = localBranch.TrackedBranch;

                if (localBranch.Tip != remoteBranch.Tip)
                    update = true;
            }
            
            _logger.LogInformation(logMessage);
            return Task.FromResult(update);
        }

        public override async Task<RevisionDescription?> RunUpdateAsync(string? currentVersion, string binPath, CancellationToken cancel = default)
        {
            if (currentVersion == null)
                TryClone();

            using var repository = new Repository(_repoPath);
            var localBranch = repository.Branches[_branch];
            var remoteBranch = localBranch.TrackedBranch;

            Commands.Checkout(repository, localBranch);
            repository.Merge(remoteBranch, null, null);

            foreach (var submodule in repository.Submodules)
            {
                repository.Submodules.Update(submodule.Name, null);
            }
            
            // Now we build and package it.

            var process = new Process
            {
                StartInfo = {FileName = "python", RedirectStandardInput = false,
                    RedirectStandardOutput = true, CreateNoWindow = true, UseShellExecute = true,
                    WorkingDirectory = _repoPath, Arguments = "Tools/package_release_build.py -p windows mac linux linux-arm64"
                },
            };

            process.Start();
            await process.WaitForExitAsync(cancel);

            return null;
        }
        
        private void TryClone()
        {
            Repository.Clone(_baseUrl, _repoPath);
            
            using var repository = new Repository(_repoPath);
            var remote = repository.Network.Remotes["origin"];
            var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
            Commands.Fetch(repository, remote.Name, refSpecs, null, null);
            var remoteBranch = repository.Branches[$"origin/{_branch}"];
            var localBranch = repository.CreateBranch(_branch);
            repository.Branches.Update(localBranch, b => b.TrackedBranch = remoteBranch.CanonicalName);
        }
    }
}