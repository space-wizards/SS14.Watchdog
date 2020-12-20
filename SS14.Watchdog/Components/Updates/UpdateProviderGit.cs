using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
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
        private readonly IConfiguration _configuration;
        
        public UpdateProviderGit(ServerInstance serverInstanceInstance, UpdateProviderGitConfiguration configuration, ILogger<UpdateProviderGit> logger, IConfiguration config)
        {
            _serverInstance = serverInstanceInstance;
            _baseUrl = configuration.BaseUrl;
            _branch = configuration.Branch;
            _logger = logger;
            _repoPath = Path.Combine(_serverInstance.InstanceDir, "source");
            _configuration = config;
        }
        
        public override Task<bool> CheckForUpdateAsync(string? currentVersion, CancellationToken cancel = default)
        {
            if (!Repository.IsValid(_repoPath) || currentVersion == null)
                return Task.FromResult(true);
            

            var logMessage = "";
            var update = false;
                
            using var repository = new Repository(_repoPath);
            var remote = repository.Network.Remotes["origin"];
            var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
            Commands.Fetch(repository, remote.Name, refSpecs, null, logMessage);
                
            var localBranch = repository.Branches[_branch];
            var remoteBranch = localBranch.TrackedBranch;

            if (localBranch.Tip != remoteBranch.Tip || currentVersion != remoteBranch.Tip.ToString())
                update = true;
                
            _logger.LogInformation(logMessage);
            return Task.FromResult(update);
        }

        public override async Task<string?> RunUpdateAsync(string? currentVersion, string binPath, CancellationToken cancel = default)
        {
            try
            {
                if (!Repository.IsValid(_repoPath) || currentVersion == null)
                    TryClone();

                using var repository = new Repository(_repoPath);

                var remote = repository.Network.Remotes["origin"];
                Commands.Fetch(repository, remote.Name, remote.FetchRefSpecs.Select(x => x.Specification),
                    null, null);

                _logger.LogTrace("Updating...");
                
                Commands.Pull(repository, new Signature("Watchdog", "N/A", DateTimeOffset.Now), null);
                var localBranch = Commands.Checkout(repository, repository.Branches[_branch]);
                
                _logger.LogDebug($"Went from {currentVersion} to {localBranch.Tip}");

                foreach (var submodule in repository.Submodules)
                {
                    repository.Submodules.Update(submodule.Name, null);
                }

                using var engineRepository = new Repository(Path.Combine(_repoPath, "RobustToolbox"));

                var engineVersion = engineRepository.Describe(engineRepository.Branches["master"].Tip, 
                                        new DescribeOptions(){MinimumCommitIdAbbreviatedSize = 0, Strategy = DescribeStrategy.Tags})?.Trim()
                                    ?? throw new NullReferenceException("Can't find version for engine submodule.");

                if (!engineVersion.StartsWith('v'))
                    throw new InvalidDataException($"Engine submodule tag \"{engineVersion}\" doesn't start with v!");

                engineVersion = engineVersion.Substring(1);

                // Now we build and package it.

                var processClientBuild = new Process
                {
                    StartInfo =
                    {
                        FileName = "python", CreateNoWindow = true, UseShellExecute = true,
                        WorkingDirectory = _repoPath,
                        Arguments = "Tools/package_client_build.py"
                    },
                };

                // Platform to build the server for.
                var serverPlatform = GetHostPlatformName() switch
                {
                    PlatformNameWindows => "windows",
                    PlatformNameMacOS => "mac",
                    PlatformNameLinux => RuntimeInformation.OSArchitecture == Architecture.Arm64
                        ? "linux-arm64"
                        : "linux",
                    _ => throw new PlatformNotSupportedException()
                };

                var processServerBuild = new Process
                {
                    StartInfo =
                    {
                        FileName = "python", CreateNoWindow = true, UseShellExecute = true,
                        WorkingDirectory = _repoPath,
                        Arguments = $"Tools/package_server_build.py -p {serverPlatform}"
                    },
                };

                var binariesPath = Path.Combine(_serverInstance.InstanceDir, "binaries");
                
                var binariesRoot = new Uri(new Uri(_configuration["BaseUrl"]),
                    $"instances/{_serverInstance.Key}/binaries/");
                
                _logger.LogTrace("Building client packages...");
                
                processClientBuild.Start();
                await processClientBuild.WaitForExitAsync(cancel);
                
                File.Move(Path.Combine(_repoPath, "release", ClientZipName), Path.Combine(binariesPath, ClientZipName), true);
                
                _logger.LogTrace("Building server packages...");
                
                processServerBuild.Start();
                await processServerBuild.WaitForExitAsync(cancel);

                _logger.LogTrace("Applying server update.");
                
                if (Directory.Exists(binPath))
                {
                    Directory.Delete(binPath, true);
                }

                Directory.CreateDirectory(binPath);

                _logger.LogTrace("Extracting zip file");

                var serverPackage = Path.Combine(_repoPath, "release", $"SS14.Server_{GetHostPlatformName()}_{GetHostArchitectureName()}.zip");

                var stream = File.Open(serverPackage, FileMode.Open);

                // Actually extract.
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                archive.ExtractToDirectory(binPath);
                
                // Remove the package now that it's extracted.
                File.Delete(serverPackage);

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

                var build = new Build()
                {
                    Download = new Uri(binariesRoot, ClientZipName).ToString(),
                    Hash = GetFileHash(Path.Combine(binariesPath, ClientZipName)),
                    Version = localBranch.Tip.ToString(),
                    EngineVersion = engineVersion,
                    ForkId = _baseUrl,
                };

                await File.WriteAllTextAsync(Path.Combine(binPath, "build.json"), JsonSerializer.Serialize(build), cancel);
                
                // ReSharper disable once RedundantTypeArgumentsOfMethod
                return localBranch.Tip.ToString();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to run update!");

                return null;
            }
        }

        private class Build
        {
            [JsonPropertyName("download")]
            public string Download { get; set; }
            
            [JsonPropertyName("hash")]
            public string Hash { get; set; }
            
            [JsonPropertyName("version")]
            public string Version { get; set; }
            
            [JsonPropertyName("engine_version")]
            public string EngineVersion { get; set; }
            
            [JsonPropertyName("fork_id")]
            public string ForkId { get; set; }
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