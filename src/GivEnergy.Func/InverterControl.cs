using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using net.melodiouscode.GivEnergy.Func.Data;
using NodaTime;
using NodaTime.Extensions;

namespace net.melodiouscode.GivEnergy.Func
{
	[StorageAccount("AzureWebJobsStorage")]
	public class InverterControl
	{
		private readonly GivEnergyService _service;
		private readonly JsonSerializerOptions _serializerOptions;
		private readonly HttpClient? _sentryCronClient;

		public InverterControl(GivEnergyService service, IHttpClientFactory factory)
		{
			_service = service;

			if (Startup.UseSentryCron)
			{
				_sentryCronClient = factory.CreateClient("SentryCron");
			}

			_serializerOptions = new JsonSerializerOptions()
			{
				AllowTrailingCommas = true,
				Converters = { new JsonStringEnumConverter() }
			};
		}

		[FunctionName(nameof(InverterControl))]
		public async Task Run([TimerTrigger("0 * * * * *")] TimerInfo myTimer,
			[Blob("storage/current.json", FileAccess.Read)]
			BlobClient readCurrent,
			[Blob("storage/current.json", FileAccess.Write)]
			BlobClient writeCurrent,
			[Blob("storage/schedule.json", FileAccess.Read)]
			BlobClient scheduleClient,
			ILogger log)
		{
			if (myTimer.IsPastDue)
			{
				log.LogInformation("Timer is past due, aborting.");
				return;
			}

			try
			{
				await MarkStarted();

				await using var scheduleStream = await scheduleClient.OpenReadAsync();

				var schedule =
					JsonSerializer.Deserialize<ActionPayload>(await new StreamReader(scheduleStream).ReadToEndAsync(),
						_serializerOptions)
					?? throw new InvalidOperationException("The schedule is empty or missing");

				var zonedDateTime = SystemClock.Instance
					.InZone(NodaTime.TimeZones.TzdbDateTimeZoneSource.Default.ForId(schedule.TimeZone))
					.GetCurrentZonedDateTime();

				var nextAction = schedule.GetCurrentState(zonedDateTime.TimeOfDay.ToTimeOnly());

				if (nextAction == null)
				{
					log.LogInformation($"No action to perform for inverter {schedule.Inverter}.");
					return;
				}

				ActionPayload.ActionEvent? currentAction = null;
				if (await readCurrent.ExistsAsync())
				{
					await using var stream = await readCurrent.OpenReadAsync();

					currentAction = JsonSerializer.Deserialize<ActionPayload.ActionEvent>(
						await new StreamReader(stream).ReadToEndAsync(), _serializerOptions);
				}

				if (currentAction == null || nextAction.Id != currentAction.Id)
				{
					log.LogInformation($"Changing from {currentAction} to {nextAction}.");

					await ExecuteAction(schedule.Inverter, nextAction);

					await WriteCurrentAction(writeCurrent, nextAction);
				}
				else
				{
					log.LogInformation($"Staying with {currentAction}.");
				}

				await MarkCompleted();
			}
			catch (Exception e)
			{
				log.LogCritical(e, e.Message);

				await MarkError();

				throw;
			}
		}

		private Task MarkStarted()
		{
			return _sentryCronClient?.GetAsync("?status=in_progress") ?? Task.CompletedTask;
		}

		private Task MarkCompleted()
		{
			return _sentryCronClient?.GetAsync("?status=ok") ?? Task.CompletedTask;
		}

		private Task MarkError()
		{
			return _sentryCronClient?.GetAsync("?status=error") ?? Task.CompletedTask;
		}

		private Task ExecuteAction(string inverter, ActionPayload.ActionEvent action)
		{
			return action.Status switch
			{
				ActionPayload.InverterStatus.EcoMode => _service.SetEcoMode(inverter, true),
				ActionPayload.InverterStatus.TimedCharge => _service.EnableTimedCharge(inverter, action.StartTime,
					action.EndTime, action.UntilPercentage ?? 100),
				ActionPayload.InverterStatus.TimedDischarge => _service.EnableTimedDischarge(inverter,
					action.StartTime, action.EndTime, action.UntilPercentage ?? 0),
				ActionPayload.InverterStatus.TimedExport => _service.EnableTimedExport(inverter, action.StartTime,
					action.EndTime, action.UntilPercentage ?? 0),
				_ => throw new ArgumentOutOfRangeException(nameof(action), action.Status,
					"Unexpected action status requested.")
			};
		}

		private async Task WriteCurrentAction(BlobClient writeCurrent, ActionPayload.ActionEvent nextAction)
		{
			var s = JsonSerializer.Serialize(nextAction, _serializerOptions);

			await writeCurrent.UploadAsync(new BinaryData(s), true);
		}
	}
}