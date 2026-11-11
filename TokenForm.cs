using System.ComponentModel;
using System.Net;

namespace GitHubTrafficTray;

/// <summary>Abfrage des Fine-grained Tokens beim Start. Der Token wird nicht gespeichert.</summary>
internal sealed class TokenForm : Form
{
    private readonly TextBox _txtToken;
    private readonly CheckBox _chkShow;
    private readonly Button _btnOk;
    private readonly Button _btnCancel;
    private readonly Label _lblError;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public GitHubClient? Client { get; private set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? UserLogin { get; private set; }

    public TokenForm()
    {
        Text = $"{AppConfig.AppName} – Token eingeben";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ShowInTaskbar = true;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 230);

        var info = new Label
        {
            Text = $"Fine-grained Personal Access Token für „{AppConfig.Owner}“ eingeben.\n" +
                   "Benötigte Repository-Berechtigung: Administration → Read-only.\n" +
                   "Der Token wird nur im Arbeitsspeicher gehalten und nicht gespeichert.",
            Location = new Point(16, 14),
            Size = new Size(528, 60)
        };

        _txtToken = new TextBox
        {
            Location = new Point(16, 82),
            Width = 528,
            UseSystemPasswordChar = true
        };

        _chkShow = new CheckBox
        {
            Text = "Token anzeigen",
            Location = new Point(16, 114),
            AutoSize = true
        };
        _chkShow.CheckedChanged += (_, _) => _txtToken.UseSystemPasswordChar = !_chkShow.Checked;

        _lblError = new Label
        {
            Location = new Point(16, 142),
            Size = new Size(528, 36),
            ForeColor = Color.Firebrick
        };

        _btnOk = new Button { Text = "Starten", Location = new Point(352, 186), Size = new Size(92, 30) };
        _btnCancel = new Button { Text = "Abbrechen", Location = new Point(452, 186), Size = new Size(92, 30), DialogResult = DialogResult.Cancel };
        _btnOk.Click += async (_, _) => await ValidateAsync();

        Controls.AddRange(new Control[] { info, _txtToken, _chkShow, _lblError, _btnOk, _btnCancel });
        AcceptButton = _btnOk;
        CancelButton = _btnCancel;
    }

    private async Task ValidateAsync()
    {
        string token = _txtToken.Text.Trim();
        if (token.Length == 0)
        {
            _lblError.Text = "Bitte einen Token eingeben.";
            return;
        }

        SetBusy(true);
        _lblError.ForeColor = SystemColors.ControlText;
        _lblError.Text = "Prüfe Token …";

        var client = new GitHubClient(token);
        try
        {
            var user = await client.GetAuthenticatedUserAsync(CancellationToken.None);
            Client = client;
            UserLogin = user.Login;
            DialogResult = DialogResult.OK;
            return;
        }
        catch (GitHubApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            client.Dispose();
            ShowError("Token ungültig oder abgelaufen (401).");
        }
        catch (Exception ex)
        {
            var answer = MessageBox.Show(this,
                $"Der Token konnte nicht geprüft werden:\n\n{TrafficService.Describe(ex)}\n\nTrotzdem mit diesem Token starten?",
                AppConfig.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Yes)
            {
                Client = client;
                UserLogin = null;
                DialogResult = DialogResult.OK;
                return;
            }
            client.Dispose();
            ShowError("Prüfung fehlgeschlagen: " + TrafficService.Describe(ex));
        }
        SetBusy(false);
    }

    private void ShowError(string text)
    {
        _lblError.ForeColor = Color.Firebrick;
        _lblError.Text = text;
    }

    private void SetBusy(bool busy)
    {
        _btnOk.Enabled = !busy;
        _txtToken.Enabled = !busy;
        UseWaitCursor = busy;
    }
}
