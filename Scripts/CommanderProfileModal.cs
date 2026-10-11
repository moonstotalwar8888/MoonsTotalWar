using System;
using System.Collections.Generic;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: UNIVERSAL COMMANDER PROFILE, PLANETARY BIO & LEADERBOARD MODAL
	/// - Opens from Top-Left Avatar (My Commander Profile & Rename / Medals / Colony Config)
	/// - Opens from Top-Left Colony Switcher (Empire Registry & Sector Jump)
	/// - Opens from Top-Right Rank Trophy (Year 2148 Clock, Aether-Reaper Countdown, & Live Leaderboard)
	/// - Opens from MoonView / GalaxyView when clicking ANY occupied Player Outpost (Planetary Target Bio & Dossier)
	/// </summary>
	public partial class CommanderProfileModal : Control
	{
		private ColorRect _backdrop;
		private Panel _chassis;
		private VBoxContainer _rootBox;
		private Label _lblModalHeader;
		private Label _lblSubHeader;
		private ScrollContainer _scroll;
		private VBoxContainer _contentStack;

		// Input fields for Rename & Comms
		private LineEdit _renameCommanderInput;
		private LineEdit _renameBaseInput;
		private LineEdit _signalMessageInput;
		private Label _statusFeedbackLabel;

		private List<SupabaseService.GalacticCommanderEntry> _cachedLeaderboard = new List<SupabaseService.GalacticCommanderEntry>();

		public override void _Ready()
		{
			SetAnchorsPreset(LayoutPreset.FullRect);
			MouseFilter = MouseFilterEnum.Ignore;
			ZIndex = 300;

			GetViewport().Connect("size_changed", Callable.From(RecenterModal));
			BuildFrame();
			Visible = false;
		}

		private void RecenterModal()
		{
			if (_chassis == null) return;
			Vector2 vp = GetViewportRect().Size;
			if (vp.X <= 0 || vp.Y <= 0) return;

			// Wide Command Console framing:
			// - Top Cockpit Bar (Y: 0..68) remains permanently visible -> topSafeY = 74
			// - Bottom Nav Dock (Y: vp.Y - 72..vp.Y) -> bottomSafeY = vp.Y - 78
			// - Side docks auto-hide while any primary modal is open, giving up to 1100px width!
			float topSafeY = 74f;
			float bottomSafeY = Math.Max(topSafeY + 340f, vp.Y - 78f);
			float maxSafeH = bottomSafeY - topSafeY;

			float desiredMaxW = 1100f;
			float w = Mathf.Min(desiredMaxW, Math.Max(480f, vp.X * 0.90f));
			float x = (vp.X - w) * 0.5f;

			float h = maxSafeH;
			float y = topSafeY + (maxSafeH - h) * 0.5f;

			_chassis.AnchorLeft = 0f;
			_chassis.AnchorTop = 0f;
			_chassis.AnchorRight = 0f;
			_chassis.AnchorBottom = 0f;
			_chassis.CustomMinimumSize = new Vector2(w, h);
			_chassis.Size = new Vector2(w, h);
			_chassis.Position = new Vector2(x, y);

			if (_rootBox != null)
			{
				float padX = 16f;
				float padY = 12f;
				float innerW = Math.Max(320f, w - padX * 2f);
				float innerH = Math.Max(260f, h - padY * 2f);

				_rootBox.AnchorLeft = 0f;
				_rootBox.AnchorTop = 0f;
				_rootBox.AnchorRight = 0f;
				_rootBox.AnchorBottom = 0f;
				_rootBox.GrowHorizontal = GrowDirection.End;
				_rootBox.GrowVertical = GrowDirection.End;
				_rootBox.Position = new Vector2(padX, padY);
				_rootBox.CustomMinimumSize = new Vector2(innerW, innerH);
				_rootBox.Size = new Vector2(innerW, innerH);

				if (_scroll != null)
				{
					_scroll.CustomMinimumSize = new Vector2(innerW, 0);
					_scroll.Size = new Vector2(innerW, _scroll.Size.Y);
				}
				if (_contentStack != null)
				{
					float stackW = Math.Max(260f, innerW - 14f);
					_contentStack.CustomMinimumSize = new Vector2(stackW, 0);
					_contentStack.Size = new Vector2(stackW, _contentStack.Size.Y);
				}
			}
		}

		private void BuildFrame()
		{
			for (int i = GetChildCount() - 1; i >= 0; i--)
			{
				Node c = GetChild(i);
				RemoveChild(c);
				c.QueueFree();
			}

			_backdrop = new ColorRect
			{
				Color = new Color(0f, 0f, 0f, 0.72f),
				MouseFilter = MouseFilterEnum.Stop
			};
			_backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
			_backdrop.GuiInput += (InputEvent @event) =>
			{
				if (@event is InputEventMouseButton mb && mb.IsPressed() && mb.ButtonIndex == MouseButton.Left)
				{
					CloseModal();
				}
			};
			AddChild(_backdrop);

			_chassis = new Panel
			{
				CustomMinimumSize = new Vector2(650, 560),
				ClipContents = true,
				MouseFilter = MouseFilterEnum.Stop
			};
			StyleBoxFlat cStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.05f, 0.11f, 0.21f, 0.99f),
				BorderColor = new Color("#00F0FF"),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 10,
				CornerRadiusTopRight = 10,
				CornerRadiusBottomLeft = 10,
				CornerRadiusBottomRight = 10,
				ShadowColor = new Color(0f, 0.94f, 1f, 0.30f),
				ShadowSize = 22
			};
			_chassis.AddThemeStyleboxOverride("panel", cStyle);
			AddChild(_chassis);

			_rootBox = new VBoxContainer
			{
				GrowHorizontal = GrowDirection.End,
				GrowVertical = GrowDirection.End,
				ClipContents = true
			};
			_rootBox.AddThemeConstantOverride("separation", 8);
			_chassis.AddChild(_rootBox);

			HBoxContainer headerRow = new HBoxContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			headerRow.Alignment = BoxContainer.AlignmentMode.Center;
			headerRow.AddThemeConstantOverride("separation", 10);

			VBoxContainer titleCol = new VBoxContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClipContents = true
			};
			titleCol.AddThemeConstantOverride("separation", 2);

			_lblModalHeader = new Label
			{
				Text = "COMMANDER PROFILE DOSSIER",
				Modulate = new Color("#00F0FF"),
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			_lblModalHeader.AddThemeFontSizeOverride("font_size", 20);
			_lblModalHeader.AddThemeColorOverride("font_outline_color", Colors.Black);
			_lblModalHeader.AddThemeConstantOverride("outline_size", 3);
			titleCol.AddChild(_lblModalHeader);

			_lblSubHeader = new Label
			{
				Text = "CENTRAL COMMAND · ENCRYPTED GALACTIC REGISTRY",
				Modulate = new Color("#E2E8F0"),
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			_lblSubHeader.AddThemeFontSizeOverride("font_size", 14);
			titleCol.AddChild(_lblSubHeader);

			headerRow.AddChild(titleCol);

			Button closeBtn = new Button
			{
				Text = " ✕ CLOSE ",
				CustomMinimumSize = new Vector2(96, 38),
				SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
				MouseFilter = MouseFilterEnum.Stop
			};
			closeBtn.AddThemeFontSizeOverride("font_size", 15);
			StyleBoxFlat closeStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.25f, 0.06f, 0.08f, 0.95f),
				BorderColor = new Color("#EF4444")
			};
			closeStyle.SetBorderWidthAll(2);
			closeStyle.SetCornerRadiusAll(8);
			closeBtn.AddThemeStyleboxOverride("normal", closeStyle);
			closeBtn.AddThemeStyleboxOverride("hover", closeStyle);
			closeBtn.AddThemeStyleboxOverride("pressed", closeStyle);
			closeBtn.Pressed += CloseModal;
			headerRow.AddChild(closeBtn);

			_rootBox.AddChild(headerRow);
			_rootBox.AddChild(new HSeparator());

			_scroll = new ScrollContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill,
				HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
				ClipContents = true
			};
			_rootBox.AddChild(_scroll);

			MarginContainer scrollPad = new MarginContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};
			scrollPad.AddThemeConstantOverride("margin_right", 8);
			scrollPad.AddThemeConstantOverride("margin_bottom", 6);
			_scroll.AddChild(scrollPad);

			_contentStack = new VBoxContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};
			_contentStack.AddThemeConstantOverride("separation", 10);
			scrollPad.AddChild(_contentStack);

			RecenterModal();
		}

		private void ClearContent()
		{
			if (_contentStack == null || !IsInstanceValid(_contentStack)) return;
			for (int i = _contentStack.GetChildCount() - 1; i >= 0; i--)
			{
				Node child = _contentStack.GetChild(i);
				_contentStack.RemoveChild(child);
				child.QueueFree();
			}
		}

		// =========================================================================
		// MODE 1: OWN COMMANDER PROFILE & IDENTITY RESET (Top-Left Avatar Button)
		// =========================================================================
		private int _profileViewVersion = 0;

		public async void OpenOwnCommanderProfile()
		{
			int myVersion = ++_profileViewVersion;
			ClearContent();
			RecenterModal();
			Visible = true;
			BaseHUD.Instance?.SyncSideDocksVisibilityForActiveModals();

			var srvDef = GameData.GetServerDef(SupabaseService.CachedServerId);
			_lblModalHeader.Text = "👨‍🚀  COMMANDER PROFILE & DOSSIER";
			_lblModalHeader.Modulate = new Color("#00F0FF");
			_lblSubHeader.Text = $"ACTIVE SERVER: {srvDef.Name} ({srvDef.Id} · {srvDef.Speed:0.#}X)  ·  SECTOR: {srvDef.Id}:M-{SupabaseService.CachedMoonId}:B-{SupabaseService.CachedBaseSlot:D2}";

			if (SupabaseService.Instance != null)
			{
				_cachedLeaderboard = await SupabaseService.Instance.FetchAllColoniesAsync();
			}

			if (!IsInstanceValid(this) || !Visible || myVersion != _profileViewVersion) return;
			ClearContent();
			RecenterModal();

			int myRank = 1;
			long myPop = BaseHUD.Instance != null
				? SupabaseService.CalculateColonyPopulation(BaseHUD.Instance.BuildingLevels)
				: 700;

			for (int i = 0; i < _cachedLeaderboard.Count; i++)
			{
				if (_cachedLeaderboard[i].IsMe)
				{
					myRank = i + 1;
					myPop = _cachedLeaderboard[i].Population;
					break;
				}
			}

			// 1. Identity & Standing Card
			PanelContainer idCard = CreateStyledCard(new Color("#00F0FF"));
			VBoxContainer idBox = CreateCardVBox(idCard);

			HBoxContainer topIdRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
			VBoxContainer callsignCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
			callsignCol.AddThemeConstantOverride("separation", 2);
			callsignCol.AddChild(MakeLabel("REGISTERED COMMANDER CALLSIGN:", 12, new Color("#94A3B8")));
			callsignCol.AddChild(MakeLabel(SupabaseService.CachedCommanderName.ToUpper(), 24, Colors.White));
			topIdRow.AddChild(callsignCol);

			PanelContainer rankBadge = CreateMiniStatBox("GALACTIC RANK", $"#{myRank}", new Color("#FBBF24"));
			rankBadge.CustomMinimumSize = new Vector2(130, 54);
			topIdRow.AddChild(rankBadge);
			idBox.AddChild(topIdRow);

			HBoxContainer statsRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
			statsRow.AddThemeConstantOverride("separation", 8);
			statsRow.AddChild(CreateMiniStatBox("EMPIRE POPULATION", $"{myPop:N0}", new Color("#4ADE80")));
			statsRow.AddChild(CreateMiniStatBox("HOME COORDINATE", $"M-{SupabaseService.CachedMoonId} : B-{SupabaseService.CachedBaseSlot:D2}", new Color("#00F0FF")));
			statsRow.AddChild(CreateMiniStatBox("ALLIANCE", SupabaseService.CachedAllianceName.ToUpper(), new Color("#38BDF8")));
			idBox.AddChild(statsRow);

			_contentStack.AddChild(idCard);

			// 2. Medal Hall (Galactic Decorations)
			PanelContainer medalCard = CreateStyledCard(new Color("#FBBF24"));
			VBoxContainer medalBox = CreateCardVBox(medalCard);
			medalBox.AddChild(MakeLabel("🏅  GALACTIC DECORATIONS & MEDAL HALL", 14, new Color("#FBBF24")));

			int coreLvl = BaseHUD.Instance != null ? BaseHUD.Instance.BuildingLevels.GetValueOrDefault("hub_cmd", 1) : 1;
			HBoxContainer medalRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
			medalRow.AddThemeConstantOverride("separation", 8);
			medalRow.AddChild(CreateMiniStatBox("POPULATION", $"🏅 {(coreLvl >= 5 ? 1 : 0)}", new Color("#FBBF24")));
			medalRow.AddChild(CreateMiniStatBox("RAIDER", "🏅 0", new Color("#FBBF24")));
			medalRow.AddChild(CreateMiniStatBox("ATTACKER", "🏅 0", new Color("#FBBF24")));
			medalRow.AddChild(CreateMiniStatBox("DEFENDER", "🏅 0", new Color("#FBBF24")));
			medalBox.AddChild(medalRow);
			_contentStack.AddChild(medalCard);

			// 3. Rename Commander Callsign (100 GGold) & Rename Colony Base (Free)
			PanelContainer configCard = CreateStyledCard(new Color("#22C55E"));
			VBoxContainer configBox = CreateCardVBox(configCard);
			configBox.AddChild(MakeLabel("⚙  IDENTITY & COLONY DESIGNATION CONTROLS", 14, new Color("#4ADE80")));

			// Commander Rename Row
			HBoxContainer cmdRenameRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
			cmdRenameRow.AddThemeConstantOverride("separation", 8);
			_renameCommanderInput = new LineEdit
			{
				Text = SupabaseService.CachedCommanderName,
				PlaceholderText = "New Commander Callsign...",
				CustomMinimumSize = new Vector2(140, 40),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_renameCommanderInput.AddThemeFontSizeOverride("font_size", 14);
			cmdRenameRow.AddChild(_renameCommanderInput);

			Button btnRenameCmd = new Button
			{
				Text = "RENAME CALLSIGN (100 GGOLD)",
				CustomMinimumSize = new Vector2(210, 40)
			};
			btnRenameCmd.AddThemeFontSizeOverride("font_size", 12);
			btnRenameCmd.Modulate = new Color("#FBBF24");
			btnRenameCmd.Pressed += OnRenameCommanderPressed;
			cmdRenameRow.AddChild(btnRenameCmd);
			configBox.AddChild(cmdRenameRow);

			// Sector Base Rename Row
			HBoxContainer baseRenameRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
			baseRenameRow.AddThemeConstantOverride("separation", 8);
			_renameBaseInput = new LineEdit
			{
				Text = SupabaseService.CachedBaseName,
				PlaceholderText = "New Colony Base Name...",
				CustomMinimumSize = new Vector2(140, 40),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_renameBaseInput.AddThemeFontSizeOverride("font_size", 14);
			baseRenameRow.AddChild(_renameBaseInput);

			Button btnRenameBase = new Button
			{
				Text = "RENAME COLONY (FREE)",
				CustomMinimumSize = new Vector2(210, 40)
			};
			btnRenameBase.AddThemeFontSizeOverride("font_size", 12);
			btnRenameBase.Modulate = new Color("#4ADE80");
			btnRenameBase.Pressed += OnRenameColonyPressed;
			baseRenameRow.AddChild(btnRenameBase);
			configBox.AddChild(baseRenameRow);

			_statusFeedbackLabel = MakeLabel("", 12, new Color("#4ADE80"), true);
			configBox.AddChild(_statusFeedbackLabel);

			_contentStack.AddChild(configCard);

			// 3B. Tactical Audio & Music Loop Control Card
			PanelContainer audioCard = CreateStyledCard(new Color("#38BDF8"));
			VBoxContainer audioBox = CreateCardVBox(audioCard);
			HBoxContainer audioRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
			audioRow.AddThemeConstantOverride("separation", 10);

			VBoxContainer audioInfoCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
			audioInfoCol.AddThemeConstantOverride("separation", 2);
			audioInfoCol.AddChild(MakeLabel("🎵  ORBITAL SOUNDTRACK & MUSIC TELEMETRY", 13, new Color("#38BDF8"), true));

			bool initialMuted = MusicManager.IsMuted;
			Label audioStatusLbl = MakeLabel(
				initialMuted
					? "STATUS: 🔇 GAME MUSIC MUTED (SILENT OPERATIONS)"
					: "STATUS: 🔊 GAME MUSIC ACTIVE (SEAMLESS LOOP ENGAGED)",
				11,
				initialMuted ? new Color("#F87171") : new Color("#4ADE80"),
				true
			);
			audioInfoCol.AddChild(audioStatusLbl);
			audioRow.AddChild(audioInfoCol);

			Button btnToggleMusic = new Button
			{
				Text = initialMuted ? "🔇 UNMUTE MUSIC" : "🔊 MUTE GAME MUSIC",
				CustomMinimumSize = new Vector2(170, 40)
			};
			btnToggleMusic.AddThemeFontSizeOverride("font_size", 12);

			Action<bool> applyMusicBtnStyle = (bool muted) =>
			{
				btnToggleMusic.Text = muted ? "🔇 UNMUTE MUSIC" : "🔊 MUTE GAME MUSIC";
				btnToggleMusic.Modulate = Colors.White;
				StyleBoxFlat mBtnStyle = new StyleBoxFlat
				{
					BgColor = muted ? new Color(0.28f, 0.08f, 0.10f, 0.96f) : new Color(0.04f, 0.22f, 0.34f, 0.96f),
					BorderColor = muted ? new Color("#EF4444") : new Color("#00F0FF")
				};
				mBtnStyle.SetCornerRadiusAll(8);
				mBtnStyle.SetBorderWidthAll(2);
				btnToggleMusic.AddThemeStyleboxOverride("normal", mBtnStyle);
				btnToggleMusic.AddThemeStyleboxOverride("hover", mBtnStyle);
				btnToggleMusic.AddThemeStyleboxOverride("pressed", mBtnStyle);

				audioStatusLbl.Text = muted
					? "STATUS: 🔇 GAME MUSIC MUTED (SILENT OPERATIONS)"
					: "STATUS: 🔊 GAME MUSIC ACTIVE (SEAMLESS LOOP ENGAGED)";
				audioStatusLbl.Modulate = muted ? new Color("#F87171") : new Color("#4ADE80");
			};
			applyMusicBtnStyle(initialMuted);

			btnToggleMusic.Pressed += () =>
			{
				bool nowMuted = MusicManager.ToggleMute(this);
				applyMusicBtnStyle(nowMuted);
			};

			audioRow.AddChild(btnToggleMusic);
			audioBox.AddChild(audioRow);
			_contentStack.AddChild(audioCard);

			// 4. Navigation & Logout Footer (Responsive width so 3 buttons never push past the modal width!)
			HBoxContainer navRow = new HBoxContainer
			{
				Alignment = BoxContainer.AlignmentMode.Center,
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			navRow.AddThemeConstantOverride("separation", 8);

			Button btnViewMoon = new Button
			{
				Text = $"🪐 VIEW MOON (M-{SupabaseService.CachedMoonId})",
				CustomMinimumSize = new Vector2(140, 42),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			btnViewMoon.AddThemeFontSizeOverride("font_size", 12);
			btnViewMoon.Modulate = new Color("#00F0FF");
			btnViewMoon.Pressed += () =>
			{
				MoonView.SelectedMoonToView = SupabaseService.CachedMoonId;
				CloseModal();
				string target = ResourceLoader.Exists("res://Scripts/MoonView.tscn") ? "res://Scripts/MoonView.tscn" : "res://Scenes/MoonView.tscn";
				GetTree().ChangeSceneToFile(target);
			};
			navRow.AddChild(btnViewMoon);

			Button btnSwitchServer = new Button
			{
				Text = "🌌 SWITCH SERVER",
				CustomMinimumSize = new Vector2(140, 42),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			btnSwitchServer.AddThemeFontSizeOverride("font_size", 12);
			btnSwitchServer.Modulate = new Color("#FBBF24");
			btnSwitchServer.Pressed += () =>
			{
				BaseHUD.Instance?.TriggerInstantSave();
				CloseModal();
				string target = ResourceLoader.Exists("res://Scripts/ServerSelect.tscn") ? "res://Scripts/ServerSelect.tscn" : "res://Scenes/ServerSelect.tscn";
				GetTree().ChangeSceneToFile(target);
			};
			navRow.AddChild(btnSwitchServer);

			Button btnLogout = new Button
			{
				Text = "🚪 LOG OUT",
				CustomMinimumSize = new Vector2(120, 42),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			btnLogout.AddThemeFontSizeOverride("font_size", 12);
			btnLogout.Modulate = new Color("#EF4444");
			btnLogout.Pressed += () =>
			{
				BaseHUD.Instance?.TriggerInstantSave();
				SupabaseService.ClearAllSessionCaches();
				CloseModal();
				string target = ResourceLoader.Exists("res://Scripts/IntroScene.tscn") ? "res://Scripts/IntroScene.tscn" : "res://Scenes/IntroScene.tscn";
				GetTree().ChangeSceneToFile(target);
			};
			navRow.AddChild(btnLogout);

			_contentStack.AddChild(navRow);
		}

		// =========================================================================
		// MODE 2: EMPIRE COLONY SWITCHER (Top-Left "M:100 ; B:1 ▼" Button)
		// =========================================================================
		public void OpenEmpireColonySwitcher()
		{
			_profileViewVersion++;
			ClearContent();
			RecenterModal();
			Visible = true;
			BaseHUD.Instance?.SyncSideDocksVisibilityForActiveModals();

			var srvDef = GameData.GetServerDef(SupabaseService.CachedServerId);
			_lblModalHeader.Text = $"🪐  EMPIRE COMMAND · {srvDef.Name} ({srvDef.Id})";
			_lblModalHeader.Modulate = new Color("#4ADE80");
			_lblSubHeader.Text = $"MONITOR YOUR COLONIES ON {srvDef.Name} ({srvDef.Speed:0.#}X SPEED) OR SWITCH SERVER UNIVERSE";

			PanelContainer primaryCard = CreateStyledCard(new Color("#4ADE80"));
			VBoxContainer box = CreateCardVBox(primaryCard);

			HBoxContainer row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
			VBoxContainer info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			info.AddChild(MakeLabel($"{SupabaseService.CachedBaseName.ToUpper()}  [HOME CAPITAL — {srvDef.Id}]", 17, new Color("#00F0FF")));
			info.AddChild(MakeLabel($"COORDINATE: {srvDef.Id} : M-{SupabaseService.CachedMoonId} : B-{SupabaseService.CachedBaseSlot:D2}   ·   SPEED: {srvDef.Speed:0.#}X   ·   STATUS: ● ENCRYPTED LINK SECURE", 13, new Color("#4ADE80"), true));
			row.AddChild(info);

			Button btnEnter = new Button
			{
				Text = "ACTIVE BASE",
				CustomMinimumSize = new Vector2(140, 44)
			};
			btnEnter.AddThemeFontSizeOverride("font_size", 13);
			btnEnter.Modulate = new Color("#00F0FF");
			btnEnter.Pressed += () =>
			{
				CloseModal();
				string target = ResourceLoader.Exists("res://Scripts/base_view.tscn") ? "res://Scripts/base_view.tscn" : "res://Scenes/base_view.tscn";
				GetTree().ChangeSceneToFile(target);
			};
			row.AddChild(btnEnter);
			box.AddChild(row);
			_contentStack.AddChild(primaryCard);

			PanelContainer srvCard = CreateStyledCard(new Color("#38BDF8"));
			VBoxContainer srvBox = CreateCardVBox(srvCard);
			srvBox.AddChild(MakeLabel("🌌  OPERATIONAL SERVER UNIVERSE SWITCHER", 14, new Color("#38BDF8")));
			srvBox.AddChild(MakeLabel("Your progress on Server 1 (Genesis Prime 1x) and Server 2 (Nebula Cluster 3x) is 100% isolated in the cloud. You can switch between servers at any time without losing progress.", 13, new Color("#CBD5E1"), true));
			Button btnGoServers = new Button
			{
				Text = "🌌  OPEN SERVER SELECTOR (S1 / S2)",
				CustomMinimumSize = new Vector2(0, 42)
			};
			btnGoServers.AddThemeFontSizeOverride("font_size", 13);
			btnGoServers.Modulate = new Color("#00F0FF");
			btnGoServers.Pressed += () =>
			{
				BaseHUD.Instance?.TriggerInstantSave();
				CloseModal();
				string target = ResourceLoader.Exists("res://Scripts/ServerSelect.tscn") ? "res://Scripts/ServerSelect.tscn" : "res://Scenes/ServerSelect.tscn";
				GetTree().ChangeSceneToFile(target);
			};
			srvBox.AddChild(btnGoServers);
			_contentStack.AddChild(srvCard);

			PanelContainer expCard = CreateStyledCard(new Color("#FBBF24"));
			VBoxContainer expBox = CreateCardVBox(expCard);
			expBox.AddChild(MakeLabel("🚀  COLONIZATION EXPANSION PROTOCOL", 14, new Color("#FBBF24")));
			expBox.AddChild(MakeLabel("To claim additional Moon Slots (up to 20 per Moon across 1,000 Moons), upgrade your Industrial Core & Orbital Shipyard to Level 20 and assemble a Colony Ship (u9).", 13, new Color("#CBD5E1"), true));
			_contentStack.AddChild(expCard);
		}

		// =========================================================================
		// MODE 3: GALACTIC LEADERBOARD & MOONS 2148 CLOCK (Top-Right 🏆 RANK Button)
		// =========================================================================
		public async void OpenGalacticLeaderboard()
		{
			int myVersion = ++_profileViewVersion;
			ClearContent();
			RecenterModal();
			Visible = true;
			BaseHUD.Instance?.SyncSideDocksVisibilityForActiveModals();

			var srvDef = GameData.GetServerDef(SupabaseService.CachedServerId);
			_lblModalHeader.Text = $"🏆  {srvDef.Name} ({srvDef.Id}) · STANDINGS";
			_lblModalHeader.Modulate = new Color("#FBBF24");

			// Live Year 2148 Clock & 6-Month Aether-Reaper Countdown (From Legacy React Code)
			DateTime now = DateTime.UtcNow;
			int moonsYear = now.Year + 122;
			string moonsDate = $"{now.Day:D2}/{now.Month:D2}/{moonsYear}  ·  {now.Hour:D2}:{now.Minute:D2} UTC";

			DateTime nextReaper = new DateTime(now.Year, 7, 1, 0, 0, 0, DateTimeKind.Utc);
			if (now >= nextReaper) nextReaper = new DateTime(now.Year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			TimeSpan reaperDiff = nextReaper - now;

			_lblSubHeader.Text = $"STARDATE: {moonsDate}   |   ☠ AETHER-REAPER: {(int)reaperDiff.TotalDays}d {reaperDiff.Hours}h UNTIL ARRIVAL";

			if (SupabaseService.Instance != null)
			{
				_cachedLeaderboard = await SupabaseService.Instance.FetchAllColoniesAsync();
			}

			if (myVersion != _profileViewVersion || !IsInstanceValid(this) || !Visible) return;

			PanelContainer clockBanner = CreateStyledCard(new Color("#EF4444"));
			VBoxContainer clockBox = CreateCardVBox(clockBanner);
			clockBox.AddChild(MakeLabel($"⚔  OPERATIONAL UNIVERSE: {srvDef.Name} ({srvDef.Id} · {srvDef.Speed:0.#}X SPEED)  ·  {_cachedLeaderboard.Count} REGISTERED COMMANDERS", 14, new Color("#FBBF24"), true));
			clockBox.AddChild(MakeLabel("Click any Commander row below to inspect their Planetary Dossier, Moon Coordinate, and Decorations.", 12, new Color("#CBD5E1"), true));
			_contentStack.AddChild(clockBanner);

			for (int i = 0; i < _cachedLeaderboard.Count; i++)
			{
				var entry = _cachedLeaderboard[i];
				int rank = i + 1;

				Color borderCol = entry.IsMe ? new Color("#22C55E") : (rank <= 3 ? new Color("#FBBF24") : new Color("#38BDF8"));
				PanelContainer rowCard = CreateStyledCard(borderCol);
				HBoxContainer row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
				row.AddThemeConstantOverride("separation", 14);

				Label rankLbl = MakeLabel($"#{rank}", 18, rank <= 3 ? new Color("#FBBF24") : new Color("#94A3B8"));
				rankLbl.CustomMinimumSize = new Vector2(48, 0);
				row.AddChild(rankLbl);

				VBoxContainer midCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
				midCol.AddThemeConstantOverride("separation", 2);
				string meTag = entry.IsMe ? "  (YOU)" : "";
				midCol.AddChild(MakeLabel($"{entry.CommanderName}{meTag}", 16, entry.IsMe ? new Color("#4ADE80") : Colors.White));
				midCol.AddChild(MakeLabel($"📍 Moon M-{entry.MoonId} · Slot B-{entry.BaseSlot:D2} ({entry.BaseName})  ·  Core Lvl {entry.IndustrialCoreLevel}", 12, new Color("#94A3B8")));
				row.AddChild(midCol);

				VBoxContainer rightCol = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Pass };
				rightCol.AddChild(MakeLabel($"{entry.Population:N0} POP", 15, new Color("#00F0FF")));

				Button inspectBtn = new Button
				{
					Text = "🔍 BIO / DOSSIER",
					CustomMinimumSize = new Vector2(132, 34)
				};
				inspectBtn.AddThemeFontSizeOverride("font_size", 12);
				inspectBtn.Modulate = new Color("#FBBF24");
				var capturedEntry = entry;
				inspectBtn.Pressed += () => OpenPlayerBioDossier(capturedEntry);
				rightCol.AddChild(inspectBtn);

				row.AddChild(rightCol);

				MarginContainer m = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
				m.AddThemeConstantOverride("margin_left", 14);
				m.AddThemeConstantOverride("margin_right", 14);
				m.AddThemeConstantOverride("margin_top", 10);
				m.AddThemeConstantOverride("margin_bottom", 10);
				m.AddChild(row);
				rowCard.AddChild(m);

				_contentStack.AddChild(rowCard);
			}
		}

		// =========================================================================
		// MODE 4: TARGET PLAYER BIO / PLANETARY DOSSIER (Clicked from MoonView or Leaderboard)
		// =========================================================================
		public void OpenPlayerBioDossier(SupabaseService.GalacticCommanderEntry target)
		{
			if (target == null) return;

			_profileViewVersion++;
			ClearContent();
			RecenterModal();
			Visible = true;
			BaseHUD.Instance?.SyncSideDocksVisibilityForActiveModals();

			_lblModalHeader.Text = $"📡  PLANETARY COMMANDER BIO & DOSSIER";
			_lblModalHeader.Modulate = target.IsMe ? new Color("#4ADE80") : new Color("#F59E0B");
			_lblSubHeader.Text = $"SECTOR COORDINATE: {SupabaseService.CachedServerId} : M-{target.MoonId} : B-{target.BaseSlot:D2}  ·  {(target.IsMe ? "LOCAL COMMAND" : "FOREIGN OUTPOST")}";

			// 1. Identity Card
			PanelContainer bioCard = CreateStyledCard(target.IsMe ? new Color("#4ADE80") : new Color("#EF4444"));
			VBoxContainer bioBox = CreateCardVBox(bioCard);

			bioBox.AddChild(MakeLabel("COMMANDER CALLSIGN:", 12, new Color("#94A3B8")));
			bioBox.AddChild(MakeLabel(target.CommanderName.ToUpper(), 26, Colors.White));
			bioBox.AddChild(MakeLabel($"COLONY DESIGNATION: {target.BaseName.ToUpper()}   ·   ALLIANCE: [{target.AllianceName.ToUpper()}]", 14, new Color("#FBBF24")));

			HBoxContainer metricsRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
			metricsRow.AddThemeConstantOverride("separation", 10);
			metricsRow.AddChild(CreateMiniStatBox("POPULATION", $"{target.Population:N0}", new Color("#4ADE80")));
			metricsRow.AddChild(CreateMiniStatBox("CORE LEVEL", $"LVL {target.IndustrialCoreLevel}", new Color("#00F0FF")));
			metricsRow.AddChild(CreateMiniStatBox("MOON SECTOR", $"M-{target.MoonId}", new Color("#FBBF24")));
			metricsRow.AddChild(CreateMiniStatBox("SURFACE SLOT", $"B-{target.BaseSlot:D2}", new Color("#E879F9")));
			bioBox.AddChild(metricsRow);

			_contentStack.AddChild(bioCard);

			// 2. Decorations & Medals
			PanelContainer medalCard = CreateStyledCard(new Color("#FBBF24"));
			VBoxContainer medalBox = CreateCardVBox(medalCard);
			medalBox.AddChild(MakeLabel("🏅  COMMANDER DECORATIONS & MEDALS", 14, new Color("#FBBF24")));

			HBoxContainer mRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
			mRow.AddThemeConstantOverride("separation", 10);
			mRow.AddChild(CreateMiniStatBox("POP MEDALS", $"🏅 {target.MedalPop}", Colors.White));
			mRow.AddChild(CreateMiniStatBox("RAID MEDALS", $"🏅 {target.MedalRaid}", Colors.White));
			mRow.AddChild(CreateMiniStatBox("ATTACK MEDALS", $"🏅 {target.MedalAtk}", Colors.White));
			mRow.AddChild(CreateMiniStatBox("DEFENSE MEDALS", $"🏅 {target.MedalDef}", Colors.White));
			medalBox.AddChild(mRow);
			_contentStack.AddChild(medalCard);

			// 3. Tactical Fleet Actions & Watchlist Card (Attack / Move / Defend / Drop Off Units / Watchlist)
			// Also shown on the player's own bases so the player can send fleets between their owned/occupied bases and drop off units!
			_contentStack.AddChild(BuildTacticalActionCard(target.MoonId, target.BaseSlot, true, target.CommanderName, target.BaseName, target.AllianceName));

			// 4. Diplomatic Signal Relay & Navigation
			PanelContainer actionCard = CreateStyledCard(new Color("#00F0FF"));
			VBoxContainer actionBox = CreateCardVBox(actionCard);
			actionBox.AddChild(MakeLabel("📩  SEND MESSAGE TO COMMANDER", 14, new Color("#00F0FF")));

			HBoxContainer msgRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
			msgRow.AddThemeConstantOverride("separation", 10);
			_signalMessageInput = new LineEdit
			{
				PlaceholderText = $"Write message to {target.CommanderName}...",
				CustomMinimumSize = new Vector2(260, 42),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_signalMessageInput.AddThemeFontSizeOverride("font_size", 14);
			msgRow.AddChild(_signalMessageInput);

			Button btnSendSignal = new Button
			{
				Text = "📡 SEND MESSAGE",
				CustomMinimumSize = new Vector2(142, 42)
			};
			btnSendSignal.AddThemeFontSizeOverride("font_size", 13);
			btnSendSignal.Modulate = new Color("#4ADE80");
			btnSendSignal.Pressed += async () =>
			{
				string msgText = _signalMessageInput?.Text?.Trim() ?? "";
				if (!string.IsNullOrWhiteSpace(msgText))
				{
					_signalMessageInput.Text = "";
					if (SupabaseService.Instance != null)
					{
						await SupabaseService.Instance.SendInboxSignalAsync(
							SupabaseService.CachedCommanderName,
							target.CommanderName,
							"PERSONAL",
							$"MESSAGE FROM {SupabaseService.CachedCommanderName.ToUpper()}",
							msgText
						);
					}
					_statusFeedbackLabel.Text = $"✓ Message sent to {target.CommanderName}! Click '💬 CHAT HISTORY' to view conversation.";
				}
			};
			msgRow.AddChild(btnSendSignal);

			Button btnOpenThread = new Button
			{
				Text = "💬 CHAT HISTORY",
				CustomMinimumSize = new Vector2(148, 42)
			};
			btnOpenThread.AddThemeFontSizeOverride("font_size", 13);
			btnOpenThread.Modulate = new Color("#00F0FF");
			string targetCmdrCallsign = target.CommanderName;
			btnOpenThread.Pressed += () =>
			{
				CloseModal();
				BaseHUD.Instance?.OpenDirectCommanderThreadModal(targetCmdrCallsign);
			};
			msgRow.AddChild(btnOpenThread);
			actionBox.AddChild(msgRow);

			_statusFeedbackLabel = MakeLabel("", 13, new Color("#4ADE80"), true);
			actionBox.AddChild(_statusFeedbackLabel);

			HBoxContainer navRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
			navRow.AddThemeConstantOverride("separation", 14);

			Button btnJumpMoon = new Button
			{
				Text = $"🪐 VIEW MOON M-{target.MoonId} ORBIT",
				CustomMinimumSize = new Vector2(210, 44)
			};
			btnJumpMoon.AddThemeFontSizeOverride("font_size", 13);
			btnJumpMoon.Modulate = new Color("#00F0FF");
			btnJumpMoon.Pressed += () =>
			{
				MoonView.SelectedMoonToView = target.MoonId;
				CloseModal();
				string moonPath = ResourceLoader.Exists("res://Scripts/MoonView.tscn") ? "res://Scripts/MoonView.tscn" : "res://Scenes/MoonView.tscn";
				GetTree().ChangeSceneToFile(moonPath);
			};
			navRow.AddChild(btnJumpMoon);

			Button btnBackRanks = new Button
			{
				Text = "🏆 VIEW LEADERBOARD",
				CustomMinimumSize = new Vector2(190, 44)
			};
			btnBackRanks.AddThemeFontSizeOverride("font_size", 13);
			btnBackRanks.Modulate = new Color("#FBBF24");
			btnBackRanks.Pressed += OpenGalacticLeaderboard;
			navRow.AddChild(btnBackRanks);

			actionBox.AddChild(navRow);
			_contentStack.AddChild(actionCard);
		}

		// =========================================================================
		// MODE 5: EMPTY MOON SLOT COMMAND MODAL (Clicked from MoonView on vacant crater)
		// =========================================================================
		public void OpenEmptyMoonSlotDossier(int moonId, int slotId) => OpenEmptyMoonSlotModal(moonId, slotId);

		public void OpenEmptyMoonSlotModal(int moonId, int slotId)
		{
			_profileViewVersion++;
			ClearContent();
			RecenterModal();
			Visible = true;
			BaseHUD.Instance?.SyncSideDocksVisibilityForActiveModals();

			_lblModalHeader.Text = $"🌑  EMPTY MOON SLOT — M-{moonId} : B-{slotId:D2}";
			_lblModalHeader.Modulate = new Color("#38BDF8");
			_lblSubHeader.Text = $"UNCLAIMED LUNAR CRATER  ·  OPEN FOR FLEET MOVEMENT & COLONIZATION";

			// Scan any of our fleets currently stationed at, launching from, or heading toward this exact Empty Moon Slot!
			var stationedHere = new List<GameMath.FleetManifest>();
			var launchingFromHere = new List<GameMath.FleetManifest>();
			var headingHere = new List<GameMath.FleetManifest>();

			var myFleets = BaseHUD.Instance?.SavedFleets ?? SupabaseService.CachedSavedFleets;
			if (myFleets != null)
			{
				foreach (var f in myFleets)
				{
					if (f == null || f.Status == "WATCHLIST_DATA") continue;
					if (f.Status == "STATIONED" && GameMath.TryParseSectorCoordinate(f.LocationCoord, out int sm, out int ss) && sm == moonId && ss == slotId)
					{
						stationedHere.Add(f);
					}
					else if (f.Status == "IN_TRANSIT")
					{
						if (GameMath.TryParseSectorCoordinate(f.LocationCoord, out int om, out int os) && om == moonId && os == slotId)
						{
							launchingFromHere.Add(f);
						}
						if (GameMath.TryParseSectorCoordinate(f.TargetCoord, out int dm, out int ds) && dm == moonId && ds == slotId)
						{
							headingHere.Add(f);
						}
					}
				}
			}

			PanelContainer infoCard = CreateStyledCard(stationedHere.Count > 0 ? new Color("#4ADE80") : new Color("#38BDF8"));
			VBoxContainer infoBox = CreateCardVBox(infoCard);
			string statusTitle = stationedHere.Count > 0
				? $"SECTOR STATUS: {stationedHere.Count} OF YOUR FLEET(S) STATIONED IN ORBIT"
				: (launchingFromHere.Count > 0
					? $"SECTOR STATUS: {launchingFromHere.Count} FLEET(S) LAUNCHED FROM THIS ORBIT"
					: "SECTOR STATUS: UNCLAIMED EMPTY MOON SLOT");
			infoBox.AddChild(MakeLabel(statusTitle, 16, stationedHere.Count > 0 ? new Color("#4ADE80") : Colors.White, true));
			infoBox.AddChild(MakeLabel(
				"This moon slot has no player base on it. You can move any of your available fleets here to station them in orbit, launch attacks or moves from here to other moons, or save this coordinate to your Watchlist.",
				13, new Color("#CBD5E1"), true));

			HBoxContainer metricsRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
			metricsRow.AddThemeConstantOverride("separation", 10);
			metricsRow.AddChild(CreateMiniStatBox("MOON SECTOR", $"M-{moonId}", new Color("#00F0FF")));
			metricsRow.AddChild(CreateMiniStatBox("BASE SLOT", $"B-{slotId:D2}", new Color("#38BDF8")));
			metricsRow.AddChild(CreateMiniStatBox("FLEETS AT SLOT", stationedHere.Count > 0 ? $"🛰️ {stationedHere.Count} ORBITING" : (launchingFromHere.Count > 0 ? $"🚀 {launchingFromHere.Count} MOVING" : "0 STATIONED"), new Color("#4ADE80")));
			infoBox.AddChild(metricsRow);
			_contentStack.AddChild(infoCard);

			// If we have fleets stationed at, launching from, or heading to this slot, display a dedicated Live Orbital Fleet Activity Card!
			if (stationedHere.Count > 0 || launchingFromHere.Count > 0 || headingHere.Count > 0)
			{
				PanelContainer orbCard = CreateStyledCard(new Color("#4ADE80"));
				VBoxContainer orbBox = CreateCardVBox(orbCard);
				orbBox.AddChild(MakeLabel($"🛰️  YOUR FLEET ACTIVITY AT M-{moonId}:B-{slotId:D2}", 14, new Color("#4ADE80"), true));

				foreach (var sf in stationedHere)
				{
					int uCount = 0;
					foreach (var kv in sf.Units) uCount += kv.Value;
					HBoxContainer sRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
					sRow.AddThemeConstantOverride("separation", 8);
					Label sLbl = MakeLabel($"● [{sf.Name}] — {uCount:N0} Units Stationed in Orbit (Ready to Launch or Recall)", 13, Colors.White, true);
					sLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					sRow.AddChild(sLbl);

					Button btnRecallHere = new Button
					{
						Text = "↩ RECALL HOME",
						CustomMinimumSize = new Vector2(138, 34)
					};
					btnRecallHere.AddThemeFontSizeOverride("font_size", 11);
					btnRecallHere.Modulate = new Color("#00F0FF");
					var capSf = sf;
					btnRecallHere.Pressed += () =>
					{
						string srv = SupabaseService.CachedServerId;
						float spd = BaseHUD.Instance != null ? Math.Max(1.0f, BaseHUD.Instance.ServerSpeed) : Math.Max(1.0f, SupabaseService.CachedServerSpeed);
						int rSec = GameMath.CalcFleetTravelTimeSeconds(moonId, slotId, SupabaseService.CachedMoonId, SupabaseService.CachedBaseSlot, capSf.Units, spd);
						long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
						capSf.Status = "IN_TRANSIT";
						capSf.Protocol = "RETURN";
						capSf.LocationCoord = $"{srv}:{moonId}:{slotId}";
						capSf.TargetCoord = $"{srv}:{SupabaseService.CachedMoonId}:{SupabaseService.CachedBaseSlot}";
						capSf.TotalFlightDurationSec = rSec;
						capSf.DepartureTimeUnixMs = nowMs;
						capSf.EtaSecondsLeft = rSec;
						capSf.ArrivalTimeUnixMs = nowMs + ((long)rSec * 1000L);
						SupabaseService.MarkLocalFleetMutated(capSf);
						BaseHUD.Instance?.TriggerInstantSave();
						BaseHUD.Instance?.UpdateFleetRadarDropdownUI();
						MoonView.Instance?.RefreshMoonSlotVisuals();
						OpenEmptyMoonSlotModal(moonId, slotId);
					};
					sRow.AddChild(btnRecallHere);
					orbBox.AddChild(sRow);
				}

				foreach (var of in launchingFromHere)
				{
					orbBox.AddChild(MakeLabel(
						$"🚀 [{of.Name}] — Departed this slot ({of.Protocol}) ➔ Heading to {of.TargetCoord} (ETA {GameMath.FormatTime((int)Math.Ceiling(of.EtaSecondsLeft))})",
						13, new Color("#38BDF8"), true));
				}

				foreach (var inf in headingHere)
				{
					orbBox.AddChild(MakeLabel(
						$"🛰️ [{inf.Name}] — En route to this slot from {inf.LocationCoord} (ETA {GameMath.FormatTime((int)Math.Ceiling(inf.EtaSecondsLeft))})",
						13, new Color("#00F0FF"), true));
				}

				_contentStack.AddChild(orbCard);
			}

			_contentStack.AddChild(BuildTacticalActionCard(moonId, slotId, false, "EMPTY MOON SLOT", $"Open Slot B-{slotId:D2}", "NONE"));
		}

		// =========================================================================
		// SHARED TACTICAL FLEET ACTION & WATCHLIST CARD + QUICK FLEET DISPATCHER
		// =========================================================================
		private string _activeQuickActionProtocol = "";
		private int _quickTargetMoon = 100;
		private int _quickTargetSlot = 1;
		private bool _quickTargetOccupied = false;
		private string _quickTargetCmdr = "";
		private string _quickTargetBase = "";
		private string _quickTargetAlliance = "NONE";

		private PanelContainer BuildTacticalActionCard(int moonId, int slotId, bool isOccupied, string cmdrName, string baseName, string allianceName)
		{
			_quickTargetMoon = moonId;
			_quickTargetSlot = slotId;
			_quickTargetOccupied = isOccupied;
			_quickTargetCmdr = cmdrName ?? "";
			_quickTargetBase = baseName ?? "";
			_quickTargetAlliance = string.IsNullOrWhiteSpace(allianceName) ? "NONE" : allianceName.Trim().ToUpper();

			PanelContainer tacCard = CreateStyledCard(new Color("#FBBF24"));
			VBoxContainer tacBox = CreateCardVBox(tacCard);

			HBoxContainer topTitleRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			topTitleRow.AddThemeConstantOverride("separation", 10);
			Label tacHdr = MakeLabel("🎯  TACTICAL FLEET ORDERS & WATCHLIST", 15, new Color("#FBBF24"), true);
			tacHdr.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			topTitleRow.AddChild(tacHdr);

			bool onWatchlist = SupabaseService.IsInWatchlist(moonId, slotId);
			Button btnWatch = new Button
			{
				Text = onWatchlist ? "⭐ IN WATCHLIST (REMOVE)" : "☆ SAVE TO WATCHLIST",
				CustomMinimumSize = new Vector2(205, 38)
			};
			btnWatch.AddThemeFontSizeOverride("font_size", 12);
			btnWatch.Modulate = onWatchlist ? new Color("#FBBF24") : new Color("#38BDF8");
			btnWatch.Pressed += () =>
			{
				if (SupabaseService.IsInWatchlist(moonId, slotId))
				{
					SupabaseService.RemoveFromWatchlist(moonId, slotId);
				}
				else
				{
					SupabaseService.AddOrUpdateWatchlist(moonId, slotId, _quickTargetCmdr, _quickTargetBase, _quickTargetAlliance, isOccupied);
				}
				BaseHUD.Instance?.TriggerInstantSave();
				RefreshCurrentTacticalModal();
			};
			topTitleRow.AddChild(btnWatch);
			tacBox.AddChild(topTitleRow);

			// Evaluate game rules for Attack, Move, Defend (Reinforce), and Drop Off Units (Owned Bases)
			bool isMyOwnedBase = BuildingInspectorModal.IsPlayerOwnedBaseCoordinate(moonId, slotId);
			string myAlliance = (SupabaseService.CachedAllianceName ?? "NONE").Trim().ToUpper();
			bool sameAlliance = isOccupied && !isMyOwnedBase && myAlliance != "NONE" && _quickTargetAlliance == myAlliance;
			bool targetHasNoAlliance = _quickTargetAlliance == "NONE" || string.IsNullOrEmpty(_quickTargetAlliance);

			// Rules:
			// 1. Attack: Only allowed on foreign occupied bases (not own bases, not empty slots, and not same alliance).
			bool canAttack = isOccupied && !isMyOwnedBase && !sameAlliance;
			// 2. Move: Allowed on own bases, any open moon slot, OR on a player who does NOT belong to an alliance.
			bool canMove = isMyOwnedBase || !isOccupied || targetHasNoAlliance;
			// 3. Defend (Reinforce): Strictly allowed ONLY to players in the SAME alliance as you.
			bool canDefend = isOccupied && !isMyOwnedBase && sameAlliance;

			// Like the Laser Cannon button rule: if any fleet is currently registered at this owned base coordinate, show its DROP OFF UNITS button right here!
			if (isMyOwnedBase)
			{
				var allMyFleets = BaseHUD.Instance?.SavedFleets ?? SupabaseService.CachedSavedFleets;
				string srv = SupabaseService.CachedServerId;
				string hereCoord = $"{srv}:{moonId}:{slotId}";
				if (allMyFleets != null)
				{
					foreach (var fltHere in allMyFleets.ToArray())
					{
						if (fltHere == null || fltHere.Status == "IN_TRANSIT" || fltHere.Status == "WATCHLIST_DATA") continue;
						if (string.Equals(fltHere.LocationCoord, hereCoord, StringComparison.OrdinalIgnoreCase) &&
							BuildingInspectorModal.IsFleetAtPlayerOwnedCoordinate(fltHere))
						{
							int shipCnt = 0;
							foreach (var kv in fltHere.Units) shipCnt += Math.Max(0, kv.Value);
							HBoxContainer dropRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
							dropRow.AddThemeConstantOverride("separation", 8);
							Label dropLbl = MakeLabel($"⚓ [{fltHere.Name}] ({shipCnt:N0} Units) is registered at your owned coordinate M-{moonId}:B-{slotId:D2}", 13, new Color("#4ADE80"), true);
							dropLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
							dropRow.AddChild(dropLbl);

							Button btnDropHere = new Button
							{
								Text = "🪂 DROP OFF UNITS HERE",
								CustomMinimumSize = new Vector2(180, 38)
							};
							btnDropHere.AddThemeFontSizeOverride("font_size", 12);
							btnDropHere.Modulate = new Color("#22C55E");
							var capturedFltHere = fltHere;
							btnDropHere.Pressed += () =>
							{
								BuildingInspectorModal.DropOffFleetUnitsToOwnedBase(capturedFltHere, RefreshCurrentTacticalModal);
							};
							dropRow.AddChild(btnDropHere);
							tacBox.AddChild(dropRow);
						}
					}
				}
			}

			HBoxContainer actionBtnsRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			actionBtnsRow.AddThemeConstantOverride("separation", 10);

			if (isOccupied && !isMyOwnedBase)
			{
				Button btnAttack = new Button
				{
					Text = canAttack ? "⚔️ ATTACK BASE" : "🔒 ATTACK (ALLY)",
					CustomMinimumSize = new Vector2(0, 44),
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					Disabled = !canAttack
				};
				btnAttack.AddThemeFontSizeOverride("font_size", 13);
				btnAttack.Modulate = canAttack ? new Color("#EF4444") : new Color("#64748B");
				btnAttack.Pressed += () =>
				{
					_activeQuickActionProtocol = "RAID";
					RefreshCurrentTacticalModal();
				};
				actionBtnsRow.AddChild(btnAttack);
			}

			Button btnMove = new Button
			{
				Text = isMyOwnedBase ? "🛰️ SEND FLEET TO THIS BASE" : (canMove ? "🛰️ MOVE FLEET HERE" : $"🔒 MOVE LOCKED ([{_quickTargetAlliance}])"),
				CustomMinimumSize = new Vector2(0, 44),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				Disabled = !canMove
			};
			btnMove.AddThemeFontSizeOverride("font_size", 13);
			btnMove.Modulate = canMove ? new Color("#00F0FF") : new Color("#64748B");
			btnMove.Pressed += () =>
			{
				_activeQuickActionProtocol = "MOVE";
				RefreshCurrentTacticalModal();
			};
			actionBtnsRow.AddChild(btnMove);

			if (isMyOwnedBase)
			{
				Button btnSendDropOff = new Button
				{
					Text = "🪂 SEND & DROP OFF UNITS HERE",
					CustomMinimumSize = new Vector2(0, 44),
					SizeFlagsHorizontal = SizeFlags.ExpandFill
				};
				btnSendDropOff.AddThemeFontSizeOverride("font_size", 13);
				btnSendDropOff.Modulate = new Color("#22C55E");
				btnSendDropOff.Pressed += () =>
				{
					_activeQuickActionProtocol = "DROPOFF";
					RefreshCurrentTacticalModal();
				};
				actionBtnsRow.AddChild(btnSendDropOff);
			}
			else if (isOccupied)
			{
				Button btnDefend = new Button
				{
					Text = canDefend ? "🛡️ DEFEND ALLY" : "🔒 DEFEND (SAME ALLIANCE ONLY)",
					CustomMinimumSize = new Vector2(0, 44),
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					Disabled = !canDefend
				};
				btnDefend.AddThemeFontSizeOverride("font_size", 13);
				btnDefend.Modulate = canDefend ? new Color("#4ADE80") : new Color("#64748B");
				btnDefend.Pressed += () =>
				{
					_activeQuickActionProtocol = "REINFORCE";
					RefreshCurrentTacticalModal();
				};
				actionBtnsRow.AddChild(btnDefend);
			}

			tacBox.AddChild(actionBtnsRow);

			// Rule explanation helper text in clear gaming English
			string ruleHint = isMyOwnedBase
				? "Rule: This is your owned base coordinate. You can send fleets between your occupied & owned bases to juggle troops or drop off units directly into your base garrison."
				: (!isOccupied
					? "Rule: You can move any available fleet to this open moon slot. Once stationed here, that fleet can move directly to any other target without returning home first."
					: (canDefend
						? $"Rule: {cmdrName} is in your Alliance [{myAlliance}]. You can send defense fleets to reinforce their base."
						: (canMove
							? $"Rule: {cmdrName} has no alliance. You can attack their base or move a fleet into their orbit. (Defense fleets can only be sent to members of your own alliance)."
							: $"Rule: {cmdrName} belongs to Alliance [{_quickTargetAlliance}]. You can attack their base, but you cannot move or send defense fleets to an enemy alliance.")));
			tacBox.AddChild(MakeLabel(ruleHint, 12, new Color("#94A3B8"), true));

			// If player clicked Attack, Move, or Defend, show the Quick Fleet Selector right here!
			if (!string.IsNullOrEmpty(_activeQuickActionProtocol))
			{
				tacBox.AddChild(new HSeparator());
				BuildQuickFleetSelectorList(tacBox, moonId, slotId, _activeQuickActionProtocol);
			}

			return tacCard;
		}

		private void BuildQuickFleetSelectorList(VBoxContainer parentBox, int targetMoon, int targetSlot, string protocol)
		{
			string actionTitle = protocol == "RAID"
				? $"⚔️ SELECT A FLEET TO ATTACK M-{targetMoon}:B-{targetSlot:D2}"
				: (protocol == "REINFORCE"
					? $"🛡️ SELECT A FLEET TO DEFEND ALLY AT M-{targetMoon}:B-{targetSlot:D2}"
					: (protocol == "DROPOFF"
						? $"🪂 SELECT A FLEET TO SEND & DROP OFF UNITS AT M-{targetMoon}:B-{targetSlot:D2}"
						: $"🛰️ SELECT A FLEET TO MOVE TO M-{targetMoon}:B-{targetSlot:D2}"));

			Color actionColor = protocol == "RAID"
				? new Color("#EF4444")
				: (protocol == "REINFORCE" || protocol == "DROPOFF" ? new Color("#4ADE80") : new Color("#00F0FF"));

			parentBox.AddChild(MakeLabel(actionTitle, 14, actionColor, true));

			// Check Radar Range
			int radarLvl = BaseHUD.Instance != null
				? Math.Clamp(BaseHUD.Instance.BuildingLevels.GetValueOrDefault("hub_rng", 1), 1, 10)
				: Math.Clamp(SupabaseService.CachedRadarLevel, 1, 10);

			if (!GameMath.IsMoonVisibleInRadarCircle(SupabaseService.CachedMoonId, targetMoon, radarLvl))
			{
				parentBox.AddChild(MakeLabel(
					$"🔒 Target Moon M-{targetMoon} is outside your Level {radarLvl} Deep Space Radar range ({radarLvl * 10}% Galaxy Visibility). Upgrade your Deep Space Radar to launch fleets to this moon!",
					13, new Color("#EF4444"), true));
				return;
			}

			var allFleets = BaseHUD.Instance?.SavedFleets ?? SupabaseService.CachedSavedFleets ?? new List<GameMath.FleetManifest>();
			string activeSrv = SupabaseService.CachedServerId;
			string targetCoordStr = $"{activeSrv}:{targetMoon}:{targetSlot}";

			int availableCount = 0;
			foreach (var f in allFleets)
			{
				if (f == null || (f.Status != "DOCKED" && f.Status != "STATIONED")) continue;
				// Don't list a fleet that is already at the exact target coordinate
				if (string.Equals(f.LocationCoord, targetCoordStr, StringComparison.OrdinalIgnoreCase)) continue;

				availableCount++;
				PanelContainer fCard = CreateStyledCard(actionColor);
				VBoxContainer fBox = CreateCardVBox(fCard);

				long atk = 0, def = 0, hp = 0;
				int totalUnits = 0;
				int laserCount = f.Units.GetValueOrDefault("u6", 0);
				foreach (var kv in f.Units)
				{
					totalUnits += kv.Value;
					var u = GameData.Units.Find(x => x.Id == kv.Key);
					if (u != null)
					{
						atk += (long)u.Atk * kv.Value;
						def += (long)u.Def * kv.Value;
						hp += (long)u.Hp * kv.Value;
					}
				}

				GameMath.TryParseSectorCoordinate(f.LocationCoord, out int origMoon, out int origSlot);
				float srvSpeed = BaseHUD.Instance != null ? Math.Max(1.0f, BaseHUD.Instance.ServerSpeed) : Math.Max(1.0f, SupabaseService.CachedServerSpeed);
				int travelSec = GameMath.CalcFleetTravelTimeSeconds(origMoon, origSlot, targetMoon, targetSlot, f.Units, srvSpeed);

				string statusTag = f.Status == "DOCKED" ? $"At Home Base (M-{origMoon}:B-{origSlot:D2})" : $"Stationed at M-{origMoon}:B-{origSlot:D2}";
				fBox.AddChild(MakeLabel($"🚀 {f.Name.ToUpper()}  ·  {totalUnits:N0} Units  ·  {statusTag}", 14, Colors.White, true));
				fBox.AddChild(MakeLabel($"Attack: {atk:N0}  |  Defense: {def:N0}  |  Hull HP: {hp:N0}  |  Flight Time: {GameMath.FormatTime(travelSec)}", 12, new Color("#00F0FF"), true));

				// Only show Cannon Building Target selector if this fleet contains Laser Cannons (u6 > 0) and is attacking!
				if (laserCount > 0 && protocol == "RAID")
				{
					string curBldName = GameMath.GetSiegeTargetDisplayName(f.TargetBuildingId);
					Button btnTargetBld = new Button
					{
						Text = $"💥 Laser Cannons ({laserCount}) Target: {curBldName.ToUpper()} (Click to Change)",
						CustomMinimumSize = new Vector2(0, 36),
						SizeFlagsHorizontal = SizeFlags.ExpandFill
					};
					btnTargetBld.AddThemeFontSizeOverride("font_size", 12);
					btnTargetBld.Modulate = new Color("#FBBF24");
					var capturedFleetForTarget = f;
					btnTargetBld.Pressed += () =>
					{
						int curIdx = 0;
						for (int i = 0; i < GameMath.SiegeTargetBuildings.Length; i++)
						{
							if (GameMath.SiegeTargetBuildings[i].Id == capturedFleetForTarget.TargetBuildingId)
							{
								curIdx = i;
								break;
							}
						}
						int nextIdx = (curIdx + 1) % GameMath.SiegeTargetBuildings.Length;
						capturedFleetForTarget.TargetBuildingId = GameMath.SiegeTargetBuildings[nextIdx].Id;
						BaseHUD.Instance?.TriggerInstantSave();
						RefreshCurrentTacticalModal();
					};
					fBox.AddChild(btnTargetBld);
				}

				Button btnLaunch = new Button
				{
					Text = protocol == "RAID"
						? $"🚀 LAUNCH ATTACK WITH {f.Name.ToUpper()} ({GameMath.FormatTime(travelSec)})"
						: (protocol == "REINFORCE"
							? $"🛡️ SEND {f.Name.ToUpper()} TO DEFEND ({GameMath.FormatTime(travelSec)})"
							: (protocol == "DROPOFF"
								? $"🪂 SEND & DROP OFF {f.Name.ToUpper()} ({GameMath.FormatTime(travelSec)})"
								: $"🛰️ MOVE {f.Name.ToUpper()} HERE ({GameMath.FormatTime(travelSec)})")),
					CustomMinimumSize = new Vector2(0, 42),
					SizeFlagsHorizontal = SizeFlags.ExpandFill
				};
				btnLaunch.AddThemeFontSizeOverride("font_size", 13);
				btnLaunch.Modulate = actionColor;
				var capturedFleet = f;
				btnLaunch.Pressed += () => ExecuteQuickFleetLaunch(capturedFleet, origMoon, origSlot, targetMoon, targetSlot, protocol, travelSec);
				fBox.AddChild(btnLaunch);

				parentBox.AddChild(fCard);
			}

			if (availableCount == 0)
			{
				parentBox.AddChild(MakeLabel(
					"You have no available fleets ready to launch right now. Open your Fleet Station at your base to create a fleet from your Garrison Armory!",
					13, new Color("#F59E0B"), true));
			}
		}

		private async void ExecuteQuickFleetLaunch(GameMath.FleetManifest f, int origMoon, int origSlot, int targetMoon, int targetSlot, string protocol, int travelSec)
		{
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			long arrivalMs = nowMs + ((long)travelSec * 1000L);
			string activeSrv = SupabaseService.CachedServerId;

			f.Status = "IN_TRANSIT";
			f.Protocol = protocol;
			f.LocationCoord = $"{activeSrv}:{origMoon}:{origSlot}";
			f.TargetCoord = $"{activeSrv}:{targetMoon}:{targetSlot}";
			f.TargetCommanderName = _quickTargetOccupied ? _quickTargetCmdr : "";
			f.TotalFlightDurationSec = travelSec;
			f.DepartureTimeUnixMs = nowMs;
			f.EtaSecondsLeft = travelSec;
			f.ArrivalTimeUnixMs = arrivalMs;
			SupabaseService.MarkLocalFleetMutated(f);

			BaseHUD.Instance?.TriggerInstantSave();
			BaseHUD.Instance?.UpdateFleetRadarDropdownUI();
			MoonView.Instance?.RefreshMoonSlotVisuals();
			string protoVerb = protocol == "RAID" ? "ATTACKING" : (protocol == "REINFORCE" ? "DEFENDING ALLY AT" : (protocol == "DROPOFF" ? "DROPPING OFF UNITS AT" : "MOVING TO"));
			BaseHUD.Instance?.ShowFleetDockNotification(
				$"🚀 FLEET LAUNCHED: [{f.Name}] is {protoVerb} M-{targetMoon}:B-{targetSlot:D2} (ETA {GameMath.FormatTime(travelSec)})",
				protocol == "RAID" ? new Color("#EF4444") : (protocol == "REINFORCE" || protocol == "DROPOFF" ? new Color("#4ADE80") : new Color("#00F0FF"))
			);

			bool isMyOwnedBase = BuildingInspectorModal.IsPlayerOwnedBaseCoordinate(targetMoon, targetSlot);
			if (_quickTargetOccupied && !isMyOwnedBase && !string.IsNullOrEmpty(_quickTargetCmdr) && SupabaseService.Instance != null)
			{
				int totalUnits = 0;
				foreach (var kv in f.Units) totalUnits += kv.Value;
				int lasers = f.Units.GetValueOrDefault("u6", 0);
				string bldName = GameMath.GetSiegeTargetDisplayName(f.TargetBuildingId);
				string origin = $"{activeSrv}:{origMoon}:{origSlot}";
				string sigPayload = $"{f.Id}|{SupabaseService.CachedCommanderName}|{origin}|{protocol}|{bldName}|{totalUnits}|{lasers}|{travelSec}|{arrivalMs}";

				await SupabaseService.Instance.SendInboxSignalAsync(
					SupabaseService.CachedCommanderName,
					_quickTargetCmdr,
					"INTEL",
					$"INCOMING_FLEET:{f.Id}",
					sigPayload
				);
			}

			_activeQuickActionProtocol = "";
			CloseModal();
		}

		private void RefreshCurrentTacticalModal()
		{
			if (_quickTargetOccupied)
			{
				var entry = new SupabaseService.GalacticCommanderEntry
				{
					CommanderName = _quickTargetCmdr,
					BaseName = _quickTargetBase,
					AllianceName = _quickTargetAlliance,
					MoonId = _quickTargetMoon,
					BaseSlot = _quickTargetSlot,
					IsMe = false
				};
				var found = _cachedLeaderboard.Find(x => x.MoonId == _quickTargetMoon && x.BaseSlot == _quickTargetSlot);
				if (found != null) entry = found;
				OpenPlayerBioDossier(entry);
			}
			else
			{
				OpenEmptyMoonSlotModal(_quickTargetMoon, _quickTargetSlot);
			}
		}

		private void OnRenameCommanderPressed()
		{
			if (_renameCommanderInput == null) return;
			string newName = _renameCommanderInput.Text.Trim().ToUpper();
			if (newName.Length < 3)
			{
				_statusFeedbackLabel.Text = "ERROR: Callsign must be at least 3 characters.";
				_statusFeedbackLabel.Modulate = new Color("#EF4444");
				return;
			}

			long curGold = BaseHUD.Instance != null ? BaseHUD.Instance.GGold : SupabaseService.CachedGGold;
			if (curGold < 100)
			{
				_statusFeedbackLabel.Text = "DENIED: Requires 100 Galaxy Gold (GGold) to broadcast new Callsign.";
				_statusFeedbackLabel.Modulate = new Color("#EF4444");
				return;
			}

			// Lock the player's persistent Moon & Slot coordinate keys before renaming so a callsign change NEVER shifts coordinates!
			int lockedMoon = (SupabaseService.CachedMoonId >= 1 && SupabaseService.CachedMoonId <= 1000) ? SupabaseService.CachedMoonId : 100;
			int lockedSlot = (SupabaseService.CachedBaseSlot >= 1 && SupabaseService.CachedBaseSlot <= 20) ? SupabaseService.CachedBaseSlot : 1;
			SupabaseService.CachedMoonId = lockedMoon;
			SupabaseService.CachedBaseSlot = lockedSlot;

			SupabaseService.CachedCommanderName = newName;
			SupabaseService.CachedGGold = Math.Max(0, curGold - 100);
			if (SupabaseService.Instance != null) SupabaseService.Instance.LoggedInUsername = newName;

			if (BaseHUD.Instance != null)
			{
				BaseHUD.Instance.GGold = SupabaseService.CachedGGold;
				BaseHUD.Instance.CommanderName = newName;
				BaseHUD.Instance.CurrentMoon = lockedMoon;
				BaseHUD.Instance.CurrentBaseSlot = lockedSlot;
				if (BaseHUD.Instance.BuildingLevels != null)
				{
					BaseHUD.Instance.BuildingLevels["__meta_moon"] = lockedMoon;
					BaseHUD.Instance.BuildingLevels["__meta_slot"] = lockedSlot;
				}
				BaseHUD.Instance.UpdateHUDDisplay();
				BaseHUD.Instance.TriggerInstantSave();
			}
			else if (SupabaseService.Instance != null)
			{
				SupabaseService.CachedBuildingLevels["__meta_moon"] = lockedMoon;
				SupabaseService.CachedBuildingLevels["__meta_slot"] = lockedSlot;
				_ = SupabaseService.Instance.SaveColonyStateAsync(
					SupabaseService.CachedResE,
					SupabaseService.CachedResI,
					SupabaseService.CachedResT,
					SupabaseService.CachedResH3,
					SupabaseService.CachedGGold,
					SupabaseService.CachedBuildingLevels,
					SupabaseService.CachedActiveBuild,
					SupabaseService.CachedBuildQueue,
					SupabaseService.CachedActiveBoosts,
					newName);
			}

			SupabaseService.InvalidateColoniesCache();
			MoonView.Instance?.RefreshMoonSlotVisuals();

			_statusFeedbackLabel.Text = $"✓ Callsign updated to {newName} across all galactic registries! (Sector M-{lockedMoon}:B-{lockedSlot:D2} locked)";
			_statusFeedbackLabel.Modulate = new Color("#4ADE80");
		}

		private void OnRenameColonyPressed()
		{
			if (_renameBaseInput == null) return;
			string newBase = _renameBaseInput.Text.Trim();
			if (newBase.Length < 3)
			{
				_statusFeedbackLabel.Text = "ERROR: Colony name must be at least 3 characters.";
				_statusFeedbackLabel.Modulate = new Color("#EF4444");
				return;
			}

			// Lock the player's persistent Moon & Slot coordinate keys before renaming so a base name change NEVER shifts coordinates or despawns the base!
			int lockedMoon = (SupabaseService.CachedMoonId >= 1 && SupabaseService.CachedMoonId <= 1000) ? SupabaseService.CachedMoonId : 100;
			int lockedSlot = (SupabaseService.CachedBaseSlot >= 1 && SupabaseService.CachedBaseSlot <= 20) ? SupabaseService.CachedBaseSlot : 1;
			SupabaseService.CachedMoonId = lockedMoon;
			SupabaseService.CachedBaseSlot = lockedSlot;

			string activeSrv = string.IsNullOrWhiteSpace(SupabaseService.CachedServerId) ? "S1" : SupabaseService.CachedServerId.Trim().ToUpper();
			SupabaseService.SetServerCustomBaseName(activeSrv, newBase);

			string boostMetaKey = activeSrv == "S1" ? "__meta_base_name_S1" : $"__srv_{activeSrv}_base_name";
			if (SupabaseService.CachedActiveBoosts != null)
			{
				SupabaseService.CachedActiveBoosts[boostMetaKey] = new GameMath.BoostData
				{
					Multiplier = 1.0f,
					Expiration = DateTimeOffset.UtcNow.AddYears(10),
					Label = newBase
				};
			}

			if (BaseHUD.Instance != null)
			{
				BaseHUD.Instance.CurrentBaseName = newBase;
				BaseHUD.Instance.CurrentMoon = lockedMoon;
				BaseHUD.Instance.CurrentBaseSlot = lockedSlot;
				if (BaseHUD.Instance.BuildingLevels != null)
				{
					BaseHUD.Instance.BuildingLevels["__meta_moon"] = lockedMoon;
					BaseHUD.Instance.BuildingLevels["__meta_slot"] = lockedSlot;
				}
				if (BaseHUD.Instance.ActiveBoosts != null)
				{
					BaseHUD.Instance.ActiveBoosts[boostMetaKey] = new GameMath.BoostData
					{
						Multiplier = 1.0f,
						Expiration = DateTimeOffset.UtcNow.AddYears(10),
						Label = newBase
					};
				}
				BaseHUD.Instance.UpdateHUDDisplay();
				BaseHUD.Instance.TriggerInstantSave();
			}
			else if (SupabaseService.Instance != null)
			{
				SupabaseService.CachedBuildingLevels["__meta_moon"] = lockedMoon;
				SupabaseService.CachedBuildingLevels["__meta_slot"] = lockedSlot;
				_ = SupabaseService.Instance.SaveColonyStateAsync(
					SupabaseService.CachedResE,
					SupabaseService.CachedResI,
					SupabaseService.CachedResT,
					SupabaseService.CachedResH3,
					SupabaseService.CachedGGold,
					SupabaseService.CachedBuildingLevels,
					SupabaseService.CachedActiveBuild,
					SupabaseService.CachedBuildQueue,
					SupabaseService.CachedActiveBoosts,
					SupabaseService.CachedCommanderName);
			}

			if (SupabaseService.IsInWatchlist(lockedMoon, lockedSlot))
			{
				int coreLvl = BaseHUD.Instance != null
					? BaseHUD.Instance.BuildingLevels.GetValueOrDefault("hub_cmd", 1)
					: SupabaseService.CachedBuildingLevels.GetValueOrDefault("hub_cmd", 1);
				SupabaseService.AddOrUpdateWatchlistEntry(
					lockedMoon,
					lockedSlot,
					SupabaseService.CachedCommanderName,
					newBase,
					SupabaseService.CachedAllianceName,
					coreLvl,
					true);
			}

			SupabaseService.InvalidateColoniesCache();
			MoonView.Instance?.RefreshMoonSlotVisuals();

			_statusFeedbackLabel.Text = $"✓ Sector designation updated to '{newBase}' at M-{lockedMoon}:B-{lockedSlot:D2}!";
			_statusFeedbackLabel.Modulate = new Color("#4ADE80");
		}

		private PanelContainer CreateStyledCard(Color borderColor)
		{
			PanelContainer p = new PanelContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};
			StyleBoxFlat s = new StyleBoxFlat
			{
				BgColor = new Color(0.06f, 0.13f, 0.24f, 0.98f), // Bright Fleet Station High-Visibility Card Backdrop
				BorderColor = new Color(borderColor.R, borderColor.G, borderColor.B, 0.92f),
				BorderWidthLeft = 4,
				BorderWidthRight = 2,
				BorderWidthTop = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 8,
				CornerRadiusTopRight = 8,
				CornerRadiusBottomLeft = 8,
				CornerRadiusBottomRight = 8
			};
			p.AddThemeStyleboxOverride("panel", s);
			return p;
		}

		private VBoxContainer CreateCardVBox(PanelContainer card)
		{
			MarginContainer m = new MarginContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};
			m.AddThemeConstantOverride("margin_left", 14);
			m.AddThemeConstantOverride("margin_right", 14);
			m.AddThemeConstantOverride("margin_top", 10);
			m.AddThemeConstantOverride("margin_bottom", 10);

			VBoxContainer v = new VBoxContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};
			v.AddThemeConstantOverride("separation", 8);
			m.AddChild(v);
			card.AddChild(m);
			return v;
		}

		private Label MakeLabel(string text, int fontSize, Color color, bool wrap = false)
		{
			Label l = new Label
			{
				Text = text,
				Modulate = color,
				AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
				SizeFlagsHorizontal = wrap ? SizeFlags.ExpandFill : SizeFlags.Fill,
				MouseFilter = MouseFilterEnum.Ignore
			};
			int boostedSize = fontSize <= 14 ? fontSize + 3 : (fontSize <= 18 ? fontSize + 2 : fontSize);
			l.AddThemeFontSizeOverride("font_size", boostedSize);
			l.AddThemeColorOverride("font_outline_color", Colors.Black);
			l.AddThemeConstantOverride("outline_size", 2);
			return l;
		}

		private PanelContainer CreateMiniStatBox(string label, string val, Color accentColor)
		{
			PanelContainer box = new PanelContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				CustomMinimumSize = new Vector2(88, 56),
				MouseFilter = MouseFilterEnum.Ignore
			};

			StyleBoxFlat style = new StyleBoxFlat
			{
				BgColor = new Color(0.04f, 0.09f, 0.18f, 0.96f),
				BorderColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.75f),
				BorderWidthLeft = 2,
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
			box.AddThemeStyleboxOverride("panel", style);

			VBoxContainer v = new VBoxContainer
			{
				Alignment = BoxContainer.AlignmentMode.Center,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Ignore
			};
			v.AddThemeConstantOverride("separation", 2);

			Label lblTop = MakeLabel(label, 12, new Color("#CBD5E1"), false);
			lblTop.HorizontalAlignment = HorizontalAlignment.Center;

			Label lblVal = MakeLabel(val, 17, accentColor, false);
			lblVal.HorizontalAlignment = HorizontalAlignment.Center;

			v.AddChild(lblTop);
			v.AddChild(lblVal);
			box.AddChild(v);
			return box;
		}

		public void CloseModal()
		{
			_profileViewVersion++;
			Visible = false;
			BaseHUD.Instance?.SyncSideDocksVisibilityForActiveModals();
			MoonView.Instance?.NotifyExternalStateUpdated(refreshCloudOccupants: false);
			GalaxyView.Instance?.QueueRedraw();
		}
	}
}
