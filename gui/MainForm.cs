using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;
using Doppler.Properties;
using System.Xml;
using System.Resources;
using System.Globalization;
using Doppler.languages;
using System.Collections.Specialized;
using Doppler.Controls;
using DopplerControls;
using System.Xml.Serialization;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Rss;

//[assembly: log4net.Config.XmlConfigurator(Watch = true)]
namespace Doppler
{
    //private PluginCollection Plugins = new PluginCollection();
    
    public partial class MainForm : Form
    {
        private log4net.ILog log = null;
        private SysTrayNavigator sysTrayNavigator;
        private ListViewColumnSorter FilesColumnSorter;
        private ListViewColumnSorter FeedsColumnSorter;
        private int RetrieversActive;
      //  public const string GRIDTOKEN = "B54F48B0171D4a79A89B22EC44CC3DE6";

        public MainForm(string[] startargs)
        {
            InitializeLog4Net();

            if (Settings.Default.Culture != null && Settings.Default.Culture != "")
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(Settings.Default.Culture);
            }

            InitializeComponent();

            Settings.Default.Reload();

            // check if we come from a previous version. By default on a new version CallUpgrade is set to true
            if (Settings.Default.CallUpgrade)
            {
                Settings.Default.Upgrade();
                Settings.Default.CallUpgrade = false;
            }
            
            if (Settings.Default.Feeds == null)
            {
                Settings.Default.Feeds = new FeedList();
                // assume it's a new setup, install the Default feed
                FeedItem feedItem = new FeedItem();
                feedItem.Title = "Doppler Test Channel";
                feedItem.Url = "http://www.dopplerradio.net/welcome.xml";
                feedItem.PlaylistName = "Doppler Test Channel";
                feedItem.Description = "This feed provides you with a small MP3 file to make sure that Doppler works on your machine";
                feedItem.Authenticate = false;
                feedItem.IsChecked = true;
                feedItem.Visible = true;
                Settings.Default.Feeds.Add(feedItem);
            }

            if (Settings.Default.ShowPlayer == true)
            {
                embeddedMediaPlayerToolStripMenuItem.Checked = true;
            }
            else
            {
                embeddedMediaPlayerToolStripMenuItem.Checked = false;
            }

            if (Settings.Default.History == null)
            {
                Settings.Default.History = new History();
            }

            if (Settings.Default.ViewerPane == true)
            {
                ExpandCollapseButton.Text = "»";
                splitContainer1.Panel2Collapsed = false;
            }
            else
            {
                ExpandCollapseButton.Text = "«";
                splitContainer1.Panel2Collapsed = true;
            }

            // set up the sorting stuff

            FilesColumnSorter = new ListViewColumnSorter();
            FilesListView.ListViewItemSorter = FilesColumnSorter;

            sysTrayNavigator = new SysTrayNavigator(notifyIcon1);

            downloader.FileDownloadComplete += new FileDownloadCompleteHandler(downloader_FileDownloadComplete);
            downloader.FileDownloadAborted += new FileDownloadAbortedHandler(downloader_FileDownloadAborted);
            downloader.FileDownloadError += new FileDownloadErrorHandler(downloader_FileDownloadError);
            downloader.SetInformationLabel += new SetInformationLabelHandler(downloader_SetInformationLabel);
            downloader.AllAborted += new AllAbortedHandler(downloader_AllAborted);
            // is this the first time the application is run? If so, show the options box

            if (Settings.Default.DownloadLocation == "")
            {
                SetupForm fSetup = new SetupForm();
                if (fSetup.ShowDialog() == DialogResult.Cancel)
                {
                    if(Settings.Default.DownloadLocation == "")
                    {
                        string strMessage = FormStrings.DopplerNeedsADownloadFolderAsYouDidNotSpecifyADownloadFolderDopplerWillAssumeOneForYou;
                        strMessage = strMessage + FormStrings.TheFolderPickedIs + Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)+"\\My Podcasts";
                        if(!Directory.Exists(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)+"\\My Podcasts"))
                        {
                            Directory.CreateDirectory(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)+"\\My Podcasts");
                        }
                        Settings.Default.DownloadLocation = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)+"\\My Podcasts";
                        MessageBox.Show(strMessage, FormStrings.NoDownloadFolder, MessageBoxButtons.OK, MessageBoxIcon.Information);

                    }
                }
				fSetup.Dispose();
            }

            // setup the column sorters
            FilesColumnSorter = new ListViewColumnSorter();
            FilesListView.ListViewItemSorter = FilesColumnSorter;
            FeedsColumnSorter = new ListViewColumnSorter();
            FeedsListView.ListViewItemSorter = FeedsColumnSorter;
            try
            {
                FeedsColumnSorter.SortColumn = Settings.Default.FeedsSortColumn;
            } catch {
          
                FeedsColumnSorter.SortColumn = 0;
                Settings.Default.FeedsSortColumn = 0;
            }

            try
            {
                FeedsColumnSorter.Order = Settings.Default.FeedsSortOrder;
            }
            catch
            {
                FeedsColumnSorter.Order = SortOrder.Ascending;
                Settings.Default.FeedsSortOrder = SortOrder.Ascending;
            }
            FeedsListView.Sort();
            FillFeedList();
            if (Settings.Default.StartMinimized)
            {
                this.WindowState = FormWindowState.Minimized;
                if (Settings.Default.MinimizeToSystemTray)
                {
                    this.ShowInTaskbar = false;
                }
            }

            // first time! initialize the OPML directories
            if (Settings.Default.DirectoryList == null || Settings.Default.DirectoryList.Count == 0)
            {
                Settings.Default.DirectoryList = new DirectoryList();
                Settings.Default.DirectoryList.Add(new DirectoryItem("Podcast Alley Top 10", "http://www.podcastalley.com/PodcastAlleyTop10.opml"));
                Settings.Default.DirectoryList.Add(new DirectoryItem("Podcast Alley Top 50", "http://www.podcastalley.com/PodcastAlleyTop50.opml"));
                Settings.Default.DirectoryList.Add(new DirectoryItem("Podcast Alley Newest 10", "http://www.podcastalley.com/PodcastAlley10Newest.opml"));
            }

            // see if we need to check for updates
            if (Settings.Default.UpdateCheck)
            {
                if (DateTime.Now.ToShortDateString() != Settings.Default.LastUpdateCheck.ToShortDateString())
                {
                    UpdateCheckTimer.Enabled = true;
                }
            }
        }

        private void InitializeLog4Net()
        {
            log4net.Appender.RollingFileAppender rolApp = new log4net.Appender.RollingFileAppender();
            rolApp.AppendToFile = true;
            rolApp.RollingStyle = log4net.Appender.RollingFileAppender.RollingMode.Date;
            rolApp.ImmediateFlush = true;
            rolApp.DatePattern = "yyyyMMdd";
            rolApp.File = Utils.GetAppDataFolder() + "\\DopplerLog.xml";
            rolApp.LockingModel = new log4net.Appender.FileAppender.MinimalLock();
            rolApp.MaximumFileSize = "1Mb";
            rolApp.MaxSizeRollBackups = 1;
            log4net.Layout.XmlLayout xmlLayout = new log4net.Layout.XmlLayout();
            xmlLayout.Prefix = "log4net";
            rolApp.Layout = new log4net.Layout.XmlLayout();
            rolApp.Name = "RollingFileAppender";
            rolApp.ActivateOptions();
            log4net.Config.BasicConfigurator.Configure(rolApp);
            log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        }

        void downloader_AllAborted()
        {
            sysTrayNavigator.Threads = 0;
        }

        void downloader_SetInformationLabel(string message, bool visible)
        {
            SetControlProperty(InformationLabel, "Text", message);
            SetControlProperty(InformationLabel, "Visible", visible);
        }

        void downloader_FileDownloadError(DopplerControls.DownloadItem item, string Error, Exception ex)
        {
            notifyIcon1.ShowBalloonTip(1000, FormStrings.Error, string.Format(FormStrings.Errorwhiledownloadingfrom,item.FeedTitle) + "\n" + Error, ToolTipIcon.Error);
            DecreaseNumberOfDownloadThreads();
            this.Invoke(new MethodInvoker(FillViewerPane));
        }

        private void DecreaseNumberOfDownloadThreads()
        {
            sysTrayNavigator.Threads--;
            if (sysTrayNavigator.Threads == 0)
            {
                SetControlProperty(InformationLabel, "Visible", false);
            }
        }       

        private void FillFeedList()
        {
            FeedsListView.BeginUpdate();
            FeedsListView.Items.Clear();
            // build up the image list
            ImageList imgList = new ImageList();
            imgList.ImageSize = new Size(16,16);
            imgList.ColorDepth = ColorDepth.Depth16Bit;           
            imgList.Images.Add("feed",Resources.feed_icon_10x10);

            if (Settings.Default.ShowFavIcons)
            {
                DirectoryInfo dirInfo = new DirectoryInfo(Utils.DataFolder);
                foreach (FileInfo fileIcon in dirInfo.GetFiles("*.ico"))
                {
                    try
                    {
                        Icon icoMain = new Icon(fileIcon.FullName);
                        Icon ico = (Icon)icoMain.Clone();
                        imgList.Images.Add(fileIcon.Name, ico);
                    }
                    catch { }
                }
            }
            FeedsListView.SmallImageList = imgList; 
            ArrayList arrItems = new ArrayList();
            Cursor.Current = Cursors.WaitCursor;
            ListView.ListViewItemCollection col = new ListView.ListViewItemCollection(FeedsListView);
            if (Settings.Default.Feeds != null)
            {
                if (Settings.Default.ShowCategories != null && Settings.Default.ShowCategories.Count > 0)
                {
                    Settings.Default.ShowCategories.Sort();
                    foreach (string strCategory in Settings.Default.ShowCategories)
                    {
                        ListViewGroup lvg = FeedsListView.Groups.Add(strCategory, strCategory);
                    }

                    foreach (FeedItem feedItem in Settings.Default.Feeds) 
                    {
                        if (Settings.Default.ShowCategories.Contains(feedItem.Category))
                        {
                            bool boolAdd = true;
                            if (Settings.Default.ShowSelected)
                            {
                                boolAdd = feedItem.IsChecked;
                            }
                            if (boolAdd)
                            {
                                ListViewItem lvi = GetListViewItem(FeedsListView.Groups[feedItem.Category], feedItem);
                                lvi.ToolTipText = feedItem.Description ;
                                if (Settings.Default.ShowFavIcons)
                                {
                                    if (imgList.Images.ContainsKey(feedItem.FeedHashCode + ".ico"))
                                    {
                                        lvi.ImageKey = feedItem.FeedHashCode + ".ico";
                                    }
                                    else
                                    {
                                        lvi.ImageKey = "feed";
                                    }
                                }
                                else
                                {
                                    lvi.ImageKey = "feed";
                                }
                                arrItems.Add(lvi);
                            }
                        }
                    }
                    ListViewItem[] lviItems = new ListViewItem[arrItems.Count];
                    arrItems.CopyTo(lviItems);
                    
                    FeedsListView.Items.AddRange(lviItems);
                }
                else
                {
                    foreach (string strCategory in Settings.Default.Feeds.Categories)
                    {
                        ListViewGroup lvg = FeedsListView.Groups.Add(strCategory, strCategory);
                    }

                    foreach (FeedItem feedItem in Settings.Default.Feeds)
                    {
                        bool boolAdd = true;
                        if (Settings.Default.ShowSelected)
                        {
                            boolAdd = feedItem.IsChecked;
                        }
                        if (boolAdd)
                        {
                            ListViewItem lvi = GetListViewItem(FeedsListView.Groups[feedItem.Category], feedItem);
                            lvi.ToolTipText = feedItem.Description;
                            if (imgList.Images.ContainsKey(feedItem.FeedHashCode + ".ico"))
                            {
                                lvi.ImageKey = feedItem.FeedHashCode + ".ico";
                            }
                            else
                            {
                                lvi.ImageKey = "feed";
                            }
                            arrItems.Add(lvi);
                        }

                    }
                    ListViewItem[] lviItems = new ListViewItem[arrItems.Count];
                    arrItems.CopyTo(lviItems);

                    FeedsListView.Items.AddRange(lviItems);
                }

            }
            foreach (ColumnHeader ch in FeedsListView.Columns)
            {
                ch.Width = -2;
            }

            RefreshInfoLabel();

            Cursor.Current = Cursors.Default;
            FeedsListView.EndUpdate();
            FeedsListView.Update();
        }

        private void RefreshInfoLabel()
        {
            int totalpodcastCount = 0;
            // count the number of not downloaded podcasts
            for(int q=0;q<Settings.Default.Feeds.Count;q++)
            {
                FeedItem feedItem = Settings.Default.Feeds[q];
                Rss.RssFeed rssFeed = Utils.DeserializeFeed(feedItem);
                if (rssFeed != null)
                {
                    int totalFeedPodcasts = 0;
                    if (rssFeed.Channels[0] != null)
                    {
                        foreach (Rss.RssItem rssItem in rssFeed.Channels[0].Items)
                        {
                            if (rssItem.Enclosure != null)
                            {
                                totalFeedPodcasts++;
                            }
                        }
                        // count the number of downloaded podcasts for this feed
                        ArrayList downloadedPodcasts = Settings.Default.History.GetItemsByFeedGUID(feedItem.GUID);
                        if (downloadedPodcasts != null)
                        {
                            totalpodcastCount += totalFeedPodcasts - downloadedPodcasts.Count;
                        }
                        else
                        {
                            totalFeedPodcasts += totalFeedPodcasts;
                        }
                    }
                }
            }
            if (HeaderLabel.InvokeRequired)
            {
                if (totalpodcastCount > 0)
                {
                    SetControlProperty(HeaderLabel, "Text", String.Format(FormStrings.FeedsSubscribedNewPodcastsInFeeds, Settings.Default.Feeds.Count, totalpodcastCount));
                }
                else
                {
                    SetControlProperty(HeaderLabel, "Text", String.Format(FormStrings.FeedsSubscribed, Settings.Default.Feeds.Count));
                }
            }
            else
            {
                if (totalpodcastCount > 0)
                {
                    HeaderLabel.Text = String.Format(FormStrings.FeedsSubscribedNewPodcastsInFeeds, Settings.Default.Feeds.Count, totalpodcastCount);
                }
                else
                {
                    HeaderLabel.Text = String.Format(FormStrings.FeedsSubscribed, Settings.Default.Feeds.Count);
                }

            }
       }

        private ListViewItem GetListViewItem(ListViewGroup lvg, FeedItem feedItem)
        {
            ListViewItem lvi = new ListViewItem();
            lvi.Tag = feedItem;
            lvi.Checked = feedItem.IsChecked;
            lvi.Text = feedItem.Title;

            lvi.Name = feedItem.GUID;
            lvi.Group = lvg;
            if (feedItem.Pubdate != null)
            {
                lvi.SubItems.Add(feedItem.Pubdate);
            }
            else
            {
                lvi.SubItems.Add("-"+FormStrings.newfeed+"-");
            }
            // add an empty status;
            lvi.SubItems.Add("");
            return lvi;
        }
        private void menuProperties_Click(object sender, EventArgs e)
        {
            if (FeedsListView.SelectedItems.Count > 0)
            {
                EditFeed(false);
            }
        }

        private void EditFeed(FeedItem feedItem)
        {
            FeedForm feedForm = new FeedForm(feedItem);
            if (feedForm.ShowDialog() == DialogResult.OK)
            {
                FillFeedList();
            }
            feedForm.Dispose();
            // find the item and select it in the list
            foreach (ListViewItem lvi2 in FeedsListView.Items)
            {
                if ((FeedItem)lvi2.Tag == feedItem)
                {
                    lvi2.Selected = true;
                    FeedsListView.EnsureVisible(lvi2.Index);
                    break;
                }
            }
        }

        private void EditFeed(bool inFront)
        {
            ListViewItem lvi = FeedsListView.SelectedItems[0];
            FeedItem feedItem = (FeedItem)lvi.Tag;
            FeedForm fFeed = new FeedForm(feedItem,false);
            //fFeed.Item = feedItem;

            fFeed.TopMost = inFront;

            if (fFeed.ShowDialog() == DialogResult.OK)
            {
                FillFeedList();
            }
			fFeed.Dispose();
            // find the item and select it in the list
            foreach (ListViewItem lvi2 in FeedsListView.Items)
            {
                if ((FeedItem)lvi2.Tag == feedItem)
                {
                    lvi2.Selected = true;
                    FeedsListView.EnsureVisible(lvi2.Index);
                    break;
                }
            }

        }

        private void TopToolStripPanel_Click(object sender, EventArgs e)
        {

        }

        private void propertiesProperties_Click(object sender, EventArgs e)
        {
            EditFeed(false);
        }

		private void SaveSettingsOnExit()
		{
			Settings.Default.WindowLocation = this.Location;
			Settings.Default.WindowSize = this.Size;

			try
			{
				Settings.Default.Splitter1 = splitContainer1.SplitterDistance;
				Settings.Default.Splitter2 = splitContainer2.SplitterDistance;
				Settings.Default.Splitter3 = splitContainerPosts.SplitterDistance;
			}
			catch (Exception)
			{ }
			Settings.Default.Save();
		}

		private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
			SaveSettingsOnExit();
            Application.Exit();
        }

        void retriever_RetrieveCompleteCallback(FeedItem feedItem, bool boolSuccess)
        {
            RefreshInfoLabel();
            RetrieversActive--;
        }

        private delegate bool AddFileToDownloader(DownloadItem item);

        void retriever_FileFound(FeedItem feedItem, string URL, long DownloadSize, string strFilename, Rss.RssItem rssItem, bool byteRanging)
        {
            //Settings set = new Settings();
            DownloadItem item = new DownloadItem();

  
            item.Path = strFilename;

            item.GUID = System.Guid.NewGuid().ToString();

            item.Url = URL;
            item.IsTorrent = false;

            item.PostTitle = rssItem.Title;
            item.RssItemHashcode = rssItem.GetHashCode();
            item.DownloadSize = DownloadSize;
            item.MaxBytes = System.Convert.ToInt64(feedItem.MaxMb * 1024);
            item.Authenticate = feedItem.Authenticate;
            item.Username = feedItem.Username;
            
			if (feedItem.Authenticate)
			{
				item.Password = EncDec.Decrypt(feedItem.Password, feedItem.Username);
			}
			else
			{
				item.Password = feedItem.Password;
			}

            item.Filename = strFilename;
            item.FeedUrl = feedItem.Url;
            item.FeedTitle = feedItem.Title;
            item.FeedPlaylist = feedItem.PlaylistName;
            item.FeedGUID = feedItem.GUID;
            item.TimeOut = Settings.Default.TimeOut;
            item.TagTitle = feedItem.TagTitle;
            item.TagGenre = feedItem.TagGenre;
            item.TagArtist = feedItem.TagArtist;
            item.TagAlbum = feedItem.TagAlbum;
            item.TagTrackCounter = feedItem.TrackCounter;
            item.TagUseTrackCounter = feedItem.UseTrackCounter;
            item.UseSpaceSavers = feedItem.UseSpaceSavers;
            item.spacesaver_ageDays = feedItem.Spacesaver_Days;
            item.spacesaver_maxFiles = feedItem.Spacesaver_Files;
            item.spacesaver_maxMb = feedItem.spacesaver_maxMb;
            item.ByteRanging = byteRanging;
            if (((bool)downloader.Invoke(new AddFileToDownloader(downloader.AddFile), new object[] { item })) == true)
            {
                sysTrayNavigator.Threads++;
            }
        }

        void retriever_SetFeedStatus(ListViewItem lvi, int intStatus, string strPubDate, string strStatus)
        {
            switch (intStatus)
            {
                case 1:
                    {
                        // starting retrieve, updating status
                      
                        string currentStatus = (string)GetSubControlProperty(lvi.SubItems[2], "Text");
                        if (currentStatus != strStatus)
                        {
                            SetControlProperty(lvi, "Font", new Font("Tahoma", 8.25F, FontStyle.Bold));
                            SetControlProperty(lvi, "ForeColor", Color.Black);
                            SetSubControlProperty(lvi.SubItems[1], "Text", strPubDate);
                            SetSubControlProperty(lvi.SubItems[2], "Text", strStatus);
                        }
                       
                        break;
                    }
                case 2:
                    {
                        // end retrieve with errors
                        SetControlProperty(lvi, "Font", new Font("Tahoma", 8.25F, FontStyle.Regular));
                        SetSubControlProperty(lvi.SubItems[1], "Tag", null);
                       
                        SetControlProperty(lvi, "ForeColor", Color.Red);

                        string currentStatus = (string) GetSubControlProperty(lvi.SubItems[2],"Text");
                        if (currentStatus != strStatus)
                        {
                            SetSubControlProperty(lvi.SubItems[2], "Text", strStatus);
                        }
                        SetSubControlProperty(lvi.SubItems[1], "Text", strPubDate);

                        break;
                    }
                case 3:
                    {
                        // end retrieve no errors
                        SetControlProperty(lvi, "Font", new Font("Tahoma", 8.25F, FontStyle.Regular));
                        SetSubControlProperty(lvi.SubItems[1], "Tag", null);
                        SetSubControlProperty(lvi.SubItems[1], "Text", strPubDate);
                       

                        SetControlProperty(lvi, "ForeColor", Color.Black);
                        SetSubControlProperty(lvi.SubItems[2], "Text", "");
                      
                        break;
                    }
            }
        }


        private void retrievethisfeedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ListViewItem lvi = FeedsListView.SelectedItems[0];
            FeedItem feedItem = (FeedItem)lvi.Tag;

            int intMaxThreads = Settings.Default.MaxThreads;

            ThreadPool.SetMaxThreads(intMaxThreads, intMaxThreads);
            Retriever myRetriever = new Retriever(feedItem, lvi);
            myRetriever.SetFeedStatus += new RetrieverSetFeedStatusHandler(retriever_SetFeedStatus);
            myRetriever.FileFound += new FileFoundHandler(retriever_FileFound);
            myRetriever.RetrieveCompleteCallback += new RetrieveCompleteHandler(retriever_RetrieveCompleteCallback);
            ThreadPool.QueueUserWorkItem(new WaitCallback(myRetriever.ParseItem));
        }

        delegate void SetValueDelegate(Object obj, Object val, Object[] index);

        public void SetControlProperty(Control ctrl, String propName, Object val)
        {
            PropertyInfo propInfo = ctrl.GetType().GetProperty(propName);
            Delegate dgtSetValue = new SetValueDelegate(propInfo.SetValue);

            ctrl.Invoke(dgtSetValue, new Object[3] { ctrl, val, /*index*/null });
        }

        public void SetControlProperty(ListViewItem ctrl, String propName, Object val)
        {
            PropertyInfo propInfo = ctrl.GetType().GetProperty(propName);
            Delegate dgtSetValue = new SetValueDelegate(propInfo.SetValue);

            FeedsListView.Invoke(dgtSetValue, new Object[3] { ctrl, val, /*index*/null });
        }

        delegate Object GetListViewItemDelegate(Object obj, Object[] index);


        public ListViewItem GetListViewItem(ListView list, string strGUID)
        {


            PropertyInfo propInfo = list.GetType().GetProperty("Items");

            ListView.ListViewItemCollection lvis;

            Delegate dgtGetLvi = new GetListViewItemDelegate(propInfo.GetValue);

            lvis = (ListView.ListViewItemCollection)FeedsListView.Invoke(dgtGetLvi, new Object[] { list });
            return lvis[strGUID];
        }

        delegate object GetValueDelegate(Object obj, Object[] target);

        public object GetSubControlProperty(ListViewItem.ListViewSubItem ctrl, String propName)
        {
            PropertyInfo propInfo = ctrl.GetType().GetProperty(propName);
            Delegate dgtGetValue = new GetValueDelegate(propInfo.GetValue);

            return FeedsListView.Invoke(dgtGetValue, new Object[2] { ctrl, /*index*/null });
        }

        public void SetSubControlProperty(ListViewItem.ListViewSubItem ctrl, String propName, Object val)
        {
            PropertyInfo propInfo = ctrl.GetType().GetProperty(propName);
            Delegate dgtSetValue = new SetValueDelegate(propInfo.SetValue);

            FeedsListView.Invoke(dgtSetValue, new Object[3] { ctrl, val, /*index*/null });
        }

        private void FeedsListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            FillViewerPane();
        }

        private void FillViewerPane()
        {
            if (FeedsListView.SelectedItems.Count > 0)
            {
                ListViewItem lvi = FeedsListView.SelectedItems[0];
                FeedItem feedItem = (FeedItem)lvi.Tag;
                if (File.Exists(Path.Combine(Utils.DataFolder, feedItem.FeedHashCode + ".jpg")))
                {
                    FeedPictureBox.Image = new Bitmap(Path.Combine(Utils.DataFolder, feedItem.FeedHashCode + ".jpg"));
                    FeedPictureBox.Visible = true;
                    InformationLabel.Left = 58;
                }
                else
                {
                    FeedPictureBox.Image = null;
                    FeedPictureBox.Visible = false;
                    InformationLabel.Left = 7;
                }
                splitContainer1.Panel2.Tag = feedItem;
                if (splitContainer1.Panel2Collapsed == false)
                {
                    if (tabPostFiles.SelectedTab == tabPosts)
                    {
                        // show the posts
                        ShowPosts();
                    }
                    else
                    {
                        ShowFiles();
                    }
                }
            }
        }

        private void ShowFiles()
        {
            ListViewItem lvi;
            string strLocation = null;
           
            FeedItem feedItem = (FeedItem)splitContainer1.Panel2.Tag;
            if (feedItem != null)
            {
                FilesListView.BeginUpdate();
                FilesListView.Items.Clear();
                string strDir = Utils.GetValidFolderPath(feedItem.Title);

                if (feedItem.OverrideDownloadsFolder)
                {
                    strLocation = feedItem.DownloadsFolder;
                }
                else
                {
                    strLocation = Path.Combine(Settings.Default.DownloadLocation, strDir);
                }
                FileInfo[] downloadingFiles = downloader.GetDownloadingFiles();
                if (Directory.Exists(strLocation))
                {
                    fileSystemWatcher1.Path = strLocation;
                    fileSystemWatcher1.EnableRaisingEvents = true;
                    DirectoryInfo dirInfo = new DirectoryInfo(strLocation);
                    FileInfo[] files = dirInfo.GetFiles();
                    foreach (FileInfo fileInfo in files)
                    {                       
                        if (fileInfo.Extension.ToLower() == ".incomplete")
                        {
                            lvi = new ListViewItem(fileInfo.Name.Substring(0, fileInfo.Name.IndexOf(fileInfo.Extension)));
                            lvi.ForeColor = Color.DarkBlue;
                        }
                        else
                        {
                            lvi = new ListViewItem(fileInfo.Name);
                        }
                        lvi.SubItems.Add(fileInfo.Length.ToString());
                        
                        lvi.SubItems.Add(fileInfo.LastWriteTime.ToString());
                        lvi.Tag = fileInfo;
                        if (downloadingFiles != null)
                        {
                            Font iFont = new Font(lvi.Font, FontStyle.Italic);
                            foreach (FileInfo downloadingFile in downloadingFiles)
                            {
                                if (downloadingFile.FullName+".incomplete" == fileInfo.FullName )
                                {
                                    lvi.ForeColor = Color.DarkGray;
                                }
                            }
                        }

                        FilesListView.Items.Add(lvi);
                    }

                }
                else
                {
                    lvi = new ListViewItem(FormStrings.Firstretrievesomefiles);
                    lvi.Font = new Font("Tahoma", 8.25F, FontStyle.Italic);
                    FilesListView.Items.Add(lvi);
                }
                foreach (ColumnHeader ch in FilesListView.Columns)
                {
                    ch.Width = -2;
                }
                FilesListView.EndUpdate();
            }
        }

        private void ShowPosts()
        {
            FeedItem feedItem = (FeedItem)splitContainer1.Panel2.Tag;
            if (feedItem != null)
            {
                PostsListView.Items.Clear();
                ListViewItem lvi = new ListViewItem(FormStrings.RetrievingPleaseWait);
                lvi.BackColor = Color.Black;
                lvi.ForeColor = Color.White;
                PostsListView.Items.Add(lvi);
                if (feedItem != null)
                {
                    if (feedItem.Url.ToLower().StartsWith("http://") || feedItem.Url.ToLower().StartsWith("https://"))
                    {
                        tabPosts.Tag = feedItem;
                        //RssReader rssReader = new RssReader(feedItem,false);
                        Hashtable hashArguments = new Hashtable();
                        hashArguments.Add("FeedItem", feedItem);
                        hashArguments.Add("Force", false);

                        if (backgroundRssRetriever.IsBusy)
                        {
                            backgroundRssRetriever.CancelAsync();
                        }
                        while (backgroundRssRetriever.IsBusy) { Application.DoEvents(); }
                        backgroundRssRetriever.RunWorkerAsync(hashArguments);

                    }
                }
            }
        }

        private void optionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowOptions(false);
        }

        private void ShowOptions(bool centered)
        {
            OptionsForm fOptions = new OptionsForm();
            if (centered)
            {
                fOptions.StartPosition = FormStartPosition.CenterScreen;
            }
          
            Cursor.Current = Cursors.WaitCursor;
            Settings.Default.Save();
            Cursor.Current = Cursors.Default;
            bool ShowFavIcons = Settings.Default.ShowFavIcons;
           
            if (fOptions.ShowDialog() == DialogResult.Cancel)
            {
                Cursor.Current = Cursors.WaitCursor;
                Settings.Default.Reload();
                Cursor.Current = Cursors.Default;
            } else {
                Cursor.Current = Cursors.WaitCursor;
                Settings.Default.Save();
                Cursor.Current = Cursors.Default;
            }
            
            if (Settings.Default.ShowFavIcons != ShowFavIcons)
            {
                FillFeedList();
            }
            FeedCheckTimer.Enabled = Settings.Default.CheckAutomatic;
            fOptions.Dispose();
        }

        private void openDownloadfolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string strLocation = null;
            if (FeedsListView.SelectedItems.Count > 0)
            {
                Settings set = new Settings();
                // opens the downloadfolder in explorer
                FeedItem feedItem = (FeedItem)FeedsListView.SelectedItems[0].Tag;

                string strURL = feedItem.Url;
                // \ / : * ? " < > |
                string strDir = Utils.GetValidFolderPath(feedItem.Title);

                if (feedItem.OverrideDownloadsFolder)
                {
                    strLocation = feedItem.DownloadsFolder;
                }
                else
                {
                    strLocation = set.DownloadLocation + "\\" + strDir;
                }
                if (Directory.Exists(strLocation))
                {
                    System.Diagnostics.Process.Start(strLocation);
                }
                else
                {
                    MessageBox.Show(FormStrings.NofolderavailableRetrievethisfeedfirst, FormStrings.Nofolderavailable, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void downloader_FileDownloadComplete(DopplerControls.DownloadItem item, ListViewItem lvi)
        {
            if (Settings.Default.History[item.RssItemHashcode.ToString()] == null)
            {
                HistoryItem hi = new HistoryItem();
                hi.FeedUrl = item.FeedUrl;
                hi.FeedGUID = item.FeedGUID;
                
                hi.Hashcode = item.RssItemHashcode;
                
                hi.Title = item.PostTitle;
                hi.ItemDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                FileInfo fileInfo = new FileInfo(item.Filename);
                hi.FileName = fileInfo.Name;

                Settings.Default.History.Add(hi);
                Settings.Default.Save();
            }
            notifyIcon1.BalloonTipTitle = FormStrings.Newdownload;
            notifyIcon1.BalloonTipText = String.Format(FormStrings.Downloadedanewpodcastfrom, item.FeedTitle);
            notifyIcon1.ShowBalloonTip(1000);
            DecreaseNumberOfDownloadThreads();
            this.Invoke(new MethodInvoker(FillViewerPane));
        }

        private void downloader_FileDownloadAborted(DownloadItem item, ListViewItem lvi)
        {
            if (Settings.Default.LogLevel > 1) log.Info("File download aborted: " + item.Filename);
            notifyIcon1.ShowBalloonTip(4000, FormStrings.Downloadaborted, String.Format(FormStrings.Downloadfromaborted, item.FeedTitle), ToolTipIcon.Warning);
            DecreaseNumberOfDownloadThreads();
            this.Invoke(new MethodInvoker(FillViewerPane));
        }

        private void markallToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FeedsListView.BeginUpdate();
            for (int q = 0; q < FeedsListView.Items.Count; q++)
            {
                FeedsListView.Items[q].Checked = true;
            }
            FeedsListView.EndUpdate();
        }

        private void unmarkallToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FeedsListView.BeginUpdate();
            for (int q = 0; q < FeedsListView.Items.Count; q++)
            {
                FeedsListView.Items[q].Checked = false;
            }
            FeedsListView.EndUpdate();
        }

        private void pictureAdd_Click(object sender, EventArgs e)
        {
            FeedItem fi = new FeedItem();
            if (Settings.Default.DefaultItem != null)
            {
                fi.CleanRating = Settings.Default.DefaultItem.CleanRating;
                fi.PlaylistName = Settings.Default.DefaultItem.PlaylistName;
                fi.Pubdate = "-"+FormStrings.newfeed+"-";
                fi.MaxMb = Settings.Default.DefaultItem.MaxMb;
                fi.RetrieveNumberOfFiles = Settings.Default.DefaultItem.RetrieveNumberOfFiles;
                fi.Spacesaver_Days = Settings.Default.DefaultItem.Spacesaver_Days;
                fi.Spacesaver_Files = Settings.Default.DefaultItem.Spacesaver_Files;
                fi.Spacesaver_Files = Settings.Default.DefaultItem.Spacesaver_Files;
                fi.TagAlbum = Settings.Default.DefaultItem.TagAlbum;
                fi.TagGenre = Settings.Default.DefaultItem.TagGenre;
                fi.TagArtist = Settings.Default.DefaultItem.TagArtist;
                fi.Textfilter = Settings.Default.DefaultItem.Textfilter;
                fi.UseSpaceSavers = Settings.Default.DefaultItem.UseSpaceSavers;
                fi.RemoveFromPlaylist = Settings.Default.DefaultItem.RemoveFromPlaylist;
            }
            FeedForm fFeed = new FeedForm(fi);
            if (fFeed.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                FillFeedList();
            }
			fFeed.Dispose();
        }

        private void AddFeed(string Url, bool TopMost, bool centered)
        {
           
            FeedItem fi = new FeedItem();
            if (Url != null)
            {
                fi.Url = Url;
            }
            else
            {
                string strClipboard = Clipboard.GetText();
                if (strClipboard.ToLower().StartsWith("http"))
                {
                    fi.Url = strClipboard;
                }
            }
            if (Settings.Default.DefaultItem != null)
            {
                fi.CleanRating = Settings.Default.DefaultItem.CleanRating;
                fi.PlaylistName = Settings.Default.DefaultItem.PlaylistName;
                fi.Pubdate = "-" + FormStrings.newfeed + "-";
                //fi.addToiTunes = Settings.Default.DefaultItem.addToiTunes;
                //fi.addToWMP = Settings.Default.DefaultItem.addToWMP;
                fi.MaxMb = Settings.Default.DefaultItem.MaxMb;
                fi.RetrieveNumberOfFiles = Settings.Default.DefaultItem.RetrieveNumberOfFiles;
                fi.Spacesaver_Days = Settings.Default.DefaultItem.Spacesaver_Days;
                fi.Spacesaver_Files = Settings.Default.DefaultItem.Spacesaver_Files;
                fi.Spacesaver_Files = Settings.Default.DefaultItem.Spacesaver_Files;
                fi.TagAlbum = Settings.Default.DefaultItem.TagAlbum;
                fi.TagGenre = Settings.Default.DefaultItem.TagGenre;
                fi.TagArtist = Settings.Default.DefaultItem.TagArtist;
                fi.Textfilter = Settings.Default.DefaultItem.Textfilter;
                fi.UseSpaceSavers = Settings.Default.DefaultItem.UseSpaceSavers;
                fi.RemoveFromPlaylist = Settings.Default.DefaultItem.RemoveFromPlaylist;
                fi.RemovePlayed = Settings.Default.DefaultItem.RemovePlayed;
            }
            FeedForm fFeed = new FeedForm(fi, true);
            fFeed.TopMost = TopMost;
            if (centered)
            {
                fFeed.StartPosition = FormStartPosition.CenterScreen;
            }
            if (fFeed.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                FillFeedList();
            }
            fFeed.Dispose();
            Settings.Default.Save();
            RefreshInfoLabel();
            

        }

        private void AddFeed(bool centered)
        {
            AddFeed(null,false, centered);
            RefreshInfoLabel();
        }

        private void opendownloadhistoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FeedItem feedItem = null;
            feedItem = (FeedItem)FeedsListView.SelectedItems[0].Tag;
            if (feedItem != null)
            { 
                HistoryForm fHistory = new HistoryForm(feedItem);           
                fHistory.ShowDialog();
				fHistory.Dispose();
            }
            RefreshInfoLabel();
            FillViewerPane();
        }

        private void toolStripDropDownButton5_Click(object sender, EventArgs e)
        {

        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutForm fAbout = new AboutForm();
            fAbout.ShowDialog();
			fAbout.Dispose();
        }

        private void RetrieveButton_Click(object sender, EventArgs e)
        {
            RetrieveFeeds();
        }

        public void RetrieveFeeds()
        {
            RetrieverPool pool = new RetrieverPool(Settings.Default.MaxThreads);
            if (!Settings.Default.CheckAutomatic)
            {
                Settings.Default.LastRetrieve = DateTime.Now;
            }

            for (int q = 0; q < FeedsListView.Items.Count; q++)
            {
                if (FeedsListView.Items[q].Checked)
                {
                    FeedItem feedItem = (FeedItem)FeedsListView.Items[q].Tag;
                    Retriever retriever = new Retriever(feedItem, FeedsListView.Items[q]);
                    retriever.SetFeedStatus += new RetrieverSetFeedStatusHandler(retriever_SetFeedStatus);
                    retriever.FileFound += new FileFoundHandler(retriever_FileFound);
                    retriever.RetrieveCompleteCallback += new RetrieveCompleteHandler(retriever_RetrieveCompleteCallback);
                    pool.QueueUserWorkItem(new WaitCallback(retriever.ParseItem), null);
                    RetrieversActive++;
                }
            }
        }

        /// <summary>
        /// Runs a scheduled retrieve.
        /// </summary>
        private void RetrieveScheduled()
        {
            RetrieverPool pool = new RetrieverPool(Settings.Default.MaxThreads);
            for (int q = 0; q < FeedsListView.Items.Count; q++)
            {

                if (FeedsListView.Items[q].Checked)
                {
                    FeedItem feedItem = (FeedItem)FeedsListView.Items[q].Tag;
                    Retriever myRetriever = new Retriever(feedItem, FeedsListView.Items[q]);
                    //myRetriever.SetFeedStatus += new RetrieverSetFeedStatusHandler(myRetriever_SetFeedStatus);
                    myRetriever.FileFound += new FileFoundHandler(retriever_FileFound);
                    myRetriever.RetrieveCompleteCallback += new RetrieveCompleteHandler(retriever_RetrieveCompleteCallback);
                    pool.QueueUserWorkItem(new WaitCallback(myRetriever.ParseItem),null);
                }
            }
        }

        /// <summary>
        /// Handles the Click event of the buttonAdd control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.EventArgs"/> instance containing the event data.</param>
        private void AddButton_Click(object sender, EventArgs e)
        {
            AddFeedWithWizard(false,false);
        }

        /// <summary>
        /// Handles the Click event of the addFeedToolStripMenuItem control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.EventArgs"/> instance containing the event data.</param>
        private void addFeedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AddFeed(false);
        }


        private void AddFeedWithWizard(bool TopMost, bool Center)
        {
            NewFeedWizardForm wizard = new NewFeedWizardForm();
            wizard.TopMost = TopMost;
            if (Center)
            {
                wizard.StartPosition = FormStartPosition.CenterScreen;
            }

            if (wizard.ShowDialog() == DialogResult.OK && wizard.FeedItem != null)
            {
                FeedItem feedItem = wizard.FeedItem;
                FillFeedList();
                if (wizard.ShowAdvancedDialog)
                {
                    EditFeed(feedItem);
                }
                RefreshInfoLabel();
                foreach (ListViewItem lvi2 in FeedsListView.Items)
                {
                    if ((FeedItem)lvi2.Tag == feedItem)
                    {
                        lvi2.Selected = true;
                        FeedsListView.EnsureVisible(lvi2.Index);
                        break;
                    }
                }
            }
            wizard.Dispose();

        }

        /// <summary>
        /// Handles the Opening event of the contextMenuFeeds control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.ComponentModel.CancelEventArgs"/> instance containing the event data.</param>
        private void FeedsContextMenu_Opening(object sender, CancelEventArgs e)
        {
            if (FeedsListView.SelectedItems.Count == 0 || FeedsListView.Items.Count == 0)
            {
                e.Cancel = true;
            }
        }

        /// <summary>
        /// Handles the FormClosed event of the frmMain control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.Windows.Forms.FormClosedEventArgs"/> instance containing the event data.</param>
        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            Settings.Default.Save();
        }

        /// <summary>
        /// Draws a custom item in the listPosts listview. 2 lines, top line with title, second line with date
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PostsListView_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index != -1)
            {
                if (PostsListView.Items[e.Index].GetType().Name == "RssItem")
                {
                    Rss.RssItem rssItem = (Rss.RssItem)PostsListView.Items[e.Index];

                    if (rssItem != null)
                    {
                        if ((e.State & DrawItemState.Selected) == DrawItemState.Selected)
                        {
                            Rectangle recProgressTop = new Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width, e.Bounds.Height / 2);
                            LinearGradientBrush brushTop = new LinearGradientBrush(recProgressTop, Color.WhiteSmoke, Color.Gainsboro, LinearGradientMode.Vertical);
                            Rectangle recProgressBottom = new Rectangle(e.Bounds.X, e.Bounds.Y + (e.Bounds.Height / 2), e.Bounds.Width, e.Bounds.Height / 2);
                            LinearGradientBrush brushBottom = new LinearGradientBrush(recProgressBottom, Color.LightGray, Color.Gainsboro, LinearGradientMode.Vertical);


                            e.Graphics.FillRectangle(brushTop, recProgressTop);
                            e.Graphics.FillRectangle(brushBottom, recProgressBottom);
                            Font titleFont = this.Font;
                            if(rssItem.Title == "")
                            {
                                rssItem.Title = FormStrings.NoTitleSpecified;
                            }
                            if (!rssItem.Read)
                            {
                                titleFont = new Font(this.Font, FontStyle.Bold);
                            }
                
                            e.Graphics.DrawString(rssItem.Title, titleFont, Brushes.Black, e.Bounds.X + 16, e.Bounds.Y);
                            e.Graphics.DrawString(rssItem.PubDate.ToString("yyyy-MM-dd"), new Font("Tahoma", 7F), Brushes.White, e.Bounds.X + 16, e.Bounds.Y + 17);
                        }
                        else
                        {

                            e.Graphics.FillRectangle(Brushes.White, e.Bounds);
                            Font titleFont = this.Font;
                            if (rssItem.Title == "")
                            {
                                rssItem.Title = FormStrings.NoTitleSpecified;
                            }
                            if (!rssItem.Read)
                            {
                                titleFont = new Font(this.Font, FontStyle.Bold);
                            }
                            e.Graphics.DrawString(rssItem.Title, titleFont, Brushes.Black, e.Bounds.X + 16, e.Bounds.Y);

                            if (rssItem.PubDate.Year != 1)
                            {
                                e.Graphics.DrawString(rssItem.PubDate.ToString("yyyy-MM-dd"), new Font("Tahoma", 7F), Brushes.Black, e.Bounds.X + 16, e.Bounds.Y + 17);
                            }
                            else if (rssItem.DcDate.Year != 1)
                            {
                                e.Graphics.DrawString(rssItem.DcDate.ToString("yyyy-MM-dd"), new Font("Tahoma", 7F), Brushes.Black, e.Bounds.X + 16, e.Bounds.Y + 17);
                            }
                            e.Graphics.DrawLine(Pens.LightYellow, e.Bounds.X, e.Bounds.Y, e.Bounds.X + e.Bounds.Width, e.Bounds.Y);

                        }
                        if (rssItem.EnclosureDownloaded)
                        {
                            e.Graphics.DrawImage(Resources.disksmall, e.Bounds.X+1, e.Bounds.Y + 2);
                        }
                        else
                        {
                            if (rssItem.Enclosure != null)
                            {
                                e.Graphics.DrawImage(Resources.disksmalllight, e.Bounds.X+1, e.Bounds.Y+2);
                            }
                        }
                        e.DrawFocusRectangle();
                    }
                }
                else
                {
                    string title = ((ListViewItem)PostsListView.Items[e.Index]).Text;
                    e.Graphics.DrawString(title, new Font(this.Font, FontStyle.Italic), Brushes.Black, e.Bounds.X, e.Bounds.Y);
                }
            }
        }

        /// <summary>
        /// Handles the MeasureItem event of the PostsListView control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.Windows.Forms.MeasureItemEventArgs"/> instance containing the event data.</param>
        private void PostsListView_MeasureItem(object sender, MeasureItemEventArgs e)
        {
            e.ItemHeight = 32;
        }

        /// <summary>
        /// Handles the SelectedIndexChanged event of the PostsListView control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.EventArgs"/> instance containing the event data.</param>
        private void PostsListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            FeedItem feedItem = (FeedItem)splitContainer1.Panel2.Tag;
            if (feedItem != null)
            {
                string strPlaylist = "";
				Rss.RssItem rssItem = PostsListView.SelectedItem as Rss.RssItem;
				if (rssItem == null)
					return;
                if (rssItem.Read == false)
                {
                    ((Rss.RssItem)PostsListView.SelectedItem).Read = true;
                    //feedItem.readPosts.Add(rssItem.Hashcode().ToString());
                    Settings.Default.Feeds[feedItem.GUID].ReadPosts.Add(rssItem.GetHashCode().ToString());
                }

                postViewer1.labelItemTitle.Text = rssItem.Title;
                if (rssItem.Content != null && rssItem.Content != "")
                {
                    postViewer1.SetHTML(rssItem.Content);
                }
                else
                {
                    postViewer1.SetHTML(rssItem.Description);
                }

                if (Settings.Default.ShowPlayer)
                {
                    if (rssItem.Enclosure != null)
                    {
						if (FeedsListView.SelectedItems.Count > 0)
						{
							ShowPlayer(strPlaylist, rssItem);
						}
                    }
                    else
                    {
                        splitPostsFilesViewer.Panel2Collapsed = true;
                        ////player1.SetContent("", " ");1
                    }
                }
            }
        }

		private void ShowPlayer(string strPlaylist, Rss.RssItem rssItem)
		{
			ListViewItem lvi = FeedsListView.SelectedItems[0];
		
			FeedItem fi = (FeedItem)lvi.Tag;
			if (fi.PlaylistName != "")
			{
				strPlaylist = fi.PlaylistName;
			}
			else
			{
				strPlaylist = fi.Title;
			}
			string strFilename = Settings.Default.DownloadLocation + "\\" + Utils.GetValidFolderPath(strPlaylist) + "\\" + rssItem.Enclosure.Url.ToString().Substring(rssItem.Enclosure.Url.ToString().LastIndexOf("/") + 1);

			if (System.IO.File.Exists(strFilename))
			{
				FileInfo fileInfo = new FileInfo(strFilename);
				if (fileInfo.Extension.ToLower() == ".mp3" || fileInfo.Extension.ToLower() == ".wav" || fileInfo.Extension.ToLower() == ".wma" || fileInfo.Extension.ToLower() == ".wmv")
				{
					//axWindowsMediaPlayer1.Visible = true;

					if (fileInfo.Extension.ToLower() == ".wmv")
					{
						splitPostsFilesViewer.SplitterDistance = splitPostsFilesViewer.Height / 2;
					}
					else
					{
						splitPostsFilesViewer.SplitterDistance = splitPostsFilesViewer.Height - 45;
					}
					splitPostsFilesViewer.Panel2Collapsed = false;
					axWindowsMediaPlayer1.URL = strFilename;
					axWindowsMediaPlayer1.Ctlcontrols.stop();
				}
				else
				{
					splitPostsFilesViewer.Panel2Collapsed = true;
				}
			}
			else if (rssItem.Enclosure.Url.LastIndexOf(".") > 0)
			{
				string strExtension = rssItem.Enclosure.Url.ToString().Substring(rssItem.Enclosure.Url.ToString().LastIndexOf(".")).ToLower();
				if (strExtension == ".mp3" || strExtension == ".wav" || strExtension == ".wma" || strExtension == ".wmv")
				{
					if (strExtension == ".wmv")
					{
						splitPostsFilesViewer.SplitterDistance = splitPostsFilesViewer.Height / 2;
					}
					else
					{
						splitPostsFilesViewer.SplitterDistance = splitPostsFilesViewer.Height - 45;
					}
					splitPostsFilesViewer.Panel2Collapsed = false;
					axWindowsMediaPlayer1.URL = rssItem.Enclosure.Url;
					axWindowsMediaPlayer1.Ctlcontrols.stop();
				}
				else
				{
					splitPostsFilesViewer.Panel2Collapsed = true;
				}
			}
			else
			{
				splitPostsFilesViewer.Panel2Collapsed = true;
			}
			postViewer1.viewerTag = rssItem;
		}
		/// <summary>
        /// Handles the SplitterMoved event of the splitContainer1 control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.Windows.Forms.SplitterEventArgs"/> instance containing the event data.</param>
        private void splitContainer1_SplitterMoved(object sender, SplitterEventArgs e)
        {
           // FeedsListView.Columns[2].Width = -2;
        }

        private void FilesListView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            // Determine if clicked column is already the column that is being sorted.
            if (e.Column == FilesColumnSorter.SortColumn)
            {
                // Reverse the current sort direction for this column.
                if (FilesColumnSorter.Order == SortOrder.Ascending)
                {
                    FilesColumnSorter.Order = SortOrder.Descending;
                }
                else
                {
                    FilesColumnSorter.Order = SortOrder.Ascending;
                }
            }
            else
            {
                // Set the column number that is to be sorted; default to ascending.
                FilesColumnSorter.SortColumn = e.Column;
                FilesColumnSorter.Order = SortOrder.Ascending;
            }

            // Perform the sort with these new sort options.
            FilesListView.Sort();
        }

        /// <summary>
        /// Handles the SelectedIndexChanged event of the tabPostFiles control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.EventArgs"/> instance containing the event data.</param>
        private void tabPostFiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (tabPostFiles.SelectedTab == tabPosts)
            {
                ShowPosts();
                fileSystemWatcher1.EnableRaisingEvents = false;
            }
            else
            {
                ShowFiles();

            }
        }


        /// <summary>
        /// Handles the SelectedIndexChanged event of the FilesListView control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.EventArgs"/> instance containing the event data.</param>
        private void FilesListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (Settings.Default.ShowPlayer)
            {
                ListViewItem lvi = FilesListView.FocusedItem;
                FileInfo fileinfo = (FileInfo)lvi.Tag;
                if (fileinfo != null)
                {
                    if (fileinfo.Extension.ToLower() == ".mp3" || fileinfo.Extension.ToLower() == ".wav" || fileinfo.Extension.ToLower() == ".wma" || fileinfo.Extension.ToLower() == ".wmv")
                    {
						playSelectedFile.Enabled = true;
						WMPLib.IWMPPlaylist axWindowsMediaPlayer1newPlaylist = axWindowsMediaPlayer1.newPlaylist("doppler", String.Empty);
						axWindowsMediaPlayer1newPlaylist.appendItem(axWindowsMediaPlayer1.newMedia(fileinfo.FullName));
						axWindowsMediaPlayer1.currentPlaylist = axWindowsMediaPlayer1newPlaylist;
                        axWindowsMediaPlayer1.Ctlcontrols.stop();
                        if (fileinfo.Extension.ToLower() == ".wmv")
                        {
                            splitPostsFilesViewer.SplitterDistance = splitPostsFilesViewer.Height / 2;
                        }
                        else
                        {
                            splitPostsFilesViewer.SplitterDistance = splitPostsFilesViewer.Height - 45;
                        }
                        splitPostsFilesViewer.Panel2Collapsed = false;
                    }
                    else
                    {
                        splitPostsFilesViewer.Panel2Collapsed = true;
                        //player1.Enabled = false;
						playSelectedFile.Enabled = false;
                    }
                }
            }
			else
			{
				playSelectedFile.Enabled = false;
			}
        }

        /// <summary>
        /// Handles the Click event of the retrievefeedsToolStripMenuItem control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.EventArgs"/> instance containing the event data.</param>
        private void retrievefeedsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RetrieveFeeds();
        }

        /// <summary>
        /// Handles the Click event of the viewlogToolStripMenuItem control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.EventArgs"/> instance containing the event data.</param>
        private void viewlogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            LogForm fLog = new LogForm();
            fLog.Show();
        }

        /// <summary>
        /// Handles the Click event of the importfrompodcastdirectoryToolStripMenuItem control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.EventArgs"/> instance containing the event data.</param>
        private void importfrompodcastdirectoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            BrowseOPMLForm fBrowserOpml = new BrowseOPMLForm();
            fBrowserOpml.ShowDialog();
			fBrowserOpml.Dispose();
            FillFeedList();
        }

        /// <summary>
        /// Handles the Paint event of the panelTopBar control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.Windows.Forms.PaintEventArgs"/> instance containing the event data.</param>
        private void panelTopBar_Paint(object sender, PaintEventArgs e)
        {
            Panel thisPanel = (Panel)sender;
            Rectangle recPanel = thisPanel.ClientRectangle;
            Rectangle recProgressTop = new Rectangle(recPanel.X, recPanel.Y, recPanel.Width, recPanel.Height / 2);
            LinearGradientBrush brushTop = new LinearGradientBrush(recProgressTop, Color.AntiqueWhite, Color.Orange, LinearGradientMode.Vertical);
            Rectangle recProgressBottom = new Rectangle(recPanel.X, recPanel.Y + (recPanel.Height / 2), recPanel.Width, recPanel.Height / 2);
            LinearGradientBrush brushBottom = new LinearGradientBrush(recProgressBottom, Color.DarkOrange, Color.Orange, LinearGradientMode.Vertical);


            e.Graphics.FillRectangle(brushTop, recProgressTop);
            e.Graphics.FillRectangle(brushBottom, recProgressBottom);
        }

        /// <summary>
        /// Handles the Paint event of the panel2 control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.Windows.Forms.PaintEventArgs"/> instance containing the event data.</param>
        private void panel2_Paint(object sender, PaintEventArgs e)
        {
            Panel thisPanel = (Panel)sender;
            Rectangle recPanel = thisPanel.ClientRectangle;
            Rectangle recProgressTop = new Rectangle(recPanel.X, recPanel.Y, recPanel.Width, recPanel.Height / 2);
            LinearGradientBrush brushTop = new LinearGradientBrush(recProgressTop, Color.AntiqueWhite, Color.Orange, LinearGradientMode.Vertical);
            Rectangle recProgressBottom = new Rectangle(recPanel.X, recPanel.Y + (recPanel.Height / 2), recPanel.Width, recPanel.Height / 2);
            LinearGradientBrush brushBottom = new LinearGradientBrush(recProgressBottom, Color.DarkOrange, Color.Orange, LinearGradientMode.Vertical);


            e.Graphics.FillRectangle(brushTop, recProgressTop);
            e.Graphics.FillRectangle(brushBottom, recProgressBottom);
        }

        /// <summary>
        /// Handles the Click event of the ExpandCollapseButton control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.EventArgs"/> instance containing the event data.</param>
        private void ExpandCollapseButton_Click(object sender, EventArgs e)
        {
            if (splitContainer1.Panel2Collapsed == false)
            {
                ExpandCollapseButton.Text = "«";
                Settings.Default.ViewerPane = false;
                splitContainer1.Panel2Collapsed = true;
                fileSystemWatcher1.EnableRaisingEvents = false;
            }
            else
            {
                ExpandCollapseButton.Text = "»";
                if (tabPostFiles.SelectedTab == tabFiles)
                {
                    if (fileSystemWatcher1.Path != "")
                    {
                        fileSystemWatcher1.EnableRaisingEvents = true;
                    }
                    ShowFiles();
                }
                else
                {
                    ShowPosts();
                }
                
                Settings.Default.ViewerPane = true;
                splitContainer1.Panel2Collapsed = false;
            }
        }

        /// <summary>
        /// Handles the Paint event of the panel1 control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.Windows.Forms.PaintEventArgs"/> instance containing the event data.</param>
        private void panel1_Paint(object sender, PaintEventArgs e)
        {
            Panel thisPanel = (Panel)sender;
            Rectangle recPanel = thisPanel.ClientRectangle;
            Rectangle recProgressTop = new Rectangle(recPanel.X, recPanel.Y, recPanel.Width, recPanel.Height / 2);
            LinearGradientBrush brushTop = new LinearGradientBrush(recProgressTop, Color.AntiqueWhite, Color.Orange, LinearGradientMode.Vertical);
            Rectangle recProgressBottom = new Rectangle(recPanel.X, recPanel.Y + (recPanel.Height / 2), recPanel.Width, recPanel.Height / 2);
            LinearGradientBrush brushBottom = new LinearGradientBrush(recProgressBottom, Color.DarkOrange, Color.Orange, LinearGradientMode.Vertical);


            e.Graphics.FillRectangle(brushTop, recProgressTop);
            e.Graphics.FillRectangle(brushBottom, recProgressBottom);
        }

        /// <summary>
        /// Handles the Click event of the deletethisfeedsubscriptionToolStripMenuItem control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.EventArgs"/> instance containing the event data.</param>
        private void deletethisfeedsubscriptionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DeleteFeed();
        }

        /// <summary>
        /// Deletes the feed.
        /// </summary>
        private void DeleteFeed()
        {
            if (FeedsListView.SelectedItems.Count > 0)
            {
                int Index = FeedsListView.SelectedIndices[0];
                ListViewItem lvi = FeedsListView.SelectedItems[0];
                FeedItem feedItem = (FeedItem)lvi.Tag;
                if (feedItem != null)
                {
                    if (MessageBox.Show(FormStrings.Areyousureyouwanttodeletethisfeedsubscription, FormStrings.Deletesubscription, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        Settings.Default.Feeds.Remove(feedItem.Url);
                        Settings.Default.Save();
                        lvi.Remove();
                        if (FeedsListView.Items.Count > Index)
                        {
                            try
                            {
                                FeedsListView.Items[Index].Selected = true;
                            }
                            catch (Exception)
                            { }
                        }
                        else
                        {
                            if (FeedsListView.Items.Count != 0)
                            {
                                FeedsListView.Items[Index - 1].Selected = true;
                            }
                            else
                            {
                                // make sure there are no 'old' posts waiting, which have no backend feed anymore
                                ClearPosts();
                                ClearFiles();
                            }
                        }
                    }
                }
            }
            
            RefreshInfoLabel();
        }

        /// <summary>
        /// Clears the files pane.
        /// </summary>
        private void ClearFiles()
        {
            FilesListView.Items.Clear();
        }

        /// <summary>
        /// Clears the posts pane.
        /// </summary>
        private void ClearPosts()
        {
            PostsListView.Items.Clear();
        }

        /// <summary>
        /// Handles the DoWork event of the backgroundRssRetriever control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.ComponentModel.DoWorkEventArgs"/> instance containing the event data.</param>
        private void backgroundRssRetriever_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker bw = (BackgroundWorker)sender;
            Hashtable hashArguments = (Hashtable)e.Argument;

            FeedItem feedItem = (FeedItem)hashArguments["FeedItem"];
            bool Force = (bool)hashArguments["Force"];
            Rss.RssFeed rssFeed = null;
            try
            {
                rssFeed = Utils.GetFeed(feedItem, true);
            }
            catch (ApplicationException ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                if (Settings.Default.LogLevel > 0) log.Error("BackGroundRetriever", ex);
            }
            if (rssFeed != null && rssFeed.Channels.Count > 0)
            {
                e.Result = rssFeed.Channels[0];
            }
            else
            {
                e.Result = null;
            }
            if (bw.CancellationPending)
            {
                e.Cancel = true;
            }
        }

        /// <summary>
        /// Handles the RunWorkerCompleted event of the backgroundRssRetriever control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.ComponentModel.RunWorkerCompletedEventArgs"/> instance containing the event data.</param>
        private void backgroundRssRetriever_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
           
            FeedItem feedItem = (FeedItem)splitContainer1.Panel2.Tag;
            if (e.Cancelled)
            {
                // The user canceled the operation.
                //MessageBox.Show("Operation was canceled");
            }
            else if (e.Error != null)
            {
                // There was an error during the operation.
                string msg = String.Format(FormStrings.Anerroroccurred, e.Error.Message);
                MessageBox.Show(msg);
            }
            else
            {
                if (e.Result != null)
                {
                    Rss.RssChannel rssChannel = (Rss.RssChannel)e.Result;
                    // RssFeed rssFeed = (RssFeed)e.Result;
                    FillPosts(feedItem, rssChannel);
                }
            }
        }

        /// <summary>
        /// Fills the posts pane.
        /// </summary>
        /// <param name="feedItem">The feed item.</param>
        /// <param name="rssChannel">The RSS channel.</param>
        /// <returns></returns>
        private bool FillPosts(FeedItem feedItem, Rss.RssChannel rssChannel)
        {
            bool boolRead = false;
            PostsListView.Items.Clear();

            PostsListView.BeginUpdate();
            PostsListView.ItemHeight = 2;
            StringCollection stcCurrent = new StringCollection();
            rssChannel.Items.Sort();
            for (int q = 0; q < rssChannel.Items.Count; q++)
            {
                if (feedItem.ReadPosts != null)
                {
                    foreach (string hashRead in feedItem.ReadPosts)
                    {
                        boolRead = false;
                        string strHash = rssChannel.Items[q].GetHashCode().ToString();
                        if (hashRead == strHash)
                        {
                            stcCurrent.Add(hashRead);
                            boolRead = true;
                            break;
                        }
                    }
                }
                else
                {
                    boolRead = false;
                }
                Rss.RssItem rssItem = rssChannel.Items[q];

                // RssItem rssItem = rssFeed.Items[q];
                rssItem.Read = boolRead;
                if (Settings.Default.History[rssItem.GetHashCode().ToString()] != null)
                {
                    rssItem.EnclosureDownloaded = true;
                }
                else
                {
                    rssItem.EnclosureDownloaded = false;
                }
              
                PostsListView.Items.Add(rssItem);

            }
            Settings.Default.Feeds[feedItem.GUID].ReadPosts = stcCurrent;
            PostsListView.EndUpdate();
            return boolRead;
        }

        /// <summary>
        /// Handles the DoubleClick event of the notifyIcon1 control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.EventArgs"/> instance containing the event data.</param>
        private void notifyIcon1_DoubleClick(object sender, EventArgs e)
        {
            //LockWindowUpdate(this.Handle);
            BeginUpdate();
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.Activate();
            if (this.ShowInTaskbar == false) this.ShowInTaskbar = true;
            EndUpdate();
        }

        /// <summary>
        /// Handles the FormClosing event of the MainForm control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.Windows.Forms.FormClosingEventArgs"/> instance containing the event data.</param>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason != CloseReason.ApplicationExitCall && e.CloseReason != CloseReason.WindowsShutDown)
            {
				SaveSettingsOnExit();
                if (Settings.Default.MinimizeOnClose)
                {
                    this.WindowState = FormWindowState.Minimized;
                    if (Settings.Default.MinimizeToSystemTray)
                    {
                        this.Hide();
                        this.ShowInTaskbar = false;
                    }
                    e.Cancel = true;
                }
                else
                {
                    sysTrayNavigator.Dispose();
                    Settings.Default.WindowLocation = this.Location;
                    try
                    {
                        Settings.Default.Splitter1 = splitContainer1.SplitterDistance;
                        Settings.Default.Splitter2 = splitContainer2.SplitterDistance;
                        Settings.Default.Splitter3 = splitContainerPosts.SplitterDistance;
                    }
                    catch (Exception)
                    { }
                }
            }
        }

        /// <summary>
        /// Handles the SizeChanged event of the MainForm control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.EventArgs"/> instance containing the event data.</param>
        private void MainForm_SizeChanged(object sender, EventArgs e)
        {
            if (Settings.Default.MinimizeToSystemTray)
            {
                if (this.WindowState == FormWindowState.Minimized)
                {
                    this.ShowInTaskbar = false;
                }
            }
        }

        /// <summary>
        /// Handles the MouseHover event of the buttonAdd control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="T:System.EventArgs"/> instance containing the event data.</param>
        private void buttonAdd_MouseHover(object sender, EventArgs e)
        {
           
            AddButton.FlatAppearance.BorderColor = Color.Gray;
        }

        private void buttonAdd_MouseLeave(object sender, EventArgs e)
        {
            AddButton.FlatAppearance.BorderColor = Color.White;
        }

        private void buttonRetrieve_MouseHover(object sender, EventArgs e)
        {
           
            RetrieveButton.FlatAppearance.BorderColor = Color.Gray;
        }

        private void buttonRetrieve_MouseLeave(object sender, EventArgs e)
        {
            RetrieveButton.FlatAppearance.BorderColor = Color.White;
        }

        private void FilesContextMenu_Opening(object sender, CancelEventArgs e)
        {
            if (FilesListView.SelectedItems.Count == 0 || FilesListView.Items.Count == 0)
            {
                e.Cancel = true;
            }  
        }

        private void deleteFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            bool delete = true;
            if(Settings.Default.ConfirmFileDelete)
            {
                if (MessageBox.Show(((FilesListView.SelectedItems.Count > 1) ? FormStrings.Areyousureyouwanttodeletethesefiles : FormStrings.Areyousureyouwanttodeletethisfile), FormStrings.Delete, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    delete = true;
                }
                else
                {
                    delete = false;
                }
            }
            if(delete)
            {
                try
                {
                    foreach (ListViewItem lvi in FilesListView.SelectedItems)
                    {
                        FileInfo fileInfo = (FileInfo)lvi.Tag;
                        File.Delete(fileInfo.FullName);
                        if(Settings.Default.LogLevel > 1) log.Info("User deleted " + fileInfo.FullName);
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    if(Settings.Default.LogLevel > 0) log.Error("Cannot delete file", ex);
                    MessageBox.Show(FormStrings.CannotDeleteFileAccessDenied, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                ShowFiles();
            }

        }

        private void fileSystemWatcher1_Changed(object sender, FileSystemEventArgs e)
        {
            UpdateSingleFileInListFiles(e);
        }

        private void UpdateSingleFileInListFiles(FileSystemEventArgs e)
        {
            for (int q = 0; q < FilesListView.Items.Count; q++)
            {
                FileInfo fileInfo = new FileInfo(e.FullPath);
                string path1 = e.Name + "[incomplete]";
                if (fileInfo.Extension == ".incomplete")
                {
                    path1 = fileInfo.Name.Substring(0, fileInfo.Name.IndexOf(".incomplete")) +"[incomplete]";
                }
               
                if (FilesListView.Items[q].Text == e.Name || FilesListView.Items[q].Text == path1)
                {
                    //     listFiles.Items[q].Text = e.Name;
                    if(File.Exists(path1))
                    {
                    FilesListView.Items[q].SubItems[1].Text = fileInfo.Length.ToString();
                    FilesListView.Items[q].Tag = fileInfo;

                    break;
                    }
                }
            }
        }

        private void fileSystemWatcher1_Created(object sender, FileSystemEventArgs e)
        {
            ShowFiles();
        }

        private void fileSystemWatcher1_Deleted(object sender, FileSystemEventArgs e)
        {
            ShowFiles();
        }

        private void fileSystemWatcher1_Renamed(object sender, RenamedEventArgs e)
        {
            UpdateSingleFileInListFiles(e);
        }

        private void toolStripButtonSearchText_KeyPress(object sender, KeyPressEventArgs e)
        {
           
        }

        private void exportOPMLFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ExportOPML();

        }

        private void ExportOPML()
        {
            XmlDocument xmlDoc = Utils.GetOPML();
            saveFileDialog1.AddExtension = true;
            saveFileDialog1.Filter = string.Format("{0} (*.opml)|*.opml|{1} (*.*)|*.*",FormStrings.OPMLFiles,FormStrings.AllFiles);
            saveFileDialog1.DefaultExt = "opml";
            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                //saveFileDialog1.FileName
                xmlDoc.Save(saveFileDialog1.FileName);
            }
			saveFileDialog1.Dispose();
        }

     
        private void importOPMLFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ImportOPML();
        }

        private void ImportOPML()
        {
            openFileDialog1.Title = FormStrings.ImportOPMLFile;
            openFileDialog1.FileName = "";
            openFileDialog1.AddExtension = true;
            openFileDialog1.Filter = string.Format("{0} (*.opml)|*.opml|{1} (*.*)|*.*", FormStrings.OPMLFiles, FormStrings.AllFiles);
            openFileDialog1.DefaultExt = "opml";
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                bool boolAdded = false;
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(openFileDialog1.FileName);
                FeedList feedList = OPMLParser.Parse(xmlDoc);
                foreach (FeedItem feedItem in feedList)
                {
                    bool boolFound = false;
                    foreach (FeedItem existingFeedItem in Settings.Default.Feeds)
                    {
                        if (feedItem.Url == existingFeedItem.Url)
                        {
                            boolFound = true;
                            break;
                        }
                    }
                    if (boolFound == false)
                    {
                        Settings.Default.Feeds.Add(feedItem);
                        boolAdded = true;
                    }
                }
                if (boolAdded)
                {
                    FillFeedList();
                }
            }
			openFileDialog1.Dispose();
        }

        private void toolStripView_DropDownOpening(object sender, EventArgs e)
        {
            
            categoriesToolStripMenuItem.DropDownItems.Clear();
            ArrayList arrCategories = Settings.Default.Feeds.Categories;
            foreach (string strCategory in arrCategories)
            {
                ToolStripMenuItem itmCategory = new ToolStripMenuItem();
                if (Settings.Default.ShowCategories != null && Settings.Default.ShowCategories.Contains(strCategory))
                {
                    itmCategory.Checked = true;
                }
                else
                {
                    itmCategory.Checked = false;
                }
                itmCategory.Text = strCategory;
                itmCategory.DisplayStyle = ToolStripItemDisplayStyle.Text;
                itmCategory.Tag = strCategory;
                itmCategory.CheckOnClick = true;
                itmCategory.Click += new EventHandler(itmCategory_Click);
               
                categoriesToolStripMenuItem.DropDownItems.Add(itmCategory);

            }
        }

        void itmCategory_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem itmCategory = (ToolStripMenuItem)sender;
            string strCategory = (string)itmCategory.Tag;
            if (Settings.Default.ShowCategories == null)
            {
                Settings.Default.ShowCategories = new ArrayList();
            }
            if(itmCategory.Checked == false)
            {
                 Settings.Default.ShowCategories.Remove(strCategory);
          
            } else {
                 Settings.Default.ShowCategories.Add(strCategory);
               
            }
            FillFeedList();
        }

        private void showFilteredSubscriptionsToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void FeedsListView_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            ListViewItem lvi = ((ListView)sender).Items[e.Index];
            FeedItem feedItem = (FeedItem)lvi.Tag;
            if (!lvi.SubItems[2].Text.StartsWith("An error"))
            {
                if (e.NewValue == CheckState.Checked)
                {
                    feedItem.IsChecked = true;
                }
                else
                {
                    feedItem.IsChecked = false;
                }
                Settings.Default.Feeds[lvi.Name] = feedItem;
            }
        }

        private void toolStripMenuItemExit_Click(object sender, EventArgs e)
        {
            sysTrayNavigator.Dispose();
            try
            {
                Settings.Default.Splitter1 = splitContainer1.SplitterDistance;
                Settings.Default.Splitter2 = splitContainer2.SplitterDistance;
                Settings.Default.Splitter3 = splitContainerPosts.SplitterDistance;
            }
            catch (Exception)
            { }
            Settings.Default.Save();
            Application.Exit();
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            FeedsListView.Update();
        }

        private void showSelectedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (showSelectedToolStripMenuItem.Checked)
            {
                Settings.Default.ShowSelected = true;
                
            }
            else
            {
                Settings.Default.ShowSelected = false;
     
            }
            
            FillFeedList();
        }

        private void schedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OptionsForm fOptions = new OptionsForm("tabScheduling");
            fOptions.ShowDialog();
			fOptions.Dispose();
        }

        private void toolStripDropDownButton7_Click(object sender, EventArgs e)
        {

        }

        private void englishToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Settings.Default.Culture = "en";
            if (Settings.Default.Culture != null && Settings.Default.Culture != "")
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(Settings.Default.Culture);
            }
            if (MessageBox.Show("In order to select English as the default language in Doppler you need to restart Doppler.\n\nDo you want to do this now?", "Change language", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
            {
                Application.Restart();
            }
        }

        private void nederlandsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Settings.Default.Culture = "nl";
            if (Settings.Default.Culture != null && Settings.Default.Culture != "")
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(Settings.Default.Culture);
            }
            if (MessageBox.Show("Om Nederlands als taal te activeren dient u Doppler af te sluiten en opnieuw op te starten\n\nWilt u dit nu doen?", "Taal wisseling", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
            {
                Application.Restart();
            }
            
        }

        private void editFeedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (FeedsListView.SelectedItems.Count > 0)
            {
                EditFeed(false);
            }
        }

        private void toolStripEdit_Click(object sender, EventArgs e)
        {
            if (FeedsListView.SelectedItems.Count > 0)
            {
                editFeedToolStripMenuItem.Enabled = true;
                deleteFeedToolStripMenuItem.Enabled = true;
            }
            else
            {
                editFeedToolStripMenuItem.Enabled = false;
                deleteFeedToolStripMenuItem.Enabled = false;
            }
        }

        private void deleteFeedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (FeedsListView.CheckedItems.Count > 0)
            {
                if (MessageBox.Show(String.Format(FormStrings.Areyousureyourwanttodeletethexselectedfeeds, FeedsListView.CheckedItems.Count), FormStrings.Deletesubscription, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    FeedList feedsToDelete = new FeedList();
                    for (int q = 0; q < Settings.Default.Feeds.Count; q++)
                    {
                        FeedItem feedItem = Settings.Default.Feeds[q];
                        if (feedItem.IsChecked)
                        {
                            feedsToDelete.Add(feedItem);
                        }
                    }
                    foreach (FeedItem feedToDelete in feedsToDelete)
                    {
                        Settings.Default.Feeds.Remove(feedToDelete.Url);
                    }
                }
            }
            Settings.Default.Save();
            FillFeedList();
        }

        private void categoriesToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {

        }

        private void panelBottom_Paint(object sender, PaintEventArgs e)
        {
            Panel thisPanel = (Panel)sender;
            Rectangle recPanel = thisPanel.ClientRectangle;
            Rectangle recProgressTop = new Rectangle(recPanel.X, recPanel.Y, recPanel.Width, recPanel.Height / 2);
            LinearGradientBrush brushTop = new LinearGradientBrush(recProgressTop, Color.WhiteSmoke, Color.Gainsboro, LinearGradientMode.Vertical);
            Rectangle recProgressBottom = new Rectangle(recPanel.X, recPanel.Y + (recPanel.Height / 2), recPanel.Width, recPanel.Height / 2);
            LinearGradientBrush brushBottom = new LinearGradientBrush(recProgressBottom, Color.LightGray, Color.Gainsboro, LinearGradientMode.Vertical);


            e.Graphics.FillRectangle(brushTop, recProgressTop);
            e.Graphics.FillRectangle(brushBottom, recProgressBottom);
        }

        private void splitPostsFilesViewer_Panel1_Paint(object sender, PaintEventArgs e)
        {
            Panel thisPanel = (Panel)sender;

            Rectangle recPanel = thisPanel.ClientRectangle;
            Rectangle recLeft = panelTopBar.ClientRectangle;
            Rectangle recProgressTop = new Rectangle(recPanel.X, recPanel.Y, recPanel.Width, recLeft.Height / 2);
            LinearGradientBrush brushTop = new LinearGradientBrush(recProgressTop, Color.FromArgb(255, 254, 252), Color.FromArgb(186, 207, 238), LinearGradientMode.Vertical);
            Rectangle recProgressBottom = new Rectangle(recPanel.X, recPanel.Y + (recLeft.Height / 2), recPanel.Width, recLeft.Height / 2);
            LinearGradientBrush brushBottom = new LinearGradientBrush(recProgressBottom, Color.FromArgb(161, 190, 232), Color.FromArgb(102, 146, 203), LinearGradientMode.Vertical);


            e.Graphics.FillRectangle(brushTop, recProgressTop);
            e.Graphics.FillRectangle(brushBottom, recProgressBottom);

        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ListViewItem lvi = FilesListView.FocusedItem;
            FileInfo fileinfo = (FileInfo)lvi.Tag;
            System.Diagnostics.Process.Start(fileinfo.FullName);
        }

        private void synchronizeToolStripMenuItem_Click(object sender, EventArgs e)
        {
           
        }

        private void catchupfeedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ListViewItem lvi = FeedsListView.SelectedItems[0];
            FeedItem feedItem = (FeedItem)lvi.Tag;
            CatchupForm fCatchup = new CatchupForm(feedItem);
            fCatchup.ShowDialog();
            fCatchup.Dispose();
        }

        private void FeedsListView_DragEnter(object sender, DragEventArgs e)
        {
            IDataObject dataObject = e.Data;
            string strURL = (string)dataObject.GetData(DataFormats.Text);
            if (strURL.ToLower().StartsWith("http"))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void FeedsListView_DragDrop(object sender, DragEventArgs e)
        {
            IDataObject dataObject = e.Data;
            string strURL = (string)dataObject.GetData(DataFormats.Text);
            if (strURL.ToLower().StartsWith("http"))
            {
                AddFeed(strURL, true, false);
            }
        }

        private void FeedCheckTimer_Tick(object sender, EventArgs e)
        {
            // check what kind of check we are running
            try
            {
                if (NetworkInterface.GetIsNetworkAvailable())
                {
                    if (Settings.Default.CheckAutomatic == true)
                    {
                        if (Settings.Default.CheckType == 0)
                        {
                            // we are checking in intervals
                            DateTime LastChecked = Settings.Default.LastRetrieve;
                            DateTime Now = DateTime.Now;

                            if (LastChecked.AddMinutes(Settings.Default.IntervalMinutes).CompareTo(Now) < 0)
                            {
                                Settings.Default.LastRetrieve = DateTime.Now;
                                if (Settings.Default.LogLevel > 1) log.Info("Starting automatic retrieve");
                                RetrieveFeeds();
                            }

                        }
                        else
                        {
                            // we are checking at specific times
                            if (Settings.Default.CheckHour1Enabled)
                            {
                                if (DateTime.Now.Hour == Settings.Default.CheckHour1.Hour && DateTime.Now.Minute == Settings.Default.CheckHour1.Minute)
                                {
                                    if (Settings.Default.LogLevel > 1) log.Info("Starting automatic retrieve");

                                    RetrieveFeeds();
                                }
                            }
                            if (Settings.Default.CheckHour2Enabled)
                            {
                                if (DateTime.Now.Hour == Settings.Default.CheckHour2.Hour && DateTime.Now.Minute == Settings.Default.CheckHour2.Minute)
                                {
                                    if (Settings.Default.LogLevel > 1) log.Info("Starting automatic retrieve");

                                    RetrieveFeeds();
                                }
                            }
                            if (Settings.Default.CheckHour3Enabled)
                            {
                                if (DateTime.Now.Hour == Settings.Default.CheckHour3.Hour && DateTime.Now.Minute == Settings.Default.CheckHour3.Minute)
                                {
                                    if (Settings.Default.LogLevel > 1) log.Info("Starting automatic retrieve");

                                    RetrieveFeeds();
                                }
                            }
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                if (Settings.Default.LogLevel > 0) log.Error("Automatic feed check", ex);
            }
        }

        private void catchupAllFeedsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CatchupForm fCatchup = new CatchupForm();
            fCatchup.ShowDialog();
            fCatchup.Dispose();
        }

        private void backgroundCatchupper_DoWork(object sender, DoWorkEventArgs e)
        {
            for (int q = 0; q < Settings.Default.Feeds.Count; q++)
            {
                FeedItem feedItem = Settings.Default.Feeds[q];
                try
                {
                    if (feedItem.IsChecked)
                    {
                        Rss.RssFeed rssFeed = Utils.GetFeed(feedItem);
                        if (rssFeed != null)
                        {
                            //progressBar1.Maximum = rssFeed.Channels[0].Items.Count;
                            //progressBar1.Minimum = 0;
                            for (int y = 0; y < rssFeed.Channels[0].Items.Count; y++)
                            {
                                //progressBar1.Value = q;
                                Rss.RssItem rssItem = rssFeed.Channels[0].Items[y];
                                if (Settings.Default.History[rssItem.GetHashCode().ToString()] == null)
                                {
                                    HistoryItem historyItem = new HistoryItem();
                                    historyItem.FeedGUID = feedItem.GUID;
                                    historyItem.Hashcode = rssItem.GetHashCode();
                                    historyItem.Title = rssItem.Title;
                                    historyItem.ItemDate = rssItem.PubDate.ToString();
                                    historyItem.FeedUrl = feedItem.Url;
                                    Settings.Default.History.Add(historyItem);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (Settings.Default.LogLevel > 0) log.Error("Error while catching up", ex);
                    MessageBox.Show(ex.Message, "Error while catching up " + feedItem.Title, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                }
            }
            Settings.Default.Save();
        }

        private void UpdateCheckTimer_Tick(object sender, EventArgs e)
        {
            if (NetworkInterface.GetIsNetworkAvailable())
            {

                try
                {
                    bool newVersion = Utils.CheckForLatestVersion();
                    
                    Settings.Default.LastUpdateCheck = DateTime.Now;
                    if (newVersion)
                    {
                        if (MessageBox.Show("A new version of Doppler is available. Do you want go to the website and download the new version?", "New version", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        {
                            System.Diagnostics.Process.Start("http://www.dopplerradio.net");
                        }
                    }
                    UpdateCheckTimer.Enabled = false;
                }
                catch
                { //something went wrong, leave the timer running
                }
            }
        }

        private void checkForUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OptionsForm fOptions = new OptionsForm("tabMain");
            fOptions.ShowDialog();
            fOptions.Dispose();
        }

        private void svenskaToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Settings.Default.Culture = "sv";
            if (Settings.Default.Culture != null && Settings.Default.Culture != "")
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(Settings.Default.Culture);
            }
            if (MessageBox.Show("För att byta språk till Svenska som standardspråk i Doppler måste du starta om Doppler.\n\nVill du göra det nu?", "Byt Språk", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
            {
                Application.Restart();
            }
        }

        private void applyDefaultFeedSettingsToAllFeedsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Settings.Default.DefaultItem != null)
            {
                FeedItem defaultFeed = Settings.Default.DefaultItem;
                for (int q = 0; q < Settings.Default.Feeds.Count; q++)
                {
                    FeedItem feedItem = Settings.Default.Feeds[q];
                    if (defaultFeed.TagTitle != null && defaultFeed.TagTitle != "") feedItem.TagTitle = defaultFeed.TagTitle;
                    if (defaultFeed.TagGenre != null && defaultFeed.TagGenre != "") feedItem.TagGenre = defaultFeed.TagGenre;
                    if (defaultFeed.TagAlbum != null && defaultFeed.TagAlbum != "") feedItem.TagAlbum = defaultFeed.TagAlbum;
                    if (defaultFeed.TagArtist != null && defaultFeed.TagArtist != "") feedItem.TagArtist = defaultFeed.TagArtist;
                    feedItem.UseSpaceSavers = defaultFeed.UseSpaceSavers;
                    if (defaultFeed.UseSpaceSavers)
                    {
                        feedItem.Spacesaver_Files = defaultFeed.Spacesaver_Files;
                        feedItem.Spacesaver_Files = defaultFeed.Spacesaver_Files;
                        feedItem.Spacesaver_Days = defaultFeed.Spacesaver_Days;
                    }
                    feedItem.MaxMb = defaultFeed.MaxMb;
                    feedItem.RetrieveNumberOfFiles = defaultFeed.RetrieveNumberOfFiles;
                    if (defaultFeed.PlaylistName != null && defaultFeed.PlaylistName != "") feedItem.PlaylistName = defaultFeed.PlaylistName;
                    if (defaultFeed.Textfilter != null && defaultFeed.Textfilter != "") feedItem.Textfilter = "";
                    Settings.Default.Feeds[q] = feedItem;

                }
                Settings.Default.Save();
            }
        }

        private void FeedsListView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            // Determine if clicked column is already the column that is being sorted.
            if (e.Column == FeedsColumnSorter.SortColumn)
            {
                // Reverse the current sort direction for this column.
                if (FeedsColumnSorter.Order == SortOrder.Ascending)
                {
                    FeedsColumnSorter.Order = SortOrder.Descending;
                }
                else
                {
                    FeedsColumnSorter.Order = SortOrder.Ascending;
                }
            }
            else
            {
                // Set the column number that is to be sorted; default to ascending.
                FeedsColumnSorter.SortColumn = e.Column;
                FeedsColumnSorter.Order = SortOrder.Ascending;
            }

            // Perform the sort with these new sort options.
            Settings.Default.FeedsSortOrder = FeedsColumnSorter.Order;
            Settings.Default.FilesSortColumn = FeedsColumnSorter.SortColumn;
            FeedsListView.Sort();
            FillFeedList();
        }

        private void retrieveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RetrieveFeeds();
        }

        private const int WM_SETREDRAW = 0x000B;
        private const int WM_USER = 0x400;
        private const int EM_GETEVENTMASK = (WM_USER + 59);
        private const int EM_SETEVENTMASK = (WM_USER + 69);
        [DllImport("user32", CharSet = CharSet.Auto)]
        private extern static IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);

 
        public void BeginUpdate() 
        {
            this.SuspendLayout();
        }
        public void EndUpdate()
        {
            this.ResumeLayout();
        } 


        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            //BeginUpdate();
            this.WindowState = FormWindowState.Normal;
            //EndUpdate();
        }

        private void refreshPostsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if(splitContainer1.Panel2.Tag != null)
            {
                Cursor.Current = Cursors.WaitCursor;
                FeedItem feedItem = (FeedItem)splitContainer1.Panel2.Tag;
                Rss.RssFeed rssFeed = Utils.GetFeed(feedItem);
                FillPosts(feedItem,rssFeed.Channels[0]);
                Cursor.Current = Cursors.Default;
            }
        }

        private void PostsContextMenu_Opening(object sender, CancelEventArgs e)
        {
            if (PostsListView.SelectedItems.Count > 0)
            {
                Rss.RssItem rssItem = (Rss.RssItem)PostsListView.SelectedItem;
                
                if (Settings.Default.History[rssItem.GetHashCode().ToString()] != null)
                {
                    addToHistoryToolStripMenuItem.Enabled = false;
                    removeFromHistoryToolStripMenuItem.Enabled = true;
                }
                else
                {
                    addToHistoryToolStripMenuItem.Enabled = true;
                    removeFromHistoryToolStripMenuItem.Enabled = false;
                }
            }
            else
            {
                e.Cancel = true;
            }
        }

        private void addToHistoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FeedItem feedItem = (FeedItem)splitContainer1.Panel2.Tag;
            int CurrentIndex = PostsListView.SelectedIndex;
            Rss.RssItem rssItem = (Rss.RssItem)PostsListView.SelectedItem;
            HistoryItem hi = new HistoryItem();
            hi.FeedUrl = feedItem.Url;
            hi.FeedGUID = feedItem.GUID;
            hi.Hashcode = rssItem.GetHashCode();

            hi.Title = rssItem.Title;
            hi.ItemDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            Settings.Default.History.Add(hi);
            //Settings.Default.Save();
            Rss.RssFeed rssFeed = Utils.GetFeed(feedItem, true);

            FillPosts(feedItem,rssFeed.Channels[0]);

            PostsListView.SelectedIndex = CurrentIndex;
            
        }

        private void removeFromHistoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FeedItem feedItem = (FeedItem)splitContainer1.Panel2.Tag;
            int CurrentIndex = PostsListView.SelectedIndex;
            Rss.RssItem rssItem = (Rss.RssItem)PostsListView.SelectedItem;
            HistoryItem historyItem = Settings.Default.History[rssItem.GetHashCode().ToString()];
            if (historyItem != null)
            {
                Settings.Default.History.Remove(historyItem);
            }
            //Settings.Default.Save();
            Rss.RssFeed rssFeed = Utils.GetFeed(feedItem, true);

            FillPosts(feedItem, rssFeed.Channels[0]);

            PostsListView.SelectedIndex = CurrentIndex;
        }

     
        private void embeddedMediaPlayerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (embeddedMediaPlayerToolStripMenuItem.Checked)
            {
               Settings.Default.ShowPlayer = false;
               embeddedMediaPlayerToolStripMenuItem.Checked = false;
                if (splitPostsFilesViewer.Panel2Collapsed == false)
                {
                    splitPostsFilesViewer.Panel2Collapsed = true;
                }
            }
            else
            {
                Settings.Default.ShowPlayer = true;
                embeddedMediaPlayerToolStripMenuItem.Checked = true;
            }
        }

        private void PostsListView_DoubleClick(object sender, EventArgs e)
        {
            if (PostsListView.SelectedItem != null)
            {
				Rss.RssItem post = PostsListView.SelectedItem as Rss.RssItem;
				if (post != null)
				{
					if (post.Link != null && post.Link != "")
					{
						System.Diagnostics.Process.Start(post.Link);
					}
				}
            }
        }

        private void tabPosts_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
            }
        }

        private void addFeedToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            AddFeedWithWizard(true,true);
        }

        private void optionsToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            ShowOptions(true);
        }

        private void openDopplerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.WindowState = FormWindowState.Normal;
            }
            if (this.ShowInTaskbar == false)
            {
                this.ShowInTaskbar = true;
            }
        }

        private void FilesListView_DoubleClick(object sender, EventArgs e)
        {
            if (FilesListView.SelectedItems.Count > 0)
            {
                ListViewItem lvi = FilesListView.FocusedItem;
                FileInfo fileinfo = (FileInfo)lvi.Tag;
				try
				{
					System.Diagnostics.Process.Start(fileinfo.FullName);
				}
				catch (Win32Exception ex)
				{
					if (Settings.Default.LogLevel > 0) log.Error("There was a problem playing the selected file " + fileinfo.Name, ex);
					MessageBox.Show("There was a problem playing the selected file. Please check to ensure the file is completely downloaded.", "Error playing file!", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
				}
            }
        }

        private void toolsToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
           
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            try
            {
                splitContainer1.SplitterDistance = Settings.Default.Splitter1;
                splitContainer2.SplitterDistance = Settings.Default.Splitter2;
                splitContainerPosts.SplitterDistance = Settings.Default.Splitter3;
            }
            catch (Exception)
            {
                // something went wrong, set the distance to default values
                splitContainer1.SplitterDistance = 0;
            }
            if (Settings.Default.ForceRetrieveOnStartup)
            {
                Settings.Default.LastRetrieve = DateTime.Now;
                RetrieveFeeds();
            }
        }

        private void notifyIcon1_MouseMove(object sender, MouseEventArgs e)
        {
            if (sysTrayNavigator.Threads > 0)
            {
                notifyIcon1.Text = String.Format("{0} active download" + ((sysTrayNavigator.Threads > 1) ? "s" : ""), sysTrayNavigator.Threads);
            }
            else
            {
                notifyIcon1.Text = "Dopper - Idle";
                if (Settings.Default.CheckAutomatic)
                {
                    if (Settings.Default.LastRetrieve.Year != 1)
                        notifyIcon1.Text += ", last checked at " + Settings.Default.LastRetrieve.ToString();
                }
            }
        }

        private void FeedsListView_DoubleClick(object sender, EventArgs e)
        {
            if (FeedsListView.Items.Count > 0 && FeedsListView.SelectedItems.Count > 0)
            {
                ListViewItem lvi = FeedsListView.SelectedItems[0];
                if (lvi.SubItems[2].Text.StartsWith("An error"))
                {
                    if (Settings.Default.LogLevel == 0)
                    {
                        if (MessageBox.Show(FormStrings.LoggingIsCurrentDisabledDoYouWantToEnableTheLogToRecordErrors, "Logging", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        {
                            Settings.Default.LogLevel = 1;
                            MessageBox.Show(FormStrings.LoggingHasBeenEnabledAndErrorsWillShowUpInTheLogFromNowOn, "Logging", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                    lvi.SubItems[2].Text = "";
                    LogForm fLog = new LogForm();
                    fLog.Show();
                    // loop throught the items and clear the error messages
                    foreach (ListViewItem lvi2 in FeedsListView.Items)
                    {
                        lvi2.ForeColor = Color.Black;
                        if (lvi2.SubItems[2].Text.StartsWith("An error"))
                        {
                            lvi2.SubItems[2].Text = "";
                        }
                    }
                }
            }
        }

        private void openLocalFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ListViewItem lvi = FilesListView.FocusedItem;
            FileInfo fileinfo = (FileInfo)lvi.Tag;
            System.Diagnostics.Process.Start(fileinfo.DirectoryName);
        }

        private void showTipsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StartupTipForm startupTipForm = new StartupTipForm();
            startupTipForm.ShowDialog();
        }

        private void FeedsListView_SizeChanged(object sender, EventArgs e)
        {
            FeedsListView.Columns[2].Width = -2;
        }

        private void francaisToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Settings.Default.Culture = "fr";
            if (Settings.Default.Culture != null && Settings.Default.Culture != "")
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(Settings.Default.Culture);
            }
            if (MessageBox.Show("Pour sélectionner le Français comme langue par défaut, vous devez redémarrer Doppler.\n\nVoulez vous redémarrer maintenant?", "Changer la langue", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
            {
                Application.Restart();
            }
        }

        private void slovenskyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Settings.Default.Culture = "sk";
            if (Settings.Default.Culture != null && Settings.Default.Culture != "")
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(Settings.Default.Culture);
            }
            if (MessageBox.Show("Ak si chcete vybrať angličtinu ako predvolený jazyk programu, musíte program Doppler reštartovať.\n\nChcete tak urobiť ihneď?", "Zmeniť jazyk", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
            {
                Application.Restart();
            }
        }

        private void polskiToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Settings.Default.Culture = "pl";
            if (Settings.Default.Culture != null && Settings.Default.Culture != "")
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(Settings.Default.Culture);
            }
            if (MessageBox.Show("W celu ustawienia języka angielskiego jako domyślny, należy zrestarować Doppler'a.\n\nCzy chcesz to zrobić teraz?", "Zmień język", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
            {
                Application.Restart();
            }


        }

        private void downloadThisPodcastToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Rss.RssChannel channel = new Rss.RssChannel();
            channel.Description = languages.FormStrings.TemporaryDownloadChannel;
            RssFeed feed = new RssFeed();
            feed.Channels.Add(channel);

            RssItem rssItem = (RssItem)PostsListView.SelectedItem;
            channel.Items.Add(rssItem);
           
            FeedItem feedItem = (FeedItem)splitContainer1.Panel2.Tag;
            int intMaxThreads = Settings.Default.MaxThreads;

            ListViewItem lvi = FeedsListView.SelectedItems[0];

            ThreadPool.SetMaxThreads(intMaxThreads, intMaxThreads);
            Retriever retriever = new Retriever(feedItem, lvi);
            retriever.FileFound += new FileFoundHandler(retriever_FileFound);
            retriever.RetrieveCompleteCallback += new RetrieveCompleteHandler(retriever_RetrieveCompleteCallback);
            ThreadPool.QueueUserWorkItem(new WaitCallback(retriever.ParseTemporaryRss), feed.Channels[0]);
            
        }

        private void refreshFeedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ListViewItem lvi = FeedsListView.SelectedItems[0];
            FeedItem feedItem = (FeedItem)lvi.Tag;

            Cursor.Current = Cursors.WaitCursor;
            Rss.RssFeed rssFeed = Utils.GetFeed(feedItem);
            if (rssFeed != null && rssFeed.Exceptions.Count == 0)
            {
                FillPosts(feedItem, rssFeed.Channels[0]);
                Cursor.Current = Cursors.Default;
            }
            else
            {
                Cursor.Current = Cursors.Default;
                if (rssFeed != null && rssFeed.Exceptions.Count > 0)
                {
                    MessageBox.Show("An error occurred while refreshing this feed\n\n" + rssFeed.Exceptions[0].Message, "Refresh feed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    MessageBox.Show("An error occurred while refreshing this feed\n\nIs the URL correct?", "Refresh feed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

		private void axWindowsMediaPlayer1_PlayStateChange(object sender, AxWMPLib._WMPOCXEvents_PlayStateChangeEvent e)
		{
		}

		private void playSelectedFile_Click(object sender, EventArgs e)
		{
			FileInfo file = FilesListView.FocusedItem.Tag as FileInfo;
			axWindowsMediaPlayer1.URL = file.FullName;
		}
    } 
}

