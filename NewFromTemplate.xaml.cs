using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using static BVCC.Data;

namespace BVCC
{
    public partial class NewFromTemplate : UserControl
    {
        public NewFromTemplate()
        {
            InitializeComponent();
            List<TemplateItem> templateItems = new List<TemplateItem>();
            var templateDir = new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates"));

            templateItems.Clear();

            foreach (var dir in templateDir.GetDirectories())
            {
                var templateItem = new TemplateItem
                {
                    Name = dir.Name,
                    TemplatePath = dir.FullName,
                };
                if (dir.Name.Contains("Avatar"))
                {
                    templateItem.Description = "Create a new avatar project";
                    templateItem.Image = "/person.png";
                    templateItem.Type = TemplateType.Avatar;
                }
                else if(dir.Name.Contains("World"))
                {
                    templateItem.Description = "Create a new world project";
                    templateItem.Image = "/worldlogo.png";
                    templateItem.Type = TemplateType.World;
                }
                else
                {
                    templateItem.Description = "Create a new project";
                    templateItem.Type = TemplateType.Other;
                }

                templateItems.Add(templateItem);
            }

            TemplateList.ItemsSource = templateItems;
        }

        private void BackBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            UIHelper.GoBack();
        }

        private void Template_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TemplateItem template)
            {
                NewProjectSettingsPage projectSettingsPage = new NewProjectSettingsPage();
                projectSettingsPage.SetTemplate(template);
                UIHelper.SwipePage(projectSettingsPage, false);
            }
        }
    }
    
}