using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static BVCC.Data;
using Path = System.IO.Path;

namespace BVCC
{
    public partial class ProjectBackupPage : UserControl
    {
        public ProjectItem Project;
        public ObservableCollection<ProjectBackup> Backups { get; set; }

        public ProjectBackupPage(ProjectItem project)
        {
            InitializeComponent();
            Project = project;
            ProjectNameText.Text = $"{project.ProjectName.ToUpper()} BACKUPS";
            Backups = new ObservableCollection<ProjectBackup>();
            BackupHistoryList.ItemsSource = Backups;

            LoadingBar.Visibility = Visibility.Collapsed;
            RefreshBackups();
        }

        private void RefreshBackups()
        {
            Backups.Clear();
            var projectBackups = App.savedata.ProjectBackups
                .Where(pb => pb.Project.ProjectPath == Project.ProjectPath)
                .OrderByDescending(pb => pb.date);

            foreach (var backup in projectBackups)
            {
                Backups.Add(backup);
            }

        }
        private async void CreateBackup_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            btn.IsEnabled = false;
            LoadingBar.Visibility = Visibility.Visible;
            LoadingBar.IsIndeterminate = true;
            LoadingBar.Value = 0;
            string customName = BackupNameInput.Text;

            if (string.IsNullOrWhiteSpace(customName) || customName == "Backup Name...")
            {
                var projectBackups = App.savedata.ProjectBackups
                    .Where(pb => pb.Project.ProjectPath == Project.ProjectPath)
                    .ToList();

                int nextNumber = 1;

                while (projectBackups.Any(pb => pb.Name.Equals($"{Project.ProjectName}-{nextNumber}", StringComparison.OrdinalIgnoreCase)))
                {
                    nextNumber++;
                }

                customName = $"{Project.ProjectName}-{nextNumber}";
            }
            else
            {
                bool exists = App.savedata.ProjectBackups.Any(pb =>
                    pb.Project.ProjectPath == Project.ProjectPath &&
                    string.Equals(pb.Name, customName, StringComparison.OrdinalIgnoreCase)
                );

                if (exists)
                {
                    CustomDialog.Show("Duplicate Name",
                         "A backup with this name already exists for this project. Please choose a different name.",
                         CustomDialog.Mode.Message);
                    LoadingBar.Visibility = Visibility.Collapsed;
                    btn.IsEnabled = true;
                    return;
                }
            }

            try
            {
                string sourcePath = Project.ProjectPath;
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

                string projectDir = Path.Combine(App.BackupFolder, Project.ProjectName);
                Directory.CreateDirectory(projectDir);
                string zipFilePath = Path.Combine(projectDir, $"{Project.ProjectName}_{timestamp}.zip");

                await Task.Run(() =>
                {
                    string[] foldersToInclude = { "Assets", "ProjectSettings", "Packages" };
                    int totalFiles = 0;
                    foreach (var f in foldersToInclude)
                    {
                        string path = Path.Combine(sourcePath, f);
                        if (Directory.Exists(path))
                            totalFiles += Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).Length;
                    }
                    Dispatcher.Invoke(() => LoadingBar.IsIndeterminate = false);
                    Dispatcher.Invoke(() => LoadingBar.Maximum = totalFiles);
                    using (ZipArchive archive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
                    {
                        foreach (string folderName in foldersToInclude)
                        {
                            string folderPath = Path.Combine(sourcePath, folderName);
                            if (!Directory.Exists(folderPath)) continue;

                            DirectoryInfo di = new DirectoryInfo(folderPath);
                            foreach (FileInfo file in di.GetFiles("*.*", SearchOption.AllDirectories))
                            {
                                string relativePath = Path.Combine(folderName, file.FullName.Substring(di.FullName.Length + 1));

                                archive.CreateEntryFromFile(file.FullName, relativePath);
                                Dispatcher.Invoke(() => LoadingBar.Value += 1);
                            }
                        }
                    }

                    var masterProject = App.savedata.Projects.FirstOrDefault(p => p.ProjectName == Project.ProjectName);
                    App.savedata.ProjectBackups.Add(new ProjectBackup
                    {
                        Name = customName ?? Project.ProjectName,
                        Path = zipFilePath,
                        Project = masterProject ?? Project,
                        date = DateTime.Now
                    });
                    App.SaveToDisk();
                });

                RefreshBackups();
            }
            catch (Exception ex)
            {
                CustomDialog.Show(ex.Message, "Backup Failed", CustomDialog.Mode.Message);
            }
            finally
            {
                LoadingBar.Visibility = Visibility.Collapsed;
                btn.IsEnabled = true;
            }
        }

        private void CopyDirectorySimple(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
            }
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectorySimple(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
            }
        }

        private void DeleteBackup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ProjectBackup item)
            {
                var vis = LoadingBar.Visibility;
                var intr = LoadingBar.IsIndeterminate;
                LoadingBar.Visibility = Visibility.Visible;
                LoadingBar.IsIndeterminate = true;
                App.savedata.ProjectBackups.Remove(item);
                App.SaveToDisk();
                File.Delete(item.Path);
                Backups.Remove(item);
                LoadingBar.Visibility = vis;
                LoadingBar.IsIndeterminate = intr;
            }
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e) => UIHelper.GoBack();

        private void LoadingBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LoadingBar.Template.FindName("ProgressFill", LoadingBar) is FrameworkElement fill)
            {
                double percent = LoadingBar.Value / LoadingBar.Maximum;
                double targetWidth = percent * LoadingBar.ActualWidth;
                fill.Width = targetWidth;
                //DoubleAnimation animation = new DoubleAnimation
                //{
                //    To = targetWidth,
                //    Duration = TimeSpan.FromMilliseconds(200),
                //    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut }
                //};
                //fill.BeginAnimation(FrameworkElement.WidthProperty, animation);
            }
        }
        private void BackupNameInput_GotFocus(object sender, RoutedEventArgs e)
        {
            if (BackupNameInput.Text == "Backup Name...")
            {
                BackupNameInput.Text = "";
                BackupNameInput.Opacity = 1.0;
            }
        }

        private void BackupNameInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(BackupNameInput.Text))
            {
                BackupNameInput.Text = "Backup Name...";
                BackupNameInput.Opacity = 0.5;
            }
        }

        private void OpenBackupPath_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is ProjectBackup p)
            {
                App.OpenProjectPath(p.Path);
            }
        }
        private async void CloneBackup_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is ProjectBackup p)
            {
                var btn = (Button)sender;
                btn.IsEnabled = false;
                LoadingBar.Visibility = Visibility.Visible;
                LoadingBar.IsIndeterminate = true;

                try
                {

                    string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    string newProjectName = $"{p.Name}-Clone-{timestamp}";
                    string parentDir = Directory.GetParent(p.Project.ProjectPath).FullName;
                    string newProjectPath = Path.Combine(parentDir, newProjectName);

                    await Task.Run(() =>
                    {
                        if (Directory.Exists(newProjectPath))
                        {
                            CustomDialog.Show("A folder with the new project name already exists. Please try again.", App.savedata.AppName, CustomDialog.Mode.Message);
                            return;
                        }

                        Directory.CreateDirectory(newProjectPath);

                        ZipFile.ExtractToDirectory(p.Path, newProjectPath);

                        App.savedata.Projects.Add(new ProjectItem
                        {
                            ProjectName = newProjectName,
                            ProjectPath = newProjectPath,
                            LastModified = DateTime.Now,
                            Starred = false
                        });

                        App.SaveToDisk();
                    });

                    App.ProjectsPage?.RefreshProjects();
                    CustomDialog.Show("Clone Created", $"Project cloned to:\n{newProjectPath}", CustomDialog.Mode.Message);
                    if (App.savedata.SwipeOnProjectClone)
                    {
                        UIHelper.SwipePage(App.ProjectsPage.ProjectListUI, true);
                    }
                }
                catch (Exception ex)
                {
                    CustomDialog.Show("Clone Failed", ex.Message, CustomDialog.Mode.Message);
                }
                finally
                {
                    LoadingBar.Visibility = Visibility.Collapsed;
                    btn.IsEnabled = true;
                }
            }
        }

        private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is ProjectBackup p)
            {
                bool confirm = (bool)CustomDialog.Show("Restore Backup?",
                    "This will OVERWRITE your current project files with this backup. This cannot be undone.",
                    CustomDialog.Mode.Question);

                if (!confirm) return;

                LoadingBar.Visibility = Visibility.Visible;
                LoadingBar.IsIndeterminate = true;

                try
                {
                    string extractPath = p.Project.ProjectPath;

                    await Task.Run(() =>
                    {
                        if (!HasWritePermission(extractPath))
                        {
                            CustomDialog.Show("The project folder is read-only or locked by another program (like Unity)", App.savedata.AppName, CustomDialog.Mode.Message);
                            return;
                        }
                        using (ZipArchive archive = ZipFile.OpenRead(p.Path))
                        {
                            foreach (ZipArchiveEntry entry in archive.Entries)
                            {
                                string fullPath = Path.Combine(extractPath, entry.FullName);
                                string directory = Path.GetDirectoryName(fullPath);

                                if (!Directory.Exists(directory))
                                    Directory.CreateDirectory(directory);

                                if (!string.IsNullOrEmpty(entry.Name))
                                    entry.ExtractToFile(fullPath, overwrite: true);
                            }
                        }
                    });

                    CustomDialog.Show("Success", "Project restored successfully!", CustomDialog.Mode.Message);
                }
                catch (Exception ex)
                {
                    CustomDialog.Show("Restore Failed", $"Ensure Unity is closed.\n\nError: {ex.Message}", CustomDialog.Mode.Message);
                }
                finally
                {
                    LoadingBar.Visibility = Visibility.Collapsed;
                }
            }
        }

        private bool HasWritePermission(string filePath)
        {
            try
            {
                using (FileStream fs = File.Create(Path.Combine(filePath, "temp.txt"), 1, FileOptions.DeleteOnClose))
                { return true; }
            }
            catch { return false; }
        }
    }
}