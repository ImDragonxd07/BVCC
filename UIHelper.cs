using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BVCC
{
    public static class UIHelper
    {
        private static Stack<object> _navigationHistory = new Stack<object>();

        private static bool _isNavigatingInternal = false;
        public static void GoBack()
        {
            if (_navigationHistory.Count > 0)
            {
                _isNavigatingInternal = true;
                object previousPage = _navigationHistory.Pop();
                SwipePage(previousPage, true);
            }
        }
        public static void SwipePage(object newContent, bool goingBack = false)
        {
            if (Application.Current.MainWindow is ProjectsPage mainWin)
            {
                // 1. Record history if this is a forward navigation
                if (!goingBack && !_isNavigatingInternal)
                {
                    if (mainWin.ViewA.Content != null)
                    {
                        _navigationHistory.Push(mainWin.ViewA.Content);
                    }
                }

                // --- Your existing Title Logic ---
                string title;
                switch (newContent)
                {
                    case object _ when newContent == App.SettingsPage: title = "SETTINGS"; break;
                    case object _ when newContent == App.NewFromTemplatePage: title = "NEW PROJECT FROM TEMPLATE"; break;
                    case PackageManagerPage _: title = "PACKAGE MANAGER"; break;
                    case NewProjectSettingsPage _: title = "CREATE NEW PROJECT"; break;
                    case ProjectBackupPage _: title = "PROJECT BACKUPS"; break;
                    default: title = "PROJECTS"; break;
                }

                App.ProjectsPage.SubHeaderText.Text = title;

                // --- Your Animation Logic ---
                if (mainWin.ViewA.Content == newContent) mainWin.ViewA.Content = null;
                if (mainWin.ViewB.Content == newContent) mainWin.ViewB.Content = null;

                double aExitPos = goingBack ? 1200 : -1200; // I bumped this to 1200 to ensure clear exit
                double bStartPos = goingBack ? -1200 : 1200;

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
                    _isNavigatingInternal = false;
                };

                mainWin.TransA.BeginAnimation(TranslateTransform.XProperty, animA);
                mainWin.TransB.BeginAnimation(TranslateTransform.XProperty, animB);
            }
        }
    }
    
}