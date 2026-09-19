using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace MoonsTotalWar.Engine
{
	/// <summary>
	/// MOONS TOTAL WAR: SUPABASE HTTP & REST DATA SERVICE (C# Godot 4 Edition)
	/// Connected to Production Supabase Database: kaldvuinxvqtompmagxj
	/// </summary>
	public partial class SupabaseService : Node
	{
		public static SupabaseService Instance { get; private set; }

		[Export] public string SupabaseUrl = "https://kaldvuinxvqtompmagxj.supabase.co";
		[Export] public string SupabaseAnonKey = "sb_publishable_zR5AJCm-hlbUs7URghPZ5g_Jv9MsJGe";

		private HttpRequest _httpClient;
		private string _accessToken = "";
		private string _userId = "";

		public override void _Ready()
		{
			if (Instance == null)
			{
				Instance = this;
			}

			_httpClient = new HttpRequest();
			AddChild(_httpClient);
			GD.Print("[SUPABASE SERVICE] Initialized with Live Database Target: " + SupabaseUrl);
		}

		/// <summary>
		/// Registers a new Commander account using a unique internal email address.
		/// </summary>
		public async Task<(bool Success, string Error)> SignUpAsync(string username, string password)
		{
			string cleanUser = username.Trim().ToLower();
			if (string.IsNullOrEmpty(cleanUser) || string.IsNullOrEmpty(password))
			{
				return (false, "Username and Security Keycode required.");
			}

			string internalEmail = $"{cleanUser}@moons.global";
			string url = $"{SupabaseUrl}/auth/v1/signup";
			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}"
			};

			var payload = new Dictionary<string, object>
			{
				{ "email", internalEmail },
				{ "password", password }
			};

			string jsonBody = JsonSerializer.Serialize(payload);
			GD.Print($"[SUPABASE] Sending SignUp request for {cleanUser} ({internalEmail})...");

			Error err = _httpClient.Request(url, headers, HttpClient.Method.Post, jsonBody);
			if (err != Error.Ok)
			{
				return (false, $"HTTP Request failed: {err}");
			}

			var result = await ToSignal(_httpClient, HttpRequest.SignalName.RequestCompleted);
			long responseCode = (long)result[1];
			byte[] body = (byte[])result[3];
			string responseText = Encoding.UTF8.GetString(body);

			GD.Print($"[SUPABASE] SignUp Response Code: {responseCode} | Body: {responseText}");

			if (responseCode == 200 || responseCode == 201)
			{
				// Auto-login immediately after registration
				return await SignInAsync(username, password);
			}
			else
			{
				// If user already exists in cloud DB, proceed straight to SignIn
				if (responseText.Contains("already registered") || responseText.Contains("already_exists"))
				{
					GD.Print("[SUPABASE] User already registered in cloud DB. Redirecting to SignIn...");
					return await SignInAsync(username, password);
				}

				return (false, $"Auth Error ({responseCode}): {responseText}");
			}
		}

		/// <summary>
		/// Authenticates an existing Commander account.
		/// </summary>
		public async Task<(bool Success, string Error)> SignInAsync(string username, string password)
		{
			string cleanUser = username.Trim().ToLower();
			string internalEmail = $"{cleanUser}@moons.global";
			string url = $"{SupabaseUrl}/auth/v1/token?grant_type=password";
			string[] headers = new string[]
			{
				"Content-Type: application/json",
				$"apikey: {SupabaseAnonKey}"
			};

			var payload = new Dictionary<string, object>
			{
				{ "email", internalEmail },
				{ "password", password }
			};

			string jsonBody = JsonSerializer.Serialize(payload);
			GD.Print($"[SUPABASE] Sending SignIn request for {cleanUser}...");

			Error err = _httpClient.Request(url, headers, HttpClient.Method.Post, jsonBody);
			if (err != Error.Ok)
			{
				return (false, $"HTTP Request failed: {err}");
			}

			var result = await ToSignal(_httpClient, HttpRequest.SignalName.RequestCompleted);
			long responseCode = (long)result[1];
			byte[] body = (byte[])result[3];
			string responseText = Encoding.UTF8.GetString(body);

			GD.Print($"[SUPABASE] SignIn Response Code: {responseCode} | Body: {responseText}");

			if (responseCode == 200)
			{
				try
				{
					using var doc = JsonDocument.Parse(responseText);
					var root = doc.RootElement;
					if (root.TryGetProperty("access_token", out var tokenProp))
					{
						_accessToken = tokenProp.GetString();
					}
					if (root.TryGetProperty("user", out var userProp) && userProp.TryGetProperty("id", out var idProp))
					{
						_userId = idProp.GetString();
					}

					GD.Print($"[SUPABASE] Cloud Auth Granted! User ID: {_userId}");
					return (true, "");
				}
				catch (Exception ex)
				{
					return (false, $"Parsing Error: {ex.Message}");
				}
			}
			else
			{
				return (false, "Invalid Master Username or Security Keycode.");
			}
		}
	}
}
