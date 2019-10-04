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
using System.Windows.Controls;

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
        string appDir, xmlLogDir;
        int OneInt = 0;
        public MainWindow()
        {
            InitializeComponent();
        }
        public class MachineInfo
        {
            public string machineName;
            public List<UserInfoRecord> userInfoRecords = new List<UserInfoRecord>();
        }
        public class UserInfoRecord
        {
            public string userSID { get; set; }
            public string username { get; set; }
            public int LogOnsNum { get; set; }
            public int NormalLogOnsNum { get; set; }
            public int UnusualLogOnsNum { get; set; }
            public int FailedLogOnsNum { get; set; }
            public int LogOffsNum { get; set; }
            public int NormalLogOffsNum { get; set; }
            public int UnusualLogOffsNum { get; set; }
            public DateTime? FirstLogOn { get; set; }
            public DateTime? LastLogOn { get; set; }
            public int[] LogOnHours = new int[24];
            public int[] LogOnDays = new int[7];
            public int[] LogOffHours = new int[24];
            public int[] LogOffDays = new int[7];
            public int[] FailedLogOnHours = new int[24];
            public int[] FailedLogOnDays = new int[7];
            public List<LogRecord> LogOns = new List<LogRecord>();                      
        }        
        public class LogRecord
        {
            public bool AllLogOn;
            public bool NormalLogOn { get; set; }
            public bool UnusualLogOn { get; set; }
            public bool FailedLogOn;
            public bool NormalLogOff;
            public bool UnusualLogOff;            
            public DateTime LogOnDateTime { get; set; }
            public DateTime LogOffDateTime { get; set; }
            public string networkAddress { get; set; }
            public string desktopName { get; set; }
            public string username { get; set; }
        }
        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            if (0 != Interlocked.Exchange(ref OneInt, 1))
            {
                return;
            }
            textBlock.Clear();
            MachineInfo machineInfoObject = new MachineInfo
            {
                machineName = Environment.MachineName
            };
            string updateText = await Task.Run(() => getData(machineInfoObject));
            UserInfoListBox.ItemsSource = machineInfoObject.userInfoRecords;
            UserInfoListBox.Items.Refresh();
            textBlock.Text += updateText;                     
            Interlocked.Exchange(ref OneInt, 0);
        }        
        private string getData(MachineInfo machineInfoObject)
        {
            StringBuilder sb = new StringBuilder();
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            
            EventLogQuery eventsQuery;
            EventLogReader logReader;
            try
            {
                int num = 0;
                eventsQuery = new EventLogQuery("system", PathType.LogName, "*");
                logReader = new EventLogReader(eventsQuery);
                processEventLog(machineInfoObject, logReader, ref num);
                eventsQuery = new EventLogQuery("security", PathType.LogName, "*");
                logReader = new EventLogReader(eventsQuery);
                processEventLog(machineInfoObject, logReader, ref num);
            }
            catch
            {
                MessageBox.Show("Error reading Event log.");
                return sb.ToString();
            }
            if (logReader != null)
            {
                logReader.Dispose();
            }
            sb.Append(Environment.NewLine);
            sb.Append("Queries complete.");
            sb.Append(Environment.NewLine);
            string xmlFilePath = saveToXML(machineInfoObject);
            sb.Append("Data logged to: " + xmlFilePath);
            sb.Append(Environment.NewLine);
            addUserInfoToSB(machineInfoObject, sb);
            return sb.ToString();
        }

        private void processEventLog(MachineInfo machineInfoObject, EventLogReader logReader, ref int num)
        {
            EventRecord eventDetail;
            int temp;    
            while (true)
            {
                eventDetail = logReader.ReadEvent();
                if (eventDetail == null)
                {
                    break;
                }
                if (eventDetail.Id != 7001 && eventDetail.Id != 7002 && eventDetail.Id != 4625)
                {
                    continue;
                }
                if (!eventDetail.TimeCreated.HasValue)
                {
                    continue;
                }
                string userSID;
                string username;
                UserInfoRecord userInfoRecord;
                if (eventDetail.Id != 4625)
                {
                    userSID = eventDetail.Properties[1].Value.ToString();                    
                    try
                    {
                        SecurityIdentifier s = new SecurityIdentifier(userSID);
                        username = s.Translate(typeof(NTAccount)).Value;
                        if (username.IndexOf(@"\") != -1)
                        {
                            username = username.Substring(username.IndexOf(@"\") + 1);
                        }
                        if (machineInfoObject.userInfoRecords.Where(u => u.username == username).Count() > 0)
                        {
                            userInfoRecord = machineInfoObject.userInfoRecords.Where(u => u.username == username).FirstOrDefault();
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
                    }
                    catch
                    {
                        username = "NULL";
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
                    }                    
                }
                else
                {
                    username = eventDetail.Properties[5].Value.ToString();
                    try
                    {
                        var account = new NTAccount(username);                        
                        var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
                        userSID = sid.ToString();
                    }
                    catch
                    {
                        userSID = "NULL";
                    }
                    if (machineInfoObject.userInfoRecords.Where(u => u.username == username).Count() > 0)
                    {
                        userInfoRecord = machineInfoObject.userInfoRecords.Where(u => u.username == username).FirstOrDefault();
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
                }
                SaveFirstLast(eventDetail, userInfoRecord);
                if (eventDetail.Id == 7001)
                {                                        
                    evalDateTime(eventDetail, userInfoRecord);
                }
                else if (eventDetail.Id == 7002)
                {
                    evalDateTime(eventDetail, userInfoRecord);
                }
                else if (eventDetail.Id == 4625)
                {                    
                    evalDateTime(eventDetail, userInfoRecord);
                }
                num++;
                if (num % 100 == 0)
                {
                    temp = num;
                    Application.Current.Dispatcher.Invoke(new Action(() => { textBlock.Clear(); textBlock.Text = temp.ToString() + " events processed."; }));
                    
                }
            }
            temp = num;
            Application.Current.Dispatcher.Invoke(new Action(() => { textBlock.Clear(); textBlock.Text = temp.ToString() + " events processed."; }));
        }

        private static void SaveFirstLast(EventRecord eventDetail, UserInfoRecord userInfoRecord)
        {
            if (userInfoRecord.FirstLogOn.HasValue)
            {
                if (userInfoRecord.FirstLogOn.Value > eventDetail.TimeCreated.Value)
                {
                    userInfoRecord.FirstLogOn = eventDetail.TimeCreated;
                }
            }
            else
            {
                userInfoRecord.FirstLogOn = eventDetail.TimeCreated;
            }
            if (userInfoRecord.LastLogOn.HasValue)
            {
                if (userInfoRecord.LastLogOn.Value < eventDetail.TimeCreated.Value)
                {
                    userInfoRecord.LastLogOn = eventDetail.TimeCreated;
                }
            }
            else
            {
                userInfoRecord.LastLogOn = eventDetail.TimeCreated;
            }
        }

        private string saveToXML(MachineInfo machineInfoObject)
        {
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
            return xmlFilePath;
        }

        private void evalDateTime(EventRecord eventdetail, UserInfoRecord userInfoRecord) // get desktop and network address for log on and log off.
        {            
            DateTime eventDateTime = eventdetail.TimeCreated.Value;
            int hour = eventDateTime.Hour;
            int day = DayOfWeek[eventDateTime.DayOfWeek.ToString()];
            if (eventdetail.Id == 7001)
            {
                LogRecord logRecord = new LogRecord();
                logRecord.desktopName = eventdetail.MachineName;
                logRecord.username = userInfoRecord.username;
                userInfoRecord.LogOnHours[hour]++;
                userInfoRecord.LogOnDays[day]++;
                userInfoRecord.LogOnsNum++;                
                if (CheckIfUnusual(hour, day))
                {
                    userInfoRecord.UnusualLogOnsNum++;
                    logRecord.UnusualLogOn = true;                    
                }
                else
                {
                    userInfoRecord.NormalLogOnsNum++;
                    logRecord.NormalLogOn = true;
                }
                logRecord.AllLogOn = true;
                logRecord.LogOnDateTime = eventdetail.TimeCreated.Value;
                userInfoRecord.LogOns.Add(logRecord);
            }
            else if (eventdetail.Id == 7002)
            {
                if (userInfoRecord.LogOns.Count() == 0)
                {
                    return;
                }
                LogRecord logRecord = userInfoRecord.LogOns.Last();                
                userInfoRecord.LogOffHours[hour]++;
                userInfoRecord.LogOffDays[day]++;                
                logRecord.LogOffDateTime = eventdetail.TimeCreated.Value;
                if (CheckIfUnusual(hour, day))
                {
                    userInfoRecord.UnusualLogOffsNum++;
                    logRecord.UnusualLogOff = true;
                }
                else
                {
                    userInfoRecord.NormalLogOffsNum++;
                    logRecord.NormalLogOff = true;
                }
            }
            else if (eventdetail.Id == 4625)
            {
                userInfoRecord.FailedLogOnHours[hour]++;
                userInfoRecord.FailedLogOnDays[day]++;
                userInfoRecord.FailedLogOnsNum++;
                LogRecord logRecord = new LogRecord();
                logRecord.username = userInfoRecord.username;
                logRecord.FailedLogOn = true;
                logRecord.LogOnDateTime = eventdetail.TimeCreated.Value;
                logRecord.LogOffDateTime = eventdetail.TimeCreated.Value;
                logRecord.networkAddress = eventdetail.Properties[19].Value.ToString();
                logRecord.desktopName = eventdetail.Properties[1].Value.ToString();
                userInfoRecord.LogOns.Add(logRecord);
            }
        }

        private static bool CheckIfUnusual(int hour, int day)
        {
            if (hour < 7 || hour > 18 || day == 5 || day == 6)
            {
                return true;
            }
            return false;
        }

        private void addUserInfoToSB(MachineInfo machineInfoObject, StringBuilder sb)
        {
            /*
            sb.Append(Environment.NewLine);
            sb.Append("Machine name: " + machineInfoObject.machineName);
            sb.Append(Environment.NewLine);
            foreach (var userInfoRecord in machineInfoObject.userInfoRecords)
            {
                if (userInfoRecord.LogOnsNum == 0)
                {
                    continue;
                }
                sb.Append(Environment.NewLine);
                sb.Append("Username: " + userInfoRecord.username);
                sb.Append(Environment.NewLine);
                sb.Append("User SID: " + userInfoRecord.userSID);
                sb.Append(Environment.NewLine);
                sb.Append("First Log On: ");
                if (userInfoRecord.FirstLogOn.HasValue)
                {
                    sb.Append(userInfoRecord.FirstLogOn.Value.ToLongTimeString() + " - " + userInfoRecord.FirstLogOn.Value.ToLongDateString());
                }
                else
                {
                    sb.Append("NULL");
                }
                sb.Append(Environment.NewLine);
                sb.Append("Last Log On: ");
                if (userInfoRecord.LastLogOn.HasValue)
                {
                    sb.Append(userInfoRecord.LastLogOn.Value.ToLongTimeString() + " - " + userInfoRecord.LastLogOn.Value.ToLongDateString());
                }
                else
                {
                    sb.Append("NULL");
                }
                sb.Append(Environment.NewLine);
                processUI(userInfoRecord.LogOnEvents, sb);
                processUI(userInfoRecord.LogOffEvents, sb);                            
            }
            sb.Append(Environment.NewLine);
            foreach (var userInfoRecord in machineInfoObject.userInfoRecords)
            {
                if (userInfoRecord.FailedLogOns.Count() == 0)
                {
                    continue;
                }
                sb.Append(userInfoRecord.FailedLogOns.Count() + " Failed Log Ons for username " + userInfoRecord.username + ":");
                sb.Append(Environment.NewLine);
                foreach (var failedLogon in userInfoRecord.FailedLogOns)
                {
                    sb.Append("Failed Logon, for user " + userInfoRecord.username + ": " + failedLogon.dateTime.ToLongTimeString() + " - " + failedLogon.dateTime.ToLongDateString() + " from network address: " + failedLogon.networkAddress + " (" + failedLogon.desktopName + ")" );
                    sb.Append(Environment.NewLine);
                }
                sb.Append(Environment.NewLine);
            }
            */
        }

        private void processUI(LogRecord logRecord, StringBuilder sb)
        {
            /*
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
                    string duration = TranslateTime(i) + " - " + TranslateTime(i + 1);
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
            sb.Append(logRecord.UnusualTimes.Count().ToString() + " Unusual " + eventType + " (before 7am or after 6pm on weekday or any time on Saturday or Sunday): ");
            sb.Append(Environment.NewLine);
            foreach (var UnusualEvent in logRecord.UnusualTimes)
            {
                sb.Append(UnusualEvent.ToLongTimeString() + " - " + UnusualEvent.ToLongDateString());
                sb.Append(Environment.NewLine);
            }
            */
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

        private async void Button_OpenXML(object sender, RoutedEventArgs e)
        {
            if (0 != Interlocked.Exchange(ref OneInt, 1))
            {
                return;
            }
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
            textBlock.Text = "Loading data from: " + fileName;
            textBlock.Text += Environment.NewLine;
            StringBuilder sb = new StringBuilder();
            MachineInfo machineInfoFromXML = new MachineInfo();
            string retStr;
            (retStr, machineInfoFromXML) = await Task.Run(() => OpenXML(fileName, machineInfoFromXML));
            textBlock.Text += retStr;
            UserInfoListBox.ItemsSource = machineInfoFromXML.userInfoRecords;
            UserInfoListBox.Items.Refresh();
            Interlocked.Exchange(ref OneInt, 0);
        }
        
        private (string, MachineInfo) OpenXML(string fileName, MachineInfo machineInfoFromXML)
        {
            StringBuilder sb = new StringBuilder();
            XmlSerializer serializer = new XmlSerializer(typeof(MachineInfo));
            if (File.Exists(fileName))
            {
                using (StreamReader streamReader = new StreamReader(fileName))
                {                    
                    machineInfoFromXML = (MachineInfo)serializer.Deserialize(streamReader);
                    addUserInfoToSB(machineInfoFromXML, sb);
                }
            }
            return (sb.ToString(), machineInfoFromXML);
        }
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
