using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Instrumentation;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Xml;
using static BVCC.App;
using static BVCC.Data;
using static System.Net.WebRequestMethods;
using File = System.IO.File;
using Path = System.IO.Path;

namespace BVCC
{
    public partial class App : Application
    {
        // Logo from https://www.flaticon.com/free-icon/3d_11437059?term=cubes&related_id=11437059
        public static ProjectsPage ProjectsPage { get; private set; }
        public static SettingsPage SettingsPage { get; private set; }
        public static NewFromTemplate NewFromTemplatePage { get; private set; }

        public static SaveData savedata = new SaveData();
        public static bool TemplatesInsalled = false;

        private async Task<XmlDocument> GetDocFromWeb(string url)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                string xmlContent = await client.GetStringAsync(url).ConfigureAwait(false);
                XmlReaderSettings settings = new XmlReaderSettings();
                settings.DtdProcessing = DtdProcessing.Parse;
                settings.XmlResolver = new XmlUrlResolver();

                XmlDocument doc = new XmlDocument();
                using (StringReader stringReader = new StringReader(xmlContent))
                using (XmlReader reader = XmlReader.Create(stringReader, settings))
                {
                    doc.Load(reader);
                }
                return doc;
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            OnStartupAsync(e);
        }

        public async Task<dynamic> GetLastestRelease()
        {
            string url = "https://api.github.com/repos/ImDragonxd07/BVCC/releases";

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "BVCC-Updater");

                try
                {
                    string json = await client.GetStringAsync(url);
                    var serializer = new JavaScriptSerializer();
                    var releases = serializer.Deserialize<dynamic[]>(json);

                    if (releases != null && releases.Length > 0)
                    {
                        return releases[0];
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("API Error: " + ex.Message);
                }
            }
            return null;
        }
        private async Task OnStartupAsync(StartupEventArgs e)
        {
            string pendingProtocol = null;

            if (e.Args.Length > 0)
            {
                pendingProtocol = e.Args[0];
            }
            base.OnStartup(e);
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            var structure = new[]
            {
                "Settings.json",
                "Repos",
                "Templates"
            };

            foreach (var entry in structure)
            {
                string fullPath = Path.Combine(baseDir, entry);

                bool isDir = entry.EndsWith("/") || string.IsNullOrEmpty(Path.GetExtension(entry));

                if (isDir)
                {
                    if (!Directory.Exists(fullPath))
                        Directory.CreateDirectory(fullPath);
                }
                else
                {
                    string dir = Path.GetDirectoryName(fullPath);

                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    if (!File.Exists(fullPath))
                        File.WriteAllText(fullPath, "");
                }
            }

            string settingsPath = Path.Combine(baseDir, "Settings.json");

            if (new FileInfo(settingsPath).Length == 0)
            {
                SaveToDisk();
                MessageBox.Show("You can import VCC data from the Settings menu", "Welcome to BVCC");
            }

            savedata = JsonConvert.DeserializeObject<SaveData>(File.ReadAllText(settingsPath));

            if (string.IsNullOrEmpty(savedata.RepoPath))
            {
                savedata.RepoPath = Path.Combine(baseDir, "Repos");
                SaveToDisk();
            }
            var templatesDir = Path.Combine(baseDir, "Templates");

            if (!Directory.Exists(templatesDir) ||
                !Directory.EnumerateFiles(templatesDir, "*", SearchOption.AllDirectories).Any())
            {
                MessageBox.Show(
                    "No templates found! Please add some to the Templates folder and restart the app if you want to use them.",
                    "No Templates Detected");
            }else
            {
                TemplatesInsalled = true;
            }
            if (savedata.RepoTemplates.Count == 0)
            {
                savedata.RepoTemplates.Add(new RepoTemplate()
                {
                    Name = "AvatarDefault",
                    PackageIDs = new System.Collections.Generic.List<string>() { "com.vrchat.core.vpm-resolver", "com.vrchat.base", "com.vrchat.avatars" }
                });
                savedata.RepoTemplates.Add(new RepoTemplate()
                {
                    Name = "WorldDefault",
                    PackageIDs = new System.Collections.Generic.List<string>() { "com.vrchat.core.vpm-resolver", "com.vrchat.base", "com.vrchat.worlds" }
                });
            }
            SplashScreen splash = new SplashScreen();
            splash.Show();
            splash.LoadingStatus.Text = "INIT";

            if (savedata.CheckForUpdates && !e.Args.Contains("--noupdate"))
            {
                splash.LoadingStatus.Text = "Checking for updates...";
                var latest = await GetLastestRelease();
                if (latest != null)
                {
                    string newestVersion = latest["tag_name"];
                    string downloadUrl = latest["assets"][0]["browser_download_url"];
                    string myCurrentVersion = savedata.AppVersion;
                    if (newestVersion != myCurrentVersion)
                    {
                        splash.LoadingBar.IsIndeterminate = true;
                        splash.LoadingStatus.Text = $"Downloading update {newestVersion}...";
                        string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BVCCSetup.exe");
                        var tcs = new TaskCompletionSource<bool>();
                        using (var client = new System.Net.WebClient())
                        {
                            client.DownloadProgressChanged += async (s, ev) =>
                            {
                                if (splash.LoadingBar.IsIndeterminate)
                                    splash.LoadingBar.IsIndeterminate = false;
                                splash.LoadingBar.Value = ev.ProgressPercentage;
                                splash.LoadingStatus.Text = $"Downloading: {ev.ProgressPercentage}%";
                                await Task.Delay(1);
                            };

                            client.DownloadFileCompleted += async (s, ev) =>
                            {
                                if (ev.Error == null)
                                {
                                    splash.LoadingStatus.Text = "Installing";
                                    await Task.Delay(800);
                                    savedata.AppVersion = newestVersion;
                                    SaveToDisk();
                                    var startInfo = new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = tempPath,
                                        Arguments = "/VERYSILENT /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /SUPPRESSMSGBOXES /NORESTART",
                                        UseShellExecute = true
                                    };
                                    System.Diagnostics.Process.Start(startInfo);
                                    tcs.SetResult(true);
                                    System.Windows.Application.Current.Shutdown();
                                }
                                else
                                {
                                    tcs.SetResult(false);
                                }
                            };

                            client.DownloadFileAsync(new Uri(downloadUrl), tempPath);
                            await tcs.Task;
                            return;
                        }
                    }
                }
            }
            splash.LoadingStatus.Text = "Initializing";
            SaveToDisk();
            ProjectsPage = new ProjectsPage();
            SettingsPage = new SettingsPage();
            NewFromTemplatePage = new NewFromTemplate();
            splash.LoadingStatus.Text = "Register";
            if (!ProtocolInstaller.IsRegistered())
            {
                ProtocolInstaller.RegisterProtocol();
            }
            if (!string.IsNullOrWhiteSpace(pendingProtocol))
            {
                await ProtocolRouter.HandleAsync(pendingProtocol);
            }
            await Task.Delay(500); 
            splash.Hide();
            Application.Current.MainWindow = ProjectsPage;
            ProjectsPage.Show();
        }
        public static void SaveToDisk()
        {
            File.WriteAllText("Settings.json", Newtonsoft.Json.JsonConvert.SerializeObject(savedata, Newtonsoft.Json.Formatting.Indented));
        }
        public static void OpenProject(ProjectItem project)
        {
            project.LastModified = System.DateTime.Now;
            SaveToDisk();
            Process.Start(App.savedata.UnityEditorPath, $@"-projectPath ""{project.ProjectPath}""");
        }
        public static void OpenProjectPath(string path)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
    }
}