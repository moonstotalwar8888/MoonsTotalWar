using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: CLASH OF KINGS COMMAND COCKPIT HUD (v18.0 Live Boost Edition)
	/// - Top Bar: Commander Profile, Colony Transporter, 5 Resource Capsules, Rank Trophy.
	/// - Bottom Bar: Command Navigation Dock.
	/// - Engineering Dock: Active Build Timer, Queue Dropdown, LIFO Cancellation Engine.
	/// - Cloud Sync: Fetches Supabase state on load, runs ChronoEngine offline catchup.
	/// - Boost Engine: Applies live multipliers to 1-second ticks and auto-cleans expired boosts.
	/// - Persistence: 60-second auto-saves and Instant Save triggers.
	/// </summary>
	public partial class BaseHUD : Control
	{
		public static BaseHUD Instance { get; private set; }

		// Signals
		[Signal] public delegate void AvatarClickedEventHandler();
		[Signal] public delegate void ColonySwitcherClickedEventHandler();
		[Signal] public delegate void ResourceClickedEventHandler(string resourceType);
		[Signal] public delegate void LeaderboardClickedEventHandler();
		[Signal] public delegate void NavTierSelectedEventHandler(string tierName);
		[Signal] public delegate void EditModeToggledEventHandler(bool isEditing);
		[Signal] public delegate void SaveLayoutRequestedEventHandler();
		[Signal] public delegate void ColonyDataLoadedEventHandler(); 
		[Signal] public delegate void QueuedBuildingCompletedEventHandler(string buildingId, int newLevel);
		[Signal] public delegate void CameraZoomRequestedEventHandler(float zoomMultiplier);
		[Signal] public delegate void CameraResetRequestedEventHandler();

		// Colony Data
		public string CommanderName = "COMMANDER ALPHA";
		public string CurrentBaseName = "Alpha Outpost";
		public int CurrentMoon = 100;
		public int CurrentBaseSlot = 1;

		// Live Resource Balances
		public double ResE = 5000;
		public double ResI = 5000;
		public double ResT = 5000;
		public double ResH3 = 2500;
		public long GGold = 100; 

		public int StorageSiloLevel = 1;
		public long SiloCap = 7500;
		public float ServerSpeed = 1.0f;

		// Master Dictionaries (Synced with Cloud)
		public Dictionary<string, int> BuildingLevels = new Dictionary<string, int>();
		public Dictionary<string, GameMath.BoostData> ActiveBoosts = new Dictionary<string, GameMath.BoostData>();

		// Premium Build Queue System
		public GameMath.BuildQueueItem ActiveBuild = null;
		public List<GameMath.BuildQueueItem> BuildQueue = new List<GameMath.BuildQueueItem>();

		// Simultaneous Orbital Shipyard Bays, Garrison Armory Inventory & Fleet Hangar
		public Dictionary<string, int> UnitInventory => SupabaseService.CachedUnitInventory;
		public Dictionary<string, GameMath.UnitAssemblyBayJob> ActiveUnitBays => SupabaseService.CachedUnitBays;
		public List<GameMath.FleetManifest> SavedFleets => SupabaseService.CachedSavedFleets;

		// Fleet Data
		public int ActiveFleetCount = 0;
		public string ActiveFleetTarget = "NONE";
		public double ActiveFleetEta = 0.0;

		// State Flags
		public bool IsEditModeActive { get; private set; } = false;
		private bool _isDataLoaded = false;
		private bool _hasVerifiedCloudData = false; // Strict Cloud Safety Lock: Prevents saving until cloud data or verified session cache is loaded!
		private double _cloudRetryTimer = 0.0;
		private bool _isQueueDropdownOpen = false;

		// Timers & Real-World UTC Anchors (Immune to Mobile App Minimize / OS Background Suspension)
		private long _lastProcessUtcMs = 0;
		private long _lastResourceTickUtcMs = 0;
		private double _autoSaveTimer = 0.0;

		// UI Node References
		private Panel _bottomPanelNode;
		private Label _lblCmdName;
		private Button _btnColonySwitch;
		private Label _lblValE, _lblValI, _lblValT, _lblValH3, _lblValGGold;
		private Label _lblRateE, _lblRateI, _lblRateT, _lblRateH3, _lblRateGGold;
		private StyleBoxFlat _capStyleE, _capStyleI, _capStyleT, _capStyleH3;
		private string _activeSmartHangarResource = "";
		private ColorRect _smartHangarOverlay;
		private PanelContainer _smartHangarPanel;
		private StyleBoxFlat _smartHangarPanelStyle;
		private Label _lblSmartHangarTitle;
		private VBoxContainer _smartHangarContentBox;
		private double _h3StarvationTimerSec = 0.0;
		private double _smartHangarLiveRefreshTimer = 0.0;
		private Label _lblBuildName, _lblBuildTimer, _lblTotalQueueTime;
		private Button _btnQueueToggle;
		private PanelContainer _queueDropdownPanel;
		private VBoxContainer _queueListContainer;
		private Button _btnCancelLast;
		private PanelContainer _buildDockPanel;
		private PanelContainer _fleetDockPanel;
		private StyleBoxFlat _fleetDockStyle;
		private Label _lblFleetStatus, _lblFleetEta, _lblFleetThreatAlert;
		private Button _btnFleetDropdownToggle;
		private Button _btnBattleLogsShortcut;
		private PanelContainer _fleetDropdownPanel;
		private VBoxContainer _fleetDropdownList;
		private bool _isFleetDropdownOpen = false;
		private PanelContainer _fleetActivityToastPanel;
		private StyleBoxFlat _fleetActivityToastStyle;
		private Label _lblFleetActivityToast;
		private double _fleetActivityToastTimer = 0.0;
		private double _incomingSignalPollTimer = 0.0;
		private readonly HashSet<string> _resolvingFleetIds = new HashSet<string>();
		private Button _btnEditLayoutToggle;
		private Panel _cameraControlDock;
		private CommanderProfileModal _profileModal;
		private TacticalCommsModal _commsModal;
		private PanelContainer _commsAlertBadge;
		private Label _lblCommsAlertCount;
		public int UnreadInboxCount { get; private set; } = 0;
		public int UnreadPersonalCount { get; private set; } = 0;
		public int UnreadBattleCount { get; private set; } = 0;

		public bool IsAnyOverlayModalOpen()
		{
			if (_profileModal != null && IsInstanceValid(_profileModal) && _profileModal.Visible) return true;
			if (_commsModal != null && IsInstanceValid(_commsModal) && _commsModal.Visible) return true;
			if (_smartHangarOverlay != null && IsInstanceValid(_smartHangarOverlay) && _smartHangarOverlay.Visible) return true;
			if (BuildingInspectorModal.Instance != null && IsInstanceValid(BuildingInspectorModal.Instance) && BuildingInspectorModal.Instance.Visible) return true;
			if (_isQueueDropdownOpen || _isFleetDropdownOpen) return true;
			return false;
		}

		public bool IsAnyPrimaryModalOpen()
		{
			if (_profileModal != null && IsInstanceValid(_profileModal) && _profileModal.Visible) return true;
			if (_commsModal != null && IsInstanceValid(_commsModal) && _commsModal.Visible) return true;
			if (_smartHangarOverlay != null && IsInstanceValid(_smartHangarOverlay) && _smartHangarOverlay.Visible) return true;
			if (BuildingInspectorModal.Instance != null && IsInstanceValid(BuildingInspectorModal.Instance) && BuildingInspectorModal.Instance.Visible) return true;
			return false;
		}

		public void SyncSideDocksVisibilityForActiveModals()
		{
			bool hideSideDocks = IsAnyPrimaryModalOpen();
			if (hideSideDocks)
			{
				_isFleetDropdownOpen = false;
				_isQueueDropdownOpen = false;
			}
			if (_fleetDockPanel != null && IsInstanceValid(_fleetDockPanel))
				_fleetDockPanel.Visible = !hideSideDocks;
			if (_buildDockPanel != null && IsInstanceValid(_buildDockPanel))
				_buildDockPanel.Visible = !hideSideDocks;
			if (_cameraControlDock != null && IsInstanceValid(_cameraControlDock))
				_cameraControlDock.Visible = !hideSideDocks;
			if (_fleetDropdownPanel != null && IsInstanceValid(_fleetDropdownPanel))
				_fleetDropdownPanel.Visible = !hideSideDocks && _isFleetDropdownOpen;
			if (_queueDropdownPanel != null && IsInstanceValid(_queueDropdownPanel))
				_queueDropdownPanel.Visible = !hideSideDocks && _isQueueDropdownOpen;

			if (MoonView.Instance != null && IsInstanceValid(MoonView.Instance))
				MoonView.Instance.SetRadarNavConsoleVisible(!hideSideDocks);
			if (GalaxyView.Instance != null && IsInstanceValid(GalaxyView.Instance))
				GalaxyView.Instance.SetRadarTelemetryVisible(!hideSideDocks);
		}

		public override void _Ready()
		{
			Instance = this;

			ServerSpeed = Math.Max(1.0f, SupabaseService.CachedServerSpeed);
			CommanderName = SupabaseService.CachedCommanderName;
			CurrentBaseName = SupabaseService.CachedBaseName;
			CurrentMoon = SupabaseService.CachedMoonId;
			CurrentBaseSlot = SupabaseService.CachedBaseSlot;

			// If we already have a verified session cache (from ServerSelect or switching between Base / Moon / Galaxy),
			// hydrate all resources, building levels, layout coordinates, and queues IMMEDIATELY on Frame 0 (zero 1-second jump!)
			if (SupabaseService.HasValidSessionColonyCache && SupabaseService.CachedBuildingLevels.Count > 0)
			{
				ResE = SupabaseService.CachedResE;
				ResI = SupabaseService.CachedResI;
				ResT = SupabaseService.CachedResT;
				ResH3 = SupabaseService.CachedResH3;
				GGold = SupabaseService.CachedGGold;
				BuildingLevels = new Dictionary<string, int>(SupabaseService.CachedBuildingLevels);
				ActiveBuild = SupabaseService.CachedActiveBuild;
				BuildQueue = SupabaseService.CachedBuildQueue != null
					? new List<GameMath.BuildQueueItem>(SupabaseService.CachedBuildQueue)
					: new List<GameMath.BuildQueueItem>();
				ActiveBoosts = SupabaseService.CachedActiveBoosts != null
					? new Dictionary<string, GameMath.BoostData>(SupabaseService.CachedActiveBoosts)
					: new Dictionary<string, GameMath.BoostData>();
				StorageSiloLevel = BuildingLevels.GetValueOrDefault("hub_silo", 1);
				RecalculateSiloCap();
				_isDataLoaded = true;
				_hasVerifiedCloudData = true;
			}

			SetAnchorsPreset(LayoutPreset.FullRect);
			MouseFilter = MouseFilterEnum.Ignore;

			GetViewport().Connect("size_changed", Callable.From(RecalculateLayoutBounds));

			BuildTopCockpitBar();
			BuildTacticalSideDocks();
			BuildTacticalCameraControlDock();
			BuildBottomCommandNavigationDock();
			BuildSmartHangarOverlay();

			_profileModal = new CommanderProfileModal();
			AddChild(_profileModal);

			_commsModal = new TacticalCommsModal();
			AddChild(_commsModal);

			AvatarClicked += () => { CloseAllModalsExcept("PROFILE"); _profileModal?.OpenOwnCommanderProfile(); };
			ColonySwitcherClicked += () => { CloseAllModalsExcept("PROFILE"); _profileModal?.OpenEmpireColonySwitcher(); };
			LeaderboardClicked += () => { CloseAllModalsExcept("PROFILE"); _profileModal?.OpenGalacticLeaderboard(); };
			ResourceClicked += (resType) => OpenSmartHangarModal(resType);

			RecalculateLayoutBounds();
			UpdateHUDDisplay();
			UpdateEngineeringDockUI();

			// Initiate Cloud Handshake & Immediate Incoming Fleet Signal Check
			// If we already have a verified session cache from switching between Base, Moon, or Galaxy views,
			// do NOT overwrite our live in-memory fleets with a duplicate GET on every scene change!
			if (!SupabaseService.HasValidSessionColonyCache || SupabaseService.CachedBuildingLevels.Count == 0)
			{
				LoadColonyDataFromCloud();
			}
			else
			{
				EmitSignal(SignalName.ColonyDataLoaded);
			}
			PollIncomingHostileSignalsAsync();
		}

		public override void _ExitTree()
		{
			if (Instance == this) Instance = null;
			base._ExitTree();
		}

		public void CloseAllModalsExcept(string keepModal = "")
		{
			_isFleetDropdownOpen = false;
			_isQueueDropdownOpen = false;
			if (_fleetDropdownPanel != null) _fleetDropdownPanel.Visible = false;
			if (_queueDropdownPanel != null) _queueDropdownPanel.Visible = false;

			if (keepModal != "HANGAR" && _smartHangarOverlay != null)
				_smartHangarOverlay.Visible = false;
			if (keepModal != "PROFILE")
				_profileModal?.CloseModal();
			if (keepModal != "COMMS")
				_commsModal?.CloseModal();
			if (keepModal != "INSPECTOR" && BuildingInspectorModal.Instance != null && IsInstanceValid(BuildingInspectorModal.Instance) && BuildingInspectorModal.Instance.Visible)
				BuildingInspectorModal.Instance.CloseInspector();

			SyncSideDocksVisibilityForActiveModals();
		}

		public void OpenPlayerBioModal(SupabaseService.GalacticCommanderEntry entry)
		{
			CloseAllModalsExcept("PROFILE");
			_profileModal?.OpenPlayerBioDossier(entry);
		}

		public void OpenEmptyMoonSlotModal(int moonId, int slotId)
		{
			CloseAllModalsExcept("PROFILE");
			_profileModal?.OpenEmptyMoonSlotModal(moonId, slotId);
		}

		public void OpenCommsInboxModal(string category = "PERSONAL")
		{
			CloseAllModalsExcept("COMMS");
			_commsModal?.OpenCommsInbox(category);
		}

		public void OpenDirectCommanderThreadModal(string targetCommanderName)
		{
			CloseAllModalsExcept("COMMS");
			_commsModal?.OpenPlayerConversationThread(targetCommanderName);
		}

		public void UpdateInboxAlertBadges(int totalUnread, int unreadPersonal = 0, int unreadBattle = 0)
		{
			UnreadInboxCount = Math.Max(0, totalUnread);
			UnreadPersonalCount = Math.Max(0, unreadPersonal);
			UnreadBattleCount = Math.Max(0, unreadBattle);

			if (_commsAlertBadge != null && IsInstanceValid(_commsAlertBadge))
			{
				_commsAlertBadge.Visible = UnreadInboxCount > 0;
				if (_lblCommsAlertCount != null && IsInstanceValid(_lblCommsAlertCount))
				{
					_lblCommsAlertCount.Text = UnreadInboxCount > 99 ? " 99+ " : $" 🔴 {UnreadInboxCount} ";
				}
			}

			if (_btnBattleLogsShortcut != null && IsInstanceValid(_btnBattleLogsShortcut))
			{
				_btnBattleLogsShortcut.Text = UnreadBattleCount > 0 ? $"⚔️ LOGS ({UnreadBattleCount})" : "⚔️ LOGS";
				_btnBattleLogsShortcut.Modulate = UnreadBattleCount > 0 ? new Color("#EF4444") : new Color("#FBBF24");
			}
		}

		public void OpenGlobalChatModal()
		{
			CloseAllModalsExcept("COMMS");
			_commsModal?.OpenGlobalServerChat();
		}

		public void OpenAllianceHQModal()
		{
			CloseAllModalsExcept("COMMS");
			_commsModal?.OpenAllianceHQInterface();
		}

		private async void LoadColonyDataFromCloud()
		{
			if (SupabaseService.Instance == null) return;

			GD.Print("[BASE HUD] Initiating Cloud Handshake...");
			var data = await SupabaseService.Instance.FetchColonyStateAsync();

			if (data != null)
			{
				CommanderName = !string.IsNullOrEmpty(data.commander_name) ? data.commander_name : SupabaseService.CachedCommanderName;
				SupabaseService.CachedCommanderName = CommanderName;
				if (!string.IsNullOrEmpty(data.base_name))
				{
					CurrentBaseName = data.base_name;
					SupabaseService.SetServerCustomBaseName(SupabaseService.CachedServerId, data.base_name);
				}

				ResE = data.res_e;
				ResI = data.res_i;
				ResT = data.res_t;
				ResH3 = data.res_h3;
				GGold = data.mgold; 
				
				if (data.building_levels != null) BuildingLevels = data.building_levels;

				// Check if our base was explicitly destroyed by an enemy Laser Cannon siege (__base_destroyed == 1) while offline
				bool anyStanding = false;
				foreach (var b in GameMath.SiegeTargetBuildings)
				{
					if (BuildingLevels.GetValueOrDefault(b.Id, 0) > 0)
					{
						anyStanding = true;
						break;
					}
				}
				if (BuildingLevels.GetValueOrDefault("__base_destroyed", 0) == 1 && !anyStanding)
				{
					HandleTotalBaseLossAndServerRestart();
					return;
				}
				if (!anyStanding)
				{
					// Self-heal starter buildings if a legacy/empty row was loaded so the player is never bounced back to Server Select!
					BuildingLevels["hub_cmd"] = 1;
					BuildingLevels["hub_silo"] = 1;
					BuildingLevels["hub_mgd"] = 1;
					BuildingLevels["dist_e_0"] = 1;
					BuildingLevels["dist_i_0"] = 1;
					BuildingLevels["dist_t_0"] = 1;
					BuildingLevels["dist_h3_0"] = 1;
				}

				int metaMoon = BuildingLevels.GetValueOrDefault("__meta_moon", data.moon_id);
				int metaSlot = BuildingLevels.GetValueOrDefault("__meta_slot", data.slot_id);
				if (metaMoon >= 1 && metaMoon <= 1000)
				{
					CurrentMoon = metaMoon;
					BuildingLevels["__meta_moon"] = metaMoon;
				}
				if (metaSlot >= 1 && metaSlot <= 20)
				{
					CurrentBaseSlot = metaSlot;
					BuildingLevels["__meta_slot"] = metaSlot;
				}

				SupabaseService.CachedMoonId = CurrentMoon;
				SupabaseService.CachedBaseSlot = CurrentBaseSlot;
				SupabaseService.CachedRadarLevel = Math.Clamp(BuildingLevels.GetValueOrDefault("hub_rng", 1), 1, 10);

				foreach (var u in GameData.Units)
				{
					int savedCount = (data.unit_inventory != null && data.unit_inventory.TryGetValue(u.Id, out int invVal))
						? invVal
						: BuildingLevels.GetValueOrDefault($"__meta_unit_{u.Id}", 0);
					SupabaseService.CachedUnitInventory[u.Id] = Math.Max(0, savedCount);
					BuildingLevels[$"__meta_unit_{u.Id}"] = Math.Max(0, savedCount);
				}

				// Extract Queue & Boost Data from Cloud
				ActiveBuild = data.active_build;
				BuildQueue = data.build_queue ?? new List<GameMath.BuildQueueItem>();
				ActiveBoosts = data.active_boosts ?? new Dictionary<string, GameMath.BoostData>();

				string activeSrv = string.IsNullOrWhiteSpace(SupabaseService.CachedServerId) ? "S1" : SupabaseService.CachedServerId.Trim().ToUpper();
				string boostMetaKey = activeSrv == "S1" ? "__meta_base_name_S1" : $"__srv_{activeSrv}_base_name";
				if (ActiveBoosts.TryGetValue(boostMetaKey, out var bNameMeta) && bNameMeta != null && !string.IsNullOrWhiteSpace(bNameMeta.Label))
				{
					CurrentBaseName = bNameMeta.Label.Trim();
					SupabaseService.SetServerCustomBaseName(activeSrv, CurrentBaseName);
				}

				StorageSiloLevel = BuildingLevels.GetValueOrDefault("hub_silo", 1);
				RecalculateSiloCap();

				// Run ChronoEngine to catch up on offline production, queues, and fractional boosts
				double curH3Drain = ChronoEngine.CalculateEmpireH3Drain(SupabaseService.CachedUnitInventory, SupabaseService.CachedSavedFleets);
				var chrono = ChronoEngine.SimulateOfflineTimeline(
					ResE, ResI, ResT, ResH3, 
					BuildingLevels, 
					ActiveBuild,
					BuildQueue,
					ActiveBoosts,
					data.last_sync_time, 
					DateTimeOffset.UtcNow, 
					ServerSpeed, 
					curH3Drain 
				);

				ResE = Math.Max(0.0, chrono.resE);
				ResI = Math.Max(0.0, chrono.resI);
				ResT = Math.Max(0.0, chrono.resT);
				ResH3 = Math.Max(0.0, chrono.resH3);
				ActiveBuild = chrono.activeBuild;
				BuildQueue = chrono.buildQueue;
				ActiveBoosts = chrono.activeBoosts;

				long nowLoadMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				_lastProcessUtcMs = nowLoadMs;
				_lastResourceTickUtcMs = nowLoadMs;
				if (ActiveBuild != null && ActiveBuild.CompletionTimeUnixMs <= 0)
				{
					ActiveBuild.CompletionTimeUnixMs = nowLoadMs + (long)(Math.Max(0.1, ActiveBuild.DurationLeft) * 1000.0);
				}

				_isDataLoaded = true;
				_hasVerifiedCloudData = true;
				_cloudRetryTimer = 0.0;
				UpdateHUDDisplay();
				UpdateEngineeringDockUI();
				
				EmitSignal(SignalName.ColonyDataLoaded);
				TriggerInstantSave();
			}
			else
			{
				GD.PrintErr("[BASE HUD] Cloud fetch failed due to network timeout/error! Cloud saving is LOCKED until genuine cloud data is fetched so player progress is NEVER overwritten.");
				long nowFallbackMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				_lastProcessUtcMs = nowFallbackMs;
				_lastResourceTickUtcMs = nowFallbackMs;

				if (SupabaseService.HasValidSessionColonyCache && SupabaseService.CachedBuildingLevels.Count > 0)
				{
					_isDataLoaded = true;
					_hasVerifiedCloudData = true;
					UpdateHUDDisplay();
					UpdateEngineeringDockUI();
					EmitSignal(SignalName.ColonyDataLoaded);
				}
				else
				{
					// Do NOT set _hasVerifiedCloudData = true! Wait and retry cloud fetch automatically!
					_isDataLoaded = false;
					_hasVerifiedCloudData = false;
				}
			}
		}

		public override void _Notification(int what)
		{
			base._Notification(what);
			// When player minimizes/switches apps, presses Android Back, or closes window, immediately save state to journal & cloud!
			if (what == NotificationApplicationFocusOut ||
				what == NotificationApplicationPaused ||
				what == NotificationWMCloseRequest ||
				what == NotificationWMGoBackRequest)
			{
				if (_hasVerifiedCloudData)
				{
					TriggerInstantSave();
				}
			}
			else if (what == NotificationApplicationFocusIn || what == NotificationApplicationResumed)
			{
				// When waking from mobile minimize/background, immediately poll for any incoming fleet alerts or combat impacts!
				if (_hasVerifiedCloudData)
				{
					PollIncomingHostileSignalsAsync();
				}
			}
		}

		public void TriggerInstantSave()
		{
			// CRITICAL SAFETY LOCK: Never allow saving to Supabase unless we have verified real cloud data!
			// This guarantees that a bad network connection on login can NEVER overwrite a player's Level 7+ base with Level 1 template values!
			if (!_isDataLoaded || !_hasVerifiedCloudData || SupabaseService.Instance == null || BuildingLevels == null || BuildingLevels.Count == 0)
			{
				return;
			}

			GD.Print("[BASE HUD] Triggering Instant Cloud Save...");
			_ = SupabaseService.Instance.SaveColonyStateAsync(ResE, ResI, ResT, ResH3, GGold, BuildingLevels, ActiveBuild, BuildQueue, ActiveBoosts, CommanderName);
			_autoSaveTimer = 0.0; 
		}

		public override void _Process(double delta)
		{
			if (!_isDataLoaded || !_hasVerifiedCloudData)
			{
				// Automatically retry fetching cloud state every 4 seconds if initial login had bad network!
				_cloudRetryTimer += delta;
				if (_cloudRetryTimer >= 4.0)
				{
					_cloudRetryTimer = 0.0;
					LoadColonyDataFromCloud();
				}
				return;
			}

			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			if (_lastProcessUtcMs <= 0) _lastProcessUtcMs = nowMs;
			if (_lastResourceTickUtcMs <= 0) _lastResourceTickUtcMs = nowMs;

			// Real-world elapsed seconds (handles both normal 60fps frames AND 5-minute mobile app minimize!)
			double realElapsedSec = Math.Max(delta, (nowMs - _lastProcessUtcMs) / 1000.0);
			_lastProcessUtcMs = nowMs;

			_autoSaveTimer += realElapsedSec;

			// 1. Process Active Build & Queue using Real-World UTC Timestamps
			if (ActiveBuild != null)
			{
				if (ActiveBuild.CompletionTimeUnixMs <= 0)
				{
					ActiveBuild.CompletionTimeUnixMs = nowMs + (long)(Math.Max(0.1, ActiveBuild.DurationLeft) * 1000.0);
				}

				ActiveBuild.DurationLeft = (ActiveBuild.CompletionTimeUnixMs - nowMs) / 1000.0;

				// Loop in case the player minimized the mobile app long enough for multiple queued upgrades to finish!
				while (ActiveBuild != null && ActiveBuild.DurationLeft <= 0)
				{
					long finishedAtMs = ActiveBuild.CompletionTimeUnixMs;
					CompleteActiveBuild(saveImmediately: false);

					if (BuildQueue.Count > 0)
					{
						ActiveBuild = BuildQueue[0];
						BuildQueue.RemoveAt(0);
						ActiveBuild.CompletionTimeUnixMs = finishedAtMs + (long)(Math.Max(1.0, ActiveBuild.DurationLeft) * 1000.0);
						ActiveBuild.DurationLeft = (ActiveBuild.CompletionTimeUnixMs - nowMs) / 1000.0;
						GD.Print($"[QUEUE ENGINE] Auto-started next queued build: {ActiveBuild.BuildingName} Lvl {ActiveBuild.TargetLevel}");
					}
					UpdateEngineeringDockUI();
					TriggerInstantSave();
				}
			}
			else if (BuildQueue.Count > 0)
			{
				StartNextBuildInQueue();
			}

			// 2. Tick Simultaneous Orbital Shipyard Assembly Bays (Anchored to NextUnitDropUnixMs)
			TickSimultaneousUnitBays(realElapsedSec);

			// 2B. Tick Research Directorate Active Research Timer (Anchored to Absolute UTC Unix Seconds)
			TickResearchDirectorateTimers();

			// 3. Tick In-Flight Fleet Manifests & Incoming Radar Signals (Anchored to ArrivalTimeUnixMs)
			TickFleetMissions(realElapsedSec);

			if (_fleetActivityToastTimer > 0.0)
			{
				_fleetActivityToastTimer -= delta;
				if (_fleetActivityToastTimer <= 0.0 && _fleetActivityToastPanel != null)
				{
					_fleetActivityToastPanel.Visible = false;
				}
			}

			_incomingSignalPollTimer += realElapsedSec;
			if (_incomingSignalPollTimer >= 2.0)
			{
				_incomingSignalPollTimer = 0.0;
				PollIncomingHostileSignalsAsync();
			}

			// 4. Real-Time Resource Production Heartbeat (Counts exact UTC seconds even if mobile screen was minimized!)
			double resElapsedSec = (nowMs - _lastResourceTickUtcMs) / 1000.0;
			if (resElapsedSec >= 1.0)
			{
				_lastResourceTickUtcMs = nowMs;
				TickResourceHeartbeat(resElapsedSec);
			}

			// 5. 15-Second High-Frequency Auto-Save (Plus Instant Save on every click!)
			if (_autoSaveTimer >= 15.0)
			{
				TriggerInstantSave();
			}

			SyncSideDocksVisibilityForActiveModals();
			UpdateTimersDisplay();
		}

		private void TickSimultaneousUnitBays(double delta)
		{
			if (ActiveUnitBays.Count == 0) return;

			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			var completedKeys = new List<string>();
			bool anyUnitBorn = false;

			foreach (var kvp in ActiveUnitBays)
			{
				string uId = kvp.Key;
				var job = kvp.Value;
				if (job == null || job.RemainingCount <= 0)
				{
					completedKeys.Add(uId);
					continue;
				}

				double singleSec = Math.Max(1.0, job.SingleUnitDurationSec);
				if (job.NextUnitDropUnixMs <= 0)
				{
					job.NextUnitDropUnixMs = nowMs + (long)(Math.Max(0.1, job.NextUnitTimerSec) * 1000.0);
				}

				job.NextUnitTimerSec = (job.NextUnitDropUnixMs - nowMs) / 1000.0;
				while (job.NextUnitTimerSec <= 0 && job.RemainingCount > 0)
				{
					// 1 unit finishes and drops directly into the Garrison Armory!
					UnitInventory[uId] = UnitInventory.GetValueOrDefault(uId, 0) + 1;
					job.RemainingCount--;
					anyUnitBorn = true;

					if (job.RemainingCount > 0)
					{
						job.NextUnitDropUnixMs += (long)(singleSec * 1000.0);
						job.NextUnitTimerSec = (job.NextUnitDropUnixMs - nowMs) / 1000.0;
					}
					else
					{
						job.NextUnitTimerSec = 0;
						job.NextUnitDropUnixMs = 0;
						completedKeys.Add(uId);
					}
				}
			}

			foreach (string k in completedKeys)
			{
				ActiveUnitBays.Remove(k);
			}

			if (anyUnitBorn)
			{
				TriggerInstantSave();
			}
		}

		private void TickResearchDirectorateTimers()
		{
			if (BuildingLevels == null) return;
			int endSec = BuildingLevels.GetValueOrDefault(GameMath.ResearchDirectorateShieldTimerKey, 0);
			if (endSec <= 0) return;

			long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			if (nowSec >= endSec)
			{
				int curRshLvl = Math.Clamp(BuildingLevels.GetValueOrDefault(GameMath.ResearchDirectorateShieldKey, 0), 0, 20);
				int newRshLvl = Math.Clamp(curRshLvl + 1, 1, 20);
				BuildingLevels[GameMath.ResearchDirectorateShieldKey] = newRshLvl;
				BuildingLevels.Remove(GameMath.ResearchDirectorateShieldTimerKey);

				ShowFleetDockNotification(
					$"🔬 RESEARCH COMPLETE: Harmonic Shield Deflection Matrix reached Level {newRshLvl}/20 (+{newRshLvl}% Planetary Shield Defender Casualty Mitigation)!",
					new Color("#38BDF8")
				);
				TriggerInstantSave();
			}
		}

		private void TickFleetMissions(double delta)
		{
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

			// Tick any incoming signals on radar using absolute UTC arrival timestamps
			for (int i = SupabaseService.CachedIncomingSignals.Count - 1; i >= 0; i--)
			{
				var sig = SupabaseService.CachedIncomingSignals[i];
				if (sig.ArrivalTimeUnixMs > 0)
				{
					sig.EtaSecondsLeft = Math.Max(0, (sig.ArrivalTimeUnixMs - nowMs) / 1000.0);
				}
				else
				{
					sig.EtaSecondsLeft = Math.Max(0, sig.EtaSecondsLeft - delta);
					sig.ArrivalTimeUnixMs = nowMs + (long)(sig.EtaSecondsLeft * 1000.0);
				}

				if (sig.EtaSecondsLeft <= 0)
				{
					ApplyIncomingAttackToMyBase(sig);
					SupabaseService.CachedIncomingSignals.RemoveAt(i);
					UpdateFleetRadarDropdownUI();
				}
			}

			int flyingCount = 0;
			string primaryTarget = "NONE";
			double minEta = 0;
			string activeSrv = SupabaseService.CachedServerId;
			string homeCoord = $"{activeSrv}:{CurrentMoon}:{CurrentBaseSlot}";

			for (int i = SavedFleets.Count - 1; i >= 0; i--)
			{
				var fleet = SavedFleets[i];
				if (fleet == null || fleet.Status == "WATCHLIST_DATA")
				{
					SavedFleets.RemoveAt(i);
					continue;
				}
				if (fleet.Status == "IN_TRANSIT")
				{
					if (string.IsNullOrWhiteSpace(fleet.TargetCoord))
					{
						fleet.Status = "DOCKED";
						fleet.Protocol = "IDLE";
						fleet.LocationCoord = homeCoord;
						fleet.EtaSecondsLeft = 0;
						fleet.ArrivalTimeUnixMs = 0;
						TriggerInstantSave();
						continue;
					}
					if (fleet.ArrivalTimeUnixMs > 0)
					{
						fleet.EtaSecondsLeft = Math.Max(0, (fleet.ArrivalTimeUnixMs - nowMs) / 1000.0);
					}
					else
					{
						fleet.EtaSecondsLeft = Math.Max(0, fleet.EtaSecondsLeft - delta);
						fleet.ArrivalTimeUnixMs = nowMs + (long)(fleet.EtaSecondsLeft * 1000.0);
					}

					if (fleet.EtaSecondsLeft <= 0)
					{
						if (fleet.Protocol == "RETURN")
						{
							// Surviving fleet arrived back home! Deposit plunder into base storage silos
							long retE = fleet.Cargo.E, retI = fleet.Cargo.I, retT = fleet.Cargo.T, retH3 = fleet.Cargo.H3;
							ResE = Math.Min(SiloCap, ResE + retE);
							ResI = Math.Min(SiloCap, ResI + retI);
							ResT = Math.Min(SiloCap, ResT + retT);
							ResH3 = Math.Min(SiloCap, ResH3 + retH3);
							fleet.Cargo.E = fleet.Cargo.I = fleet.Cargo.T = fleet.Cargo.H3 = 0;

							fleet.Status = "DOCKED";
							fleet.Protocol = "IDLE";
							fleet.LocationCoord = homeCoord;
							fleet.TargetCoord = "";
							fleet.ArrivalTimeUnixMs = 0;
							SupabaseService.MarkLocalFleetMutated(fleet);
							ShowFleetDockNotification(
								$"⚓ FLEET RETURNED: [{fleet.Name}] docked at base! (+⚡{retE:N0} ⛏️{retI:N0} 💎{retT:N0} ⛽{retH3:N0})",
								new Color("#4ADE80")
							);
							SupabaseService.TriggerMobileAndWebAlert(
								$"⚓ {fleet.Name} Returned to Base",
								$"Delivered ⚡{retE:N0} Power, ⛏️{retI:N0} Iron, 💎{retT:N0} Titanium, ⛽{retH3:N0} Helium-3.",
								false
							);
							UpdateHUDDisplay();
							TriggerInstantSave();
							UpdateFleetRadarDropdownUI();
							MoonView.Instance?.RefreshMoonSlotVisuals();
						}
						else if (fleet.Protocol == "RAID")
						{
							if (!_resolvingFleetIds.Contains(fleet.Id))
							{
								_resolvingFleetIds.Add(fleet.Id);
								ResolveOutboundRaidArrivalAsync(fleet, homeCoord);
							}
						}
						else if (fleet.Protocol == "DROPOFF")
						{
							// Fleet arrived at one of the player's owned base coordinates to drop off units & cargo!
							string destCoord = fleet.TargetCoord;
							fleet.Status = "DOCKED";
							fleet.Protocol = "IDLE";
							fleet.LocationCoord = destCoord;
							fleet.TargetCoord = "";
							fleet.EtaSecondsLeft = 0;
							fleet.ArrivalTimeUnixMs = 0;
							BuildingInspectorModal.DropOffFleetUnitsToOwnedBase(fleet);
						}
						else
						{
							// MOVE (Station/Transfer between own bases or Vacant Slot Guard) or REINFORCE (Alliance Support):
							string destCoord = fleet.TargetCoord;
							GameMath.TryParseSectorCoordinate(destCoord, out int destMoon, out int destSlot);
							bool arrivedAtOwnedBase = BuildingInspectorModal.IsPlayerOwnedBaseCoordinate(destMoon, destSlot);
							string protoLabel = arrivedAtOwnedBase
								? "ARRIVED AT YOUR BASE"
								: (fleet.Protocol == "REINFORCE" ? "DEFENDING ALLY" : "STATIONED IN ORBIT");
							fleet.Status = arrivedAtOwnedBase ? "DOCKED" : "STATIONED";
							fleet.Protocol = arrivedAtOwnedBase ? "IDLE" : fleet.Protocol;
							fleet.LocationCoord = destCoord;
							fleet.TargetCoord = "";
							fleet.EtaSecondsLeft = 0;
							fleet.ArrivalTimeUnixMs = 0;
							SupabaseService.MarkLocalFleetMutated(fleet);
							ShowFleetDockNotification(
								$"🛰️ {protoLabel}: [{fleet.Name}] arrived at {destCoord}!{(arrivedAtOwnedBase ? " (Ready to Drop Off Units)" : "")}",
								new Color("#00F0FF")
							);
							SupabaseService.TriggerMobileAndWebAlert(
								$"🛰️ {fleet.Name} Arrived at {destCoord}",
								$"Fleet is now {protoLabel.ToLower()} at {destCoord}.",
								false
							);
							TriggerInstantSave();
							UpdateFleetRadarDropdownUI();
							MoonView.Instance?.RefreshMoonSlotVisuals();
						}
					}
					else
					{
						flyingCount++;
						if (primaryTarget == "NONE" || fleet.EtaSecondsLeft < minEta)
						{
							primaryTarget = $"{fleet.Protocol} ➔ {fleet.TargetCoord}";
							minEta = fleet.EtaSecondsLeft;
						}
					}
				}
			}

			ActiveFleetCount = flyingCount;
			ActiveFleetTarget = primaryTarget;
			ActiveFleetEta = minEta;
		}

		private async void ResolveOutboundRaidArrivalAsync(GameMath.FleetManifest fleet, string homeCoord)
		{
			try
			{
				long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				// Calculate how many seconds ago the raid actually impacted (in case the player was offline when it hit!)
				double overdueSeconds = (fleet.ArrivalTimeUnixMs > 0 && nowMs > fleet.ArrivalTimeUnixMs)
					? (nowMs - fleet.ArrivalTimeUnixMs) / 1000.0
					: 0.0;

				var parts = (fleet.TargetCoord ?? "S1:100:2").Split(':');
				int tMoon = parts.Length > 1 && int.TryParse(parts[1], out int pm) ? pm : 100;
				int tSlot = parts.Length > 2 && int.TryParse(parts[2], out int ps) ? ps : 2;

				if (SupabaseService.Instance != null)
				{
					var (survivors, loot, _) = await SupabaseService.Instance.ResolveFleetRaidAndSiegeAsync(fleet, tMoon, tSlot);
					fleet.Units = survivors;
					fleet.Cargo = loot;
				}

				int survivorTotal = 0;
				foreach (var kv in fleet.Units) survivorTotal += kv.Value;

				int returnSec = survivorTotal > 0
					? GameMath.CalcFleetTravelTimeSeconds(tMoon, tSlot, CurrentMoon, CurrentBaseSlot, fleet.Units, ServerSpeed)
					: 10;

				if (survivorTotal <= 0)
				{
					// Entire strike fleet was wiped out in combat
					ShowFleetDockNotification(
						$"💀 FLEET LOST: [{fleet.Name}] was destroyed in battle at M-{tMoon}:B-{tSlot:D2}!",
						new Color("#EF4444")
					);
					SavedFleets.Remove(fleet);
				}
				else
				{
					ShowFleetDockNotification(
						$"⚔️ BATTLE WON AT M-{tMoon}:B-{tSlot:D2}! [{fleet.Name}] returning with ⚡{fleet.Cargo.E:N0} ⛏️{fleet.Cargo.I:N0} 💎{fleet.Cargo.T:N0} ⛽{fleet.Cargo.H3:N0}!",
						new Color("#FBBF24")
					);
					// Subtract any offline time that already passed since the moment of impact from the return trip!
					double remReturnSec = Math.Max(0.0, returnSec - overdueSeconds);
					if (remReturnSec <= 0.0)
					{
						// Fleet impacted AND completed its return flight while the player was offline!
						ResE = Math.Min(SiloCap, ResE + fleet.Cargo.E);
						ResI = Math.Min(SiloCap, ResI + fleet.Cargo.I);
						ResT = Math.Min(SiloCap, ResT + fleet.Cargo.T);
						ResH3 = Math.Min(SiloCap, ResH3 + fleet.Cargo.H3);
						fleet.Cargo.E = fleet.Cargo.I = fleet.Cargo.T = fleet.Cargo.H3 = 0;

						fleet.Status = "DOCKED";
						fleet.Protocol = "IDLE";
						fleet.LocationCoord = homeCoord;
						fleet.TargetCoord = "";
						fleet.EtaSecondsLeft = 0;
						fleet.ArrivalTimeUnixMs = 0;
						SupabaseService.MarkLocalFleetMutated(fleet);
						UpdateHUDDisplay();
					}
					else
					{
						// Surviving units are flying back home with plundered resources
						long retStartMs = (fleet.ArrivalTimeUnixMs > 0) ? fleet.ArrivalTimeUnixMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
						fleet.Status = "IN_TRANSIT";
						fleet.Protocol = "RETURN";
						fleet.LocationCoord = $"{SupabaseService.CachedServerId}:{tMoon}:{tSlot}";
						fleet.TargetCoord = homeCoord;
						fleet.TotalFlightDurationSec = Math.Max(1.0, returnSec);
						fleet.DepartureTimeUnixMs = retStartMs;
						fleet.EtaSecondsLeft = remReturnSec;
						fleet.ArrivalTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)(remReturnSec * 1000.0);
						SupabaseService.MarkLocalFleetMutated(fleet);
					}
				}
			}
			finally
			{
				_resolvingFleetIds.Remove(fleet.Id);
				SupabaseService.MarkLocalFleetMutated();
				TriggerInstantSave();
				UpdateFleetRadarDropdownUI();
				MoonView.Instance?.NotifyExternalStateUpdated(refreshCloudOccupants: true);
			}
		}

		private async void ApplyIncomingAttackToMyBase(SupabaseService.IncomingFleetSignal sig)
		{
			if (sig.Protocol != "RAID") return;

			if (SupabaseService.Instance != null)
			{
				// Mutual Offline Battle Resolution: If the Attacker went offline after launching, resolve the battle on time right now!
				await SupabaseService.Instance.ResolveDefenderOfflineImpactAsync(sig);
			}

			SupabaseService.TriggerMobileAndWebAlert(
				"💥 BASE UNDER ATTACK — IMPACT!",
				$"Commander {sig.SenderName}'s fleet ({sig.TotalUnits} units) has struck your base! Check Inbox for Battle Report.",
				true
			);

			// Reload authoritative post-battle state from Supabase so unit casualties, resource raids, and Laser Cannon siege damage sync cleanly!
			LoadColonyDataFromCloud();
		}

		public void HandleTotalBaseLossAndServerRestart()
		{
			GD.PrintErr("[COLONY DESTROYED] All buildings reached 0% HP (Level 0)! Base is now unoccupied — restarting server colony.");
			// Reset base levels to fresh starter colony and return player to Server Select to restart
			BuildingLevels.Clear();
			BuildingLevels["hub_cmd"] = 1;
			BuildingLevels["hub_silo"] = 1;
			BuildingLevels["hub_mgd"] = 1;
			BuildingLevels["dist_e_0"] = 1;
			BuildingLevels["dist_i_0"] = 1;
			BuildingLevels["dist_t_0"] = 1;
			BuildingLevels["dist_h3_0"] = 1;
			UnitInventory.Clear();
			SavedFleets.Clear();
			ActiveUnitBays.Clear();
			ResE = 5000;
			ResI = 5000;
			ResT = 5000;
			ResH3 = 2500;
			TriggerInstantSave();
			string srvTarget = ResourceLoader.Exists("res://Scripts/ServerSelect.tscn") ? "res://Scripts/ServerSelect.tscn" : "res://Scenes/ServerSelect.tscn";
			GetTree().ChangeSceneToFile(srvTarget);
		}

		private bool _isPollingIncomingSignals = false;

		private async void PollIncomingHostileSignalsAsync()
		{
			if (_isPollingIncomingSignals || SupabaseService.Instance == null || string.IsNullOrEmpty(CommanderName)) return;
			_isPollingIncomingSignals = true;

			try
			{
				var msgs = await SupabaseService.Instance.FetchInboxMessagesAsync(CommanderName);
				int unreadTotal = 0;
				int unreadPersonal = 0;
				int unreadBattle = 0;
				string myCallsign = CommanderName.Trim();

				foreach (var m in msgs)
				{
					string cat = (m.category ?? "").ToUpper();
					string title = m.title ?? "";
					bool isToMe = string.Equals(m.recipient_name?.Trim(), myCallsign, StringComparison.OrdinalIgnoreCase);

					if (cat == "INTEL" && title.StartsWith("INCOMING_FLEET:", StringComparison.Ordinal))
					{
						// Parse incoming signal format: INCOMING_FLEET:SignalId|Sender|Origin|Protocol|TargetBld|TotalUnits|Lasers|EtaSec|ArrivalTimeUnixMs
						var parts = (m.body ?? "").Split('|');
						if (parts.Length >= 8)
						{
							string sigId = parts[0];
							if (!SupabaseService.CachedIncomingSignals.Exists(x => x.SignalId == sigId))
							{
								long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
								double rawEta = double.TryParse(parts[7], out double eta) ? eta : 180;
								long arrMs = (parts.Length >= 9 && long.TryParse(parts[8], out long parsedArr) && parsedArr > 0)
									? parsedArr
									: (nowMs + (long)(rawEta * 1000.0));
								long depMs = arrMs - (long)(Math.Max(10.0, rawEta) * 1000.0);
								double realEtaLeft = Math.Max(0.0, (arrMs - nowMs) / 1000.0);

								var newSig = new SupabaseService.IncomingFleetSignal
								{
									SignalId = sigId,
									FleetId = sigId,
									SenderName = parts[1],
									OriginCoord = parts[2],
									TargetCoord = $"{SupabaseService.CachedServerId}:{CurrentMoon}:{CurrentBaseSlot}",
									Protocol = parts[3],
									TargetBuildingName = parts[4],
									TotalUnits = int.TryParse(parts[5], out int tu) ? tu : 10,
									LaserCannonCount = int.TryParse(parts[6], out int lc) ? lc : 0,
									EtaSecondsLeft = realEtaLeft,
									TotalDurationSec = Math.Max(10.0, rawEta),
									DepartureTimeUnixMs = depMs,
									ArrivalTimeUnixMs = arrMs
								};

								if (realEtaLeft > 0.0)
								{
									SupabaseService.CachedIncomingSignals.Add(newSig);
									bool isAtk = (newSig.Protocol ?? "RAID").ToUpper() == "RAID";
									string alertHdr = isAtk
										? $"🚨 INCOMING ATTACK FROM {newSig.SenderName.ToUpper()}!"
										: (newSig.Protocol == "REINFORCE"
											? $"🛡️ ALLY REINFORCEMENTS FROM {newSig.SenderName.ToUpper()}"
											: $"🛰️ FLEET APPROACHING FROM {newSig.SenderName.ToUpper()}");
									string alertDesc = isAtk
										? $"{newSig.TotalUnits} enemy units ({newSig.LaserCannonCount} Laser Cannons targeting {newSig.TargetBuildingName}) — Impact in {GameMath.FormatTime((int)Math.Ceiling(realEtaLeft))}!"
										: $"{newSig.TotalUnits} units arriving at your orbit in {GameMath.FormatTime((int)Math.Ceiling(realEtaLeft))}.";
									SupabaseService.TriggerMobileAndWebAlert(alertHdr, alertDesc, isAtk);
									ShowFleetDockNotification(
										$"{alertHdr}  ·  {newSig.TotalUnits} Units from {newSig.OriginCoord} (ETA {GameMath.FormatTime((int)Math.Ceiling(realEtaLeft))})",
										isAtk ? new Color("#EF4444") : new Color("#38BDF8")
									);

									UpdateTimersDisplay();
									UpdateFleetRadarDropdownUI();
								}
								else if ((newSig.Protocol ?? "RAID").ToUpper() == "RAID")
								{
									// Both players were offline when the attack timer hit 00:00, and the Defender logged back in first!
									ApplyIncomingAttackToMyBase(newSig);
								}
							}
						}
						await SupabaseService.Instance.DeleteInboxMessageAsync(m.id);
					}
					else if (cat == "INTEL" && title.StartsWith("CANCEL_FLEET:", StringComparison.Ordinal))
					{
						string cancelId = (m.body ?? "").Trim();
						SupabaseService.CachedIncomingSignals.RemoveAll(x => x.SignalId == cancelId);
						UpdateTimersDisplay();
						UpdateFleetRadarDropdownUI();
						await SupabaseService.Instance.DeleteInboxMessageAsync(m.id);
					}
					else if (cat == "INTEL" && title.StartsWith("IMPACT_SYNC:", StringComparison.Ordinal))
					{
						// Enemy raid impacted our base in Supabase — immediately apply stolen resources & reload surviving units & building HP!
						var syncParts = (m.body ?? "").Split('|');
						if (syncParts.Length >= 4)
						{
							long.TryParse(syncParts[0], out long sE);
							long.TryParse(syncParts[1], out long sI);
							long.TryParse(syncParts[2], out long sT);
							long.TryParse(syncParts[3], out long sH3);
							ShowFleetDockNotification(
								$"🚨 BASE BREACHED! Enemy raid stole ⚡{sE:N0} ⛏️{sI:N0} 💎{sT:N0} ⛽{sH3:N0} — check Battle Logs!",
								new Color("#EF4444")
							);
						}
						await SupabaseService.Instance.DeleteInboxMessageAsync(m.id);
						SupabaseService.InvalidateColoniesCache();
						LoadColonyDataFromCloud();
					}
					else if (isToMe && !m.is_read && m.id > 0)
					{
						unreadTotal++;
						if (cat == "PERSONAL" || cat == "PRIVATE") unreadPersonal++;
						else if (cat == "BATTLE") unreadBattle++;
					}
				}
				UpdateInboxAlertBadges(unreadTotal, unreadPersonal, unreadBattle);
			}
			finally
			{
				_isPollingIncomingSignals = false;
			}
		}

		private void CompleteActiveBuild(bool saveImmediately = true)
		{
			if (ActiveBuild == null) return;

			string bId = ActiveBuild.BuildingId;
			int newLvl = ActiveBuild.TargetLevel;

			BuildingLevels[bId] = newLvl;
			BuildingLevels.Remove($"__hp_{bId}");
			if (bId == "hub_silo")
			{
				StorageSiloLevel = newLvl;
				RecalculateSiloCap();
			}
			else if (bId == "hub_rng")
			{
				SupabaseService.CachedRadarLevel = Math.Clamp(newLvl, 1, 10);
			}

			GD.Print($"[QUEUE ENGINE] Completed {ActiveBuild.BuildingName} Lvl {newLvl}!");
			EmitSignal(SignalName.QueuedBuildingCompleted, bId, newLvl);

			ActiveBuild = null;
			if (saveImmediately)
			{
				UpdateEngineeringDockUI();
				TriggerInstantSave();
			}
		}

		private void StartNextBuildInQueue()
		{
			if (BuildQueue.Count == 0) return;

			ActiveBuild = BuildQueue[0];
			BuildQueue.RemoveAt(0);
			ActiveBuild.CompletionTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)(Math.Max(1.0, ActiveBuild.DurationLeft) * 1000.0);

			GD.Print($"[QUEUE ENGINE] Starting next build: {ActiveBuild.BuildingName} Lvl {ActiveBuild.TargetLevel}");
			UpdateEngineeringDockUI();
			TriggerInstantSave();
		}

		public void CancelLastQueuedBuild()
		{
			if (BuildQueue.Count == 0) return;

			// LIFO: Pop the last item added to the queue
			int lastIndex = BuildQueue.Count - 1;
			var canceledItem = BuildQueue[lastIndex];
			BuildQueue.RemoveAt(lastIndex);

			// Refund 25 GGold and 50% Resources
			GGold += 25;
			ResE += canceledItem.CostE * 0.5;
			ResI += canceledItem.CostI * 0.5;
			ResT += canceledItem.CostT * 0.5;
			ResH3 += canceledItem.CostH3 * 0.5;

			GD.Print($"[QUEUE ENGINE] Canceled {canceledItem.BuildingName} Lvl {canceledItem.TargetLevel}. Refunded 25 GGold & 50% Resources.");

			UpdateHUDDisplay();
			UpdateEngineeringDockUI();
			TriggerInstantSave();
		}

		public void RecalculateSiloCap()
		{
			SiloCap = GameMath.CalcSiloCapacity(StorageSiloLevel);
			
			ResE = Math.Max(0.0, Math.Min(SiloCap, ResE));
			ResI = Math.Max(0.0, Math.Min(SiloCap, ResI));
			ResT = Math.Max(0.0, Math.Min(SiloCap, ResT));
			ResH3 = Math.Max(0.0, Math.Min(SiloCap, ResH3));
		}

		private void RecalculateLayoutBounds()
		{
			Vector2 vpSize = GetViewportRect().Size;
			Size = vpSize;

			if (_bottomPanelNode != null)
			{
				_bottomPanelNode.Position = new Vector2(0, vpSize.Y - 72);
				_bottomPanelNode.Size = new Vector2(vpSize.X, 72);
			}

			if (_cameraControlDock != null)
			{
				_cameraControlDock.Position = new Vector2(vpSize.X - 176, vpSize.Y - 128);
				_cameraControlDock.Size = new Vector2(164, 46);
			}

			LayoutSmartHangarModal();
		}

		public static string FormatH3Rate(double val, bool includePlus = true)
		{
			if (Math.Abs(val) < 0.0001) return includePlus ? "+0" : "0";
			string sign = val > 0 ? (includePlus ? "+" : "") : "-";
			double abs = Math.Abs(val);
			if (Math.Abs(abs - Math.Round(abs)) < 0.005)
			{
				return $"{sign}{Math.Round(abs):N0}";
			}
			return $"{sign}{abs:N2}".TrimEnd('0').TrimEnd('.');
		}

		public static string FormatH3Balance(double val)
		{
			long whole = (long)Math.Max(0.0, Math.Floor(val));
			return $"{whole:N0}";
		}

		public static long GetMineHourlyProduction(string buildingId, int level, Dictionary<string, GameMath.BoostData> activeBoosts, float serverSpeed, out long rawBaseYield, out double boostMultiplier)
		{
			rawBaseYield = GameMath.CalcSubMineYield(level);
			string id = (buildingId ?? "").ToLower();
			string resCode = "E";
			if (id.StartsWith("dist_i_")) resCode = "I";
			else if (id.StartsWith("dist_t_")) resCode = "T";
			else if (id.StartsWith("dist_h3_")) resCode = "H3";

			boostMultiplier = (activeBoosts != null && activeBoosts.TryGetValue(resCode, out var b) && b.Expiration > DateTimeOffset.UtcNow)
				? b.Multiplier
				: 1.0;

			// Base production per mine at its level (1871/h at Level 10), multiplied by active resource boost if any
			return (long)Math.Round(rawBaseYield * boostMultiplier);
		}

		public (
			long mineE0, long mineE1, long mineE2, double multE, long totalProdE,
			long mineI0, long mineI1, long mineI2, double multI, long totalProdI,
			long mineT0, long mineT1, long mineT2, double multT, long totalProdT,
			long mineH0, long mineH1, long mineH2, long mineH3, long mineH4, double multH3, double totalProdH3,
			double totalUnitH3Upkeep, double netH3PerHour, int totalBuiltUnits
		) GetSmartHangarTelemetry()
		{
			float spd = Math.Max(1.0f, ServerSpeed);

			// Self-heal & synchronize BuildingLevels from static session cache if any mine key is missing or higher in cache
			if (SupabaseService.CachedBuildingLevels != null)
			{
				string[] mineKeys = new string[]
				{
					"dist_e_0", "dist_e_1", "dist_e_2",
					"dist_i_0", "dist_i_1", "dist_i_2",
					"dist_t_0", "dist_t_1", "dist_t_2",
					"dist_h3_0", "dist_h3_1", "dist_h3_2", "dist_h3_3", "dist_h3_4"
				};
				foreach (string mk in mineKeys)
				{
					int cachedLvl = SupabaseService.CachedBuildingLevels.GetValueOrDefault(mk, 0);
					int localLvl = BuildingLevels.GetValueOrDefault(mk, 0);
					if (cachedLvl > localLvl)
					{
						BuildingLevels[mk] = cachedLvl;
					}
				}
			}

			double multE = (ActiveBoosts != null && ActiveBoosts.TryGetValue("E", out var bE) && bE.Expiration > DateTimeOffset.UtcNow) ? bE.Multiplier : 1.0;
			double multI = (ActiveBoosts != null && ActiveBoosts.TryGetValue("I", out var bI) && bI.Expiration > DateTimeOffset.UtcNow) ? bI.Multiplier : 1.0;
			double multT = (ActiveBoosts != null && ActiveBoosts.TryGetValue("T", out var bT) && bT.Expiration > DateTimeOffset.UtcNow) ? bT.Multiplier : 1.0;
			double multH3 = (ActiveBoosts != null && ActiveBoosts.TryGetValue("H3", out var bH) && bH.Expiration > DateTimeOffset.UtcNow) ? bH.Multiplier : 1.0;

			long e0 = GetMineHourlyProduction("dist_e_0", BuildingLevels.GetValueOrDefault("dist_e_0", 0), ActiveBoosts, spd, out _, out _);
			long e1 = GetMineHourlyProduction("dist_e_1", BuildingLevels.GetValueOrDefault("dist_e_1", 0), ActiveBoosts, spd, out _, out _);
			long e2 = GetMineHourlyProduction("dist_e_2", BuildingLevels.GetValueOrDefault("dist_e_2", 0), ActiveBoosts, spd, out _, out _);
			long totE = e0 + e1 + e2;

			long i0 = GetMineHourlyProduction("dist_i_0", BuildingLevels.GetValueOrDefault("dist_i_0", 0), ActiveBoosts, spd, out _, out _);
			long i1 = GetMineHourlyProduction("dist_i_1", BuildingLevels.GetValueOrDefault("dist_i_1", 0), ActiveBoosts, spd, out _, out _);
			long i2 = GetMineHourlyProduction("dist_i_2", BuildingLevels.GetValueOrDefault("dist_i_2", 0), ActiveBoosts, spd, out _, out _);
			long totI = i0 + i1 + i2;

			long t0 = GetMineHourlyProduction("dist_t_0", BuildingLevels.GetValueOrDefault("dist_t_0", 0), ActiveBoosts, spd, out _, out _);
			long t1 = GetMineHourlyProduction("dist_t_1", BuildingLevels.GetValueOrDefault("dist_t_1", 0), ActiveBoosts, spd, out _, out _);
			long t2 = GetMineHourlyProduction("dist_t_2", BuildingLevels.GetValueOrDefault("dist_t_2", 0), ActiveBoosts, spd, out _, out _);
			long totT = t0 + t1 + t2;

			long h0 = GetMineHourlyProduction("dist_h3_0", BuildingLevels.GetValueOrDefault("dist_h3_0", 0), ActiveBoosts, spd, out _, out _);
			long h1 = GetMineHourlyProduction("dist_h3_1", BuildingLevels.GetValueOrDefault("dist_h3_1", 0), ActiveBoosts, spd, out _, out _);
			long h2 = GetMineHourlyProduction("dist_h3_2", BuildingLevels.GetValueOrDefault("dist_h3_2", 0), ActiveBoosts, spd, out _, out _);
			long h3 = GetMineHourlyProduction("dist_h3_3", BuildingLevels.GetValueOrDefault("dist_h3_3", 0), ActiveBoosts, spd, out _, out _);
			long h4 = GetMineHourlyProduction("dist_h3_4", BuildingLevels.GetValueOrDefault("dist_h3_4", 0), ActiveBoosts, spd, out _, out _);
			double totH3 = (double)(h0 + h1 + h2 + h3 + h4);

			int totalUnits = 0;
			double upkeepH3 = 0.0;
			foreach (var u in GameData.Units)
			{
				int count = UnitInventory != null ? Math.Max(0, UnitInventory.GetValueOrDefault(u.Id, 0)) : 0;
				if (SavedFleets != null)
				{
					foreach (var f in SavedFleets)
					{
						if (f == null || f.Status == "WATCHLIST_DATA" || f.Units == null) continue;
						count += Math.Max(0, f.Units.GetValueOrDefault(u.Id, 0));
					}
				}
				totalUnits += count;
				upkeepH3 += (double)u.H3Drain * count;
			}

			double netH3 = totH3 - upkeepH3;
			return (
				e0, e1, e2, multE, totE,
				i0, i1, i2, multI, totI,
				t0, t1, t2, multT, totT,
				h0, h1, h2, h3, h4, multH3, totH3,
				upkeepH3, netH3, totalUnits
			);
		}

		private void TickResourceHeartbeat(double elapsedSeconds = 1.0)
		{
			double dtSec = Math.Max(0.1, elapsedSeconds);
			RecalculateSiloCap();

			// 1. Cleanup Expired Boosts
			var now = DateTimeOffset.UtcNow;
			var expiredKeys = new List<string>();
			foreach (var kvp in ActiveBoosts)
			{
				if (kvp.Value.Expiration <= now) expiredKeys.Add(kvp.Key);
			}
			foreach (var key in expiredKeys)
			{
				ActiveBoosts.Remove(key);
				GD.Print($"[BASE HUD] Boost for {key} expired and was removed.");
			}

			// 2. Calculate Live Hourly Production & Unit Fuel Consumption (Supports exact decimal H3 upkeep!)
			var tel = GetSmartHangarTelemetry();

			// 3. Convert to Elapsed Real-Time Ticks (Handles both 1s heartbeat AND mobile background resume!)
			double pE = (tel.totalProdE / 3600.0) * dtSec;
			double pI = (tel.totalProdI / 3600.0) * dtSec;
			double pT = (tel.totalProdT / 3600.0) * dtSec;
			double pH3 = (tel.netH3PerHour / 3600.0) * dtSec;

			ResE = Math.Max(0.0, Math.Min(SiloCap, ResE + pE));
			ResI = Math.Max(0.0, Math.Min(SiloCap, ResI + pI));
			ResT = Math.Max(0.0, Math.Min(SiloCap, ResT + pT));

			// Helium-3 Rule:
			// - Subtracts Unit Consumption from Mine Production (supports decimal rates & decimal balance down to 0.00).
			// - Player's Current Helium-3 can NEVER go below 0.00!
			// - Once Helium-3 hits 0.00 and Net Production is negative, H3 consumption stops draining below 0.00,
			//   and Units start dying every 10 minutes (600s) until either the player raids/drops off Helium-3 above 0.00
			//   or Net Helium-3 Production recovers to >= +0/h!
			double prevH3 = ResH3;
			double nextH3 = ResH3 + pH3;
			if (nextH3 <= 0.0)
			{
				// Calculate how much of dtSec was spent AFTER hitting 0.00
				double secAtZero = dtSec;
				if (prevH3 > 0.0 && pH3 < 0.0)
				{
					double fracToZero = Math.Clamp(prevH3 / Math.Abs(pH3), 0.0, 1.0);
					secAtZero = dtSec * (1.0 - fracToZero);
				}
				ResH3 = 0.0;

				if (tel.netH3PerHour < 0.0 && tel.totalBuiltUnits > 0)
				{
					_h3StarvationTimerSec += secAtZero;

					while (_h3StarvationTimerSec >= 600.0)
					{
						_h3StarvationTimerSec -= 600.0;
						var checkTel = GetSmartHangarTelemetry();
						if (checkTel.netH3PerHour >= 0.0 || ResH3 > 0.0 || checkTel.totalBuiltUnits <= 0)
						{
							_h3StarvationTimerSec = 0.0;
							break;
						}

						int unitsLost = ChronoEngine.ApplyOneH3StarvationCycle(UnitInventory, SavedFleets, BuildingLevels);
						if (unitsLost > 0)
						{
							SupabaseService.MarkLocalFleetMutated();
							var afterTel = GetSmartHangarTelemetry();
							string recoveryNote = afterTel.netH3PerHour >= 0.0
								? $"Helium-3 balance restored ({FormatH3Rate(afterTel.netH3PerHour)}/h)! Remaining units are now safe."
								: $"Still at {FormatH3Rate(afterTel.netH3PerHour)}/h! Upgrade H3 Mines, transfer troops, or Raid Helium-3 before the next 10m cycle!";
							ShowFleetDockNotification(
								$"⚠️ NO HELIUM-3 FUEL (0.00 H3): {unitsLost:N0} Units died from fuel starvation! {recoveryNote}",
								new Color("#EF4444"),
								11.0
							);
							TriggerInstantSave();
						}
						else
						{
							break;
						}
					}
				}
				else
				{
					_h3StarvationTimerSec = 0.0;
				}
			}
			else
			{
				ResH3 = Math.Max(0.0, Math.Min(SiloCap, nextH3));
				// Reset the 10-minute starvation countdown immediately when H3 > 0.00 (e.g. from a Raid) or Net Production >= 0!
				_h3StarvationTimerSec = 0.0;
			}

			UpdateHUDDisplay();

			if (_smartHangarOverlay != null && _smartHangarOverlay.Visible)
			{
				_smartHangarLiveRefreshTimer += dtSec;
				if (_smartHangarLiveRefreshTimer >= 1.0)
				{
					_smartHangarLiveRefreshTimer = 0.0;
					RefreshSmartHangarContent();
				}
			}
		}

		public void UpdateHUDDisplay()
		{
			RecalculateSiloCap();
			ResH3 = Math.Max(0.0, ResH3);

			if (_lblCmdName != null) _lblCmdName.Text = CommanderName;
			if (_btnColonySwitch != null) _btnColonySwitch.Text = $"{SupabaseService.CachedServerId} ({ServerSpeed:0.#}x) · M:{CurrentMoon} ; B:{CurrentBaseSlot} ({CurrentBaseName}) ▼";

			if (_lblValE != null) _lblValE.Text = $"{(long)ResE:N0}";
			if (_lblValI != null) _lblValI.Text = $"{(long)ResI:N0}";
			if (_lblValT != null) _lblValT.Text = $"{(long)ResT:N0}";
			if (_lblValH3 != null) _lblValH3.Text = FormatH3Balance(ResH3);
			if (_lblValGGold != null) _lblValGGold.Text = $"{GGold:N0}";

			var tel = GetSmartHangarTelemetry();
			if (_lblRateE != null)
			{
				_lblRateE.Text = $"+{tel.totalProdE:N0}/h";
				_lblRateE.Modulate = new Color("#F0ABFC");
			}
			if (_lblRateI != null)
			{
				_lblRateI.Text = $"+{tel.totalProdI:N0}/h";
				_lblRateI.Modulate = new Color("#67E8F9");
			}
			if (_lblRateT != null)
			{
				_lblRateT.Text = $"+{tel.totalProdT:N0}/h";
				_lblRateT.Modulate = new Color("#CBD5E1");
			}
			if (_lblRateH3 != null)
			{
				if (tel.netH3PerHour < 0.0)
				{
					_lblRateH3.Text = ResH3 <= 0.0 && tel.totalBuiltUnits > 0
						? $"{FormatH3Rate(tel.netH3PerHour)}/h ⚠️"
						: $"{FormatH3Rate(tel.netH3PerHour)}/h";
					_lblRateH3.Modulate = new Color("#F87171");
					if (_capStyleH3 != null) _capStyleH3.BorderColor = new Color("#EF4444");
				}
				else
				{
					_lblRateH3.Text = $"{FormatH3Rate(tel.netH3PerHour)}/h";
					_lblRateH3.Modulate = new Color("#FDE047");
					if (_capStyleH3 != null) _capStyleH3.BorderColor = new Color(0.92f, 0.70f, 0.03f, 0.75f);
				}
			}
			if (_lblRateGGold != null)
			{
				_lblRateGGold.Text = "GET GOLD";
				_lblRateGGold.Modulate = new Color("#4ADE80");
			}
		}

		public double GetQueuedOnlyDurationSeconds()
		{
			double sum = 0.0;
			for (int i = 0; i < BuildQueue.Count; i++)
			{
				sum += Math.Max(0.0, BuildQueue[i].DurationLeft);
			}
			return sum;
		}

		public double GetTotalPipelineDurationSeconds()
		{
			double activeRem = ActiveBuild != null ? Math.Max(0.0, ActiveBuild.DurationLeft) : 0.0;
			return activeRem + GetQueuedOnlyDurationSeconds();
		}

		private void UpdateTimersDisplay()
		{
			if (_lblBuildName != null) 
				_lblBuildName.Text = ActiveBuild != null ? $"{ActiveBuild.BuildingName} ➔ Lvl {ActiveBuild.TargetLevel}" : "SYSTEMS IDLE";
			
			if (_lblBuildTimer != null) 
				_lblBuildTimer.Text = ActiveBuild != null ? FormatTime(ActiveBuild.DurationLeft) : "READY";

			double queuedSeconds = GetQueuedOnlyDurationSeconds();
			double totalSeconds = GetTotalPipelineDurationSeconds();

			if (_lblTotalQueueTime != null)
			{
				if (ActiveBuild != null || BuildQueue.Count > 0)
				{
					_lblTotalQueueTime.Text = $"TOTAL: {FormatTime(totalSeconds)}";
					_lblTotalQueueTime.Modulate = new Color("#00F0FF");
				}
				else
				{
					_lblTotalQueueTime.Text = "TOTAL: 0s";
					_lblTotalQueueTime.Modulate = new Color("#94A3B8");
				}
			}

			if (_btnQueueToggle != null)
			{
				if (BuildQueue.Count > 0)
				{
					string arrow = _isQueueDropdownOpen ? "▲" : "▼";
					_btnQueueToggle.Text = $"{arrow} QUEUED ({BuildQueue.Count}) · +{FormatTime(queuedSeconds)}";
					_btnQueueToggle.Modulate = new Color("#38BDF8");
				}
				else
				{
					_btnQueueToggle.Text = "QUEUE EMPTY (0/20)";
					_btnQueueToggle.Modulate = new Color("#94A3B8");
				}
			}

			int inboundTotalCount = SupabaseService.CachedIncomingSignals.Count;
			int inboundHostileCount = 0;
			foreach (var sig in SupabaseService.CachedIncomingSignals)
			{
				if ((sig.Protocol ?? "RAID").ToUpper() == "RAID") inboundHostileCount++;
			}
			int stationedCount = 0;
			foreach (var f in SavedFleets)
			{
				if (f.Status == "STATIONED") stationedCount++;
			}

			if (_lblFleetThreatAlert != null)
			{
				if (inboundHostileCount > 0)
				{
					_lblFleetThreatAlert.Text = $"🚨 {inboundHostileCount} INCOMING ATTACK!";
					_lblFleetThreatAlert.Modulate = new Color("#EF4444");
					if (_fleetDockStyle != null) _fleetDockStyle.BorderColor = new Color("#EF4444");
				}
				else if (inboundTotalCount > 0)
				{
					_lblFleetThreatAlert.Text = $"🛰️ {inboundTotalCount} INCOMING FLEET";
					_lblFleetThreatAlert.Modulate = new Color("#38BDF8");
					if (_fleetDockStyle != null) _fleetDockStyle.BorderColor = new Color("#38BDF8");
				}
				else if (ActiveFleetCount > 0)
				{
					_lblFleetThreatAlert.Text = $"🚀 {ActiveFleetCount} IN FLIGHT ({stationedCount} ORBIT)";
					_lblFleetThreatAlert.Modulate = new Color("#00F0FF");
					if (_fleetDockStyle != null) _fleetDockStyle.BorderColor = new Color("#00F0FF");
				}
				else if (stationedCount > 0)
				{
					_lblFleetThreatAlert.Text = $"🛰️ {stationedCount} STATIONED IN ORBIT";
					_lblFleetThreatAlert.Modulate = new Color("#4ADE80");
					if (_fleetDockStyle != null) _fleetDockStyle.BorderColor = new Color("#4ADE80");
				}
				else
				{
					_lblFleetThreatAlert.Text = "ALL FLEETS AT BASE";
					_lblFleetThreatAlert.Modulate = new Color("#94A3B8");
					if (_fleetDockStyle != null) _fleetDockStyle.BorderColor = new Color("#3B82F6");
				}
			}

			if (_lblFleetStatus != null)
			{
				if (inboundTotalCount > 0)
				{
					var firstSig = SupabaseService.CachedIncomingSignals[0];
					bool isAtk = (firstSig.Protocol ?? "RAID").ToUpper() == "RAID";
					string verb = isAtk ? "Attacking" : (firstSig.Protocol == "REINFORCE" ? "Reinforcing" : "Moving to");
					_lblFleetStatus.Text = isAtk
						? $"⚠️ {firstSig.SenderName} ({firstSig.TotalUnits} Units)"
						: $"🛰️ {firstSig.SenderName} ({verb} · {firstSig.TotalUnits} Units)";
					_lblFleetStatus.Modulate = isAtk ? new Color("#EF4444") : new Color("#38BDF8");
				}
				else if (ActiveFleetCount > 0)
				{
					_lblFleetStatus.Text = $"🚀 {ActiveFleetTarget}";
					_lblFleetStatus.Modulate = new Color("#00F0FF");
				}
				else
				{
					_lblFleetStatus.Text = "FLEETS READY";
					_lblFleetStatus.Modulate = new Color("#94A3B8");
				}
			}

			if (_lblFleetEta != null)
			{
				if (inboundTotalCount > 0)
				{
					var firstSig = SupabaseService.CachedIncomingSignals[0];
					bool isAtk = (firstSig.Protocol ?? "RAID").ToUpper() == "RAID";
					_lblFleetEta.Text = $"{(isAtk ? "IMPACT" : "ARRIVAL")}: {FormatTime(firstSig.EtaSecondsLeft)}";
					_lblFleetEta.Modulate = isAtk ? new Color("#EF4444") : new Color("#38BDF8");
				}
				else if (ActiveFleetEta > 0)
				{
					_lblFleetEta.Text = $"ETA: {FormatTime(ActiveFleetEta)}";
					_lblFleetEta.Modulate = new Color("#4ADE80");
				}
				else
				{
					_lblFleetEta.Text = "AT BASE";
					_lblFleetEta.Modulate = new Color("#4ADE80");
				}
			}

			if (_btnFleetDropdownToggle != null)
			{
				string arrow = _isFleetDropdownOpen ? "▲" : "▼";
				_btnFleetDropdownToggle.Text = $"{arrow} FLEET TRACKER ({inboundTotalCount} IN · {ActiveFleetCount} FLYING · {stationedCount} GUARDING)";
			}

			if (_isFleetDropdownOpen)
			{
				UpdateFleetRadarDropdownUI();
			}
		}

		public void UpdateFleetRadarDropdownUI()
		{
			if (_fleetDropdownPanel == null || _fleetDropdownList == null) return;
			_fleetDropdownPanel.Visible = _isFleetDropdownOpen;
			if (!_isFleetDropdownOpen) return;

			foreach (Node c in _fleetDropdownList.GetChildren()) c.QueueFree();

			// Layer 1: Incoming Fleets (Attacks, Reinforcements & Moves)
			Label hdrIn = new Label
			{
				Text = "🚨 INCOMING FLEETS TO YOUR BASE",
				Modulate = new Color("#EF4444"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			hdrIn.AddThemeFontSizeOverride("font_size", 13);
			_fleetDropdownList.AddChild(hdrIn);

			if (SupabaseService.CachedIncomingSignals.Count == 0)
			{
				Label clearLbl = new Label
				{
					Text = "  ● No incoming fleets heading toward your base.",
					Modulate = new Color("#94A3B8"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				clearLbl.AddThemeFontSizeOverride("font_size", 13);
				_fleetDropdownList.AddChild(clearLbl);
			}
			else
			{
				foreach (var sig in SupabaseService.CachedIncomingSignals)
				{
					bool isAtk = (sig.Protocol ?? "RAID").ToUpper() == "RAID";
					string actionLabel = isAtk ? "ATTACKING" : (sig.Protocol == "REINFORCE" ? "DEFENDING (ALLY)" : "MOVING TO ORBIT");
					string laserDetail = (isAtk && sig.LaserCannonCount > 0) ? $" · {sig.LaserCannonCount} Laser Cannons ➔ {sig.TargetBuildingName}" : "";
					Label sigLbl = new Label
					{
						Text = $"  {(isAtk ? "⚠️" : "🛰️")} {sig.SenderName} [{actionLabel}] — {sig.TotalUnits} Units{laserDetail}\n     From: {sig.OriginCoord}  |  Arrival: {FormatTime(sig.EtaSecondsLeft)}",
						Modulate = isAtk ? new Color("#FCA5A5") : new Color("#7DD3FC"),
						AutowrapMode = TextServer.AutowrapMode.WordSmart
					};
					sigLbl.AddThemeFontSizeOverride("font_size", 13);
					_fleetDropdownList.AddChild(sigLbl);
				}
			}

			// Layer 2: Outbound & Returning Fleets
			Label hdrOut = new Label
			{
				Text = "🚀 LAYER 2: YOUR FLEETS IN FLIGHT",
				Modulate = new Color("#00F0FF"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			hdrOut.AddThemeFontSizeOverride("font_size", 13);
			_fleetDropdownList.AddChild(hdrOut);

			int flightShown = 0;
			foreach (var f in SavedFleets)
			{
				if (f.Status != "IN_TRANSIT") continue;
				flightShown++;
				string dir = f.Protocol == "RETURN" ? "↩ RETURNING HOME" : $"➔ {f.Protocol} TO {f.TargetCoord}";
				Label fLbl = new Label
				{
					Text = $"  ● {f.Name}: {dir} ({FormatTime(f.EtaSecondsLeft)})",
					Modulate = f.Protocol == "RETURN" ? new Color("#FBBF24") : Colors.White,
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				fLbl.AddThemeFontSizeOverride("font_size", 13);
				_fleetDropdownList.AddChild(fLbl);
			}
			if (flightShown == 0)
			{
				Label noneOut = new Label
				{
					Text = "  ● No fleets currently in deep space transit.",
					Modulate = new Color("#94A3B8"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				noneOut.AddThemeFontSizeOverride("font_size", 13);
				_fleetDropdownList.AddChild(noneOut);
			}

			// Layer 3: Stationed Orbiting Fleets (Traps & Reinforcements)
			Label hdrStat = new Label
			{
				Text = "🛰️ LAYER 3: FLEETS GUARDING IN ORBIT",
				Modulate = new Color("#4ADE80"),
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			hdrStat.AddThemeFontSizeOverride("font_size", 13);
			_fleetDropdownList.AddChild(hdrStat);

			int statShown = 0;
			foreach (var f in SavedFleets)
			{
				if (f.Status != "STATIONED") continue;
				statShown++;
				Label sLbl = new Label
				{
					Text = $"  ● {f.Name}: Stationed at {f.LocationCoord} [{f.Protocol}]",
					Modulate = new Color("#4ADE80"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				sLbl.AddThemeFontSizeOverride("font_size", 13);
				_fleetDropdownList.AddChild(sLbl);
			}
			if (statShown == 0)
			{
				Label noneStat = new Label
				{
					Text = "  ● No remote fleets stationed in orbit.",
					Modulate = new Color("#94A3B8"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				noneStat.AddThemeFontSizeOverride("font_size", 13);
				_fleetDropdownList.AddChild(noneStat);
			}
		}

		public void UpdateEngineeringDockUI()
		{
			double queuedSeconds = GetQueuedOnlyDurationSeconds();
			double totalSeconds = GetTotalPipelineDurationSeconds();

			if (_btnQueueToggle != null)
			{
				if (BuildQueue.Count > 0)
				{
					string arrow = _isQueueDropdownOpen ? "▲" : "▼";
					_btnQueueToggle.Text = $"{arrow} QUEUED ({BuildQueue.Count}) · +{FormatTime(queuedSeconds)}";
					_btnQueueToggle.Modulate = new Color("#38BDF8");
				}
				else
				{
					_btnQueueToggle.Text = "QUEUE EMPTY (0/20)";
					_btnQueueToggle.Modulate = new Color("#94A3B8");
				}
				_btnQueueToggle.Disabled = BuildQueue.Count == 0;
				if (BuildQueue.Count == 0) _isQueueDropdownOpen = false;
			}

			if (_lblTotalQueueTime != null)
			{
				if (ActiveBuild != null || BuildQueue.Count > 0)
				{
					_lblTotalQueueTime.Text = $"TOTAL: {FormatTime(totalSeconds)}";
					_lblTotalQueueTime.Modulate = new Color("#00F0FF");
				}
				else
				{
					_lblTotalQueueTime.Text = "TOTAL: 0s";
					_lblTotalQueueTime.Modulate = new Color("#94A3B8");
				}
			}

			if (_queueDropdownPanel != null)
			{
				_queueDropdownPanel.Visible = _isQueueDropdownOpen;

				if (_isQueueDropdownOpen)
				{
					foreach (Node child in _queueListContainer.GetChildren()) child.QueueFree();

					Label summaryLbl = new Label
					{
						Text = $"PENDING QUEUE: +{FormatTime(queuedSeconds)}  |  ALL: {FormatTime(totalSeconds)}",
						Modulate = new Color("#FBBF24")
					};
					summaryLbl.AddThemeFontSizeOverride("font_size", 13);
					summaryLbl.AddThemeColorOverride("font_outline_color", Colors.Black);
					summaryLbl.AddThemeConstantOverride("outline_size", 2);
					_queueListContainer.AddChild(summaryLbl);

					for (int i = 0; i < BuildQueue.Count; i++)
					{
						var item = BuildQueue[i];
						Label lbl = new Label
						{
							Text = $"{i + 1}. {item.BuildingName} Lvl {item.TargetLevel}  ({FormatTime(item.DurationLeft)})",
							Modulate = Colors.White
						};
						lbl.AddThemeFontSizeOverride("font_size", 14);
						lbl.AddThemeColorOverride("font_outline_color", Colors.Black);
						lbl.AddThemeConstantOverride("outline_size", 2);
						_queueListContainer.AddChild(lbl);
					}

					_btnCancelLast.Visible = BuildQueue.Count > 0;
				}
			}
		}

		// =========================================================================
		// 1. TOP COCKPIT BAR (Screen Top)
		// =========================================================================
		private void BuildTopCockpitBar()
		{
			Panel topPanel = new Panel();
			topPanel.SetAnchorsPreset(LayoutPreset.TopWide);
			topPanel.CustomMinimumSize = new Vector2(0, 68);
			topPanel.OffsetTop = 0;
			topPanel.OffsetBottom = 68;
			topPanel.MouseFilter = MouseFilterEnum.Ignore;

			StyleBoxFlat topPanelStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.03f, 0.05f, 0.09f, 0.96f),
				BorderColor = new Color(0f, 0.94f, 1f, 0.50f),
				BorderWidthBottom = 2
			};
			topPanel.AddThemeStyleboxOverride("panel", topPanelStyle);
			AddChild(topPanel);

			HBoxContainer masterBar = new HBoxContainer();
			masterBar.SetAnchorsPreset(LayoutPreset.FullRect);
			masterBar.OffsetLeft = 12;
			masterBar.OffsetRight = -12;
			masterBar.OffsetTop = 6;
			masterBar.OffsetBottom = -6;
			masterBar.MouseFilter = MouseFilterEnum.Ignore;
			masterBar.Alignment = BoxContainer.AlignmentMode.Center;
			masterBar.AddThemeConstantOverride("separation", 10);
			topPanel.AddChild(masterBar);

			// Profile Section
			HBoxContainer profileSection = new HBoxContainer();
			profileSection.MouseFilter = MouseFilterEnum.Ignore;
			profileSection.AddThemeConstantOverride("separation", 10);

			Button avatarBtn = new Button
			{
				CustomMinimumSize = new Vector2(52, 52),
				MouseFilter = MouseFilterEnum.Stop,
				TooltipText = "Commander Profile Dossier"
			};
			avatarBtn.Pressed += () => EmitSignal(SignalName.AvatarClicked);

			StyleBoxFlat avatarStyle = new StyleBoxFlat
			{
				BgColor = new Color("#090E17"),
				BorderColor = new Color("#00F0FF"),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 26,
				CornerRadiusTopRight = 26,
				CornerRadiusBottomLeft = 26,
				CornerRadiusBottomRight = 26
			};
			avatarBtn.AddThemeStyleboxOverride("normal", avatarStyle);

			Label avatarIco = new Label
			{
				Text = "👨‍🚀",
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
			avatarIco.SetAnchorsPreset(LayoutPreset.FullRect);
			avatarIco.AddThemeFontSizeOverride("font_size", 24);
			avatarBtn.AddChild(avatarIco);
			profileSection.AddChild(avatarBtn);

			VBoxContainer identityStack = new VBoxContainer();
			identityStack.Alignment = BoxContainer.AlignmentMode.Center;
			identityStack.MouseFilter = MouseFilterEnum.Ignore;
			identityStack.AddThemeConstantOverride("separation", 2);

			_lblCmdName = new Label { Text = CommanderName, Modulate = Colors.White };
			_lblCmdName.AddThemeFontSizeOverride("font_size", 16);
			_lblCmdName.AddThemeColorOverride("font_outline_color", Colors.Black);
			_lblCmdName.AddThemeConstantOverride("outline_size", 2);
			identityStack.AddChild(_lblCmdName);

			_btnColonySwitch = new Button
			{
				Text = $"M:{CurrentMoon} ; B:{CurrentBaseSlot} ({CurrentBaseName}) ▼",
				CustomMinimumSize = new Vector2(200, 26),
				MouseFilter = MouseFilterEnum.Stop
			};
			_btnColonySwitch.AddThemeFontSizeOverride("font_size", 13);
			_btnColonySwitch.Modulate = new Color("#4ADE80");
			_btnColonySwitch.Pressed += () => EmitSignal(SignalName.ColonySwitcherClicked);

			StyleBoxFlat switchStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.02f, 0.06f, 0.10f, 0.92f),
				BorderColor = new Color(0.13f, 0.77f, 0.36f, 0.55f),
				BorderWidthLeft = 1,
				BorderWidthRight = 1,
				BorderWidthTop = 1,
				BorderWidthBottom = 1,
				CornerRadiusTopLeft = 4,
				CornerRadiusTopRight = 4,
				CornerRadiusBottomLeft = 4,
				CornerRadiusBottomRight = 4
			};
			_btnColonySwitch.AddThemeStyleboxOverride("normal", switchStyle);
			identityStack.AddChild(_btnColonySwitch);

			profileSection.AddChild(identityStack);
			masterBar.AddChild(profileSection);

			Control spacerLeft = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
			masterBar.AddChild(spacerLeft);

			// Resource Capsules (Smart Hangar Buttons)
			HBoxContainer resourceArray = new HBoxContainer();
			resourceArray.MouseFilter = MouseFilterEnum.Ignore;
			resourceArray.AddThemeConstantOverride("separation", 8);
			resourceArray.Alignment = BoxContainer.AlignmentMode.Center;

			resourceArray.AddChild(CreateResourceCapsule("⚡", "E", new Color("#D946EF"), out _lblValE, out _lblRateE, out _capStyleE));
			resourceArray.AddChild(CreateResourceCapsule("⛏", "I", new Color("#06B6D4"), out _lblValI, out _lblRateI, out _capStyleI));
			resourceArray.AddChild(CreateResourceCapsule("💎", "T", new Color("#94A3B8"), out _lblValT, out _lblRateT, out _capStyleT));
			resourceArray.AddChild(CreateResourceCapsule("⛽", "H3", new Color("#EAB308"), out _lblValH3, out _lblRateH3, out _capStyleH3));
			resourceArray.AddChild(CreateGGoldCapsule("🪙", out _lblValGGold, out _lblRateGGold));

			masterBar.AddChild(resourceArray);

			Control spacerRight = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
			masterBar.AddChild(spacerRight);

			// Layout Editor Toggle Button
			_btnEditLayoutToggle = new Button
			{
				Text = "⚙ EDIT BASE",
				CustomMinimumSize = new Vector2(128, 48),
				MouseFilter = MouseFilterEnum.Stop,
				TooltipText = "Toggle Base Layout Editor Mode"
			};
			_btnEditLayoutToggle.AddThemeFontSizeOverride("font_size", 14);
			_btnEditLayoutToggle.AddThemeColorOverride("font_outline_color", Colors.Black);
			_btnEditLayoutToggle.AddThemeConstantOverride("outline_size", 2);
			_btnEditLayoutToggle.Pressed += ToggleEditBaseLayout;

			StyleBoxFlat editStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.04f, 0.08f, 0.14f, 0.95f),
				BorderColor = new Color("#00F0FF"),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6
			};
			_btnEditLayoutToggle.AddThemeStyleboxOverride("normal", editStyle);
			masterBar.AddChild(_btnEditLayoutToggle);

			// Rank Badge
			Button rankBtn = new Button
			{
				CustomMinimumSize = new Vector2(102, 48),
				MouseFilter = MouseFilterEnum.Stop,
				TooltipText = "Galactic Leaderboard"
			};
			rankBtn.Pressed += () => EmitSignal(SignalName.LeaderboardClicked);

			StyleBoxFlat rankStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.08f, 0.06f, 0.02f, 0.95f),
				BorderColor = new Color("#FBBF24"),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6
			};
			rankBtn.AddThemeStyleboxOverride("normal", rankStyle);

			VBoxContainer rankContent = new VBoxContainer();
			rankContent.SetAnchorsPreset(LayoutPreset.FullRect);
			rankContent.Alignment = BoxContainer.AlignmentMode.Center;
			rankContent.MouseFilter = MouseFilterEnum.Ignore;
			rankContent.AddThemeConstantOverride("separation", -1);

			Label rankIco = new Label { Text = "🏆 RANK", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color("#FBBF24") };
			rankIco.AddThemeFontSizeOverride("font_size", 12);
			rankIco.AddThemeColorOverride("font_outline_color", Colors.Black);
			rankIco.AddThemeConstantOverride("outline_size", 2);

			Label rankVal = new Label { Text = "#1 PRIME", HorizontalAlignment = HorizontalAlignment.Center, Modulate = Colors.White };
			rankVal.AddThemeFontSizeOverride("font_size", 13);
			rankVal.AddThemeColorOverride("font_outline_color", Colors.Black);
			rankVal.AddThemeConstantOverride("outline_size", 2);

			rankContent.AddChild(rankIco);
			rankContent.AddChild(rankVal);
			rankBtn.AddChild(rankContent);
			masterBar.AddChild(rankBtn);
		}

		private void ToggleEditBaseLayout()
		{
			IsEditModeActive = !IsEditModeActive;

			if (IsEditModeActive)
			{
				_btnEditLayoutToggle.Text = "✓ SAVE BASE";
				_btnEditLayoutToggle.Modulate = new Color("#22C55E");
			}
			else
			{
				_btnEditLayoutToggle.Text = "⚙ EDIT BASE";
				_btnEditLayoutToggle.Modulate = Colors.White;
				EmitSignal(SignalName.SaveLayoutRequested);
			}

			EmitSignal(SignalName.EditModeToggled, IsEditModeActive);
			GD.Print($"[BASE HUD] Layout Edit Mode: {IsEditModeActive}");
		}

		// =========================================================================
		// 2. TACTICAL SIDE DOCKS & QUEUE DROPDOWN (Auto-Sizing PanelContainers — Zero Text Overlap!)
		// =========================================================================
		private void BuildTacticalSideDocks()
		{
			// Build Dock (Top-Right) — Auto-Sizing Engineering Console
			_buildDockPanel = new PanelContainer();
			PanelContainer buildDock = _buildDockPanel;
			buildDock.SetAnchorsPreset(LayoutPreset.TopRight);
			buildDock.CustomMinimumSize = new Vector2(316, 106);
			buildDock.OffsetLeft = -328;
			buildDock.OffsetRight = -12;
			buildDock.OffsetTop = 76;
			buildDock.OffsetBottom = 182;
			buildDock.MouseFilter = MouseFilterEnum.Ignore;

			StyleBoxFlat dockStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.03f, 0.05f, 0.09f, 0.95f),
				BorderColor = new Color("#F59E0B"),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 8,
				CornerRadiusTopRight = 8,
				CornerRadiusBottomLeft = 8,
				CornerRadiusBottomRight = 8,
				ContentMarginLeft = 12,
				ContentMarginRight = 12,
				ContentMarginTop = 8,
				ContentMarginBottom = 8,
				ShadowColor = new Color(0f, 0f, 0f, 0.6f),
				ShadowSize = 8
			};
			buildDock.AddThemeStyleboxOverride("panel", dockStyle);
			AddChild(buildDock);

			VBoxContainer buildBox = new VBoxContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Ignore
			};
			buildBox.AddThemeConstantOverride("separation", 5);

			// Row 1: Header + Total Pending Queue Time
			HBoxContainer headerRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			headerRow.AddThemeConstantOverride("separation", 8);
			Label dockTitle = new Label
			{
				Text = "⚙ ENGINEERING BAY",
				Modulate = new Color("#F59E0B"),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			dockTitle.AddThemeFontSizeOverride("font_size", 13);
			dockTitle.AddThemeColorOverride("font_outline_color", Colors.Black);
			dockTitle.AddThemeConstantOverride("outline_size", 2);

			_lblTotalQueueTime = new Label
			{
				Text = "TOTAL: 0s",
				Modulate = new Color("#94A3B8"),
				HorizontalAlignment = HorizontalAlignment.Right
			};
			_lblTotalQueueTime.AddThemeFontSizeOverride("font_size", 13);
			_lblTotalQueueTime.AddThemeColorOverride("font_outline_color", Colors.Black);
			_lblTotalQueueTime.AddThemeConstantOverride("outline_size", 2);

			headerRow.AddChild(dockTitle);
			headerRow.AddChild(_lblTotalQueueTime);
			buildBox.AddChild(headerRow);

			// Row 2: Active Construction Name & Active Countdown
			HBoxContainer activeRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			activeRow.AddThemeConstantOverride("separation", 8);
			_lblBuildName = new Label
			{
				Text = "SYSTEMS IDLE",
				Modulate = Colors.White,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			_lblBuildName.AddThemeFontSizeOverride("font_size", 14);
			_lblBuildName.AddThemeColorOverride("font_outline_color", Colors.Black);
			_lblBuildName.AddThemeConstantOverride("outline_size", 2);

			_lblBuildTimer = new Label
			{
				Text = "READY",
				Modulate = new Color("#22C55E"),
				HorizontalAlignment = HorizontalAlignment.Right
			};
			_lblBuildTimer.AddThemeFontSizeOverride("font_size", 14);
			_lblBuildTimer.AddThemeColorOverride("font_outline_color", Colors.Black);
			_lblBuildTimer.AddThemeConstantOverride("outline_size", 2);

			activeRow.AddChild(_lblBuildName);
			activeRow.AddChild(_lblBuildTimer);
			buildBox.AddChild(activeRow);

			// Row 3: Queue Toggle Button (Full Width)
			HBoxContainer subRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			subRow.AddThemeConstantOverride("separation", 6);
			
			_btnQueueToggle = new Button
			{
				Text = "QUEUE EMPTY (0/20)",
				CustomMinimumSize = new Vector2(0, 32),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Stop,
				Disabled = true,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			_btnQueueToggle.AddThemeFontSizeOverride("font_size", 13);
			_btnQueueToggle.Modulate = new Color("#94A3B8");
			_btnQueueToggle.Pressed += () => 
			{
				_isQueueDropdownOpen = !_isQueueDropdownOpen;
				UpdateEngineeringDockUI();
			};

			subRow.AddChild(_btnQueueToggle);
			buildBox.AddChild(subRow);
			buildDock.AddChild(buildBox);

			// Floating Queue Dropdown Panel
			_queueDropdownPanel = new PanelContainer();
			_queueDropdownPanel.SetAnchorsPreset(LayoutPreset.TopRight);
			_queueDropdownPanel.CustomMinimumSize = new Vector2(316, 186);
			_queueDropdownPanel.OffsetLeft = -328;
			_queueDropdownPanel.OffsetRight = -12;
			_queueDropdownPanel.OffsetTop = 186; 
			_queueDropdownPanel.OffsetBottom = 372;
			_queueDropdownPanel.Visible = false;

			StyleBoxFlat dropStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.02f, 0.04f, 0.07f, 0.98f),
				BorderColor = new Color("#F59E0B"),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 8,
				CornerRadiusBottomRight = 8,
				ContentMarginLeft = 12,
				ContentMarginRight = 12,
				ContentMarginTop = 8,
				ContentMarginBottom = 8
			};
			_queueDropdownPanel.AddThemeStyleboxOverride("panel", dropStyle);
			AddChild(_queueDropdownPanel);

			VBoxContainer dropBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
			dropBox.AddThemeConstantOverride("separation", 6);
			_queueDropdownPanel.AddChild(dropBox);

			ScrollContainer scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
			_queueListContainer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
			_queueListContainer.AddThemeConstantOverride("separation", 4);
			scroll.AddChild(_queueListContainer);
			dropBox.AddChild(scroll);

			_btnCancelLast = new Button
			{
				Text = "✕ CANCEL LAST (+25 GGOLD & 50% RES)",
				CustomMinimumSize = new Vector2(0, 36)
			};
			_btnCancelLast.AddThemeFontSizeOverride("font_size", 13);
			_btnCancelLast.Modulate = new Color("#EF4444");
			_btnCancelLast.Pressed += CancelLastQueuedBuild;
			dropBox.AddChild(_btnCancelLast);

			// Fleet & Incoming Threat Radar Dock (Top-Left) — Spacious Multi-Layered Tactical Console
			_fleetDockPanel = new PanelContainer();
			_fleetDockPanel.SetAnchorsPreset(LayoutPreset.TopLeft);
			_fleetDockPanel.CustomMinimumSize = new Vector2(336, 106);
			_fleetDockPanel.OffsetLeft = 12;
			_fleetDockPanel.OffsetRight = 348;
			_fleetDockPanel.OffsetTop = 76;
			_fleetDockPanel.OffsetBottom = 182;
			_fleetDockPanel.MouseFilter = MouseFilterEnum.Ignore;

			_fleetDockStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.03f, 0.05f, 0.09f, 0.95f),
				BorderColor = new Color("#3B82F6"),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 8,
				CornerRadiusTopRight = 8,
				CornerRadiusBottomLeft = 8,
				CornerRadiusBottomRight = 8,
				ContentMarginLeft = 12,
				ContentMarginRight = 12,
				ContentMarginTop = 8,
				ContentMarginBottom = 8,
				ShadowColor = new Color(0f, 0f, 0f, 0.6f),
				ShadowSize = 8
			};
			_fleetDockPanel.AddThemeStyleboxOverride("panel", _fleetDockStyle);
			AddChild(_fleetDockPanel);

			VBoxContainer fleetBox = new VBoxContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Ignore
			};
			fleetBox.AddThemeConstantOverride("separation", 5);

			// Row 1: Radar Header + Incoming Hostile Threat Status
			HBoxContainer fHeaderRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			fHeaderRow.AddThemeConstantOverride("separation", 8);
			Label fDockTitle = new Label
			{
				Text = "📡 FLEET & RADAR",
				Modulate = new Color("#38BDF8"),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			fDockTitle.AddThemeFontSizeOverride("font_size", 13);
			fDockTitle.AddThemeColorOverride("font_outline_color", Colors.Black);
			fDockTitle.AddThemeConstantOverride("outline_size", 2);

			_lblFleetThreatAlert = new Label
			{
				Text = "SECTOR CLEAR",
				Modulate = new Color("#4ADE80"),
				HorizontalAlignment = HorizontalAlignment.Right
			};
			_lblFleetThreatAlert.AddThemeFontSizeOverride("font_size", 12);
			_lblFleetThreatAlert.AddThemeColorOverride("font_outline_color", Colors.Black);
			_lblFleetThreatAlert.AddThemeConstantOverride("outline_size", 2);

			fHeaderRow.AddChild(fDockTitle);
			fHeaderRow.AddChild(_lblFleetThreatAlert);
			fleetBox.AddChild(fHeaderRow);

			// Row 2: Primary Active Fleet Movement + Live Countdown
			HBoxContainer fActiveRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			fActiveRow.AddThemeConstantOverride("separation", 8);
			_lblFleetStatus = new Label
			{
				Text = "FLEETS STANDING BY",
				Modulate = Colors.White,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			_lblFleetStatus.AddThemeFontSizeOverride("font_size", 13);
			_lblFleetStatus.AddThemeColorOverride("font_outline_color", Colors.Black);
			_lblFleetStatus.AddThemeConstantOverride("outline_size", 2);

			_lblFleetEta = new Label
			{
				Text = "DOCKED",
				Modulate = new Color("#4ADE80"),
				HorizontalAlignment = HorizontalAlignment.Right
			};
			_lblFleetEta.AddThemeFontSizeOverride("font_size", 13);
			_lblFleetEta.AddThemeColorOverride("font_outline_color", Colors.Black);
			_lblFleetEta.AddThemeConstantOverride("outline_size", 2);

			fActiveRow.AddChild(_lblFleetStatus);
			fActiveRow.AddChild(_lblFleetEta);
			fleetBox.AddChild(fActiveRow);

			// Row 3: Expandable Multi-Layer Fleet & Radar Signal Dropdown Button + Battle Logs Shortcut
			HBoxContainer fSubRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			fSubRow.AddThemeConstantOverride("separation", 6);

			_btnFleetDropdownToggle = new Button
			{
				Text = "▼ RADAR (0 IN · 0 FLY · 0 ORBIT)",
				CustomMinimumSize = new Vector2(0, 32),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Stop,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			_btnFleetDropdownToggle.AddThemeFontSizeOverride("font_size", 12);
			_btnFleetDropdownToggle.Modulate = new Color("#38BDF8");
			_btnFleetDropdownToggle.Pressed += () =>
			{
				_isFleetDropdownOpen = !_isFleetDropdownOpen;
				UpdateFleetRadarDropdownUI();
			};

			_btnBattleLogsShortcut = new Button
			{
				Text = "⚔️ LOGS",
				CustomMinimumSize = new Vector2(82, 32),
				MouseFilter = MouseFilterEnum.Stop
			};
			_btnBattleLogsShortcut.AddThemeFontSizeOverride("font_size", 12);
			_btnBattleLogsShortcut.Modulate = new Color("#FBBF24");
			_btnBattleLogsShortcut.Pressed += () => OpenCommsInboxModal("BATTLE");

			fSubRow.AddChild(_btnFleetDropdownToggle);
			fSubRow.AddChild(_btnBattleLogsShortcut);
			fleetBox.AddChild(fSubRow);
			_fleetDockPanel.AddChild(fleetBox);

			// Live Fleet Activity Notification Banner (Flashes right below the Top-Left Fleet Dock whenever ANY fleet launches, arrives, or approaches!)
			_fleetActivityToastPanel = new PanelContainer();
			_fleetActivityToastPanel.SetAnchorsPreset(LayoutPreset.TopLeft);
			_fleetActivityToastPanel.CustomMinimumSize = new Vector2(336, 42);
			_fleetActivityToastPanel.OffsetLeft = 12;
			_fleetActivityToastPanel.OffsetRight = 348;
			_fleetActivityToastPanel.OffsetTop = 186;
			_fleetActivityToastPanel.OffsetBottom = 228;
			_fleetActivityToastPanel.Visible = false;
			_fleetActivityToastPanel.MouseFilter = MouseFilterEnum.Stop;

			_fleetActivityToastStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.03f, 0.06f, 0.12f, 0.97f),
				BorderColor = new Color("#00F0FF"),
				BorderWidthLeft = 3,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6,
				ContentMarginLeft = 10,
				ContentMarginRight = 10,
				ContentMarginTop = 6,
				ContentMarginBottom = 6
			};
			_fleetActivityToastPanel.AddThemeStyleboxOverride("panel", _fleetActivityToastStyle);

			_lblFleetActivityToast = new Label
			{
				Text = "🚀 FLEET ACTIVITY SIGNAL",
				Modulate = Colors.White,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_lblFleetActivityToast.AddThemeFontSizeOverride("font_size", 13);
			_lblFleetActivityToast.AddThemeColorOverride("font_outline_color", Colors.Black);
			_lblFleetActivityToast.AddThemeConstantOverride("outline_size", 2);
			_fleetActivityToastPanel.AddChild(_lblFleetActivityToast);
			AddChild(_fleetActivityToastPanel);

			// Multi-Layered Expandable Fleet & Threat Radar Dropdown Panel (Below Top-Left Fleet Dock)
			_fleetDropdownPanel = new PanelContainer();
			_fleetDropdownPanel.SetAnchorsPreset(LayoutPreset.TopLeft);
			_fleetDropdownPanel.CustomMinimumSize = new Vector2(336, 218);
			_fleetDropdownPanel.OffsetLeft = 12;
			_fleetDropdownPanel.OffsetRight = 348;
			_fleetDropdownPanel.OffsetTop = 186;
			_fleetDropdownPanel.OffsetBottom = 404;
			_fleetDropdownPanel.Visible = false;

			StyleBoxFlat fDropStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.02f, 0.04f, 0.08f, 0.98f),
				BorderColor = new Color("#3B82F6"),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 8,
				CornerRadiusBottomRight = 8,
				ContentMarginLeft = 12,
				ContentMarginRight = 12,
				ContentMarginTop = 8,
				ContentMarginBottom = 8
			};
			_fleetDropdownPanel.AddThemeStyleboxOverride("panel", fDropStyle);
			AddChild(_fleetDropdownPanel);

			VBoxContainer fDropBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
			fDropBox.AddThemeConstantOverride("separation", 6);
			_fleetDropdownPanel.AddChild(fDropBox);

			ScrollContainer fScroll = new ScrollContainer
			{
				SizeFlagsVertical = SizeFlags.ExpandFill,
				HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
			};
			_fleetDropdownList = new VBoxContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};
			_fleetDropdownList.AddThemeConstantOverride("separation", 6);
			fScroll.AddChild(_fleetDropdownList);
			fDropBox.AddChild(fScroll);
		}

		// =========================================================================
		// 2B. TACTICAL CAMERA ZOOM & RECENTER DOCK (Mobile + PC One-Tap Controls)
		// =========================================================================
		private void BuildTacticalCameraControlDock()
		{
			Vector2 vpSize = GetViewportRect().Size;

			_cameraControlDock = new Panel
			{
				Name = "TacticalCameraControlDock",
				Position = new Vector2(vpSize.X - 176, vpSize.Y - 128),
				Size = new Vector2(164, 46),
				CustomMinimumSize = new Vector2(164, 46),
				MouseFilter = MouseFilterEnum.Stop
			};

			StyleBoxFlat camDockStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.03f, 0.05f, 0.09f, 0.94f),
				BorderColor = new Color(0f, 0.94f, 1f, 0.65f),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 23,
				CornerRadiusTopRight = 23,
				CornerRadiusBottomLeft = 23,
				CornerRadiusBottomRight = 23
			};
			_cameraControlDock.AddThemeStyleboxOverride("panel", camDockStyle);
			AddChild(_cameraControlDock);

			HBoxContainer camRow = new HBoxContainer();
			camRow.SetAnchorsPreset(LayoutPreset.FullRect);
			camRow.OffsetLeft = 8;
			camRow.OffsetRight = -8;
			camRow.OffsetTop = 5;
			camRow.OffsetBottom = -5;
			camRow.Alignment = BoxContainer.AlignmentMode.Center;
			camRow.AddThemeConstantOverride("separation", 6);
			_cameraControlDock.AddChild(camRow);

			Button btnZoomOut = CreateCameraControlButton(" - ", "Zoom Out", () => EmitSignal(SignalName.CameraZoomRequested, 0.85f));
			Button btnCenter = CreateCameraControlButton(" ⌖ ", "Recenter Camera", () => EmitSignal(SignalName.CameraResetRequested));
			Button btnZoomIn = CreateCameraControlButton(" + ", "Zoom In", () => EmitSignal(SignalName.CameraZoomRequested, 1.18f));

			camRow.AddChild(btnZoomOut);
			camRow.AddChild(btnCenter);
			camRow.AddChild(btnZoomIn);
		}

		private Button CreateCameraControlButton(string symbol, string tooltip, Action onPress)
		{
			Button btn = new Button
			{
				Text = symbol,
				CustomMinimumSize = new Vector2(44, 34),
				MouseFilter = MouseFilterEnum.Stop,
				TooltipText = tooltip
			};
			btn.AddThemeFontSizeOverride("font_size", 16);
			btn.Modulate = new Color("#00F0FF");

			StyleBoxFlat btnStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.06f, 0.11f, 0.18f, 0.96f),
				BorderColor = new Color(0f, 0.94f, 1f, 0.45f),
				BorderWidthLeft = 1,
				BorderWidthRight = 1,
				BorderWidthTop = 1,
				BorderWidthBottom = 1,
				CornerRadiusTopLeft = 15,
				CornerRadiusTopRight = 15,
				CornerRadiusBottomLeft = 15,
				CornerRadiusBottomRight = 15
			};
			btn.AddThemeStyleboxOverride("normal", btnStyle);
			btn.Pressed += onPress;
			return btn;
		}

		// =========================================================================
		// 3. BOTTOM COMMAND NAVIGATION DOCK (Absolute Viewport Anchor)
		// =========================================================================
		private void BuildBottomCommandNavigationDock()
		{
			Vector2 vpSize = GetViewportRect().Size;

			_bottomPanelNode = new Panel();
			_bottomPanelNode.Name = "BottomNavigationDockPanel";
			_bottomPanelNode.Position = new Vector2(0, vpSize.Y - 72);
			_bottomPanelNode.Size = new Vector2(vpSize.X, 72);
			_bottomPanelNode.CustomMinimumSize = new Vector2(0, 72);
			_bottomPanelNode.MouseFilter = MouseFilterEnum.Ignore;

			StyleBoxFlat bStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.03f, 0.05f, 0.09f, 0.97f),
				BorderColor = new Color(0f, 0.94f, 1f, 0.6f),
				BorderWidthTop = 2
			};
			_bottomPanelNode.AddThemeStyleboxOverride("panel", bStyle);
			AddChild(_bottomPanelNode);

			HBoxContainer navRow = new HBoxContainer();
			navRow.SetAnchorsPreset(LayoutPreset.FullRect);
			navRow.OffsetLeft = 14;
			navRow.OffsetRight = -14;
			navRow.OffsetTop = 7;
			navRow.OffsetBottom = -7;
			navRow.MouseFilter = MouseFilterEnum.Ignore;
			navRow.Alignment = BoxContainer.AlignmentMode.Center;
			navRow.AddThemeConstantOverride("separation", 12);
			_bottomPanelNode.AddChild(navRow);

			navRow.AddChild(CreateNavTab("🌕", "BASE", "BASE"));
			navRow.AddChild(CreateNavTab("🪐", "MOON SLOTS", "MOON"));
			navRow.AddChild(CreateNavTab("🌌", "GALAXY MAP", "GALAXY"));
			navRow.AddChild(CreateNavTab("🛡️", "ALLIANCE", "ALLIANCE"));
			navRow.AddChild(CreateNavTab("✉️", "COMMS", "COMMS"));
			navRow.AddChild(CreateNavTab("💬", "GLOBAL CHAT", "CHAT"));
		}

		private Button CreateNavTab(string icon, string title, string targetTier)
		{
			Button tabBtn = new Button
			{
				CustomMinimumSize = new Vector2(138, 56),
				MouseFilter = MouseFilterEnum.Stop
			};

			StyleBoxFlat tabStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.06f, 0.10f, 0.16f, 0.95f),
				BorderColor = new Color(0f, 0.94f, 1f, 0.7f),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 8,
				CornerRadiusTopRight = 8,
				CornerRadiusBottomLeft = 8,
				CornerRadiusBottomRight = 8
			};
			tabBtn.AddThemeStyleboxOverride("normal", tabStyle);

			VBoxContainer stack = new VBoxContainer();
			stack.SetAnchorsPreset(LayoutPreset.FullRect);
			stack.Alignment = BoxContainer.AlignmentMode.Center;
			stack.MouseFilter = MouseFilterEnum.Ignore;
			stack.AddThemeConstantOverride("separation", 0);

			Label ico = new Label { Text = icon, HorizontalAlignment = HorizontalAlignment.Center };
			ico.AddThemeFontSizeOverride("font_size", 18);

			Label lbl = new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color("#00F0FF") };
			lbl.AddThemeFontSizeOverride("font_size", 14);
			lbl.AddThemeColorOverride("font_outline_color", Colors.Black);
			lbl.AddThemeConstantOverride("outline_size", 2);

			stack.AddChild(ico);
			stack.AddChild(lbl);
			tabBtn.AddChild(stack);

			if (targetTier == "COMMS")
			{
				_commsAlertBadge = new PanelContainer
				{
					Visible = UnreadInboxCount > 0,
					MouseFilter = MouseFilterEnum.Ignore
				};
				_commsAlertBadge.SetAnchorsPreset(LayoutPreset.TopRight);
				_commsAlertBadge.OffsetLeft = -46;
				_commsAlertBadge.OffsetRight = -4;
				_commsAlertBadge.OffsetTop = 3;
				_commsAlertBadge.OffsetBottom = 21;

				StyleBoxFlat alertStyle = new StyleBoxFlat
				{
					BgColor = new Color(0.88f, 0.11f, 0.18f, 0.98f),
					BorderColor = new Color("#FEF08A"),
					BorderWidthLeft = 1,
					BorderWidthRight = 1,
					BorderWidthTop = 1,
					BorderWidthBottom = 1,
					CornerRadiusTopLeft = 9,
					CornerRadiusTopRight = 9,
					CornerRadiusBottomLeft = 9,
					CornerRadiusBottomRight = 9,
					ContentMarginLeft = 5,
					ContentMarginRight = 5,
					ContentMarginTop = 1,
					ContentMarginBottom = 1
				};
				_commsAlertBadge.AddThemeStyleboxOverride("panel", alertStyle);

				_lblCommsAlertCount = new Label
				{
					Text = UnreadInboxCount > 0 ? $" 🔴 {UnreadInboxCount} " : " 🔴 1 ",
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center,
					Modulate = Colors.White,
					MouseFilter = MouseFilterEnum.Ignore
				};
				_lblCommsAlertCount.AddThemeFontSizeOverride("font_size", 10);
				_lblCommsAlertCount.AddThemeColorOverride("font_outline_color", Colors.Black);
				_lblCommsAlertCount.AddThemeConstantOverride("outline_size", 2);
				_commsAlertBadge.AddChild(_lblCommsAlertCount);
				tabBtn.AddChild(_commsAlertBadge);
			}

			tabBtn.Pressed += () => OnNavTabPressed(targetTier);
			return tabBtn;
		}

		public void ShowFleetDockNotification(string message, Color accentColor, double durationSec = 9.0)
		{
			if (_fleetActivityToastPanel == null || _lblFleetActivityToast == null) return;
			_lblFleetActivityToast.Text = message;
			_lblFleetActivityToast.Modulate = accentColor;
			if (_fleetActivityToastStyle != null)
			{
				_fleetActivityToastStyle.BorderColor = accentColor;
			}
			_fleetActivityToastPanel.Visible = true;
			_fleetActivityToastTimer = durationSec;
		}

		private void OnNavTabPressed(string targetTier)
		{
			GD.Print($"[BASE HUD] Navigation Tab Clicked: {targetTier}");
			EmitSignal(SignalName.NavTierSelected, targetTier);

			if (targetTier == "BASE")
			{
				string target = ResourceLoader.Exists("res://Scripts/base_view.tscn") ? "res://Scripts/base_view.tscn" : "res://Scenes/base_view.tscn";
				GetTree().ChangeSceneToFile(target);
			}
			else if (targetTier == "MOON")
			{
				MoonView.SelectedMoonToView = CurrentMoon;
				string target = ResourceLoader.Exists("res://Scripts/MoonView.tscn") ? "res://Scripts/MoonView.tscn" : "res://Scenes/MoonView.tscn";
				GetTree().ChangeSceneToFile(target);
			}
			else if (targetTier == "GALAXY")
			{
				string target = ResourceLoader.Exists("res://Scripts/GalaxyView.tscn") ? "res://Scripts/GalaxyView.tscn" : "res://Scenes/GalaxyView.tscn";
				GetTree().ChangeSceneToFile(target);
			}
			else if (targetTier == "ALLIANCE")
			{
				OpenAllianceHQModal();
			}
			else if (targetTier == "COMMS")
			{
				OpenCommsInboxModal("PERSONAL");
			}
			else if (targetTier == "CHAT")
			{
				OpenGlobalChatModal();
			}
		}

		private Button CreateResourceCapsule(string icon, string resCode, Color color, out Label valLabel, out Label rateLabel, out StyleBoxFlat capStyle)
		{
			string fullName = resCode switch
			{
				"E" => "Power (Click to view Power Mines A, B, C production)",
				"I" => "Iron (Click to view Iron Mines A, B, C production)",
				"T" => "Titanium (Click to view Titanium Mines A, B, C production)",
				"H3" => "Helium-3 (Click to view H3 Mines A–E production & Unit Fuel Upkeep)",
				_ => $"{resCode} Resources"
			};

			Button capsuleBtn = new Button
			{
				CustomMinimumSize = new Vector2(124, 50),
				MouseFilter = MouseFilterEnum.Stop,
				TooltipText = fullName
			};
			capsuleBtn.Pressed += () => EmitSignal(SignalName.ResourceClicked, resCode);

			capStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.04f, 0.06f, 0.10f, 0.96f),
				BorderColor = new Color(color.R, color.G, color.B, 0.78f),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 10,
				CornerRadiusTopRight = 10,
				CornerRadiusBottomLeft = 10,
				CornerRadiusBottomRight = 10
			};
			StyleBoxFlat hoverStyle = (StyleBoxFlat)capStyle.Duplicate();
			hoverStyle.BgColor = new Color(0.08f, 0.12f, 0.20f, 0.98f);
			hoverStyle.BorderColor = Colors.White;

			capsuleBtn.AddThemeStyleboxOverride("normal", capStyle);
			capsuleBtn.AddThemeStyleboxOverride("hover", hoverStyle);
			capsuleBtn.AddThemeStyleboxOverride("pressed", hoverStyle);

			VBoxContainer stack = new VBoxContainer();
			stack.SetAnchorsPreset(LayoutPreset.FullRect);
			stack.Alignment = BoxContainer.AlignmentMode.Center;
			stack.MouseFilter = MouseFilterEnum.Ignore;
			stack.AddThemeConstantOverride("separation", -2);

			HBoxContainer topRow = new HBoxContainer();
			topRow.Alignment = BoxContainer.AlignmentMode.Center;
			topRow.MouseFilter = MouseFilterEnum.Ignore;
			topRow.AddThemeConstantOverride("separation", 5);

			Label ico = new Label { Text = icon, Modulate = color };
			ico.AddThemeFontSizeOverride("font_size", 15);

			valLabel = new Label { Text = "0", Modulate = Colors.White };
			valLabel.AddThemeFontSizeOverride("font_size", 14);
			valLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
			valLabel.AddThemeConstantOverride("outline_size", 2);

			topRow.AddChild(ico);
			topRow.AddChild(valLabel);

			rateLabel = new Label
			{
				Text = "+0/h",
				HorizontalAlignment = HorizontalAlignment.Center,
				Modulate = color.Lerp(Colors.White, 0.35f),
				MouseFilter = MouseFilterEnum.Ignore
			};
			rateLabel.AddThemeFontSizeOverride("font_size", 12);
			rateLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
			rateLabel.AddThemeConstantOverride("outline_size", 2);

			stack.AddChild(topRow);
			stack.AddChild(rateLabel);
			capsuleBtn.AddChild(stack);
			return capsuleBtn;
		}

		private Button CreateGGoldCapsule(string icon, out Label valLabel, out Label rateLabel)
		{
			Button capsuleBtn = new Button
			{
				CustomMinimumSize = new Vector2(124, 50),
				MouseFilter = MouseFilterEnum.Stop,
				TooltipText = "Galaxy Gold Store (Click to get GGold Packages & Boosts)"
			};
			capsuleBtn.Pressed += () => EmitSignal(SignalName.ResourceClicked, "GGD");

			StyleBoxFlat goldStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.10f, 0.07f, 0.02f, 0.96f),
				BorderColor = new Color("#FBBF24"),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 10,
				CornerRadiusTopRight = 10,
				CornerRadiusBottomLeft = 10,
				CornerRadiusBottomRight = 10
			};
			StyleBoxFlat hoverStyle = (StyleBoxFlat)goldStyle.Duplicate();
			hoverStyle.BgColor = new Color(0.18f, 0.13f, 0.04f, 0.98f);
			hoverStyle.BorderColor = Colors.White;

			capsuleBtn.AddThemeStyleboxOverride("normal", goldStyle);
			capsuleBtn.AddThemeStyleboxOverride("hover", hoverStyle);
			capsuleBtn.AddThemeStyleboxOverride("pressed", hoverStyle);

			VBoxContainer stack = new VBoxContainer();
			stack.SetAnchorsPreset(LayoutPreset.FullRect);
			stack.Alignment = BoxContainer.AlignmentMode.Center;
			stack.MouseFilter = MouseFilterEnum.Ignore;
			stack.AddThemeConstantOverride("separation", -2);

			HBoxContainer row = new HBoxContainer();
			row.Alignment = BoxContainer.AlignmentMode.Center;
			row.MouseFilter = MouseFilterEnum.Ignore;
			row.AddThemeConstantOverride("separation", 4);

			Label ico = new Label { Text = icon };
			ico.AddThemeFontSizeOverride("font_size", 15);

			valLabel = new Label { Text = "0", Modulate = new Color("#FBBF24") };
			valLabel.AddThemeFontSizeOverride("font_size", 14);
			valLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
			valLabel.AddThemeConstantOverride("outline_size", 2);

			Label plusIco = new Label { Text = "+", Modulate = new Color("#22C55E") };
			plusIco.AddThemeFontSizeOverride("font_size", 15);
			plusIco.AddThemeColorOverride("font_outline_color", Colors.Black);
			plusIco.AddThemeConstantOverride("outline_size", 2);

			row.AddChild(ico);
			row.AddChild(valLabel);
			row.AddChild(plusIco);

			rateLabel = new Label
			{
				Text = "GET GOLD",
				HorizontalAlignment = HorizontalAlignment.Center,
				Modulate = new Color("#4ADE80"),
				MouseFilter = MouseFilterEnum.Ignore
			};
			rateLabel.AddThemeFontSizeOverride("font_size", 12);
			rateLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
			rateLabel.AddThemeConstantOverride("outline_size", 2);

			stack.AddChild(row);
			stack.AddChild(rateLabel);
			capsuleBtn.AddChild(stack);
			return capsuleBtn;
		}

		// =========================================================================
		// 4. SMART HANGAR RESOURCE PRODUCTION & GGOLD STORE MODAL
		// - Fits strictly inside the safe screen corridor (X: 356..vpSize.X-336, Y: 76..vpSize.Y-80)
		// - Never overlaps Top Bar, Left Fleet Dock, Right Engineering Bay, or Bottom Nav Dock
		// - Uses friendly, easy-to-read gaming language for players
		// =========================================================================
		private void BuildSmartHangarOverlay()
		{
			_smartHangarOverlay = new ColorRect
			{
				Name = "SmartHangarOverlay",
				Color = new Color(0f, 0f, 0f, 0.65f),
				Visible = false,
				MouseFilter = MouseFilterEnum.Stop
			};
			_smartHangarOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
			_smartHangarOverlay.GuiInput += (InputEvent @event) =>
			{
				if (@event is InputEventMouseButton mb && mb.IsPressed() && mb.ButtonIndex == MouseButton.Left)
				{
					CloseSmartHangarModal();
				}
			};
			AddChild(_smartHangarOverlay);

			_smartHangarPanel = new PanelContainer
			{
				Name = "SmartHangarPanel",
				MouseFilter = MouseFilterEnum.Stop
			};
			_smartHangarPanelStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.03f, 0.06f, 0.11f, 0.98f),
				BorderColor = new Color("#00F0FF"),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 10,
				CornerRadiusTopRight = 10,
				CornerRadiusBottomLeft = 10,
				CornerRadiusBottomRight = 10,
				ContentMarginLeft = 16,
				ContentMarginRight = 16,
				ContentMarginTop = 12,
				ContentMarginBottom = 12,
				ShadowColor = new Color(0f, 0f, 0f, 0.7f),
				ShadowSize = 18
			};
			_smartHangarPanel.AddThemeStyleboxOverride("panel", _smartHangarPanelStyle);
			_smartHangarOverlay.AddChild(_smartHangarPanel);

			VBoxContainer rootBox = new VBoxContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill
			};
			rootBox.AddThemeConstantOverride("separation", 8);
			_smartHangarPanel.AddChild(rootBox);

			HBoxContainer headerRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			headerRow.AddThemeConstantOverride("separation", 10);

			_lblSmartHangarTitle = new Label
			{
				Text = "SMART HANGAR · RESOURCE PRODUCTION",
				Modulate = Colors.White,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			_lblSmartHangarTitle.AddThemeFontSizeOverride("font_size", 20);
			_lblSmartHangarTitle.AddThemeColorOverride("font_outline_color", Colors.Black);
			_lblSmartHangarTitle.AddThemeConstantOverride("outline_size", 3);

			Button btnClose = new Button
			{
				Text = " ✕ ",
				CustomMinimumSize = new Vector2(44, 40),
				MouseFilter = MouseFilterEnum.Stop
			};
			btnClose.AddThemeFontSizeOverride("font_size", 17);
			StyleBoxFlat closeStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.25f, 0.06f, 0.08f, 0.95f),
				BorderColor = new Color("#EF4444"),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6
			};
			btnClose.AddThemeStyleboxOverride("normal", closeStyle);
			btnClose.Pressed += CloseSmartHangarModal;

			headerRow.AddChild(_lblSmartHangarTitle);
			headerRow.AddChild(btnClose);
			rootBox.AddChild(headerRow);

			// Quick Switcher Tabs inside the Smart Hangar so players can jump between Power, Iron, Titanium, Helium-3, and GGold
			HBoxContainer tabRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			tabRow.AddThemeConstantOverride("separation", 6);
			tabRow.AddChild(CreateSmartHangarTabButton("⚡ POWER", "E", new Color("#D946EF")));
			tabRow.AddChild(CreateSmartHangarTabButton("⛏️ IRON", "I", new Color("#06B6D4")));
			tabRow.AddChild(CreateSmartHangarTabButton("💎 TITANIUM", "T", new Color("#94A3B8")));
			tabRow.AddChild(CreateSmartHangarTabButton("⛽ HELIUM-3", "H3", new Color("#EAB308")));
			tabRow.AddChild(CreateSmartHangarTabButton("🪙 GGOLD STORE", "GGD", new Color("#FBBF24")));
			rootBox.AddChild(tabRow);

			rootBox.AddChild(new HSeparator());

			ScrollContainer scroll = new ScrollContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill,
				HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
			};
			rootBox.AddChild(scroll);

			_smartHangarContentBox = new VBoxContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};
			_smartHangarContentBox.AddThemeConstantOverride("separation", 8);
			scroll.AddChild(_smartHangarContentBox);

			LayoutSmartHangarModal();
		}

		private Button CreateSmartHangarTabButton(string label, string resCode, Color accent)
		{
			Button b = new Button
			{
				Text = label,
				CustomMinimumSize = new Vector2(0, 40),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Stop,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			b.AddThemeFontSizeOverride("font_size", 14);
			b.Modulate = accent;
			StyleBoxFlat st = new StyleBoxFlat
			{
				BgColor = new Color(0.06f, 0.10f, 0.16f, 0.95f),
				BorderColor = new Color(accent.R, accent.G, accent.B, 0.65f),
				BorderWidthLeft = 1,
				BorderWidthRight = 1,
				BorderWidthTop = 1,
				BorderWidthBottom = 1,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6
			};
			b.AddThemeStyleboxOverride("normal", st);
			b.Pressed += () => OpenSmartHangarModal(resCode);
			return b;
		}

		private void LayoutSmartHangarModal()
		{
			if (_smartHangarPanel == null || !IsInstanceValid(_smartHangarPanel)) return;
			Vector2 vpSize = GetViewportRect().Size;
			if (vpSize.X <= 0 || vpSize.Y <= 0) return;

			// Top Resource Hangar (Y: 0..68) is ALWAYS kept visible!
			// Side docks auto-hide while Smart Hangar is open so the modal enjoys a wide, ultra-readable layout.
			float topSafeY = 66f;
			float bottomSafeY = Math.Max(topSafeY + 320f, vpSize.Y - 74f);
			float maxSafeH = bottomSafeY - topSafeY;

			float desiredW = 1180f;
			float desiredH = Math.Min(640f, maxSafeH);

			float panelW = Mathf.Min(desiredW, Math.Max(420f, vpSize.X - 24f));
			float panelX = (vpSize.X - panelW) * 0.5f;
			float panelY = topSafeY + (maxSafeH - desiredH) * 0.50f;

			_smartHangarPanel.Position = new Vector2(panelX, panelY);
			_smartHangarPanel.CustomMinimumSize = new Vector2(panelW, desiredH);
			_smartHangarPanel.Size = new Vector2(panelW, desiredH);
		}

		public void OpenSmartHangarModal(string resCode)
		{
			if (_smartHangarOverlay == null) return;

			// Close any open side dropdowns or other HUD modals so there is zero overlap
			_isFleetDropdownOpen = false;
			_isQueueDropdownOpen = false;
			if (_fleetDropdownPanel != null) _fleetDropdownPanel.Visible = false;
			if (_queueDropdownPanel != null) _queueDropdownPanel.Visible = false;
			_profileModal?.CloseModal();
			_commsModal?.CloseModal();
			if (BuildingInspectorModal.Instance != null && IsInstanceValid(BuildingInspectorModal.Instance) && BuildingInspectorModal.Instance.Visible)
			{
				BuildingInspectorModal.Instance.CloseInspector();
			}

			if (_smartHangarOverlay.Visible && string.Equals(_activeSmartHangarResource, resCode, StringComparison.OrdinalIgnoreCase))
			{
				CloseSmartHangarModal();
				return;
			}

			_activeSmartHangarResource = (resCode ?? "E").ToUpper();
			LayoutSmartHangarModal();
			RefreshSmartHangarContent();
			_smartHangarOverlay.Visible = true;
			SyncSideDocksVisibilityForActiveModals();
		}

		public void CloseSmartHangarModal()
		{
			if (_smartHangarOverlay != null) _smartHangarOverlay.Visible = false;
			SyncSideDocksVisibilityForActiveModals();
		}

		private PanelContainer CreateSmartHangarCard(Color borderCol, Color bgCol)
		{
			PanelContainer card = new PanelContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};
			StyleBoxFlat st = new StyleBoxFlat
			{
				BgColor = bgCol,
				BorderColor = borderCol,
				BorderWidthLeft = 3,
				BorderWidthRight = 1,
				BorderWidthTop = 1,
				BorderWidthBottom = 1,
				CornerRadiusTopLeft = 8,
				CornerRadiusTopRight = 8,
				CornerRadiusBottomLeft = 8,
				CornerRadiusBottomRight = 8,
				ContentMarginLeft = 12,
				ContentMarginRight = 12,
				ContentMarginTop = 8,
				ContentMarginBottom = 8
			};
			card.AddThemeStyleboxOverride("panel", st);
			return card;
		}

		private void AddMineRowToBox(VBoxContainer parent, string mineLabel, int level, long hourlyOutput, Color accent)
		{
			HBoxContainer row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			row.AddThemeConstantOverride("separation", 8);

			string statusText = level > 0 ? $"Level {level}" : "Not Built Yet (Level 0)";
			Label lblLeft = new Label
			{
				Text = $"• {mineLabel}  ({statusText})",
				Modulate = level > 0 ? Colors.White : new Color("#94A3B8"),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			lblLeft.AddThemeFontSizeOverride("font_size", 15);

			Label lblRight = new Label
			{
				Text = $"+{hourlyOutput:N0} / hour",
				Modulate = level > 0 ? accent : new Color("#64748B"),
				HorizontalAlignment = HorizontalAlignment.Right
			};
			lblRight.AddThemeFontSizeOverride("font_size", 15);

			row.AddChild(lblLeft);
			row.AddChild(lblRight);
			parent.AddChild(row);
		}

		private void RefreshSmartHangarContent()
		{
			if (_smartHangarContentBox == null || !IsInstanceValid(_smartHangarContentBox)) return;
			foreach (Node c in _smartHangarContentBox.GetChildren()) c.QueueFree();

			var tel = GetSmartHangarTelemetry();
			string code = string.IsNullOrEmpty(_activeSmartHangarResource) ? "E" : _activeSmartHangarResource;

			if (code == "E" || code == "I" || code == "T")
			{
				Color themeCol = code == "E" ? new Color("#D946EF") : (code == "I" ? new Color("#06B6D4") : new Color("#94A3B8"));
				string resName = code == "E" ? "POWER" : (code == "I" ? "IRON" : "TITANIUM");
				string icon = code == "E" ? "⚡" : (code == "I" ? "⛏️" : "💎");
				string minePrefix = code == "E" ? "Power Mine" : (code == "I" ? "Iron Mine" : "Titanium Mine");
				double curStored = code == "E" ? ResE : (code == "I" ? ResI : ResT);
				long mA = code == "E" ? tel.mineE0 : (code == "I" ? tel.mineI0 : tel.mineT0);
				long mB = code == "E" ? tel.mineE1 : (code == "I" ? tel.mineI1 : tel.mineT1);
				long mC = code == "E" ? tel.mineE2 : (code == "I" ? tel.mineI2 : tel.mineT2);
				long totProd = code == "E" ? tel.totalProdE : (code == "I" ? tel.totalProdI : tel.totalProdT);
				double boostMult = code == "E" ? tel.multE : (code == "I" ? tel.multI : tel.multT);

				int lvlA = BuildingLevels.GetValueOrDefault($"dist_{code.ToLower()}_0", 0);
				int lvlB = BuildingLevels.GetValueOrDefault($"dist_{code.ToLower()}_1", 0);
				int lvlC = BuildingLevels.GetValueOrDefault($"dist_{code.ToLower()}_2", 0);

				if (_lblSmartHangarTitle != null)
				{
					_lblSmartHangarTitle.Text = $"{icon} SMART HANGAR · {resName} PRODUCTION";
					_lblSmartHangarTitle.Modulate = themeCol;
				}
				if (_smartHangarPanelStyle != null) _smartHangarPanelStyle.BorderColor = themeCol;

				// Card 1: Storage & Total Production Summary
				PanelContainer sumCard = CreateSmartHangarCard(themeCol, new Color(0.05f, 0.09f, 0.16f, 0.96f));
				VBoxContainer sumBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				sumBox.AddThemeConstantOverride("separation", 4);
				sumCard.AddChild(sumBox);

				Label lblTotalBig = new Label
				{
					Text = $"TOTAL {resName} PRODUCTION:  +{totProd:N0} / HOUR",
					Modulate = new Color("#4ADE80")
				};
				lblTotalBig.AddThemeFontSizeOverride("font_size", 18);
				sumBox.AddChild(lblTotalBig);

				string boostInfo = boostMult > 1.0f ? $"Active Boost: +{((boostMult - 1.0) * 100):F0}% Bonus" : "No Production Boost Active";
				Label lblStored = new Label
				{
					Text = $"Stored in Silos: {(long)curStored:N0} / {SiloCap:N0}   |   Server Speed: {ServerSpeed:0.#}x   |   {boostInfo}",
					Modulate = new Color("#CBD5E1"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				lblStored.AddThemeFontSizeOverride("font_size", 15);
				sumBox.AddChild(lblStored);
				_smartHangarContentBox.AddChild(sumCard);

				// Card 2: Mines A, B, C Breakdown
				PanelContainer minesCard = CreateSmartHangarCard(themeCol, new Color(0.03f, 0.06f, 0.11f, 0.96f));
				VBoxContainer minesBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				minesBox.AddThemeConstantOverride("separation", 6);
				minesCard.AddChild(minesBox);

				Label lblMinesHdr = new Label
				{
					Text = $"{resName} MINES (A, B, C) HOURLY BREAKDOWN:",
					Modulate = themeCol
				};
				lblMinesHdr.AddThemeFontSizeOverride("font_size", 16);
				minesBox.AddChild(lblMinesHdr);

				AddMineRowToBox(minesBox, $"{minePrefix} A", lvlA, mA, new Color("#4ADE80"));
				AddMineRowToBox(minesBox, $"{minePrefix} B", lvlB, mB, new Color("#4ADE80"));
				AddMineRowToBox(minesBox, $"{minePrefix} C", lvlC, mC, new Color("#4ADE80"));

				minesBox.AddChild(new HSeparator());

				HBoxContainer totalRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				Label totLeft = new Label
				{
					Text = $"COMBINED {resName} OUTPUT (MINES A + B + C):",
					Modulate = Colors.White,
					SizeFlagsHorizontal = SizeFlags.ExpandFill
				};
				totLeft.AddThemeFontSizeOverride("font_size", 16);
				Label totRight = new Label
				{
					Text = $"+{totProd:N0} / hour",
					Modulate = new Color("#4ADE80")
				};
				totRight.AddThemeFontSizeOverride("font_size", 17);
				totalRow.AddChild(totLeft);
				totalRow.AddChild(totRight);
				minesBox.AddChild(totalRow);

				_smartHangarContentBox.AddChild(minesCard);
			}
			else if (code == "H3")
			{
				Color themeCol = tel.netH3PerHour < 0.0 ? new Color("#EF4444") : new Color("#EAB308");
				if (_lblSmartHangarTitle != null)
				{
					_lblSmartHangarTitle.Text = "⛽ SMART HANGAR · HELIUM-3 PRODUCTION & ARMY FUEL UPKEEP";
					_lblSmartHangarTitle.Modulate = themeCol;
				}
				if (_smartHangarPanelStyle != null) _smartHangarPanelStyle.BorderColor = themeCol;

				// Card 1: Net Helium-3 Balance (Production minus Unit Consumption)
				PanelContainer netCard = CreateSmartHangarCard(themeCol, tel.netH3PerHour < 0.0 ? new Color(0.16f, 0.04f, 0.05f, 0.96f) : new Color(0.05f, 0.09f, 0.16f, 0.96f));
				VBoxContainer netBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				netBox.AddThemeConstantOverride("separation", 5);
				netCard.AddChild(netBox);

				Label lblNetHdr = new Label
				{
					Text = $"NET HELIUM-3 PRODUCTION:  {FormatH3Rate(tel.netH3PerHour)} / HOUR",
					Modulate = tel.netH3PerHour >= 0.0 ? new Color("#4ADE80") : new Color("#F87171")
				};
				lblNetHdr.AddThemeFontSizeOverride("font_size", 18);
				netBox.AddChild(lblNetHdr);

				Label lblFormula = new Label
				{
					Text = $"Total Mines Production ({FormatH3Rate(tel.totalProdH3)}/h)  −  All Built Units Fuel Upkeep (-{FormatH3Rate(tel.totalUnitH3Upkeep, false)}/h)  =  {FormatH3Rate(tel.netH3PerHour)}/h\n" +
						   $"Stored Helium-3 Balance: {FormatH3Balance(ResH3)} / {SiloCap:N0}  (Stops at 0 — never goes negative)",
					Modulate = Colors.White,
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				lblFormula.AddThemeFontSizeOverride("font_size", 15);
				netBox.AddChild(lblFormula);

				if (tel.netH3PerHour < 0.0)
				{
					string starveStatus;
					if (ResH3 <= 0.0 && tel.totalBuiltUnits > 0)
					{
						int secLeft = Math.Max(1, (int)Math.Ceiling(600.0 - _h3StarvationTimerSec));
						starveStatus = $"🚨 OUT OF FUEL (0 HELIUM-3)! Your units are starving! Next unit losses in {FormatTime(secLeft)} (every 10 minutes) until you Raid Helium-3, upgrade H3 Mines, or juggle/drop off troops between your owned & occupied bases!";
					}
					else
					{
						double hoursLeft = ResH3 / Math.Max(0.01, Math.Abs(tel.netH3PerHour));
						int secUntilZero = (int)Math.Round(hoursLeft * 3600.0);
						starveStatus = $"⚠️ FUEL WARNING: Your army consumes more Helium-3 than your mines produce! Stored Helium-3 will hit 0 in {FormatTime(secUntilZero)}. Once it hits 0, units start dying every 10 minutes — Raid for Helium-3, upgrade H3 Mines, or send fleets to drop off units at your owned bases!";
					}

					Label lblWarn = new Label
					{
						Text = starveStatus,
						Modulate = new Color("#FCA5A5"),
						AutowrapMode = TextServer.AutowrapMode.WordSmart
					};
					lblWarn.AddThemeFontSizeOverride("font_size", 14);
					netBox.AddChild(lblWarn);
				}
				else
				{
					Label lblSafe = new Label
					{
						Text = "✓ FUEL SUPPLY HEALTHY: Your Helium-3 Mines produce enough fuel to keep all of your units supplied.",
						Modulate = new Color("#4ADE80"),
						AutowrapMode = TextServer.AutowrapMode.WordSmart
					};
					lblSafe.AddThemeFontSizeOverride("font_size", 14);
					netBox.AddChild(lblSafe);
				}

				_smartHangarContentBox.AddChild(netCard);

				// Card 2: Helium-3 Mines A, B, C, D, E Production Breakdown
				PanelContainer minesCard = CreateSmartHangarCard(new Color("#EAB308"), new Color(0.03f, 0.06f, 0.11f, 0.96f));
				VBoxContainer minesBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				minesBox.AddThemeConstantOverride("separation", 5);
				minesCard.AddChild(minesBox);

				Label lblMinesHdr = new Label
				{
					Text = "⛽ HELIUM-3 MINES (A, B, C, D, E) PRODUCTION:",
					Modulate = new Color("#EAB308")
				};
				lblMinesHdr.AddThemeFontSizeOverride("font_size", 16);
				minesBox.AddChild(lblMinesHdr);

				AddMineRowToBox(minesBox, "Helium-3 Mine A", BuildingLevels.GetValueOrDefault("dist_h3_0", 0), tel.mineH0, new Color("#4ADE80"));
				AddMineRowToBox(minesBox, "Helium-3 Mine B", BuildingLevels.GetValueOrDefault("dist_h3_1", 0), tel.mineH1, new Color("#4ADE80"));
				AddMineRowToBox(minesBox, "Helium-3 Mine C", BuildingLevels.GetValueOrDefault("dist_h3_2", 0), tel.mineH2, new Color("#4ADE80"));
				AddMineRowToBox(minesBox, "Helium-3 Mine D", BuildingLevels.GetValueOrDefault("dist_h3_3", 0), tel.mineH3, new Color("#4ADE80"));
				AddMineRowToBox(minesBox, "Helium-3 Mine E", BuildingLevels.GetValueOrDefault("dist_h3_4", 0), tel.mineH4, new Color("#4ADE80"));

				minesBox.AddChild(new HSeparator());
				HBoxContainer totMineRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				Label tmLeft = new Label { Text = "TOTAL HELIUM-3 MINES PRODUCTION (A–E):", Modulate = Colors.White, SizeFlagsHorizontal = SizeFlags.ExpandFill };
				tmLeft.AddThemeFontSizeOverride("font_size", 16);
				Label tmRight = new Label { Text = $"{FormatH3Rate(tel.totalProdH3)} / hour", Modulate = new Color("#4ADE80") };
				tmRight.AddThemeFontSizeOverride("font_size", 17);
				totMineRow.AddChild(tmLeft);
				totMineRow.AddChild(tmRight);
				minesBox.AddChild(totMineRow);
				_smartHangarContentBox.AddChild(minesCard);

				// Card 3: All Built Units Helium-3 Consumption Breakdown
				PanelContainer unitsCard = CreateSmartHangarCard(new Color("#38BDF8"), new Color(0.03f, 0.06f, 0.11f, 0.96f));
				VBoxContainer unitsBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				unitsBox.AddThemeConstantOverride("separation", 5);
				unitsCard.AddChild(unitsBox);

				Label lblUnitsHdr = new Label
				{
					Text = $"⚔️ ALL BUILT UNITS FUEL CONSUMPTION ({tel.totalBuiltUnits:N0} TOTAL UNITS):",
					Modulate = new Color("#38BDF8")
				};
				lblUnitsHdr.AddThemeFontSizeOverride("font_size", 16);
				unitsBox.AddChild(lblUnitsHdr);

				int anyUnitTypes = 0;
				foreach (var u in GameData.Units)
				{
					int garrisonCnt = UnitInventory != null ? Math.Max(0, UnitInventory.GetValueOrDefault(u.Id, 0)) : 0;
					int fleetCnt = 0;
					if (SavedFleets != null)
					{
						foreach (var f in SavedFleets)
						{
							if (f == null || f.Status == "WATCHLIST_DATA" || f.Units == null) continue;
							fleetCnt += Math.Max(0, f.Units.GetValueOrDefault(u.Id, 0));
						}
					}
					int totalCnt = garrisonCnt + fleetCnt;
					if (totalCnt <= 0) continue;

					anyUnitTypes++;
					double drain = (double)u.H3Drain * totalCnt;

					HBoxContainer uRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
					Label uLeft = new Label
					{
						Text = $"• {u.Name}: {totalCnt:N0}x  ({garrisonCnt:N0} at Base, {fleetCnt:N0} in Fleets · {FormatH3Rate(u.H3Drain, false)}/h each)",
						Modulate = Colors.White,
						SizeFlagsHorizontal = SizeFlags.ExpandFill
					};
					uLeft.AddThemeFontSizeOverride("font_size", 15);

					Label uRight = new Label
					{
						Text = $"-{FormatH3Rate(drain, false)} / hour",
						Modulate = new Color("#F87171"),
						HorizontalAlignment = HorizontalAlignment.Right
					};
					uRight.AddThemeFontSizeOverride("font_size", 15);

					uRow.AddChild(uLeft);
					uRow.AddChild(uRight);
					unitsBox.AddChild(uRow);
				}

				if (anyUnitTypes == 0)
				{
					Label noUnitsLbl = new Label
					{
						Text = "You currently have 0 built units. Build units in the Orbital Shipyard — they will use Helium-3 fuel per hour once built.",
						Modulate = new Color("#94A3B8"),
						AutowrapMode = TextServer.AutowrapMode.WordSmart
					};
					noUnitsLbl.AddThemeFontSizeOverride("font_size", 15);
					unitsBox.AddChild(noUnitsLbl);
				}

				unitsBox.AddChild(new HSeparator());
				HBoxContainer totDrainRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				Label tdLeft = new Label { Text = "TOTAL ARMY HELIUM-3 CONSUMPTION:", Modulate = Colors.White, SizeFlagsHorizontal = SizeFlags.ExpandFill };
				tdLeft.AddThemeFontSizeOverride("font_size", 16);
				Label tdRight = new Label { Text = $"-{FormatH3Rate(tel.totalUnitH3Upkeep, false)} / hour", Modulate = new Color("#F87171") };
				tdRight.AddThemeFontSizeOverride("font_size", 17);
				totDrainRow.AddChild(tdLeft);
				totDrainRow.AddChild(tdRight);
				unitsBox.AddChild(totDrainRow);

				_smartHangarContentBox.AddChild(unitsCard);
			}
			else if (code == "GGD")
			{
				Color goldCol = new Color("#FBBF24");
				if (_lblSmartHangarTitle != null)
				{
					_lblSmartHangarTitle.Text = $"🪙 GALAXY GOLD STORE · TREASURY: {GGold:N0} GGOLD";
					_lblSmartHangarTitle.Modulate = goldCol;
				}
				if (_smartHangarPanelStyle != null) _smartHangarPanelStyle.BorderColor = goldCol;

				PanelContainer introCard = CreateSmartHangarCard(goldCol, new Color(0.10f, 0.07f, 0.02f, 0.96f));
				VBoxContainer introBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				introBox.AddThemeConstantOverride("separation", 4);
				introCard.AddChild(introBox);

				Label lblIntro = new Label
				{
					Text = $"CURRENT GALAXY GOLD BALANCE:  🪙 {GGold:N0} GGOLD\n" +
						   "Get Galaxy Gold packages below to queue up to 20 building upgrades and activate +30% / +75% Resource Production Boosts!",
					Modulate = Colors.White,
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				lblIntro.AddThemeFontSizeOverride("font_size", 15);
				introBox.AddChild(lblIntro);
				_smartHangarContentBox.AddChild(introCard);

				var packs = new[]
				{
					new { Name = "RECON PACK", Gold = 1000, Bonus = "Starter Bundle", Price = "$0.99" },
					new { Name = "COMMAND PACK", Gold = 5000, Bonus = "Popular Choice", Price = "$4.49" },
					new { Name = "EMPIRE PACK", Gold = 11000, Bonus = "+10% Extra Gold", Price = "$8.99" },
					new { Name = "OVERLORD PACK", Gold = 60000, Bonus = "Best Value Vault", Price = "$39.99" }
				};

				foreach (var pack in packs)
				{
					PanelContainer packCard = CreateSmartHangarCard(goldCol, new Color(0.04f, 0.07f, 0.12f, 0.96f));
					HBoxContainer row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
					row.AddThemeConstantOverride("separation", 12);
					packCard.AddChild(row);

					Label lblPack = new Label
					{
						Text = $"🪙 {pack.Name}  —  +{pack.Gold:N0} GGOLD  ({pack.Bonus})",
						Modulate = goldCol,
						SizeFlagsHorizontal = SizeFlags.ExpandFill,
						AutowrapMode = TextServer.AutowrapMode.WordSmart
					};
					lblPack.AddThemeFontSizeOverride("font_size", 16);

					Button btnBuy = new Button
					{
						Text = $"BUY {pack.Price}",
						CustomMinimumSize = new Vector2(138, 44),
						MouseFilter = MouseFilterEnum.Stop
					};
					btnBuy.AddThemeFontSizeOverride("font_size", 15);
					btnBuy.Modulate = new Color("#4ADE80");
					int gAmount = pack.Gold;
					string pName = pack.Name;
					btnBuy.Pressed += () =>
					{
						GGold += gAmount;
						UpdateHUDDisplay();
						TriggerInstantSave();
						RefreshSmartHangarContent();
						ShowFleetDockNotification($"🪙 {pName} UNLOCKED: +{gAmount:N0} Galaxy Gold added to your Treasury!", new Color("#FBBF24"));
					};

					row.AddChild(lblPack);
					row.AddChild(btnBuy);
					_smartHangarContentBox.AddChild(packCard);
				}
			}
		}

		private static string FormatTime(double totalSeconds)
		{
			int s = Math.Max(0, (int)totalSeconds);
			int m = s / 60;
			int remS = s % 60;
			int h = m / 60;
			int remM = m % 60;

			if (h > 0) return $"{h}h {remM:D2}m {remS:D2}s";
			if (m > 0) return $"{m}m {remS:D2}s";
			return $"{remS}s";
		}
	}
}
