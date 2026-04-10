using System.IO;
using System.Text.Json;
using monitor_desktop.Models.AuthManagement;

namespace monitor_desktop.Services
{
    public class TokenManager
    {
        private static readonly string TokenFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkPulse",
            "token.json");

        private TokenStorage _currentToken;

        public TokenStorage CurrentToken => _currentToken;

        public void SaveToken(TokenStorage token)
        {
            _currentToken = token;

            try
            {
                var directory = Path.GetDirectoryName(TokenFilePath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(token, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(TokenFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save token: {ex.Message}");
            }
        }

        public TokenStorage LoadToken()
        {
            try
            {
                if (File.Exists(TokenFilePath))
                {
                    var json = File.ReadAllText(TokenFilePath);
                    _currentToken = JsonSerializer.Deserialize<TokenStorage>(json);

                    if (_currentToken != null && !_currentToken.IsValid())
                    {
                        ClearToken();
                        return null;
                    }

                    return _currentToken;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load token: {ex.Message}");
            }

            return null;
        }

        public void ClearToken()
        {
            _currentToken = null;
            try
            {
                if (File.Exists(TokenFilePath))
                    File.Delete(TokenFilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear token: {ex.Message}");
            }
        }

        public bool IsAuthenticated => _currentToken != null && _currentToken.IsValid();
    }
}