using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
            textBlock.Text += "Querying Log Ons & Log offs ...";
            textBlock.Text += Environment.NewLine;
            Task.Factory.StartNew(() => getData());
        }

        public class MachineInfo
        {
            public string machineName;
            public List<UserInfoRecord> userInfoRecords = new List<UserInfoRecord>();
        }
        public class UserInfoRecord
        {
            public int LogOnNum = 0;
            public int LogOffNum = 0;
            public int[] LogOnHours = new int[24];
            public int[] LogOffHours = new int[24];            
            public int[] LogOnDays = new int[7];
            public int[] LogOffDays = new int[7];
            public List<DateTime> UnusualLogOns = new List<DateTime>();
            public List<DateTime> UnusualLogOffs = new List<DateTime>();
            public string userSID;
            public string username;            
        }

        private void getData()
        {
            StringBuilder sb = new StringBuilder();            
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            MachineInfo machineInfoObject = new MachineInfo
            {
                machineName = Environment.MachineName
            };
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
                    string username = "Could not translate SID to username";
                    try
                    {
                        SecurityIdentifier s = new SecurityIdentifier(userSID);
                        if (s.IsAccountSid())
                        {
                            username = s.Translate(typeof(NTAccount)).Value;
                        }
                    }
                    catch { }
                    UserInfoRecord userInfoRecord;                  
                    if (machineInfoObject.userInfoRecords.Where(u => u.userSID == userSID).Count() > 0)
                    {
                        userInfoRecord = machineInfoObject.userInfoRecords.Where(u => u.userSID == userSID).FirstOrDefault();                        
                    }
                    else
                    {
                        userInfoRecord = new UserInfoRecord
                        {
                            userSID = userSID,
                            username = username,                        
                        };
                        machineInfoObject.userInfoRecords.Add(userInfoRecord);
                    }                    
                    if (eventdetail.Id == 7001)
                    {
                        evalDateTime(eventdetail, userInfoRecord.LogOnDays, userInfoRecord.LogOnHours, ref userInfoRecord.LogOnNum, userInfoRecord.UnusualLogOns);
                    }
                    else if (eventdetail.Id == 7002)
                    {
                        evalDateTime(eventdetail, userInfoRecord.LogOffDays, userInfoRecord.LogOffHours, ref userInfoRecord.LogOffNum, userInfoRecord.UnusualLogOffs);
                    }
                }
            }
            catch (EventLogNotFoundException e)
            {
                MessageBox.Show("Error reading event log: " + e.InnerException.Message);
                Interlocked.Exchange(ref OneInt, 0);
                return;
            }

            var xmlFileName = "LogonAuditor - " + Environment.MachineName + " - " + DateTime.Now.ToLongDateString() + " - " + DateTime.Now.ToLongTimeString() + ".xml";
            xmlFileName = xmlFileName.Replace(":", ".");
            string xmlFilePath = Path.Combine(appDir, xmlFileName);
            XmlSerializer serializer = new XmlSerializer(typeof(MachineInfo));
            using (StreamWriter streamWriter = new StreamWriter(xmlFilePath))
            {
                serializer.Serialize(streamWriter, machineInfoObject);
            }
            sb.Append("Data logged to: " + xmlFilePath);
            sb.Append(Environment.NewLine);
            addUserInfoToSB(machineInfoObject, sb);
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                textBlock.Text += "Queries complete.";
                textBlock.Text += Environment.NewLine;
                textBlock.Text += sb.ToString();
                Interlocked.Exchange(ref OneInt, 0);
            }));            
        }

        private void evalDateTime(EventRecord eventdetail, int[] eventDays, int[] eventTimes, ref int eventNum, List<DateTime> UnusualEvents)
        {
            if (!eventdetail.TimeCreated.HasValue)
            {
                return;
            }
            int day;
            int hour;
            DateTime eventDateTime = eventdetail.TimeCreated.Value;
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

        private void addUserInfoToSB(MachineInfo machineInfoObject, StringBuilder sb)
        {
            sb.Append(Environment.NewLine);
            sb.Append("Machine name: " + machineInfoObject.machineName);
            sb.Append(Environment.NewLine);
            foreach (var userInfoRecord in machineInfoObject.userInfoRecords)
            {
                sb.Append(Environment.NewLine);
                sb.Append("Username: " + userInfoRecord.username);
                sb.Append(Environment.NewLine);
                sb.Append("User SID: " + userInfoRecord.userSID);
                sb.Append(Environment.NewLine);
                processUI(userInfoRecord.LogOnHours, userInfoRecord.LogOnDays, userInfoRecord.LogOnNum, userInfoRecord.UnusualLogOns, sb, true);
                processUI(userInfoRecord.LogOffHours, userInfoRecord.LogOffDays, userInfoRecord.LogOffNum, userInfoRecord.UnusualLogOffs, sb, false);
            }
        }

        private void processUI(int[] eventTimes, int[] eventDays, int eventNum, List<DateTime> UnusualEvents, StringBuilder sb, bool LogOns)
        {
            string duration;
            string eventType = "Log On(s)";
            if (!LogOns)
            {
                eventType = "Log Off(s)";
            }
            sb.Append(eventType + ":");
            sb.Append(Environment.NewLine);
            sb.Append(eventNum.ToString() + " " + eventType + " (total).");
            sb.Append(Environment.NewLine);
            sb.Append("Number of " + eventType + " - Hour of " + eventType + ": ");
            sb.Append(Environment.NewLine);
            for (int i = 0; i < eventTimes.Length; i++)
            {
                if (eventTimes[i] > 0)
                {
                    duration = TranslateTime(i);
                    duration += " - " + TranslateTime(i + 1);
                    sb.Append(eventTimes[i].ToString() + " - " + duration);
                    sb.Append(Environment.NewLine);
                }
            }
            sb.Append("Number of " + eventType + " - day of the week: ");
            sb.Append(Environment.NewLine);
            for (int d = 0; d < eventDays.Length; d++)
            {
                if (eventDays[d] > 0)
                {
                    sb.Append(eventDays[d] + " - " + DayOfWeek.FirstOrDefault(x => x.Value == d).Key);
                    sb.Append(Environment.NewLine);
                }
            }
            sb.Append(UnusualEvents.Count().ToString() + " Unusual " + eventType + " (before 8am or after 6pm on weekday or any time on Saturday or Sunday): ");
            sb.Append(Environment.NewLine);
            if (UnusualEvents.Count() > 0)
            {
                foreach (var UnusualEvent in UnusualEvents)
                {
                    sb.Append(UnusualEvent.ToLongTimeString() + " - " + UnusualEvent.ToLongDateString());
                    sb.Append(Environment.NewLine);
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
            textBlock.Clear();
            Task.Factory.StartNew(() => OpenXML());
        }
        private void OpenXML()
        {
            StringBuilder sb = new StringBuilder();            
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.InitialDirectory = appDir;
            if (openFileDialog.ShowDialog() == false)
            {
                return;
            }
            string fileName = openFileDialog.FileName;
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                textBlock.Text = "Loading data from: " + fileName;
                textBlock.Text += Environment.NewLine;
            }));
            XmlSerializer serializer = new XmlSerializer(typeof(MachineInfo));
            if (File.Exists(fileName))
            {
                using (StreamReader streamReader = new StreamReader(fileName))
                {
                    MachineInfo machineInfoFromXML = new MachineInfo();
                    machineInfoFromXML = (MachineInfo)serializer.Deserialize(streamReader);
                    addUserInfoToSB(machineInfoFromXML, sb);
                }
            }
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                textBlock.Text += sb.ToString();
                Interlocked.Exchange(ref OneInt, 0);
            }));            
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            appDir = System.IO.Path.GetDirectoryName(exePath);
        }
    }

}
