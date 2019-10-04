using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LogonAuditor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private void UserInfoListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (UserInfoListBox.SelectedItems.Count > 0)
            {
                List<UserInfoRecord> userInfoRecords = UserInfoListBox.SelectedItems.OfType<UserInfoRecord>().ToList();
                List<LogRecord> AllLogOnRecords = new List<LogRecord>();
                List<LogRecord> NormalLogOnRecords = new List<LogRecord>();
                List<LogRecord> UnusualLogOnRecords = new List<LogRecord>();
                List<LogRecord> FailedLogOnRecords = new List<LogRecord>();
                foreach (var userInfoRecord in userInfoRecords)
                {
                    foreach (var logRecord in userInfoRecord.LogOns)
                    {
                        if (logRecord.AllLogOn == true)
                        {
                            AllLogOnRecords.Add(logRecord);
                        }
                        if (logRecord.NormalLogOn == true)
                        {
                            NormalLogOnRecords.Add(logRecord);
                        }
                        if (logRecord.UnusualLogOn == true)
                        {
                            UnusualLogOnRecords.Add(logRecord);
                        }
                        if (logRecord.FailedLogOn == true)
                        {
                            FailedLogOnRecords.Add(logRecord);
                        }
                    }
                }
                AllLogonsListBox.ItemsSource = AllLogOnRecords;
                AllLogonsListBox.Items.Refresh();
                NormalLogonsListBox.ItemsSource = NormalLogOnRecords;
                NormalLogonsListBox.Items.Refresh();
                UnusualLogonsListBox.ItemsSource = UnusualLogOnRecords;
                UnusualLogonsListBox.Items.Refresh();
                FailedLogonsListBox.ItemsSource = FailedLogOnRecords;
                FailedLogonsListBox.Items.Refresh();
                UpdateColumnWidths(updateGrid);
            }
        }
        
        public void UpdateAllListView()
        {
            UpdateListView(AllLogonsListBox);
            UpdateListView(NormalLogonsListBox);
            UpdateListView(UnusualLogonsListBox);
            UpdateListView(FailedLogonsListBox);
        }
        public void UpdateColumnWidths(Grid gridToUpdate)
        {
            foreach (UIElement element in gridToUpdate.Children)
            {
                UpdateListView(element);
            }
            UpdateAllListView();
        }

        private void UpdateListView(UIElement element)
        {
            element.UpdateLayout();
            if (element is ListView)
            {
                var e = element as ListView;
                ListViewTargetUpdated(e);
                e.UpdateLayout();
            }
        }

        private static void UpdateColumnWidthsRun(GridView gridViewToUpdate)
        {
            foreach (var column in gridViewToUpdate.Columns)
            {
                // If this is an "auto width" column...

                //if (double.IsNaN(column.Width))
                {
                    // Set its Width back to NaN to auto-size again
                    column.Width = 0;
                    column.Width = double.NaN;
                }

            }
        }
        private void ListViewTargetUpdated(ListView listViewToUpdate)
        {
            // Get a reference to the ListView's GridView...        
            if (null != listViewToUpdate)
            {
                var gridView = listViewToUpdate.View as GridView;
                if (null != gridView)
                {
                    // ... and update its column widths
                    UpdateColumnWidthsRun(gridView);
                }
            }
        }
    }
}
