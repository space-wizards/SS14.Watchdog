using System.IO;
using System.Runtime;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Serilog;
using SS14.Watchdog.Components.BackgroundTasks;
using SS14.Watchdog.Components.ServerManagement;
using SS14.Watchdog.Configuration;

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
            services.Configure<ServersConfiguration>(Configuration.GetSection("Servers"));

            services.AddControllers();

            services.AddSingleton<ServerManager>();
            services.AddSingleton<IServerManager>(p => p.GetService<ServerManager>()!);
            services.AddHostedService(p => p.GetService<ServerManager>());

            services.AddSingleton<BackgroundTaskQueue>();
            services.AddSingleton<IBackgroundTaskQueue>(p => p.GetService<BackgroundTaskQueue>()!);
            services.AddHostedService(p => p.GetService<BackgroundTaskQueue>());
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
                }
            }

            app.UseRouting();

            app.UseAuthentication();

            app.UseAuthorization();

            app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
        }
    }
}
