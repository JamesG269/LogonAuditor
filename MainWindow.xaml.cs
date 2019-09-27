using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.DirectoryServices.AccountManagement;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.IO;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace LogonAuditor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        string appDir;
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            textBlock.Clear();
            sb.Clear();
            Task.Factory.StartNew(() => getData());
        }
        StringBuilder sb = new StringBuilder();
        

        public class userInfo
        {
            public int LogOnNum = 0;
            public int LogOffNum = 0;
            public int[] LogOnTimes = new int[24];
            public int[] LogOffTimes = new int[24];
            public string userSID;
        }

        private void getData()
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            List<userInfo> userInfoList = new List<userInfo>();
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => textBlock.Text += "Querying Log Ons ...\n"));
            string query = "*";
            EventLogQuery eventsQuery = new EventLogQuery("system", PathType.LogName, query);
            try
            {
                EventLogReader logReader = new EventLogReader(eventsQuery);
                for (EventRecord eventdetail = logReader.ReadEvent(); eventdetail != null; eventdetail = logReader.ReadEvent())
                {
                    if (eventdetail.Id == 7001)
                    {
                        string userSID = eventdetail.Properties[1].Value.ToString();
                        int c = userInfoList.Where(u => u.userSID == userSID).Count();
                        if (c == 0)
                        {
                            userInfo tempUI = new userInfo();
                            tempUI.userSID = userSID;
                            userInfoList.Add(tempUI);
                        }
                        userInfo uI = userInfoList.Where(u => u.userSID == userSID).FirstOrDefault();
                        uI.LogOnTimes[eventdetail.TimeCreated.Value.Hour]++;
                        uI.LogOnNum++;
                    }
                    else if (eventdetail.Id == 7002)
                    {
                        string userSID = eventdetail.Properties[1].Value.ToString();
                        int c = userInfoList.Where(u => u.userSID == userSID).Count();
                        if (c == 0)
                        {
                            userInfo tempUI = new userInfo();
                            tempUI.userSID = userSID;
                            userInfoList.Add(tempUI);
                        }
                        userInfo uI = userInfoList.Where(u => u.userSID == userSID).FirstOrDefault();
                        uI.LogOffTimes[eventdetail.TimeCreated.Value.Hour]++;
                        uI.LogOffNum++;
                    }
                }
            }
            catch (EventLogNotFoundException e)
            {
                MessageBox.Show("Error reading event log.");
                return;
            }

            var fileName = "LoginAuditor - " + Environment.MachineName + " - " + DateTime.Now.ToLongDateString() + " - " + DateTime.Now.ToLongTimeString() + ".xml";
            fileName = fileName.Replace(":", ".");
            string saveFile = System.IO.Path.Combine(appDir, fileName);
            XmlSerializer serializer = new XmlSerializer(typeof(List<userInfo>));
            using (StreamWriter streamWriter = new StreamWriter(saveFile))
            {
                serializer.Serialize(streamWriter, userInfoList);
            }
            sb.Append("Data logged to: " + saveFile + "\n");
            addUserInfoToSB(userInfoList);
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            { textBlock.Text += "Queries complete.\n"; textBlock.Text += sb.ToString(); }
            ));
        }

        private void addUserInfoToSB(List<userInfo> userInfoList)
        {
            foreach (var uI in userInfoList)
            {                
                sb.Append("\nLog On events: \n");
                processUI(uI, uI.LogOnTimes);
                sb.Append("\nLog Off events: \n");
                processUI(uI, uI.LogOffTimes);
            }
        }

        private void processUI(userInfo uI, int[] eventTimes)
        {
            string username;
            try
            {
                SecurityIdentifier s = new SecurityIdentifier(uI.userSID);
                username = s.Translate(typeof(NTAccount)).Value;
            }
            catch
            {
                username = "Could not translate SID.";
            }
            sb.Append("Username: " + username);
            sb.Append("\n");
            sb.Append("Number of events (total): " + uI.LogOnNum.ToString());
            sb.Append("\n");
            sb.Append("Number of events - Hour of Event: ");
            sb.Append("\n");
            for (int i = 0; i < 24; i++)
            {
                if (uI.LogOnTimes[i] > 0)
                {
                    string dur = TranslateTime(i);
                    dur += " - " + TranslateTime(i + 1);                    
                    sb.Append(eventTimes[i].ToString() + " - " + dur + "\n");
                }
            }
        }

        private string TranslateTime(int hour)
        {
            string time;
            hour = hour % 24;
            if (hour < 12)
            {
                if (hour == 0)
                {
                    hour = 12;
                }
                time = hour.ToString() + "AM";
            }
            else
            {
                if (hour > 12)
                {
                    hour = hour - 12;
                }
                time = hour.ToString() + "PM";
            }
            return time;
        }

        private void Button_OpenXML(object sender, RoutedEventArgs e)
        {
            sb.Clear();
            textBlock.Clear();
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.InitialDirectory = appDir;
            if (openFileDialog.ShowDialog() == false)
            {
                return;
            }
            string fileName = openFileDialog.FileName;
            textBlock.Text = "Loading data from: " + fileName + "\n";
            XmlSerializer serializer = new XmlSerializer(typeof(List<userInfo>));
            if (File.Exists(fileName))
            {
                using (StreamReader streamReader = new StreamReader(fileName))
                {
                    List<userInfo> userInfoListFromXML = new List<userInfo>();
                    userInfoListFromXML = (List<userInfo>)serializer.Deserialize(streamReader);
                    addUserInfoToSB(userInfoListFromXML);                    
                }
            }
            textBlock.Text += sb.ToString();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            appDir = System.IO.Path.GetDirectoryName(exePath);
        }
    }

}
