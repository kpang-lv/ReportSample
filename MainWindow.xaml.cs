/* 
Copyright (C) 2014 Five9, Inc. All rights reserved.

www.five9.com/legal#terms

Software is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES 
 OR CONDITIONS OF ANY KIND, either express or implied.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;
using System.Data;
using System.IO;
using ReportSample.Five9.CFG;
using Kent.Boogaart.KBCsv;

namespace ReportSample
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        WsAdminClient wsAdminClient = null;
        AuthHeaderInserter inserter = null;
        BackgroundWorker reportBackgroundWorker = null;
        BackgroundWorker quotaBackgroundWorker = null;

        String folderName = null;
        String reportName = null;
        DateTime? startDate = null;
        DateTime? endDate = null;
        wsObjectType? filterType = null;
        string filterValue = null;

        String reportData = null;

        limitTimeoutState[] quotaLimits = null;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            wsAdminClient = new WsAdminClient();

            // Add our AuthHeaderInserter behavior to the client endpoint
            // this will invoke our behavior before every send so that
            // we can insert the "Authorization" HTTP header before it is sent.
            inserter = new AuthHeaderInserter();
            wsAdminClient.Endpoint.Behaviors.Add(new AuthHeaderBehavior(inserter));

            reportBackgroundWorker = new BackgroundWorker();
            reportBackgroundWorker.WorkerReportsProgress = true;
            reportBackgroundWorker.DoWork += new DoWorkEventHandler(reportBackgroundWorker_DoWork);
            reportBackgroundWorker.ProgressChanged += new ProgressChangedEventHandler(reportBackgroundWorker_ProgressChanged);
            reportBackgroundWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(reportBackgroundWorker_RunWorkerCompleted);

            quotaBackgroundWorker = new BackgroundWorker();
            quotaBackgroundWorker.DoWork += new DoWorkEventHandler(quotaBackgroundWorker_DoWork);
            quotaBackgroundWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(quotaBackgroundWorker_RunWorkerCompleted);

            foreach (wsObjectType objType in (wsObjectType[])Enum.GetValues(typeof(wsObjectType)))
            {
                ComboBoxItem cbi = new ComboBoxItem();
                cbi.Content = objType.ToString();

                cbxFilterBy.Items.Add(cbi);
            }
        }

        private void btnRunReport_Click(object sender, RoutedEventArgs e)
        {
            if (String.IsNullOrEmpty(tbxUsername.Text) || String.IsNullOrEmpty(pbxPassword.Password))
            {
                MessageBox.Show("Please enter a username and password.", "Invalid Parameter", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            if (String.IsNullOrEmpty(tbxFolderName.Text) || String.IsNullOrEmpty(tbxReportName.Text))
            {
                MessageBox.Show("Please enter a folder name and report name.", "Invalid Parameter", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            if (dpStartDate.SelectedDate == null || dpEndDate.SelectedDate == null)
            {
                MessageBox.Show("Please select a start and end date", "Invalid Parameter", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            inserter.Username = tbxUsername.Text;
            inserter.Password = pbxPassword.Password;


            folderName = tbxFolderName.Text;
            reportName = tbxReportName.Text;

            startDate = dpStartDate.SelectedDate;
            endDate = dpEndDate.SelectedDate;

            ComboBoxItem cbi = (ComboBoxItem)cbxFilterBy.SelectedValue;

            if (cbi.Content.Equals("None"))
            {
                filterType = null;
                filterValue = null;
            }
            else
            {
                filterType = (wsObjectType)Enum.Parse(typeof(wsObjectType), (string)cbi.Content);
                filterValue = tbxFilterValue.Text;
            }

            quotaBackgroundWorker.RunWorkerAsync();

            reportBackgroundWorker.RunWorkerAsync();
        }

        void reportBackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                reportBackgroundWorker.ReportProgress(1);

                runReport reqRunReport = new runReport();

                // To run a report you must specify a folder name and the name of the report.
                // These can be found in the Five9 Reporting UI where the folder name is the "section" name
                // and the report name is the name of the report.
                // Note: Both are case sensitive and should be entered exactly as they appear in the UI.
                reqRunReport.folderName = folderName;
                reqRunReport.reportName = reportName;

                // All reports require a start and end date!
                reqRunReport.criteria = new customReportCriteria();
                reqRunReport.criteria.time = new reportTimeCriteria();
                reqRunReport.criteria.time.start = startDate.Value;
                reqRunReport.criteria.time.startSpecified = true;
                reqRunReport.criteria.time.end = endDate.Value;
                reqRunReport.criteria.time.endSpecified = true;

                // You can filter reports by additional criteria.  This roughly translate to a "Where" clause
                // in a SQL query.  Such as "Where Skill = 'Inbound'"
                if (filterType != null)
                {
                    reqRunReport.criteria.reportObjects = new reportObjectList[1];
                    reqRunReport.criteria.reportObjects[0] = new reportObjectList();
                    reqRunReport.criteria.reportObjects[0].objectType = filterType.Value;
                    reqRunReport.criteria.reportObjects[0].objectTypeSpecified = true;

                    string[] filterValues = filterValue.Split(",".ToCharArray());

                    reqRunReport.criteria.reportObjects[0].objectNames = filterValues;
                }

                runReportResponse respReportResponse = wsAdminClient.runReport(reqRunReport);
                string reportId = respReportResponse.@return;

                reportBackgroundWorker.ReportProgress(34);

                bool reportBeingGenerated = true;

                while (reportBeingGenerated)
                {
                    isReportRunning reqIsReportRunning = new isReportRunning();
                    reqIsReportRunning.identifier = reportId;
                    reqIsReportRunning.timeout = 5; // in seconds;

                    isReportRunningResponse respIsReportRunning = wsAdminClient.isReportRunning(reqIsReportRunning);
                    reportBeingGenerated = respIsReportRunning.@return;
                }

                reportBackgroundWorker.ReportProgress(67);

                getReportResultCsv reqGetReportResultCsv = new getReportResultCsv();
                reqGetReportResultCsv.identifier = reportId;

                getReportResultCsvResponse respGetReportResultCsv = wsAdminClient.getReportResultCsv(reqGetReportResultCsv);
                reportData = respGetReportResultCsv.@return;

                reportBackgroundWorker.ReportProgress(100);
            }
            catch (Exception exc)
            {
                reportBackgroundWorker.ReportProgress(100);
                reportData = exc.Message;
            }
        }

        void reportBackgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            pbStatus.Value = e.ProgressPercentage;
        }

        void reportBackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            DataTable dt = new DataTable();

            // Kent Boogaart developed an awesome CSV library available on Codeplex that we'll use to
            // parse the CSV data so we can turn it into a DataTable to display in a DataGrid

            CsvReader csvReader = new CsvReader(new StringReader(reportData));
            
            HeaderRecord headerRecord = csvReader.ReadHeaderRecord();

            foreach (string col in headerRecord.Values)
            {
                dt.Columns.Add(col);
            }

            foreach (DataRecord dataRecord in csvReader.DataRecords)
            {
                DataRow dataRow = dt.NewRow();

                foreach (string col in dataRecord.HeaderRecord.Values)
                {
                    dataRow[col] = dataRecord[col];
                }

                dt.Rows.Add(dataRow);
            }

            dgReportData.ItemsSource = dt.DefaultView;

            pbStatus.Value = 0;
        }

        void quotaBackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            getCallCountersState req = new getCallCountersState();

            quotaLimits = wsAdminClient.getCallCountersState(req);
        }

        void quotaBackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {

            string quotas = "";

            foreach (limitTimeoutState lts in quotaLimits)
            {
                    quotas += "Timeout: " + lts.timeout + " seconds\r\n";

                    foreach (callCounterState ccs in lts.callCounterStates)
                    {
                        // There are many quota limits, but we are only interested in the ones dealing with reports.
                        if (ccs.operationType.Equals(apiOperationType.ReportRequest) ||
                             ccs.operationType.Equals(apiOperationType.RetrieveReport))
                        {
                            quotas += "    Operation: " + ccs.operationType + ", Limit[" + ccs.limit + "], Value[" + ccs.value + "]" + ((ccs.value >= ccs.limit) ? " ---> Exceeded" : "") + "\r\n";
                        }
                    }
            }

            tbxQuotaLimits.Text = quotas;
        }
    }
}
