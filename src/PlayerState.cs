using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace SarkasticQoL
{
	/*
		What a player switched on or off for themselves (!tame off ...), by their platform id, in a
		text file in BepInEx/config. A vanilla client stores nothing for us.
	*/
	internal class PlayerState
	{
		private readonly string path = Path.Combine(Paths.ConfigPath, "sarkasticeu.qol.players.txt");
		private readonly Dictionary<string, HashSet<string>> off = new Dictionary<string, HashSet<string>>();

		public void Load()
		{
			off.Clear();
			if (!File.Exists(path))
			{
				return;
			}
			foreach (string line in File.ReadAllLines(path))
			{
				string[] f = line.Split(' ');
				if (f.Length >= 2 && f[0] != "#")
				{
					off[f[0]] = new HashSet<string>(f, StringComparer.OrdinalIgnoreCase) { };
					off[f[0]].Remove(f[0]);
				}
			}
		}

		private void Save()
		{
			List<string> lines = new List<string> { "# Sarkastic.eu QoL: per player, the features they switched off. <platform id> <feature> ..." };
			foreach (KeyValuePair<string, HashSet<string>> entry in off)
			{
				if (entry.Value.Count > 0)
				{
					lines.Add(entry.Key + " " + string.Join(" ", entry.Value));
				}
			}
			File.WriteAllLines(path, lines);
		}

		public static string IdOf(ZNetPeer peer)
		{
			return peer.m_socket.GetHostName();
		}

		public bool Wants(ZNetPeer peer, string feature)
		{
			return !(off.TryGetValue(IdOf(peer), out HashSet<string> set) && set.Contains(feature));
		}

		public void Set(ZNetPeer peer, string feature, bool on)
		{
			string id = IdOf(peer);
			if (!off.TryGetValue(id, out HashSet<string> set))
			{
				set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				off[id] = set;
			}
			if (on ? set.Remove(feature) : set.Add(feature))
			{
				Save();
			}
		}
	}
}
