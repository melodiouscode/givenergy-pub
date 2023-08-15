using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace net.melodiouscode.GivEnergy.Func.Data
{
	public class SettingsList
	{
		[JsonPropertyName("data")] public Datum[] Data { get; set; } = Array.Empty<Datum>();

		public Datum GetId(string name)
		{
			return Data.FirstOrDefault(d => d.Name == name) ??
			       throw new ArgumentException($"No setting with name {name} found");
		}
	}


	public class Datum
	{
		[JsonPropertyName("id")] public int Id { get; set; }
		[JsonPropertyName("name")] public string Name { get; set; } = "";
		[JsonPropertyName("validation")] public string Validation { get; set; } = "";
		[JsonPropertyName("validation_rules")] public string[] ValidationRules { get; set; } = Array.Empty<string>();

		public void Validate(object value)
		{
			if (!ValidationRules.Any())
			{
				return;
			}

			if (ValidationRules.Contains("boolean") && bool.TryParse(value.ToString(), out _))
			{
				return;
			}

			if (ValidationRules.Contains("date_format:H:i") && TimeOnly.TryParse(value.ToString(), out _))
			{
				return;
			}

			if (ValidationRules.Any(x=>x.StartsWith("between:")) && int.TryParse(value.ToString(), out int i))
			{
				var between = ValidationRules.First(x => x.StartsWith("between:"));
				var min = int.Parse(between.Split(":")[1].Split(",")[0]);
				var max = int.Parse(between.Split(":")[1].Split(",")[1]);

				if (i >= min && i <= max)
				{
					return;
				}
			}
			
			var ex = new InvalidOperationException(Validation);
			ex.Data.Add("ValidationRules", ValidationRules);
			ex.Data.Add("Value", value);
			ex.Data.Add("Name", Name);

			throw ex;
		}
	}
}