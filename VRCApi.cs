using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VRChat.API.Api;
using VRChat.API.Client;
using VRChat.API.Model;
using File = System.IO.File;
namespace BVCC
{
    public class VRCApi
    {
        private TaskCompletionSource<string> _tfaCompletionSource;
        public void Provide2FACode(string code)
        {
            _tfaCompletionSource?.TrySetResult(code);
        }
        public void Cancel2FA()
        {
            _tfaCompletionSource?.TrySetCanceled();
        }
        public IVRChat _client;
        private readonly string _username;
        private readonly string _password;
        private DateTime _lastRequestTime = DateTime.MinValue;
        private static readonly TimeSpan MinDelay = TimeSpan.FromMilliseconds(400);
        private static readonly string SessionFile = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "session.dat"
        );
        private readonly Dictionary<string, Avatar> _avatarCache = new();
        public bool IsLoggedIn { get; private set; }
        public string CurrentUserName { get; private set; } = string.Empty;
        public VRCApi(string username, string password)
        {
            _username = username;
            _password = password;
            _client = BuildClient();
        }
        public async Task<(bool success, string message)> LoginAsync()
        {
            try
            {
                var user = await _client.Authentication.GetCurrentUserAsync();
                if (user.RequiresTwoFactorAuth?.Count > 0)
                {
                    Debug.WriteLine("2FA required: " + string.Join(", ", user.RequiresTwoFactorAuth));
                    var twoFaResult = await Handle2FAAsync(user.RequiresTwoFactorAuth);
                    return twoFaResult
                        ? (true, "2FA successful login")
                        : (false, "2FA failed or cancelled");
                }
                var ok = await FinalizeLoginAsync(user);
                return ok
                    ? (true, $"Logged in as {user.DisplayName}")
                    : (false, "Finalize login failed");
            }
            catch (VRChat.API.Client.ApiException ex)
            {
                return (false, $"VRChat API error {ex.ErrorCode}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Login error: {ex}");
                return (false, ex.Message);
            }
        }
        public async Task<(bool success, string message)> TryRestoreSessionAsync()
        {
            if (!File.Exists(SessionFile))
                return (false, "No session file found.");
            try
            {
                var cookies = LoadSessionCookies();
                if (cookies == null || cookies.Count == 0)
                    return (false, "Session file is empty or corrupted.");
                var authCookie = cookies.FirstOrDefault(c => c.Name == "auth");
                var twoFactorCookie = cookies.FirstOrDefault(c => c.Name == "twoFactorAuth");
                if (authCookie == null || string.IsNullOrWhiteSpace(authCookie.Value))
                    return (false, "Missing auth cookie (session expired).");
                _client = BuildClient(authCookie.Value, twoFactorCookie?.Value);
                var user = await _client.Authentication.GetCurrentUserAsync();
                if (user == null)
                    return (false, "API returned null user (invalid session).");
                if (user.RequiresTwoFactorAuth != null &&
                    user.RequiresTwoFactorAuth.Count > 0)
                {
                    DeleteSession();
                    return (false, "Session requires 2FA again (expired session).");
                }
                IsLoggedIn = true;
                CurrentUserName = user.DisplayName;
                return (true, $"Session restored as {CurrentUserName}");
            }
            catch (VRChat.API.Client.ApiException ex)
            {
                DeleteSession();
                return (false, $"VRChat API error {ex.ErrorCode}: {ex.Message}");
            }
            catch (Exception ex)
            {
                DeleteSession();
                return (false, $"Unexpected error: {ex.Message}");
            }
        }
        public void Logout()
        {
            IsLoggedIn = false;
            CurrentUserName = string.Empty;
            _avatarCache.Clear();
            DeleteSession();
            _client = BuildClient();
        }
        public async Task<User> GetUserAsync(string userId)
        {
            EnsureLoggedIn();
            await ThrottleAsync();
            return await _client.Users.GetUserAsync(userId);
        }
        public async Task<World> GetWorldAsync(string worldId)
        {
            EnsureLoggedIn();
            await ThrottleAsync();
            return await _client.Worlds.GetWorldAsync(worldId);
        }
        public async Task<Avatar> GetAvatarAsync(string avatarId)
        {
            EnsureLoggedIn();
            if (_avatarCache.TryGetValue(avatarId, out var cached))
                return cached;
            await ThrottleAsync();
            var avatar = await _client.Avatars.GetAvatarAsync(avatarId);
            _avatarCache[avatarId] = avatar;
            return avatar;
        }
        private IVRChat BuildClient(string? authCookie = null, string? twoFactorCookie = null)
        {
            var builder = new VRChatClientBuilder()
                .WithCredentials(_username, _password)
                .WithUserAgent($"{App.savedata.AppName}/{App.savedata.AppVersion}");
            if (!string.IsNullOrEmpty(authCookie))
                builder = builder.WithAuthCookie(authCookie, twoFactorCookie ?? "");
            return builder.Build();
        }
        private async Task<bool> Handle2FAAsync(List<string> required)
        {
            _tfaCompletionSource = new TaskCompletionSource<string>();
            try
            {
                string code = await _tfaCompletionSource.Task;
                if (required.Contains("emailOtp"))
                    await _client.Authentication.Verify2FAEmailCodeAsync(new TwoFactorEmailCode(code));
                else if (required.Contains("totp"))
                    await _client.Authentication.Verify2FAAsync(new TwoFactorAuthCode(code));
                var user = await _client.Authentication.GetCurrentUserAsync();
                return await FinalizeLoginAsync(user);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"2FA failed: {ex}");
                return false;
            }
        }
        private async Task<bool> FinalizeLoginAsync(CurrentUser user)
        {
            IsLoggedIn = true;
            CurrentUserName = user.DisplayName;
            SaveSession();
            Debug.WriteLine($"Logged in as {CurrentUserName}");
            return await Task.FromResult(true);
        }
        private void SaveSession()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SessionFile)!);
                var cookies = _client.GetCookies()
                    .Select(c => new SavedCookie
                    {
                        Name = c.Name,
                        Value = c.Value,
                        Domain = c.Domain,
                        Path = c.Path
                    })
                    .ToList();
                var json = JsonSerializer.Serialize(cookies);
                var encrypted = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(json),
                    null,
                    DataProtectionScope.CurrentUser
                );
                File.WriteAllBytes(SessionFile, encrypted);
                Debug.WriteLine("Session saved");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save session: {ex.Message}");
            }
        }
        private List<SavedCookie>? LoadSessionCookies()
        {
            var encrypted = File.ReadAllBytes(SessionFile);
            var json = Encoding.UTF8.GetString(
                ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser)
            );
            return JsonSerializer.Deserialize<List<SavedCookie>>(json);
        }
        private static void DeleteSession()
        {
            try { if (File.Exists(SessionFile)) File.Delete(SessionFile); }
            catch { }
        }
        private async Task ThrottleAsync()
        {
            var elapsed = DateTime.UtcNow - _lastRequestTime;
            if (elapsed < MinDelay)
                await Task.Delay(MinDelay - elapsed);
            _lastRequestTime = DateTime.UtcNow;
        }
        private void EnsureLoggedIn()
        {
            if (!IsLoggedIn)
                throw new InvalidOperationException("Not logged in to VRChat API");
        }
        private class SavedCookie
        {
            public string Name { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public string Domain { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
        }
    }
}