using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using static BVCC.Data;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;

namespace BVCC
{

    public partial class ProjectsPage : Window
    {
        public FrameworkElement ProjectListUI;
        public ProjectsPage()
        {
            InitializeComponent();
            ProjectListUI = ViewA.Content as FrameworkElement;
            CreateFromTemplateButton.Visibility = App.TemplatesInsalled ? Visibility.Visible : Visibility.Collapsed;
            RefreshProjects();
        }
        string searchText = "";
        public void RefreshProjects()
        {
            ProjectListBox.ItemsSource = App.savedata.Projects;
            ICollectionView view = CollectionViewSource.GetDefaultView(ProjectListBox.ItemsSource);

            if (view != null)
            {
                view.Filter = (obj) =>
                {
                    if (string.IsNullOrWhiteSpace(searchText)) return true;
                    if (obj is ProjectItem project) // Replace 'Project' with your actual class name
                    {
                        return project.ProjectName.ToLower().Contains(searchText.ToLower());
                    }
                    return false;
                };

                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription("Starred", ListSortDirection.Descending));
                view.SortDescriptions.Add(new SortDescription("LastModified", ListSortDirection.Descending));
                view.Refresh();
            }
        }
        private ProjectItem GetProjectItem(object sender)
        {
            if (sender is Button btn && btn.DataContext is ProjectItem project)
            {
                return project as ProjectItem;
            }
            else
            {
                return null;
            }
        }

        // EVENTS
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void EXIT_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void MINIMISE_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private void SETTINGS_Click(object sender, RoutedEventArgs e) {
            UIHelper.SwipePage(App.SettingsPage);
        }

        private void LaunchBtn_Click(object sender, RoutedEventArgs e)
        {
            ProjectItem project = GetProjectItem(sender);
            if (project != null)
            {
                App.OpenProject(project);
            }
        }

        private void ManageButton_Click(object sender, RoutedEventArgs e)
        {
            ProjectItem project = GetProjectItem(sender);
            if (project != null) {
                PackageManagerPage packageManagerPage = new PackageManagerPage();
                packageManagerPage.LoadProject(project);
                UIHelper.SwipePage(packageManagerPage);
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshProjects();
        }
        public ProjectItem GetProjectItemFromPath(string path)
        {
            ProjectItem newitem = new ProjectItem
            {
                ProjectName = System.IO.Path.GetFileName(path),
                ProjectPath = (string)path,
                LastModified = DateTime.Now
            };
            return newitem;
        }
        public ProjectItem ImportProjectFromFile(string path)
        {
            ProjectItem newitem = GetProjectItemFromPath(path);
            if (App.savedata.Projects.Contains(newitem)) { 
                System.Windows.MessageBox.Show("Project already exists in the list.", "Import Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null; }
            App.savedata.Projects.Add(newitem);
            App.SaveToDisk();
            RefreshProjects();
            return newitem;
        }
        private void ImportProject_Click(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.ShowDialog();
            if(Directory.Exists(dialog.SelectedPath))
            {
                ImportProjectFromFile(dialog.SelectedPath);
            }
        }

        private void StarBtn_Click(object sender, RoutedEventArgs e)
        {
            ProjectItem project = GetProjectItem(sender);
            if (project != null)
            {
                project.Starred = !project.Starred;
                RefreshProjects();
                App.SaveToDisk();
            }
        }
        private void ProjectSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            searchText = ProjectSearchBox.Text.Trim().ToLower();
            RefreshProjects();
        }

        private void CreateFromTemplate_Click(object sender, RoutedEventArgs e)
        {
            UIHelper.SwipePage(App.NewFromTemplatePage);
        }
    }
}