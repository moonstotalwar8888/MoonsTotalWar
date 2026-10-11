using Godot;
using System;
using System.Collections.Generic;
using MoonsTotalWar.Engine;

namespace MoonsTotalWar.UI
{
	/// <summary>
	/// MOONS TOTAL WAR: MULTIVERSE SERVER SELECTOR
	/// Allows commanders to enter speed-scaled universes (Server 1: 1x, Server 2: 3x) with 100% isolated colony progress.
	/// </summary>
	public partial class ServerSelectScene : Control
	{
		private SupabaseService _supabase;
		private CanvasLayer _uiCanvas;
		private ColorRect _deepSpaceBase;
		private TextureRect _bgImage;
		private ColorRect _darkOverlay;
		private float _animTime = 0f;
		private PanelContainer _centerPanel;
		private VBoxContainer _serverListContainer;
		private readonly Dictionary<string, Label> _serverColonyBadgeLabels = new Dictionary<string, Label>();
		private readonly Dictionary<string, Button> _serverEnterButtons = new Dictionary<string, Button>();

		public override void _Ready()
		{
			RenderingServer.SetDefaultClearColor(new Color(0.01f, 0.02f, 0.05f, 1.0f));
			SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

			_uiCanvas = new CanvasLayer { Layer = 10 };
			AddChild(_uiCanvas);

			_supabase = SupabaseService.Instance ?? GetNodeOrNull<SupabaseService>("/root/SupabaseService");
			BuildCinematicBackground();
			BuildServerSelectUI();

			GetViewport().Connect("size_changed", Callable.From(RecenterServerPanelOnViewport));
			RecenterServerPanelOnViewport();
			CallDeferred(nameof(RecenterServerPanelOnViewport));

			RefreshServerColonyBadgesAsync();
			MusicManager.PlayTrack(this, MusicManager.TRACK_LOGIN);
		}

		public override void _Process(double delta)
		{
			_animTime += (float)delta;
			if (_bgImage != null)
			{
				float panX = MathF.Sin(_animTime * 0.3f) * 20f;
				float panY = MathF.Cos(_animTime * 0.25f) * 12f;
				float zoomScale = 1.05f + MathF.Sin(_animTime * 0.15f) * 0.03f;

				_bgImage.Position = new Vector2(-80f + panX, -50f + panY);
				_bgImage.Scale = new Vector2(zoomScale, zoomScale);
			}
		}

		private void BuildCinematicBackground()
		{
			// 1. Deep Obsidian Space Fallback Backdrop (Replaces Godot's default grey clear color)
			_deepSpaceBase = new ColorRect
			{
				Color = new Color(0.01f, 0.02f, 0.05f, 1.0f),
				MouseFilter = MouseFilterEnum.Ignore
			};
			_deepSpaceBase.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			_uiCanvas.AddChild(_deepSpaceBase);

			// 2. Animated Space Texture (Matches IntroScene portal)
			_bgImage = new TextureRect
			{
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
				CustomMinimumSize = new Vector2(1600, 900),
				Position = new Vector2(-80, -50),
				MouseFilter = MouseFilterEnum.Ignore
			};

			Texture2D bgTex = null;
			if (ResourceLoader.Exists("res://Assets/login-bg.jpg"))
			{
				bgTex = GD.Load<Texture2D>("res://Assets/login-bg.jpg");
			}
			else if (ResourceLoader.Exists("res://Assets/galaxy_bg.jpg"))
			{
				bgTex = GD.Load<Texture2D>("res://Assets/galaxy_bg.jpg");
			}
			if (bgTex != null)
			{
				_bgImage.Texture = bgTex;
			}
			_uiCanvas.AddChild(_bgImage);

			// 3. Translucent Dark Space Vignette
			_darkOverlay = new ColorRect
			{
				Color = new Color(0.02f, 0.04f, 0.08f, 0.62f),
				MouseFilter = MouseFilterEnum.Ignore
			};
			_darkOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			_uiCanvas.AddChild(_darkOverlay);
		}

		private void RecenterServerPanelOnViewport()
		{
			Vector2 vp = GetViewportRect().Size;
			if (vp.X <= 1f || vp.Y <= 1f)
			{
				vp = DisplayServer.WindowGetSize();
			}

			if (_deepSpaceBase != null)
			{
				_deepSpaceBase.Position = Vector2.Zero;
				_deepSpaceBase.Size = vp;
			}

			if (_bgImage != null)
			{
				_bgImage.CustomMinimumSize = new Vector2(Mathf.Max(1600f, vp.X + 240f), Mathf.Max(900f, vp.Y + 180f));
				_bgImage.Size = _bgImage.CustomMinimumSize;
			}

			if (_darkOverlay != null)
			{
				_darkOverlay.Position = Vector2.Zero;
				_darkOverlay.Size = vp;
			}

			if (_centerPanel != null)
			{
				float panelW = Mathf.Clamp(vp.X - 48f, 340f, 640f);
				float panelH = Mathf.Clamp(vp.Y - 64f, 320f, 560f);
				_centerPanel.CustomMinimumSize = new Vector2(panelW, panelH);
				_centerPanel.Size = new Vector2(panelW, panelH);
				_centerPanel.Position = new Vector2(Mathf.Round((vp.X - panelW) * 0.5f), Mathf.Round((vp.Y - panelH) * 0.5f));
			}
		}

		private void BuildServerSelectUI()
		{
			_centerPanel = new PanelContainer
			{
				CustomMinimumSize = new Vector2(620, 540)
			};
			_uiCanvas.AddChild(_centerPanel);

			StyleBoxFlat panelStyle = new StyleBoxFlat();
			panelStyle.BgColor = new Color(0.03f, 0.06f, 0.11f, 0.96f);
			panelStyle.SetCornerRadiusAll(12);
			panelStyle.SetBorderWidthAll(2);
			panelStyle.BorderColor = new Color(0f, 0.94f, 1f, 0.85f);
			panelStyle.ShadowColor = new Color(0f, 0.94f, 1f, 0.25f);
			panelStyle.ShadowSize = 22;
			_centerPanel.AddThemeStyleboxOverride("panel", panelStyle);

			VBoxContainer mainContent = new VBoxContainer();
			mainContent.AddThemeConstantOverride("separation", 12);

			MarginContainer margin = new MarginContainer();
			margin.AddThemeConstantOverride("margin_left", 22);
			margin.AddThemeConstantOverride("margin_right", 22);
			margin.AddThemeConstantOverride("margin_top", 18);
			margin.AddThemeConstantOverride("margin_bottom", 18);
			margin.AddChild(mainContent);
			_centerPanel.AddChild(margin);

			// Header
			Label header = new Label();
			header.Text = "🌌  OPERATIONAL UNIVERSES (ISOLATED SERVERS)";
			header.HorizontalAlignment = HorizontalAlignment.Center;
			header.AddThemeFontSizeOverride("font_size", 19);
			header.Modulate = new Color(0f, 0.94f, 1f);
			mainContent.AddChild(header);

			Label subHeader = new Label();
			subHeader.Text = $"COMMANDER: {SupabaseService.CachedCommanderName.ToUpper()}  ·  Each Server Universe stores its own independent Base, Fleets & Leaderboard.";
			subHeader.HorizontalAlignment = HorizontalAlignment.Center;
			subHeader.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			subHeader.AddThemeFontSizeOverride("font_size", 12);
			subHeader.Modulate = new Color("#94A3B8");
			mainContent.AddChild(subHeader);

			// Scrollable Server List (Expands cleanly inside the centered panel)
			ScrollContainer scroll = new ScrollContainer
			{
				SizeFlagsVertical = SizeFlags.ExpandFill,
				HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
			};
			mainContent.AddChild(scroll);

			_serverListContainer = new VBoxContainer();
			_serverListContainer.AddThemeConstantOverride("separation", 10);
			_serverListContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			scroll.AddChild(_serverListContainer);

			PopulateServerCards();
		}

		private void PopulateServerCards()
		{
			_serverColonyBadgeLabels.Clear();
			_serverEnterButtons.Clear();

			foreach (var srv in GameData.Servers)
			{
				bool isOnline = srv.Status == "ONLINE";

				PanelContainer card = new PanelContainer
				{
					CustomMinimumSize = new Vector2(0, 86),
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					MouseFilter = MouseFilterEnum.Pass
				};

				StyleBoxFlat cardStyle = new StyleBoxFlat();
				cardStyle.BgColor = isOnline ? new Color(0.06f, 0.11f, 0.18f, 0.94f) : new Color(0.04f, 0.06f, 0.09f, 0.75f);
				cardStyle.SetCornerRadiusAll(8);
				cardStyle.SetBorderWidthAll(isOnline ? 2 : 1);
				cardStyle.BorderColor = isOnline ? new Color(0f, 0.94f, 1f, 0.65f) : new Color(0.3f, 0.35f, 0.4f, 0.45f);
				cardStyle.ContentMarginLeft = 16;
				cardStyle.ContentMarginRight = 16;
				cardStyle.ContentMarginTop = 12;
				cardStyle.ContentMarginBottom = 12;
				card.AddThemeStyleboxOverride("panel", cardStyle);

				HBoxContainer row = new HBoxContainer
				{
					Alignment = BoxContainer.AlignmentMode.Center,
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					MouseFilter = MouseFilterEnum.Pass
				};
				row.AddThemeConstantOverride("separation", 14);
				card.AddChild(row);

				// Info (takes expanding space)
				VBoxContainer infoBox = new VBoxContainer
				{
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					MouseFilter = MouseFilterEnum.Pass
				};
				infoBox.AddThemeConstantOverride("separation", 3);

				Label nameLbl = new Label
				{
					Text = $"{srv.Id} · {srv.Name}  [{srv.Speed:0.#}X SPEED]",
					Modulate = isOnline ? Colors.White : new Color("#64748B"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				nameLbl.AddThemeFontSizeOverride("font_size", 15);
				infoBox.AddChild(nameLbl);

				Label descLbl = new Label
				{
					Text = srv.Desc,
					Modulate = isOnline ? new Color("#94A3B8") : new Color("#475569"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				descLbl.AddThemeFontSizeOverride("font_size", 12);
				infoBox.AddChild(descLbl);

				Label statusBadge = new Label
				{
					Text = isOnline ? "● Scanning universe registry..." : "🔒 SERVER LOCKED — FUTURE EXPANSION",
					Modulate = isOnline ? new Color("#38BDF8") : new Color("#64748B"),
					AutowrapMode = TextServer.AutowrapMode.WordSmart
				};
				statusBadge.AddThemeFontSizeOverride("font_size", 11);
				infoBox.AddChild(statusBadge);
				_serverColonyBadgeLabels[srv.Id] = statusBadge;

				row.AddChild(infoBox);

				// Action button
				Button enterBtn = new Button
				{
					Text = isOnline ? $"ENTER ({srv.Speed:0.#}X)" : "LOCKED",
					Disabled = !isOnline,
					CustomMinimumSize = new Vector2(148, 46)
				};
				enterBtn.AddThemeFontSizeOverride("font_size", 13);
				enterBtn.Modulate = isOnline ? new Color("#00F0FF") : new Color("#64748B");
				string capturedServerId = srv.Id;
				enterBtn.Pressed += () => OnEnterServer(capturedServerId);
				row.AddChild(enterBtn);
				_serverEnterButtons[srv.Id] = enterBtn;

				_serverListContainer.AddChild(card);
			}
		}

		private async void RefreshServerColonyBadgesAsync()
		{
			if (_supabase == null) return;
			var summaries = await _supabase.FetchCommanderServerSummariesAsync();

			foreach (var srv in GameData.Servers)
			{
				if (srv.Status != "ONLINE") continue;
				if (!_serverColonyBadgeLabels.TryGetValue(srv.Id, out Label badge) || !IsInstanceValid(badge)) continue;

				if (summaries.TryGetValue(srv.Id, out var summary) && summary.HasColony)
				{
					badge.Text = $"✓ ACTIVE COLONY: Moon M-{summary.MoonId} : Slot B-{summary.SlotId:D2}  ·  Core Lvl {summary.CoreLevel}  ·  {summary.Population:N0} POP";
					badge.Modulate = new Color("#4ADE80");
					if (_serverEnterButtons.TryGetValue(srv.Id, out Button btn) && IsInstanceValid(btn))
					{
						btn.Text = $"RESUME ({srv.Speed:0.#}X)";
						btn.Modulate = new Color("#4ADE80");
					}
				}
				else
				{
					badge.Text = $"○ No Colony Established on {srv.Name} yet — Click to Initialize ({srv.Speed:0.#}x Speed)";
					badge.Modulate = new Color("#FBBF24");
					if (_serverEnterButtons.TryGetValue(srv.Id, out Button btn) && IsInstanceValid(btn))
					{
						btn.Text = $"DEPLOY ({srv.Speed:0.#}X)";
						btn.Modulate = new Color("#00F0FF");
					}
				}
			}
		}

		private void OnEnterServer(string serverId)
		{
			var srvDef = GameData.GetServerDef(serverId);
			if (srvDef.Status != "ONLINE") return;

			SupabaseService.SelectActiveServer(srvDef.Id, srvDef.Speed);
			GD.Print($"[SERVER SELECT] Entering Isolated Sector Universe: {srvDef.Id} ({srvDef.Name} @ {srvDef.Speed}x Speed)");
			GetTree().ChangeSceneToFile("res://Scenes/base_view.tscn");
		}
	}
}
