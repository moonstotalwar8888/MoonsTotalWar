using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: SUPABASE HTTP & EDGE GATEWAY DATA SERVICE (v25.0 Hardened Security Edition)
	/// Uses SecurityVault runtime byte-stream decoding, HMAC-SHA256 request signing, Edge Function gateway routing,
	/// and serialized HTTP save/load synchronization so rapid scene switching can NEVER overwrite a newly launched fleet!
	/// </summary>
	public partial class SupabaseService : Node
	{
		public static SupabaseService Instance { get; private set; }
		private readonly SemaphoreSlim _saveSyncLock = new SemaphoreSlim(1, 1);
		public static long LastLocalFleetMutationUtcMs = 0;

		// Resolved dynamically at runtime from SecurityVault (never exported to .tscn or plain-text binary string tables)
		public string SupabaseUrl => SecurityVault.GetEndpointUrl();
		public string SupabaseAnonKey => SecurityVault.GetPublishableKey();

		// When true, routes requests through the /functions/v1/moons-gateway Edge Function (with automatic fallback if not yet deployed)
		public static bool UseEdgeFunctionGateway = true;
		private static bool _edgeGatewayUnavailableFallback = false;

		public string AccessToken { get; private set; } = "";
		public string RefreshToken { get; private set; } = "";
		public long TokenExpiresAtUnixSec { get; private set; } = 0;
		private string _cachedAuthEmail = "";
		private string _cachedAuthPassword = "";
		public string UserId { get; private set; } = "";
		public string LoggedInUsername { get; set; } = "";
		public string ActiveServerId { get; set; } = "S1";
		public static long LastLocalActionUtcMs = 0;

		// Persistent session cache so switching between BaseView, MoonView, and GalaxyView is instant
		public static string CachedServerId = "S1";
		public static float CachedServerSpeed = 1.0f;
		public static int CachedMoonId = 100;
		public static int CachedBaseSlot = 1;
		public static int CachedRadarLevel = 1; // Level 1 = 10% circular galaxy visibility .. Level 10 = 100%
		public static string CachedCommanderName = "COMMANDER ALPHA";
		public static string CachedBaseName = "Alpha Outpost";
		public static Dictionary<string, string> CachedServerBaseNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public static string CachedAllianceName = "NONE";

		public static string GetServerCustomBaseName(ColonySaveData rawRow, string serverId)
		{
			string srv = string.IsNullOrWhiteSpace(serverId) ? "S1" : serverId.Trim().ToUpper();
			bool isLocalPlayerRow = rawRow == null ||
				(Instance != null && !string.IsNullOrEmpty(Instance.UserId) && string.Equals(rawRow.id, Instance.UserId, StringComparison.OrdinalIgnoreCase));

			if (isLocalPlayerRow && CachedServerBaseNames.TryGetValue(srv, out string cachedName) && !string.IsNullOrWhiteSpace(cachedName))
			{
				return cachedName;
			}
			if (rawRow != null)
			{
				if (rawRow.active_boosts != null)
				{
					string boostMetaKey = srv == "S1" ? "__meta_base_name_S1" : $"__srv_{srv}_base_name";
					if (rawRow.active_boosts.TryGetValue(boostMetaKey, out var bData) && bData != null && !string.IsNullOrWhiteSpace(bData.Label))
					{
						return bData.Label.Trim();
					}
				}
				if (srv == "S1" && !string.IsNullOrWhiteSpace(rawRow.base_name))
				{
					return rawRow.base_name.Trim();
				}
			}
			return srv == "S1" ? "Alpha Outpost" : $"{srv} Outpost";
		}

		public static void SetServerCustomBaseName(string serverId, string newBaseName)
		{
			string srv = string.IsNullOrWhiteSpace(serverId) ? "S1" : serverId.Trim().ToUpper();
			string clean = string.IsNullOrWhiteSpace(newBaseName) ? (srv == "S1" ? "Alpha Outpost" : $"{srv} Outpost") : newBaseName.Trim();
			CachedServerBaseNames[srv] = clean;
			if (string.Equals(CachedServerId, srv, StringComparison.OrdinalIgnoreCase))
			{
				CachedBaseName = clean;
			}
		}

		// Persistent Garrison, Simultaneous Unit Assembly Bays, and Fleet Hangar Cache
		public static bool HasValidSessionColonyCache = false;
		public static double CachedResE = 5000;
		public static double CachedResI = 5000;
		public static double CachedResT = 5000;
		public static double CachedResH3 = 2500;
		public static long CachedGGold = 100;
		public static Dictionary<string, int> CachedBuildingLevels = new Dictionary<string, int>();
		public static GameMath.BuildQueueItem CachedActiveBuild = null;
		public static List<GameMath.BuildQueueItem> CachedBuildQueue = new List<GameMath.BuildQueueItem>();
		public static Dictionary<string, GameMath.BoostData> CachedActiveBoosts = new Dictionary<string, GameMath.BoostData>();
		public static Dictionary<string, int> CachedUnitInventory = new Dictionary<string, int>();
		public static Dictionary<string, GameMath.UnitAssemblyBayJob> CachedUnitBays = new Dictionary<string, GameMath.UnitAssemblyBayJob>();
		public static List<GameMath.FleetManifest> CachedSavedFleets = new List<GameMath.FleetManifest>();
		public static List<GameMath.WatchlistEntry> CachedWatchlist = new List<GameMath.WatchlistEntry>();
		public static List<IncomingFleetSignal> CachedIncomingSignals = new List<IncomingFleetSignal>();

		public static bool IsInWatchlist(int moonId, int slotId)
		{
			if (CachedWatchlist == null) return false;
			return CachedWatchlist.Exists(w => w != null && w.MoonId == moonId && w.SlotId == slotId);
		}

		public static void AddOrUpdateWatchlist(int moonId, int slotId, string commanderName, string baseName, string allianceName, bool isOccupied, int coreLevel = 1)
		{
			AddOrUpdateWatchlistEntry(moonId, slotId, commanderName, baseName, allianceName, coreLevel, isOccupied);
		}

		public static void AddOrUpdateWatchlistEntry(int moonId, int slotId, string commanderName, string baseName, string allianceName, int coreLevel, bool isOccupied)
		{
			if (CachedWatchlist == null) CachedWatchlist = new List<GameMath.WatchlistEntry>();
			var existing = CachedWatchlist.Find(w => w != null && w.MoonId == moonId && w.SlotId == slotId);
			if (existing != null)
			{
				existing.CommanderName = string.IsNullOrWhiteSpace(commanderName) ? "EMPTY MOON SLOT" : commanderName.Trim().ToUpper();
				existing.BaseName = string.IsNullOrWhiteSpace(baseName) ? "Open Sector" : baseName.Trim();
				existing.AllianceName = string.IsNullOrWhiteSpace(allianceName) ? "NONE" : allianceName.Trim().ToUpper();
				existing.CoreLevel = coreLevel;
				existing.IsOccupied = isOccupied;
				existing.ServerId = CachedServerId;
			}
			else
			{
				CachedWatchlist.Add(new GameMath.WatchlistEntry
				{
					ServerId = CachedServerId,
					MoonId = moonId,
					SlotId = slotId,
					CommanderName = string.IsNullOrWhiteSpace(commanderName) ? "EMPTY MOON SLOT" : commanderName.Trim().ToUpper(),
					BaseName = string.IsNullOrWhiteSpace(baseName) ? "Open Sector" : baseName.Trim(),
					AllianceName = string.IsNullOrWhiteSpace(allianceName) ? "NONE" : allianceName.Trim().ToUpper(),
					CoreLevel = coreLevel,
					IsOccupied = isOccupied
				});
			}
		}

		public static void RemoveFromWatchlist(int moonId, int slotId)
		{
			CachedWatchlist?.RemoveAll(w => w == null || (w.MoonId == moonId && w.SlotId == slotId));
		}

		// Isolated Multi-Server State Cache (Stores S2+ states cleanly inside active_boosts["__srv_S2"] or dedicated server_states so S1 & S2 NEVER overwrite each other even on a single-row UUID primary key!)
		private static Dictionary<string, ColonySaveData> _isolatedServerSnapshots = new Dictionary<string, ColonySaveData>();

		public class ServerColonySummary
		{
			public string ServerId { get; set; }
			public bool HasColony { get; set; }
			public int MoonId { get; set; }
			public int SlotId { get; set; }
			public int CoreLevel { get; set; }
			public long Population { get; set; }
		}

		public static void SelectActiveServer(string serverId, float serverSpeed)
		{
			string cleanSrv = string.IsNullOrWhiteSpace(serverId) ? "S1" : serverId.Trim().ToUpper();
			CachedServerId = cleanSrv;
			CachedServerSpeed = Math.Max(1.0f, serverSpeed);
			if (Instance != null)
			{
				Instance.ActiveServerId = cleanSrv;
			}

			// Clear in-memory runtime caches before loading the selected server's isolated state from Supabase!
			HasValidSessionColonyCache = false;
			CachedRadarLevel = 1;
			CachedResE = 5000;
			CachedResI = 5000;
			CachedResT = 5000;
			CachedResH3 = 2500;
			CachedGGold = 100;
			CachedBuildingLevels = new Dictionary<string, int>();
			CachedActiveBuild = null;
			CachedBuildQueue = new List<GameMath.BuildQueueItem>();
			CachedActiveBoosts = new Dictionary<string, GameMath.BoostData>();
			CachedUnitInventory = new Dictionary<string, int>();
			CachedUnitBays = new Dictionary<string, GameMath.UnitAssemblyBayJob>();
			CachedSavedFleets = new List<GameMath.FleetManifest>();
			CachedWatchlist = new List<GameMath.WatchlistEntry>();
			CachedIncomingSignals = new List<IncomingFleetSignal>();
			CachedBaseName = GetServerCustomBaseName(null, cleanSrv);
			CachedAllianceName = "NONE";

			// If the cloud row was already downloaded on the ServerSelect screen, pre-warm the session cache for this server right now so BaseView spawns at the exact saved positions & levels with zero 1-second jump!
			if (Instance != null && !string.IsNullOrEmpty(Instance.UserId) && _isolatedServerSnapshots.TryGetValue(Instance.UserId, out ColonySaveData snapRow) && snapRow != null)
			{
				var preUnpacked = Instance.UnpackIsolatedServerColony(snapRow, cleanSrv);
				if (preUnpacked != null)
				{
					PopulateStaticSessionCacheFromColony(preUnpacked);
				}
			}
		}

		public static void ClearAllSessionCaches()
		{
			HasValidSessionColonyCache = false;
			CachedRadarLevel = 1;
			CachedMoonId = 100;
			CachedBaseSlot = 1;
			CachedCommanderName = "COMMANDER ALPHA";
			CachedBaseName = "Alpha Outpost";
			CachedServerBaseNames.Clear();
			CachedAllianceName = "NONE";
			CachedResE = 5000;
			CachedResI = 5000;
			CachedResT = 5000;
			CachedResH3 = 2500;
			CachedGGold = 100;
			CachedBuildingLevels = new Dictionary<string, int>();
			CachedActiveBuild = null;
			CachedBuildQueue = new List<GameMath.BuildQueueItem>();
			CachedActiveBoosts = new Dictionary<string, GameMath.BoostData>();
			CachedUnitInventory = new Dictionary<string, int>();
			CachedUnitBays = new Dictionary<string, GameMath.UnitAssemblyBayJob>();
			CachedSavedFleets = new List<GameMath.FleetManifest>();
			CachedWatchlist = new List<GameMath.WatchlistEntry>();
			CachedIncomingSignals = new List<IncomingFleetSignal>();
			InvalidateColoniesCache();
			InvalidateInboxCache();

			if (Instance != null)
			{
				Instance.AccessToken = "";
				Instance.RefreshToken = "";
				Instance.TokenExpiresAtUnixSec = 0;
				Instance.UserId = "";
				Instance.LoggedInUsername = "";
			}
		}

		public static void MarkLocalFleetMutated(GameMath.FleetManifest fleet = null)
		{
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			LastLocalFleetMutationUtcMs = nowMs;
			if (fleet != null)
			{
				fleet.LastUpdatedUtcMs = nowMs;
			}
		}

		public static void PopulateStaticSessionCacheFromColony(ColonySaveData col, bool preserveNewerLocalFleets = false)
		{
			if (col == null) return;
			CachedResE = col.res_e;
			CachedResI = col.res_i;
			CachedResT = col.res_t;
			CachedResH3 = col.res_h3;
			CachedGGold = col.mgold;
			if (!string.IsNullOrEmpty(col.commander_name)) CachedCommanderName = col.commander_name;
			if (!string.IsNullOrEmpty(col.base_name))
			{
				CachedBaseName = col.base_name;
				string activeSrvKey = string.IsNullOrWhiteSpace(CachedServerId) ? "S1" : CachedServerId.Trim().ToUpper();
				CachedServerBaseNames[activeSrvKey] = col.base_name;
			}
			if (!string.IsNullOrEmpty(col.alliance_name)) CachedAllianceName = col.alliance_name;
			if (col.building_levels != null)
			{
				CachedBuildingLevels = new Dictionary<string, int>(col.building_levels);
				int m = CachedBuildingLevels.GetValueOrDefault("__meta_moon", col.moon_id);
				int s = CachedBuildingLevels.GetValueOrDefault("__meta_slot", col.slot_id);
				if (m >= 1 && m <= 1000)
				{
					CachedMoonId = m;
					CachedBuildingLevels["__meta_moon"] = m;
				}
				if (s >= 1 && s <= 20)
				{
					CachedBaseSlot = s;
					CachedBuildingLevels["__meta_slot"] = s;
				}
				CachedRadarLevel = Math.Clamp(CachedBuildingLevels.GetValueOrDefault("hub_rng", 1), 1, 10);
			}
			CachedActiveBuild = col.active_build;
			CachedBuildQueue = col.build_queue != null ? new List<GameMath.BuildQueueItem>(col.build_queue) : new List<GameMath.BuildQueueItem>();
			CachedActiveBoosts = col.active_boosts != null ? new Dictionary<string, GameMath.BoostData>(col.active_boosts) : new Dictionary<string, GameMath.BoostData>();
			if (col.unit_inventory != null) CachedUnitInventory = new Dictionary<string, int>(col.unit_inventory);
			if (col.unit_bays != null) CachedUnitBays = new Dictionary<string, GameMath.UnitAssemblyBayJob>(col.unit_bays);

			// Build lookup of existing in-memory fleets so a stale cloud GET can NEVER revert a newly launched or updated fleet!
			var localFleetMap = new Dictionary<string, GameMath.FleetManifest>();
			if (preserveNewerLocalFleets && CachedSavedFleets != null)
			{
				foreach (var lf in CachedSavedFleets)
				{
					if (lf != null && !string.IsNullOrEmpty(lf.Id))
					{
						localFleetMap[lf.Id] = lf;
					}
				}
			}

			var mergedFleets = new List<GameMath.FleetManifest>();
			var seenIds = new HashSet<string>();

			if (!preserveNewerLocalFleets || CachedWatchlist == null || CachedWatchlist.Count == 0)
			{
				CachedWatchlist = new List<GameMath.WatchlistEntry>();
			}

			if (col.saved_fleets != null)
			{
				foreach (var f in col.saved_fleets)
				{
					if (f == null) continue;
					if (f.Status == "WATCHLIST_DATA" || (f.Id != null && f.Id.EndsWith("_WATCHLIST", StringComparison.OrdinalIgnoreCase)))
					{
						if (f.Cargo?.Watchlist != null && (!preserveNewerLocalFleets || CachedWatchlist == null || CachedWatchlist.Count == 0))
						{
							CachedWatchlist = new List<GameMath.WatchlistEntry>(f.Cargo.Watchlist);
						}
						continue;
					}

					if (preserveNewerLocalFleets && !string.IsNullOrEmpty(f.Id) && localFleetMap.TryGetValue(f.Id, out var localF))
					{
						seenIds.Add(f.Id);
						long localStamp = Math.Max(localF.LastUpdatedUtcMs, localF.DepartureTimeUnixMs);
						long cloudStamp = Math.Max(f.LastUpdatedUtcMs, f.DepartureTimeUnixMs);

						// If the local in-memory fleet has a newer launch/mutation timestamp (or was launched within the last 15s while cloud still says STATIONED/DOCKED),
						// keep the authoritative local fleet so switching views never resets the fleet back!
						bool localIsNewerLaunch = (localStamp > cloudStamp) ||
												  (localF.Status == "IN_TRANSIT" && f.Status != "IN_TRANSIT" && (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - localF.DepartureTimeUnixMs) < 30000L);

						mergedFleets.Add(localIsNewerLaunch ? localF : f);
					}
					else
					{
						if (!string.IsNullOrEmpty(f.Id)) seenIds.Add(f.Id);
						mergedFleets.Add(f);
					}
				}
			}

			// Also preserve any brand-new fleet created locally within the last 30 seconds that hasn't finished round-tripping to the cloud yet
			if (preserveNewerLocalFleets && localFleetMap.Count > 0)
			{
				long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				foreach (var kvp in localFleetMap)
				{
					if (!seenIds.Contains(kvp.Key) && (nowMs - kvp.Value.LastUpdatedUtcMs) < 30000L)
					{
						mergedFleets.Add(kvp.Value);
					}
				}
			}

			CachedSavedFleets = mergedFleets;
			HasValidSessionColonyCache = true;
		}

		public class IncomingFleetSignal
		{
			public string SignalId { get; set; }
			public string FleetId { get; set; }
			public string SenderName { get; set; }
			public string OriginCoord { get; set; }
			public string TargetCoord { get; set; }
			public string Protocol { get; set; } // RAID, MOVE, REINFORCE
			public string TargetBuildingName { get; set; }
			public int TotalUnits { get; set; }
			public int LaserCannonCount { get; set; }
			public double EtaSecondsLeft { get; set; }
			public double TotalDurationSec { get; set; } = 180;
			public long DepartureTimeUnixMs { get; set; } = 0;
			public long ArrivalTimeUnixMs { get; set; } = 0;
		}

		public class ColonySaveData
		{
			public string id { get; set; }
			public string commander_name { get; set; }
			public string base_name { get; set; }
			public string alliance_name { get; set; }
			public int moon_id { get; set; }
			public int slot_id { get; set; }
			public double res_e { get; set; }
			public double res_i { get; set; }
			public double res_t { get; set; }
			public double res_h3 { get; set; }
			public long mgold { get; set; }
			public DateTimeOffset last_sync_time { get; set; }
			public Dictionary<string, int> building_levels { get; set; }
			public GameMath.BuildQueueItem active_build { get; set; }
			public List<GameMath.BuildQueueItem> build_queue { get; set; }
			public Dictionary<string, GameMath.BoostData> active_boosts { get; set; }
			public Dictionary<string, int> unit_inventory { get; set; }
			public Dictionary<string, GameMath.UnitAssemblyBayJob> unit_bays { get; set; }
			public List<GameMath.FleetManifest> saved_fleets { get; set; }
		}

		public class GalacticCommanderEntry
		{
			public string UserId { get; set; }
			public string CommanderName { get; set; }
			public string BaseName { get; set; }
			public string AllianceName { get; set; }
			public int MoonId { get; set; }
			public int BaseSlot { get; set; }
			public int IndustrialCoreLevel { get; set; }
			public int AllianceHQLevel { get; set; }
			public int AllianceRoleCode { get; set; } // 4=Supreme Commander/Founder, 3=War General, 2=Chief Scientist, 1=Diplomat, 0=Member
			public long Population { get; set; }
			public int MedalPop { get; set; }
			public int MedalRaid { get; set; }
			public int MedalAtk { get; set; }
			public int MedalDef { get; set; }
			public bool IsMe { get; set; }
		}

		public class AllianceIncomingAttackAlert
		{
			public string FleetId { get; set; }
			public string AttackerName { get; set; }
			public string AttackerAlliance { get; set; }
			public string AttackerOriginCoord { get; set; }
			public string DefenderName { get; set; }
			public string DefenderBaseName { get; set; }
			public string DefenderCoord { get; set; }
			public int DefenderMoonId { get; set; }
			public int DefenderSlotId { get; set; }
			public string TargetBuildingName { get; set; }
			public int TotalUnits { get; set; }
			public int LaserCannonCount { get; set; }
			public double EtaSecondsLeft { get; set; }
			public long ArrivalTimeUnixMs { get; set; }
		}

		public class AllianceScienceSharedNode
		{
			public string ScienceKey { get; set; }
			public int CurrentLevel { get; set; }
			public int MaxLevel { get; set; }
			public long ContributedE { get; set; }
			public long ContributedI { get; set; }
			public long ContributedT { get; set; }
			public long ContributedH3 { get; set; }
			public long RequiredE { get; set; }
			public long RequiredI { get; set; }
			public long RequiredT { get; set; }
			public long RequiredH3 { get; set; }
			public long UpgradeEndUnixSec { get; set; }
			public bool IsUpgradingNow { get; set; }
			public double RemainingUpgradeSeconds { get; set; }
			public bool IsFullyFunded =>
				ContributedE >= RequiredE &&
				ContributedI >= RequiredI &&
				ContributedT >= RequiredT &&
				ContributedH3 >= RequiredH3;
		}

		public static int CachedAllianceShieldScienceLevel = 0;

		public override void _Ready()
		{
			if (Instance != null && Instance != this)
			{
				QueueFree();
				return;
			}

			Instance = this;
			GD.Print("[SUPABASE SERVICE] Encrypted Cloud Gateway Initialized.");
			CallDeferred(nameof(MakeImmortal));
		}

		private void MakeImmortal()
		{
			Node root = GetTree().Root;
			if (GetParent() != root)
			{
				GetParent().RemoveChild(this);
				root.AddChild(this);
				GD.Print("[SUPABASE SERVICE] Reparented to Root. Persistence achieved.");
			}
		}

		private static string MethodToHttpString(HttpClient.Method method)
		{
			return method switch
			{
				HttpClient.Method.Get => "GET",
				HttpClient.Method.Post => "POST",
				HttpClient.Method.Put => "PUT",
				HttpClient.Method.Patch => "PATCH",
				HttpClient.Method.Delete => "DELETE",
				_ => "GET"
			};
		}

		private async Task<(long responseCode, string responseText)> ExecuteRawHttpRequestAsync(string url, string[] headers, HttpClient.Method method, string body = "")
		{
			HttpRequest reqNode = new HttpRequest();
			AddChild(reqNode);
			reqNode.Timeout = 10.0f;

			Error err = reqNode.Request(url, headers, method, body);
			if (err != Error.Ok)
			{
				reqNode.QueueFree();
				throw new Exception($"Godot HttpRequest failed to start: {err}");
			}

			var result = await ToSignal(reqNode, HttpRequest.SignalName.RequestCompleted);

			long responseCode = (long)result[1];
			byte[] responseBytes = (byte[])result[3];
			string responseText = "";

			if (responseBytes != null && responseBytes.Length > 0)
			{
				responseText = Encoding.UTF8.GetString(responseBytes);
			}

			reqNode.QueueFree();
			return (responseCode, responseText);
		}

		private async Task<(long responseCode, string responseText)> SendGodotRequestAsync(string url, string[] headers, HttpClient.Method method, string body = "")
		{
			string methodStr = MethodToHttpString(method);
			var (tsHeader, sigHeader) = SecurityVault.ComputeRequestSignature(methodStr, url, body);

			var signedHeaders = new List<string>(headers ?? Array.Empty<string>())
			{
				$"X-Moons-Timestamp: {tsHeader}",
				$"X-Moons-Signature: {sigHeader}"
			};

			// Route through Supabase Edge Function Gateway if enabled and available
			string baseEndpoint = SupabaseUrl;
			if (UseEdgeFunctionGateway && !_edgeGatewayUnavailableFallback && url.StartsWith(baseEndpoint, StringComparison.OrdinalIgnoreCase))
			{
				string relativeTarget = url.Substring(baseEndpoint.Length);
				var gatewayHeaders = new List<string>(signedHeaders)
				{
					$"X-Moons-Target-Path: {relativeTarget}",
					$"X-Moons-Target-Method: {methodStr}"
				};
				string gatewayUrl = SecurityVault.GetEdgeGatewayUrl();
				var (gwCode, gwText) = await ExecuteRawHttpRequestAsync(gatewayUrl, gatewayHeaders.ToArray(), method, body);
				if (gwCode != 404 && gwCode != 502 && gwCode != 503 && gwCode != 0)
				{
					return (gwCode, gwText);
				}
				// Edge Function not yet deployed on project -> transparently use signed direct endpoint for the rest of the session
				_edgeGatewayUnavailableFallback = true;
			}

			return await ExecuteRawHttpRequestAsync(url, signedHeaders.ToArray(), method, body);
		}

		private const string SESSION_CACHE_FILE = "user://commander_auth_session.json";

		private class SavedAuthSession
		{
			public string Username { get; set; } = "";
			public string Email { get; set; } = "";
			public string Password { get; set; } = "";
			public string UserId { get; set; } = "";
			public string AccessToken { get; set; } = "";
			public string RefreshToken { get; set; } = "";
			public long TokenExpiresAtUnixSec { get; set; } = 0;
		}

		public (string username, string password) GetSavedLoginCredentials()
		{
			return LoadSavedLoginCredentials();
		}

		public (string username, string password) LoadSavedLoginCredentials()
		{
			try
			{
				if (!FileAccess.FileExists(SESSION_CACHE_FILE)) return ("", "");
				using var file = FileAccess.Open(SESSION_CACHE_FILE, FileAccess.ModeFlags.Read);
				if (file == null) return ("", "");
				string storedContent = file.GetAsText();
				if (string.IsNullOrWhiteSpace(storedContent)) return ("", "");
				string json = SecurityVault.DecryptLocalText(storedContent);
				if (string.IsNullOrWhiteSpace(json)) return ("", "");
				var s = JsonSerializer.Deserialize<SavedAuthSession>(json);
				if (s != null)
				{
					return (s.Username ?? "", s.Password ?? "");
				}
			}
			catch { }
			return ("", "");
		}

		private void SaveLoginSessionToDisk(string username, string email, string password)
		{
			try
			{
				var session = new SavedAuthSession
				{
					Username = username ?? "",
					Email = email ?? "",
					Password = password ?? "",
					UserId = UserId ?? "",
					AccessToken = AccessToken ?? "",
					RefreshToken = RefreshToken ?? "",
					TokenExpiresAtUnixSec = TokenExpiresAtUnixSec
				};
				string json = JsonSerializer.Serialize(session);
				string encryptedPayload = SecurityVault.EncryptLocalText(json);
				using var file = FileAccess.Open(SESSION_CACHE_FILE, FileAccess.ModeFlags.Write);
				if (file != null)
				{
					file.StoreString(encryptedPayload);
					file.Flush();
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SUPABASE] Session cache write warning: {ex.Message}");
			}
		}

		private static List<string> BuildCandidateAuthEmails(string rawInput)
		{
			var list = new List<string>();
			if (string.IsNullOrWhiteSpace(rawInput)) return list;

			string trimmed = rawInput.Trim().ToLowerInvariant();

			void AddUnique(string emailCandidate)
			{
				if (!string.IsNullOrWhiteSpace(emailCandidate) && !list.Contains(emailCandidate))
				{
					list.Add(emailCandidate);
				}
			}

			// 1. If the player typed a full email address (e.g., player@gmail.com or user@moons.global), try it first AND its @moons.global username prefix!
			if (trimmed.Contains('@'))
			{
				AddUnique(trimmed.Replace(" ", "_"));
				string localPart = trimmed.Split('@')[0].Trim();
				if (!string.IsNullOrEmpty(localPart))
				{
					AddUnique($"{localPart.Replace(" ", "_")}@moons.global");
					AddUnique($"{localPart.Replace(" ", "")}@moons.global");
				}
			}
			else
			{
				// 2. Standard commander callsign -> @moons.global (try underscore-normalized, space-stripped, and hyphen-normalized variants)
				string withUnderscores = System.Text.RegularExpressions.Regex.Replace(trimmed, @"\s+", "_");
				string withoutSpaces = System.Text.RegularExpressions.Regex.Replace(trimmed, @"\s+", "");
				AddUnique($"{withUnderscores}@moons.global");
				AddUnique($"{withoutSpaces}@moons.global");
				if (trimmed.Contains('_'))
				{
					AddUnique($"{trimmed.Replace("_", "")}@moons.global");
				}
			}

			return list;
		}

		private static string ExtractReadableSupabaseAuthError(long code, string responseText)
		{
			if (string.IsNullOrWhiteSpace(responseText))
			{
				return $"Authentication failed (HTTP {code}). Please check your connection.";
			}
			try
			{
				using var doc = JsonDocument.Parse(responseText);
				var root = doc.RootElement;
				string msg = "";
				if (root.TryGetProperty("msg", out var mProp) && mProp.ValueKind == JsonValueKind.String) msg = mProp.GetString();
				else if (root.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String) msg = msgProp.GetString();
				else if (root.TryGetProperty("error_description", out var edProp) && edProp.ValueKind == JsonValueKind.String) msg = edProp.GetString();

				string errCode = "";
				if (root.TryGetProperty("error_code", out var ecProp) && ecProp.ValueKind == JsonValueKind.String) errCode = ecProp.GetString();

				if (errCode == "invalid_credentials" || msg.Contains("Invalid login credentials", StringComparison.OrdinalIgnoreCase))
				{
					return "Incorrect Username or Security Keycode. (Check spelling or tap REGISTER NEW if this is a new account).";
				}
				if (errCode == "email_not_confirmed" || msg.Contains("Email not confirmed", StringComparison.OrdinalIgnoreCase))
				{
					return "Account pending confirmation. Please re-enter or contact command.";
				}
				if (msg.Contains("Password should be at least", StringComparison.OrdinalIgnoreCase) || errCode == "weak_password")
				{
					return "Security Keycode must be at least 6 characters long.";
				}
				if (!string.IsNullOrWhiteSpace(msg))
				{
					return msg;
				}
			}
			catch { }
			return $"Auth Error ({code}): {responseText}";
		}

		public async Task<(bool Success, string Error)> SignUpAsync(string username, string password)
		{
			string cleanUser = (username ?? "").Trim();
			string cleanPass = password ?? "";
			if (string.IsNullOrEmpty(cleanUser) || string.IsNullOrEmpty(cleanPass.Trim()))
				return (false, "Username and Security Keycode required.");

			if (cleanPass.Trim().Length < 6)
				return (false, "Security Keycode must be at least 6 characters long.");

			var candidateEmails = BuildCandidateAuthEmails(cleanUser);
			string internalEmail = candidateEmails.Count > 0 ? candidateEmails[0] : $"{cleanUser.ToLowerInvariant().Replace(" ", "_")}@moons.global";
			string url = $"{SupabaseUrl}/auth/v1/signup";

			var payload = new { email = internalEmail, password = cleanPass.Trim() };
			string jsonBody = JsonSerializer.Serialize(payload);

			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}"
			};

			GD.Print($"[SUPABASE] Sending SignUp request for {cleanUser} ({internalEmail})...");

			try
			{
				var (code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Post, jsonBody);

				if (code == 200 || code == 201)
				{
					// Check if SignUp already returned an access_token directly!
					try
					{
						using var doc = JsonDocument.Parse(responseText);
						var root = doc.RootElement;
						if (root.TryGetProperty("access_token", out var tokenProp) && tokenProp.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(tokenProp.GetString()))
						{
							AccessToken = tokenProp.GetString();
							if (root.TryGetProperty("refresh_token", out var refreshProp) && refreshProp.ValueKind == JsonValueKind.String) RefreshToken = refreshProp.GetString();
							int expiresIn = 3600;
							if (root.TryGetProperty("expires_in", out var expProp) && expProp.TryGetInt32(out int parsedExp)) expiresIn = parsedExp;
							TokenExpiresAtUnixSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Max(300, expiresIn - 300);
							if (root.TryGetProperty("user", out var userProp) && userProp.TryGetProperty("id", out var idProp)) UserId = idProp.GetString();

							_cachedAuthEmail = internalEmail;
							_cachedAuthPassword = cleanPass.Trim();
							LoggedInUsername = cleanUser.Contains('@') ? cleanUser.Split('@')[0].Trim().ToUpper() : cleanUser.ToUpper();
							CachedCommanderName = LoggedInUsername;
							SaveLoginSessionToDisk(LoggedInUsername, internalEmail, _cachedAuthPassword);
							GD.Print($"[SUPABASE] Instant SignUp Session Established for {LoggedInUsername} ({UserId})!");
							return (true, "");
						}
					}
					catch { }

					return await SignInAsync(cleanUser, cleanPass);
				}
				else
				{
					if (responseText.Contains("already registered", StringComparison.OrdinalIgnoreCase) ||
						responseText.Contains("already_exists", StringComparison.OrdinalIgnoreCase) ||
						responseText.Contains("user_already_exists", StringComparison.OrdinalIgnoreCase))
					{
						GD.Print("[SUPABASE] User already registered in cloud DB. Redirecting to SignIn...");
						var signInAttempt = await SignInAsync(cleanUser, cleanPass);
						if (signInAttempt.Success)
						{
							return signInAttempt;
						}
						return (false, $"Commander '{cleanUser.ToUpper()}' is already registered! Enter your original Security Keycode to log in, or choose a different Username.");
					}
					return (false, ExtractReadableSupabaseAuthError(code, responseText));
				}
			}
			catch (Exception ex)
			{
				return (false, $"Network Error: {ex.Message}");
			}
		}

		public async Task<(bool Success, string Error)> SignInAsync(string username, string password)
		{
			string rawUser = (username ?? "").Trim();
			string rawPass = password ?? "";
			if (string.IsNullOrEmpty(rawUser) || string.IsNullOrEmpty(rawPass.Trim()))
				return (false, "Username and Security Keycode required.");

			string url = $"{SupabaseUrl}/auth/v1/token?grant_type=password";
			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}"
			};

			// Build candidate emails & candidate passwords (trimmed and untrimmed in case mobile keyboard added a trailing space during registration!)
			var candidateEmails = BuildCandidateAuthEmails(rawUser);
			var candidatePasswords = new List<string> { rawPass.Trim() };
			if (rawPass != rawPass.Trim()) candidatePasswords.Add(rawPass);
			if (rawPass.Trim().Length > 0)
			{
				// Also try lowercase first-char variant if mobile auto-capitalized the first letter of the password
				string pTrim = rawPass.Trim();
				if (char.IsUpper(pTrim[0]))
				{
					string lowerFirst = char.ToLowerInvariant(pTrim[0]) + pTrim.Substring(1);
					if (!candidatePasswords.Contains(lowerFirst)) candidatePasswords.Add(lowerFirst);
				}
				else if (char.IsLower(pTrim[0]))
				{
					string upperFirst = char.ToUpperInvariant(pTrim[0]) + pTrim.Substring(1);
					if (!candidatePasswords.Contains(upperFirst)) candidatePasswords.Add(upperFirst);
				}
			}

			GD.Print($"[SUPABASE] Sending SignIn request for {rawUser}...");

			long lastCode = 400;
			string lastResponseText = "";

			try
			{
				foreach (string emailAttempt in candidateEmails)
				{
					foreach (string passAttempt in candidatePasswords)
					{
						var payload = new { email = emailAttempt, password = passAttempt };
						string jsonBody = JsonSerializer.Serialize(payload);
						var (code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Post, jsonBody);
						lastCode = code;
						lastResponseText = responseText;

						if (code == 200)
						{
							using var doc = JsonDocument.Parse(responseText);
							var root = doc.RootElement;
							if (root.TryGetProperty("access_token", out var tokenProp)) AccessToken = tokenProp.GetString();
							if (root.TryGetProperty("refresh_token", out var refreshProp)) RefreshToken = refreshProp.GetString();
							int expiresIn = 3600;
							if (root.TryGetProperty("expires_in", out var expProp) && expProp.TryGetInt32(out int parsedExp)) expiresIn = parsedExp;
							TokenExpiresAtUnixSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Max(300, expiresIn - 300);
							if (root.TryGetProperty("user", out var userProp) && userProp.TryGetProperty("id", out var idProp)) UserId = idProp.GetString();

							_cachedAuthEmail = emailAttempt;
							_cachedAuthPassword = passAttempt;
							LoggedInUsername = rawUser.Contains('@') ? rawUser.Split('@')[0].Trim().ToUpper() : rawUser.ToUpper();
							CachedCommanderName = LoggedInUsername;
							SaveLoginSessionToDisk(LoggedInUsername, emailAttempt, passAttempt);

							GD.Print($"[SUPABASE] Cloud Auth Granted! Commander: {LoggedInUsername} | User ID: {UserId} | Token Auto-Refresh Armed.");
							return (true, "");
						}
					}
				}

				return (false, ExtractReadableSupabaseAuthError(lastCode, lastResponseText));
			}
			catch (Exception ex)
			{
				return (false, $"Network Error: {ex.Message}");
			}
		}

		/// <summary>
		/// Automatically refreshes the Supabase JWT AccessToken before it expires (every ~50 mins)
		/// or immediately when a 401/403 response is encountered, so players playing all day NEVER lose a save!
		/// </summary>
		public async Task<bool> EnsureValidAuthTokenAsync(bool forceRefresh = false)
		{
			long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			if (!forceRefresh && !string.IsNullOrEmpty(AccessToken) && TokenExpiresAtUnixSec > 0 && nowSec < TokenExpiresAtUnixSec)
			{
				return true;
			}

			// 1. Try OAuth2 refresh_token grant first
			if (!string.IsNullOrEmpty(RefreshToken))
			{
				try
				{
					string refreshUrl = $"{SupabaseUrl}/auth/v1/token?grant_type=refresh_token";
					string refreshBody = JsonSerializer.Serialize(new { refresh_token = RefreshToken });
					string[] headers = new string[]
					{
						"Content-Type: application/json",
						$"apikey: {SupabaseAnonKey}"
					};
					var (code, respText) = await SendGodotRequestAsync(refreshUrl, headers, HttpClient.Method.Post, refreshBody);
					if (code == 200 && !string.IsNullOrEmpty(respText))
					{
						using var doc = JsonDocument.Parse(respText);
						var root = doc.RootElement;
						if (root.TryGetProperty("access_token", out var tProp)) AccessToken = tProp.GetString();
						if (root.TryGetProperty("refresh_token", out var rProp)) RefreshToken = rProp.GetString();
						int expIn = 3600;
						if (root.TryGetProperty("expires_in", out var eProp) && eProp.TryGetInt32(out int pe)) expIn = pe;
						TokenExpiresAtUnixSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Max(300, expIn - 300);
						GD.Print("[SUPABASE] AccessToken seamlessly refreshed via refresh_token for long play session.");
						return true;
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[SUPABASE] Token refresh error: {ex.Message}");
				}
			}

			// 2. Fallback: Silent re-authentication using session credentials if refresh token rotated
			if (!string.IsNullOrEmpty(_cachedAuthEmail) && !string.IsNullOrEmpty(_cachedAuthPassword))
			{
				try
				{
					string loginUrl = $"{SupabaseUrl}/auth/v1/token?grant_type=password";
					string loginBody = JsonSerializer.Serialize(new { email = _cachedAuthEmail, password = _cachedAuthPassword });
					string[] headers = new string[]
					{
						"Content-Type: application/json",
						$"apikey: {SupabaseAnonKey}"
					};
					var (code, respText) = await SendGodotRequestAsync(loginUrl, headers, HttpClient.Method.Post, loginBody);
					if (code == 200 && !string.IsNullOrEmpty(respText))
					{
						using var doc = JsonDocument.Parse(respText);
						var root = doc.RootElement;
						if (root.TryGetProperty("access_token", out var tProp)) AccessToken = tProp.GetString();
						if (root.TryGetProperty("refresh_token", out var rProp)) RefreshToken = rProp.GetString();
						int expIn = 3600;
						if (root.TryGetProperty("expires_in", out var eProp) && eProp.TryGetInt32(out int pe)) expIn = pe;
						TokenExpiresAtUnixSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Max(300, expIn - 300);
						GD.Print("[SUPABASE] AccessToken seamlessly renewed via silent re-auth.");
						return true;
					}
				}
				catch { }
			}

			return !string.IsNullOrEmpty(AccessToken);
		}

		private string GetLocalJournalFilePath(string userId)
		{
			string safeId = string.IsNullOrWhiteSpace(userId) ? "default" : userId.Trim();
			return $"user://colony_journal_{safeId}.json";
		}

		private void SaveLocalDeviceJournal(string userId, ColonySaveData rawRow)
		{
			if (string.IsNullOrWhiteSpace(userId) || rawRow == null) return;
			try
			{
				string path = GetLocalJournalFilePath(userId);
				string json = JsonSerializer.Serialize(rawRow);
				string encryptedJournal = SecurityVault.EncryptLocalText(json);
				using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
				if (file != null)
				{
					file.StoreString(encryptedJournal);
					file.Flush();
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[LOCAL JOURNAL] Write warning: {ex.Message}");
			}
		}

		private ColonySaveData LoadLocalDeviceJournal(string userId)
		{
			if (string.IsNullOrWhiteSpace(userId)) return null;
			try
			{
				string path = GetLocalJournalFilePath(userId);
				if (!FileAccess.FileExists(path)) return null;
				using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
				if (file == null) return null;
				string storedContent = file.GetAsText();
				if (string.IsNullOrWhiteSpace(storedContent)) return null;
				string json = SecurityVault.DecryptLocalText(storedContent);
				if (string.IsNullOrWhiteSpace(json)) return null;
				var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
				return JsonSerializer.Deserialize<ColonySaveData>(json, opts);
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// Compares the cloud row with the device's instant Write-Ahead Journal (user://colony_journal_{id}.json).
		/// If the player played for hours or closed/updated the app while a save was in flight or offline,
		/// the local journal has a newer timestamp and higher building levels—so we automatically recover 100% of their progress!
		/// </summary>
		private (ColonySaveData mergedRow, bool journalWasNewer) ReconcileCloudRowWithLocalJournal(ColonySaveData cloudRow, string userId)
		{
			var localRow = LoadLocalDeviceJournal(userId);
			if (localRow == null) return (cloudRow, false);
			if (cloudRow == null) return (localRow, true);

			bool journalRecoveredAnyProgress = false;

			// 1. Check if Local Journal has a newer sync timestamp than the Cloud Row
			bool localTimestampIsNewer = (localRow.last_sync_time > cloudRow.last_sync_time.AddSeconds(2));

			// Check if Cloud Row has recorded a newer combat resolution (__meta_last_resolved_flt) or siege damage (__hp_*)
			// so a stale local journal NEVER overwrites post-battle resource losses, garrison casualties, or siege damage!
			bool cloudHasNewerCombat = false;
			if (cloudRow.building_levels != null)
			{
				int cloudResolvedFlt = cloudRow.building_levels.GetValueOrDefault("__meta_last_resolved_flt", 0);
				int localResolvedFlt = localRow.building_levels != null ? localRow.building_levels.GetValueOrDefault("__meta_last_resolved_flt", 0) : 0;
				if (cloudResolvedFlt != 0 && cloudResolvedFlt != localResolvedFlt)
				{
					cloudHasNewerCombat = true;
				}
				if (!cloudHasNewerCombat)
				{
					foreach (var kv in cloudRow.building_levels)
					{
						if (kv.Key.StartsWith("__hp_", StringComparison.Ordinal) || kv.Key.EndsWith("__base_destroyed", StringComparison.Ordinal) || kv.Key == "__base_destroyed")
						{
							int lVal = localRow.building_levels != null ? localRow.building_levels.GetValueOrDefault(kv.Key, -1) : -1;
							if (kv.Value != lVal)
							{
								cloudHasNewerCombat = true;
								break;
							}
						}
					}
				}
			}

			// 2. Always take the highest building levels & unit backups between Cloud and Local Journal (unless siege damage is recorded)
			if (localRow.building_levels != null)
			{
				if (cloudRow.building_levels == null)
				{
					cloudRow.building_levels = new Dictionary<string, int>(localRow.building_levels);
					journalRecoveredAnyProgress = true;
				}
				else
				{
					foreach (var kv in localRow.building_levels)
					{
						int cloudVal = cloudRow.building_levels.GetValueOrDefault(kv.Key, 0);
						bool isMetaKey = kv.Key.StartsWith("__", StringComparison.Ordinal) && !kv.Key.Contains("_bld_");
						bool hasCloudSiegeDmg = cloudRow.building_levels.ContainsKey($"__hp_{kv.Key}");
						if (!isMetaKey && !hasCloudSiegeDmg && !cloudHasNewerCombat && kv.Value > cloudVal)
						{
							cloudRow.building_levels[kv.Key] = kv.Value;
							journalRecoveredAnyProgress = true;
						}
						else if (localTimestampIsNewer && !cloudHasNewerCombat && isMetaKey && kv.Value > 0)
						{
							cloudRow.building_levels[kv.Key] = kv.Value;
						}
					}
				}
			}

			// 3. If the local journal was written more recently than the cloud row (and no newer combat occurred on cloud),
			// restore the player's latest active build, build queue, unit bays, fleets, and resource balances from the journal!
			if (localTimestampIsNewer && !cloudHasNewerCombat)
			{
				cloudRow.res_e = localRow.res_e;
				cloudRow.res_i = localRow.res_i;
				cloudRow.res_t = localRow.res_t;
				cloudRow.res_h3 = localRow.res_h3;
				cloudRow.mgold = Math.Max(cloudRow.mgold, localRow.mgold);
				cloudRow.active_build = localRow.active_build;
				if (localRow.build_queue != null) cloudRow.build_queue = localRow.build_queue;
				if (localRow.active_boosts != null) cloudRow.active_boosts = localRow.active_boosts;
				if (localRow.unit_inventory != null) cloudRow.unit_inventory = localRow.unit_inventory;
				if (localRow.unit_bays != null) cloudRow.unit_bays = localRow.unit_bays;
				if (localRow.saved_fleets != null) cloudRow.saved_fleets = localRow.saved_fleets;
				cloudRow.last_sync_time = localRow.last_sync_time;
				journalRecoveredAnyProgress = true;
				GD.Print($"[LOCAL JOURNAL RECOVERY] Restored newer local device progress from {localRow.last_sync_time:O} and syncing to Supabase!");
			}

			return (cloudRow, journalRecoveredAnyProgress);
		}

		public async Task<Dictionary<string, ServerColonySummary>> FetchCommanderServerSummariesAsync()
		{
			var summaries = new Dictionary<string, ServerColonySummary>();
			foreach (var srv in GameData.Servers)
			{
				summaries[srv.Id] = new ServerColonySummary { ServerId = srv.Id, HasColony = false };
			}

			if (string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(AccessToken)) return summaries;

			await EnsureValidAuthTokenAsync();
			string url = $"{SupabaseUrl}/rest/v1/colonies?id=eq.{UserId}&select=*";
			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}",
				$"Authorization: Bearer {AccessToken}"
			};

			try
			{
				var (code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Get);
				if (code == 401 || code == 403)
				{
					await EnsureValidAuthTokenAsync(forceRefresh: true);
					headers[2] = $"Authorization: Bearer {AccessToken}";
					(code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Get);
				}
				if (code == 200 && !string.IsNullOrEmpty(responseText) && responseText.Trim() != "[]")
				{
					var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
					var colonies = JsonSerializer.Deserialize<List<ColonySaveData>>(responseText, options);
					if (colonies != null && colonies.Count > 0)
					{
						var (rootRow, _) = ReconcileCloudRowWithLocalJournal(colonies[0], UserId);
						_isolatedServerSnapshots[UserId] = rootRow;
						EnsureCoordinateAssigned(rootRow, "S1");

						// S1 Summary (Root Colony)
						var s1Levels = ExtractServerBuildingLevels(rootRow, "S1");
						if (s1Levels.GetValueOrDefault("hub_cmd", 0) > 0)
						{
							summaries["S1"] = new ServerColonySummary
							{
								ServerId = "S1",
								HasColony = true,
								MoonId = s1Levels.GetValueOrDefault("__meta_moon", 100),
								SlotId = s1Levels.GetValueOrDefault("__meta_slot", 1),
								CoreLevel = s1Levels.GetValueOrDefault("hub_cmd", 1),
								Population = CalculateColonyPopulation(s1Levels)
							};
						}

						// S2 Summary (Isolated Sub-Universe)
						if (HasInitializedServerColony(rootRow, "S2"))
						{
							var s2Levels = ExtractServerBuildingLevels(rootRow, "S2");
							summaries["S2"] = new ServerColonySummary
							{
								ServerId = "S2",
								HasColony = true,
								MoonId = s2Levels.GetValueOrDefault("__meta_moon", 100),
								SlotId = s2Levels.GetValueOrDefault("__meta_slot", 1),
								CoreLevel = s2Levels.GetValueOrDefault("hub_cmd", 1),
								Population = CalculateColonyPopulation(s2Levels)
							};
						}
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SUPABASE] FetchCommanderServerSummariesAsync Error: {ex.Message}");
			}

			return summaries;
		}

		public static bool HasInitializedServerColony(ColonySaveData rawRow, string serverId)
		{
			if (rawRow?.building_levels == null) return false;
			if (string.IsNullOrEmpty(serverId) || serverId == "S1")
			{
				if (rawRow.building_levels.GetValueOrDefault("__base_destroyed", 0) == 1) return false;
				if (rawRow.building_levels.GetValueOrDefault("hub_cmd", 0) > 0) return true;
				if (rawRow.building_levels.GetValueOrDefault("__meta_moon", 0) > 0) return true;
				foreach (var kv in rawRow.building_levels)
				{
					if (!kv.Key.StartsWith("__", StringComparison.Ordinal) && kv.Value > 0) return true;
				}
				return rawRow.moon_id > 0;
			}

			string initKey = $"__srv_{serverId}_init";
			if (rawRow.building_levels.GetValueOrDefault($"__srv_{serverId}_bld___base_destroyed", 0) == 1) return false;
			return rawRow.building_levels.GetValueOrDefault(initKey, 0) == 1;
		}

		public static Dictionary<string, int> ExtractServerBuildingLevels(ColonySaveData rawRow, string serverId)
		{
			var cleanDict = new Dictionary<string, int>();
			if (rawRow?.building_levels == null) return cleanDict;

			if (string.IsNullOrEmpty(serverId) || serverId == "S1")
			{
				foreach (var kvp in rawRow.building_levels)
				{
					// Strip out any __srv_S2_* keys so S1 only sees pure S1 keys!
					if (kvp.Key.StartsWith("__srv_", StringComparison.Ordinal)) continue;
					cleanDict[kvp.Key] = kvp.Value;
				}

				// Self-Healing Guard: If S1 building keys were ever missing/wiped (e.g. only __srv_S2_* keys existed in the row),
				// automatically restore S1's foundational starter colony levels so S1 never shows as "No Colony" or triggers a false wipe!
				bool anyStandingOnS1 = false;
				foreach (var b in GameMath.SiegeTargetBuildings)
				{
					if (cleanDict.GetValueOrDefault(b.Id, 0) > 0)
					{
						anyStandingOnS1 = true;
						break;
					}
				}
				if (!anyStandingOnS1)
				{
					// Strictly use persistent UUID or root moon_id/slot_id columns — NEVER derive coordinates from mutable commander_name or base_name!
					string persistentIdSeed = !string.IsNullOrWhiteSpace(rawRow.id) ? rawRow.id.Trim() : (Instance?.UserId ?? "DEFAULT_UUID");
					var coord = DeriveDeterministicCoordinate(persistentIdSeed);
					int rootMoon = (rawRow.moon_id >= 1 && rawRow.moon_id <= 1000) ? rawRow.moon_id : coord.moon;
					int rootSlot = (rawRow.slot_id >= 1 && rawRow.slot_id <= 20) ? rawRow.slot_id : coord.slot;
					cleanDict["hub_cmd"] = Math.Max(1, cleanDict.GetValueOrDefault("hub_cmd", 1));
					cleanDict["hub_silo"] = Math.Max(1, cleanDict.GetValueOrDefault("hub_silo", 1));
					cleanDict["hub_mgd"] = Math.Max(1, cleanDict.GetValueOrDefault("hub_mgd", 1));
					cleanDict["dist_e_0"] = Math.Max(1, cleanDict.GetValueOrDefault("dist_e_0", 1));
					cleanDict["dist_i_0"] = Math.Max(1, cleanDict.GetValueOrDefault("dist_i_0", 1));
					cleanDict["dist_t_0"] = Math.Max(1, cleanDict.GetValueOrDefault("dist_t_0", 1));
					cleanDict["dist_h3_0"] = Math.Max(1, cleanDict.GetValueOrDefault("dist_h3_0", 1));
					if (cleanDict.GetValueOrDefault("__meta_moon", 0) <= 0) cleanDict["__meta_moon"] = rootMoon;
					if (cleanDict.GetValueOrDefault("__meta_slot", 0) <= 0) cleanDict["__meta_slot"] = rootSlot;
				}
				else
				{
					// Even when buildings are standing, if __meta_moon or __meta_slot were missing, anchor to root moon_id / slot_id or persistent UUID
					if (cleanDict.GetValueOrDefault("__meta_moon", 0) <= 0 && rawRow.moon_id >= 1 && rawRow.moon_id <= 1000)
					{
						cleanDict["__meta_moon"] = rawRow.moon_id;
					}
					if (cleanDict.GetValueOrDefault("__meta_slot", 0) <= 0 && rawRow.slot_id >= 1 && rawRow.slot_id <= 20)
					{
						cleanDict["__meta_slot"] = rawRow.slot_id;
					}
				}

				return cleanDict;
			}

			string prefix = $"__srv_{serverId}_bld_";
			foreach (var kvp in rawRow.building_levels)
			{
				if (kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
				{
					string innerKey = kvp.Key.Substring(prefix.Length);
					cleanDict[innerKey] = kvp.Value;
				}
			}
			return cleanDict;
		}

		private ColonySaveData UnpackIsolatedServerColony(ColonySaveData rawRow, string serverId)
		{
			if (rawRow == null) return null;
			string srv = string.IsNullOrWhiteSpace(serverId) ? "S1" : serverId.Trim().ToUpper();

			if (srv == "S1")
			{
				string s1BaseName = GetServerCustomBaseName(rawRow, "S1");
				var s1Levels = ExtractServerBuildingLevels(rawRow, "S1");
				int s1Moon = s1Levels.GetValueOrDefault("__meta_moon", rawRow.moon_id);
				int s1Slot = s1Levels.GetValueOrDefault("__meta_slot", rawRow.slot_id);

				var s1Col = new ColonySaveData
				{
					id = rawRow.id,
					commander_name = rawRow.commander_name,
					base_name = s1BaseName,
					alliance_name = string.IsNullOrEmpty(rawRow.alliance_name) ? "NONE" : rawRow.alliance_name,
					moon_id = s1Moon,
					slot_id = s1Slot,
					res_e = rawRow.res_e,
					res_i = rawRow.res_i,
					res_t = rawRow.res_t,
					res_h3 = rawRow.res_h3,
					mgold = rawRow.mgold,
					last_sync_time = rawRow.last_sync_time,
					building_levels = s1Levels,
					active_build = (rawRow.active_build != null && !IsServerTaggedBuild(rawRow.active_build, "S2")) ? rawRow.active_build : null,
					build_queue = new List<GameMath.BuildQueueItem>(),
					active_boosts = new Dictionary<string, GameMath.BoostData>(),
					unit_inventory = new Dictionary<string, int>(),
					unit_bays = new Dictionary<string, GameMath.UnitAssemblyBayJob>(),
					saved_fleets = new List<GameMath.FleetManifest>()
				};

				if (rawRow.build_queue != null)
				{
					foreach (var q in rawRow.build_queue)
					{
						if (q != null && !IsServerTaggedBuild(q, "S2")) s1Col.build_queue.Add(q);
					}
				}

				if (rawRow.active_boosts != null)
				{
					foreach (var kv in rawRow.active_boosts)
					{
						if (!kv.Key.StartsWith("__srv_", StringComparison.Ordinal)) s1Col.active_boosts[kv.Key] = kv.Value;
					}
				}

				if (rawRow.unit_inventory != null)
				{
					foreach (var kv in rawRow.unit_inventory)
					{
						if (!kv.Key.StartsWith("__srv_", StringComparison.Ordinal)) s1Col.unit_inventory[kv.Key] = kv.Value;
					}
				}

				if (rawRow.unit_bays != null)
				{
					foreach (var kv in rawRow.unit_bays)
					{
						if (!kv.Key.StartsWith("__srv_", StringComparison.Ordinal)) s1Col.unit_bays[kv.Key] = kv.Value;
					}
				}

				if (rawRow.saved_fleets != null)
				{
					foreach (var f in rawRow.saved_fleets)
					{
						if (f != null && !IsServerTaggedFleet(f, "S2")) s1Col.saved_fleets.Add(f);
					}
				}

				EnsureCoordinateAssigned(s1Col, "S1");
				return s1Col;
			}

			// Server 2 (or other secondary server): Unpack isolated sub-state from rawRow without touching S1!
			bool isInitialized = HasInitializedServerColony(rawRow, srv);
			var srvLevels = ExtractServerBuildingLevels(rawRow, srv);

			if (!isInitialized || srvLevels.Count == 0)
			{
				string persistentSrvSeed = $"{(!string.IsNullOrWhiteSpace(rawRow.id) ? rawRow.id.Trim() : (UserId ?? "DEFAULT_UUID"))}_{srv}";
				var coord = DeriveDeterministicCoordinate(persistentSrvSeed);
				srvLevels = new Dictionary<string, int>
				{
					{ "hub_cmd", 1 },
					{ "hub_silo", 1 },
					{ "hub_mgd", 1 },
					{ "dist_e_0", 1 },
					{ "dist_i_0", 1 },
					{ "dist_t_0", 1 },
					{ "dist_h3_0", 1 },
					{ "__meta_moon", coord.moon },
					{ "__meta_slot", coord.slot }
				};
			}

			string metaPrefix = $"__srv_{srv}_";
			var rawDict = rawRow.building_levels ?? new Dictionary<string, int>();

			double srvResE = isInitialized ? rawDict.GetValueOrDefault($"{metaPrefix}res_e", 5000) : 5000;
			double srvResI = isInitialized ? rawDict.GetValueOrDefault($"{metaPrefix}res_i", 5000) : 5000;
			double srvResT = isInitialized ? rawDict.GetValueOrDefault($"{metaPrefix}res_t", 5000) : 5000;
			double srvResH3 = isInitialized ? rawDict.GetValueOrDefault($"{metaPrefix}res_h3", 2500) : 2500;
			long srvSyncSec = isInitialized ? (long)rawDict.GetValueOrDefault($"{metaPrefix}sync_sec", 0) : 0;
			DateTimeOffset srvLastSync = srvSyncSec > 1700000000L
				? DateTimeOffset.FromUnixTimeSeconds(srvSyncSec)
				: DateTimeOffset.UtcNow;

			string srvBaseName = GetServerCustomBaseName(rawRow, srv);

			var srvCol = new ColonySaveData
			{
				id = rawRow.id,
				commander_name = rawRow.commander_name,
				base_name = srvBaseName,
				alliance_name = string.IsNullOrEmpty(rawRow.alliance_name) ? "NONE" : rawRow.alliance_name,
				moon_id = srvLevels.GetValueOrDefault("__meta_moon", 0),
				slot_id = srvLevels.GetValueOrDefault("__meta_slot", 0),
				res_e = srvResE,
				res_i = srvResI,
				res_t = srvResT,
				res_h3 = srvResH3,
				mgold = rawRow.mgold, // Account-wide Galaxy Gold balance
				last_sync_time = srvLastSync,
				building_levels = srvLevels,
				active_build = null,
				build_queue = new List<GameMath.BuildQueueItem>(),
				active_boosts = new Dictionary<string, GameMath.BoostData>(),
				unit_inventory = new Dictionary<string, int>(),
				unit_bays = new Dictionary<string, GameMath.UnitAssemblyBayJob>(),
				saved_fleets = new List<GameMath.FleetManifest>()
			};

			// Unpack S2 Active Build & Queue
			string buildTag = $"[{srv}]";
			if (rawRow.active_build != null && IsServerTaggedBuild(rawRow.active_build, srv))
			{
				srvCol.active_build = CloneAndStripServerBuildTag(rawRow.active_build, buildTag);
			}
			if (rawRow.build_queue != null)
			{
				foreach (var q in rawRow.build_queue)
				{
					if (q != null && q.BuildingId == $"__active_{srv}")
					{
						var cloned = CloneAndStripServerBuildTag(q, buildTag);
						cloned.BuildingId = q.BuildingName.Contains("::") ? q.BuildingName.Split("::")[0] : cloned.BuildingId;
						if (q.BuildingName.Contains("::")) cloned.BuildingName = q.BuildingName.Split("::")[1];
						srvCol.active_build = cloned;
					}
					else if (q != null && IsServerTaggedBuild(q, srv))
					{
						srvCol.build_queue.Add(CloneAndStripServerBuildTag(q, buildTag));
					}
				}
			}

			// Unpack S2 Boosts
			if (rawRow.active_boosts != null)
			{
				foreach (var kv in rawRow.active_boosts)
				{
					if (kv.Key.StartsWith(metaPrefix, StringComparison.Ordinal))
					{
						srvCol.active_boosts[kv.Key.Substring(metaPrefix.Length)] = kv.Value;
					}
				}
			}

			// Unpack S2 Unit Inventory
			if (rawRow.unit_inventory != null)
			{
				foreach (var kv in rawRow.unit_inventory)
				{
					if (kv.Key.StartsWith(metaPrefix, StringComparison.Ordinal))
					{
						srvCol.unit_inventory[kv.Key.Substring(metaPrefix.Length)] = kv.Value;
					}
				}
			}
			foreach (var u in GameData.Units)
			{
				if (!srvCol.unit_inventory.ContainsKey(u.Id))
				{
					srvCol.unit_inventory[u.Id] = srvLevels.GetValueOrDefault($"__meta_unit_{u.Id}", 0);
				}
			}

			// Unpack S2 Unit Bays
			if (rawRow.unit_bays != null)
			{
				foreach (var kv in rawRow.unit_bays)
				{
					if (kv.Key.StartsWith(metaPrefix, StringComparison.Ordinal))
					{
						srvCol.unit_bays[kv.Key.Substring(metaPrefix.Length)] = kv.Value;
					}
				}
			}

			// Unpack S2 Fleets
			if (rawRow.saved_fleets != null)
			{
				foreach (var f in rawRow.saved_fleets)
				{
					if (f != null && IsServerTaggedFleet(f, srv))
					{
						srvCol.saved_fleets.Add(f);
					}
				}
			}

			EnsureCoordinateAssigned(srvCol, srv);
			return srvCol;
		}

		private static bool IsServerTaggedBuild(GameMath.BuildQueueItem item, string serverId)
		{
			if (item == null) return false;
			return (item.BuildingId != null && item.BuildingId.StartsWith($"__srv_{serverId}_", StringComparison.Ordinal)) ||
				   (item.BuildingId != null && item.BuildingId == $"__active_{serverId}") ||
				   (item.BuildingName != null && item.BuildingName.StartsWith($"[{serverId}]", StringComparison.Ordinal));
		}

		private static GameMath.BuildQueueItem CloneAndStripServerBuildTag(GameMath.BuildQueueItem item, string tagPrefix)
		{
			string cleanId = item.BuildingId ?? "";
			foreach (var srv in GameData.Servers)
			{
				string p = $"__srv_{srv.Id}_";
				if (cleanId.StartsWith(p, StringComparison.Ordinal)) cleanId = cleanId.Substring(p.Length);
			}
			string cleanName = item.BuildingName ?? "";
			if (cleanName.StartsWith(tagPrefix, StringComparison.Ordinal)) cleanName = cleanName.Substring(tagPrefix.Length).Trim();

			return new GameMath.BuildQueueItem
			{
				BuildingId = cleanId,
				BuildingName = cleanName,
				TargetLevel = item.TargetLevel,
				DurationLeft = item.DurationLeft,
				CompletionTimeUnixMs = item.CompletionTimeUnixMs,
				CostE = item.CostE,
				CostI = item.CostI,
				CostT = item.CostT,
				CostH3 = item.CostH3
			};
		}

		private static bool IsServerTaggedFleet(GameMath.FleetManifest fleet, string serverId)
		{
			if (fleet == null) return false;
			string loc = fleet.LocationCoord ?? "";
			string tgt = fleet.TargetCoord ?? "";
			string id = fleet.Id ?? "";
			return loc.StartsWith($"{serverId}:", StringComparison.OrdinalIgnoreCase) ||
				   tgt.StartsWith($"{serverId}:", StringComparison.OrdinalIgnoreCase) ||
				   id.StartsWith($"{serverId}_", StringComparison.OrdinalIgnoreCase);
		}

		public async Task<ColonySaveData> FetchColonyStateAsync(bool forceRefreshEvenIfCached = false)
		{
			if (string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(AccessToken)) return null;

			// Wait for any in-flight SaveColonyStateAsync PATCH to finish first so we NEVER fetch stale pre-save cloud state!
			await _saveSyncLock.WaitAsync();
			try
			{
				string activeSrv = string.IsNullOrWhiteSpace(CachedServerId) ? "S1" : CachedServerId.Trim().ToUpper();
				ActiveServerId = activeSrv;

				// If a fleet was just launched or mutated locally within the last 4 seconds and we already have a verified session cache,
				// return the live in-memory session state directly so scene transitions (Base <-> Moon <-> Galaxy) are 100% atomic!
				long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				if (!forceRefreshEvenIfCached && HasValidSessionColonyCache && (nowMs - LastLocalFleetMutationUtcMs) < 4000L && _isolatedServerSnapshots.TryGetValue(UserId, out ColonySaveData recentSnap) && recentSnap != null)
				{
					var localCol = UnpackIsolatedServerColony(recentSnap, activeSrv);
					PopulateStaticSessionCacheFromColony(localCol, preserveNewerLocalFleets: true);
					return localCol;
				}

				await EnsureValidAuthTokenAsync();
				string url = $"{SupabaseUrl}/rest/v1/colonies?id=eq.{UserId}&select=*";

				string[] headers = new string[]
				{
					"Content-Type: application/json",
					$"apikey: {SupabaseAnonKey}",
					$"Authorization: Bearer {AccessToken}"
				};

				var (code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Get);
				if (code == 401 || code == 403)
				{
					await EnsureValidAuthTokenAsync(forceRefresh: true);
					headers[2] = $"Authorization: Bearer {AccessToken}";
					(code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Get);
				}

				if (code == 200)
				{
					if (responseText.Trim() == "[]")
					{
						GD.Print("[SUPABASE] Colony row missing! Initiating Self-Healing Genesis...");
						bool created = await CreateInitialColonyRowAsync();
						if (created)
						{
							var retry = await SendGodotRequestAsync(url, headers, HttpClient.Method.Get);
							responseText = retry.responseText;
						}
						else
						{
							return null;
						}
					}

					var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
					var colonies = JsonSerializer.Deserialize<List<ColonySaveData>>(responseText, options);

					if (colonies != null && colonies.Count > 0)
					{
						var (rawRow, journalWasNewer) = ReconcileCloudRowWithLocalJournal(colonies[0], UserId);
						// Cache the raw row so when saving S2 we preserve S1's data 100% intact (and vice versa!)
						_isolatedServerSnapshots[UserId] = rawRow;
						SaveLocalDeviceJournal(UserId, rawRow);

						if ((string.IsNullOrEmpty(rawRow.commander_name) || rawRow.commander_name == "COMMANDER ALPHA") && !string.IsNullOrEmpty(LoggedInUsername))
						{
							rawRow.commander_name = LoggedInUsername;
						}

						var col = UnpackIsolatedServerColony(rawRow, activeSrv);

						PopulateStaticSessionCacheFromColony(col, preserveNewerLocalFleets: HasValidSessionColonyCache);

						GD.Print($"[SUPABASE] Isolated Colony state for Server [{activeSrv}] ({CachedServerSpeed}x Speed) successfully fetched from cloud (JournalRecovered={journalWasNewer}).");
						return col;
					}
				}
				else
				{
					// Offline / Network Timeout Fallback: If the player's device has a verified local journal for this UserId,
					// load their journal so they never lose progress on spotty mobile networks!
					var localFallback = LoadLocalDeviceJournal(UserId);
					if (localFallback != null)
					{
						_isolatedServerSnapshots[UserId] = localFallback;
						var col = UnpackIsolatedServerColony(localFallback, activeSrv);
						PopulateStaticSessionCacheFromColony(col, preserveNewerLocalFleets: HasValidSessionColonyCache);
						GD.Print($"[SUPABASE] Network fetch returned ({code}); restored colony state from local device journal.");
						return col;
					}
				}
				
				GD.PrintErr($"[SUPABASE] Fetch failed ({code}): {responseText}");
				return null;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SUPABASE] Fetch Network Error: {ex.Message}");
				return null;
			}
			finally
			{
				_saveSyncLock.Release();
			}
		}

		public static void EnsureCoordinateAssigned(ColonySaveData col, string serverId = null)
		{
			if (col == null) return;
			if (col.building_levels == null) col.building_levels = new Dictionary<string, int>();

			string srv = string.IsNullOrWhiteSpace(serverId) ? CachedServerId : serverId.Trim().ToUpper();
			int moon = col.building_levels.GetValueOrDefault("__meta_moon", 0);
			int slot = col.building_levels.GetValueOrDefault("__meta_slot", 0);

			if (srv == "S1")
			{
				if ((moon < 1 || moon > 1000) && col.moon_id >= 1 && col.moon_id <= 1000) moon = col.moon_id;
				if ((slot < 1 || slot > 20) && col.slot_id >= 1 && col.slot_id <= 20) slot = col.slot_id;
			}

			if (moon < 1 || moon > 1000 || slot < 1 || slot > 20)
			{
				// Strictly use persistent UUID (col.id or Instance.UserId) — NEVER derive coordinates from mutable commander_name or base_name!
				string persistentUuid = !string.IsNullOrWhiteSpace(col.id)
					? col.id.Trim()
					: (!string.IsNullOrWhiteSpace(Instance?.UserId) ? Instance.UserId.Trim() : "DEFAULT_UUID");
				string seed = srv == "S1" ? persistentUuid : $"{persistentUuid}_{srv}";
				var assigned = DeriveDeterministicCoordinate(seed);
				moon = assigned.moon;
				slot = assigned.slot;
			}

			col.building_levels["__meta_moon"] = moon;
			col.building_levels["__meta_slot"] = slot;
			col.moon_id = moon;
			col.slot_id = slot;
		}

		public static (int moon, int slot) DeriveDeterministicCoordinate(string seedKey)
		{
			if (string.IsNullOrEmpty(seedKey)) return (100, 1);
			uint hash = 2166136261u;
			for (int i = 0; i < seedKey.Length; i++)
			{
				hash ^= seedKey[i];
				hash *= 16777619u;
			}
			// Cluster early testers on Moons 98..105 first so they see each other on nearby Moons & Moon Slots!
			int[] starterMoons = new int[] { 100, 100, 100, 101, 102, 99, 98, 103, 104, 105 };
			int moon = starterMoons[hash % (uint)starterMoons.Length];
			int slot = (int)((hash / 13u) % 20u) + 1;
			return (moon, slot);
		}

		public static long CalculateColonyPopulation(Dictionary<string, int> buildingLevels)
		{
			if (buildingLevels == null) return 700;
			long totalLvls = 0;
			foreach (var kvp in buildingLevels)
			{
				if (kvp.Key.StartsWith("__")) continue;
				if (kvp.Value > 0) totalLvls += kvp.Value;
			}
			return Math.Max(100, totalLvls * 100);
		}

		private async Task<bool> CreateInitialColonyRowAsync()
		{
			string url = $"{SupabaseUrl}/rest/v1/colonies";

			string initialCallsign = !string.IsNullOrEmpty(LoggedInUsername) ? LoggedInUsername : "COMMANDER ALPHA";
			var coord = DeriveDeterministicCoordinate(UserId ?? initialCallsign);

			var starterLevels = new Dictionary<string, int>
			{
				{ "hub_cmd", 1 },
				{ "hub_silo", 1 },
				{ "hub_mgd", 1 },
				{ "dist_e_0", 1 },
				{ "dist_i_0", 1 },
				{ "dist_t_0", 1 },
				{ "dist_h3_0", 1 },
				{ "__meta_moon", coord.moon },
				{ "__meta_slot", coord.slot }
			};

			var payload = new
			{
				id = UserId,
				commander_name = initialCallsign,
				res_e = 5000,
				res_i = 5000,
				res_t = 5000,
				res_h3 = 2500,
				mgold = 100,
				building_levels = starterLevels,
				last_sync_time = DateTimeOffset.UtcNow.ToString("o") 
			};

			string jsonBody = JsonSerializer.Serialize(payload);

			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}",
				$"Authorization: Bearer {AccessToken}",
				"Prefer: return=minimal"
			};

			try
			{
				var (code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Post, jsonBody);
				
				if (code == 201 || code == 204 || code == 200)
				{
					GD.Print($"[SUPABASE] Self-Healing Genesis Complete for {initialCallsign} at M:{coord.moon} B:{coord.slot}.");
					return true;
				}
				else
				{
					GD.PrintErr($"[SUPABASE] Self-Healing failed ({code}): {responseText}");
					return false;
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SUPABASE] Self-Healing Network Error: {ex.Message}");
				return false;
			}
		}

		public async Task SaveColonyStateAsync(
			double resE, double resI, double resT, double resH3, long gGold, 
			Dictionary<string, int> buildingLevels,
			GameMath.BuildQueueItem activeBuild,
			List<GameMath.BuildQueueItem> buildQueue,
			Dictionary<string, GameMath.BoostData> activeBoosts,
			string commanderNameOverride = null)
		{
			if (string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(AccessToken)) return;

			string activeSrv = string.IsNullOrWhiteSpace(CachedServerId) ? "S1" : CachedServerId.Trim().ToUpper();
			ActiveServerId = activeSrv;

			if (buildingLevels != null)
			{
				int existingMetaMoon = buildingLevels.GetValueOrDefault("__meta_moon", 0);
				int existingMetaSlot = buildingLevels.GetValueOrDefault("__meta_slot", 0);
				int safeMoon = (CachedMoonId >= 1 && CachedMoonId <= 1000)
					? CachedMoonId
					: ((existingMetaMoon >= 1 && existingMetaMoon <= 1000) ? existingMetaMoon : 100);
				int safeSlot = (CachedBaseSlot >= 1 && CachedBaseSlot <= 20)
					? CachedBaseSlot
					: ((existingMetaSlot >= 1 && existingMetaSlot <= 20) ? existingMetaSlot : 1);

				CachedMoonId = safeMoon;
				CachedBaseSlot = safeSlot;
				buildingLevels["__meta_moon"] = safeMoon;
				buildingLevels["__meta_slot"] = safeSlot;
				foreach (var u in GameData.Units)
				{
					buildingLevels[$"__meta_unit_{u.Id}"] = CachedUnitInventory.GetValueOrDefault(u.Id, 0);
				}
				CachedBuildingLevels = new Dictionary<string, int>(buildingLevels);
			}
			CachedResE = resE;
			CachedResI = resI;
			CachedResT = resT;
			CachedResH3 = resH3;
			CachedGGold = gGold;
			CachedActiveBuild = activeBuild;
			if (buildQueue != null) CachedBuildQueue = new List<GameMath.BuildQueueItem>(buildQueue);
			if (activeBoosts != null) CachedActiveBoosts = new Dictionary<string, GameMath.BoostData>(activeBoosts);
			SetServerCustomBaseName(activeSrv, CachedBaseName);
			HasValidSessionColonyCache = true;

			await _saveSyncLock.WaitAsync();
			try
			{
			string cmdToSave = !string.IsNullOrEmpty(commanderNameOverride)
				? commanderNameOverride
				: (!string.IsNullOrEmpty(CachedCommanderName) ? CachedCommanderName : "COMMANDER ALPHA");

			// Retrieve cached raw row so S1 and S2 data are merged cleanly without ever overwriting each other!
			if (!_isolatedServerSnapshots.TryGetValue(UserId, out ColonySaveData rawRow) || rawRow == null)
			{
				try
				{
					string fetchUrl = $"{SupabaseUrl}/rest/v1/colonies?id=eq.{UserId}&select=*";
					string[] fetchHeaders = new string[]
					{
						"Content-Type: application/json",
						$"apikey: {SupabaseAnonKey}",
						$"Authorization: Bearer {AccessToken}"
					};
					var (fCode, fText) = await SendGodotRequestAsync(fetchUrl, fetchHeaders, HttpClient.Method.Get);
					if (fCode == 200 && !string.IsNullOrEmpty(fText) && fText.Trim() != "[]")
					{
						var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
						var rows = JsonSerializer.Deserialize<List<ColonySaveData>>(fText, opts);
						if (rows != null && rows.Count > 0)
						{
							rawRow = rows[0];
							_isolatedServerSnapshots[UserId] = rawRow;
						}
					}
				}
				catch { }
			}

			if (rawRow == null)
			{
				var defaultS1Coord = DeriveDeterministicCoordinate(UserId ?? "DEFAULT_UUID");
				rawRow = new ColonySaveData
				{
					id = UserId,
					commander_name = cmdToSave,
					base_name = CachedBaseName,
					alliance_name = CachedAllianceName,
					moon_id = CachedMoonId > 0 ? CachedMoonId : defaultS1Coord.moon,
					slot_id = CachedBaseSlot > 0 ? CachedBaseSlot : defaultS1Coord.slot,
					res_e = 5000,
					res_i = 5000,
					res_t = 5000,
					res_h3 = 2500,
					mgold = gGold,
					last_sync_time = DateTimeOffset.UtcNow,
					building_levels = new Dictionary<string, int>
					{
						{ "hub_cmd", 1 },
						{ "hub_silo", 1 },
						{ "hub_mgd", 1 },
						{ "dist_e_0", 1 },
						{ "dist_i_0", 1 },
						{ "dist_t_0", 1 },
						{ "dist_h3_0", 1 },
						{ "__meta_moon", CachedMoonId > 0 ? CachedMoonId : defaultS1Coord.moon },
						{ "__meta_slot", CachedBaseSlot > 0 ? CachedBaseSlot : defaultS1Coord.slot }
					},
					build_queue = new List<GameMath.BuildQueueItem>(),
					active_boosts = new Dictionary<string, GameMath.BoostData>(),
					unit_inventory = new Dictionary<string, int>(),
					unit_bays = new Dictionary<string, GameMath.UnitAssemblyBayJob>(),
					saved_fleets = new List<GameMath.FleetManifest>()
				};
			}

			var mergedBlds = rawRow.building_levels != null
				? new Dictionary<string, int>(rawRow.building_levels)
				: new Dictionary<string, int>();

			var mergedBoosts = rawRow.active_boosts != null
				? new Dictionary<string, GameMath.BoostData>(rawRow.active_boosts)
				: new Dictionary<string, GameMath.BoostData>();

			var mergedUnits = rawRow.unit_inventory != null
				? new Dictionary<string, int>(rawRow.unit_inventory)
				: new Dictionary<string, int>();

			var mergedBays = rawRow.unit_bays != null
				? new Dictionary<string, GameMath.UnitAssemblyBayJob>(rawRow.unit_bays)
				: new Dictionary<string, GameMath.UnitAssemblyBayJob>();

			var mergedQueue = new List<GameMath.BuildQueueItem>();
			var mergedFleets = new List<GameMath.FleetManifest>();

			if (activeSrv == "S1")
			{
				// Remove old S1 keys while keeping all __srv_S2_* keys untouched
				var s1OldKeys = new List<string>();
				foreach (var k in mergedBlds.Keys)
				{
					if (!k.StartsWith("__srv_", StringComparison.Ordinal)) s1OldKeys.Add(k);
				}
				foreach (var k in s1OldKeys) mergedBlds.Remove(k);

				if (buildingLevels != null)
				{
					foreach (var kv in buildingLevels)
					{
						if (!kv.Key.StartsWith("__srv_", StringComparison.Ordinal))
						{
							// Anti-Regression Protection: If rawRow already had a higher level for this building and this wasn't a Laser Cannon siege,
							// never allow a stale Level 1 template dictionary to downgrade a player's building!
							int existingCloudVal = rawRow.building_levels != null ? rawRow.building_levels.GetValueOrDefault(kv.Key, 0) : 0;
							bool isMetaOrPosKey = kv.Key.StartsWith("__", StringComparison.Ordinal);
							bool hasSiegeDamageRecord = buildingLevels.ContainsKey($"__hp_{kv.Key}");
							if (!isMetaOrPosKey && !hasSiegeDamageRecord && existingCloudVal > kv.Value)
							{
								mergedBlds[kv.Key] = existingCloudVal;
								buildingLevels[kv.Key] = existingCloudVal;
							}
							else
							{
								mergedBlds[kv.Key] = kv.Value;
							}
						}
					}
				}

				// Merge S1 boosts while keeping __srv_S2_* boosts
				var s1OldBoosts = new List<string>();
				foreach (var k in mergedBoosts.Keys)
				{
					if (!k.StartsWith("__srv_", StringComparison.Ordinal)) s1OldBoosts.Add(k);
				}
				foreach (var k in s1OldBoosts) mergedBoosts.Remove(k);
				if (activeBoosts != null)
				{
					foreach (var kv in activeBoosts) mergedBoosts[kv.Key] = kv.Value;
				}

				// Merge S1 units while keeping __srv_S2_* units
				var s1OldUnits = new List<string>();
				foreach (var k in mergedUnits.Keys)
				{
					if (!k.StartsWith("__srv_", StringComparison.Ordinal)) s1OldUnits.Add(k);
				}
				foreach (var k in s1OldUnits) mergedUnits.Remove(k);
				if (CachedUnitInventory != null)
				{
					foreach (var kv in CachedUnitInventory) mergedUnits[kv.Key] = kv.Value;
				}

				// Merge S1 unit bays while keeping __srv_S2_* bays
				var s1OldBays = new List<string>();
				foreach (var k in mergedBays.Keys)
				{
					if (!k.StartsWith("__srv_", StringComparison.Ordinal)) s1OldBays.Add(k);
				}
				foreach (var k in s1OldBays) mergedBays.Remove(k);
				if (CachedUnitBays != null)
				{
					foreach (var kv in CachedUnitBays) mergedBays[kv.Key] = kv.Value;
				}

				// Merge S1 build queue while preserving any S2 queued items
				if (buildQueue != null)
				{
					foreach (var q in buildQueue)
					{
						if (q != null) mergedQueue.Add(q);
					}
				}
				if (rawRow.build_queue != null)
				{
					foreach (var q in rawRow.build_queue)
					{
						if (q != null && IsServerTaggedBuild(q, "S2")) mergedQueue.Add(q);
					}
				}

				// Merge S1 fleets while preserving S2 fleets
				if (CachedSavedFleets != null)
				{
					foreach (var f in CachedSavedFleets)
					{
						if (f != null && f.Status != "WATCHLIST_DATA") mergedFleets.Add(f);
					}
				}
				if (CachedWatchlist != null && CachedWatchlist.Count > 0)
				{
					mergedFleets.Add(new GameMath.FleetManifest
					{
						Id = "S1_WATCHLIST",
						Name = "WATCHLIST",
						LocationCoord = $"S1:{CachedMoonId}:{CachedBaseSlot}",
						Status = "WATCHLIST_DATA",
						Protocol = "IDLE",
						Cargo = new GameMath.FleetCargoHold { Watchlist = new List<GameMath.WatchlistEntry>(CachedWatchlist) }
					});
				}
				if (rawRow.saved_fleets != null)
				{
					foreach (var f in rawRow.saved_fleets)
					{
						if (f != null && IsServerTaggedFleet(f, "S2")) mergedFleets.Add(f);
					}
				}

				// Persist custom S1 Base Name inside active_boosts["__meta_base_name_S1"] as well as rawRow.base_name so even if optional column fallback occurs, custom base names are 100% preserved!
				mergedBoosts["__meta_base_name_S1"] = new GameMath.BoostData
				{
					Multiplier = 1.0f,
					Expiration = DateTimeOffset.UtcNow.AddYears(10),
					Label = string.IsNullOrWhiteSpace(CachedBaseName) ? "Alpha Outpost" : CachedBaseName.Trim()
				};

				rawRow.commander_name = cmdToSave;
				rawRow.base_name = CachedBaseName;
				rawRow.alliance_name = CachedAllianceName;
				rawRow.moon_id = CachedMoonId;
				rawRow.slot_id = CachedBaseSlot;
				rawRow.res_e = resE;
				rawRow.res_i = resI;
				rawRow.res_t = resT;
				rawRow.res_h3 = resH3;
				rawRow.mgold = gGold;
				rawRow.building_levels = mergedBlds;
				rawRow.active_build = activeBuild;
				rawRow.build_queue = mergedQueue;
				rawRow.active_boosts = mergedBoosts;
				rawRow.unit_inventory = mergedUnits;
				rawRow.unit_bays = mergedBays;
				rawRow.saved_fleets = mergedFleets;
				rawRow.last_sync_time = DateTimeOffset.UtcNow;
			}
			else
			{
				// Saving Server 2 (S2) isolated state — S1 root columns remain 100% untouched!
				string metaPrefix = $"__srv_{activeSrv}_";
				string bldPrefix = $"__srv_{activeSrv}_bld_";

				var oldSrvKeys = new List<string>();
				foreach (var k in mergedBlds.Keys)
				{
					if (k.StartsWith(metaPrefix, StringComparison.Ordinal)) oldSrvKeys.Add(k);
				}
				foreach (var k in oldSrvKeys) mergedBlds.Remove(k);

				mergedBlds[$"{metaPrefix}init"] = 1;
				mergedBlds[$"{metaPrefix}res_e"] = (int)Math.Round(resE);
				mergedBlds[$"{metaPrefix}res_i"] = (int)Math.Round(resI);
				mergedBlds[$"{metaPrefix}res_t"] = (int)Math.Round(resT);
				mergedBlds[$"{metaPrefix}res_h3"] = (int)Math.Round(resH3);
				mergedBlds[$"{metaPrefix}sync_sec"] = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

				if (buildingLevels != null)
				{
					foreach (var kv in buildingLevels)
					{
						if (!kv.Key.StartsWith("__srv_", StringComparison.Ordinal))
						{
							string targetKey = $"{bldPrefix}{kv.Key}";
							int existingCloudVal = rawRow.building_levels != null ? rawRow.building_levels.GetValueOrDefault(targetKey, 0) : 0;
							bool isMetaOrPosKey = kv.Key.StartsWith("__", StringComparison.Ordinal);
							bool hasSiegeDamageRecord = buildingLevels.ContainsKey($"__hp_{kv.Key}");
							if (!isMetaOrPosKey && !hasSiegeDamageRecord && existingCloudVal > kv.Value)
							{
								mergedBlds[targetKey] = existingCloudVal;
								buildingLevels[kv.Key] = existingCloudVal;
							}
							else
							{
								mergedBlds[targetKey] = kv.Value;
							}
						}
					}
				}

				// Merge S2 Boosts + S2 Custom Base Name
				var oldSrvBoosts = new List<string>();
				foreach (var k in mergedBoosts.Keys)
				{
					if (k.StartsWith(metaPrefix, StringComparison.Ordinal)) oldSrvBoosts.Add(k);
				}
				foreach (var k in oldSrvBoosts) mergedBoosts.Remove(k);
				if (activeBoosts != null)
				{
					foreach (var kv in activeBoosts) mergedBoosts[$"{metaPrefix}{kv.Key}"] = kv.Value;
				}
				mergedBoosts[$"{metaPrefix}base_name"] = new GameMath.BoostData
				{
					Multiplier = 1.0f,
					Expiration = DateTimeOffset.UtcNow.AddYears(10),
					Label = string.IsNullOrWhiteSpace(CachedBaseName) ? $"{activeSrv} Outpost" : CachedBaseName.Trim()
				};

				// Merge S2 Units
				var oldSrvUnits = new List<string>();
				foreach (var k in mergedUnits.Keys)
				{
					if (k.StartsWith(metaPrefix, StringComparison.Ordinal)) oldSrvUnits.Add(k);
				}
				foreach (var k in oldSrvUnits) mergedUnits.Remove(k);
				if (CachedUnitInventory != null)
				{
					foreach (var kv in CachedUnitInventory) mergedUnits[$"{metaPrefix}{kv.Key}"] = kv.Value;
				}

				// Merge S2 Unit Bays
				var oldSrvBays = new List<string>();
				foreach (var k in mergedBays.Keys)
				{
					if (k.StartsWith(metaPrefix, StringComparison.Ordinal)) oldSrvBays.Add(k);
				}
				foreach (var k in oldSrvBays) mergedBays.Remove(k);
				if (CachedUnitBays != null)
				{
					foreach (var kv in CachedUnitBays) mergedBays[$"{metaPrefix}{kv.Key}"] = kv.Value;
				}

				// Preserve S1 build queue items and append tagged S2 activeBuild + buildQueue
				if (rawRow.build_queue != null)
				{
					foreach (var q in rawRow.build_queue)
					{
						if (q != null && !IsServerTaggedBuild(q, activeSrv)) mergedQueue.Add(q);
					}
				}
				if (activeBuild != null)
				{
					mergedQueue.Add(new GameMath.BuildQueueItem
					{
						BuildingId = $"__active_{activeSrv}",
						BuildingName = $"{activeBuild.BuildingId}::{activeBuild.BuildingName}",
						TargetLevel = activeBuild.TargetLevel,
						DurationLeft = activeBuild.DurationLeft,
						CompletionTimeUnixMs = activeBuild.CompletionTimeUnixMs,
						CostE = activeBuild.CostE,
						CostI = activeBuild.CostI,
						CostT = activeBuild.CostT,
						CostH3 = activeBuild.CostH3
					});
				}
				if (buildQueue != null)
				{
					foreach (var q in buildQueue)
					{
						if (q == null) continue;
						mergedQueue.Add(new GameMath.BuildQueueItem
						{
							BuildingId = $"__srv_{activeSrv}_{q.BuildingId}",
							BuildingName = $"[{activeSrv}] {q.BuildingName}",
							TargetLevel = q.TargetLevel,
							DurationLeft = q.DurationLeft,
							CompletionTimeUnixMs = q.CompletionTimeUnixMs,
							CostE = q.CostE,
							CostI = q.CostI,
							CostT = q.CostT,
							CostH3 = q.CostH3
						});
					}
				}

				// Preserve S1 fleets and append tagged S2 fleets
				if (rawRow.saved_fleets != null)
				{
					foreach (var f in rawRow.saved_fleets)
					{
						if (f != null && !IsServerTaggedFleet(f, activeSrv)) mergedFleets.Add(f);
					}
				}
				if (CachedSavedFleets != null)
				{
					foreach (var f in CachedSavedFleets)
					{
						if (f == null || f.Status == "WATCHLIST_DATA") continue;
						if (string.IsNullOrEmpty(f.LocationCoord) || !f.LocationCoord.StartsWith($"{activeSrv}:", StringComparison.OrdinalIgnoreCase))
						{
							f.LocationCoord = $"{activeSrv}:{CachedMoonId}:{CachedBaseSlot}";
						}
						mergedFleets.Add(f);
					}
				}
				if (CachedWatchlist != null && CachedWatchlist.Count > 0)
				{
					mergedFleets.Add(new GameMath.FleetManifest
					{
						Id = $"{activeSrv}_WATCHLIST",
						Name = "WATCHLIST",
						LocationCoord = $"{activeSrv}:{CachedMoonId}:{CachedBaseSlot}",
						Status = "WATCHLIST_DATA",
						Protocol = "IDLE",
						Cargo = new GameMath.FleetCargoHold { Watchlist = new List<GameMath.WatchlistEntry>(CachedWatchlist) }
					});
				}

				rawRow.commander_name = cmdToSave;
				rawRow.mgold = gGold;
				rawRow.building_levels = mergedBlds;
				rawRow.build_queue = mergedQueue;
				rawRow.active_boosts = mergedBoosts;
				rawRow.unit_inventory = mergedUnits;
				rawRow.unit_bays = mergedBays;
				rawRow.saved_fleets = mergedFleets;
			}

			_isolatedServerSnapshots[UserId] = rawRow;

			// 1. SYNCHRONOUS WRITE-AHEAD DEVICE JOURNAL (0ms on disk):
			// Even if the player swipes the app closed or installs an APK update 10ms after clicking Upgrade,
			// their click is 100% permanently recorded on device storage before the HTTP request even leaves the phone!
			SaveLocalDeviceJournal(UserId, rawRow);

			// 2. ENSURE VALID JWT ACCESS TOKEN (Prevents 1-hour session token expiration from dropping saves!)
			await EnsureValidAuthTokenAsync();

			string url = $"{SupabaseUrl}/rest/v1/colonies?id=eq.{UserId}";

			var payload = new
			{
				commander_name = rawRow.commander_name,
				base_name = rawRow.base_name,
				alliance_name = rawRow.alliance_name,
				moon_id = rawRow.moon_id > 0 ? rawRow.moon_id : 100,
				slot_id = rawRow.slot_id > 0 ? rawRow.slot_id : 1,
				res_e = rawRow.res_e,
				res_i = rawRow.res_i,
				res_t = rawRow.res_t,
				res_h3 = rawRow.res_h3,
				mgold = rawRow.mgold,
				building_levels = rawRow.building_levels,
				active_build = rawRow.active_build,
				build_queue = rawRow.build_queue,
				active_boosts = rawRow.active_boosts,
				unit_inventory = rawRow.unit_inventory,
				unit_bays = rawRow.unit_bays,
				saved_fleets = rawRow.saved_fleets,
				last_sync_time = rawRow.last_sync_time.ToString("o") 
			};

			string jsonBody = JsonSerializer.Serialize(payload);

			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}",
				$"Authorization: Bearer {AccessToken}",
				"Prefer: return=minimal"
			};

				var (code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Patch, jsonBody);

				// 3. AUTOMATIC 401/403 JWT EXPIRATION RECOVERY & RETRY:
				// If the player has been playing for 1+ hours and their JWT just expired, immediately refresh and re-send the PATCH!
				if (code == 401 || code == 403)
				{
					GD.Print("[SUPABASE] Save received 401/403 (token expired during long session) -> Refreshing JWT and retrying save immediately...");
					await EnsureValidAuthTokenAsync(forceRefresh: true);
					headers[2] = $"Authorization: Bearer {AccessToken}";
					(code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Patch, jsonBody);
				}

				// 4. AUTOMATIC 400 SCHEMA COLUMN FALLBACK:
				// If a database table is missing an optional column (e.g. moon_id, slot_id, base_name, or unit_bays),
				// automatically retry with the guaranteed core columns so building_levels, queues, and resources NEVER fail to save!
				if (code == 400)
				{
					var coreFallbackPayload = new
					{
						commander_name = rawRow.commander_name,
						res_e = rawRow.res_e,
						res_i = rawRow.res_i,
						res_t = rawRow.res_t,
						res_h3 = rawRow.res_h3,
						mgold = rawRow.mgold,
						building_levels = rawRow.building_levels,
						active_build = rawRow.active_build,
						build_queue = rawRow.build_queue,
						active_boosts = rawRow.active_boosts,
						last_sync_time = rawRow.last_sync_time.ToString("o")
					};
					string fallbackJson = JsonSerializer.Serialize(coreFallbackPayload);
					(code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Patch, fallbackJson);
				}

				if (code != 204 && code != 200)
				{
					GD.PrintErr($"[SUPABASE] Save failed ({code}): {responseText}");
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SUPABASE] Save Network Error: {ex.Message}");
			}
			finally
			{
				_saveSyncLock.Release();
			}
		}

		private static List<ColonySaveData> _cachedRawColonyRows = null;
		private static long _lastColonyRowsFetchMs = 0;
		private static List<GalacticCommanderEntry> _cachedAllColoniesList = null;
		private static string _cachedAllColoniesSrv = "";
		private static long _lastAllColoniesFetchMs = 0;
		private static List<InboxMessageData> _cachedInboxMessages = null;
		private static string _cachedInboxCmdr = "";
		private static string _cachedInboxSrv = "";
		private static long _lastInboxFetchMs = 0;

		public static List<GalacticCommanderEntry> GetCachedColoniesSnapshot()
		{
			string activeSrv = string.IsNullOrWhiteSpace(CachedServerId) ? "S1" : CachedServerId.Trim().ToUpper();
			if (_cachedAllColoniesList != null && _cachedAllColoniesSrv == activeSrv)
			{
				return new List<GalacticCommanderEntry>(_cachedAllColoniesList);
			}
			return null;
		}

		public static void InvalidateColoniesCache()
		{
			_lastColonyRowsFetchMs = 0;
			_lastAllColoniesFetchMs = 0;
		}

		public static void InvalidateInboxCache()
		{
			_lastInboxFetchMs = 0;
		}

		public async Task<List<ColonySaveData>> FetchRawColonyRowsCachedAsync(bool forceRefresh = false)
		{
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			if (!forceRefresh && _cachedRawColonyRows != null && (nowMs - _lastColonyRowsFetchMs) < 3500)
			{
				return _cachedRawColonyRows;
			}

			string url = $"{SupabaseUrl}/rest/v1/colonies?select=*&order=id.asc";
			string authBearer = !string.IsNullOrEmpty(AccessToken) ? AccessToken : SupabaseAnonKey;
			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}",
				$"Authorization: Bearer {authBearer}"
			};

			try
			{
				var (code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Get);
				if (code == 200 && !string.IsNullOrEmpty(responseText))
				{
					var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
					var rows = JsonSerializer.Deserialize<List<ColonySaveData>>(responseText, options);
					if (rows != null)
					{
						// Always sort deterministically by persistent UUID so Postgres physical tuple moves after PATCH updates NEVER change iteration order!
						rows.Sort((a, b) => string.CompareOrdinal(a?.id ?? "", b?.id ?? ""));
						_cachedRawColonyRows = rows;
						_lastColonyRowsFetchMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
						return rows;
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SUPABASE] FetchRawColonyRowsCachedAsync Error: {ex.Message}");
			}
			return _cachedRawColonyRows ?? new List<ColonySaveData>();
		}

		/// <summary>
		/// Fetches all registered player colonies for the CURRENTLY ACTIVE SERVER (S1 or S2) from Supabase
		/// so MoonView, GalaxyView, and LeaderboardModal only show players belonging to the active server universe!
		/// </summary>
		public async Task<List<GalacticCommanderEntry>> FetchAllColoniesAsync(bool forceRefresh = false)
		{
			string activeSrv = string.IsNullOrWhiteSpace(CachedServerId) ? "S1" : CachedServerId.Trim().ToUpper();
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			if (!forceRefresh && _cachedAllColoniesList != null && _cachedAllColoniesSrv == activeSrv && (nowMs - _lastAllColoniesFetchMs) < 3500)
			{
				return new List<GalacticCommanderEntry>(_cachedAllColoniesList);
			}

			var resultList = new List<GalacticCommanderEntry>();

			try
			{
				var rows = await FetchRawColonyRowsCachedAsync(forceRefresh);
				if ((rows == null || rows.Count == 0) && _cachedAllColoniesList != null && _cachedAllColoniesList.Count > 1 && _cachedAllColoniesSrv == activeSrv)
				{
					return new List<GalacticCommanderEntry>(_cachedAllColoniesList);
				}
				if (rows != null && rows.Count > 0)
				{
					{
						// Sort rows deterministically by persistent UUID (row.id) before slot assignment so PATCH updates never shift coordinates!
						var sortedRows = new List<ColonySaveData>(rows);
						sortedRows.Sort((a, b) => string.CompareOrdinal(a?.id ?? "", b?.id ?? ""));

						var usedCoords = new HashSet<string>();

						foreach (var row in sortedRows)
						{
							if (row == null) continue;
							bool isMe = (!string.IsNullOrEmpty(UserId) && row.id == UserId);

							// Only include players who actually have an initialized colony on the active server!
							if (!isMe && !HasInitializedServerColony(row, activeSrv))
								continue;

							var srvLevels = ExtractServerBuildingLevels(row, activeSrv);
							if (srvLevels.Count == 0 && isMe && BaseHUD.Instance != null)
							{
								srvLevels = new Dictionary<string, int>(BaseHUD.Instance.BuildingLevels);
							}

							int m = srvLevels.GetValueOrDefault("__meta_moon", 0);
							int s = srvLevels.GetValueOrDefault("__meta_slot", 0);
							if (activeSrv == "S1")
							{
								if ((m < 1 || m > 1000) && row.moon_id >= 1 && row.moon_id <= 1000) m = row.moon_id;
								if ((s < 1 || s > 20) && row.slot_id >= 1 && row.slot_id <= 20) s = row.slot_id;
							}
							if (isMe)
							{
								if (CachedMoonId >= 1 && CachedMoonId <= 1000) m = CachedMoonId;
								if (CachedBaseSlot >= 1 && CachedBaseSlot <= 20) s = CachedBaseSlot;
							}
							if (m < 1 || m > 1000 || s < 1 || s > 20)
							{
								string persistentUuid = !string.IsNullOrWhiteSpace(row.id) ? row.id.Trim() : "DEFAULT_UUID";
								string seed = activeSrv == "S1" ? persistentUuid : $"{persistentUuid}_{activeSrv}";
								var assigned = DeriveDeterministicCoordinate(seed);
								m = assigned.moon;
								s = assigned.slot;
							}

							// Resolve any slot collision on the same moon deterministically so every player has a distinct slot
							string coordKey = $"{m}:{s}";
							while (usedCoords.Contains(coordKey))
							{
								s = (s % 20) + 1;
								if (s == 1) m = (m % 1000) + 1;
								coordKey = $"{m}:{s}";
							}
							usedCoords.Add(coordKey);

							if (isMe)
							{
								// Keep local session coordinates synchronized with the authoritative resolved coordinate
								CachedMoonId = m;
								CachedBaseSlot = s;
							}

							int coreLvl = srvLevels.GetValueOrDefault("hub_cmd", 1);
							int ahqLvl = srvLevels.GetValueOrDefault("hub_ahq", 0);
							int roleCode = srvLevels.GetValueOrDefault("__meta_alliance_role", 0);
							long pop = CalculateColonyPopulation(srvLevels);

							string cName = !string.IsNullOrEmpty(row.commander_name) ? row.commander_name : "COMMANDER";
							if (isMe && !string.IsNullOrEmpty(CachedCommanderName) && CachedCommanderName != "COMMANDER ALPHA")
							{
								cName = CachedCommanderName;
							}

							// Resolve custom BaseName strictly from each player's own row (never leaking the local player's CachedBaseName to other players!)
							string customBaseName;
							if (isMe)
							{
								customBaseName = !string.IsNullOrWhiteSpace(CachedBaseName) ? CachedBaseName : GetServerCustomBaseName(row, activeSrv);
							}
							else
							{
								string rowCustomName = GetServerCustomBaseName(row, activeSrv);
								if (string.IsNullOrWhiteSpace(rowCustomName) ||
									string.Equals(rowCustomName, "Alpha Outpost", StringComparison.OrdinalIgnoreCase) ||
									string.Equals(rowCustomName, $"{activeSrv} Outpost", StringComparison.OrdinalIgnoreCase))
								{
									customBaseName = $"{cName.ToUpper()} OUTPOST";
								}
								else
								{
									customBaseName = rowCustomName.Trim();
								}
							}

							resultList.Add(new GalacticCommanderEntry
							{
								UserId = row.id ?? "",
								CommanderName = cName.ToUpper(),
								BaseName = customBaseName,
								AllianceName = isMe ? CachedAllianceName : (string.IsNullOrEmpty(row.alliance_name) ? "NONE" : row.alliance_name),
								MoonId = m,
								BaseSlot = s,
								IndustrialCoreLevel = Math.Max(1, coreLvl),
								AllianceHQLevel = Math.Max(0, ahqLvl),
								AllianceRoleCode = roleCode,
								Population = pop,
								MedalPop = srvLevels.GetValueOrDefault("__meta_medal_pop", coreLvl >= 5 ? 1 : 0),
								MedalRaid = srvLevels.GetValueOrDefault("__meta_medal_raid", 0),
								MedalAtk = srvLevels.GetValueOrDefault("__meta_medal_atk", 0),
								MedalDef = srvLevels.GetValueOrDefault("__meta_medal_def", 0),
								IsMe = isMe
							});
						}
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SUPABASE] FetchAllColoniesAsync Error: {ex.Message}");
			}

			// Always guarantee the local player is in the list even in Offline / Tester Sandbox mode
			if (!resultList.Exists(e => e.IsMe))
			{
				resultList.Add(new GalacticCommanderEntry
				{
					UserId = UserId ?? "LOCAL_COMMANDER",
					CommanderName = CachedCommanderName.ToUpper(),
					BaseName = CachedBaseName,
					AllianceName = CachedAllianceName,
					MoonId = CachedMoonId,
					BaseSlot = CachedBaseSlot,
					IndustrialCoreLevel = BaseHUD.Instance != null ? BaseHUD.Instance.BuildingLevels.GetValueOrDefault("hub_cmd", 1) : 1,
					AllianceHQLevel = BaseHUD.Instance != null ? BaseHUD.Instance.BuildingLevels.GetValueOrDefault("hub_ahq", 0) : 0,
					AllianceRoleCode = BaseHUD.Instance != null ? BaseHUD.Instance.BuildingLevels.GetValueOrDefault("__meta_alliance_role", 0) : 0,
					Population = BaseHUD.Instance != null ? CalculateColonyPopulation(BaseHUD.Instance.BuildingLevels) : 700,
					MedalPop = 1,
					MedalRaid = 0,
					MedalAtk = 0,
					MedalDef = 0,
					IsMe = true
				});
			}

			resultList.Sort((a, b) => b.Population.CompareTo(a.Population));
			_cachedAllColoniesList = new List<GalacticCommanderEntry>(resultList);
			_cachedAllColoniesSrv = activeSrv;
			_lastAllColoniesFetchMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			return resultList;
		}

		/// <summary>
		/// Scans all active fleets across the current server in Supabase to detect any incoming hostile RAID fleets
		/// targeting ANY member of the specified Alliance! Details Attacker name, Attacker origin coordinates,
		/// Defender member name, Defender coordinates, Target building, Fleet size, Laser Cannons, and live ETA.
		/// </summary>
		public async Task<List<AllianceIncomingAttackAlert>> FetchAllianceIncomingAttacksAsync(string allianceName)
		{
			var alerts = new List<AllianceIncomingAttackAlert>();
			if (string.IsNullOrWhiteSpace(allianceName) || allianceName.Trim().ToUpper() == "NONE") return alerts;

			string cleanAlliance = allianceName.Trim().ToUpper();
			string activeSrv = string.IsNullOrWhiteSpace(CachedServerId) ? "S1" : CachedServerId.Trim().ToUpper();
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

			try
			{
				var allColonies = await FetchAllColoniesAsync();
				var allianceMembersByCoord = new Dictionary<string, GalacticCommanderEntry>(StringComparer.OrdinalIgnoreCase);
				foreach (var col in allColonies)
				{
					if (string.Equals((col.AllianceName ?? "").Trim(), cleanAlliance, StringComparison.OrdinalIgnoreCase))
					{
						allianceMembersByCoord[$"{col.MoonId}:{col.BaseSlot}"] = col;
					}
				}

				if (allianceMembersByCoord.Count == 0) return alerts;

				var rows = await FetchRawColonyRowsCachedAsync();
				if (rows != null && rows.Count > 0)
				{
					{
						var seenFleetIds = new HashSet<string>();
						foreach (var row in rows)
						{
							if (row == null) continue;
							var unpacked = UnpackIsolatedServerColony(row, activeSrv);
							var fleets = unpacked?.saved_fleets;
							if (fleets == null) continue;

							string attackerName = (unpacked.commander_name ?? row.commander_name ?? "HOSTILE COMMANDER").Trim().ToUpper();
							string attackerAlliance = string.IsNullOrWhiteSpace(unpacked.alliance_name) ? "NONE" : unpacked.alliance_name.Trim().ToUpper();

							foreach (var f in fleets)
							{
								if (f == null || f.Status != "IN_TRANSIT" || f.Protocol != "RAID") continue;
								if (!string.IsNullOrEmpty(f.Id) && seenFleetIds.Contains(f.Id)) continue;

								if (!GameMath.TryParseSectorCoordinate(f.TargetCoord, out int tMoon, out int tSlot)) continue;
								if (!allianceMembersByCoord.TryGetValue($"{tMoon}:{tSlot}", out var defenderMember)) continue;

								double etaSec = f.ArrivalTimeUnixMs > 0
									? (f.ArrivalTimeUnixMs - nowMs) / 1000.0
									: f.EtaSecondsLeft;
								if (etaSec <= -15.0) continue;

								int totalShips = 0;
								int lasers = 0;
								if (f.Units != null)
								{
									foreach (var kv in f.Units) totalShips += kv.Value;
									lasers = f.Units.GetValueOrDefault("u6", 0);
								}

								GameMath.TryParseSectorCoordinate(f.LocationCoord, out int oMoon, out int oSlot);
								if (!string.IsNullOrEmpty(f.Id)) seenFleetIds.Add(f.Id);

								alerts.Add(new AllianceIncomingAttackAlert
								{
									FleetId = f.Id ?? Guid.NewGuid().ToString(),
									AttackerName = attackerName,
									AttackerAlliance = attackerAlliance,
									AttackerOriginCoord = $"M-{oMoon}:B-{oSlot:D2}",
									DefenderName = defenderMember.CommanderName,
									DefenderBaseName = defenderMember.BaseName,
									DefenderCoord = $"M-{tMoon}:B-{tSlot:D2}",
									DefenderMoonId = tMoon,
									DefenderSlotId = tSlot,
									TargetBuildingName = GameMath.GetSiegeTargetDisplayName(f.TargetBuildingId),
									TotalUnits = totalShips,
									LaserCannonCount = lasers,
									EtaSecondsLeft = Math.Max(0.0, etaSec),
									ArrivalTimeUnixMs = f.ArrivalTimeUnixMs
								});
							}
						}
					}
				}

				// Also include any local incoming signals on our own HUD if not already in the list
				if (CachedIncomingSignals != null)
				{
					foreach (var sig in CachedIncomingSignals)
					{
						if (sig == null || sig.Protocol != "RAID") continue;
						if (alerts.Exists(a => a.FleetId == sig.FleetId)) continue;
						double eta = sig.ArrivalTimeUnixMs > 0 ? (sig.ArrivalTimeUnixMs - nowMs) / 1000.0 : sig.EtaSecondsLeft;
						if (eta <= 0) continue;

						GameMath.TryParseSectorCoordinate(sig.OriginCoord, out int om, out int os);
						alerts.Add(new AllianceIncomingAttackAlert
						{
							FleetId = sig.FleetId,
							AttackerName = (sig.SenderName ?? "HOSTILE COMMANDER").ToUpper(),
							AttackerAlliance = "HOSTILE",
							AttackerOriginCoord = om > 0 ? $"M-{om}:B-{os:D2}" : (sig.OriginCoord ?? "UNKNOWN"),
							DefenderName = CachedCommanderName.ToUpper(),
							DefenderBaseName = CachedBaseName,
							DefenderCoord = $"M-{CachedMoonId}:B-{CachedBaseSlot:D2}",
							DefenderMoonId = CachedMoonId,
							DefenderSlotId = CachedBaseSlot,
							TargetBuildingName = sig.TargetBuildingName ?? "Industrial Core",
							TotalUnits = sig.TotalUnits,
							LaserCannonCount = sig.LaserCannonCount,
							EtaSecondsLeft = Math.Max(0.0, eta),
							ArrivalTimeUnixMs = sig.ArrivalTimeUnixMs
						});
					}
				}

				alerts.Sort((a, b) => a.EtaSecondsLeft.CompareTo(b.EtaSecondsLeft));
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SUPABASE] FetchAllianceIncomingAttacksAsync Error: {ex.Message}");
			}

			return alerts;
		}

		// =========================================================================
		// SHARED ALLIANCE SCIENCE POOL, MEMBER CONTRIBUTIONS & 1-HOUR OFFICER UPGRADE
		// =========================================================================
		public static readonly string[] AllianceScienceKeys = new string[]
		{
			GameMath.AllianceShieldScienceKey, // #1: Planetary Shield Casualty Mitigation Matrix (Lvl 1-10, +1% per Lvl = +10% Max)
			"sci_rally",
			"sci_weapons",
			"sci_shields",
			"sci_econ",
			"sci_siege"
		};

		public async Task<Dictionary<string, AllianceScienceSharedNode>> FetchAllianceSharedSciencesAsync(string allianceName)
		{
			var result = new Dictionary<string, AllianceScienceSharedNode>();
			long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

			foreach (var key in AllianceScienceKeys)
			{
				int maxL = (key == GameMath.AllianceShieldScienceKey) ? 10 : 20;
				var reqs = GameMath.GetAllianceSciencePoolRequirement(key, 0);
				result[key] = new AllianceScienceSharedNode
				{
					ScienceKey = key,
					CurrentLevel = 0,
					MaxLevel = maxL,
					RequiredE = reqs.reqE,
					RequiredI = reqs.reqI,
					RequiredT = reqs.reqT,
					RequiredH3 = reqs.reqH3
				};
			}

			if (string.IsNullOrWhiteSpace(allianceName) || allianceName.Trim().ToUpper() == "NONE")
			{
				CachedAllianceShieldScienceLevel = 0;
				return result;
			}

			string cleanAlliance = allianceName.Trim().ToUpper();
			string activeSrv = string.IsNullOrWhiteSpace(CachedServerId) ? "S1" : CachedServerId.Trim().ToUpper();

			try
			{
				var rows = await FetchRawColonyRowsCachedAsync();
				if (rows != null && rows.Count > 0)
				{
					{
						var memberLevelsList = new List<Dictionary<string, int>>();
						foreach (var row in rows)
						{
							if (row == null) continue;
							bool isMe = (!string.IsNullOrEmpty(UserId) && row.id == UserId);
							if (!isMe && !HasInitializedServerColony(row, activeSrv)) continue;

							var unpacked = UnpackIsolatedServerColony(row, activeSrv);
							string rAlliance = isMe
								? CachedAllianceName
								: (string.IsNullOrWhiteSpace(unpacked.alliance_name) ? (row.alliance_name ?? "NONE") : unpacked.alliance_name);
							if (!string.Equals(rAlliance.Trim(), cleanAlliance, StringComparison.OrdinalIgnoreCase)) continue;

							var blds = (isMe && BaseHUD.Instance != null && BaseHUD.Instance.BuildingLevels != null && BaseHUD.Instance.BuildingLevels.Count > 0)
								? new Dictionary<string, int>(BaseHUD.Instance.BuildingLevels)
								: (unpacked.building_levels ?? new Dictionary<string, int>());
							memberLevelsList.Add(blds);
						}

						if (memberLevelsList.Count == 0 && BaseHUD.Instance?.BuildingLevels != null)
						{
							memberLevelsList.Add(new Dictionary<string, int>(BaseHUD.Instance.BuildingLevels));
						}

						foreach (var key in AllianceScienceKeys)
						{
							int maxL = (key == GameMath.AllianceShieldScienceKey) ? 10 : 20;
							int highestLvl = 0;
							long latestEndSec = 0;

							// Pass 1: Determine authoritative Alliance Science level & any active 1-hour upgrade timer across all alliance members
							foreach (var blds in memberLevelsList)
							{
								int l = Math.Clamp(blds.GetValueOrDefault($"__ali_lvl_{key}", blds.GetValueOrDefault($"__{key}", 0)), 0, maxL);
								if (l > highestLvl) highestLvl = l;

								long endSec = blds.GetValueOrDefault($"__ali_end_{key}", 0);
								if (endSec > latestEndSec) latestEndSec = endSec;
							}

							bool isUpgrading = false;
							double remSec = 0;
							if (latestEndSec > 0)
							{
								int targetUpgradingLvl = 0;
								foreach (var blds in memberLevelsList)
								{
									int t = blds.GetValueOrDefault($"__ali_tgt_{key}", 0);
									if (t > targetUpgradingLvl) targetUpgradingLvl = t;
								}
								if (targetUpgradingLvl <= highestLvl) targetUpgradingLvl = Math.Min(maxL, highestLvl + 1);

								if (nowSec >= latestEndSec)
								{
									// 1-Hour (3,600s) Alliance Science upgrade has completed!
									highestLvl = Math.Clamp(Math.Max(highestLvl, targetUpgradingLvl), 0, maxL);
									latestEndSec = 0;
									if (BaseHUD.Instance != null && BaseHUD.Instance.BuildingLevels != null)
									{
										int mySaved = BaseHUD.Instance.BuildingLevels.GetValueOrDefault($"__ali_lvl_{key}", 0);
										if (highestLvl > mySaved || BaseHUD.Instance.BuildingLevels.GetValueOrDefault($"__ali_end_{key}", 0) > 0)
										{
											BaseHUD.Instance.BuildingLevels[$"__ali_lvl_{key}"] = highestLvl;
											BaseHUD.Instance.BuildingLevels.Remove($"__ali_end_{key}");
											BaseHUD.Instance.BuildingLevels.Remove($"__ali_tgt_{key}");
											BaseHUD.Instance.TriggerInstantSave();
										}
									}
								}
								else
								{
									isUpgrading = true;
									remSec = latestEndSec - nowSec;
								}
							}

							// Sync highest completed alliance science level into local player's state so combat & UI always stay in sync
							if (BaseHUD.Instance != null && BaseHUD.Instance.BuildingLevels != null)
							{
								if (BaseHUD.Instance.BuildingLevels.GetValueOrDefault($"__ali_lvl_{key}", 0) < highestLvl)
								{
									BaseHUD.Instance.BuildingLevels[$"__ali_lvl_{key}"] = highestLvl;
								}
							}

							int nextTargetLvl = Math.Min(maxL, highestLvl + 1);
							long sumE = 0, sumI = 0, sumT = 0, sumH = 0;
							foreach (var blds in memberLevelsList)
							{
								sumE += Math.Max(0, blds.GetValueOrDefault($"__ali_don_{key}_L{nextTargetLvl}_e", 0));
								sumI += Math.Max(0, blds.GetValueOrDefault($"__ali_don_{key}_L{nextTargetLvl}_i", 0));
								sumT += Math.Max(0, blds.GetValueOrDefault($"__ali_don_{key}_L{nextTargetLvl}_t", 0));
								sumH += Math.Max(0, blds.GetValueOrDefault($"__ali_don_{key}_L{nextTargetLvl}_h", 0));
							}

							var reqs = GameMath.GetAllianceSciencePoolRequirement(key, highestLvl);
							result[key] = new AllianceScienceSharedNode
							{
								ScienceKey = key,
								CurrentLevel = highestLvl,
								MaxLevel = maxL,
								ContributedE = Math.Min(reqs.reqE, sumE),
								ContributedI = Math.Min(reqs.reqI, sumI),
								ContributedT = Math.Min(reqs.reqT, sumT),
								ContributedH3 = Math.Min(reqs.reqH3, sumH),
								RequiredE = reqs.reqE,
								RequiredI = reqs.reqI,
								RequiredT = reqs.reqT,
								RequiredH3 = reqs.reqH3,
								UpgradeEndUnixSec = latestEndSec,
								IsUpgradingNow = isUpgrading,
								RemainingUpgradeSeconds = remSec
							};
						}
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SUPABASE] FetchAllianceSharedSciencesAsync Error: {ex.Message}");
			}

			if (result.TryGetValue(GameMath.AllianceShieldScienceKey, out var shdSci))
			{
				CachedAllianceShieldScienceLevel = shdSci.CurrentLevel;
			}

			return result;
		}

		public static bool ContributeToAllianceSciencePool(string scienceKey, int currentScienceLevel, double donateFraction = 0.25)
		{
			if (BaseHUD.Instance == null || BaseHUD.Instance.BuildingLevels == null) return false;
			int maxL = (scienceKey == GameMath.AllianceShieldScienceKey) ? 10 : 20;
			if (currentScienceLevel >= maxL) return false;

			int nextTargetLvl = currentScienceLevel + 1;
			var reqs = GameMath.GetAllianceSciencePoolRequirement(scienceKey, currentScienceLevel);

			string kE = $"__ali_don_{scienceKey}_L{nextTargetLvl}_e";
			string kI = $"__ali_don_{scienceKey}_L{nextTargetLvl}_i";
			string kT = $"__ali_don_{scienceKey}_L{nextTargetLvl}_t";
			string kH = $"__ali_don_{scienceKey}_L{nextTargetLvl}_h";

			long stepE = Math.Max(1L, (long)Math.Ceiling(reqs.reqE * donateFraction));
			long stepI = Math.Max(1L, (long)Math.Ceiling(reqs.reqI * donateFraction));
			long stepT = Math.Max(1L, (long)Math.Ceiling(reqs.reqT * donateFraction));
			long stepH = reqs.reqH3 > 0 ? Math.Max(1L, (long)Math.Ceiling(reqs.reqH3 * donateFraction)) : 0L;

			long giveE = Math.Min((long)BaseHUD.Instance.ResE, stepE);
			long giveI = Math.Min((long)BaseHUD.Instance.ResI, stepI);
			long giveT = Math.Min((long)BaseHUD.Instance.ResT, stepT);
			long giveH = Math.Min((long)BaseHUD.Instance.ResH3, stepH);

			if (giveE <= 0 && giveI <= 0 && giveT <= 0 && giveH <= 0) return false;

			BaseHUD.Instance.ResE -= giveE;
			BaseHUD.Instance.ResI -= giveI;
			BaseHUD.Instance.ResT -= giveT;
			BaseHUD.Instance.ResH3 -= giveH;

			BaseHUD.Instance.BuildingLevels[kE] = (int)Math.Min(int.MaxValue, BaseHUD.Instance.BuildingLevels.GetValueOrDefault(kE, 0) + giveE);
			BaseHUD.Instance.BuildingLevels[kI] = (int)Math.Min(int.MaxValue, BaseHUD.Instance.BuildingLevels.GetValueOrDefault(kI, 0) + giveI);
			BaseHUD.Instance.BuildingLevels[kT] = (int)Math.Min(int.MaxValue, BaseHUD.Instance.BuildingLevels.GetValueOrDefault(kT, 0) + giveT);
			BaseHUD.Instance.BuildingLevels[kH] = (int)Math.Min(int.MaxValue, BaseHUD.Instance.BuildingLevels.GetValueOrDefault(kH, 0) + giveH);

			BaseHUD.Instance.UpdateHUDDisplay();
			BaseHUD.Instance.TriggerInstantSave();
			return true;
		}

		public static bool StartAllianceScienceOneHourUpgrade(string scienceKey, int currentScienceLevel)
		{
			if (BaseHUD.Instance == null || BaseHUD.Instance.BuildingLevels == null) return false;
			int myRole = BaseHUD.Instance.BuildingLevels.GetValueOrDefault("__meta_alliance_role", 0);
			// Strict Blueprint Rule: Only the Alliance Leader (R5 = 4) or Assigned Officers (R2/R3/R4 = 1..3) may press UPGRADE!
			if (myRole < 1) return false;

			int maxL = (scienceKey == GameMath.AllianceShieldScienceKey) ? 10 : 20;
			if (currentScienceLevel >= maxL) return false;

			long endUnixSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + GameMath.AllianceScienceUpgradeDurationSec; // Strict 1 Hour (3,600s)
			BaseHUD.Instance.BuildingLevels[$"__ali_tgt_{scienceKey}"] = currentScienceLevel + 1;
			BaseHUD.Instance.BuildingLevels[$"__ali_end_{scienceKey}"] = (int)Math.Min(int.MaxValue, endUnixSec);
			BaseHUD.Instance.TriggerInstantSave();
			return true;
		}

		// =========================================================================
		// DIPLOMATIC INBOX & GLOBAL SERVER CHAT REST ENDPOINTS
		// =========================================================================
		public async Task<List<InboxMessageData>> FetchInboxMessagesAsync(string commanderName, bool forceRefresh = false)
		{
			var list = new List<InboxMessageData>();
			if (string.IsNullOrEmpty(commanderName)) return list;

			string activeSrv = string.IsNullOrWhiteSpace(CachedServerId) ? "S1" : CachedServerId.Trim().ToUpper();
			string cleanCmdr = commanderName.Trim().ToUpper();
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			if (!forceRefresh && _cachedInboxMessages != null && _cachedInboxCmdr == cleanCmdr && _cachedInboxSrv == activeSrv && (nowMs - _lastInboxFetchMs) < 2200)
			{
				return new List<InboxMessageData>(_cachedInboxMessages);
			}

			string targetLow = Uri.EscapeDataString(commanderName.Trim().ToLower());
			string senderUp = Uri.EscapeDataString(commanderName.Trim().ToUpper());
			string senderLow = Uri.EscapeDataString(commanderName.Trim().ToLower());

			// Fetch both received messages (recipient_name=eq.targetLow) AND sent messages (sender_name=eq.senderUp or senderLow)
			// so every Commander Conversation Folder displays the full sent & received chat history!
			string url = $"{SupabaseUrl}/rest/v1/inbox?or=(recipient_name.eq.{targetLow},sender_name.eq.{senderUp},sender_name.eq.{senderLow})&order=created_at.desc&limit=120";
			string authBearer = !string.IsNullOrEmpty(AccessToken) ? AccessToken : SupabaseAnonKey;

			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}",
				$"Authorization: Bearer {authBearer}"
			};

			try
			{
				var (code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Get);
				if (code == 200 && !string.IsNullOrEmpty(responseText))
				{
					var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
					var rows = JsonSerializer.Deserialize<List<InboxMessageData>>(responseText, options);
					if (rows != null)
					{
						string myCallsign = commanderName.Trim();
						foreach (var r in rows)
						{
							if (r == null) continue;
							string title = r.title ?? "";

							// Filter messages by server tag so S1 and S2 inbox / incoming fleet signals never mix!
							if (title.StartsWith("[S", StringComparison.Ordinal) && title.Contains("]"))
							{
								int closeIdx = title.IndexOf(']');
								string msgSrv = title.Substring(1, closeIdx - 1).ToUpper();
								if (msgSrv != activeSrv) continue;
								r.title = title.Substring(closeIdx + 1).TrimStart();
							}
							else if (activeSrv != "S1" && title.StartsWith("INCOMING_FLEET:", StringComparison.Ordinal))
							{
								// Untagged legacy incoming fleet signals belong to S1
								continue;
							}

							// Ephemeral radar/sync control packets AND non-PERSONAL category logs (BATTLE, INTEL, ALLIANCE)
							// must ONLY be delivered to their actual recipient! (Sent messages are only needed for PERSONAL 2-way chat threads.)
							string checkTitle = r.title ?? "";
							string catUpper = (r.category ?? "PERSONAL").Trim().ToUpper();
							bool isRecipientOnlyMessage =
								catUpper == "BATTLE" ||
								catUpper == "INTEL" ||
								catUpper == "ALLIANCE" ||
								checkTitle.StartsWith("INCOMING_FLEET:", StringComparison.Ordinal) ||
								checkTitle.StartsWith("CANCEL_FLEET:", StringComparison.Ordinal) ||
								checkTitle.StartsWith("IMPACT_SYNC:", StringComparison.Ordinal) ||
								checkTitle.Contains("DEFENSE REPORT", StringComparison.OrdinalIgnoreCase);

							if (isRecipientOnlyMessage && !string.Equals(r.recipient_name?.Trim(), myCallsign, StringComparison.OrdinalIgnoreCase))
							{
								continue;
							}

							// Also ensure Attackers only see Attack/Raid Reports and Defenders only see Defense Reports if any legacy self-sent report exists
							if (catUpper == "BATTLE" &&
								checkTitle.Contains("DEFENSE REPORT", StringComparison.OrdinalIgnoreCase) &&
								string.Equals(r.sender_name?.Trim(), myCallsign, StringComparison.OrdinalIgnoreCase) &&
								!checkTitle.Contains($"({myCallsign})", StringComparison.OrdinalIgnoreCase))
							{
								continue;
							}

							list.Add(r);
						}
						_cachedInboxMessages = new List<InboxMessageData>(list);
						_cachedInboxCmdr = cleanCmdr;
						_cachedInboxSrv = activeSrv;
						_lastInboxFetchMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SUPABASE] FetchInboxMessagesAsync Error: {ex.Message}");
			}
			return list;
		}

		public async Task<bool> MarkInboxMessagesReadAsync(List<long> messageIds)
		{
			if (messageIds == null || messageIds.Count == 0) return false;
			var validIds = new List<long>();
			foreach (var id in messageIds)
			{
				if (id > 0) validIds.Add(id);
			}
			if (validIds.Count == 0) return false;

			string idList = string.Join(",", validIds);
			string url = $"{SupabaseUrl}/rest/v1/inbox?id=in.({idList})";
			string authBearer = !string.IsNullOrEmpty(AccessToken) ? AccessToken : SupabaseAnonKey;

			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}",
				$"Authorization: Bearer {authBearer}",
				"Prefer: return=minimal"
			};

			try
			{
				string json = JsonSerializer.Serialize( new { is_read = true });
				var (code, _) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Patch, json);
				InvalidateInboxCache();
				return (code == 200 || code == 204);
			}
			catch
			{
				return false;
			}
		}

		public async Task<bool> SendInboxSignalAsync(string senderName, string recipientName, string category, string title, string body)
		{
			if (string.IsNullOrEmpty(recipientName) || string.IsNullOrEmpty(body)) return false;

			string activeSrv = string.IsNullOrWhiteSpace(CachedServerId) ? "S1" : CachedServerId.Trim().ToUpper();
			string taggedTitle = (title != null && title.StartsWith($"[{activeSrv}]", StringComparison.Ordinal))
				? title
				: $"[{activeSrv}] {title}";

			string url = $"{SupabaseUrl}/rest/v1/inbox";
			string authBearer = !string.IsNullOrEmpty(AccessToken) ? AccessToken : SupabaseAnonKey;

			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}",
				$"Authorization: Bearer {authBearer}",
				"Prefer: return=minimal"
			};

			var payload = new[]
			{
				new
				{
					sender_name = senderName.ToUpper(),
					recipient_name = recipientName.Trim().ToLower(),
					category = category,
					title = taggedTitle,
					body = body,
					is_read = false,
					created_at = DateTimeOffset.UtcNow.ToString("o")
				}
			};

			try
			{
				string json = JsonSerializer.Serialize(payload);
				var (code, _) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Post, json);
				return (code == 200 || code == 201 || code == 204);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SUPABASE] SendInboxSignalAsync Error: {ex.Message}");
				return false;
			}
		}

		public async Task<bool> DeleteInboxMessageAsync(long messageId)
		{
			string url = $"{SupabaseUrl}/rest/v1/inbox?id=eq.{messageId}";
			string authBearer = !string.IsNullOrEmpty(AccessToken) ? AccessToken : SupabaseAnonKey;

			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}",
				$"Authorization: Bearer {authBearer}"
			};

			try
			{
				var (code, _) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Delete);
				InvalidateInboxCache();
				return (code == 200 || code == 204);
			}
			catch
			{
				return false;
			}
		}

		public async Task<List<ChatMessageData>> FetchGlobalChatAsync(string serverId = "S1")
		{
			var list = new List<ChatMessageData>();
			string url = $"{SupabaseUrl}/rest/v1/chat?server_id=eq.{serverId}&order=created_at.desc&limit=40";
			string authBearer = !string.IsNullOrEmpty(AccessToken) ? AccessToken : SupabaseAnonKey;

			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}",
				$"Authorization: Bearer {authBearer}"
			};

			try
			{
				var (code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Get);
				if (code == 200 && !string.IsNullOrEmpty(responseText))
				{
					var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
					var rows = JsonSerializer.Deserialize<List<ChatMessageData>>(responseText, options);
					if (rows != null)
					{
						// Keep descending order (order=created_at.desc) so Latest Messages are at the TOP and Older Messages flow down!
						list.AddRange(rows);
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SUPABASE] FetchGlobalChatAsync Error: {ex.Message}");
			}
			return list;
		}

		public async Task<bool> SendGlobalChatAsync(string commanderName, string messageText, string serverId = "S1")
		{
			if (string.IsNullOrWhiteSpace(messageText)) return false;

			string url = $"{SupabaseUrl}/rest/v1/chat";
			string authBearer = !string.IsNullOrEmpty(AccessToken) ? AccessToken : SupabaseAnonKey;

			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}",
				$"Authorization: Bearer {authBearer}",
				"Prefer: return=minimal"
			};

			var payload = new[]
			{
				new
				{
					server_id = serverId,
					commander_name = commanderName.ToUpper(),
					message = messageText.Trim(),
					created_at = DateTimeOffset.UtcNow.ToString("o")
				}
			};

			try
			{
				string json = JsonSerializer.Serialize(payload);
				var (code, _) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Post, json);
				return (code == 200 || code == 201 || code == 204);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SUPABASE] SendGlobalChatAsync Error: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Executes full Battle & Laser Cannon Structural Siege calculation when an attacking fleet reaches its target coordinate.
		/// - Calculates Attacker vs Defender combat power & unit casualties.
		/// - Applies surviving Laser Cannon (u6) structural siege damage (20 HP / 1% of Lvl 20 max HP per cannon) to the targeted building.
		/// - Drops the building's level if HP crosses 100-HP level thresholds.
		/// - Checks if ALL buildings on the defender's base reached 0% HP (Level 0) — if so, the base is destroyed/unoccupied and the player must restart!
		/// - Sends detailed Battle Reports to both Attacker and Defender inboxes and returns surviving units + loot for the return flight home.
		/// </summary>
		public async Task<(Dictionary<string, int> survivingUnits, GameMath.FleetCargoHold plunderedLoot, string battleSummary)> ResolveFleetRaidAndSiegeAsync(
			GameMath.FleetManifest attackingFleet,
			int targetMoon,
			int targetSlot)
		{
			var survivors = new Dictionary<string, int>();
			var loot = new GameMath.FleetCargoHold
			{
				E = attackingFleet.Cargo?.E ?? 0,
				I = attackingFleet.Cargo?.I ?? 0,
				T = attackingFleet.Cargo?.T ?? 0,
				H3 = attackingFleet.Cargo?.H3 ?? 0
			};

			// 1. Calculate Attacker Combat Power, HP & Cargo Capacity
			long atkPower = 0;
			long atkHp = 0;
			long maxCargoCap = 0;
			foreach (var kvp in attackingFleet.Units)
			{
				var u = GameData.Units.Find(x => x.Id == kvp.Key);
				if (u != null && kvp.Value > 0)
				{
					atkPower += (long)u.Atk * kvp.Value;
					atkHp += (long)u.Hp * kvp.Value;
					maxCargoCap += (long)u.Cargo * kvp.Value;
				}
			}

			// 2. Fetch Defender Colony Data from Supabase (Strictly Filtered to Active Server!)
			string activeSrv = string.IsNullOrWhiteSpace(CachedServerId) ? "S1" : CachedServerId.Trim().ToUpper();
			ColonySaveData defenderRawRow = null;
			ColonySaveData defenderServerColony = null;
			string defenderName = "OUTPOST GARRISON";
			long defUnitPower = 0;
			long defWallPower = 300;
			var defStartUnits = new Dictionary<string, int>();
			var defSurvivingUnits = new Dictionary<string, int>();
			int defTotalStartUnits = 0;
			int defTotalSurvivingUnits = 0;

			try
			{
				// Resolve the authoritative occupant of (targetMoon, targetSlot) via FetchAllColoniesAsync (which sorts by UUID and resolves slot collisions)
				string expectedDefenderUserId = null;
				var allActiveColonies = await FetchAllColoniesAsync();
				if (allActiveColonies != null)
				{
					var matchedEntry = allActiveColonies.Find(c => c != null && c.MoonId == targetMoon && c.BaseSlot == targetSlot);
					if (matchedEntry != null && !string.IsNullOrEmpty(matchedEntry.UserId))
					{
						expectedDefenderUserId = matchedEntry.UserId;
					}
				}

				string url = $"{SupabaseUrl}/rest/v1/colonies?select=*&order=id.asc";
				string authBearer = !string.IsNullOrEmpty(AccessToken) ? AccessToken : SupabaseAnonKey;
				string[] headers = new string[]
				{
					"Content-Type: application/json",
					$"apikey: {SupabaseAnonKey}",
					$"Authorization: Bearer {authBearer}"
				};

				var (code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Get);
				if (code == 200 && !string.IsNullOrEmpty(responseText))
				{
					var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
					var rows = JsonSerializer.Deserialize<List<ColonySaveData>>(responseText, options);
					if (rows != null)
					{
						foreach (var r in rows)
						{
							if (r == null) continue;
							if (!HasInitializedServerColony(r, activeSrv)) continue;
							var unpacked = UnpackIsolatedServerColony(r, activeSrv);
							int rm = unpacked.building_levels.GetValueOrDefault("__meta_moon", r.moon_id > 0 ? r.moon_id : 100);
							int rs = unpacked.building_levels.GetValueOrDefault("__meta_slot", r.slot_id > 0 ? r.slot_id : 1);
							bool matchesUuid = !string.IsNullOrEmpty(expectedDefenderUserId) && string.Equals(r.id, expectedDefenderUserId, StringComparison.OrdinalIgnoreCase);
							bool matchesCoord = (rm == targetMoon && rs == targetSlot);
							if (matchesUuid || matchesCoord)
							{
								defenderRawRow = r;
								defenderServerColony = unpacked;
								defenderName = (r.commander_name ?? "DEFENDER").ToUpper();
								if (matchesUuid) break;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[COMBAT ORACLE] Defender lookup warning: {ex.Message}");
			}

			if (defenderServerColony != null && defenderServerColony.building_levels != null)
			{
				// Idempotency Guard: Check if this exact Fleet ID was already resolved (e.g., if Defender resolved it at 00:00 while Attacker was offline!)
				uint fltHash = 2166136261u;
				string fltKeyStr = attackingFleet.Id ?? "FLT";
				for (int i = 0; i < fltKeyStr.Length; i++)
				{
					fltHash ^= fltKeyStr[i];
					fltHash *= 16777619u;
				}
				int fltStamp = (int)(fltHash & 0x7FFFFFFF);
				if (defenderServerColony.building_levels.GetValueOrDefault("__meta_last_resolved_flt", 0) == fltStamp)
				{
					GD.Print($"[COMBAT ORACLE] Fleet {attackingFleet.Id} was already resolved on target colony! Skipping duplicate calculation.");
					return (attackingFleet.Units, loot, "Battle already resolved.");
				}
				defenderServerColony.building_levels["__meta_last_resolved_flt"] = fltStamp;

				defUnitPower = 0;
				foreach (var u in GameData.Units)
				{
					int dCount = 0;
					if (defenderServerColony.unit_inventory != null && defenderServerColony.unit_inventory.TryGetValue(u.Id, out int invVal))
					{
						dCount = Math.Max(dCount, invVal);
					}
					int metaVal = defenderServerColony.building_levels.GetValueOrDefault($"__meta_unit_{u.Id}", 0);
					dCount = Math.Max(dCount, metaVal);

					// Also include units in any DOCKED fleets parked at the defender's home base!
					if (defenderServerColony.saved_fleets != null)
					{
						foreach (var df in defenderServerColony.saved_fleets)
						{
							if (df != null && df.Status == "DOCKED" && df.Units != null)
							{
								// Note: DOCKED fleets hold their own units; we engage them below as well
							}
						}
					}

					if (dCount > 0)
					{
						defStartUnits[u.Id] = dCount;
						defTotalStartUnits += dCount;
						defUnitPower += (long)(u.Def + (u.Hp * 0.5)) * dCount;
					}
				}

				// Also sum any DOCKED fleets at the defender's base so parked fleets fight and take casualties too!
				if (defenderServerColony.saved_fleets != null)
				{
					foreach (var df in defenderServerColony.saved_fleets)
					{
						if (df != null && df.Status == "DOCKED" && df.Units != null)
						{
							foreach (var kv in df.Units)
							{
								if (kv.Value <= 0) continue;
								var u = GameData.Units.Find(x => x.Id == kv.Key);
								if (u != null)
								{
									defTotalStartUnits += kv.Value;
									defUnitPower += (long)(u.Def + (u.Hp * 0.5)) * kv.Value;
								}
							}
						}
					}
				}

				int coreLvl = defenderServerColony.building_levels.GetValueOrDefault("hub_cmd", 1);
				int shdLvl = defenderServerColony.building_levels.GetValueOrDefault("hub_shd", 0);
				defWallPower = (coreLvl * 180) + GameMath.GetPlanetaryShieldDefensePower(shdLvl);
			}

			long totalDefPower = defUnitPower + defWallPower;

			// Calculate Defender Casualty Mitigation from all 3 Blueprint sources (Capped at 35%):
			// 1. Planetary Shield Building (hub_shd, Lvl 1-10): +1% per level (up to 10%)
			// 2. Research Directorate First Research (__rsh_shield_mit, Lvl 1-20): +1% per level (up to 20%)
			// 3. Alliance Science (sci_shield_mit, Lvl 1-10): +1% per level (up to 10%)
			int defShdBldLvl = defenderServerColony?.building_levels != null
				? defenderServerColony.building_levels.GetValueOrDefault("hub_shd", 0)
				: 0;
			int defRshShieldLvl = defenderServerColony?.building_levels != null
				? defenderServerColony.building_levels.GetValueOrDefault(GameMath.ResearchDirectorateShieldKey, 0)
				: 0;
			int defAllianceShieldSciLvl = defenderServerColony?.building_levels != null
				? defenderServerColony.building_levels.GetValueOrDefault($"__ali_lvl_{GameMath.AllianceShieldScienceKey}", 0)
				: 0;
			var defMitigationBreakdown = GameMath.GetDefenderCasualtyMitigationBreakdown(defShdBldLvl, defRshShieldLvl, defAllianceShieldSciLvl);
			double shieldMitigation = defMitigationBreakdown.mitigationRatio;

			// 3. True Unit-vs-Unit Combat Resolution (Both Attacker AND Defender suffer real casualties!)
			double totalEngagement = Math.Max(1.0, atkPower + totalDefPower);
			double attackerLossRatio;
			double defenderLossRatio;

			if (defTotalStartUnits <= 0)
			{
				// Defender has 0 garrison units (only base turrets/shield)
				attackerLossRatio = Math.Clamp((defWallPower * 0.25) / Math.Max(1.0, atkPower + defWallPower), 0.0, 0.25);
				defenderLossRatio = 1.0;
			}
			else if (atkPower >= totalDefPower * 1.35)
			{
				// Decisive Attacker Victory: Superior attacking force wipes out or heavily crushes defender units!
				defenderLossRatio = (atkPower >= totalDefPower * 1.85)
					? 1.0 // Total wipeout of defending units when attacker has overwhelming force!
					: Math.Clamp(0.80 + ((atkPower - totalDefPower) / totalEngagement) * 0.35, 0.80, 1.0);
				defenderLossRatio = Math.Clamp(defenderLossRatio * (1.0 - shieldMitigation * 0.4), 0.70, 1.0);

				double powerRatio = (double)totalDefPower / Math.Max(1.0, atkPower);
				attackerLossRatio = Math.Clamp(powerRatio * 0.42, 0.02, 0.45);
			}
			else if (atkPower >= totalDefPower)
			{
				// Close Attacker Victory
				defenderLossRatio = Math.Clamp(0.65 + ((atkPower - totalDefPower) / totalEngagement) * 0.5, 0.65, 0.92);
				attackerLossRatio = Math.Clamp((totalDefPower * 0.55) / totalEngagement, 0.15, 0.55);
			}
			else
			{
				// Defender holds the line, but still takes real unit casualties proportional to attacker's firepower!
				double atkToDefRatio = (double)atkPower / Math.Max(1.0, totalDefPower);
				defenderLossRatio = Math.Clamp(atkToDefRatio * 0.65 * (1.0 - shieldMitigation * 0.5), 0.10, 0.70);
				attackerLossRatio = Math.Clamp(0.60 + ((totalDefPower - atkPower) / totalEngagement) * 0.40, 0.60, 0.98);
			}

			int totalStartUnits = 0;
			int totalSurvivingUnits = 0;
			long survivingCargoCap = 0;

			foreach (var kvp in attackingFleet.Units)
			{
				int startQty = kvp.Value;
				totalStartUnits += startQty;
				int lostQty = (int)Math.Round(startQty * attackerLossRatio);
				int remQty = Math.Max(0, startQty - lostQty);
				if (remQty > 0)
				{
					survivors[kvp.Key] = remQty;
					totalSurvivingUnits += remQty;
					var u = GameData.Units.Find(x => x.Id == kvp.Key);
					if (u != null) survivingCargoCap += (long)u.Cargo * remQty;
				}
			}

			// Apply Defender Unit Casualties to Defender's Garrison Armory AND any Docked Fleets at base!
			foreach (var kvp in defStartUnits)
			{
				int startQty = kvp.Value;
				int lostQty = (defenderLossRatio >= 0.999) ? startQty : (int)Math.Ceiling(startQty * defenderLossRatio);
				int remQty = Math.Max(0, startQty - lostQty);
				defSurvivingUnits[kvp.Key] = remQty;
				defTotalSurvivingUnits += remQty;
			}

			if (defenderServerColony?.saved_fleets != null)
			{
				for (int fIdx = defenderServerColony.saved_fleets.Count - 1; fIdx >= 0; fIdx--)
				{
					var df = defenderServerColony.saved_fleets[fIdx];
					if (df == null || df.Status != "DOCKED" || df.Units == null) continue;

					var newDfUnits = new Dictionary<string, int>();
					int dfRemTotal = 0;
					foreach (var kv in df.Units)
					{
						int startQ = kv.Value;
						int lostQ = (defenderLossRatio >= 0.999) ? startQ : (int)Math.Ceiling(startQ * defenderLossRatio);
						int remQ = Math.Max(0, startQ - lostQ);
						if (remQ > 0)
						{
							newDfUnits[kv.Key] = remQ;
							dfRemTotal += remQ;
							defTotalSurvivingUnits += remQ;
						}
					}
					if (dfRemTotal <= 0)
					{
						defenderServerColony.saved_fleets.RemoveAt(fIdx);
					}
					else
					{
						df.Units = newDfUnits;
					}
				}
			}

			int defUnitsKilled = Math.Max(0, defTotalStartUnits - defTotalSurvivingUnits);
			int atkUnitsKilled = Math.Max(0, totalStartUnits - totalSurvivingUnits);

			// 4. Laser Cannon (u6) Structural Siege Calculation
			// Strict Blueprint Rule: An attack fleet ONLY deals damage to a building if a Laser Cannon (u6) unit actually exists inside the fleet!
			int initialLasersInFleet = attackingFleet.Units != null ? attackingFleet.Units.GetValueOrDefault("u6", 0) : 0;
			int survivingLasers = initialLasersInFleet > 0 ? survivors.GetValueOrDefault("u6", 0) : 0;
			string targetBldId = string.IsNullOrEmpty(attackingFleet.TargetBuildingId) ? "hub_cmd" : attackingFleet.TargetBuildingId;
			string targetBldName = GameMath.GetSiegeTargetDisplayName(targetBldId);

			var defBuildings = defenderServerColony?.building_levels ?? new Dictionary<string, int> { { "hub_cmd", 10 }, { targetBldId, 10 } };
			if (!defBuildings.ContainsKey(targetBldId)) defBuildings[targetBldId] = 5;

			// Update defender's __meta_unit_* counts in defBuildings with their surviving units!
			foreach (var u in GameData.Units)
			{
				defBuildings[$"__meta_unit_{u.Id}"] = defSurvivingUnits.GetValueOrDefault(u.Id, 0);
			}

			var siegeResult = GameMath.ApplyLaserCannonSiegeDamage(defBuildings, targetBldId, survivingLasers);

			// 5. All 4 Resources Plunder Calculation (Power, Iron, Titanium, AND Helium-3!)
			long currentCargoLoaded = loot.E + loot.I + loot.T + loot.H3;
			long freeCargoSpace = Math.Max(0, survivingCargoCap - currentCargoLoaded);
			long perResCap = freeCargoSpace / 4;

			int defSiloLvl = defBuildings.GetValueOrDefault("hub_silo", 1);
			double vaultProt = GameMath.GetSiloVaultProtection(defSiloLvl); // e.g. 5% to 30% protected
			double raidableFraction = Math.Clamp(0.45 * (1.0 - vaultProt), 0.15, 0.45);

			long stolenE = Math.Min(perResCap, defenderServerColony != null ? (long)(defenderServerColony.res_e * raidableFraction) : 2500);
			long stolenI = Math.Min(perResCap, defenderServerColony != null ? (long)(defenderServerColony.res_i * raidableFraction) : 2500);
			long stolenT = Math.Min(perResCap, defenderServerColony != null ? (long)(defenderServerColony.res_t * raidableFraction) : 1800);
			long stolenH3 = Math.Min(perResCap, defenderServerColony != null ? (long)(defenderServerColony.res_h3 * raidableFraction) : 1200);

			// If any cargo space remains after 1/4 split, fill remaining capacity from available raidable resources!
			long remSpace = Math.Max(0, freeCargoSpace - (stolenE + stolenI + stolenT + stolenH3));
			if (remSpace > 0 && defenderServerColony != null)
			{
				long maxAvailE = Math.Max(0, (long)(defenderServerColony.res_e * raidableFraction) - stolenE);
				long extraE = Math.Min(remSpace, maxAvailE);
				stolenE += extraE; remSpace -= extraE;

				long maxAvailI = Math.Max(0, (long)(defenderServerColony.res_i * raidableFraction) - stolenI);
				long extraI = Math.Min(remSpace, maxAvailI);
				stolenI += extraI; remSpace -= extraI;

				long maxAvailT = Math.Max(0, (long)(defenderServerColony.res_t * raidableFraction) - stolenT);
				long extraT = Math.Min(remSpace, maxAvailT);
				stolenT += extraT; remSpace -= extraT;

				long maxAvailH3 = Math.Max(0, (long)(defenderServerColony.res_h3 * raidableFraction) - stolenH3);
				long extraH3 = Math.Min(remSpace, maxAvailH3);
				stolenH3 += extraH3; remSpace -= extraH3;
			}

			loot.E += stolenE;
			loot.I += stolenI;
			loot.T += stolenT;
			loot.H3 += stolenH3;

			// 5B. Persist Defender Unit Casualties, Siege Damage & All 4 Plundered Resources to Supabase (Isolated to Active Server!)
			if (defenderRawRow != null && defenderServerColony != null && !string.IsNullOrEmpty(defenderRawRow.id))
			{
				try
				{
					double newDefE = Math.Max(0, defenderServerColony.res_e - stolenE);
					double newDefI = Math.Max(0, defenderServerColony.res_i - stolenI);
					double newDefT = Math.Max(0, defenderServerColony.res_t - stolenT);
					double newDefH3 = Math.Max(0, defenderServerColony.res_h3 - stolenH3);

					if (siegeResult.isBaseCompletelyDestroyed)
					{
						defBuildings.Clear();
						defBuildings["__base_destroyed"] = 1;
						defBuildings["__meta_moon"] = targetMoon;
						defBuildings["__meta_slot"] = targetSlot;
					}
					else
					{
						// Always guarantee the defender's authoritative coordinates and minimum Core presence stay intact after combat!
						defBuildings["__meta_moon"] = targetMoon;
						defBuildings["__meta_slot"] = targetSlot;
						if (defBuildings.GetValueOrDefault("hub_cmd", 0) <= 0)
						{
							defBuildings["hub_cmd"] = 1;
						}
					}

					var mergedDefBlds = defenderRawRow.building_levels != null
						? new Dictionary<string, int>(defenderRawRow.building_levels)
						: new Dictionary<string, int>();

					var mergedDefUnits = defenderRawRow.unit_inventory != null
						? new Dictionary<string, int>(defenderRawRow.unit_inventory)
						: new Dictionary<string, int>();

					var mergedDefFleets = new List<GameMath.FleetManifest>();

					object defPatchPayload;
					if (activeSrv == "S1")
					{
						var s1OldKeys = new List<string>();
						foreach (var k in mergedDefBlds.Keys)
						{
							if (!k.StartsWith("__srv_", StringComparison.Ordinal)) s1OldKeys.Add(k);
						}
						foreach (var k in s1OldKeys) mergedDefBlds.Remove(k);
						foreach (var kv in defBuildings) mergedDefBlds[kv.Key] = kv.Value;

						var s1OldUnits = new List<string>();
						foreach (var k in mergedDefUnits.Keys)
						{
							if (!k.StartsWith("__srv_", StringComparison.Ordinal)) s1OldUnits.Add(k);
						}
						foreach (var k in s1OldUnits) mergedDefUnits.Remove(k);
						foreach (var u in GameData.Units)
						{
							mergedDefUnits[u.Id] = defSurvivingUnits.GetValueOrDefault(u.Id, 0);
						}

						if (defenderServerColony.saved_fleets != null)
						{
							foreach (var f in defenderServerColony.saved_fleets)
							{
								if (f != null) mergedDefFleets.Add(f);
							}
						}
						if (defenderRawRow.saved_fleets != null)
						{
							foreach (var f in defenderRawRow.saved_fleets)
							{
								if (f != null && IsServerTaggedFleet(f, "S2")) mergedDefFleets.Add(f);
							}
						}

						defenderRawRow.building_levels = mergedDefBlds;
						defenderRawRow.res_e = newDefE;
						defenderRawRow.res_i = newDefI;
						defenderRawRow.res_t = newDefT;
						defenderRawRow.res_h3 = newDefH3;
						defenderRawRow.unit_inventory = mergedDefUnits;
						defenderRawRow.saved_fleets = mergedDefFleets;
						defenderRawRow.last_sync_time = DateTimeOffset.UtcNow.AddSeconds(2);

						defPatchPayload = new
						{
							res_e = newDefE,
							res_i = newDefI,
							res_t = newDefT,
							res_h3 = newDefH3,
							building_levels = mergedDefBlds,
							unit_inventory = mergedDefUnits,
							saved_fleets = mergedDefFleets,
							last_sync_time = DateTimeOffset.UtcNow.AddSeconds(2).ToString("o")
						};
					}
					else
					{
						string metaPrefix = $"__srv_{activeSrv}_";
						string bldPrefix = $"__srv_{activeSrv}_bld_";
						var oldKeys = new List<string>();
						foreach (var k in mergedDefBlds.Keys)
						{
							if (k.StartsWith(bldPrefix, StringComparison.Ordinal)) oldKeys.Add(k);
						}
						foreach (var k in oldKeys) mergedDefBlds.Remove(k);

						mergedDefBlds[$"{metaPrefix}init"] = 1;
						mergedDefBlds[$"{metaPrefix}res_e"] = (int)Math.Round(newDefE);
						mergedDefBlds[$"{metaPrefix}res_i"] = (int)Math.Round(newDefI);
						mergedDefBlds[$"{metaPrefix}res_t"] = (int)Math.Round(newDefT);
						mergedDefBlds[$"{metaPrefix}res_h3"] = (int)Math.Round(newDefH3);
						foreach (var kv in defBuildings) mergedDefBlds[$"{bldPrefix}{kv.Key}"] = kv.Value;

						var oldSrvUnits = new List<string>();
						foreach (var k in mergedDefUnits.Keys)
						{
							if (k.StartsWith(metaPrefix, StringComparison.Ordinal)) oldSrvUnits.Add(k);
						}
						foreach (var k in oldSrvUnits) mergedDefUnits.Remove(k);
						foreach (var u in GameData.Units)
						{
							mergedDefUnits[$"{metaPrefix}{u.Id}"] = defSurvivingUnits.GetValueOrDefault(u.Id, 0);
						}

						if (defenderRawRow.saved_fleets != null)
						{
							foreach (var f in defenderRawRow.saved_fleets)
							{
								if (f != null && !IsServerTaggedFleet(f, activeSrv)) mergedDefFleets.Add(f);
							}
						}
						if (defenderServerColony.saved_fleets != null)
						{
							foreach (var f in defenderServerColony.saved_fleets)
							{
								if (f != null) mergedDefFleets.Add(f);
							}
						}

						defPatchPayload = new
						{
							building_levels = mergedDefBlds,
							unit_inventory = mergedDefUnits,
							saved_fleets = mergedDefFleets,
							last_sync_time = DateTimeOffset.UtcNow.AddSeconds(2).ToString("o")
						};
					}

					string defUrl = $"{SupabaseUrl}/rest/v1/colonies?id=eq.{defenderRawRow.id}";
					string authBearer = !string.IsNullOrEmpty(AccessToken) ? AccessToken : SupabaseAnonKey;
					string[] defHeaders = new string[]
					{
						"Content-Type: application/json",
						$"apikey: {SupabaseAnonKey}",
						$"Authorization: Bearer {authBearer}",
						"Prefer: return=minimal"
					};
					await SendGodotRequestAsync(defUrl, defHeaders, HttpClient.Method.Patch, JsonSerializer.Serialize(defPatchPayload));
					InvalidateColoniesCache();

					// Send an instant IMPACT_SYNC signal to the defender so if they are online right now, their client reloads their surviving units & resources immediately!
					await SendInboxSignalAsync(
						"TACTICAL COMBAT ORACLE",
						defenderRawRow.commander_name,
						"INTEL",
						$"IMPACT_SYNC:{attackingFleet.Id}",
						$"{stolenE}|{stolenI}|{stolenT}|{stolenH3}|{defUnitsKilled}"
					);
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[COMBAT ORACLE] Defender siege state sync warning: {ex.Message}");
				}
			}

			// 6. Build Clear, Gamer-Friendly Attack & Defense Reports (Attacker receives Attack Report, Defender receives Defense Report!)
			string attackerCallsign = !string.IsNullOrWhiteSpace(attackingFleet.OriginCommanderName)
				? attackingFleet.OriginCommanderName.Trim().ToUpper()
				: CachedCommanderName.Trim().ToUpper();

			string atkOutcomeHeader = (totalSurvivingUnits > 0 && atkPower >= totalDefPower)
				? "🏆 ATTACK VICTORY — ENEMY DEFENSES BREACHED!"
				: (totalSurvivingUnits > 0 ? "⚔️ ATTACK SKIRMISH CONCLUDED — FLEET RETURNING" : "💀 ATTACK DEFEAT — STRIKE FLEET DESTROYED");

			string defOutcomeHeader = (atkPower < totalDefPower || totalSurvivingUnits == 0)
				? "🛡️ DEFENSE VICTORY — HOSTILE FLEET REPELLED!"
				: "🚨 BASE BREACHED — DEFENSIVE GARRISON OVERRUN";

			string oldEffStr = GameMath.FormatEffectiveLevel(siegeResult.oldEffectiveLevel);
			string newEffStr = GameMath.FormatEffectiveLevel(siegeResult.newEffectiveLevel);
			string siegeLine = initialLasersInFleet <= 0
				? $"🛡️ Structural Integrity: Attack fleet carried 0 Laser Cannons — no building damage dealt to {targetBldName}."
				: (survivingLasers > 0
					? $"💥 Laser Cannon Strike ({survivingLasers:N0}x Cannons @ 1 HP/Cannon): Hit {targetBldName} for -{siegeResult.hpDamageDealt:N0} HP! Building dropped from Lvl {oldEffStr} ({siegeResult.oldHp:N0} HP) ➔ Lvl {newEffStr} ({siegeResult.newHp:N0} HP)."
					: $"🛡️ Structural Integrity: All {initialLasersInFleet:N0}x Laser Cannons were destroyed before bombarding {targetBldName} (0 HP damage).");

			if (siegeResult.isBaseCompletelyDestroyed)
			{
				siegeLine += "\n🔥 TOTAL BASE DESTRUCTION: Every building at the target base reached 0% HP (Level 0)! The base has been wiped out!";
			}

			string attackerReportBody =
				$"{atkOutcomeHeader}\n" +
				$"Target Base: {activeSrv} : Moon M-{targetMoon} : Slot B-{targetSlot:D2} ({defenderName})\n" +
				$"• Your Attack Fleet [{attackingFleet.Name}]: Sent {totalStartUnits:N0} Units  |  Lost: -{atkUnitsKilled:N0}  |  Survived: {totalSurvivingUnits:N0} Units\n" +
				$"• Enemy Garrison ({defenderName}): Defended with {defTotalStartUnits:N0} Units  |  Lost: -{defUnitsKilled:N0}  |  Survived: {defTotalSurvivingUnits:N0} Units\n" +
				$"• Enemy Planetary Shield: +{GameMath.GetPlanetaryShieldDefensePower(defShdBldLvl):N0} DEF  |  Casualty Mitigation: {defMitigationBreakdown.cappedTotalPct}% / 35% Cap\n" +
				$"{siegeLine}\n" +
				$"💰 RESOURCES GAINED (PLUNDERED): +⚡ {stolenE:N0} Power  |  +⛏️ {stolenI:N0} Iron  |  +💎 {stolenT:N0} Titanium  |  +⛽ {stolenH3:N0} Helium-3";

			string defenderReportBody =
				$"{defOutcomeHeader}\n" +
				$"Attacked By: Commander {attackerCallsign}  ➔  Your Base ({activeSrv} : Moon M-{targetMoon} : Slot B-{targetSlot:D2})\n" +
				$"• Your Defending Garrison ({defenderName}): Defended with {defTotalStartUnits:N0} Units  |  Lost: -{defUnitsKilled:N0}  |  Survived: {defTotalSurvivingUnits:N0} Units\n" +
				$"• Hostile Attack Fleet [{attackingFleet.Name}]: Sent {totalStartUnits:N0} Units  |  Lost: -{atkUnitsKilled:N0}  |  Survived: {totalSurvivingUnits:N0} Units\n" +
				$"• Your Planetary Shield: +{GameMath.GetPlanetaryShieldDefensePower(defShdBldLvl):N0} DEF  |  Casualty Mitigation: {defMitigationBreakdown.cappedTotalPct}% / 35% Cap (Shield: +{defMitigationBreakdown.shieldBuildingPct}% · Research: +{defMitigationBreakdown.researchDirectoratePct}% · Alliance Science: +{defMitigationBreakdown.allianceSciencePct}%)\n" +
				$"{siegeLine}\n" +
				$"📉 RESOURCES LOST (STOLEN): -⚡ {stolenE:N0} Power  |  -⛏️ {stolenI:N0} Iron  |  -💎 {stolenT:N0} Titanium  |  -⛽ {stolenH3:N0} Helium-3";

			// Send ATTACK REPORT strictly to the Attacker ONLY
			await SendInboxSignalAsync(
				"TACTICAL COMBAT ORACLE",
				attackerCallsign,
				"BATTLE",
				$"⚔️ ATTACK REPORT: {activeSrv}:{targetMoon}:{targetSlot} ({defenderName})",
				attackerReportBody
			);

			// Send DEFENSE REPORT strictly to the Defender ONLY (using TACTICAL COMBAT ORACLE as sender so it NEVER appears in Attacker's inbox!)
			if (defenderRawRow != null && !string.IsNullOrEmpty(defenderRawRow.commander_name) &&
				!string.Equals(defenderRawRow.commander_name.Trim(), attackerCallsign, StringComparison.OrdinalIgnoreCase))
			{
				await SendInboxSignalAsync(
					"TACTICAL COMBAT ORACLE",
					defenderRawRow.commander_name,
					"BATTLE",
					$"🛡️ DEFENSE REPORT: SECTOR {activeSrv}:M-{targetMoon}:B-{targetSlot:D2} ATTACKED BY {attackerCallsign}!",
					defenderReportBody
				);
			}

			return (survivors, loot, attackerReportBody);
		}

		/// <summary>
		/// Mutual Offline Battle Resolution: If the Attacker launched an attack and then went offline (closed their phone),
		/// and the Defender is online when the incoming timer reaches 00:00, this locates the Attacker's in-transit fleet in Supabase,
		/// resolves the battle on time, updates the Attacker's fleet to RETURN with surviving units & loot, and updates the Defender's base!
		/// </summary>
		public async Task<bool> ResolveDefenderOfflineImpactAsync(IncomingFleetSignal sig)
		{
			if (sig == null || sig.Protocol != "RAID" || string.IsNullOrEmpty(sig.FleetId)) return false;

			string activeSrv = string.IsNullOrWhiteSpace(CachedServerId) ? "S1" : CachedServerId.Trim().ToUpper();
			try
			{
				string url = $"{SupabaseUrl}/rest/v1/colonies?select=*";
				string authBearer = !string.IsNullOrEmpty(AccessToken) ? AccessToken : SupabaseAnonKey;
				string[] headers = new string[]
				{
					"Content-Type: application/json",
					$"apikey: {SupabaseAnonKey}",
					$"Authorization: Bearer {authBearer}"
				};

				var (code, responseText) = await SendGodotRequestAsync(url, headers, HttpClient.Method.Get);
				if (code != 200 || string.IsNullOrEmpty(responseText)) return false;

				var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
				var rows = JsonSerializer.Deserialize<List<ColonySaveData>>(responseText, options);
				if (rows == null) return false;

				foreach (var row in rows)
				{
					if (row?.saved_fleets == null) continue;
					var targetFleet = row.saved_fleets.Find(f => f != null && f.Id == sig.FleetId);
					if (targetFleet != null)
					{
						// If the attacker is online and already switched this fleet to RETURN or DOCKED, we just reload our colony!
						if (targetFleet.Status != "IN_TRANSIT" || targetFleet.Protocol != "RAID")
						{
							return true;
						}

						GD.Print($"[MUTUAL COMBAT ORACLE] Attacker ({row.commander_name}) is offline at impact! Resolving battle for Fleet {sig.FleetId} now...");
						targetFleet.OriginCommanderName = row.commander_name ?? sig.SenderName ?? "COMMANDER";
						var (survivors, loot, _) = await ResolveFleetRaidAndSiegeAsync(targetFleet, CachedMoonId, CachedBaseSlot);

						int survCount = 0;
						foreach (var kv in survivors) survCount += kv.Value;

						GameMath.TryParseSectorCoordinate(sig.OriginCoord, out int origMoon, out int origSlot);
						float srvSpeed = Math.Max(1.0f, CachedServerSpeed);

						if (survCount <= 0)
						{
							row.saved_fleets.Remove(targetFleet);
						}
						else
						{
							int retSec = GameMath.CalcFleetTravelTimeSeconds(CachedMoonId, CachedBaseSlot, origMoon, origSlot, survivors, srvSpeed);
							long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
							targetFleet.Units = survivors;
							targetFleet.Cargo = loot;
							targetFleet.Status = "IN_TRANSIT";
							targetFleet.Protocol = "RETURN";
							targetFleet.LocationCoord = $"{activeSrv}:{CachedMoonId}:{CachedBaseSlot}";
							targetFleet.TargetCoord = $"{activeSrv}:{origMoon}:{origSlot}";
							targetFleet.TotalFlightDurationSec = Math.Max(1.0, retSec);
							targetFleet.DepartureTimeUnixMs = nowMs;
							targetFleet.EtaSecondsLeft = retSec;
							targetFleet.ArrivalTimeUnixMs = nowMs + ((long)retSec * 1000L);
							targetFleet.LastUpdatedUtcMs = nowMs;
						}

						// Save updated fleet state back to the offline Attacker's row so when they log in, their fleet is flying home with loot!
						string atkUrl = $"{SupabaseUrl}/rest/v1/colonies?id=eq.{row.id}";
						string[] patchHeaders = new string[]
						{
							"Content-Type: application/json",
							$"apikey: {SupabaseAnonKey}",
							$"Authorization: Bearer {authBearer}",
							"Prefer: return=minimal"
						};
						var atkPatch = new
						{
							saved_fleets = row.saved_fleets,
							last_sync_time = DateTimeOffset.UtcNow.AddSeconds(2).ToString("o")
						};
						await SendGodotRequestAsync(atkUrl, patchHeaders, HttpClient.Method.Patch, JsonSerializer.Serialize(atkPatch));
						InvalidateColoniesCache();
						return true;
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MUTUAL COMBAT ORACLE] Error resolving offline attacker fleet: {ex.Message}");
			}
			return false;
		}

		/// <summary>
		/// Triggers native Mobile Haptic Vibration + Web/PWA System Notification Banner when a new incoming fleet or battle impact is detected!
		/// </summary>
		public static void TriggerMobileAndWebAlert(string alertTitle, string alertBody, bool isHostile)
		{
			try
			{
				// 1. Mobile Haptic Vibration (Android / iOS / Mobile Web)
				if (isHostile)
				{
					Input.VibrateHandheld(450);
				}
				else
				{
					Input.VibrateHandheld(180);
				}

				// 2. Web / Mobile Browser Notification API + Permission Request (When running on Web/Mobile Browser export)
				if (OS.HasFeature("web"))
				{
					string safeTitle = (alertTitle ?? "MOONS: TOTAL WAR").Replace("'", "\\'").Replace("\n", " ");
					string safeBody = (alertBody ?? "").Replace("'", "\\'").Replace("\n", " ");
					JavaScriptBridge.Eval($@"
						(function() {{
							try {{
								if ('Notification' in window) {{
									if (Notification.permission === 'granted') {{
										new Notification('{safeTitle}', {{ body: '{safeBody}', tag: 'moons-tactical-alert', renotify: true }});
									}} else if (Notification.permission !== 'denied') {{
										Notification.requestPermission().then(function(p) {{
											if (p === 'granted') {{
												new Notification('{safeTitle}', {{ body: '{safeBody}', tag: 'moons-tactical-alert', renotify: true }});
											}}
										}});
									}}
								}}
							}} catch (e) {{}}
						}})();
					");
				}
			}
			catch { }
		}
	}
}
