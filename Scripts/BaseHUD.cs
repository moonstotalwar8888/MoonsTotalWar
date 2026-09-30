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
		[Signal] public delegate void SpeedupClickedEventHandler();
		[Signal] public delegate void NavTierSelectedEventHandler(string tierName);
		[Signal] public delegate void EditModeToggledEventHandler(bool isEditing);
		[Signal] public delegate void SaveLayoutRequestedEventHandler();
		[Signal] public delegate void ColonyDataLoadedEventHandler(); 
		[Signal] public delegate void QueuedBuildingCompletedEventHandler(string buildingId, int newLevel);

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

		// Fleet Data
		public int ActiveFleetCount = 0;
		public string ActiveFleetTarget = "NONE";
		public double ActiveFleetEta = 0.0;

		// State Flags
		public bool IsEditModeActive { get; private set; } = false;
		private bool _isDataLoaded = false;
		private bool _isQueueDropdownOpen = false;

		// Timers
		private double _heartbeatTimer = 0.0;
		private double _autoSaveTimer = 0.0;

		// UI Node References
		private Panel _bottomPanelNode;
		private Label _lblCmdName;
		private Button _btnColonySwitch;
		private Label _lblValE, _lblValI, _lblValT, _lblValH3, _lblValGGold;
		private Label _lblBuildName, _lblBuildTimer;
		private Button _btnQueueToggle;
		private Panel _queueDropdownPanel;
		private VBoxContainer _queueListContainer;
		private Button _btnCancelLast;
		private Label _lblFleetStatus, _lblFleetEta;
		private Button _btnEditLayoutToggle;

		public override void _Ready()
		{
			Instance = this;

			SetAnchorsPreset(LayoutPreset.FullRect);
			MouseFilter = MouseFilterEnum.Ignore;

			GetViewport().Connect("size_changed", Callable.From(RecalculateLayoutBounds));

			BuildTopCockpitBar();
			BuildTacticalSideDocks();
			BuildBottomCommandNavigationDock();

			RecalculateLayoutBounds();

			// Initiate Cloud Handshake
			LoadColonyDataFromCloud();
		}

		private async void LoadColonyDataFromCloud()
		{
			if (SupabaseService.Instance == null) return;

			GD.Print("[BASE HUD] Initiating Cloud Handshake...");
			var data = await SupabaseService.Instance.FetchColonyStateAsync();

			if (data != null)
			{
				CommanderName = data.commander_name;
				ResE = data.res_e;
				ResI = data.res_i;
				ResT = data.res_t;
				ResH3 = data.res_h3;
				GGold = data.mgold; 
				
				if (data.building_levels != null) BuildingLevels = data.building_levels;

				// Extract Queue & Boost Data from Cloud
				ActiveBuild = data.active_build;
				BuildQueue = data.build_queue ?? new List<GameMath.BuildQueueItem>();
				ActiveBoosts = data.active_boosts ?? new Dictionary<string, GameMath.BoostData>();

				StorageSiloLevel = BuildingLevels.GetValueOrDefault("hub_silo", 1);
				RecalculateSiloCap();

				// Run ChronoEngine to catch up on offline production, queues, and fractional boosts
				var chrono = ChronoEngine.SimulateOfflineTimeline(
					ResE, ResI, ResT, ResH3, 
					BuildingLevels, 
					ActiveBuild,
					BuildQueue,
					ActiveBoosts,
					data.last_sync_time, 
					DateTimeOffset.UtcNow, 
					ServerSpeed, 
					0 
				);

				ResE = chrono.resE;
				ResI = chrono.resI;
				ResT = chrono.resT;
				ResH3 = chrono.resH3;
				ActiveBuild = chrono.activeBuild;
				BuildQueue = chrono.buildQueue;
				ActiveBoosts = chrono.activeBoosts;

				_isDataLoaded = true;
				UpdateHUDDisplay();
				UpdateEngineeringDockUI();
				
				EmitSignal(SignalName.ColonyDataLoaded);
				TriggerInstantSave();
			}
			else
			{
				GD.PrintErr("[BASE HUD] Failed to load cloud data. Running in offline/fallback mode.");
				_isDataLoaded = true;
				UpdateHUDDisplay();
				UpdateEngineeringDockUI();
				EmitSignal(SignalName.ColonyDataLoaded);
			}
		}

		public void TriggerInstantSave()
		{
			if (!_isDataLoaded || SupabaseService.Instance == null) return;

			GD.Print("[BASE HUD] Triggering Instant Cloud Save...");
			_ = SupabaseService.Instance.SaveColonyStateAsync(ResE, ResI, ResT, ResH3, GGold, BuildingLevels, ActiveBuild, BuildQueue, ActiveBoosts);
			_autoSaveTimer = 0.0; 
		}

		public override void _Process(double delta)
		{
			if (!_isDataLoaded) return;

			_heartbeatTimer += delta;
			_autoSaveTimer += delta;

			// Process Active Build Queue
			if (ActiveBuild != null)
			{
				ActiveBuild.DurationLeft -= delta;
				if (ActiveBuild.DurationLeft <= 0)
				{
					CompleteActiveBuild();
				}
			}
			else if (BuildQueue.Count > 0)
			{
				StartNextBuildInQueue();
			}

			if (ActiveFleetEta > 0)
			{
				ActiveFleetEta = Math.Max(0, ActiveFleetEta - delta);
			}

			// 1-Second Production Heartbeat
			if (_heartbeatTimer >= 1.0)
			{
				_heartbeatTimer = 0.0;
				TickResourceHeartbeat();
			}

			// 60-Second Auto-Save
			if (_autoSaveTimer >= 60.0)
			{
				TriggerInstantSave();
			}

			UpdateTimersDisplay();
		}

		private void CompleteActiveBuild()
		{
			if (ActiveBuild == null) return;

			string bId = ActiveBuild.BuildingId;
			int newLvl = ActiveBuild.TargetLevel;

			BuildingLevels[bId] = newLvl;
			if (bId == "hub_silo")
			{
				StorageSiloLevel = newLvl;
				RecalculateSiloCap();
			}

			GD.Print($"[QUEUE ENGINE] Completed {ActiveBuild.BuildingName} Lvl {newLvl}!");
			EmitSignal(SignalName.QueuedBuildingCompleted, bId, newLvl);

			ActiveBuild = null;
			UpdateEngineeringDockUI();
			TriggerInstantSave();
		}

		private void StartNextBuildInQueue()
		{
			if (BuildQueue.Count == 0) return;

			ActiveBuild = BuildQueue[0];
			BuildQueue.RemoveAt(0);

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
			
			ResE = Math.Min(SiloCap, ResE);
			ResI = Math.Min(SiloCap, ResI);
			ResT = Math.Min(SiloCap, ResT);
			ResH3 = Math.Min(SiloCap, ResH3);
		}

		private void RecalculateLayoutBounds()
		{
			Vector2 vpSize = GetViewportRect().Size;
			Size = vpSize;

			if (_bottomPanelNode != null)
			{
				_bottomPanelNode.Position = new Vector2(0, vpSize.Y - 60);
				_bottomPanelNode.Size = new Vector2(vpSize.X, 60);
			}
		}

		private void TickResourceHeartbeat()
		{
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

			// 2. Calculate Base Hourly Rates
			double rateE = 0, rateI = 0, rateT = 0, rateH3 = 0;

			rateE += GameMath.CalcSubMineYield(BuildingLevels.GetValueOrDefault("dist_e_0", 0));
			rateE += GameMath.CalcSubMineYield(BuildingLevels.GetValueOrDefault("dist_e_1", 0));
			rateE += GameMath.CalcSubMineYield(BuildingLevels.GetValueOrDefault("dist_e_2", 0));

			rateI += GameMath.CalcSubMineYield(BuildingLevels.GetValueOrDefault("dist_i_0", 0));
			rateI += GameMath.CalcSubMineYield(BuildingLevels.GetValueOrDefault("dist_i_1", 0));
			rateI += GameMath.CalcSubMineYield(BuildingLevels.GetValueOrDefault("dist_i_2", 0));

			rateT += GameMath.CalcSubMineYield(BuildingLevels.GetValueOrDefault("dist_t_0", 0));
			rateT += GameMath.CalcSubMineYield(BuildingLevels.GetValueOrDefault("dist_t_1", 0));
			rateT += GameMath.CalcSubMineYield(BuildingLevels.GetValueOrDefault("dist_t_2", 0));

			rateH3 += GameMath.CalcSubMineYield(BuildingLevels.GetValueOrDefault("dist_h3_0", 0));
			rateH3 += GameMath.CalcSubMineYield(BuildingLevels.GetValueOrDefault("dist_h3_1", 0));
			rateH3 += GameMath.CalcSubMineYield(BuildingLevels.GetValueOrDefault("dist_h3_2", 0));
			rateH3 += GameMath.CalcSubMineYield(BuildingLevels.GetValueOrDefault("dist_h3_3", 0));
			rateH3 += GameMath.CalcSubMineYield(BuildingLevels.GetValueOrDefault("dist_h3_4", 0));

			// 3. Apply Live Boost Multipliers
			double multE = ActiveBoosts.ContainsKey("E") ? ActiveBoosts["E"].Multiplier : 1.0;
			double multI = ActiveBoosts.ContainsKey("I") ? ActiveBoosts["I"].Multiplier : 1.0;
			double multT = ActiveBoosts.ContainsKey("T") ? ActiveBoosts["T"].Multiplier : 1.0;
			double multH3 = ActiveBoosts.ContainsKey("H3") ? ActiveBoosts["H3"].Multiplier : 1.0;

			double finalRateE = rateE * multE;
			double finalRateI = rateI * multI;
			double finalRateT = rateT * multT;
			double finalRateH3 = rateH3 * multH3;

			// 4. Convert to Per-Second Ticks
			double pE = (finalRateE * ServerSpeed) / 3600.0;
			double pI = (finalRateI * ServerSpeed) / 3600.0;
			double pT = (finalRateT * ServerSpeed) / 3600.0;
			double pH3 = (finalRateH3 * ServerSpeed) / 3600.0;

			ResE = Math.Min(SiloCap, ResE + pE);
			ResI = Math.Min(SiloCap, ResI + pI);
			ResT = Math.Min(SiloCap, ResT + pT);
			ResH3 = Math.Min(SiloCap, ResH3 + pH3);

			UpdateHUDDisplay();
		}

		public void UpdateHUDDisplay()
		{
			RecalculateSiloCap();

			if (_lblCmdName != null) _lblCmdName.Text = CommanderName;
			if (_btnColonySwitch != null) _btnColonySwitch.Text = $"M:{CurrentMoon} ; B:{CurrentBaseSlot} ({CurrentBaseName}) ▼";

			if (_lblValE != null) _lblValE.Text = $"{(long)ResE:N0}";
			if (_lblValI != null) _lblValI.Text = $"{(long)ResI:N0}";
			if (_lblValT != null) _lblValT.Text = $"{(long)ResT:N0}";
			if (_lblValH3 != null) _lblValH3.Text = $"{(long)ResH3:N0}";
			if (_lblValGGold != null) _lblValGGold.Text = $"{GGold:N0}";
		}

		private void UpdateTimersDisplay()
		{
			if (_lblBuildName != null) 
				_lblBuildName.Text = ActiveBuild != null ? $"{ActiveBuild.BuildingName} Lvl {ActiveBuild.TargetLevel}" : "SYSTEMS IDLE";
			
			if (_lblBuildTimer != null) 
				_lblBuildTimer.Text = ActiveBuild != null ? FormatTime(ActiveBuild.DurationLeft) : "COMPLETE";

			if (_lblFleetStatus != null) _lblFleetStatus.Text = ActiveFleetCount > 0 ? $"🚀 {ActiveFleetCount} FLEET OUTBOUND [{ActiveFleetTarget}]" : "NO ACTIVE FLEET MISSIONS";
			if (_lblFleetEta != null) _lblFleetEta.Text = ActiveFleetEta > 0 ? $"ETA: {FormatTime(ActiveFleetEta)}" : "DOCKED";
		}

		public void UpdateEngineeringDockUI()
		{
			if (_btnQueueToggle != null)
			{
				_btnQueueToggle.Text = BuildQueue.Count > 0 ? $"▼ {BuildQueue.Count} QUEUED UPGRADES" : "QUEUE EMPTY";
				_btnQueueToggle.Disabled = BuildQueue.Count == 0;
				if (BuildQueue.Count == 0) _isQueueDropdownOpen = false;
			}

			if (_queueDropdownPanel != null)
			{
				_queueDropdownPanel.Visible = _isQueueDropdownOpen;

				if (_isQueueDropdownOpen)
				{
					foreach (Node child in _queueListContainer.GetChildren()) child.QueueFree();

					for (int i = 0; i < BuildQueue.Count; i++)
					{
						var item = BuildQueue[i];
						Label lbl = new Label
						{
							Text = $"{i + 1}. {item.BuildingName} Lvl {item.TargetLevel}",
							Modulate = new Color("#94A3B8")
						};
						lbl.AddThemeFontSizeOverride("font_size", 8);
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
			topPanel.CustomMinimumSize = new Vector2(0, 56);
			topPanel.OffsetTop = 0;
			topPanel.OffsetBottom = 56;
			topPanel.MouseFilter = MouseFilterEnum.Ignore;

			StyleBoxFlat topPanelStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.03f, 0.05f, 0.08f, 0.94f),
				BorderColor = new Color(0f, 0.94f, 1f, 0.35f),
				BorderWidthBottom = 2
			};
			topPanel.AddThemeStyleboxOverride("panel", topPanelStyle);
			AddChild(topPanel);

			HBoxContainer masterBar = new HBoxContainer();
			masterBar.SetAnchorsPreset(LayoutPreset.FullRect);
			masterBar.OffsetLeft = 10;
			masterBar.OffsetRight = -10;
			masterBar.OffsetTop = 4;
			masterBar.OffsetBottom = -4;
			masterBar.MouseFilter = MouseFilterEnum.Ignore;
			masterBar.Alignment = BoxContainer.AlignmentMode.Center;
			masterBar.AddThemeConstantOverride("separation", 8);
			topPanel.AddChild(masterBar);

			// Profile Section
			HBoxContainer profileSection = new HBoxContainer();
			profileSection.MouseFilter = MouseFilterEnum.Ignore;
			profileSection.AddThemeConstantOverride("separation", 8);

			Button avatarBtn = new Button
			{
				CustomMinimumSize = new Vector2(44, 44),
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
				CornerRadiusTopLeft = 22,
				CornerRadiusTopRight = 22,
				CornerRadiusBottomLeft = 22,
				CornerRadiusBottomRight = 22
			};
			avatarBtn.AddThemeStyleboxOverride("normal", avatarStyle);

			Label avatarIco = new Label
			{
				Text = "👨‍🚀",
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
			avatarIco.SetAnchorsPreset(LayoutPreset.FullRect);
			avatarIco.AddThemeFontSizeOverride("font_size", 20);
			avatarBtn.AddChild(avatarIco);
			profileSection.AddChild(avatarBtn);

			VBoxContainer identityStack = new VBoxContainer();
			identityStack.Alignment = BoxContainer.AlignmentMode.Center;
			identityStack.MouseFilter = MouseFilterEnum.Ignore;
			identityStack.AddThemeConstantOverride("separation", 1);

			_lblCmdName = new Label { Text = CommanderName, Modulate = Colors.White };
			_lblCmdName.AddThemeFontSizeOverride("font_size", 11);
			identityStack.AddChild(_lblCmdName);

			_btnColonySwitch = new Button
			{
				Text = $"M:{CurrentMoon} ; B:{CurrentBaseSlot} ({CurrentBaseName}) ▼",
				CustomMinimumSize = new Vector2(165, 20),
				MouseFilter = MouseFilterEnum.Stop
			};
			_btnColonySwitch.AddThemeFontSizeOverride("font_size", 8);
			_btnColonySwitch.Modulate = new Color("#22C55E");
			_btnColonySwitch.Pressed += () => EmitSignal(SignalName.ColonySwitcherClicked);

			StyleBoxFlat switchStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.02f, 0.05f, 0.08f, 0.85f),
				BorderColor = new Color(0.13f, 0.77f, 0.36f, 0.4f),
				BorderWidthLeft = 1,
				BorderWidthRight = 1,
				BorderWidthTop = 1,
				BorderWidthBottom = 1,
				CornerRadiusTopLeft = 3,
				CornerRadiusTopRight = 3,
				CornerRadiusBottomLeft = 3,
				CornerRadiusBottomRight = 3
			};
			_btnColonySwitch.AddThemeStyleboxOverride("normal", switchStyle);
			identityStack.AddChild(_btnColonySwitch);

			profileSection.AddChild(identityStack);
			masterBar.AddChild(profileSection);

			Control spacerLeft = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
			masterBar.AddChild(spacerLeft);

			// Resource Capsules
			HBoxContainer resourceArray = new HBoxContainer();
			resourceArray.MouseFilter = MouseFilterEnum.Ignore;
			resourceArray.AddThemeConstantOverride("separation", 6);
			resourceArray.Alignment = BoxContainer.AlignmentMode.Center;

			resourceArray.AddChild(CreateResourceCapsule("⚡", "E", new Color("#D946EF"), out _lblValE));
			resourceArray.AddChild(CreateResourceCapsule("⛏️", "I", new Color("#06B6D4"), out _lblValI));
			resourceArray.AddChild(CreateResourceCapsule("💎", "T", new Color("#94A3B8"), out _lblValT));
			resourceArray.AddChild(CreateResourceCapsule("⛽", "H3", new Color("#EAB308"), out _lblValH3));
			resourceArray.AddChild(CreateGGoldCapsule("💰", out _lblValGGold));

			masterBar.AddChild(resourceArray);

			Control spacerRight = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
			masterBar.AddChild(spacerRight);

			// Layout Editor Toggle Button
			_btnEditLayoutToggle = new Button
			{
				Text = "🏗️ EDIT BASE",
				CustomMinimumSize = new Vector2(100, 42),
				MouseFilter = MouseFilterEnum.Stop,
				TooltipText = "Toggle Base Layout Editor Mode"
			};
			_btnEditLayoutToggle.AddThemeFontSizeOverride("font_size", 9);
			_btnEditLayoutToggle.Pressed += ToggleEditBaseLayout;

			StyleBoxFlat editStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.04f, 0.08f, 0.14f, 0.92f),
				BorderColor = new Color("#00F0FF"),
				BorderWidthLeft = 1,
				BorderWidthRight = 1,
				BorderWidthTop = 1,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 5,
				CornerRadiusTopRight = 5,
				CornerRadiusBottomLeft = 5,
				CornerRadiusBottomRight = 5
			};
			_btnEditLayoutToggle.AddThemeStyleboxOverride("normal", editStyle);
			masterBar.AddChild(_btnEditLayoutToggle);

			// Rank Badge
			Button rankBtn = new Button
			{
				CustomMinimumSize = new Vector2(72, 42),
				MouseFilter = MouseFilterEnum.Stop,
				TooltipText = "Galactic Leaderboard"
			};
			rankBtn.Pressed += () => EmitSignal(SignalName.LeaderboardClicked);

			StyleBoxFlat rankStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.08f, 0.06f, 0.02f, 0.92f),
				BorderColor = new Color("#FBBF24"),
				BorderWidthLeft = 1,
				BorderWidthRight = 1,
				BorderWidthTop = 1,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 5,
				CornerRadiusTopRight = 5,
				CornerRadiusBottomLeft = 5,
				CornerRadiusBottomRight = 5
			};
			rankBtn.AddThemeStyleboxOverride("normal", rankStyle);

			VBoxContainer rankContent = new VBoxContainer();
			rankContent.SetAnchorsPreset(LayoutPreset.FullRect);
			rankContent.Alignment = BoxContainer.AlignmentMode.Center;
			rankContent.MouseFilter = MouseFilterEnum.Ignore;
			rankContent.AddThemeConstantOverride("separation", -2);

			Label rankIco = new Label { Text = "🏆 RANK", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color("#FBBF24") };
			rankIco.AddThemeFontSizeOverride("font_size", 8);

			Label rankVal = new Label { Text = "#1 PRIME", HorizontalAlignment = HorizontalAlignment.Center, Modulate = Colors.White };
			rankVal.AddThemeFontSizeOverride("font_size", 9);

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
				_btnEditLayoutToggle.Text = "💾 SAVE BASE";
				_btnEditLayoutToggle.Modulate = new Color("#22C55E");
			}
			else
			{
				_btnEditLayoutToggle.Text = "🏗️ EDIT BASE";
				_btnEditLayoutToggle.Modulate = Colors.White;
				EmitSignal(SignalName.SaveLayoutRequested);
			}

			EmitSignal(SignalName.EditModeToggled, IsEditModeActive);
			GD.Print($"[BASE HUD] Layout Edit Mode: {IsEditModeActive}");
		}

		// =========================================================================
		// 2. TACTICAL SIDE DOCKS & QUEUE DROPDOWN
		// =========================================================================
		private void BuildTacticalSideDocks()
		{
			// Build Dock (Top-Right)
			Panel buildDock = new Panel();
			buildDock.SetAnchorsPreset(LayoutPreset.TopRight);
			buildDock.CustomMinimumSize = new Vector2(180, 52);
			buildDock.OffsetLeft = -190;
			buildDock.OffsetRight = -10;
			buildDock.OffsetTop = 64;
			buildDock.OffsetBottom = 116;
			buildDock.MouseFilter = MouseFilterEnum.Ignore;

			StyleBoxFlat dockStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.03f, 0.05f, 0.08f, 0.88f),
				BorderColor = new Color("#F59E0B"),
				BorderWidthLeft = 1,
				BorderWidthRight = 1,
				BorderWidthTop = 1,
				BorderWidthBottom = 1,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6
			};
			buildDock.AddThemeStyleboxOverride("panel", dockStyle);
			AddChild(buildDock);

			VBoxContainer buildBox = new VBoxContainer();
			buildBox.SetAnchorsPreset(LayoutPreset.FullRect);
			buildBox.OffsetLeft = 8;
			buildBox.OffsetRight = -8;
			buildBox.OffsetTop = 4;
			buildBox.OffsetBottom = -4;
			buildBox.MouseFilter = MouseFilterEnum.Ignore;
			buildBox.AddThemeConstantOverride("separation", 0);

			Label dockTitle = new Label { Text = "🛠️ ENGINEERING DOCK", Modulate = new Color("#F59E0B") };
			dockTitle.AddThemeFontSizeOverride("font_size", 8);
			buildBox.AddChild(dockTitle);

			HBoxContainer activeRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
			_lblBuildName = new Label { Text = "SYSTEMS IDLE", Modulate = Colors.White, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			_lblBuildName.AddThemeFontSizeOverride("font_size", 9);
			_lblBuildTimer = new Label { Text = "COMPLETE", Modulate = new Color("#22C55E") };
			_lblBuildTimer.AddThemeFontSizeOverride("font_size", 9);
			activeRow.AddChild(_lblBuildName);
			activeRow.AddChild(_lblBuildTimer);
			buildBox.AddChild(activeRow);

			HBoxContainer subRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
			
			_btnQueueToggle = new Button { Text = "QUEUE EMPTY", SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Stop, Disabled = true };
			_btnQueueToggle.AddThemeFontSizeOverride("font_size", 8);
			_btnQueueToggle.Modulate = new Color("#94A3B8");
			_btnQueueToggle.Pressed += () => 
			{
				_isQueueDropdownOpen = !_isQueueDropdownOpen;
				UpdateEngineeringDockUI();
			};

			Button speedupBtn = new Button { Text = "⚡ SPEEDUP", CustomMinimumSize = new Vector2(50, 14), MouseFilter = MouseFilterEnum.Stop };
			speedupBtn.AddThemeFontSizeOverride("font_size", 7);
			speedupBtn.Modulate = new Color("#FBBF24");
			speedupBtn.Pressed += () => EmitSignal(SignalName.SpeedupClicked);

			subRow.AddChild(_btnQueueToggle);
			subRow.AddChild(speedupBtn);
			buildBox.AddChild(subRow);
			buildDock.AddChild(buildBox);

			// Floating Queue Dropdown Panel
			_queueDropdownPanel = new Panel();
			_queueDropdownPanel.SetAnchorsPreset(LayoutPreset.TopRight);
			_queueDropdownPanel.CustomMinimumSize = new Vector2(180, 120);
			_queueDropdownPanel.OffsetLeft = -190;
			_queueDropdownPanel.OffsetRight = -10;
			_queueDropdownPanel.OffsetTop = 120; 
			_queueDropdownPanel.OffsetBottom = 240;
			_queueDropdownPanel.Visible = false;

			StyleBoxFlat dropStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.02f, 0.04f, 0.06f, 0.95f),
				BorderColor = new Color("#F59E0B"),
				BorderWidthLeft = 1,
				BorderWidthRight = 1,
				BorderWidthTop = 0,
				BorderWidthBottom = 1,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6
			};
			_queueDropdownPanel.AddThemeStyleboxOverride("panel", dropStyle);
			AddChild(_queueDropdownPanel);

			VBoxContainer dropBox = new VBoxContainer();
			dropBox.SetAnchorsPreset(LayoutPreset.FullRect);
			dropBox.OffsetLeft = 8;
			dropBox.OffsetRight = -8;
			dropBox.OffsetTop = 4;
			dropBox.OffsetBottom = -4;
			_queueDropdownPanel.AddChild(dropBox);

			ScrollContainer scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
			_queueListContainer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			scroll.AddChild(_queueListContainer);
			dropBox.AddChild(scroll);

			_btnCancelLast = new Button { Text = "❌ CANCEL LAST (REFUNDS 25 GGOLD + 50% RES)", CustomMinimumSize = new Vector2(0, 24) };
			_btnCancelLast.AddThemeFontSizeOverride("font_size", 7);
			_btnCancelLast.Modulate = new Color("#EF4444");
			_btnCancelLast.Pressed += CancelLastQueuedBuild;
			dropBox.AddChild(_btnCancelLast);

			// Fleet Dock (Top-Left)
			Panel fleetDock = new Panel();
			fleetDock.SetAnchorsPreset(LayoutPreset.TopLeft);
			fleetDock.CustomMinimumSize = new Vector2(210, 36);
			fleetDock.OffsetLeft = 10;
			fleetDock.OffsetRight = 220;
			fleetDock.OffsetTop = 64;
			fleetDock.OffsetBottom = 100;
			fleetDock.MouseFilter = MouseFilterEnum.Ignore;

			StyleBoxFlat fleetStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.03f, 0.05f, 0.08f, 0.88f),
				BorderColor = new Color("#3B82F6"),
				BorderWidthLeft = 1,
				BorderWidthRight = 1,
				BorderWidthTop = 1,
				BorderWidthBottom = 1,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6
			};
			fleetDock.AddThemeStyleboxOverride("panel", fleetStyle);
			AddChild(fleetDock);

			VBoxContainer fleetBox = new VBoxContainer();
			fleetBox.SetAnchorsPreset(LayoutPreset.FullRect);
			fleetBox.OffsetLeft = 8;
			fleetBox.OffsetRight = -8;
			fleetBox.OffsetTop = 3;
			fleetBox.OffsetBottom = -3;
			fleetBox.MouseFilter = MouseFilterEnum.Ignore;
			fleetBox.AddThemeConstantOverride("separation", -1);

			_lblFleetStatus = new Label { Text = $"🚀 {ActiveFleetCount} FLEET OUTBOUND [{ActiveFleetTarget}]", Modulate = new Color("#3B82F6") };
			_lblFleetStatus.AddThemeFontSizeOverride("font_size", 8);

			_lblFleetEta = new Label { Text = $"ETA: {FormatTime(ActiveFleetEta)}", Modulate = new Color("#22C55E") };
			_lblFleetEta.AddThemeFontSizeOverride("font_size", 9);

			fleetBox.AddChild(_lblFleetStatus);
			fleetBox.AddChild(_lblFleetEta);
			fleetDock.AddChild(fleetBox);
		}

		// =========================================================================
		// 3. BOTTOM COMMAND NAVIGATION DOCK (Absolute Viewport Anchor)
		// =========================================================================
		private void BuildBottomCommandNavigationDock()
		{
			Vector2 vpSize = GetViewportRect().Size;

			_bottomPanelNode = new Panel();
			_bottomPanelNode.Name = "BottomNavigationDockPanel";
			_bottomPanelNode.Position = new Vector2(0, vpSize.Y - 60);
			_bottomPanelNode.Size = new Vector2(vpSize.X, 60);
			_bottomPanelNode.CustomMinimumSize = new Vector2(0, 60);
			_bottomPanelNode.MouseFilter = MouseFilterEnum.Ignore;

			StyleBoxFlat bStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.03f, 0.05f, 0.08f, 0.96f),
				BorderColor = new Color(0f, 0.94f, 1f, 0.5f),
				BorderWidthTop = 2
			};
			_bottomPanelNode.AddThemeStyleboxOverride("panel", bStyle);
			AddChild(_bottomPanelNode);

			HBoxContainer navRow = new HBoxContainer();
			navRow.SetAnchorsPreset(LayoutPreset.FullRect);
			navRow.OffsetLeft = 20;
			navRow.OffsetRight = -20;
			navRow.OffsetTop = 6;
			navRow.OffsetBottom = -6;
			navRow.MouseFilter = MouseFilterEnum.Ignore;
			navRow.Alignment = BoxContainer.AlignmentMode.Center;
			navRow.AddThemeConstantOverride("separation", 14);
			_bottomPanelNode.AddChild(navRow);

			navRow.AddChild(CreateNavTab("🌕", "BASE", "BASE"));
			navRow.AddChild(CreateNavTab("🪐", "MOON SLOTS", "MOON"));
			navRow.AddChild(CreateNavTab("🌌", "GALAXY MAP", "GALAXY"));
			navRow.AddChild(CreateNavTab("🛡️", "ALLIANCE", "ALLIANCE"));
			navRow.AddChild(CreateNavTab("✉️", "COMMS", "COMMS"));
		}

		private Button CreateNavTab(string icon, string title, string targetTier)
		{
			Button tabBtn = new Button
			{
				CustomMinimumSize = new Vector2(115, 46),
				MouseFilter = MouseFilterEnum.Stop
			};

			StyleBoxFlat tabStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.06f, 0.09f, 0.14f, 0.92f),
				BorderColor = new Color(0f, 0.94f, 1f, 0.6f),
				BorderWidthLeft = 1,
				BorderWidthRight = 1,
				BorderWidthTop = 1,
				BorderWidthBottom = 1,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6
			};
			tabBtn.AddThemeStyleboxOverride("normal", tabStyle);

			VBoxContainer stack = new VBoxContainer();
			stack.SetAnchorsPreset(LayoutPreset.FullRect);
			stack.Alignment = BoxContainer.AlignmentMode.Center;
			stack.MouseFilter = MouseFilterEnum.Ignore;
			stack.AddThemeConstantOverride("separation", -1);

			Label ico = new Label { Text = icon, HorizontalAlignment = HorizontalAlignment.Center };
			ico.AddThemeFontSizeOverride("font_size", 14);

			Label lbl = new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color("#00F0FF") };
			lbl.AddThemeFontSizeOverride("font_size", 9);

			stack.AddChild(ico);
			stack.AddChild(lbl);
			tabBtn.AddChild(stack);

			tabBtn.Pressed += () => OnNavTabPressed(targetTier);
			return tabBtn;
		}

		private void OnNavTabPressed(string targetTier)
		{
			GD.Print($"[BASE HUD] Navigation Tab Clicked: {targetTier}");
			EmitSignal(SignalName.NavTierSelected, targetTier);

			if (targetTier == "BASE")
			{
				GetTree().ChangeSceneToFile("res://Scenes/base_view.tscn");
			}
			else if (targetTier == "MOON")
			{
				GetTree().ChangeSceneToFile("res://Scenes/MoonView.tscn");
			}
			else if (targetTier == "GALAXY")
			{
				GetTree().ChangeSceneToFile("res://Scenes/GalaxyView.tscn");
			}
		}

		private Button CreateResourceCapsule(string icon, string resCode, Color color, out Label valLabel)
		{
			Button capsuleBtn = new Button
			{
				CustomMinimumSize = new Vector2(92, 30),
				MouseFilter = MouseFilterEnum.Stop,
				TooltipText = $"{resCode} Resources"
			};
			capsuleBtn.Pressed += () => EmitSignal(SignalName.ResourceClicked, resCode);

			StyleBoxFlat capStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.04f, 0.06f, 0.09f, 0.88f),
				BorderColor = new Color(color.R, color.G, color.B, 0.45f),
				BorderWidthLeft = 1,
				BorderWidthRight = 1,
				BorderWidthTop = 1,
				BorderWidthBottom = 1,
				CornerRadiusTopLeft = 15,
				CornerRadiusTopRight = 15,
				CornerRadiusBottomLeft = 15,
				CornerRadiusBottomRight = 15
			};
			capsuleBtn.AddThemeStyleboxOverride("normal", capStyle);

			HBoxContainer row = new HBoxContainer();
			row.SetAnchorsPreset(LayoutPreset.FullRect);
			row.Alignment = BoxContainer.AlignmentMode.Center;
			row.MouseFilter = MouseFilterEnum.Ignore;
			row.AddThemeConstantOverride("separation", 4);

			Label ico = new Label { Text = icon };
			ico.AddThemeFontSizeOverride("font_size", 10);

			valLabel = new Label { Text = "0", Modulate = Colors.White };
			valLabel.AddThemeFontSizeOverride("font_size", 9);

			row.AddChild(ico);
			row.AddChild(valLabel);
			capsuleBtn.AddChild(row);
			return capsuleBtn;
		}

		private Button CreateGGoldCapsule(string icon, out Label valLabel)
		{
			Button capsuleBtn = new Button
			{
				CustomMinimumSize = new Vector2(88, 30),
				MouseFilter = MouseFilterEnum.Stop,
				TooltipText = "Galaxy Gold Treasury"
			};
			capsuleBtn.Pressed += () => EmitSignal(SignalName.ResourceClicked, "GGD");

			StyleBoxFlat goldStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.08f, 0.06f, 0.02f, 0.90f),
				BorderColor = new Color("#FBBF24"),
				BorderWidthLeft = 1,
				BorderWidthRight = 1,
				BorderWidthTop = 1,
				BorderWidthBottom = 1,
				CornerRadiusTopLeft = 15,
				CornerRadiusTopRight = 15,
				CornerRadiusBottomLeft = 15,
				CornerRadiusBottomRight = 15
			};
			capsuleBtn.AddThemeStyleboxOverride("normal", goldStyle);

			HBoxContainer row = new HBoxContainer();
			row.SetAnchorsPreset(LayoutPreset.FullRect);
			row.Alignment = BoxContainer.AlignmentMode.Center;
			row.MouseFilter = MouseFilterEnum.Ignore;
			row.AddThemeConstantOverride("separation", 3);

			Label ico = new Label { Text = icon };
			ico.AddThemeFontSizeOverride("font_size", 10);

			valLabel = new Label { Text = "0", Modulate = new Color("#FBBF24") };
			valLabel.AddThemeFontSizeOverride("font_size", 9);

			Label plusIco = new Label { Text = "+", Modulate = new Color("#22C55E") };
			plusIco.AddThemeFontSizeOverride("font_size", 10);

			row.AddChild(ico);
			row.AddChild(valLabel);
			row.AddChild(plusIco);
			capsuleBtn.AddChild(row);
			return capsuleBtn;
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
