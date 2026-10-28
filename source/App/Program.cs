using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using System;
using System.IO;
using System.Threading;

namespace App
{
    public sealed class Program
    {
        // The folder of published client releases: the configured one, else dist/client in the
        // nearest folder above the server that has one. Made if it's configured and missing.
        private static string FindClientFolder(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var full = Path.GetFullPath(configured);
                Directory.CreateDirectory(full);
                return full;
            }
            for (var dir = new DirectoryInfo(System.AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "dist", "client");
                if (Directory.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        // Requests wait for Redis synchronously, each holding a thread-pool thread, and Redis's
        // replies need a free pool thread to be read. With few threads (the pool starts with one per
        // CPU and adds about one a second) a burst of sign-ins took them all, and the server stopped
        // until the pool grew: everyone signing in again after a restart froze sign-ins for minutes.
        // So only BusyRequests requests run at once, the rest wait without holding a thread, and the
        // pool starts big enough to keep threads free beside them.
        private const int BusyRequests = 16;
        private const int MinPoolThreads = 64;

        public static void Main(string[] args)
        {
            ThreadPool.GetMinThreads(out var workers, out var io);
            ThreadPool.SetMinThreads(Math.Max(workers, MinPoolThreads), io);

            var builder = WebApplication.CreateBuilder(args);
            var service = builder.Services;

            service.AddSingleton<CoreService>();
            service.AddControllers();

#if DEBUG
            service.AddEndpointsApiExplorer();
            service.AddSwaggerGen();
#endif

            var app = builder.Build();

            var core = app.Services.GetService<CoreService>();

            app.Urls.Clear();
            var address = core.Config.serverInfo.bindAddress;

            app.Urls.Add($"http://{address}:{core.Config.serverInfo.port}");
            
            if (!core.IsProduction())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseStaticFiles(new StaticFileOptions()
            {
                FileProvider = new PhysicalFileProvider($"{core.Resources.ResourcePath}/web/sfx"),
                RequestPath = "/sfx"
            });

            app.UseStaticFiles(new StaticFileOptions()
            {
                FileProvider = new PhysicalFileProvider($"{core.Resources.ResourcePath}/web/music"),
                RequestPath = "/music"
            });

            // The published game client, for players' clients to update from (see
            // client-rs/app/src/update.rs).
            var clientFolder = FindClientFolder(core.Config.serverSettings.clientFolder);
            if (clientFolder != null)
            {
                var types = new FileExtensionContentTypeProvider();
                types.Mappings[".sig"] = "text/plain";
                types.Mappings[".gz"] = "application/octet-stream";
                app.UseStaticFiles(new StaticFileOptions()
                {
                    FileProvider = new PhysicalFileProvider(clientFolder),
                    RequestPath = "/client",
                    ContentTypeProvider = types,
                    // Each blob's name is its content's hash, so it never changes; the lists must
                    // always be fetched fresh.
                    OnPrepareResponse = ctx =>
                    {
                        var blob = ctx.File.Name.EndsWith(".gz");
                        ctx.Context.Response.Headers["Cache-Control"] = blob ? "public, max-age=31536000, immutable" : "no-cache";
                    }
                });
                core.Logger.Info($"Serving the game client's updates from {clientFolder}");
            }

            var busy = new SemaphoreSlim(BusyRequests);
            app.Use(async (context, next) =>
            {
                try
                {
                    await busy.WaitAsync(context.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    return; // the client gave up waiting (it retries with a new request)
                }
                try
                {
                    await next();
                }
                finally
                {
                    busy.Release();
                }
            });

            //app.UseHttpsRedirection();
            app.UseRouting();
            app.UseAuthorization();
            app.MapControllers(); //app.MapEndpoints(endpoints => endpoints.MapControllers());

            app.Use(async (context, next) =>
            {
                core.Logger.Info($"Request: \"{context.Request.Path}\" from: {context.Connection.RemoteIpAddress}");
                await next();
            });
            
            app.UseMiddleware<ExceptionMiddleware>();

            app.Run();
        }
    }
}