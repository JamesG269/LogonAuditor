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
using System.Runtime.Serialization;

namespace LogonAuditor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Dictionary<string, int> DayOfWeek = new Dictionary<string, int>()
            {
                {"Monday",0 },
                {"Tuesday", 1 },
                {"Wednesday", 2 },
                {"Thursday", 3 },
                {"Friday", 4 },
                {"Saturday", 5 },
                {"Sunday", 6 }
            };
        string appDir;
        int OneInt = 0;
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (0 != Interlocked.Exchange(ref OneInt, 1))
            {
                return;
            }
            textBlock.Clear();
            Task.Factory.StartNew(() => getData());
        }

        public class userInfo
        {
            public int LogOnNum = 0;
            public int LogOffNum = 0;
            public int[] LogOnHours = new int[24];
            public int[] LogOffHours = new int[24];
            public string userSID;
            public int[] LogOnDays = new int[7];
            public int[] LogOffDays = new int[7];
            public List<DateTime> UnusualLogOns = new List<DateTime>();
            public List<DateTime> UnusualLogOffs = new List<DateTime>();
            public string username;
        }

        private void getData()
        {
            StringBuilder sb = new StringBuilder();
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            List<userInfo> userInfoList = new List<userInfo>();
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => textBlock.Text += "Querying Log Ons & Log offs ...\n"));
            string query = "*";
            EventLogQuery eventsQuery = new EventLogQuery("system", PathType.LogName, query);
            try
            {
                EventLogReader logReader = new EventLogReader(eventsQuery);

                for (EventRecord eventdetail = logReader.ReadEvent(); eventdetail != null; eventdetail = logReader.ReadEvent())
                {
                    if (eventdetail.Id != 7001 && eventdetail.Id != 7002)
                    {
                        continue;
                    }
                    string userSID = eventdetail.Properties[1].Value.ToString();
                    string username = Environment.MachineName + @"\" + userSID;
                    try
                    {
                        SecurityIdentifier s = new SecurityIdentifier(userSID);
                        if (s.IsAccountSid())
                        {
                            username = s.Translate(typeof(NTAccount)).Value;
                        }
                    }
                    catch { }                    
                    int c = userInfoList.Where(u => u.userSID == userSID).Count();
                    if (c == 0)
                    {
                        userInfo tempUI = new userInfo();
                        tempUI.userSID = userSID;
                        userInfoList.Add(tempUI);
                    }
                    userInfo uI = userInfoList.Where(u => u.userSID == userSID).FirstOrDefault();
                    uI.userSID = userSID;
                    uI.username = username;
                    if (eventdetail.Id == 7001)
                    {
                        evalDateTime(eventdetail, ref uI.LogOnDays, ref uI.LogOnHours, ref uI.LogOnNum, uI.UnusualLogOns);
                    }
                    else if (eventdetail.Id == 7002)
                    {
                        evalDateTime(eventdetail, ref uI.LogOffDays, ref uI.LogOffHours, ref uI.LogOffNum, uI.UnusualLogOffs);
                    }
                }
            }
            catch (EventLogNotFoundException e)
            {
                MessageBox.Show("Error reading event log: " + e.InnerException.Message);
                Interlocked.Exchange(ref OneInt, 0);
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
            sb.Append("Data logged to: " + saveFile);
            sb.Append("\n");
            addUserInfoToSB(userInfoList, sb);
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                textBlock.Text += "Queries complete.";
                textBlock.Text += "\n";
                textBlock.Text += sb.ToString();                
            }));
            Interlocked.Exchange(ref OneInt, 0);
        }

        private void evalDateTime(EventRecord eventdetail, ref int[] eventDays, ref int[] eventTimes, ref int eventNum, List<DateTime> UnusualEvents)
        {
            int day;
            int hour;
            DateTime eventDateTime = eventdetail.TimeCreated ?? DateTime.Now;
            hour = eventDateTime.Hour;
            eventTimes[hour]++;
            day = DayOfWeek[eventDateTime.DayOfWeek.ToString()];
            eventDays[day]++;
            eventNum++;
            if (hour < 8 || hour > 18 || day == 5 || day == 6)
            {
                UnusualEvents.Add(eventDateTime);
            }
        }

        private void addUserInfoToSB(List<userInfo> userInfoList, StringBuilder sb)
        {
            foreach (var uI in userInfoList)
            {
                sb.Append("\n");
                sb.Append("Username: " + uI.username);
                sb.Append("\n");
                processUI(uI.LogOnHours, uI.LogOnDays, uI.LogOnNum, uI.UnusualLogOns, sb, true);
                processUI(uI.LogOffHours, uI.LogOffDays, uI.LogOffNum, uI.UnusualLogOffs, sb, false);
            }
        }

        private void processUI(int[] eventTimes, int[] eventDays, int eventNum, List<DateTime> UnusualEvents, StringBuilder sb, bool LogOns)
        {
            string duration;
            string eventType = "Log Ons";
            if (!LogOns)
            {
                eventType = "Log Offs";
            }
            sb.Append(eventType + ":");
            sb.Append("\n");
            sb.Append(eventNum.ToString() + " " + eventType + " (total).");
            sb.Append("\n");
            sb.Append("Number of " + eventType + " - Hour of " + eventType + ": ");
            sb.Append("\n");
            for (int i = 0; i < eventTimes.Length; i++)
            {
                if (eventTimes[i] > 0)
                {
                    duration = TranslateTime(i);
                    duration += " - " + TranslateTime(i + 1);
                    sb.Append(eventTimes[i].ToString() + " - " + duration + "\n");
                }
            }
            sb.Append("Number of " + eventType + " - day of the week: ");
            sb.Append("\n");
            for (int d = 0; d < eventDays.Length; d++)
            {
                if (eventDays[d] > 0)
                {
                    sb.Append(eventDays[d] + " - " + DayOfWeek.FirstOrDefault(x => x.Value == d).Key);
                    sb.Append("\n");
                }
            }
            sb.Append(UnusualEvents.Count().ToString() + " Unusual " + eventType + " (before 8am or after 6pm or on Saturday or Sunday): ");
            sb.Append("\n");
            if (UnusualEvents.Count() > 0)
            {
                foreach (var UE in UnusualEvents)
                {
                    sb.Append(UE.ToLongTimeString() + " - " + UE.ToLongDateString());
                    sb.Append("\n");
                }
            }
        }

        private string TranslateTime(int hour)
        {
            string time;
            hour %= 24;
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
                    hour -= 12;
                }
                time = hour.ToString() + "PM";
            }
            return time;
        }

        private void Button_OpenXML(object sender, RoutedEventArgs e)
        {
            if (0 != Interlocked.Exchange(ref OneInt, 1))
            {
                return;
            }
            StringBuilder sb = new StringBuilder();
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
                    addUserInfoToSB(userInfoListFromXML, sb);
                }
            }
            textBlock.Text += sb.ToString();
            Interlocked.Exchange(ref OneInt, 0);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            appDir = System.IO.Path.GetDirectoryName(exePath);
        }
    }

}
