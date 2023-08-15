using net.melodiouscode.GivEnergy.Func.Data;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace net.melodiouscode.GivEnergy.Func
{
	public class GivEnergyService
	{
		private readonly HttpClient _client;

		public GivEnergyService(IHttpClientFactory factory)
		{
			_client = factory.CreateClient("GivEnergy");
		}

		private readonly Dictionary<string, SettingsList> _knownSettings = new();

		private async Task<SettingsList> GetSettingsList(string inverter)
		{
			if (_knownSettings.TryGetValue(inverter, out var list))
			{
				return list;
			}

			var settingsResult = await _client.GetAsync(new Uri($"inverter/{inverter}/settings", UriKind.Relative));

			if (!settingsResult.IsSuccessStatusCode)
			{
				throw new InvalidOperationException(
					$"Failed to get settings for inverter {inverter}. Response: {await settingsResult.Content.ReadAsStringAsync()}");
			}

			var settingsList = await settingsResult.Content.ReadFromJsonAsync<SettingsList>()
			                   ?? throw new InvalidOperationException(
				                   $"Failed to get settings for inverter {inverter}. Response: {await settingsResult.Content.ReadAsStringAsync()}");

			_knownSettings.Add(inverter, settingsList);

			return settingsList;
		}

		public Task SetDischargeDepth(string inverter, int depth)
		{
			return WriteSetting(inverter, "Battery Reserve % Limit", depth);
		}

		public  Task SetChargeHeight(string inverter, int height)
		{
			return WriteSetting(inverter, "AC Charge Upper % Limit", height);
		}
		
		public async Task SetEcoMode(string inverter, bool state, int? dischargeDepth = null)
		{
			await DisableTimedEvents(inverter);

			if (state)
			{
				dischargeDepth ??= await GetDefaultDischargeDepth(inverter);

				await EnableEcoMode(inverter);
				await SetDischargeDepth(inverter, dischargeDepth.Value);
			}
			else
			{
				await DisableEcoMode(inverter);
			}
		}

		private async Task DisableEcoMode(string inverter)
		{
			await WriteSetting(inverter, "Enable Eco Mode", false);
		}

		private async Task WriteSetting(string inverter, string setting, object value)
		{
			var retryCount = 0;

			do
			{
				var settingsList = await GetSettingsList(inverter);

				var datum = settingsList.GetId(setting);

				datum.Validate(value);

				var payload = new
				{
					value
				};

				var result =
					await _client.PostAsJsonAsync(
						new Uri($"inverter/{inverter}/settings/{datum.Id}/write", UriKind.Relative), payload);

				if (!result.IsSuccessStatusCode)
				{
					var content = await result.Content.ReadAsStringAsync();

					if (!content.Contains("timeout", StringComparison.InvariantCultureIgnoreCase))
					{
						throw new InvalidOperationException(
							$"Failed to write setting {setting} to {value} for inverter {inverter}. Response: {content}");
					}

					if (retryCount >= 3)
					{
						throw new InvalidOperationException(
							$"Failed to write setting {setting} to {value} for inverter {inverter}. Response: {content}");
					}
				}
				else
				{
					var givResult = await result.Content.ReadFromJsonAsync<SettingWriteResult>();

					if (givResult?.Data?.Success != true)
					{
						if (givResult?.Data?.Message.Contains("timeout", StringComparison.InvariantCultureIgnoreCase) == false)
						{
							throw new InvalidOperationException(
																$"Failed to write setting {setting} to {value} for inverter {inverter}. Response: {givResult?.Data?.Message ?? "Unknown Error"}");
						}

						if (retryCount >= 3)
						{
							throw new InvalidOperationException(
								$"Failed to write setting {setting} to {value} for inverter {inverter}. Response: {givResult?.Data?.Message ?? "Unknown Error"}");
						}
					}

					return;
				}
				
				retryCount++;

				await Task.Delay(2000);
			} while (retryCount <= 3);
		}

		public async Task EnableTimedCharge(string inverter, string startTime, string endTime, int untilPercentage)
		{
			await DisableTimedEvents(inverter);

			await WriteSetting(inverter, "AC Charge 1 Start Time", startTime);
			await WriteSetting(inverter, "AC Charge 1 End Time", endTime);
			
			await SetChargeHeight(inverter, untilPercentage);

			await WriteSetting(inverter, "AC Charge Enable", true);

			await EnableEcoMode(inverter);
		}

		public async Task DisableTimedEvents(string inverter)
		{
			await DisableTimedExport(inverter);
			await DisableTimedDischarge(inverter);
			await DisableTimedCharge(inverter);
		}

		private async Task EnableEcoMode(string inverter)
		{
			await WriteSetting(inverter, "Enable Eco Mode", true);
		}

		private async Task DisableTimedDischarge(string inverter)
		{
			await WriteSetting(inverter, "Enable DC Discharge", false);
		}

		private async Task DisableTimedCharge(string inverter)
		{
			await WriteSetting(inverter, "AC Charge Enable", false);
		}

		private async Task DisableTimedExport(string inverter)
		{
			await WriteSetting(inverter, "Enable DC Discharge", false);
		}

		public async Task EnableTimedDischarge(string inverter, string startTime, string endTime,
			int? untilPercentage)
		{
			await DisableTimedEvents(inverter);

			await DisableEcoMode(inverter);

			await WriteSetting(inverter, "DC Discharge 1 Start Time", startTime);
			await WriteSetting(inverter, "DC Discharge 1 End Time", endTime);
			await WriteSetting(inverter, "Enable DC Discharge", true);

			untilPercentage ??= await GetDefaultDischargeDepth(inverter);

			await SetDischargeDepth(inverter, untilPercentage.Value);
		}

		private Task<int> GetDefaultDischargeDepth(string inverter)
		{
			return ReadSetting(inverter, "Battery Cutoff % Limit");
		}

		private async Task<int> ReadSetting(string inverter, string setting)
		{
			var settingsList = await GetSettingsList(inverter);

			var datum = settingsList.GetId(setting);

			var result =
				await _client.PostAsync(new Uri($"inverter/{inverter}/settings/{datum.Id}/read", UriKind.Relative),
					null);

			if (!result.IsSuccessStatusCode)
			{
				var content = await result.Content.ReadAsStringAsync();

				throw new InvalidOperationException(
					$"Failed to read setting {setting} for inverter {inverter}. Response: {content}");
			}

			var givResult = await result.Content.ReadFromJsonAsync<SettingReadResult>()
			                ?? throw new InvalidOperationException(
				                $"Failed to read setting {setting} for inverter {inverter}. Response: {await result.Content.ReadAsStringAsync()}");

			if (int.TryParse(givResult.Data?.Value?.ToString(), out var dataValue))
			{
				return dataValue;
			}

			throw new InvalidOperationException(
				$"Failed to read setting {setting} for inverter {inverter}, value was invalid. Response: {await result.Content.ReadAsStringAsync()}");
		}

		public async Task EnableTimedExport(string inverter, string startTime, string endTime, int? untilPercentage)
		{
			await DisableTimedEvents(inverter);

			await WriteSetting(inverter, "DC Discharge 1 Start Time", startTime);
			await WriteSetting(inverter, "DC Discharge 1 End Time", endTime);
			await WriteSetting(inverter, "Enable DC Discharge", true);

			untilPercentage ??= await ReadSetting(inverter, "Battery Cutoff % Limit");

			await SetDischargeDepth(inverter, untilPercentage.Value);

			await DisableEcoMode(inverter);
		}
	}
}