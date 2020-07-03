using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModUpdater.Curse;
using ModUpdater.Masady;
using Serilog;
using Serilog.Events;

namespace ModUpdater {
    class Program {

        static async Task Main(string[] args) {
            await CreateHostBuilder(args).RunConsoleAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .ConfigureHostConfiguration(x => {
                x.AddJsonFile("appsettings.json", optional : false);
            })
            .UseSerilog((hostingContext, loggerConfiguration) => loggerConfiguration
                .ReadFrom.Configuration(hostingContext.Configuration)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console())
            .ConfigureAppConfiguration(x => { x.AddJsonFile("appsettings.logging.json", optional : true); })
            .ConfigureServices((hostContext, services) => {
                services.AddTransient<HttpClientLoggingHandler>();
                services.AddHttpClient<GithubClient>()
                    .AddHttpMessageHandler<HttpClientLoggingHandler>();
                services.AddHttpClient<CurseClient>()
                    .AddHttpMessageHandler<HttpClientLoggingHandler>();
                services.AddHttpClient<MasadyClient>()
                    .AddHttpMessageHandler<HttpClientLoggingHandler>();

                services.AddHostedService<ModUpdaterService>();
                services.AddSingleton(LoadSettingsWithCustomSerializer(hostContext));
                services.Configure<ConsoleLifetimeOptions>(opts => opts.SuppressStatusMessages = true);
                services.AddLazyCache();
            })
            .UseConsoleLifetime();

        private static Settings LoadSettingsWithCustomSerializer(HostBuilderContext hostContext) {
            var appsettingsFile = hostContext.HostingEnvironment.ContentRootFileProvider.GetFileInfo("appsettings.json");
            var appSettingsJson = File.ReadAllText(appsettingsFile.PhysicalPath);
            var serializeOptions = new JsonSerializerOptions();
            serializeOptions.Converters.Add(new ProviderConverter());
            return JsonSerializer.Deserialize<Settings>(appSettingsJson, serializeOptions);
        }
    }
}