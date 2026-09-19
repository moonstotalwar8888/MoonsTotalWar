using Godot;
using System;
using MoonsTotalWar.Engine;

namespace MoonsTotalWar.UI
{
	/// <summary>
	/// MOONS TOTAL WAR: CENTRAL COMMAND AUTHENTICATION PORTAL
	/// Handles Master Login, Registration, and Character Genesis Callsigns with Supabase C# Client.
	/// </summary>
	public partial class AuthScene : Control
	{
		private LineEdit _usernameInput;
		private LineEdit _passwordInput;
		private LineEdit _callsignInput;
		
		private Button _loginTabBtn;
		private Button _registerTabBtn;
		private Button _submitBtn;
		private Button _genesisBtn;
		private Button _backBtn;

		private VBoxContainer _authFormBox;
		private VBoxContainer _genesisFormBox;
		private Label _statusLabel;

		private bool _isRegisterMode = false;
		private SupabaseService _supabase;

		public override void _Ready()
		{
			_supabase = GetNodeOrNull<SupabaseService>("/root/SupabaseService");
			if (_supabase == null)
			{
				_supabase = new SupabaseService();
				AddChild(_supabase);
			}

			BuildAuthUI();
		}

		private void BuildAuthUI()
		{
			// Fullscreen background anchor
			AnchorsPreset = (int)LayoutPreset.FullRect;

			// Dark Glass Center Panel
			PanelContainer centerPanel = new PanelContainer();
			centerPanel.CustomMinimumSize = new Vector2(480, 520);
			centerPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
			AddChild(centerPanel);

			StyleBoxFlat panelStyle = new StyleBoxFlat();
			panelStyle.BgColor = new Color(0.04f, 0.06f, 0.10f, 0.95f);
			panelStyle.SetCornerRadiusAll(12);
			panelStyle.SetBorderWidthAll(2);
			panelStyle.BorderColor = new Color(0f, 0.94f, 1f, 0.8f);
			centerPanel.AddThemeStyleboxOverride("panel", panelStyle);

			VBoxContainer contentBox = new VBoxContainer();
			contentBox.AddThemeConstantOverride("separation", 15);
			
			MarginContainer margin = new MarginContainer();
			margin.AddThemeConstantOverride("margin_left", 25);
			margin.AddThemeConstantOverride("margin_right", 25);
			margin.AddThemeConstantOverride("margin_top", 25);
			margin.AddThemeConstantOverride("margin_bottom", 25);
			margin.AddChild(contentBox);
			centerPanel.AddChild(margin);

			// Title
			Label titleLabel = new Label();
			titleLabel.Text = "MOONS: TOTAL WAR";
			titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
			titleLabel.AddThemeFontSizeOverride("font_size", 24);
			titleLabel.Modulate = new Color(0f, 0.94f, 1f);
			contentBox.AddChild(titleLabel);

			Label subTitleLabel = new Label();
			subTitleLabel.Text = "CENTRAL COMMAND ACCESS PORTAL";
			subTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
			subTitleLabel.AddThemeFontSizeOverride("font_size", 11);
			subTitleLabel.Modulate = new Color(0.6f, 0.7f, 0.8f);
			contentBox.AddChild(subTitleLabel);

			// Tab switch buttons (LOGIN / REGISTER)
			HBoxContainer tabRow = new HBoxContainer();
			tabRow.Alignment = BoxContainer.AlignmentMode.Center;
			
			_loginTabBtn = new Button();
			_loginTabBtn.Text = "LOGIN";
			_loginTabBtn.CustomMinimumSize = new Vector2(180, 40);
			_loginTabBtn.Pressed += () => SetAuthMode(false);
			tabRow.AddChild(_loginTabBtn);

			_registerTabBtn = new Button();
			_registerTabBtn.Text = "REGISTER";
			_registerTabBtn.CustomMinimumSize = new Vector2(180, 40);
			_registerTabBtn.Pressed += () => SetAuthMode(true);
			tabRow.AddChild(_registerTabBtn);

			contentBox.AddChild(tabRow);

			// AUTH FORM BOX
			_authFormBox = new VBoxContainer();
			_authFormBox.AddThemeConstantOverride("separation", 10);
			contentBox.AddChild(_authFormBox);

			Label userLbl = new Label();
			userLbl.Text = "MASTER USERNAME:";
			userLbl.AddThemeFontSizeOverride("font_size", 10);
			_authFormBox.AddChild(userLbl);

			_usernameInput = new LineEdit();
			_usernameInput.PlaceholderText = "Enter callsign or username...";
			_usernameInput.CustomMinimumSize = new Vector2(0, 42);
			_authFormBox.AddChild(_usernameInput);

			Label passLbl = new Label();
			passLbl.Text = "SECURITY KEYCODE:";
			passLbl.AddThemeFontSizeOverride("font_size", 10);
			_authFormBox.AddChild(passLbl);

			_passwordInput = new LineEdit();
			_passwordInput.PlaceholderText = "Enter security key...";
			_passwordInput.Secret = true;
			_passwordInput.CustomMinimumSize = new Vector2(0, 42);
			_authFormBox.AddChild(_passwordInput);

			_submitBtn = new Button();
			_submitBtn.Text = "AUTHORIZE ACCESS";
			_submitBtn.CustomMinimumSize = new Vector2(0, 48);
			_submitBtn.Modulate = new Color(0f, 0.94f, 1f);
			_submitBtn.Pressed += OnSubmitAuth;
			_authFormBox.AddChild(_submitBtn);

			// GENESIS FORM BOX (Hidden by default)
			_genesisFormBox = new VBoxContainer();
			_genesisFormBox.AddThemeConstantOverride("separation", 10);
			_genesisFormBox.Visible = false;
			contentBox.AddChild(_genesisFormBox);

			Label genLbl = new Label();
			genLbl.Text = "INITIALIZE COMMANDER CALLSIGN:";
			genLbl.AddThemeFontSizeOverride("font_size", 10);
			_genesisFormBox.AddChild(genLbl);

			_callsignInput = new LineEdit();
			_callsignInput.PlaceholderText = "Commander Callsign...";
			_callsignInput.CustomMinimumSize = new Vector2(0, 42);
			_genesisFormBox.AddChild(_callsignInput);

			_genesisBtn = new Button();
			_genesisBtn.Text = "INITIALIZE COLONY";
			_genesisBtn.CustomMinimumSize = new Vector2(0, 48);
			_genesisBtn.Modulate = new Color(1f, 0.75f, 0f);
			_genesisBtn.Pressed += OnGenesisSubmit;
			_genesisFormBox.AddChild(_genesisBtn);

			// Status output label
			_statusLabel = new Label();
			_statusLabel.Text = "Awaiting portal credentials...";
			_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_statusLabel.AddThemeFontSizeOverride("font_size", 10);
			_statusLabel.Modulate = new Color(0.6f, 0.7f, 0.8f);
			contentBox.AddChild(_statusLabel);

			SetAuthMode(false);
		}

		private void SetAuthMode(bool isRegister)
		{
			_isRegisterMode = isRegister;
			_submitBtn.Text = isRegister ? "CREATE ACCOUNT" : "AUTHORIZE ACCESS";
			_statusLabel.Text = isRegister ? "Register new commander account." : "Enter credentials to log in.";
		}

		private async void OnSubmitAuth()
		{
			string user = _usernameInput.Text.Trim();
			string pass = _passwordInput.Text.Trim();

			if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
			{
				_statusLabel.Text = "ERROR: Username and password required.";
				_statusLabel.Modulate = Colors.Red;
				return;
			}

			_statusLabel.Text = "Transmitting authentication request...";
			_statusLabel.Modulate = Colors.Yellow;

			if (_isRegisterMode)
			{
				var (success, error) = await _supabase.SignUpAsync(user, pass);
				if (success)
				{
					_statusLabel.Text = "ACCOUNT CREATED! Please log in.";
					_statusLabel.Modulate = Colors.Green;
					SetAuthMode(false);
				}
				else
				{
					_statusLabel.Text = $"REGISTER ERROR: {error}";
					_statusLabel.Modulate = Colors.Red;
				}
			}
			else
			{
				var (success, error) = await _supabase.SignInAsync(user, pass);
				if (success)
				{
					_statusLabel.Text = "ACCESS GRANTED! Transitioning...";
					_statusLabel.Modulate = Colors.Green;
					
					GetTree().ChangeSceneToFile("res://Scenes/ServerSelect.tscn");
				}
				else
				{
					_statusLabel.Text = $"LOGIN DENIED: {error}";
					_statusLabel.Modulate = Colors.Red;
				}
			}
		}

		private void OnGenesisSubmit()
		{
			// Character Genesis logic
		}
	}
}
