using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using SS14.Watchdog.Components.ServerManagement;
using SS14.Watchdog.Configuration.Updates;

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
            try
            {
                if (currentVersion == null)
                    TryClone();

                _logger.LogTrace("Updating...");

                using var repository = new Repository(_repoPath);
                var localBranch = repository.Branches[_branch];
                var remoteBranch = localBranch.TrackedBranch;

                Commands.Checkout(repository, localBranch);
                repository.Merge(remoteBranch,
                    new Signature("Watchdog", "telecommunications@spacestation14.io", DateTimeOffset.Now), null);

                foreach (var submodule in repository.Submodules)
                {
                    repository.Submodules.Update(submodule.Name, null);
                }

                // Now we build and package it.

                _logger.LogTrace("Building...");

                var process = new Process
                {
                    StartInfo =
                    {
                        FileName = "python", CreateNoWindow = true, UseShellExecute = true,
                        WorkingDirectory = _repoPath,
                        Arguments = "Tools/package_release_build.py -p windows mac linux linux-arm64"
                    },
                };

                var binariesPath = Path.Combine(_serverInstance.InstanceDir, "binaries");
                
                process.Start();
                await process.WaitForExitAsync(cancel);

                foreach (var file in Directory.EnumerateFiles(Path.Combine(_repoPath, "release")))
                {
                    File.Move(file, Path.Combine(binariesPath, Path.GetFileName(file)), true);
                }
                
                _logger.LogTrace("Applying server update.");
                
                if (Directory.Exists(binPath))
                {
                    Directory.Delete(binPath, true);
                }

                Directory.CreateDirectory(binPath);

                _logger.LogTrace("Extracting zip file");

                var name = $"SS14.Server_{GetHostPlatformName()}_{GetHostArchitectureName()}.zip";

                var stream = File.Open(Path.Combine(binariesPath, name), FileMode.Open);
                
                // Reset file position so we can extract.
                stream.Seek(0, SeekOrigin.Begin);

                // Actually extract.
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                archive.ExtractToDirectory(binPath);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // chmod +x Robust.Server

                    var rsPath = Path.Combine(binPath, "Robust.Server");
                    if (File.Exists(rsPath))
                    {
                        var proc = Process.Start(new ProcessStartInfo("chmod")
                        {
                            ArgumentList = {"+x", rsPath}
                        });

                        await proc!.WaitForExitAsync(cancel);
                    }
                }

                DownloadInfoPair? GetInfoPair(string platform)
                {
                    var fileName = GetBuildFilename(platform);
                    var diskFileName = Path.Combine(binariesPath, fileName);

                    if (!File.Exists(diskFileName))
                    {
                        return null;
                    }

                    var download = Path.Combine(binariesPath, fileName);
                    var hash = GetFileHash(diskFileName);

                    _logger.LogTrace("SHA256 hash for {fileName} is {hash}", fileName, hash);

                    return new DownloadInfoPair(download, hash);
                }

                var revisionDescription = new RevisionDescription(
                    localBranch.Tip.ToString(),
                    GetInfoPair(PlatformNameWindows),
                    GetInfoPair(PlatformNameLinux),
                    GetInfoPair(PlatformNameMacOS));

                // ReSharper disable once RedundantTypeArgumentsOfMethod
                return revisionDescription;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to run update!");

                return null;
            }
        }
        
        private void TryClone()
        {
            _logger.LogTrace("Cloning git repository...");
            
            if(Directory.Exists(_repoPath))
                Directory.Delete(_repoPath, true);
            
            Repository.Clone(_baseUrl, _repoPath, new CloneOptions(){RecurseSubmodules = true});
            
            using var repository = new Repository(_repoPath);
            var remote = repository.Network.Remotes["origin"];
            var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
            Commands.Fetch(repository, remote.Name, refSpecs, null, null);
            var remoteBranch = repository.Branches[$"origin/{_branch}"];
            var localBranch = repository.Branches.Any(x => x.FriendlyName == _branch) 
                ? repository.Branches[_branch] : repository.CreateBranch(_branch);
            repository.Branches.Update(localBranch, b => b.TrackedBranch = remoteBranch.CanonicalName);
        }
    }
}