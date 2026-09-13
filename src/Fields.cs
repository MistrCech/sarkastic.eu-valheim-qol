using System;
using UnityEngine;

namespace SarkasticQoL
{
	/*
		Per-object overrides of a component's public fields, read by every client and by the server
		itself when the object is created (ZNetView.LoadFields): the object's data holds
		"HasFields" = true, "HasFields<Component>" = true and "<Component>.<field>" = value, for
		int, float, bool, Vector3, string, GameObject and ItemDrop fields (the last two by prefab
		name). This is how the game itself lets a world differ from its prefabs; no client mod.

		An object that is already loaded keeps its current fields until it is created again, so a
		change that must show at once is followed by Recreate.
	*/
	internal static class Fields
	{
		private static readonly int HasFields = "HasFields".GetStableHashCode();

		public static int Key(string component, string field)
		{
			return (component + "." + field).GetStableHashCode();
		}

		public static void Set(ZDO zdo, string component, string field, bool value)
		{
			Mark(zdo, component);
			zdo.Set(Key(component, field), value);
		}

		public static void Set(ZDO zdo, string component, string field, int value)
		{
			Mark(zdo, component);
			zdo.Set(Key(component, field), value);
		}

		public static void Set(ZDO zdo, string component, string field, float value)
		{
			Mark(zdo, component);
			zdo.Set(Key(component, field), value);
		}

		public static void Set(ZDO zdo, string component, string field, string value)
		{
			Mark(zdo, component);
			zdo.Set(Key(component, field), value);
		}

		public static void Set(ZDO zdo, string component, string field, Vector3 value)
		{
			Mark(zdo, component);
			zdo.Set(Key(component, field), value);
		}

		private static void Mark(ZDO zdo, string component)
		{
			zdo.Set(HasFields, true);
			zdo.Set(("HasFields" + component).GetStableHashCode(), true);
		}

		public static bool GetBool(ZDO zdo, string component, string field, bool prefabValue)
		{
			return zdo.GetBool(Key(component, field), prefabValue);
		}

		public static int GetInt(ZDO zdo, string component, string field, int prefabValue)
		{
			return zdo.GetInt(Key(component, field), prefabValue);
		}

		/*
			Replaces the object with a copy under a new id so that every client (and the server)
			creates it afresh with the current overrides. All data carries over: Serialize writes
			the flags, prefab, rotation and every value, Deserialize reads them into the new one.
			Anything that refers to the object by id (a spawner's link to what it spawned, a
			portal's pair) would lose it, so this is for pieces without such links: containers,
			crafting stations, fires, ballistas.
		*/
		public static ZDO Recreate(ZDO zdo)
		{
			ZDO fresh = ZDOMan.instance.CreateNewZDO(zdo.GetPosition(), zdo.GetPrefab());
			ZPackage pkg = new ZPackage();
			zdo.Serialize(pkg);
			pkg.SetPos(0);
			fresh.Deserialize(pkg);
			fresh.SetOwner(ZDOMan.GetSessionID());
			fresh.DataRevision++;
			ZDOMan.instance.SetDirtySector(fresh);
			World.Retire(zdo);
			Destroy(zdo);
			return fresh;
		}

		// Both ZNetScene.Destroy and ZDOMan.DestroyZDO only act on an object the server owns.
		/*
			A new world object, the way ZNetView.Awake registers one it has just created: every
			client (and the server) creates the prefab for it. Returns null if there is no such prefab.
		*/
		public static ZDO Place(int prefabHash, Vector3 position, Quaternion rotation)
		{
			GameObject prefab = ZNetScene.instance.GetPrefab(prefabHash);
			ZNetView view = prefab ? prefab.GetComponent<ZNetView>() : null;
			if (!view)
			{
				return null;
			}
			ZDO zdo = ZDOMan.instance.CreateNewZDO(position, prefabHash);
			zdo.Persistent = view.m_persistent;
			zdo.Type = view.m_type;
			zdo.Distant = view.m_distant;
			zdo.SetPrefab(prefabHash);
			zdo.SetRotation(rotation);
			zdo.DataRevision++;
			ZDOMan.instance.SetDirtySector(zdo);
			return zdo;
		}

		public static void Destroy(ZDO zdo)
		{
			zdo.SetOwner(ZDOMan.GetSessionID());
			ZNetView instance = ZNetScene.instance.FindInstance(zdo);
			if (instance)
			{
				ZNetScene.instance.Destroy(instance.gameObject);
			}
			else
			{
				ZDOMan.instance.DestroyZDO(zdo);
			}
		}

		public static bool IsLoaded(ZDO zdo)
		{
			return ZNetScene.instance.FindInstance(zdo);
		}

		public static T Component<T>(ZDO zdo) where T : Component
		{
			ZNetView instance = ZNetScene.instance.FindInstance(zdo);
			return instance ? instance.GetComponent<T>() : null;
		}

		public static T PrefabComponent<T>(int prefabHash) where T : Component
		{
			GameObject prefab = ZNetScene.instance.GetPrefab(prefabHash);
			return prefab ? prefab.GetComponent<T>() : null;
		}
	}
}
