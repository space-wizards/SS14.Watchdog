using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Mono.Unix;
using SS14.Watchdog.Components.ServerManagement;
using SS14.Watchdog.Configuration.Updates;

namespace SS14.Watchdog.Components.Updates
{
    /// <summary>
    ///     Builds updates from a Git repository.
    /// </summary>
    public class UpdateProviderGit : UpdateProvider
    {
        private readonly ServerInstance _serverInstance;
        private readonly string _baseUrl;
        private readonly string _branch;
        private readonly bool _hybridACZ;
        private readonly ILogger<UpdateProviderGit> _logger;
        private readonly string _repoPath;
        private readonly IConfiguration _configuration;
        private bool _newPackaging;

        public UpdateProviderGit(ServerInstance serverInstanceInstance, UpdateProviderGitConfiguration configuration, ILogger<UpdateProviderGit> logger, IConfiguration config)
        {
            _serverInstance = serverInstanceInstance;
            _baseUrl = configuration.BaseUrl;
            _branch = configuration.Branch;
            _hybridACZ = configuration.HybridACZ;
            _logger = logger;
            _repoPath = Path.Combine(_serverInstance.InstanceDir, "source");
            _configuration = config;
        }

        private async Task<CommandResult> CommandHelper(string cd, string command, string[] args, CancellationToken cancel = default)
        {
            var si = new ProcessStartInfo {
                FileName = command,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            if (!string.IsNullOrWhiteSpace(cd))
                si.WorkingDirectory = cd;

            // MSDN lied to me! https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-6.0
            foreach (var s in args)
                si.ArgumentList.Add(s);
            var proc = new Process
            {
                StartInfo = si
            };
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancel);
            var stderrTask = proc.StandardError.ReadToEndAsync(cancel);

            try
            {
                await proc.WaitForExitAsync(cancel);
                return new CommandResult(
                    proc.ExitCode,
                    await stdoutTask,
                    await stderrTask);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process already exited.
                }

                throw;
            }
        }

        private async Task CommandHelperChecked(string reason, string cd, string command, string[] args, CancellationToken cancel = default)
        {
            int exitCode;
            try
            {
                var result = await CommandHelper(cd, command, args, cancel);
                exitCode = result.ExitCode;
                if (exitCode != 0)
                {
                    _logger.LogWarning(
                        "Command failed while running {Command} {Arguments}. Exit code: {ExitCode}. Stdout: {Stdout}. Stderr: {Stderr}",
                        command,
                        string.Join(" ", args),
                        result.ExitCode,
                        result.Stdout,
                        result.Stderr);
                }
            }
            catch (Exception ex)
            {
                throw new Exception(reason, ex);
            }
            if (exitCode != 0)
            {
                throw new Exception(reason);
            }
        }

        private async Task<string> CommandHelperCheckedStdout(string reason, string cd, string command, string[] args)
        {
            try
            {
                var si = new ProcessStartInfo {
                    FileName = command,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WorkingDirectory = cd,
                    RedirectStandardOutput = true
                };
                foreach (var s in args)
                    si.ArgumentList.Add(s);
                var proc = new Process
                {
                    StartInfo = si
                };
                proc.Start();
                var text = proc.StandardOutput.ReadToEnd();
                await proc.WaitForExitAsync();
                if (proc.ExitCode != 0)
                {
                    throw new Exception($"Exit code: {proc.ExitCode}");
                }
                return text;
            }
            catch (Exception ex)
            {
                throw new Exception(reason, ex);
            }
        }

        // Git actions

        private async Task<bool> GitCheckRepositoryValid()
        {
            try
            {
                return (await CommandHelper(_repoPath, "git", new[] {"status"})).ExitCode == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task<bool> GitFetchOrigin(CancellationToken cancel = default)
        {
            _logger.LogInformation("Fetching git updates for {Key} from {Repository} branch {Branch}.",
                _serverInstance.Key,
                _baseUrl,
                _branch);

            return (await CommandHelper(_repoPath, "git", new[] {"fetch", _baseUrl, _branch}, cancel)).ExitCode == 0;
        }

        private async Task GitSwitchBranch(CancellationToken cancel = default)
        {
            await CommandHelperChecked($"Failed to switch to branch {_branch}!", _repoPath, "git",
                new[]{"switch", _branch}, cancel);
        }

        private async Task GitCheckedSubmoduleUpdate(CancellationToken cancel = default)
        {
            _logger.LogInformation("Updating git submodules for {Key}.", _serverInstance.Key);

            await CommandHelperChecked("Failed submodule update!", _repoPath, "git", new[] {"submodule", "update", "--init", "--depth=1", "--recursive"}, cancel);
        }

        private async Task GitResetToFetchHead(CancellationToken cancel = default)
        {
            _logger.LogInformation("Resetting git repository for {Key} to fetched HEAD.", _serverInstance.Key);

            await CommandHelperChecked("Failed reset to fetch-head", _repoPath, "git", new[] {"reset", "--hard", "FETCH_HEAD"}, cancel);
        }

        private async Task TryClone(CancellationToken cancel = default)
        {
            _logger.LogInformation(
                "Cloning git repository for {Key} from {Repository} branch {Branch} into {Path}.",
                _serverInstance.Key,
                _baseUrl,
                _branch,
                _repoPath);

            if (Directory.Exists(_repoPath))
                await DeleteDirectoryWithRetry(_repoPath, cancel);

            try
            {
                // NOTE: These are expected to prepare everything including submodules,
                // because this is used for orbital nuking in the event of an update issue.
                // The --depth=1 is a performance cheat. Works though.
                await CommandHelperChecked("Failed initial clone!", "", "git", new[] {"clone", "--depth=1", "--branch", _branch, _baseUrl, _repoPath}, cancel);
                await GitFetchOrigin(cancel);
                await GitResetToFetchHead(cancel);
                await GitCheckedSubmoduleUpdate(cancel);

                _logger.LogInformation("Finished cloning git repository for {Key}.", _serverInstance.Key);
            }
            catch (Exception)
            {
                if (Directory.Exists(_repoPath))
                    await DeleteDirectoryWithRetry(_repoPath, cancel);
                throw;
            }
        }

        private async Task DeleteDirectoryWithRetry(string path, CancellationToken cancel)
        {
            const int attempts = 5;

            for (var i = 1; i <= attempts; i++)
            {
                try
                {
                    Directory.Delete(path, true);
                    return;
                }
                catch (Exception e) when (i < attempts && e is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(
                        e,
                        "Failed to delete git source directory {Path} on attempt {Attempt}/{Attempts}; retrying.",
                        path,
                        i,
                        attempts);

                    await Task.Delay(TimeSpan.FromMilliseconds(500 * i), cancel);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    _logger.LogError(
                        e,
                        "Failed to delete git source directory {Path}. A previous git process may still have files open.",
                        path);
                    throw;
                }
            }
        }

        private async Task<string?> GitHead(string head)
        {
            try
            {
                return (await CommandHelperCheckedStdout("", _repoPath, "git", new[] {"rev-parse", head})).Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }

        public override async Task<bool> TryWriteFirstRunConfigAsync(string configFile, CancellationToken cancel = default)
        {
            var sourceConfig = GetFirstRunConfigTemplatePath();
            if (sourceConfig == null)
            {
                _logger.LogInformation(
                    "No git repository server_config.toml template found for {Key}; creating an empty config.toml instead.",
                    _serverInstance.Key);
                return false;
            }

            _logger.LogInformation(
                "Creating first-run config for {Key} from git repository template {SourceConfig}.",
                _serverInstance.Key,
                sourceConfig);

            await using var source = File.OpenRead(sourceConfig);
            await using var destination = File.Create(configFile);
            await source.CopyToAsync(destination, cancel);
            return true;
        }

        private string? GetFirstRunConfigTemplatePath()
        {
            var candidates = new[]
            {
                Path.Combine(_repoPath, "Content.Server", "server_config.toml"),
                Path.Combine(_repoPath, "bin", "Content.Server", "server_config.toml")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        // Updater and checker

        public override async Task<bool> CheckForUpdateAsync(string? currentVersion, CancellationToken cancel = default)
        {
            if ((!(await GitCheckRepositoryValid())) || currentVersion == null)
                return true;

            var update = false;

            if (!await GitFetchOrigin(cancel))
            {
                // Maybe the server's not up right now.
                return false;
            }

            var head = await GitHead("HEAD");
            var fetchHead = await GitHead("FETCH_HEAD");
            if (head != fetchHead || currentVersion != fetchHead)
            {
                update = true;
            }

            _logger.LogInformation(
                "Git update check for {Key}: head={Head}, fetchHead={FetchHead}, updating={Update}",
                _serverInstance.Key,
                head ?? "No head",
                fetchHead ?? "No fetch-head",
                update);
            return update;
        }

        public override async Task<string?> RunUpdateAsync(string? currentVersion, string binPath, CancellationToken cancel = default)
        {
            try
            {
                var isFresh = false;
                if (!await GitCheckRepositoryValid() || currentVersion == null)
                {
                    await TryClone(cancel);
                    isFresh = true;
                }

                _logger.LogTrace("Updating...");

                // NOTE: A race condition could happen here if an update check is performed while we're running an update.
                // The solution is that the update check solely occurs on FETCH_HEAD.
                // Therefore, the `git reset --hard FETCH_HEAD` is assumed to either provide one consistent HEAD or error.

                if (!isFresh)
                {
                    try
                    {
                        if (!(await GitFetchOrigin(cancel)))
                            throw new Exception("Could not fetch origin");
                        var fetchedHead = await GitHead("FETCH_HEAD");
                        _logger.LogInformation(
                            "Fetched git branch {Branch} for {Key} at {FetchedHead}.",
                            _branch,
                            _serverInstance.Key,
                            fetchedHead ?? "No fetch-head");
                        await GitResetToFetchHead(cancel);
                        await GitCheckedSubmoduleUpdate(cancel);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to update git repository, recloning to recover.");
                        await TryClone(cancel);
                    }
                }

                var actualConfirmedHead = await GitHead("HEAD");
                if (actualConfirmedHead == null)
                    throw new Exception("Head disappeared!");

                _logger.LogDebug("Git update moved from {CurrentVersion} to {NewVersion}",
                    currentVersion,
                    actualConfirmedHead);

                // Now we build and package it.

                // Platform to build the server for.

                // Where the server zip will be created by the server build script.
                var serverPackage = Path.Combine(_repoPath, "release", ServerZipName);
                var serverPlatform = GetHostSS14RID();

                // check for the new packaging system, else it will fallback to the old python one
                if (Directory.Exists(Path.Combine(_repoPath, "Content.Packaging")))
                {
                    _newPackaging = true;

                    await CommandHelperChecked("Failed to dotnet restore", _repoPath, "dotnet", new[] { "restore" }, cancel);

                    await CommandHelperChecked("Failed to build Content Packaging",
                        _repoPath, "dotnet", new[] { "build", "Content.Packaging","--configuration", "Release", "--no-restore", "/m" }, cancel);
                }
                else
                    _newPackaging = false;

                if (_hybridACZ)
                {
                    if (_newPackaging)
                    {
                        await CommandHelperChecked("Failed to build Hybrid ACZ package with Content Packaging",
                            _repoPath, "dotnet", new[] { "run", "--project", "Content.Packaging", "server", "--platform", serverPlatform, "--hybrid-acz" }, cancel);
                    }
                    else
                        await CommandHelperChecked("Failed to build Hybrid ACZ package with Python", _repoPath, "python", new[] {"Tools/package_server_build.py", "--hybrid-acz", "-p", serverPlatform}, cancel);
                }
                else
                {
                    var binariesPath = Path.Combine(_serverInstance.InstanceDir, "binaries");

                    // If you get an error here: You need a BaseUrl in the root of appsettings.yml that represents the public URL of the watchdog server.
                    var binariesRoot = new Uri(new Uri(_configuration["BaseUrl"]!),
                        $"instances/{_serverInstance.Key}/binaries/");

                    _logger.LogTrace("Building server packages...");

                    if (_newPackaging)
                    {
                        await CommandHelperChecked("Failed to build server packages with Content Packaging",
                            _repoPath, "dotnet", new[] { "run", "--project", "Content.Packaging", "server", "--platform", serverPlatform}, cancel);
                    }
                    else
                        await CommandHelperChecked("Failed to build server packages with Python", _repoPath, "python", new[] {"Tools/package_server_build.py", "-p", serverPlatform}, cancel);


                    _logger.LogTrace("Building client packages...");

                    if (_newPackaging)
                    {
                        await CommandHelperChecked("Failed to build client packages with Content Packaging",
                            _repoPath, "dotnet", new[] { "run", "--project", "Content.Packaging", "client", "--no-wipe-release"}, cancel);
                    }
                    else
                        await CommandHelperChecked("Failed to build client packages", _repoPath, "python", new[] {"Tools/package_client_build.py"}, cancel);

                    File.Move(Path.Combine(_repoPath, "release", ClientZipName), Path.Combine(binariesPath, ClientZipName), true);
                    // Unless using Hybrid ACZ, a build.json file must be written.
                    await using (var stream = File.Open(serverPackage, FileMode.Open))
                    {
                        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
                        {
                            var build = new Build()
                            {
                                Download = new Uri(binariesRoot, ClientZipName).ToString(),
                                Hash = GetFileHash(Path.Combine(binariesPath, ClientZipName)),
                                Version = actualConfirmedHead,
                                // Use ACZ version auto-detection.
                                EngineVersion = "",
                                ForkId = _baseUrl,
                            };
                            ZipArchiveEntry readmeEntry = archive.CreateEntry("build.json");
                            await using (StreamWriter writer = new StreamWriter(readmeEntry.Open()))
                            {
                                await writer.WriteLineAsync(JsonSerializer.Serialize(build));
                            }
                        }
                    }
                }

                _logger.LogTrace("Applying server update.");

                if (Directory.Exists(binPath))
                {
                    Directory.Delete(binPath, true);
                }

                Directory.CreateDirectory(binPath);

                _logger.LogTrace("Extracting zip file");

                // Actually extract.
                await using (var stream = File.Open(serverPackage, FileMode.Open))
                {
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                    {
                        archive.ExtractToDirectory(binPath);
                    }
                }

                // Remove the package now that it's extracted.
                File.Delete(serverPackage);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // chmod +x Robust.Server

                    var rsPath = Path.Combine(binPath, "Robust.Server");
                    if (File.Exists(rsPath))
                    {
                        var f = new UnixFileInfo(rsPath);
                        f.FileAccessPermissions |=
                            FileAccessPermissions.UserExecute | FileAccessPermissions.GroupExecute |
                            FileAccessPermissions.OtherExecute;
                    }
                }

                // ReSharper disable once RedundantTypeArgumentsOfMethod
                return actualConfirmedHead;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to run update!");

                return null;
            }
        }

        private sealed record CommandResult(int ExitCode, string Stdout, string Stderr);

        private class Build
        {
            [JsonPropertyName("download")]
            public string Download { get; set; } = default!;

            [JsonPropertyName("hash")]
            public string Hash { get; set; } = default!;

            [JsonPropertyName("version")]
            public string Version { get; set; } = default!;

            [JsonPropertyName("engine_version")]
            public string EngineVersion { get; set; } = default!;

            [JsonPropertyName("fork_id")]
            public string ForkId { get; set; } = default!;
        }
    }
}
