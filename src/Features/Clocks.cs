using System.Collections.Generic;

namespace SarkasticQoL.Features
{
	/*
		A sign that shows the in-game day and time (!clock on), rewritten only when the shown
		value changes: with ten-minute steps that is about every twelve real seconds. Any sign
		will do, including one the plugin placed as a label.
	*/
	internal class Clocks : IFeature
	{
		public static readonly int ClockKey = "SarkasticQoL.Clock".GetStableHashCode();

		public World.Kind Kinds => World.Kind.Sign;

		public void Start() { }
		public void Stop() { }

		public static string Now()
		{
			float fraction = EnvMan.instance.GetDayFraction();
			int minutes = (int)(fraction * 24f * 60f);
			minutes -= minutes % QoLPlugin.Settings.ClockStepMinutes.Value;
			return string.Format(QoLPlugin.Settings.ClockFormat.Value, EnvMan.instance.GetDay(), minutes / 60, minutes % 60);
		}

		public void Visit(ZDO zdo, World.Kind kind, List<ZNetPeer> peers)
		{
			if (!QoLPlugin.Settings.ClocksEnabled.Value || !zdo.GetBool(ClockKey) || !EnvMan.instance)
			{
				return;
			}
			string text = Now();
			if (zdo.GetString(ZDOVars.s_text) != text)
			{
				zdo.Set(ZDOVars.s_text, text);
			}
		}
	}
}
