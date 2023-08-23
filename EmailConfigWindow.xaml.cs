using Microsoft.Win32;
using Streamer.bot.Plugin.Interface;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace EmailBot
{
    internal partial class EmailConfigWindow : Window
    {
        private readonly EmailNotifier Notifier;
        private bool HasCredentialsChanged = false;

        public EmailConfigWindow(EmailNotifier notifier)
        {
            InitializeComponent();
            
            Notifier = notifier;

            var once = false;
            Activated += delegate (object sender, EventArgs e) {
                if (once) return;
                once = true;

                GmailIcon.Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    EmailBot.Resources.GmailLogo.GetHbitmap(),
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(EmailBot.Resources.GmailLogo.Width, EmailBot.Resources.GmailLogo.Height)
                );

                if(Notifier.Config.GoogleCredentials != null)
                    CredFilename.Text = "Existing credentials found";
                
                if(Notifier.Config.GmailQueryFilter != null)
                    query.Text = Notifier.Config.GmailQueryFilter;

                if (Notifier.Config.GmailLabel != null)
                    label.Text = Notifier.Config.GmailLabel;

                if (Notifier.Config.PollerInterval > 0)
                    pollerDelay.Text = Notifier.Config.PollerInterval.ToString();

                CheckActivation();
            };
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            Notifier.Config.GmailQueryFilter = query.Text;
            Notifier.Config.GmailLabel = label.Text;
            try
            {
                Notifier.Config.PollerInterval = int.Parse(pollerDelay.Text);
            } catch (Exception)
            {
                MessageBox.Show("The delay configured is not a valid integer.");
                return;
            }

            Close();
        }

        private void CredSelect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog dialog = new OpenFileDialog();
                dialog.CheckFileExists = true;
                dialog.DefaultExt = "json";
                dialog.Filter = "credentials.json|credentials.json|.json files|*.json|All files|*.*";
                dialog.Title = "Select credentials.json file";
                dialog.ShowDialog();

                CredFilename.Text = System.IO.Path.GetFileName(dialog.FileName);
                var fileStream = dialog.OpenFile();
                StreamReader streamReader = new StreamReader(fileStream);
                Notifier.Config.GoogleCredentials = streamReader.ReadToEnd();
                HasCredentialsChanged = true;
                streamReader.Close();
                CheckActivation();
            } catch(Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void CheckActivation()
        {
            try
            {
                Regex _regex = new Regex("[^0-9.-]+");
                if(_regex.IsMatch(pollerDelay.Text))
                {
                    SaveButton.IsEnabled = false;
                    return;
                }

                SaveButton.IsEnabled = !(Notifier.Config.GoogleCredentials == null || Notifier.Config.GoogleCredentials == "" || query.Text == "" || label.Text == "" || pollerDelay.Text == "");
            }
            catch (Exception) { }
        }

        private void QueryTextChanged(object sender, TextChangedEventArgs e)
        {
            CheckActivation();
        }

        private void LabelTextChanged(object sender, TextChangedEventArgs e)
        {
            CheckActivation();
        }

        private void PollerDelayTextChanged(object sender, TextChangedEventArgs e)
        {
            CheckActivation();
        }

        private void HyperlinkRequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }
    }
}
