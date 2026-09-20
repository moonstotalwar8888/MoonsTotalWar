using System;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: CLASH OF KINGS COMMAND COCKPIT HUD (v8.0 Base Layout Edition)
	/// - Top Bar: Commander Profile, Colony Transporter, 5 Resource Capsules, Rank Trophy.
	/// - Edit Base Layout Toggle: [🏗️ EDIT BASE] / [💾 SAVE LAYOUT] controls.
	/// - Side Docks: Engineering Construction Monitor, Fleet Operations Radar.
	/// - Bottom Bar: Command Navigation Dock (Base, Moon Slots, Galaxy Map, Alliance, Comms).
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

		// Colony Data
		public string CommanderName = "COMMANDER ALPHA";
		public string CurrentBaseName = "Alpha Outpost";
		public int CurrentMoon = 100;
		public int CurrentBaseSlot = 1;

		public double ResE = 50000;
		public double ResI = 50000;
		public double ResT = 50000;
		public double ResH3 = 25000;
		public long MGold = 100;
		public long SiloCap = 50000;
		public float ServerSpeed = 1.0f;

		// Side Docks Data
		public string ActiveBuildName = "Industrial Core Lvl 2";
		public double ActiveBuildTimeRemaining = 145.0;
		public int QueuedBuildCount = 2;
		public int ActiveFleetCount = 1;
		public string ActiveFleetTarget = "M-100:B-04";
		public double ActiveFleetEta = 320.0;

		// Edit Mode State
		public bool IsEditModeActive { get; private set; } = false;

		// UI Node References
		private Panel _bottomPanelNode;
		private Label _lblCmdName;
		private Button _btnColonySwitch;
		private Label _lblValE, _lblValI, _lblValT, _lblValH3, _lblValMGold;
		private Label _lblBuildName, _lblBuildTimer, _lblQueuedCount;
		private Label _lblFleetStatus, _lblFleetEta;
		private Button _btnEditLayoutToggle;

		private double _heartbeatTimer = 0.0;

		public override void _Ready()
		{
			Instance = this;

			SetAnchorsPreset(LayoutPreset.FullRect);
			MouseFilter = MouseFilterEnum.Ignore;

			GetViewport().Connect("size_changed", Callable.From(RecalculateLayoutBounds));

			BuildTopCockpitBar();
			BuildTacticalSideDocks();
			BuildBottomCommandNavigationDock();

			UpdateHUDDisplay();
			RecalculateLayoutBounds();
		}

		public override void _Process(double delta)
		{
			_heartbeatTimer += delta;

			if (ActiveBuildTimeRemaining > 0)
			{
				ActiveBuildTimeRemaining = Math.Max(0, ActiveBuildTimeRemaining - delta);
			}

			if (ActiveFleetEta > 0)
			{
				ActiveFleetEta = Math.Max(0, ActiveFleetEta - delta);
			}

			if (_heartbeatTimer >= 1.0)
			{
				_heartbeatTimer = 0.0;
				TickResourceHeartbeat();
			}

			UpdateTimersDisplay();
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
			double pE = (3 * 210 * ServerSpeed) / 3600.0;
			double pI = (3 * 210 * ServerSpeed) / 3600.0;
			double pT = (3 * 210 * ServerSpeed) / 3600.0;
			double pH3 = (5 * 450 * ServerSpeed) / 3600.0;

			ResE = Math.Min(SiloCap, ResE + pE);
			ResI = Math.Min(SiloCap, ResI + pI);
			ResT = Math.Min(SiloCap, ResT + pT);
			ResH3 = Math.Min(SiloCap, ResH3 + pH3);

			UpdateHUDDisplay();
		}

		public void UpdateHUDDisplay()
		{
			if (_lblCmdName != null) _lblCmdName.Text = CommanderName;
			if (_btnColonySwitch != null) _btnColonySwitch.Text = $"M:{CurrentMoon} ; B:{CurrentBaseSlot} ({CurrentBaseName}) ▼";

			if (_lblValE != null) _lblValE.Text = $"{(long)ResE:N0}";
			if (_lblValI != null) _lblValI.Text = $"{(long)ResI:N0}";
			if (_lblValT != null) _lblValT.Text = $"{(long)ResT:N0}";
			if (_lblValH3 != null) _lblValH3.Text = $"{(long)ResH3:N0}";
			if (_lblValMGold != null) _lblValMGold.Text = $"{MGold:N0}";
		}

		private void UpdateTimersDisplay()
		{
			if (_lblBuildName != null) _lblBuildName.Text = string.IsNullOrEmpty(ActiveBuildName) ? "SYSTEMS IDLE" : ActiveBuildName;
			if (_lblBuildTimer != null) _lblBuildTimer.Text = ActiveBuildTimeRemaining > 0 ? FormatTime(ActiveBuildTimeRemaining) : "COMPLETE";
			if (_lblQueuedCount != null) _lblQueuedCount.Text = QueuedBuildCount > 0 ? $"+{QueuedBuildCount} QUEUED" : "QUEUE EMPTY";

			if (_lblFleetStatus != null) _lblFleetStatus.Text = ActiveFleetCount > 0 ? $"🚀 {ActiveFleetCount} FLEET OUTBOUND [{ActiveFleetTarget}]" : "NO ACTIVE FLEET MISSIONS";
			if (_lblFleetEta != null) _lblFleetEta.Text = ActiveFleetEta > 0 ? $"ETA: {FormatTime(ActiveFleetEta)}" : "DOCKED";
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
			resourceArray.AddChild(CreateMGoldCapsule("💰", out _lblValMGold));

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
		// 2. TACTICAL SIDE DOCKS
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
			_lblBuildName = new Label { Text = ActiveBuildName, Modulate = Colors.White, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			_lblBuildName.AddThemeFontSizeOverride("font_size", 9);
			_lblBuildTimer = new Label { Text = FormatTime(ActiveBuildTimeRemaining), Modulate = new Color("#22C55E") };
			_lblBuildTimer.AddThemeFontSizeOverride("font_size", 9);
			activeRow.AddChild(_lblBuildName);
			activeRow.AddChild(_lblBuildTimer);
			buildBox.AddChild(activeRow);

			HBoxContainer subRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
			_lblQueuedCount = new Label { Text = $"+{QueuedBuildCount} QUEUED", Modulate = new Color("#94A3B8"), SizeFlagsHorizontal = SizeFlags.ExpandFill };
			_lblQueuedCount.AddThemeFontSizeOverride("font_size", 8);

			Button speedupBtn = new Button { Text = "⚡ SPEEDUP", CustomMinimumSize = new Vector2(50, 14), MouseFilter = MouseFilterEnum.Stop };
			speedupBtn.AddThemeFontSizeOverride("font_size", 7);
			speedupBtn.Modulate = new Color("#FBBF24");
			speedupBtn.Pressed += () => EmitSignal(SignalName.SpeedupClicked);

			subRow.AddChild(_lblQueuedCount);
			subRow.AddChild(speedupBtn);
			buildBox.AddChild(subRow);
			buildDock.AddChild(buildBox);

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

			valLabel = new Label { Text = "50,000", Modulate = Colors.White };
			valLabel.AddThemeFontSizeOverride("font_size", 9);

			row.AddChild(ico);
			row.AddChild(valLabel);
			capsuleBtn.AddChild(row);
			return capsuleBtn;
		}

		private Button CreateMGoldCapsule(string icon, out Label valLabel)
		{
			Button capsuleBtn = new Button
			{
				CustomMinimumSize = new Vector2(88, 30),
				MouseFilter = MouseFilterEnum.Stop,
				TooltipText = "Moongold Treasury"
			};
			capsuleBtn.Pressed += () => EmitSignal(SignalName.ResourceClicked, "MGD");

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

			valLabel = new Label { Text = "100", Modulate = new Color("#FBBF24") };
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
