using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Loki;
using Serilog.Sinks.Loki.Labels;

namespace SS14.Watchdog
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, builder)=>
                {
                    var env = context.HostingEnvironment;
                    builder.AddYamlFile("appsettings.yml", false);
                    builder.AddYamlFile($"appsettings.{env.EnvironmentName}.yml", true);
                    builder.AddYamlFile("appsettings.Secret.yml", true);
                })
                .UseSerilog((ctx, cfg) =>
                {
                    cfg.ReadFrom.Configuration(ctx.Configuration);
                    cfg.Enrich.FromLogContext();

                    SetupLoki(cfg, ctx.Configuration);
                    SetupInstanceFileLogging(cfg);
                })
                .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>());

        private static void SetupInstanceFileLogging(LoggerConfiguration log)
        {
            log.WriteTo.Map(
                "Instance",
                "(watchdog)",
                (instance, writeTo) => writeTo.File(
                    Path.Combine("logs", SanitizePathSegment(instance), "watchdog-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    restrictedToMinimumLevel: LogEventLevel.Debug,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3} {SourceContext}] {Message:lj}{NewLine}{Exception}"),
                sinkMapCountLimit: 64);
        }

        private static string SanitizePathSegment(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(value.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        }

        private static void SetupLoki(LoggerConfiguration log, IConfiguration cfg)
        {
            var dat = cfg.GetSection("Serilog:Loki").Get<LokiConfigurationData>();

            if (dat == null)
                return;

            LokiCredentials credentials;
            if (string.IsNullOrWhiteSpace(dat.Username))
            {
                credentials = new NoAuthCredentials(dat.Address);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(dat.Password))
                {
                    throw new InvalidDataException("No password specified.");
                }

                credentials = new BasicAuthCredentials(dat.Address, dat.Username, dat.Password);
            }

            log.WriteTo.LokiHttp(credentials, new DefaultLogLabelProvider(new[]
            {
                new LokiLabel("App", "SS14.Watchdog"),
                new LokiLabel("Server", dat.Name)
            }));
        }

        private sealed class LokiConfigurationData
        {
            public string? Address { get; set; }
            public string? Name { get; set; }
            public string? Username { get; set; }
            public string? Password { get; set; }
        }
    }
}
