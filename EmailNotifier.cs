using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Streamer.bot.Plugin.Interface;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Text;
using System.Windows;
using System.Drawing.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json;

namespace EmailBot
{
    public class EmailNotifier : IDisposable
    {
        private static readonly string[] Scopes = { GmailService.Scope.GmailReadonly };
        
        private readonly string LogoPath;
        internal readonly IInlineInvokeProxy BotProxy;

        private GmailService EmailService;
        private readonly HashSet<string> PastIds = new HashSet<string>();
        private long? NotBefore = null;

        private Timer PollerTimer = null;
        private bool Paused = false;

        private Application App;
        private Dispatcher UiDispatch;

        internal readonly EmailConfig Config;

        public EmailNotifier(IInlineInvokeProxy botProxy)
        {
            BotProxy = botProxy;
            BotProxy.LogDebug("[Email.bot] Email Notifier started. Loading config...");

            Config = EmailConfig.Load(botProxy);
            BotProxy.LogDebug("[Email.bot] Config loaded.");

            BotProxy.RegisterCustomTrigger("New email received", "NewEmailReceived", new string[] { "Email" });

            // Extract Logo
            LogoPath = Path.GetTempPath() + "EmailBot_GLogo.png";
            Resources.GmailLogo.Save(LogoPath, ImageFormat.Png);

            // UI Management
            AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.DisableStylusAndTouchSupport", true); // This prevents a nasty bug (hanging thread on AppDomain unload)
            var handle = new AutoResetEvent(false);
            var uiThread = new System.Threading.Thread(() => {
                UiDispatch = Dispatcher.CurrentDispatcher;
                UiDispatch.Invoke(() =>
                {
                    App = new Application()
                    {
                        ShutdownMode = ShutdownMode.OnExplicitShutdown
                    };
                    handle.Set();
                    App.Run();
                });
            });
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
            handle.WaitOne();

            // Start Service
            Initialize().Wait();
        }

        public void Dispose()
        {
            UiDispatch?.Invoke(() => {
                App?.Shutdown();
                App = null;
            });
        }

        ~EmailNotifier()
        {
            StopPoller();
            EmailService = null;
            Config?.Persist();
            BotProxy?.LogDebug("[Email.bot] Module is shuting down, notifications are interrupted.");
        }

        protected async Task<bool> Initialize()
        {
            try
            {
                if (Config.GoogleCredentials == null || Config.GoogleCredentials == "")
                    await OpenConfigEditor();

                if (Config.GoogleCredentials == null || Config.GoogleCredentials == "")
                {
                    MessageBox.Show("You must have valid API credentials to use this module.", "Gmail - Missing credentials");
                    return false;
                }

                await Authenticate();
                
                return true;
            }
            catch (Exception e)
            {
                BotProxy.LogDebug("[Email.bot] Module initialization failed : " + e);
                MessageBox.Show(e.Message, "Email.bot - Init failed");
                return false;
            }
        }

        internal async Task Authenticate()
        {
            try
            {
                UserCredential credential;
                using (var stream = new MemoryStream(Encoding.Default.GetBytes(Config.GoogleCredentials)))
                {
                    credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.FromStream(stream).Secrets,
                        Scopes,
                        "user",
                        CancellationToken.None,
                        new GmailDataStore(BotProxy));
                }

                BotProxy.LogDebug("[Email.bot] Authentication successful.");

                EmailService = new GmailService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "Email plugin for Streamer.bot"
                });
            }
            catch
            {
                EmailService = null;
            }
        }

        public async void Configure()
        {
            await OpenConfigEditor();

            if (PollerTimer != null)
            {
                PollerTimer.Dispose();
                PollerTimer = null;
                Paused = false;
            }
            if(await Initialize())
                StartPoller();
        }

        protected async Task OpenConfigEditor()
        {
            await Task.Run(() => {
                UiDispatch.Invoke(() => {
                    var config = new EmailConfigWindow(this);
                    config.ShowDialog();
                    Config.Persist();
                });
            });
        }

        public void StartPoller()
        {
            if(EmailService == null || PollerTimer != null)
            {
                return;
            }

            try
            {
                if (!Paused)
                {
                    NotBefore = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    var messages = ListEmails();
                    if (messages != null)
                    {
                        foreach (var message in messages)
                        {
                            PastIds.Add(message.Id);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                BotProxy.LogDebug("[Email.bot] Unable to prefetch existing matching emails : " + e);
            }
            try
            {
                var delay = Config.PollerInterval;
                PollerTimer = new Timer(CheckEmails, null, TimeSpan.FromSeconds(delay), TimeSpan.FromSeconds(delay));
                BotProxy.LogDebug($"[Email.bot] Poller enabled. Polling every {delay} seconds.");

                new ToastContentBuilder()
                    .AddText("Gmail - Notifications enabled")
                    .AddText($"Checking for new emails every {delay} seconds.")
                    .AddAppLogoOverride(new Uri(LogoPath))
                    .SetToastDuration(ToastDuration.Short)
                    .Show();
            }
            catch(Exception e)
            {
                BotProxy.LogDebug("[Email.bot] Unable to start the poller : " + e);
            }
        }

        public void StopPoller(bool pause = false)
        {
            if(PollerTimer != null)
            {
                PollerTimer.Dispose();
                PollerTimer = null;
                Paused = pause;
                BotProxy.LogDebug("[Email.bot] Poller stopped.");

                new ToastContentBuilder()
                    .AddText("Gmail - Notifications " + (pause ? "ON HOLD" : "DISABLED"))
                    .AddText("No new notification will be received.")
                    .AddAppLogoOverride(new Uri(LogoPath))
                    .SetToastDuration(ToastDuration.Short)
                    .Show();
            }
        }

        public void FetchEmailBody(string messageId, bool asHtml = false)
        {
            try
            {
                Message email = EmailService.Users.Messages.Get("me", messageId).Execute();

                // Check Body for non-multipart content
                var bodyPart = email.Payload.Body;

                // Body can be provided as an attachment, so check that first
                if (bodyPart?.AttachmentId != null && bodyPart?.AttachmentId != string.Empty)
                    bodyPart = EmailService.Users.Messages.Attachments.Get("me", messageId, bodyPart.AttachmentId).Execute();

                // If the body is clear, decode it and we're done
                string body = bodyPart.Data?.DecodeBase64Url();
                if (body != null && body != string.Empty)
                {
                    var payloadContentTypeL = from header in email.Payload.Headers
                                where header.Name.ToLower() == "content-type" && (header.Value.StartsWith("text/html") || header.Value.StartsWith("text/plain"))
                                select (IsHtml: header.Value.StartsWith("text/html"), Body: bodyPart.Data.DecodeBase64Url());

                    // If Content-Type is "text/html" or "text/plain", we should have exactly 1 result.
                    if (payloadContentTypeL.Count() > 0)
                    {
                        var contentType = payloadContentTypeL.First();
                        if(!(contentType.IsHtml ^ asHtml))
                        {
                            BotProxy.SetArgument("emailBody", contentType.Body);
                        }
                        return;
                    }

                    // Or we'll try our luck elsewhere
                }

                if (email.Payload.Parts.Count > 0)
                {
                    var parts = from emailPart in email.Payload.Parts
                                from header in emailPart.Headers
                                where header.Name.ToLower() == "content-type" && (header.Value.StartsWith("text/html") || header.Value.StartsWith("text/plain"))
                                select (IsHtml: header.Value.StartsWith("text/html"), emailPart.Body);

                    foreach (var (IsHtml, Body) in parts)
                    {
                        bodyPart = Body;
                        if (!(IsHtml ^ asHtml))
                        {
                            // Part body can be provided as an attachment, so check that first
                            if (bodyPart.AttachmentId != null && bodyPart.AttachmentId != string.Empty)
                                bodyPart = EmailService.Users.Messages.Attachments.Get("me", messageId, bodyPart.AttachmentId).Execute();

                            if(bodyPart.Data != null && bodyPart.Data != string.Empty)
                            {
                                BotProxy.SetArgument("emailBody", bodyPart.Data.DecodeBase64Url());
                                return;
                            }
                        }
                    }
                }
            } catch (Exception e) {
                BotProxy.LogDebug("[Email.bot] Failed to fetch email body : " + e);
            }
        }

        private void CheckEmails(object state)
        {
            BotProxy.LogVerbose("[Email.bot] Checking emails ...");

            IList<Message> messages = ListEmails();
            long latestDate = NotBefore.GetValueOrDefault(0);
            if (messages != null)
            {
                foreach (Message message in messages)
                {
                    try
                    {
                        Message email = EmailService.Users.Messages.Get("me", message.Id).Execute();
                        if (!PastIds.Contains(email.Id))
                        {
                            try
                            {
                                if(email.InternalDate != null)
                                {
                                    if (email.InternalDate < NotBefore)
                                        continue;

                                    latestDate = Math.Max(email.InternalDate.GetValueOrDefault(0), latestDate);
                                }
                                BotProxy.LogDebug("[Email.bot] New email found Id=" + email.Id + " InternalDate=" + email.InternalDate);

                                BotProxy.TriggerCodeEvent("NewEmailReceived", new Dictionary<string, object>() {
                                    { "emailId", email.Id },
                                    { "emailDate", DateTimeOffset.FromUnixTimeMilliseconds(email.InternalDate.GetValueOrDefault(DateTimeOffset.Now.ToUnixTimeMilliseconds())).DateTime },
                                    { "emailFrom", (from header in email.Payload.Headers where header.Name.ToLower() == "from" select header.Value).FirstOrDefault() },
                                    { "emailSubject", (from header in email.Payload.Headers where header.Name.ToLower() == "subject" select header.Value).FirstOrDefault() }
                                });
                                PastIds.Add(email.Id);
                            }
                            catch (Exception e)
                            {
                                BotProxy.LogDebug("[Email.bot] Unable to read email data : " + e);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        BotProxy.LogDebug("[Email.bot] Unable to read emails from Gmail : " + e);
                    }
                }
                NotBefore = latestDate;
            }
        }

        private IList<Message> ListEmails()
        {
            try
            {
                UsersResource.MessagesResource.ListRequest request = EmailService.Users.Messages.List("me");
                request.LabelIds = Config.GmailLabel;
                request.Q = Config.GmailQueryFilter;
                request.MaxResults = 20;
            
                ListMessagesResponse response = request.Execute();
                return response.Messages;
            }
            catch(Exception e)
            {
                BotProxy.LogDebug("[Email.bot] An error occured while fetching emails : " + e);
            }
            return null;
        }
    }
    internal class EmailConfig
    {
        [JsonIgnore]
        private IInlineInvokeProxy BotProxy;

        public string GoogleCredentials { get; set; }
        public string GmailLabel { get; set; } = "INBOX";
        public string GmailQueryFilter { get; set; } = "is:unread";
        public int PollerInterval { get; set; } = 60;

        public static EmailConfig Load(IInlineInvokeProxy botProxy)
        {
            var data = botProxy.GetGlobalVar<string>("EmailBotConfig");
            var config = data?.ToUnprotectedObject<EmailConfig>();
            if (config == null)
                config = new EmailConfig();
            config.BotProxy = botProxy;
            return config;
        }

        public void Persist()
        {
            BotProxy.SetGlobalVar("EmailBotConfig", this.ToProtectedData());
        }
    }
}
