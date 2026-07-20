using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Serilog;
using SS14.Watchdog.Components.BackgroundTasks;
using SS14.Watchdog.Components.DataManagement;
using SS14.Watchdog.Components.Notifications;
using SS14.Watchdog.Components.ProcessManagement;
using SS14.Watchdog.Components.ServerManagement;
using SS14.Watchdog.Configuration;
using SS14.Watchdog.Controllers;

namespace SS14.Watchdog
{
    public sealed class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        [UsedImplicitly]
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<DataOptions>(Configuration.GetSection(DataOptions.Position));
            services.Configure<NotificationOptions>(Configuration.GetSection(NotificationOptions.Position));
            services.Configure<ServersConfiguration>(Configuration.GetSection("Servers"));

            services.AddSingleton<DataManager>();
            services.AddHostedService(p => p.GetService<DataManager>()!);

            var processOptions = new ProcessOptions();
            var processSection = Configuration.GetSection(ProcessOptions.Position);
            processSection.Bind(processOptions);
            switch (processOptions.Mode)
            {
                case ProcessMode.Basic:
                    services.Configure<ProcessOptions>(processSection);
                    services.AddSingleton<IProcessManager, ProcessManagerBasic>();
                    break;
                case ProcessMode.Systemd:
                    services.Configure<SystemdProcessOptions>(processSection);
                    services.AddSingleton<IProcessManager, ProcessManagerSystemd>();
                    services.AddHostedService(p => (ProcessManagerSystemd)p.GetService<IProcessManager>()!);
                    break;
                default:
                    throw new ArgumentOutOfRangeException($"Invalid process mode: {processOptions.Mode}");
            }

            services.AddAuthentication("BasicAuthentication")
                .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("BasicAuthentication", null);

            services.AddAuthorization(options =>
            {
                options.AddPolicy("BasicAuthentication", policy => policy.RequireAuthenticatedUser());
            });

            services.AddControllers();
            services.AddOpenApi(options =>
            {
                options.AddDocumentTransformer((document, context, cancellationToken) =>
                {
                    document.Components ??= new OpenApiComponents();
                    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                    document.Components.SecuritySchemes["basicAuth"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "basic",
                        Description = "Set the Authorization header to 'Basic <token>' using the raw base64-encoded token value."
                    };

                    return Task.FromResult(document);
                });

                options.AddOperationTransformer((operation, context, cancellationToken) =>
                {
                    var endpoint = context.Description.ActionDescriptor.EndpointMetadata
                        .OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                        .FirstOrDefault(x => x.Policy == "BasicAuthentication");

                    if (endpoint == null)
                    {
                        return Task.FromResult(operation);
                    }

                    operation.Security = new List<OpenApiSecurityRequirement>
                    {
                        new()
                        {
                            [new OpenApiSecuritySchemeReference("basicAuth", context.Document, "SecurityScheme")] = new List<string>()
                        }
                    };

                    return Task.FromResult(operation);
                });
            });

            services.AddSingleton<ServerManager>();
            services.AddSingleton<IServerManager>(p => p.GetService<ServerManager>()!);
            services.AddHostedService(p => p.GetService<ServerManager>()!);

            services.AddSingleton<BackgroundTaskQueue>();
            services.AddSingleton<IBackgroundTaskQueue>(p => p.GetService<BackgroundTaskQueue>()!);
            services.AddHostedService(p => p.GetService<BackgroundTaskQueue>()!);

            services.AddSingleton<NotificationManager>();

            services.AddHttpClient(NotificationManager.DiscordHttpClient);

            // CORS
            var allowedOrigins = Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? Array.Empty<string>();

            services.AddCors(options =>
            {
                options.AddPolicy("ConfiguredCors", builder =>
                {
                    if (allowedOrigins.Length > 0)
                    {
                        builder
                            .WithOrigins(allowedOrigins)
                            .AllowAnyHeader()
                            .AllowAnyMethod();
                    }
                });
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        [UsedImplicitly]
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseSerilogRequestLogging();

            Log.ForContext<Startup>().Debug($"Using server GC: {GCSettings.IsServerGC}");

            // app.UseHttpsRedirection();

            // Mount binaries/ paths for all the server instances.
            var serverManager = app.ApplicationServices.GetRequiredService<IServerManager>();
            foreach (var instance in serverManager.Instances)
            {
                var dirPath = Path.Combine(instance.InstanceDir, "binaries");

                if (!Directory.Exists(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }

                var provider = new PhysicalFileProvider(dirPath);
                var path = $"/instances/{instance.Key}/binaries";

                app.UseStaticFiles(new StaticFileOptions
                {
                    RequestPath = path,
                    FileProvider = provider
                });

                if (env.IsDevelopment())
                {
                    app.UseDirectoryBrowser(new DirectoryBrowserOptions
                    {
                        FileProvider = provider,
                        RequestPath = path
                    });

                    app.UseSwaggerUI(options =>
                    {
                        options.SwaggerEndpoint("/openapi/v1.json", "v1");
                    });
                }
            }

            app.UseRouting();

            app.UseCors("ConfiguredCors");

            app.UseAuthentication();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapOpenApi("/openapi/{documentName}.json");
                endpoints.MapControllers();
            });
        }
    }
}
