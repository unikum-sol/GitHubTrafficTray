namespace GitHubTrafficTray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Nur eine Instanz, damit nicht zwei Prozesse dieselbe Datendatei schreiben
        using var mutex = new Mutex(true, @"Local\GitHubTrafficTray-unikum-sol", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("GitHub Traffic Tray läuft bereits (siehe Infobereich).", AppConfig.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) =>
            MessageBox.Show("Unerwarteter Fehler:\n\n" + e.Exception, AppConfig.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);

        GitHubClient client;
        string? login;
        using (var dlg = new TokenForm())
        {
            if (dlg.ShowDialog() != DialogResult.OK || dlg.Client is null) return;
            client = dlg.Client;
            login = dlg.UserLogin;
        }

        using var context = new TrayApplicationContext(client, login);
        Application.Run(context);
    }
}
