using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace net.melodiouscode.GivEnergy.Func.Data
{
	internal class SettingReadResult
	{
		[JsonPropertyName("data")] public DataPayload? Data { get; set; }

		public class DataPayload
		{
			[JsonPropertyName("value")] public object? Value { get; set; }
		}
	}
}