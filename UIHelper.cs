using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BVCC
{
    public static class UIHelper
    {

        public static void SwipePage(object newContent, bool goingBack = false)
        {
            if (Application.Current.MainWindow is ProjectsPage mainWin)
            {
                string title;
                switch (newContent)
                {
                    case var x when x == App.SettingsPage:
                        title = "SETTINGS";
                        break;

                    case var x when x == typeof(PackageManagerPage):
                        title = "PACKAGE MANAGER";
                        break;

                    case var x when x == App.NewFromTemplatePage:
                        title = "NEW PROJECT FROM TEMPLATE";
                        break;

                    case var x when x == typeof(NewProjectSettingsPage):
                        title = "CREATE NEW PROJECT";
                        break;

                    default:
                        title = "PROJECTS";
                        break;
                }
                App.ProjectsPage.SubHeaderText.Text = title;
                if (mainWin.ViewA.Content == newContent) mainWin.ViewA.Content = null;
                if (mainWin.ViewB.Content == newContent) mainWin.ViewB.Content = null;
                double aExitPos = goingBack ? 900 : -900;
                double bStartPos = goingBack ? -900 : 900;
                Duration duration = new Duration(TimeSpan.FromSeconds(0.4));
                IEasingFunction ease = new CircleEase { EasingMode = EasingMode.EaseInOut };
                mainWin.ViewB.Content = newContent;
                mainWin.ViewB.Visibility = Visibility.Visible;
                mainWin.TransB.X = bStartPos;
                DoubleAnimation animA = new DoubleAnimation(aExitPos, duration) { EasingFunction = ease };
                DoubleAnimation animB = new DoubleAnimation(0, duration) { EasingFunction = ease };
                animB.Completed += (s, e) =>
                {
                    mainWin.TransA.BeginAnimation(TranslateTransform.XProperty, null);
                    mainWin.TransB.BeginAnimation(TranslateTransform.XProperty, null);
                    mainWin.ViewB.Content = null;
                    mainWin.ViewA.Content = newContent;
                    mainWin.TransA.X = 0;
                    mainWin.ViewB.Visibility = Visibility.Collapsed;
                };
                mainWin.TransA.BeginAnimation(TranslateTransform.XProperty, animA);
                mainWin.TransB.BeginAnimation(TranslateTransform.XProperty, animB);
            }
        }
    }
    
}