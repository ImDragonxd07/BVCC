using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.IO.Packaging;
using System.Linq;
using System.Net.Http;
using System.Runtime.ConstrainedExecution;
using System.Security.Policy;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using VRChat.API.Model;
using static BVCC.Data;
using static BVCC.Data.ProjectPackage;
using File = System.IO.File;

namespace BVCC
{
    public partial class PackageManagerPage : System.Windows.Controls.UserControl
    {
        private ProjectItem currentproject;
        private List<ProjectPackage> allPackages = new List<ProjectPackage>();
        private static readonly HttpClient client = new HttpClient();
        private bool _isBulkOperation = false;
        private SemaphoreSlim _installLock = new SemaphoreSlim(1, 1);

        private string _activeFilter = "All";
        private string _activeSortField = "Status";
        private bool _sortAscending = true;
        private bool _showPreRelease
        {
            get { return App.savedata.ShowPreReleases; }
        }
        private Grid _loadingOverlay;

        public PackageManagerPage()
        {
            InitializeComponent();
            _loadingOverlay = LoadingOverlay;
            UpdateSortButtonStyles();
        }
        public void ClearRepoCache()
        {
            App.RepoCache.Clear();
            App.CachedPackages.Clear();
        }
        private async Task DeleteDirectorySafe(string targetDir)
        {
            if (!Directory.Exists(targetDir)) return;

            await Task.Run(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();

                string[] files = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                Directory.Delete(targetDir, true);
            });
        }
        public async Task DownloadFileAsync(string url, string outputPath, Action<float> onProgress = null)
        {
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    long downloaded = 0;
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        downloaded += read;
                        if (total.HasValue && onProgress != null)
                            onProgress((float)downloaded / total.Value);
                    }
                }
            }
        }
        private bool HasWriteAccess(string directoryPath)
        {
            try
            {
                string testFile = Path.Combine(directoryPath, $".write_test_{Guid.NewGuid()}");
                using (FileStream fs = File.Create(testFile, 1, FileOptions.DeleteOnClose))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
        private bool IsFileLocked(FileInfo file)
        {
            if (!file.Exists) return false;
            try
            {
                using (FileStream stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.Close();
                }
            }
            catch (IOException)
            {
                return true;
            }
            return false;
        }
        private HashSet<string> _installing = new HashSet<string>();
        private readonly object _installingLock = new object();

        public async Task InstallOrUpdatePackage(ProjectPackage package, string version, bool force = false)
        {
            if (package == null) return;
            if (string.IsNullOrEmpty(version)) return;
            currentproject.LastModified = DateTime.Now;
            App.SaveToDisk();
            string packagesPath = Path.Combine(currentproject.ProjectPath, "Packages");
            string manifestPath = Path.Combine(packagesPath, "vpm-manifest.json");
            DriveInfo drive = new DriveInfo(Path.GetPathRoot(currentproject.ProjectPath));
            long minSpace = 100L * 1024L * 1024L;
            if (drive.AvailableFreeSpace < minSpace)
            {

                if((bool)!CustomDialog.Show("Low Disk Space, Installing now could lead to a corrupted manifest file, continue?", App.savedata.AppName, CustomDialog.Mode.Question))
                {
                    return;
                }
            }
            if (!HasWriteAccess(packagesPath))
            {
                CustomDialog.Show("Permission Denied: Cannot write to the Packages folder. Is the project in a protected directory?", App.savedata.AppName, CustomDialog.Mode.Message);
                return;
            }
            if (IsFileLocked(new FileInfo(manifestPath)))
            {
                CustomDialog.Show("Manifest is locked: Is another process using it?", App.savedata.AppName, CustomDialog.Mode.Message);
                return;
            }
            lock (_installingLock)
            {
                if (_installing.Contains(package.ID))
                    return;

                _installing.Add(package.ID);
            }

            await _installLock.WaitAsync();

            List<(ProjectPackage pkg, string ver)> dependenciesToInstall =
                new List<(ProjectPackage, string)>();

            try
            {
                if (package.CurrentAction == PackageAction.None)
                    package.CurrentAction = PackageAction.Installing;

                package.ProgressingControl =
                    package.CurrentAction == PackageAction.Updating ? "Update" :
                    package.CurrentAction == PackageAction.ChangingVersion ? "Version" :
                    package.CurrentAction == PackageAction.Removing ? "Remove" :
                    "Install";

                package.InstallProgress = 0.05;

                string downloadUrl = null;

                foreach (var entry in App.RepoCache.Values)
                {
                    var versionData = entry["packages"]?[package.ID]?["versions"]?[version];
                    if (versionData != null)
                    {
                        downloadUrl = versionData["url"]?.ToString();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    CustomDialog.Show("Download URL not found for this package version.", App.savedata.AppName, CustomDialog.Mode.Message);
                    return;
                }
                string cacheFolder = Path.Combine(App.savedata.RepoPath, package.ID, version);
                string zipPath = Path.Combine(cacheFolder, $"{package.ID}_{version}.zip");
                string projectPackagePath = Path.Combine(currentproject.ProjectPath, "Packages", package.ID);

                Directory.CreateDirectory(cacheFolder);

                if (!File.Exists(zipPath))
                {
                    await DownloadFileAsync(downloadUrl, zipPath, p =>
                    {
                        package.InstallProgress = 0.05 + p * 0.6f;
                    });
                }

                package.InstallProgress = 0.7;

                await Task.Run(async () =>
                {
                    if (Directory.Exists(projectPackagePath))
                        await DeleteDirectorySafe(projectPackagePath);

                    ZipFile.ExtractToDirectory(zipPath, projectPackagePath);
                });

                package.InstallProgress = 0.85;

                await UpdateManifest(package.ID, version);

                string packageJsonPath = Path.Combine(projectPackagePath, "package.json");

                if (File.Exists(packageJsonPath))
                {
                    JObject pkgJson = null;

                    try
                    {
                        pkgJson = JObject.Parse(File.ReadAllText(packageJsonPath));
                    }
                    catch { }

                    var deps =
                        pkgJson?["vpmDependencies"] as JObject ??
                        pkgJson?["dependencies"] as JObject;

                    if (deps != null)
                    {
                        foreach (var dep in deps.Properties())
                        {
                            var depPackage = allPackages.FirstOrDefault(p => p.ID == dep.Name);

                            if (depPackage == null) continue;
                            if (depPackage.IsInstalled) continue;
                            if (string.IsNullOrEmpty(depPackage.LatestVersion)) continue;

                            dependenciesToInstall.Add((depPackage, depPackage.LatestVersion));
                        }
                    }
                }

                package.InstallProgress = 1.0;
                await Task.Delay(100);

                if (!_isBulkOperation)
                    await LoadProject(currentproject);
            }
            finally
            {
                _installLock.Release();

                lock (_installingLock)
                    _installing.Remove(package.ID);

                package.ProgressingControl = null;
                package.CurrentAction = PackageAction.None;
                package.InstallProgress = 0;
            }

            foreach (var dep in dependenciesToInstall)
            {
                await InstallOrUpdatePackage(dep.pkg, dep.ver, force: true);
            }
            
        }
        private async Task UpdateManifest(string id, string version, bool isRemoval = false)
        {
            string manifestPath = Path.Combine(currentproject.ProjectPath, "Packages", "vpm-manifest.json");
            string packageJsonPath = Path.Combine(currentproject.ProjectPath, "Packages", id, "package.json");

            if (!File.Exists(manifestPath))
                return;

            var manifestJson = JObject.Parse(File.ReadAllText(manifestPath));

            if (manifestJson["dependencies"] == null)
                manifestJson["dependencies"] = new JObject();

            if (manifestJson["locked"] == null)
                manifestJson["locked"] = new JObject();

            JObject deps = manifestJson["dependencies"] as JObject;
            JObject locked = manifestJson["locked"] as JObject;

            if (isRemoval)
            {
                if (deps != null) deps.Remove(id);
                if (locked != null) locked.Remove(id);
            }
            else
            {
                if (deps != null)
                {
                    deps[id] = new JObject
                    {
                        ["version"] = version
                    };
                }

                JObject vpmDeps = new JObject();

                if (File.Exists(packageJsonPath))
                {
                    try
                    {
                        var pkgJson = JObject.Parse(File.ReadAllText(packageJsonPath));

                        var found = pkgJson["vpmDependencies"] as JObject;
                        if (found == null)
                            found = pkgJson["dependencies"] as JObject;

                        if (found != null)
                            vpmDeps = (JObject)found.DeepClone();
                    }
                    catch
                    {

                    }
                }

                if (locked != null)
                {
                    locked[id] = new JObject
                    {
                        ["version"] = version,
                        ["dependencies"] = vpmDeps
                    };
                }
            }

            File.WriteAllText(manifestPath, manifestJson.ToString(Formatting.Indented));

            string lockPath = manifestPath + ".lock";

            if (File.Exists(lockPath))
            {
                try { File.Delete(lockPath); }
                catch { }
            }

            await Task.FromResult(0);
        }
        public async Task InstallLatestVersion(ProjectPackage package, bool force = false)
        {
            if (package == null) return;

            string latest = null;

            foreach (var entry in App.RepoCache.Values)
            {
                var pkgData = entry["packages"]?[package.ID];
                var versions = pkgData?["versions"] as JObject;
                if (versions == null) continue;

                latest = versions.Properties()
                    .Select(p => p.Name)
                    .OrderByDescending(UnityHelper.ParseVersion)
                    .FirstOrDefault();

                break;
            }

            if (string.IsNullOrWhiteSpace(latest))
                return;

            await InstallOrUpdatePackage(package, latest, force);
        }
        public async Task LoadProject(ProjectItem project)
        {
            if (project == null) return;

            currentproject = project;
            ProjectTitleText.Text = project.ProjectName;
            SetLoadingState(true);

            try
            {
                var (finalList, unityVersion) = await Task.Run(async () =>
                {
                    await App.EnsureRepoCacheAsync();

                    string manifestPath = Path.Combine(project.ProjectPath, "Packages", "vpm-manifest.json");
                    string unityPath = Path.Combine(project.ProjectPath, "ProjectSettings", "ProjectVersion.txt");

                    if (!File.Exists(manifestPath)) return (new List<ProjectPackage>(), "Unknown");

                    string manifestContent;
                    using (var fs = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs))
                        manifestContent = sr.ReadToEnd();

                    var manifestJson = JObject.Parse(manifestContent);
                    var locked = manifestJson["locked"] as JObject;
                    var userDependencies = manifestJson["dependencies"] as JObject;
                    var packageLookup = new Dictionary<string, ProjectPackage>();

                    if (locked != null)
                    {
                        foreach (var prop in locked.Properties())
                        {
                            var rawVersion = prop.Value["version"]?.ToString();
                            bool isLocal = !string.IsNullOrEmpty(rawVersion) && rawVersion.StartsWith("file:");
                            var cleanedVersion = isLocal ? null : rawVersion;

                            var package = new ProjectPackage()
                            {
                                ID = prop.Name,
                                Name = prop.Name,
                                CurrentVersion = cleanedVersion,
                                SelectedVersion = rawVersion,
                                IsInstalled = true,
                                VersionList = new List<string>()
                            };

                            foreach (var entry in App.RepoCache.Values)
                            {
                                var pkgData = entry["packages"]?[package.ID];
                                if (pkgData != null)
                                {
                                    var versions = pkgData["versions"] as JObject;
                                    if (versions != null)
                                    {
                                        var versionNames = versions.Properties().Select(p => p.Name).ToList();
                                        package.VersionList = SortVersions(versionNames, false, _showPreRelease);
                                        package.LatestVersion = package.VersionList.FirstOrDefault();
                                        package.Name = versions[package.LatestVersion]?["displayName"]?.ToString() ?? package.ID;
                                    }
                                    break;
                                }
                            }

                            if (package.LatestVersion == null)
                            {
                                package.Status = CompatibilityStatus.Incompatible;
                                package.IncompatibleReason = "Not in repositories";

                                string localPkgJson = Path.Combine(project.ProjectPath, "Packages", package.ID, "package.json");
                                if (File.Exists(localPkgJson))
                                {
                                    try
                                    {
                                        var pkgJson = JObject.Parse(File.ReadAllText(localPkgJson));
                                        var ver = pkgJson["version"]?.ToString();
                                        package.LatestVersion = ver;
                                        if (string.IsNullOrEmpty(package.CurrentVersion)) package.CurrentVersion = package.ID;
                                        package.VersionList = new List<string> { ver ?? package.ID };
                                    }
                                    catch { }
                                }
                            }
                            packageLookup[package.ID] = package;
                        }
                    }

                    if (locked != null)
                    {
                        foreach (var prop in locked.Properties())
                        {
                            if (packageLookup.TryGetValue(prop.Name, out var parentPackage))
                            {
                                var subDeps = prop.Value["dependencies"] as JObject;
                                if (subDeps != null)
                                {
                                    foreach (var d in subDeps.Properties())
                                    {
                                        if (packageLookup.TryGetValue(d.Name, out var childPackage))
                                        {
                                            parentPackage.Dependencies.Add(childPackage);
                                            childPackage.IsADependency = true;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    var availableList = new List<ProjectPackage>();
                    foreach (var entry in App.RepoCache.Values)
                    {
                        var repoPackages = entry["packages"] as JObject;
                        if (repoPackages == null) continue;

                        foreach (var pkg in repoPackages.Properties())
                        {
                            if (packageLookup.ContainsKey(pkg.Name)) continue;

                            var versions = pkg.Value["versions"] as JObject;
                            if (versions == null) continue;

                            var versionNames = versions.Properties().Select(p => p.Name).ToList();
                            var sorted = SortVersions(versionNames, false, _showPreRelease);
                            var latestVer = sorted.FirstOrDefault();

                            if (latestVer != null)
                            {
                                availableList.Add(new ProjectPackage()
                                {
                                    ID = pkg.Name,
                                    Name = versions[latestVer]?["displayName"]?.ToString() ?? pkg.Name,
                                    IsInstalled = false,
                                    LatestVersion = latestVer,
                                    VersionList = sorted,
                                    SelectedVersion = latestVer
                                });
                            }
                        }
                    }

                    var finalDisplayList = new List<ProjectPackage>();
                    if (userDependencies != null)
                    {
                        foreach (var prop in userDependencies.Properties())
                            if (packageLookup.TryGetValue(prop.Name, out var p))
                                finalDisplayList.Add(p);
                    }

                    foreach (var p in packageLookup.Values)
                        if (!finalDisplayList.Contains(p)) finalDisplayList.Add(p);

                    finalDisplayList.AddRange(availableList.OrderBy(p => p.Name));

                    string uVer = "Unknown";
                    try
                    {
                        if (File.Exists(unityPath))
                        {
                            var line = File.ReadLines(unityPath).FirstOrDefault(l => l.StartsWith("m_EditorVersion:"));
                            if (line != null) uVer = line.Split(':')[1].Trim();
                        }
                    }
                    catch { }

                    return (finalDisplayList, uVer);
                });

                allPackages = finalList;
                UnityVersionText.Content = unityVersion;
                DetailsStrip.Visibility = App.api.IsLoggedIn ? Visibility.Visible : Visibility.Collapsed;
                ApplyFilter();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadProject Error: {ex.Message}");
            }
            finally
            {
                SetLoadingState(false);
            }
            RefreshDetailStrip();
            //UnityHelper.ExtractVRCInfo(currentproject.ProjectPath);

        }
        private async void RefreshDetailStrip()
        {
            if (App.api == null || !App.api.IsLoggedIn || currentproject == null) return;

            try
            {
                var vrcInfo = await UnityHelper.ExtractVRCInfo(currentproject.ProjectPath);
                WinIcon.Visibility = Visibility.Collapsed;
                IOSIcon.Visibility = Visibility.Collapsed;
                AndroidIcon.Visibility = Visibility.Collapsed;

                if (vrcInfo.VrcId.StartsWith("wrld_"))
                {
                    var world = await App.api.GetWorldAsync(vrcInfo.VrcId);
                    VrcContentNameText.Text = world.Name;
                    UploadStatusText.Text = world.ReleaseStatus.ToString();

                    UpdatePlatformIcons(world.UnityPackages);
                }
                else if (vrcInfo.VrcId.StartsWith("avtr_"))
                {
                    var avatar = await App.api.GetAvatarAsync(vrcInfo.VrcId);
                    VrcContentNameText.Text = avatar.Name;
                    UploadStatusText.Text = avatar.ReleaseStatus.ToString();

                    UpdatePlatformIcons(avatar.UnityPackages);
                }
            }
            catch { VrcContentNameText.Text = "Offline / Not Owner"; }
        }

        private void UpdatePlatformIcons(List<UnityPackage> packages)
        {
            if (packages == null) return;

            foreach (var pkg in packages)
            {
                string platform = pkg.Platform.ToLower();

                if (platform.Contains("windows"))
                {
                    WinIcon.Visibility = Visibility.Visible;
                }
                else if (platform.Contains("android"))
                {
                    AndroidIcon.Visibility = Visibility.Visible;
                }
                else if (platform.Contains("ios"))
                {
                    IOSIcon.Visibility = Visibility.Visible;
                }
            }
        }
        private void ApplyFilter()
        {

            string query = SearchBox.Text.ToLower();

            IEnumerable<ProjectPackage> filtered = allPackages.Where(p =>
                p.ID.ToLower().Contains(query) || (p.Name?.ToLower().Contains(query) ?? false));

            if (_activeFilter == "Installed")
                filtered = filtered.Where(p => p.IsInstalled);
            else if (_activeFilter == "NotInstalled")
                filtered = filtered.Where(p => !p.IsInstalled);
            else if (_activeFilter == "Updates")
                filtered = filtered.Where(p => p.HasUpdate);

            if (_activeSortField == "Status")
            {
                filtered = _sortAscending
                    ? filtered.OrderByDescending(p => p.IsInstalled).ThenByDescending(p => p.HasUpdate)
                    : filtered.OrderBy(p => p.IsInstalled);
            }
            else if (_activeSortField == "Version")
            {
                filtered = _sortAscending
                    ? filtered.OrderBy(p => UnityHelper.ParseVersion(p.CurrentVersion ?? "0.0"))
                    : filtered.OrderByDescending(p => UnityHelper.ParseVersion(p.CurrentVersion ?? "0.0"));
            }
            else
            {
                filtered = _sortAscending
                    ? filtered.OrderBy(p => p.Name)
                    : filtered.OrderByDescending(p => p.Name);
            }
            if (!_showPreRelease)
                filtered = filtered.Where(p => !IsPreRelease(p.LatestVersion ?? ""));
            PackageListBox.ItemsSource = filtered.ToList();
        }

        private void UpdateSortButtonStyles()
        {
            foreach (var btn in new[] { SortName, SortStatus, SortVersion })
            {
                bool active = (btn.Tag as string) == _activeSortField;
                btn.Opacity = active ? 1.0 : 0.4;
                string tag = btn.Tag as string;
                string arrow = active ? (_sortAscending ? " ↑" : " ↓") : "";
                if (tag == "Name") btn.Content = "NAME" + arrow;
                else if (tag == "Status") btn.Content = "STATUS" + arrow;
                else if (tag == "Version") btn.Content = "VERSION" + arrow;
            }
        }

        private void SetLoadingState(bool loading)
        {
            LoadingOverlay.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            PackageListBox.IsEnabled = !loading;
        }

        private void FilterTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                _activeFilter = btn.Tag as string;
                UpdateFilterTabStyles();
                ApplyFilter();
            }
        }

        private void UpdateFilterTabStyles()
        {
            foreach (var btn in new[] { FilterAll, FilterInstalled, FilterNotInstalled, FilterUpdates })
            {
                btn.Background = (btn.Tag as string) == _activeFilter
                    ? System.Windows.Media.Brushes.White
                    : System.Windows.Media.Brushes.Transparent;
                btn.Foreground = (btn.Tag as string) == _activeFilter
                    ? System.Windows.Media.Brushes.Black
                    : System.Windows.Media.Brushes.White;
            }
        }

        private void SortBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                string field = btn.Tag as string;
                if (_activeSortField == field)
                    _sortAscending = !_sortAscending;
                else
                {
                    _activeSortField = field;
                    _sortAscending = true;
                }
                UpdateSortButtonStyles();
                ApplyFilter();
            }
        }
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
        private void BackBtn_Click(object sender, RoutedEventArgs e) => UIHelper.GoBack();
        private void LaunchProjectBtn_Click(object sender, RoutedEventArgs e) { if (currentproject != null) App.OpenProject(currentproject); }
        private void OpenFilePath_Click(object sender, RoutedEventArgs e) => App.OpenProjectPath(currentproject.ProjectPath);
        private void InstallPackage_Click(object sender, RoutedEventArgs e) { if (((Button)sender).DataContext is ProjectPackage p) _ = InstallOrUpdatePackage(p, p.SelectedVersion); }
        public async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            App.RepoCache.Clear();
            App.CachedPackages.Clear();
            await LoadProject(currentproject);
        }
        private async void PackageUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is ProjectPackage p)
            {
                try
                {
                    p.CurrentAction = PackageAction.Updating;
                    await InstallOrUpdatePackage(p, p.LatestVersion);
                }
                catch (Exception ex)
                {
                    CustomDialog.Show($"Failed to update: {ex.Message}", App.savedata.AppName, CustomDialog.Mode.Message);
                }
            }
        }

        private async void RemovePackage_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is ProjectPackage p &&
                (bool)CustomDialog.Show($"Remove {p.ID}?", App.savedata.AppName, CustomDialog.Mode.Question))
            {
                p.CurrentAction = PackageAction.Removing;
                p.ProgressingControl = "Remove";
                p.InstallProgress = 0.1;

                try
                {
                    string path = Path.Combine(currentproject.ProjectPath, "Packages", p.ID);
                    p.InstallProgress = 0.4;
                    if (Directory.Exists(path))
                        await Task.Run(() => DeleteDirectorySafe(path));
                    p.InstallProgress = 0.7;
                    await UpdateManifest(p.ID, null, true);
                    p.InstallProgress = 1.0;
                    await Task.Delay(200);
                    await LoadProject(currentproject);
                }
                catch (Exception ex)
                {
                    CustomDialog.Show($"Error removing package: {ex.Message}", App.savedata.AppName, CustomDialog.Mode.Message);
                }
                finally
                {
                    p.ProgressingControl = null;
                    p.CurrentAction = PackageAction.None;
                    p.InstallProgress = 0;
                }
            }
        }

        private async Task RunBulkOperation(
            List<(ProjectPackage package, string version)> targets,
            Func<ProjectPackage, string, Task> operation,
            string label)
        {
            if (targets.Count == 0) return;

            _isBulkOperation = true;

            BulkProgressBorder.Visibility = Visibility.Visible;
            UpdateAll.IsEnabled = false;
            ReinstallAll.IsEnabled = false;

            int completed = 0;
            int total = targets.Count;
            BulkProgressText.Text = $"{label} 0 / {total}";

            int maxParallel = 3;
            var semaphore = new SemaphoreSlim(maxParallel);

            var tasks = targets.Select(async t =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await operation(t.package, t.version);
                }
                finally
                {
                    semaphore.Release();

                    int done = Interlocked.Increment(ref completed);

                    Dispatcher.Invoke(() =>
                    {
                        BulkProgressText.Text = $"{label} {done} / {total}";
                        BulkProgressFill.Width =
                            (double)done / total * BulkProgressBorder.ActualWidth;
                    });
                }
            });

            await Task.WhenAll(tasks);

            _isBulkOperation = false;

            await LoadProject(currentproject);

            BulkProgressText.Text = $"Done {total} / {total}";
            BulkProgressFill.Width = BulkProgressBorder.ActualWidth;

            await Task.Delay(800);

            BulkProgressBorder.Visibility = Visibility.Collapsed;
            UpdateAll.IsEnabled = true;
            ReinstallAll.IsEnabled = true;
        }

        private async void UpdateAll_Click(object sender, RoutedEventArgs e)
        {
            
            var targets = allPackages
                .Where(p => p.IsInstalled && p.HasUpdate && !string.IsNullOrEmpty(p.LatestVersion) && p.Status != CompatibilityStatus.Incompatible)
                .Select(p => (p, p.LatestVersion))
                .ToList();
            await RunBulkOperation(targets, (p, v) => InstallOrUpdatePackage(p, v), "UPDATING");
        }

        private async void ReinstallAll_Click(object sender, RoutedEventArgs e)
        {
            var targets = allPackages
                .Where(p => p.IsInstalled && !string.IsNullOrEmpty(p.CurrentVersion) && p.Status != CompatibilityStatus.Incompatible)
                .Select(p => (p, p.CurrentVersion))
                .ToList();
            await RunBulkOperation(targets, (p, v) => InstallOrUpdatePackage(p, v, force: true), "REINSTALLING");
        }

        private void VersionSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.DataContext is ProjectPackage p)
            {
                string ver = cb.SelectedItem as string;
                if (!string.IsNullOrEmpty(ver) && ver != p.CurrentVersion)
                {
                    if ((bool)CustomDialog.Show($"Change {p.ID} to {ver}?", "Confirm", CustomDialog.Mode.Question))
                    {
                        p.CurrentAction = PackageAction.ChangingVersion;
                        _ = InstallOrUpdatePackage(p, ver);

                    }
                    else
                    {
                        cb.SelectionChanged -= VersionSelector_SelectionChanged;
                        cb.SelectedItem = p.IsInstalled ? p.CurrentVersion : p.LatestVersion;
                        cb.SelectionChanged += VersionSelector_SelectionChanged;
                    }
                }
            }
        }
        private static readonly System.Text.RegularExpressions.Regex PreReleaseRegex =
    new System.Text.RegularExpressions.Regex(@"[-+](alpha|beta|rc|pre|preview|exp|experimental)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static bool IsPreRelease(string version)
        {
            if (string.IsNullOrEmpty(version)) return false;
            return version.Contains('-') || version.Contains('+') && PreReleaseRegex.IsMatch(version);
        }

        private List<string> SortVersions(IEnumerable<string> versions, bool ascending = false, bool includePreRelease = true)
        {
            var filtered = versions.Where(v => !string.IsNullOrEmpty(v));

            if (!includePreRelease)
                filtered = filtered.Where(v => !IsPreRelease(v));

            return filtered
                .OrderBy(v => ascending ? UnityHelper.ParseVersion(v) : new Version(0, 0))
                .ThenByDescending(v => ascending ? new Version(0, 0) : UnityHelper.ParseVersion(v))
                .ToList();
        }
        private void RemoveProjectBtn_Click(object sender, RoutedEventArgs e)
        {
            if (currentproject == null) return;
            if ((bool)CustomDialog.Show($"Remove {currentproject.ProjectName} from BVCC?", "Confirm", CustomDialog.Mode.Question))
            {
                App.savedata.Projects.Remove(currentproject);
                App.SaveToDisk();
                App.ProjectsPage.RefreshProjects();
                UIHelper.SwipePage(App.ProjectsPage.ProjectListUI, true);
            }
        }
        private void LoadingOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            var sb = (Storyboard)Resources["ShimmerStoryboard"];
            sb.Begin();
        }

        private void BackupsBtn_Click(object sender, RoutedEventArgs e)
        {
            ProjectBackupPage bakckuppdage = new ProjectBackupPage(currentproject);
            UIHelper.SwipePage(bakckuppdage,false);
        }

        private void MoreBtn_Click(object sender, RoutedEventArgs e)
        {
            MorePopup.IsOpen = !MorePopup.IsOpen;
            double offset = MoreBtn.ActualWidth - MorePopup.Child.DesiredSize.Width;
            MorePopup.HorizontalOffset = offset;
        }

        private void CloneProjectBtn_Click(object sender, RoutedEventArgs e)
        {
            string newName = $"{currentproject.ProjectName} Copy";
            string newPath = Path.Combine(Path.GetDirectoryName(currentproject.ProjectPath), newName);
            int copyIndex = 1;
            while (Directory.Exists(newPath) || File.Exists(newPath))
            {
                newName = $"{currentproject.ProjectName} Copy {copyIndex}";
                newPath = Path.Combine(Path.GetDirectoryName(currentproject.ProjectPath), newName);
                copyIndex++;
            }
            try
            {
                if (Directory.Exists(currentproject.ProjectPath))
                    App.CopyDirectory(currentproject.ProjectPath, newPath);
                else if (File.Exists(currentproject.ProjectPath))
                    File.Copy(currentproject.ProjectPath, newPath);
                var newProject = new ProjectItem()
                {
                    ProjectName = newName,
                    ProjectPath = newPath,
                    LastModified = DateTime.Now
                };
                App.savedata.Projects.Add(newProject);
                App.SaveToDisk();
                App.ProjectsPage.RefreshProjects();
                UIHelper.SwipePage(App.ProjectsPage.ProjectListUI, true);
            }
            catch (Exception ex)
            {
                CustomDialog.Show($"Failed to clone project: {ex.Message}", "Error", CustomDialog.Mode.Message);
            }
        }

        private async void DeleteProjectBtn_Click(object sender, RoutedEventArgs e)
        {
            bool confirm = (bool)CustomDialog.Show(
                $"Delete {currentproject.ProjectName}? This action cannot be undone.",
                "Confirm",
                CustomDialog.Mode.Question);

            if (!confirm) return;

            string path = currentproject.ProjectPath;
            if(Directory.Exists(path))
            {
                try
                {
                    App.savedata.Projects.Remove(currentproject);
                    App.SaveToDisk();
                    App.ProjectsPage.RefreshProjects();
                    Directory.Delete(path, true);
                    UIHelper.SwipePage(App.ProjectsPage.ProjectListUI, true);
                }
                catch (Exception ex)
                {
                    CustomDialog.Show($"Failed to delete project: {ex.Message}", "Error", CustomDialog.Mode.Message);
                }
            }
        }

        private void ToggleDetailsBtn_Click(object sender, RoutedEventArgs e)
        {
            CheckBox checkBox = sender as CheckBox;
            DetailsStrip.Visibility = checkBox.IsChecked == true && App.api.IsLoggedIn ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}