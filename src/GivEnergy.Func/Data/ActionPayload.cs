using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NodaTime;

namespace net.melodiouscode.GivEnergy.Func.Data
{
	internal class ActionPayload
	{
		public enum InverterStatus
		{
			EcoMode,
			TimedCharge,
			TimedDischarge,
			TimedExport
		}

		public List<ActionEvent> Events { get; set; } = new();
		public string Inverter { get; set; } = "";
		public string TimeZone { get; set; } = "Europe/London";
		public int DefaultDischargeDepth { get; set; }
		public int DefaultChargeDepth { get; set; }
		public ActionEvent? GetCurrentState(TimeOnly time)
		{
			var action = Events.SingleOrDefault(x =>
				x.StartTimeOnly.CompareTo(time) <= 0 &&
				x.EndTimeOnly.CompareTo(time) >= 0
			);

			return action;
		}


		public class ActionEvent
		{
			public Guid Id { get; set; }
			public InverterStatus Status { get; set; } = InverterStatus.EcoMode;
			public string StartTime { get; set; } = "";
			[JsonIgnore] public TimeOnly StartTimeOnly => TimeOnly.ParseExact(StartTime, "HH:mm");
			public string EndTime { get; set; } = "";

			[JsonIgnore] public TimeOnly EndTimeOnly => TimeOnly.ParseExact(EndTime, "HH:mm");
			public int? UntilPercentage { get; set; }

			public override string ToString()
			{
				return $"{Status} from {StartTime} to {EndTime}{(UntilPercentage.HasValue ? " Until " + UntilPercentage + "%" : "")}";
			}
		}
	}
}