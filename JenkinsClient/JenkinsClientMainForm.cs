using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.DirectoryServices;
using System.Net;
using System.IO;
using System.Diagnostics;
using System.Xml.Serialization;
using System.Collections;

namespace JenkinsClient
{
    public partial class HCMainForm : Form
    {
        public HCMainForm()
        {
            InitializeComponent();
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new Settings().ShowDialog();
        }

        private void HCMainForm_Load(object sender, EventArgs e)
        {
            Properties.Settings.Default.Reload();
            AdjustControls();
        }
        private void InitializeTracing()
        {
            System.IO.FileStream myTraceLog = new System.IO.FileStream(
                AdvancedSettings.Default.LogFile,
            System.IO.FileMode.OpenOrCreate);
            // Creates the new trace listener.
            System.Diagnostics.TextWriterTraceListener myListener =
            new System.Diagnostics.TextWriterTraceListener(myTraceLog);
            System.Diagnostics.Trace.Listeners.Add(myListener);
        }

        private void ldapTimer_Tick(object sender, EventArgs e)
        {
            if (AdvancedSettings.Default.UserName == "")
            {
                string username = Environment.GetEnvironmentVariable("USERNAME");
                DirectorySearcher search = new DirectorySearcher(Properties.Settings.Default.LdapDomain);

                search.Filter = "(SAMAccountName=" + username + ")";

                SearchResult sas = search.FindOne();

                foreach (object property in sas.Properties["givenName"])
                {
                    AdvancedSettings.Default.UserName = property.ToString();
                    AdvancedSettings.Default.Save();
                    ldapTimer.Enabled = false;
                }
            }
            else
            {
                ldapTimer.Enabled = false;
            }
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new AboutHC().ShowDialog();
        }

        private Stream getHttpStream(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            return response.GetResponseStream();
        }

        private String getHttpData(string url)
        {
            string tempString = null;
            // used to build entire input
            StringBuilder sb = new StringBuilder();

            // used on each read operation
            byte[] buf = new byte[8192];

            Stream resStream = getHttpStream(url);
            int count = 0;

            do
            {
                // fill the buffer with data
                count = resStream.Read(buf, 0, buf.Length);

                // make sure we read some data
                if (count != 0)
                {
                    // translate from bytes to ASCII text
                    tempString = Encoding.ASCII.GetString(buf, 0, count);

                    // continue building the string
                    sb.Append(tempString);
                }
            }
            while (count > 0); // any more data to read?
            return sb.ToString();
        }
        private void rssLoadTimer_Tick(object sender, EventArgs e)
        {
            if (rssLoadTimer.Interval != Properties.Settings.Default.RefreshIntervalSeconds)
            {
                rssLoadTimer.Interval = Properties.Settings.Default.RefreshIntervalSeconds * 1000;
                this.Visible = false;
            }
            if (!backgroundWorkerRssLoad.IsBusy)
            {
                backgroundWorkerRssLoad.RunWorkerAsync();
            }
        }
        private NotificationData ProcessBuild(hudsonmodelFreeStyleBuild build)
        {
            Trace.TraceInformation("Checking build for user activity");
            string username = Environment.GetEnvironmentVariable("USERNAME").ToLower();
            NotificationData nd = null;
            foreach (hudsonscmChangeLogSet changeset in build.changeSet)
            {
                if (changeset.item != null)
                {
                    foreach (object change in changeset.item)
                    {
                        System.Xml.XmlNode[] changedata = (System.Xml.XmlNode[])change;

                        foreach (System.Xml.XmlNode param in changedata)
                        {
                            if (param.Name == "user")
                            {
                                string changedby = param.InnerText;
                                if (changedby.ToLower() == username)
                                {
                                    if (null == nd)
                                    {
                                        nd = new NotificationData();
                                    }
                                    nd = ProcessChangeSet(changedata, nd);
                                }
                            }
                        }
                    }
                }
            }
            return nd;
        }
        private NotificationData ProcessChangeSet(System.Xml.XmlNode[] data, NotificationData nd)
        {
            Trace.TraceInformation("Checking build for changesets");
            foreach (System.Xml.XmlNode param in data)
            {
                switch (param.Name)
                {
                    case "date":
                        nd.date.Add(param.InnerText);
                        break;
                    case "msg":
                        nd.msg.Add(param.InnerText);
                        break;
                    case "path":
                        nd.paths.Add(param.InnerText);
                        break;
                    case "revision":
                        nd.revision.Add(param.InnerText);
                        break;
                    case "user":
                        break;
                    default:
                        Trace.TraceError("This should never come");
                        break;
                }
            }
            return nd;
        }
        private void backgroundWorkerRssLoad_DoWork(object sender, DoWorkEventArgs e)
        {
            string rssUrlAll = String.Format("{0}/{1}",
                Properties.Settings.Default.JenkinsUrl, AdvancedSettings.Default.RssKeywordAll);

            string rssUrlFail = String.Format("{0}/{1}",
                    Properties.Settings.Default.JenkinsUrl, AdvancedSettings.Default.RssKeywordFailures);
            List<System.Xml.XmlNode[]> myChanges = new List<System.Xml.XmlNode[]>(); 
            try
            {
                DataSet rssDataSet = new DataSet();
                rssDataSet.ReadXml(getHttpStream(rssUrlAll));

                XmlSerializer serializer = new XmlSerializer(typeof(feed));
                feed fd = (feed)serializer.Deserialize(getHttpStream(rssUrlFail));
                List<NotificationData> notifications = new List<NotificationData>();
                if (!Properties.Settings.Default.NotifyAllErrors && Properties.Settings.Default.NotifyMyErrors)
                {
                    foreach (feedEntry fe in fd.entry)
                    {
                        Trace.TraceInformation("Processing feed " + fe.id);
                        try
                        {
                            string data = getHttpData(fe.link.href + "api/xml");
                            //Dirty fix, don't know how to correctly handle this
                            data = data.Replace("mavenBuild",
                                "freeStyleBuild").Replace("mavenModuleSetBuild",
                                "freeStyleBuild").Replace("matrixBuild",
                                "freeStyleBuild").Replace("matrixRun", "freeStyleBuild");

                            byte[] a = System.Text.Encoding.GetEncoding("iso-8859-1").GetBytes(data);
                            System.IO.MemoryStream m = new System.IO.MemoryStream(a);

                            XmlSerializer job = new XmlSerializer(typeof(hudsonmodelFreeStyleBuild));
                            hudsonmodelFreeStyleBuild build = (hudsonmodelFreeStyleBuild)job.Deserialize(m);

                            NotificationData nd = ProcessBuild(build);
                            if (null != nd)
                            {
                                nd.jobUrl = fe.link.href;
                                nd.jobDate = fe.id.Split(new char[] { ':' })[3];
                                nd.jobId = fe.id.Split(new char[] { ':' })[2];
                                notifications.Add(nd);
                            }
                        }
                        catch(Exception feedExp)
                        {
                            Trace.TraceError("Error while processing item in feed, skipping and proceeding with next", feedExp);
                        }
                    }
                }
                else if (Properties.Settings.Default.NotifyAllErrors)
                {
                    foreach (feedEntry fe in fd.entry)
                    {
                        Trace.TraceInformation("Processing feed (no user check)" + fe.id);
                        NotificationData nd = new NotificationData();
                        nd.jobUrl = fe.link.href;
                        nd.jobDate = fe.id.Split(new char[] { ':' })[3];
                        nd.jobId = fe.id.Split(new char[] { ':' })[2];
                        notifications.Add(nd);
                    }
                }

                e.Result = new object[] { rssDataSet, notifications };
            }
            catch (Exception exp)
            {
                Trace.TraceError("Error in loading RSS feeds", exp);
            }
        }

        private Hashtable pendingNotifications = new Hashtable();
        private void backgroundWorkerRssLoad_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Result != null)
            {
                object[] result = (object[])e.Result;
                dataGridView.DataSource = (DataSet)(result[0]);
                dataGridView.DataMember = "entry";
                dataGridView.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCellsExceptHeader);

                foreach(NotificationData nd in (List<NotificationData>)result[1])
                {
                    pendingNotifications[nd.jobUrl] = nd;
                }
                if (pendingNotifications.Count > 0)
                {
                    timerNotification.Enabled = true;
                }
            }
            else
            {
                toolStripStatusLabelMessages.Text = "Failed to load data from Jenkins. Clik to open log file for details";
            }
        }

        private void toolStripStatusLabelMessages_Click(object sender, EventArgs e)
        {
            Process.Start(AdvancedSettings.Default.LogFile);
        }

        private void goOfflineToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.OfflineMode = !Properties.Settings.Default.OfflineMode;
            Properties.Settings.Default.Save();
            AdjustControls();
        }
        private void AdjustControls()
        {
            if (Properties.Settings.Default.OfflineMode)
            {
                goOfflineToolStripMenuItem.Text = "&Go Online";
            }
            else
            {
                goOfflineToolStripMenuItem.Text = "&Go Offline";
            }
            if (!Properties.Settings.Default.OfflineMode)
            {
                rssLoadTimer.Enabled = true;
            }
            timerNotification.Interval = Properties.Settings.Default.RefreshIntervalSeconds * 1000;
            currentErrorLink = Properties.Settings.Default.JenkinsUrl;
        }
        private Hashtable shownNotifications = new Hashtable();
        private void timerNotification_Tick(object sender, EventArgs e)
        {
            if (pendingNotifications.Count > 0)
            {
                string firstkey = "";
                NotificationData nd = null;
                foreach(string key in pendingNotifications.Keys)
                {
                    firstkey = key;
                    nd = (NotificationData)pendingNotifications[key];
                    break;
                }
                pendingNotifications.Remove(firstkey);

                if (!shownNotifications.ContainsKey(firstkey))
                {
                    currentErrorLink = nd.jobUrl;
                    notifyIcon.ShowBalloonTip(Properties.Settings.Default.PopupTimeSeconds * 1000,
                        String.Format("{0}, a broken which is of interest to you.", AdvancedSettings.Default.UserName),
String.Format("{0} - {1}\nDid you cause the build failure? \n\nClick to get the details",
nd.jobId, nd.jobDate),
                        ToolTipIcon.Error);
                    shownNotifications[firstkey] = 0;
                }
            }
        }
        private string currentErrorLink = Properties.Settings.Default.JenkinsUrl;
        private void notifyIcon_BalloonTipClicked(object sender, EventArgs e)
        {
            Process.Start(currentErrorLink);
        }

        private void notifyIcon_DoubleClick(object sender, EventArgs e)
        {
            Visible = true;
            BringToFront();
            WindowState = FormWindowState.Normal;
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void HCMainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason != CloseReason.ApplicationExitCall)
            {
                e.Cancel = true;
            }
            Visible = false;
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Visible = true;
            WindowState = FormWindowState.Normal;
            BringToFront();
        }
    }
    class NotificationData
    {
        public string jobUrl = "";
        public string jobId = "";
        public string jobDate = "";
        public List<string> revision = new List<string>();
        public List<string> paths = new List<string>();
        public List<string> msg = new List<string>();
        public List<string> date = new List<string>();
    }
}
