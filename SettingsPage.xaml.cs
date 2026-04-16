using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Net.Http;
using System.Security.Policy;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static BVCC.Data;

namespace BVCC
{
    public partial class SettingsPage : UserControl
    {
        public SettingsPage()
        {
            InitializeComponent();
            RefreshPage();
            NameText.Text = App.savedata.AppName;
            VersionText.Text = App.savedata.AppVersion;
            _ = LoadReleasesAsync();
        }
        private async Task LoadReleasesAsync()
        {
            if (App.GitHubReleases == null || App.GitHubReleases.Count < 1)
            {
                App.GitHubReleases = await App.GetAllReleases();
            }

            AppVersionComboBox.ItemsSource = App.GitHubReleases;

            AppVersionComboBox.SelectionChanged -= AppVersionComboBox_SelectionChanged;

            AppVersionComboBox.SelectedItem = App.GitHubReleases
                                                    .FirstOrDefault(r =>
                                                        string.Equals(r.Version?.Trim(),
                                                                      App.savedata.AppVersion?.Trim(),
                                                                      StringComparison.OrdinalIgnoreCase));

            AppVersionComboBox.SelectionChanged += AppVersionComboBox_SelectionChanged;
        }
        private void RefreshPage()
        {
            RepoListBox.ItemsSource = null;
            RepoListBox.ItemsSource = App.savedata.Repositories;
            UnityEditorPathBox.Text = App.savedata.UnityEditorPath;
            ProjectPathBox.Text = App.savedata.ProjectsFolder;
            LocalRepoPathBox.Text = App.savedata.RepoPath;
            OpenAfterProjectCreateCheckBox.IsChecked = App.savedata.OpenUnityAfterProjectCreation;
            ShowPreReleasesCheckBox.IsChecked = App.savedata.ShowPreReleases;
            CheckForUpdatesCheckBox.IsChecked = App.savedata.CheckForUpdates;
        }

        public async Task<List<RepoItem>> FetchRepoPackagesAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return new List<RepoItem>();

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return new List<RepoItem>();

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", $"{App.savedata.AppName}-App");
                client.Timeout = TimeSpan.FromSeconds(10);

                try
                {
                    string jsonString = await client.GetStringAsync(uri);

                    if (string.IsNullOrWhiteSpace(jsonString))
                        return new List<RepoItem>();

                    JObject data;

                    try
                    {
                        data = JObject.Parse(jsonString);
                    }
                    catch
                    {
                        return new List<RepoItem>();
                    }

                    JObject packagesObj = data["packages"] as JObject;

                    if (packagesObj == null)
                        return new List<RepoItem>();

                    var result = new List<RepoItem>();

                    foreach (var pkgProp in packagesObj.Properties())
                    {
                        string pkgId = pkgProp.Name;

                        result.Add(new RepoItem
                        {
                            Url = url,
                            Id = pkgId,
                            Name = pkgProp.Value["name"]?.ToString() ?? pkgId
                        });
                    }

                    return result;
                }
                catch
                {
                    MessageBox.Show("Failed to fetch or parse the repository data. Please check the URL and try again.");
                    return new List<RepoItem>();
                }
            }
        }
        public void AddPackages(List<RepoItem> packages)
        {
            if (packages != null && packages.Count > 0)
            {
                int added = 0;

                foreach (var pkg in packages)
                {
                    bool exists = App.savedata.Repositories.Any(r =>
                        string.Equals(r.Id, pkg.Id, StringComparison.OrdinalIgnoreCase));

                    if (exists) continue;

                    App.savedata.Repositories.Add(pkg);
                    added++;
                }

                App.SaveToDisk();
                RefreshPage();
            }
        }
        private async void AddRepo_Click(object sender, RoutedEventArgs e)
        {
            string url = RepoUrlInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                var packages = await FetchRepoPackagesAsync(url);
                AddPackages(packages);
                MessageBox.Show($"Added {packages.Count} packages from listing.");
                RepoUrlInput.Text = "";
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load:\n{ex.Message}");
            }
        }
        private void RemoveRepo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is RepoItem item)
            {
                App.savedata.Repositories.Remove(item);
                RefreshPage();
                App.SaveToDisk();
            }
        }

        private void ImportVCC_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Json files (*.json)|*.json|All files (*.*)|*.*",
                InitialDirectory = Environment.ExpandEnvironmentVariables(@"%LocalAppData%\VRChatCreatorCompanion")
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string data = File.ReadAllText(openFileDialog.FileName);
                JObject vccdata = JObject.Parse(data);
                var projects = vccdata["userProjects"].ToArray();
                int i = projects.Length;
                foreach (string property in projects)
                {
                    App.savedata.Projects.Add(new ProjectItem
                    {
                        ProjectName = Path.GetFileName(property),
                        ProjectPath = property,
                        LastModified = new DateTime(i),
                    });
                    i--;
                }
                var editors = vccdata["preferredUnityEditors"] as JObject;
                if (editors != null && editors.HasValues)
                {
                    App.savedata.UnityEditorPath = editors.Properties().First().Value.ToString();
                }
                var defPath = vccdata["defaultProjectPath"]?.ToString();
                if (!string.IsNullOrEmpty(defPath)) App.savedata.ProjectsFolder = defPath;
                var userRepos = vccdata["userRepos"] as JArray;
                if (userRepos != null)
                {
                    foreach (var repo in userRepos)
                    {
                        string rUrl = repo["url"]?.ToString();
                        if (string.IsNullOrEmpty(rUrl)) continue;

                        string rId =
                            repo["id"]?.ToString() ??
                            rUrl;

                        if (!App.savedata.Repositories.Any(r =>
                            string.Equals(r.Url, rUrl, StringComparison.OrdinalIgnoreCase)))
                        {
                            App.savedata.Repositories.Add(new RepoItem
                            {
                                Url = rUrl,
                                Name = repo["name"]?.ToString() ?? "Imported Repo",
                                Id = rId,
                                LocalPath = repo["localPath"]?.ToString()
                            });
                        }
                    }
                }
                App.savedata.Repositories.Add(new RepoItem
                {
                    Url = "https://vrchat.github.io/packages/index.json",
                    Name = "VRChat Official",
                    Id = "com.vrchat.repos.official"
                });
                App.ProjectsPage.RefreshProjects();
                RefreshPage();
                App.SaveToDisk();
                System.Windows.MessageBox.Show("VCC Data Imported Successfully!");
            }
        }

        private void UnityEditorPathButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Unity Editor (Unity.exe)",
                Filter = "Unity Executable|Unity.exe",
                InitialDirectory = App.savedata.ProjectsFolder,
                CheckFileExists = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                App.savedata.UnityEditorPath = openFileDialog.FileName;
                RefreshPage();
                App.SaveToDisk();
            }
        }

        private void ProjectPathBoxButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select the default folder for your Unity Projects";
                dialog.SelectedPath = App.savedata.ProjectsFolder;
                if (Directory.Exists(ProjectPathBox.Text)) dialog.SelectedPath = ProjectPathBox.Text;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    App.savedata.ProjectsFolder = dialog.SelectedPath;
                    RefreshPage();
                    App.SaveToDisk();
                }
            }
        }
        private void LocalRepoPathButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select the default folder for your Local Repos";
                dialog.SelectedPath = App.savedata.RepoPath;
                if (Directory.Exists(ProjectPathBox.Text)) dialog.SelectedPath = ProjectPathBox.Text;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    App.savedata.RepoPath = dialog.SelectedPath;
                    RefreshPage();
                    App.SaveToDisk();
                }
            }
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            UIHelper.SwipePage(App.ProjectsPage.ProjectListUI, true);
        }

        public void ResetDataBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to reset all data? This cannot be undone.",
                                 "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                File.Delete("Settings.json");
                Process.Start(Process.GetCurrentProcess().MainModule.FileName);
                Application.Current.Shutdown();
            }
        }

        private void ExportRepo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON Files (*.json)|*.json",
                    FileName = "bvcc-repos.json"
                };

                if (dialog.ShowDialog() != true)
                    return;

                var exportObj = new
                {
                    App = "BVCC",
                    ExportedAt = DateTime.UtcNow,
                    Repositories = App.savedata.Repositories
                };

                var json = JsonConvert.SerializeObject(exportObj, Formatting.Indented);
                File.WriteAllText(dialog.FileName, json);

                MessageBox.Show("Repositories exported successfully.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed:\n{ex.Message}");
            }
        }

        private void RepoSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (RepoListBox == null) return;

            string query = RepoSearchBox.Text?.ToLower() ?? "";

            RepoListBox.ItemsSource = App.savedata.Repositories
                .Where(r => r.Name?.ToLower().Contains(query) == true
                         || r.Url?.ToLower().Contains(query) == true)
                .ToList();
        }

        private void ImportRepo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "JSON Files (*.json)|*.json"
                };

                if (dialog.ShowDialog() != true)
                    return;

                string json = File.ReadAllText(dialog.FileName);
                JObject root = JObject.Parse(json);

                JToken reposToken;

                if (root["App"]?.ToString() == "BVCC")
                {
                    reposToken = root["Repositories"];
                }
                else
                {
                    reposToken = root;
                }

                if (reposToken == null)
                {
                    MessageBox.Show("Invalid repo file.");
                    return;
                }

                var imported = reposToken.ToObject<List<RepoItem>>() ?? new List<RepoItem>();

                int added = 0;

                foreach (var repo in imported)
                {
                    bool exists = App.savedata.Repositories.Any(r =>
                        string.Equals(r.Url, repo.Url, StringComparison.OrdinalIgnoreCase));

                    if (exists)
                        continue;

                    App.savedata.Repositories.Add(new RepoItem
                    {
                        Name = repo.Name,
                        Url = repo.Url,
                        Id = repo.Id
                    });

                    added++;
                }

                App.SaveToDisk();
                //App.PackageManagerPage?.ClearRepoCache();
                RefreshPage();

                MessageBox.Show($"Imported {added} repositories.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed:\n{ex.Message}");
            }
        }

        private void OpenDataButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Settings.json");

                System.Diagnostics.Process.Start("explorer.exe", path);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open data folder: {ex.Message}");
            }
        }

        private void ClearCacheBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string reposPath = Path.Combine(Directory.GetCurrentDirectory(), "Repos");
                foreach (var file in Directory.GetFiles(reposPath))
                {
                    File.Delete(file);
                }
                foreach (var dir in Directory.GetDirectories(reposPath))
                {
                    Directory.Delete(dir, true);
                }
                MessageBox.Show("Cache cleared.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to clear cache: {ex.Message}");
            }
        }

        private void OpenAfterProjectCreateCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (App.savedata == null) return;
            CheckBox checkBox = sender as CheckBox;
            if (checkBox == null) return;
            App.savedata.OpenUnityAfterProjectCreation = checkBox.IsChecked == true;
            App.SaveToDisk();
        }

        private void ShowPreReleasesCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (App.savedata == null) return;
            CheckBox checkBox = sender as CheckBox;
            if (checkBox == null) return;
            App.savedata.ShowPreReleases = checkBox.IsChecked == true;
            App.SaveToDisk();
        }

        private void CheckForUpdatesCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (App.savedata == null) return;
            CheckBox checkBox = sender as CheckBox;
            if (checkBox == null) return;
            App.savedata.CheckForUpdates = checkBox.IsChecked == true;
            App.SaveToDisk();
        }


        private void AppVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            GitHubRelease selectedrelease = AppVersionComboBox.SelectedItem as Data.GitHubRelease;
            if (selectedrelease == null || selectedrelease.Version == App.savedata.AppVersion) return;
            var result = MessageBox.Show(
                "Changing your app version may result in instability, missing features, or broken projects. Only proceed if you understand the risks.",
                "Change app version",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
            else
            {
                Application.Current.MainWindow.Close();
                App.splash.Show();
                Application.Current.MainWindow = App.splash;
                App.savedata.CheckForUpdates = false;
                App.DownloadAndInstallVersion(selectedrelease);
            }
        }
    }
}