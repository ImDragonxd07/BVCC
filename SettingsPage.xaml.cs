using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Policy;
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
        }

        private void RefreshPage()
        {
            RepoListBox.ItemsSource = null;
            RepoListBox.ItemsSource = App.savedata.Repositories;
            UnityEditorPathBox.Text = App.savedata.UnityEditorPath;
            ProjectPathBox.Text = App.savedata.ProjectsFolder;
            LocalRepoPathBox.Text = App.savedata.RepoPath;
        }

        private async void AddRepo_Click(object sender, RoutedEventArgs e)
        {
            string url = RepoUrlInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(url)) return;

            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "BVCC-App");

                    string jsonString = await client.GetStringAsync(url);
                    JObject data = JObject.Parse(jsonString);

                    JObject packagesObj = data["packages"] as JObject;
                    bool isListing = packagesObj != null;
                    if (isListing)
                    {
                        int added = 0;

                        foreach (var pkgProp in packagesObj.Properties())
                        {
                            string pkgId = pkgProp.Name;

                            bool exists = App.savedata.Repositories.Any(r =>
                                string.Equals(r.Id, pkgId, StringComparison.OrdinalIgnoreCase)
                            );

                            if (exists) continue;

                            App.savedata.Repositories.Add(new RepoItem
                            {
                                Url = url,
                                Id = pkgId,
                                Name = pkgProp.Value["name"]?.ToString() ?? pkgId
                            });

                            added++;
                        }

                        RefreshPage();
                        App.SaveToDisk();
                        //App.PackageManagerPage?.ClearRepoCache();

                        MessageBox.Show($"Added {added} packages from listing.");
                        return;
                    }

                    string repoName = data["name"]?.ToString() ?? "Unknown Repo";
                    string repoId = data["id"]?.ToString() ?? url;

                    bool existsRepo = App.savedata.Repositories.Any(r =>
                        string.Equals(r.Url, url, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(r.Id, repoId, StringComparison.OrdinalIgnoreCase)
                    );

                    if (existsRepo)
                    {
                        MessageBox.Show("This repository already exists.");
                        return;
                    }

                    App.savedata.Repositories.Add(new RepoItem
                    {
                        Url = url,
                        Name = repoName,
                        Id = repoId
                    });

                    RepoUrlInput.Text = "";
                    RefreshPage();
                    App.SaveToDisk();
                    //App.PackageManagerPage?.ClearRepoCache();
                }
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
                dialog.SelectedPath = App.savedata.RepoPath ;
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
                string path = Path.Combine(Directory.GetCurrentDirectory(),"Settings.json");

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
    }
}