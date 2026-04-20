using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Management.Instrumentation;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
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
        public static SplashScreen splash { get; private set; }

        public static SaveData savedata = new SaveData();
        public static bool TemplatesInsalled = false;
        public static List<GitHubRelease> GitHubReleases { get; set; } = new List<GitHubRelease>();
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
        public static async Task<List<GitHubRelease>> GetAllReleases()
        {


            string url = "https://api.github.com/repos/ImDragonxd07/BVCC/releases";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "BVCC-Updater");
                client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

                try
                {
                    string json = await client.GetStringAsync(url);
                    JArray releases = JArray.Parse(json);

                    var result = new List<GitHubRelease>();

                    foreach (var r in releases)
                    {
                        var release = (JObject)r;

                        string version = release["tag_name"]?.ToString();
                        string name = release["name"]?.ToString();
                        bool prerelease = release["prerelease"]?.ToObject<bool>() ?? false;
                        string description = release["body"]?.ToString();

                        string downloadUrl =
                            release["assets"]?[0]?["browser_download_url"]?.ToString();

                        result.Add(new GitHubRelease
                        {
                            Version = version,
                            Name = name,
                            DownloadUrl = downloadUrl,
                            Description = description,
                            IsPreRelease = prerelease,
                            PublishedAt = DateTime.Parse(release["published_at"]?.ToString())
                        });
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("GitHub API Error: " + ex.Message);
                    return new List<GitHubRelease>();
                }
            }
        }
        private bool _ownsMutex = false;
        protected override void OnExit(ExitEventArgs e)
        {
            _pipeCts?.Cancel();

            if (_ownsMutex && _mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Mutex release error: {ex.Message}");
                }
                finally
                {
                    _mutex.Dispose();
                }
            }

            base.OnExit(e);
        }

        private const string PipeName = "BVCC_PIPE";
        private static CancellationTokenSource _pipeCts = new CancellationTokenSource();
        private async Task StartPipeServer(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Message,
                        PipeOptions.Asynchronous))
                    {
                        await server.WaitForConnectionAsync(token);

                        using (var reader = new StreamReader(server))
                        {
                            string message = await reader.ReadLineAsync();
                            if (!string.IsNullOrWhiteSpace(message))
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    _ = ProtocolRouter.HandleAsync(message);
                                });
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Pipe Server Error: {ex.Message}");
                    await Task.Delay(1000, token);
                }
            }
        }
        private async Task SendToRunningInstance(string message)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    await client.ConnectAsync(1000);

                    using (var writer = new StreamWriter(client))
                    {
                        await writer.WriteAsync(message);
                        await writer.FlushAsync();
                    }
                }
            }
            catch
            {
            }
        }

        public static async Task DownloadAndInstallVersion(GitHubRelease version)
        {
            splash.LoadingBar.IsIndeterminate = true;
            splash.LoadingStatus.Text = $"Downloading update {version.Version}...";
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
                        savedata.AppVersion = version.Version;
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

                client.DownloadFileAsync(new Uri(version.DownloadUrl), tempPath);
                await tcs.Task;
                return;
            }
        }

        private Mutex _mutex;
        public static Dictionary<string, JObject> RepoCache { get; } = new Dictionary<string, JObject>();
        public static List<ProjectPackage> CachedPackages { get; set; } = new List<ProjectPackage>();
        private static readonly HttpClient client = new HttpClient();

        public static async Task EnsureRepoCacheAsync(bool forceRefresh = false)
        {
            var toFetch = forceRefresh
                ? savedata.Repositories.ToList()
                : savedata.Repositories.Where(r => !RepoCache.ContainsKey(r.Url)).ToList();

            bool hasSplash = splash != null;

            if (hasSplash)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    splash.LoadingBar.IsIndeterminate = false;
                    splash.LoadingBar.Value = 0;
                });
            }

            int loaded = 0;
            int total = toFetch.Count;

            foreach (var repo in toFetch)
            {
                try
                {
                    var json = JObject.Parse(await client.GetStringAsync(repo.Url));
                    lock (RepoCache)
                    {
                        RepoCache[repo.Url] = json;
                    }
                }
                catch { }

                loaded++;

                if (hasSplash)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        splash.LoadingStatus.Text = $"Loading Repositories... ({loaded}/{total})";
                        splash.LoadingBar.Value = ((double)loaded / total) * 100;
                    });
                }
            }

            var validUrls = savedata.Repositories.Select(r => r.Url).ToHashSet();
            foreach (var key in RepoCache.Keys.Where(k => !validUrls.Contains(k)).ToList())
                RepoCache.Remove(key);

            var list = new List<ProjectPackage>();
            foreach (var repo in RepoCache.Values)
            {
                var packages = repo["packages"] as JObject;
                if (packages == null) continue;

                foreach (var pkg in packages.Properties())
                {
                    var versions = pkg.Value["versions"] as JObject;
                    if (versions == null) continue;

                    var latest = versions.Properties()
                        .OrderByDescending(v => v.Name)
                        .FirstOrDefault();

                    if (latest == null) continue;

                    string displayName = latest.Value["displayName"]?.ToString()
                        ?? latest.Value["name"]?.ToString()
                        ?? pkg.Name;

                    list.Add(new ProjectPackage
                    {
                        ID = pkg.Name,
                        Name = displayName,
                        LatestVersion = latest.Name,
                        SelectedVersion = latest.Name,
                        IsInstalled = false
                    });
                }
            }

            CachedPackages = list;

            if (hasSplash)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    splash.LoadingBar.IsIndeterminate = true;
                });
            }
        }
        private async Task<GitHubRelease> GetLatestGithubRelease()
        {
            GitHubReleases = await GetAllReleases();
            return GitHubReleases
                .OrderByDescending(r => r.PublishedAt)
                .FirstOrDefault();
        }
        private async Task OnStartupAsync(StartupEventArgs e)
        {
            //MessageBox.Show(string.Join("\n", e.Args));
            _mutex = new Mutex(true, @"Global\BVCC_MUTEX", out _ownsMutex);
            string pendingProtocol = null;
            var argsList = e.Args.ToList();
            if (!_ownsMutex)
            {
                pendingProtocol = e.Args.FirstOrDefault(a => a.StartsWith("bvcc://"));
                if (!string.IsNullOrWhiteSpace(pendingProtocol))
                {
                    await SendToRunningInstance(pendingProtocol);
                }

                Shutdown();
                return;
            }
            _ = Task.Run(() => StartPipeServer(_pipeCts.Token));
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
                CustomDialog.Show("You can import VCC data from the Settings menu", "Welcome to BVCC", CustomDialog.Mode.Message);

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

                CustomDialog.Show("No templates found! Please add some to the Templates folder and restart the app if you want to use them.", "No Templates Detected", CustomDialog.Mode.Message);
            }
            else
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
            splash = new SplashScreen();
            splash.Show();
            splash.LoadingStatus.Text = "INIT";
            bool skipupdate = argsList.Contains("--noupdate");
            if (savedata.CheckForUpdates && !skipupdate)
            {
                splash.LoadingStatus.Text = "Checking for updates...";
                GitHubRelease latest = await GetLatestGithubRelease();
                if (latest != null)
                {
                    string newestVersion = latest.Version;
                    string myCurrentVersion = savedata.AppVersion;
                    if (newestVersion != myCurrentVersion)
                    {
                        await DownloadAndInstallVersion(latest);
                    }
                }
            }
            splash.LoadingStatus.Text = "Initializing";
            ProjectsPage = new ProjectsPage();
            SettingsPage = new SettingsPage();
            NewFromTemplatePage = new NewFromTemplate();
            await EnsureRepoCacheAsync();
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
            if (!savedata.SeenReadme && !skipupdate)
            {
                GitHubRelease readmedata = await GetLatestGithubRelease();
                CustomDialog.Show(readmedata.Description, $"BVCC Updated to {readmedata.Version}", CustomDialog.Mode.Readme);
                App.savedata.SeenReadme = true;
            }
            SaveToDisk();
        }
        private static readonly string settingsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.json");

        public static void SaveToDisk()
        {
            File.WriteAllText(settingsPath,
                JsonConvert.SerializeObject(savedata, Newtonsoft.Json.Formatting.Indented));
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