using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using net.melodiouscode.GivEnergy.Func;
using Sentry;
using System;

[assembly: FunctionsStartup(typeof(Startup))]
namespace net.melodiouscode.GivEnergy.Func
{
	public class Startup : FunctionsStartup
	{
		public override void Configure(IFunctionsHostBuilder builder)
		{
			SetupSentry();

			builder.Services.AddHttpClient("GivEnergy", x => {
				x.BaseAddress = new Uri(GetEnvironmentVariable("BaseUrl"));
				x.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GetEnvironmentVariable("ApiKey"));
			});

			builder.Services.AddSingleton<GivEnergyService>();
		}

		private static string GetEnvironmentVariable(string name)
		{
			return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process)
				?? throw new ArgumentException($"The environment variable '{name}' is null or does not exist.", nameof(name));
		}

		private static void SetupSentry()
		{
			SentrySdk.Init(options =>
			{
				options.Dsn = GetEnvironmentVariable("SENTRY_DSN");

				options.AutoSessionTracking = true;
				options.EnableTracing = true;
			});
		}
	}
}
