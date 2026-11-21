using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace NoAv1Plugin.Api
{
    /// <summary>
    /// Registers <see cref="NoAv1PlaybackInfoFilter"/> as a global MVC action filter.
    /// </summary>
    /// <remarks>
    /// This runs during ApplicationHost.Init, before the host's ServiceProvider is built, into
    /// the same IServiceCollection Startup.ConfigureServices populates -- so a plain
    /// services.Configure&lt;MvcOptions&gt;() call here is picked up by MVC's options pipeline
    /// exactly like one registered by core startup code, regardless of registration order. This
    /// is the only extensibility point a plugin gets into the HTTP pipeline; there's no
    /// middleware/IApplicationBuilder hook available to plugins.
    /// </remarks>
    public class NoAv1ServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.Configure<MvcOptions>(options =>
            {
                options.Filters.Add<NoAv1PlaybackInfoFilter>();
            });
        }
    }
}
