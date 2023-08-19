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
			SetupSentry(builder);

			builder.Services.AddHttpClient("GivEnergy", x =>
			{
				x.BaseAddress = new Uri(GetEnvironmentVariable("BaseUrl"));
				x.DefaultRequestHeaders.Authorization =
					new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GetEnvironmentVariable("ApiKey"));
			});
			
			builder.Services.AddSingleton<GivEnergyService>();
		}

		public static bool UseSentryCron => EnvironmentVariableExists("SENTRY_CRON");
		private static bool EnvironmentVariableExists(string name)
		{
			return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process));
		}

		private static string GetEnvironmentVariable(string name)
		{
			return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process)
			       ?? throw new ArgumentException($"The environment variable '{name}' is null or does not exist.",
				       nameof(name));
		}

		private static void SetupSentry(IFunctionsHostBuilder builder)
		{
			// if sentry isn't configured then we don't set it up.
			if (!EnvironmentVariableExists("SENTRY_DSN"))
			{
				return;
			}

			SentrySdk.Init(options =>
			{
				options.Dsn = GetEnvironmentVariable("SENTRY_DSN");

				options.AutoSessionTracking = true;
				options.EnableTracing = true;
				options.Environment = GetEnvironmentVariable("SENTRY_ENVIRONMENT");
				options.Release = GetEnvironmentVariable("SENTRY_RELEASE");
			});

			if (!UseSentryCron)
			{
				return;
			}

			builder.Services.AddHttpClient("SentryCron", x =>
			{
				x.BaseAddress = new Uri(GetEnvironmentVariable("SENTRY_CRON"));
			});
		}
	}
}