using System.Net.Http.Json;

namespace OptionsTrader.WinForms;

internal record LoginResult(string AccessToken, DateTime ExpiresAt, string Name, string LastName);

public partial class LoginForm : Form
{
    private readonly HttpClient _httpClient;
    private readonly string _apiBaseUrl;

    public string? AccessToken { get; private set; }
    public string? Username { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }

    public LoginForm(HttpClient httpClient, string apiBaseUrl)
    {
        InitializeComponent();
        _httpClient = httpClient;
        _apiBaseUrl = apiBaseUrl;
    }

    private async void BtnEnter_Click(object? sender, EventArgs e)
    {
        var username = txtUsername.Text.Trim();
        var password = txtPassword.Text;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("Enter username and password.");
            return;
        }

        btnEnter.Enabled = false;
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"{_apiBaseUrl}/auth/login", new { username, password });
            if (!response.IsSuccessStatusCode)
            {
                ShowError("Invalid username or password.");
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResult>();
            if (result?.AccessToken == null)
            {
                ShowError("Login failed.");
                return;
            }

            AccessToken = result.AccessToken;
            Username = username;
            FirstName = result.Name;
            LastName = result.LastName;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            ShowError($"Error: {ex.Message}");
        }
        finally
        {
            btnEnter.Enabled = true;
        }
    }

    private void ShowError(string message)
    {
        lblError.Text = message;
        lblError.Visible = true;
    }
}
