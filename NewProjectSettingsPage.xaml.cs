using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using static BVCC.Data;

namespace BVCC
{
    public partial class NewProjectSettingsPage : System.Windows.Controls.UserControl, INotifyPropertyChanged
    {
        public static readonly HttpClient client = new HttpClient();

        private List<ProjectPackage> _filteredPackages = new List<ProjectPackage>();
        private List<ProjectPackage> _availablePackages = new List<ProjectPackage>();

        private bool _suppressSelectionChanged = false;

        private TemplateItem _template;
        private Task _loadTask;

        public event PropertyChangedEventHandler PropertyChanged;
        private string projectpath;
        private double _createProgress;
        private HashSet<string> _selectedPackageIds = new HashSet<string>();

        public double CreateProgress
        {
            get => _createProgress;
            set
            {
                if (_createProgress == value) return;
                _createProgress = value;
                OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public NewProjectSettingsPage()
        {
            InitializeComponent();
        }

        private void RefreshRepoList()
        {
            _suppressSelectionChanged = true;
            var items = _filteredPackages
                .OrderByDescending(p => _selectedPackageIds.Contains(p.ID))
                .ThenBy(p => p.Name)
                .ToList();
            RepoList.ItemsSource = items;
            RepoList.SelectedItems.Clear();
            foreach (var item in items)
            {
                if (_selectedPackageIds.Contains(item.ID))
                    RepoList.SelectedItems.Add(item);
            }
            if(_selectedPackageIds.Count > 0)
            {
                BulkActionBar.Visibility = Visibility.Visible;
                SelectedCountText.Text = $"{_selectedPackageIds.Count} selected";
            }
            else
            {
                BulkActionBar.Visibility = Visibility.Collapsed;
            }

            _suppressSelectionChanged = false;
        }

        private async Task LoadRepositories()
        {
            RepoGhostLoading.Visibility = Visibility.Visible;
            RepoList.Visibility = Visibility.Collapsed;

            await App.EnsureRepoCacheAsync();

            _availablePackages = App.CachedPackages;
            _filteredPackages = _availablePackages;

            RepoGhostLoading.Visibility = Visibility.Collapsed;
            RepoList.Visibility = Visibility.Visible;

            RefreshRepoList();
            ProjectPathBox.Text = System.IO.Path.Combine(
               projectpath,
               MakeSafeFolderName(ProjectNameBox.Text)
           );
        }

        private void refreshselector()
        {
            TemplateSelector.ItemsSource = null;
            TemplateSelector.ItemsSource = App.savedata.RepoTemplates;
        }

        public async Task SetTemplate(TemplateItem template)
        {
            _template = template;
            CreateBtn.Visibility = Visibility.Collapsed;

            HeaderText.Text = template.Name;
            ProjectNameBox.Text = template.Name;
            projectpath = App.savedata.ProjectsFolder;

            _loadTask = LoadRepositories();
            await _loadTask;
            refreshselector();
            RepoTemplate defaultTemplate = null;

            switch (template.Type)
            {
                case TemplateType.World:
                    defaultTemplate = App.savedata.RepoTemplates
                        .FirstOrDefault(t => t.Name == "WorldDefault");
                    break;

                case TemplateType.Avatar:
                    defaultTemplate = App.savedata.RepoTemplates
                        .FirstOrDefault(t => t.Name == "AvatarDefault");
                    break;

                default:
                    defaultTemplate = App.savedata.RepoTemplates.FirstOrDefault();
                    break;
            }

            if (defaultTemplate != null)
            {
                SelectTemplate(defaultTemplate);
                TemplateSelector.SelectedItem = defaultTemplate;
            }
            CreateBtn.Visibility = Visibility.Visible;
        }

        private async void TemplateSelector_SelectionChangedAsync(object sender, SelectionChangedEventArgs e)
        {
            var template = TemplateSelector.SelectedItem as RepoTemplate;
            if (template == null) return;
            await SelectTemplate(template);
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TemplateNameBox.Text)) return;

            string name = TemplateNameBox.Text.Trim();

            RepoTemplate newtemplate = App.savedata.RepoTemplates
                .FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (newtemplate == null)
            {
                newtemplate = new RepoTemplate
                {
                    Name = name
                };

                App.savedata.RepoTemplates.Add(newtemplate);
            }

            newtemplate.PackageIDs = RepoList.SelectedItems
                .OfType<ProjectPackage>()
                .Where(p => !string.IsNullOrWhiteSpace(p.ID))
                .Select(p => p.ID)
                .ToList();

            refreshselector();
            TemplateSelector.SelectedItem = newtemplate;

            App.SaveToDisk();
        }
        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            UIHelper.GoBack();
        }

        private void BrowseProjectPath_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog();
            dialog.ShowDialog();
            if (Directory.Exists(dialog.FolderName))
            {
                projectpath = dialog.FolderName;
                ProjectNameBox_TextChanged(null, null);
            }
        }

        private void RepoSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = RepoSearchBox.Text.Trim().ToLower();

            _filteredPackages = string.IsNullOrEmpty(query)
                ? _availablePackages
                : _availablePackages
                    .Where(p => p.Name.ToLower().Contains(query) || p.ID.ToLower().Contains(query))
                    .ToList();

            RefreshRepoList();
        }

        private void RepoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionChanged)
                return;

            foreach (ProjectPackage p in e.AddedItems.OfType<ProjectPackage>())
                _selectedPackageIds.Add(p.ID);

            foreach (ProjectPackage p in e.RemovedItems.OfType<ProjectPackage>())
                _selectedPackageIds.Remove(p.ID);

            SelectedCountText.Text = $"{_selectedPackageIds.Count} selected";
            BulkActionBar.Visibility = _selectedPackageIds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in _filteredPackages)
                _selectedPackageIds.Add(p.ID);
            RefreshRepoList();
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            _selectedPackageIds.ExceptWith(_filteredPackages.Select(p => p.ID));
            RefreshRepoList();
        }
        private async Task SelectTemplate(RepoTemplate template)
        {
            if (template == null) return;

            if (_loadTask != null)
                await _loadTask;

            _selectedPackageIds = new HashSet<string>(template.PackageIDs);
            TemplateNameBox.Text = template.Name;

            var validIds = _availablePackages.Select(p => p.ID).ToHashSet();

            var missing = template.PackageIDs
                .Where(id => !validIds.Contains(id))
                .ToList();

            _selectedPackageIds = new HashSet<string>(
                template.PackageIDs.Where(id => validIds.Contains(id)));

            RefreshRepoList();

            if (missing.Count > 0)
            {
                CustomDialog.Show(
                    $"{missing.Count} packages in {template.Name} are missing from repositories and will be ignored.",
                    "Warning",
                    CustomDialog.Mode.Message);
            }

            var count = _selectedPackageIds.Count;
            BulkActionBar.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            SelectedCountText.Text = $"{count} selected";
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var template = TemplateSelector.SelectedItem as RepoTemplate;
            if (template == null) return;
            
            App.savedata.RepoTemplates.Remove(template);
            refreshselector();
            TemplateSelector.SelectedIndex = 0;

            App.SaveToDisk();
        }


        private async Task SetProgress(double value)
        {
            CreateProgress = value;
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        }
        private async void CreateProject_Click(object sender, RoutedEventArgs e)
        {
            var projectName = ProjectNameBox.Text.Trim();
            var projectPath = ProjectPathBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(projectName) ||
                string.IsNullOrWhiteSpace(projectPath) ||
                TemplateSelector.SelectedItem == null)
                return;

            var template = TemplateSelector.SelectedItem as RepoTemplate;
            if (template == null)
                return;

            string destination = projectPath;
            if (Directory.Exists(destination))
            {
                var result = (bool)CustomDialog.Show(
                    "A project already exists at this location.\nDo you want to overwrite it?",
                    "Confirm Overwrite",
                    CustomDialog.Mode.Question);

                if (!result)
                {
                    return;
                }

                Directory.Delete(destination, true);
            }

            string logPath = Path.Combine(destination, "bvcc-install-log.txt");

            void Log(string msg)
            {
                try
                {
                    File.AppendAllText(logPath,
                        $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
                }
                catch { }
            }

            try
            {
                Directory.CreateDirectory(destination);

                File.WriteAllText(
                    logPath,
                    $"[{DateTime.Now:o}] START\nApp: {App.savedata.AppName}\nVersion: {App.savedata.AppVersion}\nProject: {projectName}\nPath: {destination}\n\n"
                );

                Log("Initial log created");

                CreateProgress = 0;



                string source = _template.TemplatePath;

                if (!Directory.Exists(source))
                {
                    Log("Template missing: " + source);
                    CustomDialog.Show("Template not found.", App.savedata.AppName, CustomDialog.Mode.Message);
                    return;
                }

                await SetProgress(5);
                Log("Starting file copy");

                await Task.Run(() =>
                {
                    App.CopyDirectory(source, destination);
                });

                Log("File copy complete");
                await SetProgress(50);

                string pkgJson = Path.Combine(destination, "package.json");
                string readme = Path.Combine(destination, "README.md");

                if (File.Exists(pkgJson))
                {
                    File.Delete(pkgJson);
                    Log("Deleted package.json");
                }

                if (File.Exists(readme))
                {
                    File.Delete(readme);
                    Log("Deleted README.md");
                }

                await SetProgress(100);

                Log("Importing project into manager");

                var project = App.ProjectsPage.ImportProjectFromFile(destination);
                PackageManagerPage packageManager = new PackageManagerPage();
                await packageManager.LoadProject(project);

                Log("Project loaded into PackageManager");

                var packages = _availablePackages
                                            .Where(p => _selectedPackageIds.Contains(p.ID))
                                            .ToList();

                int total = packages.Count;
                int done = 0;

                Log($"Installing packages: {total}");

                foreach (var pkg in packages)
                {
                    try
                    {
                        Log($"Installing {pkg.ID}");

                        await packageManager.InstallLatestVersion(pkg);

                        done++;

                        double progress = ((double)done / total) * 100;
                        await SetProgress(progress);
                        Log($"Installed {pkg.ID} ({done}/{total}) {pkg.CurrentVersion}");
                    }
                    catch (Exception ex)
                    {
                        Log($"ERROR installing {pkg.ID}: {ex.Message}");
                    }
                }

                Log("All installs complete");

                await SetProgress(100);
                App.SaveToDisk();

                Log("Project saved");

                UIHelper.SwipePage(packageManager, true);

                Log("SwipePage");
                if (App.savedata.OpenUnityAfterProjectCreation)
                {
                    App.OpenProject(project);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(logPath,
                        $"[{DateTime.Now:HH:mm:ss.fff}] FATAL ERROR: {ex}\n");
                }
                catch { }

                CustomDialog.Show("Project creation failed. Check log file.", App.savedata.AppName, CustomDialog.Mode.Message);
            }
        }
        private static string MakeSafeFolderName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "NewProject";

            var invalid = System.IO.Path.GetInvalidFileNameChars();

            var cleaned = new string(input
                .Where(c => !invalid.Contains(c))
                .ToArray());

            cleaned = string.Join("_", cleaned.Split(' ', (char)StringSplitOptions.RemoveEmptyEntries));

            cleaned = cleaned.Trim().Trim('.');

            return string.IsNullOrWhiteSpace(cleaned) ? "NewProject" : cleaned;
        }
        private void ProjectNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ProjectNameBox == null || ProjectPathBox == null || string.IsNullOrEmpty(projectpath))
                return;
            var name = ProjectNameBox.Text.Trim();
            HeaderText.Text = name;
            string safeName = MakeSafeFolderName(name);
            ProjectPathBox.Text = System.IO.Path.Combine(projectpath, safeName);
        }

    }
}