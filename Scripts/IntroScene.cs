using Godot;
using System;
using MoonsTotalWar.Engine;

namespace MoonsTotalWar.UI
{
	/// <summary>
	/// MOONS TOTAL WAR: MASTER ANIMATED INTRO & PORTAL
	/// Handles animated background, story portal, authentication, and server selection.
	/// </summary>
	public partial class IntroScene : Control
	{
		private TextureRect _bgImage;
		private float _animTime = 0f;

		// UI Containers
		private CenterContainer _storyCenter;
		private CenterContainer _authCenter;
		private CenterContainer _serverCenter;

		// Auth Inputs & Buttons
		private PanelContainer _authCard;
		private PanelContainer _authModeBannerPanel;
		private Label _authModeBannerLabel;
		private Label _userFieldLabel;
		private Label _passFieldLabel;
		private LineEdit _usernameInput;
		private LineEdit _passwordInput;
		private Button _loginTabBtn;
		private Button _registerTabBtn;
		private Button _authSubmitBtn;
		private Label _authStatusLabel;
		private bool _isRegisterMode = false;

		// Legal / Copyright & Terms of Service Controls
		private HBoxContainer _termsAgreementRow;
		private CheckBox _termsCheckBox;
		private ColorRect _termsModalOverlay;

		private SupabaseService _supabase;

		public override void _Ready()
		{
			// Force Fullscreen Control Root
			SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

			_supabase = GetNodeOrNull<SupabaseService>("/root/SupabaseService");
			if (_supabase == null)
			{
				_supabase = new SupabaseService();
				_supabase.Name = "SupabaseService";
				AddChild(_supabase);
			}

			BuildCinematicBackground();
			BuildStoryIntroUI();
			BuildAuthUI();
			BuildServerSelectUI();
			BuildCopyrightFooterBar();
			BuildTermsModalOverlay();

			GetViewport().Connect("size_changed", Callable.From(OnViewportSizeChanged));
			OnViewportSizeChanged();

			// Show Story Intro First
			ShowState("STORY");

			// Start seamless 'login.wav' loop across Story -> Login -> Server Select
			MusicManager.PlayTrack(this, MusicManager.TRACK_LOGIN);
		}

		private void OnViewportSizeChanged()
		{
			if (_bgImage != null)
			{
				Vector2 vp = GetViewportRect().Size;
				_bgImage.CustomMinimumSize = new Vector2(Mathf.Max(1600f, vp.X + 240f), Mathf.Max(900f, vp.Y + 180f));
				_bgImage.Size = _bgImage.CustomMinimumSize;
			}
		}

		public override void _Process(double delta)
		{
			// 60FPS Subtle Pan & Breathing Zoom Loop
			_animTime += (float)delta;
			if (_bgImage != null)
			{
				float panX = MathF.Sin(_animTime * 0.3f) * 20f;
				float panY = MathF.Cos(_animTime * 0.25f) * 12f;
				float zoomScale = 1.05f + MathF.Sin(_animTime * 0.15f) * 0.03f;

				_bgImage.Position = new Vector2(-80f + panX, -50f + panY);
				_bgImage.Scale = new Vector2(zoomScale, zoomScale);
			}

			// Mobile Virtual Keyboard Auto-Lift
			if (_authCenter != null && _authCenter.Visible && DisplayServer.HasFeature(DisplayServer.Feature.VirtualKeyboard))
			{
				int kbHeight = DisplayServer.VirtualKeyboardGetHeight();
				_authCenter.Position = kbHeight > 0 ? new Vector2(0, -95f) : Vector2.Zero;
			}
		}

		private void BuildCinematicBackground()
		{
			_bgImage = new TextureRect();
			_bgImage.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			_bgImage.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
			_bgImage.CustomMinimumSize = new Vector2(1600, 900);
			_bgImage.Position = new Vector2(-80, -50);

			Texture2D bgTex = GD.Load<Texture2D>("res://Assets/login-bg.jpg");
			if (bgTex != null)
			{
				_bgImage.Texture = bgTex;
			}
			AddChild(_bgImage);

			// Translucent Dark Vignette
			ColorRect darkOverlay = new ColorRect();
			darkOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			darkOverlay.Color = new Color(0.02f, 0.04f, 0.08f, 0.55f);
			AddChild(darkOverlay);
		}

		private void BuildStoryIntroUI()
		{
			_storyCenter = new CenterContainer();
			_storyCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			AddChild(_storyCenter);

			VBoxContainer storyBox = new VBoxContainer();
			storyBox.CustomMinimumSize = new Vector2(650, 0);
			storyBox.AddThemeConstantOverride("separation", 14);
			_storyCenter.AddChild(storyBox);

			// Top Tactical Insignia Line
			Label insigniaLabel = new Label
			{
				Text = "★   GALACTIC SINGULARITY CHRONICLES   ★",
				HorizontalAlignment = HorizontalAlignment.Center,
				Modulate = new Color("#FBBF24")
			};
			insigniaLabel.AddThemeFontSizeOverride("font_size", 13);
			insigniaLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
			insigniaLabel.AddThemeConstantOverride("outline_size", 3);
			storyBox.AddChild(insigniaLabel);

			// Hero Title "MOONS" with Bold Crimson Styling, Heavy Outline & Drop Shadow
			Label titleLabel = new Label
			{
				Text = "M O O N S",
				HorizontalAlignment = HorizontalAlignment.Center
			};
			titleLabel.AddThemeFontSizeOverride("font_size", 62);
			titleLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.24f, 0.24f)); // Signature Crimson
			titleLabel.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.01f, 0.02f, 1.0f));
			titleLabel.AddThemeConstantOverride("outline_size", 6);
			titleLabel.AddThemeColorOverride("font_shadow_color", new Color(0.94f, 0.15f, 0.15f, 0.55f));
			titleLabel.AddThemeConstantOverride("shadow_offset_x", 0);
			titleLabel.AddThemeConstantOverride("shadow_offset_y", 4);
			storyBox.AddChild(titleLabel);

			// Armored Subtitle Emblem Badge "TOTAL WAR"
			HBoxContainer subBadgeRow = new HBoxContainer
			{
				Alignment = BoxContainer.AlignmentMode.Center
			};
			PanelContainer subBadge = new PanelContainer
			{
				CustomMinimumSize = new Vector2(340, 38)
			};
			StyleBoxFlat subBadgeStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.08f, 0.03f, 0.05f, 0.92f),
				BorderColor = new Color(0.96f, 0.24f, 0.24f, 0.95f),
				BorderWidthLeft = 2,
				BorderWidthRight = 2,
				BorderWidthTop = 1,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 6,
				CornerRadiusTopRight = 6,
				CornerRadiusBottomLeft = 6,
				CornerRadiusBottomRight = 6
			};
			subBadge.AddThemeStyleboxOverride("panel", subBadgeStyle);

			Label subTitleLabel = new Label
			{
				Text = "═══   T O T A L   W A R   ═══",
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Modulate = Colors.White
			};
			subTitleLabel.AddThemeFontSizeOverride("font_size", 20);
			subTitleLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
			subTitleLabel.AddThemeConstantOverride("outline_size", 3);
			subBadge.AddChild(subTitleLabel);
			subBadgeRow.AddChild(subBadge);
			storyBox.AddChild(subBadgeRow);

			// Upgraded Black Hole & 1,000 Moons Story Card
			PanelContainer storyCard = new PanelContainer();
			StyleBoxFlat cardStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.04f, 0.07f, 0.13f, 0.93f),
				BorderColor = new Color(0.94f, 0.27f, 0.27f),
				ShadowColor = new Color(0f, 0f, 0f, 0.75f),
				ShadowSize = 18
			};
			cardStyle.SetCornerRadiusAll(10);
			cardStyle.SetBorderWidthAll(2);
			storyCard.AddThemeStyleboxOverride("panel", cardStyle);

			MarginContainer margin = new MarginContainer();
			margin.AddThemeConstantOverride("margin_left", 26);
			margin.AddThemeConstantOverride("margin_right", 26);
			margin.AddThemeConstantOverride("margin_top", 20);
			margin.AddThemeConstantOverride("margin_bottom", 20);

			VBoxContainer storyContent = new VBoxContainer();
			storyContent.AddThemeConstantOverride("separation", 10);

			Label storyHeader = new Label
			{
				Text = "YEAR 2148  ·  SWALLOWED INTO THE SINGULARITY",
				HorizontalAlignment = HorizontalAlignment.Center,
				Modulate = new Color("#FBBF24")
			};
			storyHeader.AddThemeFontSizeOverride("font_size", 15);
			storyHeader.AddThemeColorOverride("font_outline_color", Colors.Black);
			storyHeader.AddThemeConstantOverride("outline_size", 2);
			storyContent.AddChild(storyHeader);

			Label bodyText = new Label
			{
				Text = "Earth's galaxy has been swallowed into the abyss of a supermassive Black Hole. Humanity escaped our dying world at the final hour—now fighting to survive among 1,000 shattered Moons lost deep inside the event horizon.\n\nWE ARE NOT ALONE IN THE DARK.\nEvery 6 months, THE AETHER-REAPER emerges from the singularity to purge the stars. Build up your Helium-3 (H3) Reserves, forge a mighty Empire, and dominate the Moons.",
				HorizontalAlignment = HorizontalAlignment.Center,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				Modulate = new Color(0.93f, 0.96f, 1.0f)
			};
			bodyText.AddThemeFontSizeOverride("font_size", 15);
			bodyText.AddThemeColorOverride("font_outline_color", Colors.Black);
			bodyText.AddThemeConstantOverride("outline_size", 2);
			storyContent.AddChild(bodyText);

			Label warCallout = new Label
			{
				Text = "⚔  TOTAL WAR IS COMING !!  ⚔",
				HorizontalAlignment = HorizontalAlignment.Center,
				Modulate = new Color(0.98f, 0.32f, 0.32f)
			};
			warCallout.AddThemeFontSizeOverride("font_size", 17);
			warCallout.AddThemeColorOverride("font_outline_color", Colors.Black);
			warCallout.AddThemeConstantOverride("outline_size", 3);
			storyContent.AddChild(warCallout);

			margin.AddChild(storyContent);
			storyCard.AddChild(margin);
			storyBox.AddChild(storyCard);

			// Enter Portal Button
			Button enterPortalBtn = new Button
			{
				Text = "⚡  ENTER WAR PORTAL  ⚡",
				CustomMinimumSize = new Vector2(0, 60)
			};
			enterPortalBtn.AddThemeFontSizeOverride("font_size", 19);
			enterPortalBtn.AddThemeColorOverride("font_color", Colors.White);
			enterPortalBtn.AddThemeColorOverride("font_outline_color", Colors.Black);
			enterPortalBtn.AddThemeConstantOverride("outline_size", 3);

			StyleBoxFlat btnStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.88f, 0.20f, 0.20f, 0.98f),
				BorderColor = new Color("#FBBF24"),
				ShadowColor = new Color(0.94f, 0.27f, 0.27f, 0.45f),
				ShadowSize = 12
			};
			btnStyle.SetCornerRadiusAll(8);
			btnStyle.SetBorderWidthAll(2);
			enterPortalBtn.AddThemeStyleboxOverride("normal", btnStyle);
			enterPortalBtn.AddThemeStyleboxOverride("hover", btnStyle);
			enterPortalBtn.AddThemeStyleboxOverride("pressed", btnStyle);

			enterPortalBtn.Pressed += OnEnterWarPortalPressed;
			storyBox.AddChild(enterPortalBtn);
		}

		private void BuildAuthUI()
		{
			_authCenter = new CenterContainer();
			_authCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			_authCenter.Visible = false;
			AddChild(_authCenter);

			VBoxContainer authBox = new VBoxContainer();
			authBox.CustomMinimumSize = new Vector2(560, 0);
			authBox.AddThemeConstantOverride("separation", 14);
			_authCenter.AddChild(authBox);

			_authCard = new PanelContainer();
			MarginContainer margin = new MarginContainer();
			margin.AddThemeConstantOverride("margin_left", 28);
			margin.AddThemeConstantOverride("margin_right", 28);
			margin.AddThemeConstantOverride("margin_top", 24);
			margin.AddThemeConstantOverride("margin_bottom", 24);

			VBoxContainer content = new VBoxContainer();
			content.AddThemeConstantOverride("separation", 14);

			Label header = new Label
			{
				Text = "CENTRAL COMMAND ACCESS PORTAL",
				HorizontalAlignment = HorizontalAlignment.Center,
				Modulate = Colors.White
			};
			header.AddThemeFontSizeOverride("font_size", 18);
			header.AddThemeColorOverride("font_outline_color", Colors.Black);
			header.AddThemeConstantOverride("outline_size", 3);
			content.AddChild(header);

			// Dynamic Mode Indicator Banner (Changes Color Clearly Between LOGIN & REGISTER)
			_authModeBannerPanel = new PanelContainer
			{
				CustomMinimumSize = new Vector2(0, 40)
			};
			_authModeBannerLabel = new Label
			{
				Text = "🔓 MODE: EXISTING COMMANDER LOGIN",
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
			_authModeBannerLabel.AddThemeFontSizeOverride("font_size", 15);
			_authModeBannerLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
			_authModeBannerLabel.AddThemeConstantOverride("outline_size", 2);
			_authModeBannerPanel.AddChild(_authModeBannerLabel);
			content.AddChild(_authModeBannerPanel);

			// Login/Register Tab Buttons
			HBoxContainer tabRow = new HBoxContainer
			{
				Alignment = BoxContainer.AlignmentMode.Center
			};
			tabRow.AddThemeConstantOverride("separation", 12);

			_loginTabBtn = new Button
			{
				Text = "🔓 LOGIN",
				CustomMinimumSize = new Vector2(235, 48),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_loginTabBtn.AddThemeFontSizeOverride("font_size", 15);
			_loginTabBtn.Pressed += () => SetAuthMode(false);
			tabRow.AddChild(_loginTabBtn);

			_registerTabBtn = new Button
			{
				Text = "🎖️ REGISTER NEW",
				CustomMinimumSize = new Vector2(235, 48),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_registerTabBtn.AddThemeFontSizeOverride("font_size", 15);
			_registerTabBtn.Pressed += () => SetAuthMode(true);
			tabRow.AddChild(_registerTabBtn);

			content.AddChild(tabRow);

			// Inputs
			_userFieldLabel = new Label
			{
				Text = "COMMANDER USERNAME:"
			};
			_userFieldLabel.AddThemeFontSizeOverride("font_size", 14);
			content.AddChild(_userFieldLabel);

			_usernameInput = new LineEdit
			{
				PlaceholderText = "Enter commander username...",
				CustomMinimumSize = new Vector2(0, 50)
			};
			_usernameInput.AddThemeFontSizeOverride("font_size", 16);
			content.AddChild(_usernameInput);

			_passFieldLabel = new Label
			{
				Text = "SECURITY KEYCODE:"
			};
			_passFieldLabel.AddThemeFontSizeOverride("font_size", 14);
			content.AddChild(_passFieldLabel);

			_passwordInput = new LineEdit
			{
				PlaceholderText = "Enter security keycode...",
				Secret = true,
				CustomMinimumSize = new Vector2(0, 50),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_passwordInput.AddThemeFontSizeOverride("font_size", 16);
			_usernameInput.TextSubmitted += (_) => _passwordInput.GrabFocus();
			_passwordInput.TextSubmitted += (_) => OnSubmitAuth();

			HBoxContainer passRow = new HBoxContainer();
			passRow.AddThemeConstantOverride("separation", 8);
			passRow.AddChild(_passwordInput);

			Button showPassBtn = new Button
			{
				Text = "👁",
				CustomMinimumSize = new Vector2(50, 50),
				TooltipText = "Show / Hide Security Keycode"
			};
			showPassBtn.AddThemeFontSizeOverride("font_size", 16);
			showPassBtn.Pressed += () =>
			{
				_passwordInput.Secret = !_passwordInput.Secret;
				showPassBtn.Modulate = _passwordInput.Secret ? Colors.White : new Color("#00F0FF");
			};
			passRow.AddChild(showPassBtn);
			content.AddChild(passRow);

			if (_supabase != null)
			{
				(string savedUser, string savedPass) = _supabase.LoadSavedLoginCredentials();
				if (!string.IsNullOrEmpty(savedUser)) _usernameInput.Text = savedUser;
				if (!string.IsNullOrEmpty(savedPass)) _passwordInput.Text = savedPass;
			}

			// Terms & Conditions Checkbox Row (Visible in REGISTER Mode)
			_termsAgreementRow = new HBoxContainer
			{
				Alignment = BoxContainer.AlignmentMode.Center,
				Visible = false
			};
			_termsAgreementRow.AddThemeConstantOverride("separation", 8);

			_termsCheckBox = new CheckBox
			{
				Text = "I accept the Terms, Privacy & Copyright Agreement",
				CustomMinimumSize = new Vector2(0, 38),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			_termsCheckBox.AddThemeFontSizeOverride("font_size", 13);
			_termsCheckBox.Modulate = new Color("#FDE68A");
			_termsAgreementRow.AddChild(_termsCheckBox);

			Button viewTermsBtn = new Button
			{
				Text = "📜 READ TERMS",
				CustomMinimumSize = new Vector2(126, 38)
			};
			viewTermsBtn.AddThemeFontSizeOverride("font_size", 12);
			viewTermsBtn.Modulate = new Color("#00F0FF");
			viewTermsBtn.Pressed += () =>
			{
				if (_termsModalOverlay != null) _termsModalOverlay.Visible = true;
			};
			_termsAgreementRow.AddChild(viewTermsBtn);
			content.AddChild(_termsAgreementRow);

			_authSubmitBtn = new Button
			{
				Text = "AUTHORIZE ACCESS",
				CustomMinimumSize = new Vector2(0, 56)
			};
			_authSubmitBtn.AddThemeFontSizeOverride("font_size", 17);
			_authSubmitBtn.AddThemeColorOverride("font_color", Colors.White);
			_authSubmitBtn.AddThemeColorOverride("font_outline_color", Colors.Black);
			_authSubmitBtn.AddThemeConstantOverride("outline_size", 2);
			_authSubmitBtn.Pressed += OnSubmitAuth;
			content.AddChild(_authSubmitBtn);

			Button sandboxBypassBtn = new Button
			{
				Text = "🧪 TESTER QUICK-ENTRY (SANDBOX MODE)",
				CustomMinimumSize = new Vector2(0, 44)
			};
			sandboxBypassBtn.AddThemeFontSizeOverride("font_size", 13);
			sandboxBypassBtn.Modulate = new Color("#4ADE80");
			sandboxBypassBtn.Pressed += () =>
			{
				GD.Print("[INTRO] Tester Sandbox Quick-Entry Activated -> Opening Universes.");
				ShowState("SERVER");
			};
			content.AddChild(sandboxBypassBtn);

			_authStatusLabel = new Label
			{
				Text = "Awaiting portal credentials...",
				HorizontalAlignment = HorizontalAlignment.Center,
				Modulate = new Color(0.75f, 0.85f, 0.95f)
			};
			_authStatusLabel.AddThemeFontSizeOverride("font_size", 13);
			content.AddChild(_authStatusLabel);

			// Back to Story
			Button backBtn = new Button
			{
				Text = "← BACK TO STORY BRIEFING",
				CustomMinimumSize = new Vector2(0, 38),
				Flat = true
			};
			backBtn.AddThemeFontSizeOverride("font_size", 13);
			backBtn.Modulate = new Color("#94A3B8");
			backBtn.Pressed += () => ShowState("STORY");
			content.AddChild(backBtn);

			margin.AddChild(content);
			_authCard.AddChild(margin);
			authBox.AddChild(_authCard);

			SetAuthMode(false);
		}

		private void BuildServerSelectUI()
		{
			_serverCenter = new CenterContainer();
			_serverCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			_serverCenter.Visible = false;
			AddChild(_serverCenter);

			VBoxContainer serverBox = new VBoxContainer();
			serverBox.CustomMinimumSize = new Vector2(620, 0);
			serverBox.AddThemeConstantOverride("separation", 16);
			_serverCenter.AddChild(serverBox);

			PanelContainer card = new PanelContainer();
			StyleBoxFlat cardStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.04f, 0.06f, 0.10f, 0.96f),
				BorderColor = new Color(0f, 0.94f, 1f, 0.85f)
			};
			cardStyle.SetCornerRadiusAll(10);
			cardStyle.SetBorderWidthAll(2);
			card.AddThemeStyleboxOverride("panel", cardStyle);

			MarginContainer margin = new MarginContainer();
			margin.AddThemeConstantOverride("margin_left", 24);
			margin.AddThemeConstantOverride("margin_right", 24);
			margin.AddThemeConstantOverride("margin_top", 22);
			margin.AddThemeConstantOverride("margin_bottom", 22);

			VBoxContainer content = new VBoxContainer();
			content.AddThemeConstantOverride("separation", 14);

			Label header = new Label
			{
				Text = "SELECT OPERATIONAL UNIVERSE",
				HorizontalAlignment = HorizontalAlignment.Center,
				Modulate = new Color(0f, 0.94f, 1f)
			};
			header.AddThemeFontSizeOverride("font_size", 20);
			header.AddThemeColorOverride("font_outline_color", Colors.Black);
			header.AddThemeConstantOverride("outline_size", 3);
			content.AddChild(header);

			ScrollContainer srvScroll = new ScrollContainer
			{
				CustomMinimumSize = new Vector2(0, 360),
				HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
			};
			VBoxContainer srvList = new VBoxContainer
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};
			srvList.AddThemeConstantOverride("separation", 12);
			srvScroll.AddChild(srvList);
			content.AddChild(srvScroll);

			foreach (var srv in GameData.Servers)
			{
				PanelContainer srvCard = new PanelContainer
				{
					MouseFilter = MouseFilterEnum.Pass
				};
				StyleBoxFlat sStyle = new StyleBoxFlat
				{
					BgColor = new Color(0.08f, 0.12f, 0.18f, 0.94f),
					BorderColor = srv.Status == "ONLINE" ? new Color(0f, 0.94f, 1f, 0.55f) : new Color(0.3f, 0.35f, 0.4f, 0.4f)
				};
				sStyle.SetCornerRadiusAll(8);
				sStyle.SetBorderWidthAll(1);
				srvCard.AddThemeStyleboxOverride("panel", sStyle);

				HBoxContainer row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };

				VBoxContainer info = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
				info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				Label n = new Label { Text = srv.Name, Modulate = Colors.White };
				n.AddThemeFontSizeOverride("font_size", 16);
				info.AddChild(n);

				Label d = new Label
				{
					Text = srv.Desc,
					Modulate = new Color(0.7f, 0.8f, 0.9f)
				};
				d.AddThemeFontSizeOverride("font_size", 13);
				info.AddChild(d);

				row.AddChild(info);

				Button enterBtn = new Button
				{
					Text = srv.Status == "ONLINE" ? $"DEPLOY ({srv.Speed:0.#}X)" : "LOCKED",
					CustomMinimumSize = new Vector2(140, 46),
					Disabled = srv.Status != "ONLINE"
				};
				enterBtn.AddThemeFontSizeOverride("font_size", 14);
				enterBtn.Modulate = srv.Status == "ONLINE" ? new Color("#22C55E") : new Color("#64748B");
				string capturedServerId = srv.Id;
				float capturedSpeed = srv.Speed;
				enterBtn.Pressed += () =>
				{
					SupabaseService.SelectActiveServer(capturedServerId, capturedSpeed);
					OnLaunchBaseView();
				};
				row.AddChild(enterBtn);

				MarginContainer pad = new MarginContainer { MouseFilter = MouseFilterEnum.Pass };
				pad.AddThemeConstantOverride("margin_left", 14);
				pad.AddThemeConstantOverride("margin_right", 14);
				pad.AddThemeConstantOverride("margin_top", 10);
				pad.AddThemeConstantOverride("margin_bottom", 10);
				pad.AddChild(row);

				srvCard.AddChild(pad);
				srvList.AddChild(srvCard);
			}

			margin.AddChild(content);
			card.AddChild(margin);
			serverBox.AddChild(card);
		}

		private void OnEnterWarPortalPressed()
		{
			GD.Print("[INTRO] Enter War Portal Pressed -> Transitioning to Auth Portal.");
			ShowState("AUTH");
		}

		private void ShowState(string state)
		{
			_storyCenter.Visible = state == "STORY";
			_authCenter.Visible = state == "AUTH";
			_serverCenter.Visible = state == "SERVER";
		}

		private void SetAuthMode(bool isRegister)
		{
			_isRegisterMode = isRegister;

			// Theme Colors: LOGIN = Tactical Cyan (#00F0FF / #0284C7), REGISTER = Imperial Amber-Crimson (#FBBF24 / #DC2626)
			Color primaryBorder = isRegister ? new Color("#F59E0B") : new Color("#00F0FF");
			Color cardBg = isRegister ? new Color(0.11f, 0.05f, 0.04f, 0.96f) : new Color(0.03f, 0.07f, 0.12f, 0.96f);
			Color bannerBg = isRegister ? new Color(0.35f, 0.12f, 0.04f, 0.95f) : new Color(0.02f, 0.22f, 0.34f, 0.95f);
			Color submitBg = isRegister ? new Color(0.86f, 0.20f, 0.14f, 0.98f) : new Color(0.02f, 0.52f, 0.78f, 0.98f);

			if (_authCard != null)
			{
				StyleBoxFlat cardStyle = new StyleBoxFlat
				{
					BgColor = cardBg,
					BorderColor = primaryBorder,
					ShadowColor = new Color(primaryBorder.R, primaryBorder.G, primaryBorder.B, 0.35f),
					ShadowSize = 18
				};
				cardStyle.SetCornerRadiusAll(12);
				cardStyle.SetBorderWidthAll(3);
				_authCard.AddThemeStyleboxOverride("panel", cardStyle);
			}

			if (_authModeBannerPanel != null)
			{
				StyleBoxFlat bannerStyle = new StyleBoxFlat
				{
					BgColor = bannerBg,
					BorderColor = primaryBorder
				};
				bannerStyle.SetCornerRadiusAll(6);
				bannerStyle.SetBorderWidthAll(1);
				_authModeBannerPanel.AddThemeStyleboxOverride("panel", bannerStyle);
			}

			if (_authModeBannerLabel != null)
			{
				_authModeBannerLabel.Text = isRegister
					? "🎖️  NEW COMMANDER REGISTRATION  (CREATE ACCOUNT)"
					: "🔓  EXISTING COMMANDER LOGIN  (ENTER CREDENTIALS)";
				_authModeBannerLabel.Modulate = isRegister ? new Color("#FBBF24") : new Color("#00F0FF");
			}

			// Style Active vs Inactive Tabs so it is 100% obvious which mode is active
			if (_loginTabBtn != null && _registerTabBtn != null)
			{
				StyleBoxFlat activeLoginStyle = new StyleBoxFlat
				{
					BgColor = new Color(0.02f, 0.48f, 0.72f, 0.98f),
					BorderColor = new Color("#00F0FF")
				};
				activeLoginStyle.SetCornerRadiusAll(6);
				activeLoginStyle.SetBorderWidthAll(2);

				StyleBoxFlat activeRegisterStyle = new StyleBoxFlat
				{
					BgColor = new Color(0.85f, 0.35f, 0.06f, 0.98f),
					BorderColor = new Color("#FBBF24")
				};
				activeRegisterStyle.SetCornerRadiusAll(6);
				activeRegisterStyle.SetBorderWidthAll(2);

				StyleBoxFlat inactiveTabStyle = new StyleBoxFlat
				{
					BgColor = new Color(0.08f, 0.11f, 0.16f, 0.75f),
					BorderColor = new Color(0.3f, 0.35f, 0.45f, 0.5f)
				};
				inactiveTabStyle.SetCornerRadiusAll(6);
				inactiveTabStyle.SetBorderWidthAll(1);

				_loginTabBtn.AddThemeStyleboxOverride("normal", !isRegister ? activeLoginStyle : inactiveTabStyle);
				_loginTabBtn.AddThemeStyleboxOverride("hover", !isRegister ? activeLoginStyle : inactiveTabStyle);
				_loginTabBtn.AddThemeStyleboxOverride("pressed", !isRegister ? activeLoginStyle : inactiveTabStyle);
				_loginTabBtn.Modulate = !isRegister ? Colors.White : new Color(0.6f, 0.68f, 0.78f);

				_registerTabBtn.AddThemeStyleboxOverride("normal", isRegister ? activeRegisterStyle : inactiveTabStyle);
				_registerTabBtn.AddThemeStyleboxOverride("hover", isRegister ? activeRegisterStyle : inactiveTabStyle);
				_registerTabBtn.AddThemeStyleboxOverride("pressed", isRegister ? activeRegisterStyle : inactiveTabStyle);
				_registerTabBtn.Modulate = isRegister ? Colors.White : new Color(0.6f, 0.68f, 0.78f);
			}

			if (_userFieldLabel != null)
			{
				_userFieldLabel.Text = isRegister ? "CREATE NEW COMMANDER USERNAME:" : "EXISTING COMMANDER USERNAME:";
				_userFieldLabel.Modulate = isRegister ? new Color("#FBBF24") : new Color("#38BDF8");
			}
			if (_passFieldLabel != null)
			{
				_passFieldLabel.Text = isRegister ? "CREATE NEW SECURITY KEYCODE:" : "ENTER SECURITY KEYCODE:";
				_passFieldLabel.Modulate = isRegister ? new Color("#FBBF24") : new Color("#38BDF8");
			}
			if (_usernameInput != null)
			{
				_usernameInput.PlaceholderText = isRegister ? "Choose your new commander callsign..." : "Enter your registered username...";
			}
			if (_passwordInput != null)
			{
				_passwordInput.PlaceholderText = isRegister ? "Create a strong security keycode..." : "Enter your security keycode...";
			}

			if (_termsAgreementRow != null)
			{
				_termsAgreementRow.Visible = isRegister;
			}

			if (_authSubmitBtn != null)
			{
				_authSubmitBtn.Text = isRegister ? "🎖️  ENLIST & CREATE NEW ACCOUNT" : "🔓  AUTHORIZE COMMAND LOGIN";
				StyleBoxFlat submitStyle = new StyleBoxFlat
				{
					BgColor = submitBg,
					BorderColor = primaryBorder
				};
				submitStyle.SetCornerRadiusAll(8);
				submitStyle.SetBorderWidthAll(2);
				_authSubmitBtn.AddThemeStyleboxOverride("normal", submitStyle);
				_authSubmitBtn.AddThemeStyleboxOverride("hover", submitStyle);
				_authSubmitBtn.AddThemeStyleboxOverride("pressed", submitStyle);
			}

			if (_authStatusLabel != null)
			{
				_authStatusLabel.Text = isRegister
					? "Registration Mode Active: Tick the Terms & Copyright box to enlist."
					: "Login Mode Active: Enter your existing Commander credentials.";
				_authStatusLabel.Modulate = isRegister ? new Color("#FDE68A") : new Color("#7DD3FC");
			}
		}

		private async void OnSubmitAuth()
		{
			string user = _usernameInput.Text.Trim();
			string pass = _passwordInput.Text.Trim();

			if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
			{
				_authStatusLabel.Text = "ERROR: Master Username and Keycode required.";
				_authStatusLabel.Modulate = Colors.Red;
				return;
			}

			if (_isRegisterMode && (_termsCheckBox == null || !_termsCheckBox.ButtonPressed))
			{
				_authStatusLabel.Text = "REQUIRED: Please tick the Terms & Copyright box before registering.";
				_authStatusLabel.Modulate = new Color("#EF4444");
				return;
			}

			_authStatusLabel.Text = "Transmitting authentication request...";
			_authStatusLabel.Modulate = Colors.Yellow;

			if (_isRegisterMode)
			{
				var (success, error) = await _supabase.SignUpAsync(user, pass);
				if (success)
				{
					_authStatusLabel.Text = "ACCOUNT CREATED! Opening Universes...";
					_authStatusLabel.Modulate = Colors.Green;
					ShowState("SERVER");
				}
				else
				{
					_authStatusLabel.Text = $"REGISTER ERROR: {error}";
					_authStatusLabel.Modulate = Colors.Red;
				}
			}
			else
			{
				var (success, error) = await _supabase.SignInAsync(user, pass);
				if (success)
				{
					_authStatusLabel.Text = "ACCESS GRANTED! Opening Universes...";
					_authStatusLabel.Modulate = Colors.Green;
					ShowState("SERVER");
				}
				else
				{
					_authStatusLabel.Text = $"LOGIN DENIED: {error}";
					_authStatusLabel.Modulate = Colors.Red;
				}
			}
		}

		private void BuildCopyrightFooterBar()
		{
			Panel footerBar = new Panel();
			footerBar.SetAnchorsPreset(LayoutPreset.BottomWide);
			footerBar.CustomMinimumSize = new Vector2(0, 34);
			footerBar.OffsetTop = -34;
			footerBar.OffsetBottom = 0;
			footerBar.MouseFilter = MouseFilterEnum.Ignore;

			StyleBoxFlat fStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.01f, 0.02f, 0.05f, 0.88f),
				BorderColor = new Color(0.96f, 0.24f, 0.24f, 0.4f),
				BorderWidthTop = 1
			};
			footerBar.AddThemeStyleboxOverride("panel", fStyle);
			AddChild(footerBar);

			HBoxContainer footerRow = new HBoxContainer();
			footerRow.SetAnchorsPreset(LayoutPreset.FullRect);
			footerRow.Alignment = BoxContainer.AlignmentMode.Center;
			footerRow.AddThemeConstantOverride("separation", 18);

			Label copyLbl = new Label
			{
				Text = "© 2026 MOONS: TOTAL WAR  ·  ALL RIGHTS RESERVED  ·  PROTECTED INTELLECTUAL PROPERTY",
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Modulate = new Color("#94A3B8")
			};
			copyLbl.AddThemeFontSizeOverride("font_size", 12);
			copyLbl.AddThemeColorOverride("font_outline_color", Colors.Black);
			copyLbl.AddThemeConstantOverride("outline_size", 2);
			footerRow.AddChild(copyLbl);

			Button footerTermsBtn = new Button
			{
				Text = "[ 📜 LEGAL & TERMS OF SERVICE ]",
				Flat = true,
				MouseFilter = MouseFilterEnum.Stop
			};
			footerTermsBtn.AddThemeFontSizeOverride("font_size", 12);
			footerTermsBtn.Modulate = new Color("#FBBF24");
			footerTermsBtn.Pressed += () =>
			{
				if (_termsModalOverlay != null) _termsModalOverlay.Visible = true;
			};
			footerRow.AddChild(footerTermsBtn);

			footerBar.AddChild(footerRow);
		}

		private void BuildTermsModalOverlay()
		{
			_termsModalOverlay = new ColorRect
			{
				Color = new Color(0f, 0f, 0f, 0.85f),
				Visible = false,
				ZIndex = 200,
				MouseFilter = MouseFilterEnum.Stop
			};
			_termsModalOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			AddChild(_termsModalOverlay);

			CenterContainer center = new CenterContainer();
			center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			_termsModalOverlay.AddChild(center);

			PanelContainer modalCard = new PanelContainer
			{
				CustomMinimumSize = new Vector2(640, 520)
			};
			StyleBoxFlat mStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.03f, 0.06f, 0.11f, 0.98f),
				BorderColor = new Color("#FBBF24"),
				ShadowColor = new Color(0f, 0f, 0f, 0.8f),
				ShadowSize = 24
			};
			mStyle.SetCornerRadiusAll(10);
			mStyle.SetBorderWidthAll(2);
			modalCard.AddThemeStyleboxOverride("panel", mStyle);
			center.AddChild(modalCard);

			MarginContainer margin = new MarginContainer();
			margin.AddThemeConstantOverride("margin_left", 24);
			margin.AddThemeConstantOverride("margin_right", 24);
			margin.AddThemeConstantOverride("margin_top", 20);
			margin.AddThemeConstantOverride("margin_bottom", 20);
			modalCard.AddChild(margin);

			VBoxContainer stack = new VBoxContainer();
			stack.AddThemeConstantOverride("separation", 12);
			margin.AddChild(stack);

			Label title = new Label
			{
				Text = "📜  OFFICIAL COPYRIGHT, EULA & TERMS OF SERVICE",
				HorizontalAlignment = HorizontalAlignment.Center,
				Modulate = new Color("#FBBF24")
			};
			title.AddThemeFontSizeOverride("font_size", 18);
			title.AddThemeColorOverride("font_outline_color", Colors.Black);
			title.AddThemeConstantOverride("outline_size", 3);
			stack.AddChild(title);

			stack.AddChild(new HSeparator());

			ScrollContainer scroll = new ScrollContainer
			{
				CustomMinimumSize = new Vector2(0, 360),
				SizeFlagsVertical = SizeFlags.ExpandFill,
				HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
			};
			stack.AddChild(scroll);

			Label legalBody = new Label
			{
				Text =
					"COPYRIGHT NOTICE — © 2026 MOONS: TOTAL WAR. ALL RIGHTS RESERVED.\n" +
					"All game code, 2.5D visual assets, UI designs, soundscapes, mathematical formulas, and universe lore (including 'The Singularity', '1,000 Shattered Moons', and 'The Aether-Reaper') are the exclusive copyrighted intellectual property of the Creator(s) of MOONS: TOTAL WAR.\n\n" +
					"1. STRICT ANTI-PIRACY & LICENSE RESTRICTIONS\n" +
					"You are granted a personal, non-exclusive, revocable license to play MOONS: TOTAL WAR. You may NOT copy, redistribute, decompile, reverse-engineer, modify, or extract source code or assets from the APK or PC client under any circumstances.\n\n" +
					"2. ALPHA / BETA TESTING & SERVER RESET DISCLAIMER\n" +
					"You acknowledge that MOONS: TOTAL WAR is in active live testing. Game balance, building timers, Helium-3 (H3) yields, and server economies may be updated, rebalanced, or reset at any time without liability.\n\n" +
					"3. VIRTUAL CURRENCY (GALAXY GOLD / GGOLD)\n" +
					"In-game currencies ('Galaxy Gold' / 'GGold'), production boosts, and virtual items have no real-world monetary cash value, cannot be redeemed for fiat currency, and remain licensed digital features of the game.\n\n" +
					"4. COMMANDER CODE OF CONDUCT & FAIR PLAY\n" +
					"Use of automation bots, packet manipulation, exploit abuse, or harassment in Alliance/Comms channels will result in immediate account suspension or permanent ban.\n\n" +
					"5. DATA & PRIVACY\n" +
					"Your Commander credentials, colony layout, and resource balances are securely stored via encrypted cloud database services solely for operating your account and leaderboards.",
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass,
				Modulate = new Color(0.92f, 0.95f, 1.0f)
			};
			legalBody.AddThemeFontSizeOverride("font_size", 14);
			scroll.AddChild(legalBody);

			HBoxContainer btnRow = new HBoxContainer
			{
				Alignment = BoxContainer.AlignmentMode.Center
			};
			btnRow.AddThemeConstantOverride("separation", 16);

			Button acceptAndCloseBtn = new Button
			{
				Text = "✓  I ACCEPT & AGREE TO TERMS",
				CustomMinimumSize = new Vector2(260, 46)
			};
			acceptAndCloseBtn.AddThemeFontSizeOverride("font_size", 15);
			acceptAndCloseBtn.Modulate = new Color("#22C55E");
			acceptAndCloseBtn.Pressed += () =>
			{
				if (_termsCheckBox != null) _termsCheckBox.ButtonPressed = true;
				_termsModalOverlay.Visible = false;
			};
			btnRow.AddChild(acceptAndCloseBtn);

			Button closeBtn = new Button
			{
				Text = "CLOSE",
				CustomMinimumSize = new Vector2(120, 46)
			};
			closeBtn.AddThemeFontSizeOverride("font_size", 14);
			closeBtn.Modulate = new Color("#94A3B8");
			closeBtn.Pressed += () => _termsModalOverlay.Visible = false;
			btnRow.AddChild(closeBtn);

			stack.AddChild(btnRow);
		}

		private void OnLaunchBaseView()
		{
			GD.Print("[INTRO] Launching BaseView Scene...");
			GetTree().ChangeSceneToFile("res://Scenes/base_view.tscn");
		}
	}
}
