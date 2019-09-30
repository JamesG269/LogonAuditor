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
            public LogRecord LogOnEvents = new LogRecord()
            { logType = "Log on(s)" };
            public LogRecord LogOffEvents = new LogRecord()
            { logType = "Log off(s)" };                
            public string userSID;
            public string username;            
        }
        public class LogRecord
        {
            public string logType;
            public int Num = 0;            
            public int[] Hours = new int[24];            
            public int[] Days = new int[7];            
            public List<DateTime> UnusualTimes = new List<DateTime>();            
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
                    if (!eventdetail.TimeCreated.HasValue)
                    {
                        continue;
                    }
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
                        evalDateTime(eventdetail, userInfoRecord.LogOnEvents);
                    }
                    else if (eventdetail.Id == 7002)
                    {
                        evalDateTime(eventdetail, userInfoRecord.LogOffEvents);
                    }
                }
                logReader.Dispose();
            }
            catch (EventLogNotFoundException e)
            {
                MessageBox.Show("Error reading event log: " + e.InnerException.Message);
                Interlocked.Exchange(ref OneInt, 0);
                return;
            }

            var xmlFileName = "LogonAuditor - " + Environment.MachineName + " - " + DateTime.Now.ToLongDateString() + " - " + DateTime.Now.ToLongTimeString() + ".xml";
            xmlFileName = xmlFileName.Replace(":", ".");            
            if (!Directory.Exists(xmlLogDir))
            {
                Directory.CreateDirectory(xmlLogDir);
            }
            string xmlFilePath = Path.Combine(xmlLogDir, xmlFileName);
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

        private void evalDateTime(EventRecord eventdetail, LogRecord logRecord)
        {            
            int day;
            int hour;
            DateTime eventDateTime = eventdetail.TimeCreated.Value;
            hour = eventDateTime.Hour;
            logRecord.Hours[hour]++;
            day = DayOfWeek[eventDateTime.DayOfWeek.ToString()];
            logRecord.Days[day]++;
            logRecord.Num++;
            if (hour < 8 || hour > 18 || day == 5 || day == 6)
            {
                logRecord.UnusualTimes.Add(eventDateTime);
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
                processUI(userInfoRecord.LogOnEvents, sb);
                processUI(userInfoRecord.LogOffEvents, sb);
            }
        }

        private void processUI(LogRecord logRecord, StringBuilder sb)
        {
            string duration;
            string eventType = logRecord.logType;            
            sb.Append(eventType + ":");
            sb.Append(Environment.NewLine);
            sb.Append(logRecord.Num.ToString() + " " + eventType + " (total).");
            sb.Append(Environment.NewLine);
            sb.Append("Number of " + eventType + " - Hour of " + eventType + ": ");
            sb.Append(Environment.NewLine);
            for (int i = 0; i < logRecord.Hours.Length; i++)
            {
                if (logRecord.Hours[i] > 0)
                {
                    duration = TranslateTime(i);
                    duration += " - " + TranslateTime(i + 1);
                    sb.Append(logRecord.Hours[i].ToString() + " - " + duration);
                    sb.Append(Environment.NewLine);
                }
            }
            sb.Append("Number of " + eventType + " - day of the week: ");
            sb.Append(Environment.NewLine);
            for (int d = 0; d < logRecord.Days.Length; d++)
            {
                if (logRecord.Days[d] > 0)
                {
                    sb.Append(logRecord.Days[d] + " - " + DayOfWeek.FirstOrDefault(x => x.Value == d).Key);
                    sb.Append(Environment.NewLine);
                }
            }
            sb.Append(logRecord.UnusualTimes.Count().ToString() + " Unusual " + eventType + " (before 8am or after 6pm on weekday or any time on Saturday or Sunday): ");
            sb.Append(Environment.NewLine);
            if (logRecord.UnusualTimes.Count() > 0)
            {
                foreach (var UnusualEvent in logRecord.UnusualTimes)
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
            if (Directory.Exists(xmlLogDir))
            {
                openFileDialog.InitialDirectory = xmlLogDir;
            }
            if (openFileDialog.ShowDialog() == false)
            {
                return;
            }
            xmlLogDir = Path.GetDirectoryName(openFileDialog.FileName);
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

        string xmlLogDir;
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            appDir = System.IO.Path.GetDirectoryName(exePath);
            xmlLogDir = Path.Combine(appDir, "LogonAuditorLogs");
            Get45PlusFromRegistry();
        }
        
        public void Get45PlusFromRegistry()
        {
            const string subkey = @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\";

            using (var ndpKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(subkey))
            {
                if (ndpKey != null && ndpKey.GetValue("Release") != null)
                {
                    textBlock.Text += $".NET Framework Version: {CheckFor45PlusVersion((int)ndpKey.GetValue("Release"))}";
                }
                else
                {
                    textBlock.Text += ".NET Framework Version 4.5 or later is not detected.";
                }
                textBlock.Text += Environment.NewLine;
            }

            // Checking the version using >= enables forward compatibility.
            string CheckFor45PlusVersion(int releaseKey)
            {
                if (releaseKey >= 528040)
                    return "4.8 or later";
                if (releaseKey >= 461808)
                    return "4.7.2";
                if (releaseKey >= 461308)
                    return "4.7.1";
                if (releaseKey >= 460798)
                    return "4.7";
                if (releaseKey >= 394802)
                    return "4.6.2";
                if (releaseKey >= 394254)
                    return "4.6.1";
                if (releaseKey >= 393295)
                    return "4.6";
                if (releaseKey >= 379893)
                    return "4.5.2";
                if (releaseKey >= 378675)
                    return "4.5.1";
                if (releaseKey >= 378389)
                    return "4.5";
                // This code should never execute. A non-null release key should mean
                // that 4.5 or later is installed.
                return "No 4.5 or later version detected";
            }
        }
    }

}
