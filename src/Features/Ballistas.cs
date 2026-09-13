using System.Collections.Generic;

namespace SarkasticQoL.Features
{
	/*
		What a player-built ballista shoots at: Turret.m_targetPlayers and m_targetTamed
		(m_targetTamedConfig is what the game uses instead once the ballista has been given
		target items). The ballista's owner -- the server, with Dedicated Simulation -- picks the
		targets, so a loaded ballista is changed on the spot as well as in its data. A player's
		choice for one ballista (!ballista) is stored in it and wins over the config defaults.
	*/
	internal class Ballistas : IFeature
	{
		public static readonly int PlayersKey = "SarkasticQoL.Ballista.Players".GetStableHashCode();
		public static readonly int TamesKey = "SarkasticQoL.Ballista.Tames".GetStableHashCode();

		public World.Kind Kinds => World.Kind.Turret;

		public void Start() { }
		public void Stop() { }

		// -1 = no choice made, 0 = off, 1 = on
		public static bool Wanted(ZDO zdo, int key, bool configDefault)
		{
			int choice = zdo.GetInt(key, -1);
			return choice < 0 ? configDefault : choice == 1;
		}

		public void Visit(ZDO zdo, World.Kind kind, List<ZNetPeer> peers)
		{
			Settings s = QoLPlugin.Settings;
			if (!s.BallistasEnabled.Value || zdo.GetLong(ZDOVars.s_creator) == 0L)
			{
				return;
			}
			Apply(zdo, Wanted(zdo, PlayersKey, s.BallistasTargetPlayers.Value), Wanted(zdo, TamesKey, s.BallistasTargetTames.Value));
		}

		public static void Apply(ZDO zdo, bool players, bool tames)
		{
			Turret prefab = Fields.PrefabComponent<Turret>(zdo.GetPrefab());
			if (!prefab)
			{
				return;
			}
			bool changed = false;
			if (Fields.GetBool(zdo, "Turret", "m_targetPlayers", prefab.m_targetPlayers) != players)
			{
				Fields.Set(zdo, "Turret", "m_targetPlayers", players);
				changed = true;
			}
			if (Fields.GetBool(zdo, "Turret", "m_targetTamed", prefab.m_targetTamed) != tames
				|| Fields.GetBool(zdo, "Turret", "m_targetTamedConfig", prefab.m_targetTamedConfig) != tames)
			{
				Fields.Set(zdo, "Turret", "m_targetTamed", tames);
				Fields.Set(zdo, "Turret", "m_targetTamedConfig", tames);
				changed = true;
			}
			Turret loaded = Fields.Component<Turret>(zdo);
			if (loaded)
			{
				loaded.m_targetPlayers = players;
				loaded.m_targetTamed = tames;
				loaded.m_targetTamedConfig = tames;
			}
			if (changed)
			{
				QoLPlugin.Log.LogInfo($"Ballista at {zdo.GetPosition():F0}: players {players}, tames {tames}");
			}
		}
	}
}
