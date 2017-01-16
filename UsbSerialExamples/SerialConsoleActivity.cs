/* Copyright 2011-2013 Google Inc.
 * Copyright 2013 mike wakerly <opensource@hoho.com>
 * Copyright 2015 Yasuyuki Hamada <yasuyuki_hamada@agri-info-design.com>
 *
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 *
 * This library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this library; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301,
 * USA.
 *
 * Project home page: https://github.com/ysykhmd/usb-serial-for-xamarin-android
 * 
 * This project is based on usb-serial-for-android and ported for Xamarin.Android.
 * Original project home page: https://github.com/mik3y/usb-serial-for-android
 */

using System;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;

using Aid.UsbSerial;
using System.Threading;
using Android.Content.PM;

namespace UsbSerialExamples
{
    [Activity(Label = "@string/app_name")]

    public class SerialConsoleActivity : Activity
    {
        const string TAG = "SerialConsoleActivity";

        enum TEST_STATUS { STANDBY, TESTING }
        enum TEST_PERIOD : Int16{ SEC10 = 10, SEC30 = 30, MIN01 = 60, MIN03 = 180, MIN05 = 300, MIN10 = 600, MIN30 = 1800 }

        const int DEFAULT_TEST_PERIOD = (int)TEST_PERIOD.MIN01;
        const int DEFAULT_TRANSFAR_RATE = 115200;

        public static UsbSerialPort UseUsbSerialPort = null;

        volatile TEST_STATUS ActivityStatus;
        ScrollView ScrollView;
        CheckData CheckInstance;
        CheckData CheckNmeaCheckSumInstance;
        CheckData CheckCyclic00ToFFInstance;
        CheckData CheckCyclic41To5AInstance;
        CheckData CheckSendDataInstance;
        CheckData CheckSendDataCdcInstance;



        String DeviceName;
        Boolean IsCdcDevice;
        Timer UpdateTestResultTimer;
        Timer TestMainTimer;
        Int32 TestTimePeriod = DEFAULT_TEST_PERIOD;
        Int32 TestTimeRemain;
        int TransfarRate;
        volatile int TestModeResourceId;

        TextView TestModeTextView;
        TextView TitleTextView;
        TextView TransfarRateTitleTextView;
        TextView TransfarRateValueTextView;
        TextView ActivityStatusTextView;
        TextView TestTimeTextView;
        TextView RemainTimeTextView;
        TextView TitleGoodTextView;
        TextView GoodCountTextView;
        TextView TitleErrorTextView;
        TextView ErrorCountTextView;
        TextView TitleTotalTextView;
        TextView TotalCountTextView;

        TextView DumpTextView;

        IMenuItem TestPeriodMenuItem;
        IMenuItem TransfarRateMenuItem;
        IMenuItem TestModeMenuItem;

        Button ModeChangeButton;

        byte[] readDataBuffer = new byte[16384];


        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.serial_console);

            ActionBar.SetTitle(Resource.String.test_console_title);

            ActivityStatus = TEST_STATUS.STANDBY;

            TransfarRate = DEFAULT_TRANSFAR_RATE;

            DeviceName = UseUsbSerialPort.GetType().Name;

            if (0 == String.Compare(DeviceName, 0, "Cdc", 0, 3))
            {
                IsCdcDevice = true;
            }
            else
            {
                IsCdcDevice = false;
            }

            TestModeTextView = (TextView)FindViewById(Resource.Id.test_mode);
            TitleTextView = (TextView)FindViewById(Resource.Id.serial_device_name);

            TransfarRateTitleTextView = (TextView)FindViewById(Resource.Id.title_transfar_rate);
            TransfarRateValueTextView = (TextView)FindViewById(Resource.Id.transfar_rate_value);
            if (IsCdcDevice)
            {
                TransfarRateTitleTextView.Enabled = false;
                TransfarRateValueTextView.Enabled = false;
            }
            else
            {
                TransfarRateValueTextView.SetText(TransfarRate.ToString(), TextView.BufferType.Normal);
            }

            DumpTextView = (TextView)FindViewById(Resource.Id.consoleText);
            ScrollView = (ScrollView)FindViewById(Resource.Id.demoScroller);

            ActivityStatusTextView = (TextView)FindViewById(Resource.Id.activity_status);
            ActivityStatusTextView.SetText(Resource.String.activity_status_standby);

            TestTimeTextView = (TextView)FindViewById(Resource.Id.test_time);
            TestTimeTextView.SetText(string.Format("{0:0#}:{1:0#}", TestTimePeriod / 60, TestTimePeriod % 60), TextView.BufferType.Normal);

            RemainTimeTextView = (TextView)FindViewById(Resource.Id.remain_time);
            RemainTimeTextView.SetText(Resource.String.remain_time_initial);

            TitleGoodTextView = (TextView)FindViewById(Resource.Id.title_good);
            GoodCountTextView = (TextView)FindViewById(Resource.Id.good_count);
            TitleErrorTextView = (TextView)FindViewById(Resource.Id.title_error);
            ErrorCountTextView = (TextView)FindViewById(Resource.Id.error_count);
            TitleTotalTextView = (TextView)FindViewById(Resource.Id.title_total);
            TotalCountTextView = (TextView)FindViewById(Resource.Id.total_count);

            ModeChangeButton = (Button)FindViewById(Resource.Id.modeChange);
            ModeChangeButton.Click += ModeChangeButtonHandler;

            UseUsbSerialPort.DataReceivedEventLinser += DataReceivedHandler;

            CheckNmeaCheckSumInstance = new CheckNmeaCheckSum(this);
            CheckCyclic00ToFFInstance = new CheckCyclic00ToFF(this);
            CheckCyclic41To5AInstance = new CheckCyclic41To5A(this);
            CheckSendDataInstance = new CheckSendData(this);
            CheckSendDataCdcInstance = new CheckSendDataCdc(this);

            CheckInstance = CheckCyclic00ToFFInstance;
            TestModeTextView.SetText(CheckInstance.TestMode, TextView.BufferType.Normal);
        }

        public override Boolean OnCreateOptionsMenu(IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.test_console_menu, menu);

            TestPeriodMenuItem = menu.FindItem(Resource.Id.menu_test_period);
            TransfarRateMenuItem = menu.FindItem(Resource.Id.menu_transfer_rate);
            TestModeMenuItem = menu.FindItem(Resource.Id.menu_test_mode);
            if (IsCdcDevice)
            {
                TransfarRateMenuItem.SetEnabled(false);
            }

            return base.OnPrepareOptionsMenu(menu);
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            if (CheckTestPeriodMenu(item))
            {
                return true;
            }

            if (CheckTransferRateMenu(item))
            {
                return true;
            }

            if (CheckTestModeMenu(item))
            {
                return true;
            }

            return base.OnOptionsItemSelected(item);
        }

        bool CheckTestPeriodMenu(IMenuItem item)
        {
            TEST_PERIOD test_period;

            switch (item.ItemId)
            {
                case Resource.Id.test_period_10sec:
                    test_period = TEST_PERIOD.SEC10;
                    break;
                case Resource.Id.test_period_30sec:
                    test_period = TEST_PERIOD.SEC30;
                    break;
                case Resource.Id.test_period_1min:
                    test_period = TEST_PERIOD.MIN01;
                    break;
                case Resource.Id.test_period_3min:
                    test_period = TEST_PERIOD.MIN03;
                    break;
                case Resource.Id.test_period_5min:
                    test_period = TEST_PERIOD.MIN05;
                    break;
                case Resource.Id.test_period_10min:
                    test_period = TEST_PERIOD.MIN10;
                    break;
                case Resource.Id.test_period_30min:
                    test_period = TEST_PERIOD.MIN30;
                    break;
                default:
                    return false;
            }
            TestTimePeriod = (Int16)test_period;
            RunOnUiThread(() =>
                TestTimeTextView.SetText(string.Format("{0:0#}:{1:0#}", TestTimePeriod / 60, TestTimePeriod % 60), TextView.BufferType.Normal)
            );
            return true;
        }

        bool CheckTransferRateMenu(IMenuItem item)
        {
            switch (item.ItemId)
            {
                case Resource.Id.transfer_rate_1200:
                    TransfarRate = 1200;
                    break;
                case Resource.Id.transfer_rate_2400:
                    TransfarRate = 2400;
                    break;
                case Resource.Id.transfer_rate_4800:
                    TransfarRate = 4800;
                    break;
                case Resource.Id.transfer_rate_9600:
                    TransfarRate = 9600;
                    break;
                case Resource.Id.transfer_rate_19200:
                    TransfarRate = 19200;
                    break;
                case Resource.Id.transfer_rate_38400:
                    TransfarRate = 38400;
                    break;
                case Resource.Id.transfer_rate_57600:
                    TransfarRate = 57600;
                    break;
                case Resource.Id.transfer_rate_115200:
                    TransfarRate = 115200;
                    break;
                default:
                    return false;
            }
            UseUsbSerialPort.Baudrate = TransfarRate;
            UseUsbSerialPort.ResetParameters();
            RunOnUiThread(() =>
                TransfarRateValueTextView.SetText(TransfarRate.ToString(), TextView.BufferType.Normal)
            );
            return true;
        }

        bool CheckTestModeMenu(IMenuItem item)
        {
            switch (item.ItemId)
            {
                case Resource.Id.test_mode_nmew_check_sum:
                    TestModeResourceId = Resource.Id.test_mode_nmew_check_sum;
                    TitleGoodTextView.Visibility = ViewStates.Visible;
                    GoodCountTextView.Visibility = ViewStates.Visible;
                    TitleErrorTextView.Visibility = ViewStates.Visible;
                    ErrorCountTextView.Visibility = ViewStates.Visible;
                    TitleTotalTextView.SetText(Resource.String.title_total_receive);
                    SetTransferRateDisplayState();
                    CheckInstance = CheckNmeaCheckSumInstance;
                    break;
                case Resource.Id.test_mode_cyclic_0x00_to_0xff:
                    TestModeResourceId = Resource.Id.test_mode_cyclic_0x00_to_0xff;
                    TitleGoodTextView.Visibility = ViewStates.Visible;
                    GoodCountTextView.Visibility = ViewStates.Visible;
                    TitleErrorTextView.Visibility = ViewStates.Visible;
                    ErrorCountTextView.Visibility = ViewStates.Visible;
                    TitleTotalTextView.SetText(Resource.String.title_total_receive);
                    SetTransferRateDisplayState();
                    CheckInstance = CheckCyclic00ToFFInstance;
                    break;
                case Resource.Id.test_mode_cyclic_0x41_to_0x5A:
                    TestModeResourceId = Resource.Id.test_mode_cyclic_0x41_to_0x5A;
                    TitleGoodTextView.Visibility = ViewStates.Visible;
                    GoodCountTextView.Visibility = ViewStates.Visible;
                    TitleErrorTextView.Visibility = ViewStates.Visible;
                    ErrorCountTextView.Visibility = ViewStates.Visible;
                    TitleTotalTextView.SetText(Resource.String.title_total_receive);
                    SetTransferRateDisplayState();
                    CheckInstance = CheckCyclic41To5AInstance;
                    break;
                case Resource.Id.test_mode_send_data:
                    TestModeResourceId = Resource.Id.test_mode_send_data;
                    TitleGoodTextView.Visibility = ViewStates.Invisible;
                    GoodCountTextView.Visibility = ViewStates.Invisible;
                    TitleErrorTextView.Visibility = ViewStates.Invisible;
                    ErrorCountTextView.Visibility = ViewStates.Gone;
                    TitleTotalTextView.SetText(Resource.String.title_total_send);
                    SetTransferRateDisplayState();
                    CheckInstance = CheckSendDataInstance;
                    break;
                case Resource.Id.test_mode_send_data_cdc:
                    TestModeResourceId = Resource.Id.test_mode_send_data_cdc;
                    TitleGoodTextView.Visibility = ViewStates.Visible;
                    GoodCountTextView.Visibility = ViewStates.Visible;
                    TitleErrorTextView.Visibility = ViewStates.Visible;
                    ErrorCountTextView.Visibility = ViewStates.Visible;
                    TitleTotalTextView.SetText(Resource.String.title_total_send);
                    SetTransferRateDisplayState();
                    CheckInstance = CheckSendDataCdcInstance;
                    break;

                default:
                    return false;
            }
            RunOnUiThread(() =>
                TestModeTextView.SetText(CheckInstance.TestMode, TextView.BufferType.Normal)
            );
            return true;

        }

        void ModeChangeButtonHandler(object sender, EventArgs e)
        {
            if (ActivityStatus == TEST_STATUS.STANDBY)
            {
                StartTest();
            }
            else
            {
                CancelTest();
            }
        }

        void StartTest()
        {
            Window.AddFlags(WindowManagerFlags.KeepScreenOn);
            CheckInstance.ResetProc();
            ActivityStatusTextView.SetText(Resource.String.activity_status_testing);
            ModeChangeButton.SetText(Resource.String.test_cancel);
            TestTimeRemain = TestTimePeriod;
            TestPeriodMenuItem.SetEnabled(false);
            TransfarRateMenuItem.SetEnabled(false);
            TestModeMenuItem.SetEnabled(false);
            Log.Debug(TAG, "Test started ");

            string trancefarRate;
            if (!IsCdcDevice)
            {
                trancefarRate = TransfarRate.ToString();
            }
            else
            {
                trancefarRate = "CDC";
            }

            UpdateTestInfo("Test start  : " + DateTime.Now.ToString("HH:mm:ss.fff") + "/" + trancefarRate + "\n");
            StartTestMainTimer();
            StartUpdateTestResultTimer();
            UseUsbSerialPort.ResetReadBuffer();
            ((CheckData)CheckInstance).StartProc();
            ActivityStatus = TEST_STATUS.TESTING;
        }

        void CancelTest()
        {
            ActivityStatus = TEST_STATUS.STANDBY;
            UpdateTestResultTimer.Dispose();
            TestMainTimer.Dispose();
            ActivityStatusTextView.SetText(Resource.String.activity_status_standby);
            ModeChangeButton.SetText(Resource.String.test_start);
            TestPeriodMenuItem.SetEnabled(true);
            if (!IsCdcDevice || TestModeResourceId == Resource.Id.test_mode_send_data_cdc)
            {
                TransfarRateMenuItem.SetEnabled(true);
            }
            TestModeMenuItem.SetEnabled(true);
            Log.Debug(TAG, "Test canceld : Error count" + CheckInstance.ErrorCountString);
            if (TestModeResourceId == Resource.Id.test_mode_send_data)
            {
                ((CheckSendData)CheckInstance).StopSendData();
            }

            if (TestModeResourceId == Resource.Id.test_mode_send_data_cdc)
            {
                ((CheckSendDataCdc)CheckInstance).StopSendData();
            }
            UpdateTestInfo("Test cancel : " + DateTime.Now.ToString("HH:mm:ss.fff/") + CheckInstance.GoodCountString + "-" + CheckInstance.ErrorCountString + "-" + CheckInstance.TotalCountString + "\n");
            Window.ClearFlags(WindowManagerFlags.KeepScreenOn);
        }

        volatile Object FinishTestMainTimerHandlerLock = new Object();
        volatile Object UpdateTestResultDisplayLock = new Object();
        void FinishTestMainTimerHandler(Object sender)
        {
            lock (FinishTestMainTimerHandlerLock)
            {
                ActivityStatus = TEST_STATUS.STANDBY;
                UpdateTestResultTimer.Dispose();
                TestMainTimer.Dispose();
                lock (UpdateTestResultDisplayLock)
                {
                    RunOnUiThread(() =>
                    {
                        ActivityStatusTextView.SetText(Resource.String.activity_status_standby);
                        RemainTimeTextView.SetText(Resource.String.remain_time_normal_end);
                        if (TestModeResourceId != Resource.Id.test_mode_send_data)
                        {
                            GoodCountTextView.SetText(CheckInstance.GoodCountString, TextView.BufferType.Normal);
                            ErrorCountTextView.SetText(CheckInstance.ErrorCountString, TextView.BufferType.Normal);
                        }
                        TotalCountTextView.SetText(CheckInstance.TotalCountString, TextView.BufferType.Normal);
                        ModeChangeButton.SetText(Resource.String.test_start);
                        TestPeriodMenuItem.SetEnabled(true);
                        if (!IsCdcDevice || TestModeResourceId == Resource.Id.test_mode_send_data_cdc)
                        {
                            TransfarRateMenuItem.SetEnabled(true);
                        }
                        TestModeMenuItem.SetEnabled(true);
                        // 以下の３行は、なぜか UI スレッド実行しないと Activity を異常終了させる
                        UpdateTestInfo("Test finish : " + DateTime.Now.ToString("HH:mm:ss.fff/") + CheckInstance.GoodCountString + "-" + CheckInstance.ErrorCountString + "-" + CheckInstance.TotalCountString + "\n");
                        Log.Debug(TAG, "Test finished : Error count " + CheckInstance.ErrorCountString);
                        Window.ClearFlags(WindowManagerFlags.KeepScreenOn);
                    });
                }
                if (TestModeResourceId == Resource.Id.test_mode_send_data)
                {
                    ((CheckSendData)CheckInstance).StopSendData();
                }
                if (TestModeResourceId == Resource.Id.test_mode_send_data_cdc)
                {
                    ((CheckSendDataCdc)CheckInstance).StopSendData();
                }
                UpdateTestResultDisplay(null);
            }
        }

        void StartTestMainTimer()
        {
            TestMainTimer = new Timer(FinishTestMainTimerHandler, this, TestTimePeriod * 1000, Timeout.Infinite);
        }

        void StartUpdateTestResultTimer()
        {
            UpdateTestResultTimer = new Timer(UpdateTestResultDisplay, this, 0, 1000);
        }

        void UpdateTestResultDisplay(Object sender)
        {
            lock(UpdateTestResultDisplayLock)
            {
                RunOnUiThread(() => {
                    RemainTimeTextView.SetText(string.Format("{0:0#}:{1:0#}", TestTimeRemain / 60, TestTimeRemain % 60), TextView.BufferType.Normal);
                    if (TestModeResourceId != Resource.Id.test_mode_send_data)
                    {
                        GoodCountTextView.SetText(CheckInstance.GoodCountString, TextView.BufferType.Normal);
                        ErrorCountTextView.SetText(CheckInstance.ErrorCountString, TextView.BufferType.Normal);
                    }
                    TotalCountTextView.SetText(CheckInstance.TotalCountString, TextView.BufferType.Normal);
                    TestTimeRemain -= 1;
                    if (TestTimeRemain < 0)
                    {
                        TestTimeRemain = 0;
                    }
                });
            }
        }

        protected override void OnPause()
        {
            base.OnPause();
            if (TestModeResourceId == Resource.Id.test_mode_send_data)
            {
                ((CheckSendData)CheckInstance).StopSendData();
            }

            if (TestModeResourceId == Resource.Id.test_mode_send_data_cdc)
            {
                ((CheckSendDataCdc)CheckInstance).StopSendData();
            }

            if (UseUsbSerialPort != null)
            {
                try
                {
                    UseUsbSerialPort.Close();
                    UseUsbSerialPort.DataReceivedEventLinser -= DataReceivedHandler;
                }
                catch (Exception)
                {
                    // Ignore.
                }
            }
            Finish();
        }

        protected override void OnResume() {
			base.OnResume ();
			Log.Debug (TAG, "Resumed, port=" + UseUsbSerialPort);
			if (UseUsbSerialPort == null) {
				TitleTextView.Text = "No serial device.";
			} else {
				try {
                    UseUsbSerialPort.Baudrate = TransfarRate;
					UseUsbSerialPort.Open ();
                    TransfarRateValueTextView.SetText(TransfarRate.ToString(), TextView.BufferType.Normal);
                } catch (Exception e) {
					Log.Error (TAG, "Error setting up device: " + e.Message, e);
					TitleTextView.Text = "Error opening device: " + e.Message;
					try {
						UseUsbSerialPort.Close ();
					} catch (Exception) {
						// Ignore.
					}
					UseUsbSerialPort = null;
					return;
				}
				TitleTextView.Text = "Serial device: " + DeviceName;
			}
            RequestedOrientation = ScreenOrientation.Portrait;
        }

        /*
         * このイベントハンドラはかなり頻繁に呼び出されるため、
         * ガベージを無暗に増やさないように、関数内の自動変数は、すべて関数外で宣言する
         */
        int DataReceivedHandlerLoopCounter;
        volatile Object dataReceivedHandlerLockObject = new Object();
        int DataRecivedHandlerLength;
        private void DataReceivedHandler(object sender, DataReceivedEventArgs e)
        {
            lock (dataReceivedHandlerLockObject)
            {
                DataRecivedHandlerLength = e.Port.Read(readDataBuffer, 0);
                if (ActivityStatus == TEST_STATUS.TESTING)
                {
                     for (DataReceivedHandlerLoopCounter = 0; DataReceivedHandlerLoopCounter < DataRecivedHandlerLength; DataReceivedHandlerLoopCounter++)
                    {
                        CheckInstance.ProcData(readDataBuffer[DataReceivedHandlerLoopCounter]);
                    }
                }
            }
        }

        public void UpdateTestInfo(String msg)
        {
            DumpTextView.Append(msg);
            ScrollView.SmoothScrollTo(0, DumpTextView.Bottom);
            Log.Info(TAG, "Test info : " + msg);
        }

        /**
         * Starts the activity, using the supplied driver instance.
         *
         * @param context
         * @param driver
         */
        static public void Show(Context context, UsbSerialPort port)
        {
            UseUsbSerialPort = port;
            Intent intent = new Intent(context, typeof(SerialConsoleActivity));
            intent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.NoHistory);
            context.StartActivity(intent);
        }

        private void SetTransferRateDisplayState()
        {
            if (IsCdcDevice && TestModeResourceId != Resource.Id.test_mode_send_data_cdc)
            {
                TransfarRateTitleTextView.Enabled = false;
                TransfarRateValueTextView.Enabled = false;
                TransfarRateMenuItem.SetEnabled(false);
            }
            else
            {
                TransfarRateTitleTextView.Enabled = true;
                TransfarRateValueTextView.Enabled = true;
                TransfarRateValueTextView.SetText(TransfarRate.ToString(), TextView.BufferType.Normal);
                TransfarRateMenuItem.SetEnabled(true);
            }
        }
    }

    /*
     * テストクラスの仮想クラス
     */
    abstract class CheckData
    {
        abstract public string TestMode { get; }
        abstract public string GoodCountString { get; }
        abstract public string ErrorCountString { get; }
        abstract public string TotalCountString { get; }
        abstract public void ProcData(byte data);
        abstract public void ResetProc();
        abstract public void StartProc();
    }

    /*
     * データ受信のテスト
     * 　・設定された速度・時間データを受信し、NMEA0183 のデータとしてチェックサムをチェックする
     */
    class CheckNmeaCheckSum : CheckData
    {
        enum STATE : byte { IDLE, DATA, SUM1, SUM2 };

        public override string TestMode { get { return "NMEA Check Sum"; } }
        public override string GoodCountString  { get { return goodCount. ToString(); } }
        public override string ErrorCountString { get { return errorCount.ToString(); } }
        public override string TotalCountString { get { return totalCount.ToString(); } }
        STATE state = STATE.IDLE;
        volatile int goodCount = 0;
        volatile int errorCount = 0;
        volatile int totalCount = 0;
        byte calcSum = 0;
        byte getSum = 0;
        byte firstCharValue;
        byte secondCharValue;
        volatile Object thisLock = new Object();
        SerialConsoleActivity parentActivity;

        public CheckNmeaCheckSum(SerialConsoleActivity activity)
        {
            parentActivity = activity;
        }

        public override void ResetProc()
        {
            goodCount = 0;
            errorCount = 0;
            totalCount = 0;
        }

        public override void StartProc()
        {
            state = STATE.IDLE;
        }

        public override void ProcData(byte data)
        {
            switch (state)
            {
                case STATE.IDLE:
                    if (data == '$')
                    {
                        state = STATE.DATA;
                        calcSum = 0;
                    }
                    break;
                case STATE.DATA:
                    if (data == '*')
                    {
                        state = STATE.SUM1;
                    }
                    else
                    {
                        calcSum ^= data;
                    }
                    break;
                case STATE.SUM1:
                    firstCharValue = CharToHex(data);
                    state = STATE.SUM2;
                    break;
                case STATE.SUM2:
                    secondCharValue = CharToHex(data);
                    getSum = (byte)(firstCharValue * 16 + secondCharValue);
                    state = STATE.IDLE;
                    lock(thisLock)
                    {
                        if (calcSum == getSum)
                        {
                            goodCount += 1;
                        }
                        else
                        {
                            errorCount += 1;
                        }
                        totalCount += 1;
                    }
                    break;
            }
        }

        byte CharToHex(byte c)
        {
            if (c >= '0' && c <= '9')
            {
                return (byte)(c - '0');
            }
            else
            {
                return (byte)((c & 0x0f) + 9);
            }
        }
    }

    /*
     * データ受信のテスト
    * 　・設定された速度・時間データを受信し、0x00-0xff の循環データか否かをチェックする
    */
    class CheckCyclic00ToFF : CheckData
    {
        enum STATE : byte { IDLE, DATA };

        public override string TestMode { get { return "Cyclic 0x00 to 0xFF"; } }
        public override string GoodCountString { get { return goodCount.ToString(); } }
        public override string ErrorCountString { get { return errorCount.ToString(); } }
        public override string TotalCountString { get { return totalCount.ToString(); } }
        STATE state = STATE.IDLE;
        volatile int goodCount = 0;
        volatile int errorCount = 0;
        volatile int totalCount = 0;

        byte LastData;
        int dataCount;
        SerialConsoleActivity parentActivity;

        public CheckCyclic00ToFF(SerialConsoleActivity activity)
        {
            parentActivity = activity;
        }

        public override void ResetProc()
        {
            goodCount = 0;
            errorCount = 0;
            totalCount = 0;
            dataCount = 0;
        }

        public override void StartProc()
        {
            state = STATE.IDLE;
        }

        public override void ProcData(byte data)
        {
            dataCount += 1;
            switch (state)
            {
                case STATE.IDLE:
                    if (0x00 == data)
                    {
                        state = STATE.DATA;
                        LastData = data;
                    }
                    break;
                case STATE.DATA:
                    if (0x00 == data)
                    {
                        if (0xFF == LastData)
                        {
                            goodCount += 1;
                            totalCount += 1;
                            LastData = data;
                        }
                        else
                        {
                            errorCount += 1;
                            totalCount += 1;
                            string msg = "Error B: " + dataCount.ToString("x8") + ":" + DateTime.Now.ToString("HH:mm:ss.fff") + ":" + LastData.ToString("x2") + "-" + data.ToString("x2") + "\n";
                            parentActivity.RunOnUiThread(() =>
                                {
                                    parentActivity.UpdateTestInfo(msg);
                                }
                            );
                            LastData = data;
                        }
                    }
                    else
                    {
                        if (data - 1 == LastData)
                        {
                            LastData = data;
                        }
                        else
                        {
                            errorCount += 1;
                            totalCount += 1;
                            state = STATE.IDLE;
                            string msg = "Error A: " + dataCount.ToString("x8") + ":" + DateTime.Now.ToString("HH:mm:ss.fff") + ":" + LastData.ToString("x2") + "-" + data.ToString("x2") + "\n";
                            parentActivity.RunOnUiThread(() =>
                                {
                                    parentActivity.UpdateTestInfo(msg);
                                }
                            );
                        }
                    }
                    break;
            }
        }
    }

    /*
     * データ送信のテスト
     * 　・設定された速度・時間 0x00-0xff の循環データを送り続ける
     * 　・データ送信のテストなので、ProcData() の中身はない
     */ 　
    class CheckSendData : CheckData
    {
        public override string TestMode { get { return "Send Data"; } }
        public override string GoodCountString { get { return goodCount.ToString(); } }
        public override string ErrorCountString { get { return errorCount.ToString(); } }
        public override string TotalCountString { get { return totalCount.ToString(); } }

        volatile int goodCount = 0;
        volatile int errorCount = 0;
        volatile int totalCount = 0;

        volatile bool threadContinueFlag;

        public override void ProcData(byte data) { }

        public CheckSendData(SerialConsoleActivity activity) { }

        public override void ResetProc()
        {
            totalCount = 0;
        }

        public override void StartProc()
        {
            var thread = new Thread(() =>
            {
                byte[] sendDataBuf = new byte[256];

                for (int i = 0; i < 256; i++)
                {
                    sendDataBuf[i] = (byte)i;
                }

                threadContinueFlag = true;
                while (threadContinueFlag)
                {
                    SerialConsoleActivity.UseUsbSerialPort.Write(sendDataBuf, 1000);
                    totalCount += 1;
                }
            });
            thread.Start();
        }

        public void StopSendData()
        {
            threadContinueFlag = false;
        }
    }

    /*
     * データ受信のテスト
     * 　・設定された速度・時間データを受信し、0x41-0x5A(A-Z) の循環データか否かをチェックする
     */
    class CheckCyclic41To5A : CheckData
    {
        enum STATE : byte { IDLE, DATA };

        public override string TestMode { get { return "Cyclic 0x41 to 0x5A"; } }
        public override string GoodCountString { get { return goodCount.ToString(); } }
        public override string ErrorCountString { get { return errorCount.ToString(); } }
        public override string TotalCountString { get { return totalCount.ToString(); } }
        STATE state = STATE.IDLE;
        volatile int goodCount = 0;
        volatile int errorCount = 0;
        volatile int totalCount = 0;
        int dataCount;

        byte LastData;
        SerialConsoleActivity parentActivity;

        public CheckCyclic41To5A(SerialConsoleActivity activity)
        {
            parentActivity = activity;
        }

        public override void ResetProc()
        {
            goodCount = 0;
            errorCount = 0;
            totalCount = 0;
            dataCount = 0;
        }

        public override void StartProc()
        {
            state = STATE.IDLE;
        }

        public override void ProcData(byte data)
        {
            dataCount += 1;
            switch (state)
            {
                case STATE.IDLE:
                    if (0x41 == data)
                    {
                        state = STATE.DATA;
                        LastData = data;
                    }
                    break;
                case STATE.DATA:
                    if (0x41 == data)
                    {
                        if (0x5A == LastData)
                        {
                            goodCount += 1;
                            totalCount += 1;
                            LastData = data;
                        }
                        else
                        {
                            errorCount += 1;
                            totalCount += 1;
                            LastData = data;
                            string msg = "Error B: " + dataCount.ToString("x8") + DateTime.Now.ToString("HH:mm:ss.fff") + ":" + LastData.ToString("x2") + "-" + data.ToString("x2") + "\n";
                            parentActivity.RunOnUiThread(() =>
                            {
                                parentActivity.UpdateTestInfo(msg);
                            }
                            );
                        }

                    }
                    else
                    {
                        if (data - 1 == LastData)
                        {
                            LastData = data;
                        }
                        else
                        {
                            errorCount += 1;
                            totalCount += 1;
                            state = STATE.IDLE;
                            string msg = "Error A: " + dataCount.ToString("x8") + DateTime.Now.ToString("HH:mm:ss.fff") + ":" + LastData.ToString("x2") + "-" + data.ToString("x2") + "\n";
                            parentActivity.RunOnUiThread(() =>
                            {
                                parentActivity.UpdateTestInfo(msg);
                            }
                            );
                        }
                    }
                    break;
            }
        }
    }

    /*
     * データ送信のテスト(CDC 用、相手方に Arduino UNO R3 を使う)
     * 　・設定された速度・時間 0x00-0xff の循環データを送り続ける
     * 　・データ送信のテストなので、ProcData() の中身はない
     * 　・Arduino側で 0x00-0xFF の 256byte を正常に受信できたか否かをレスポンスの形でもらう(Arduino 側でエラーを表示する機能がないため)
     * 　・Arduinoの CDC はボーレートの設定が必要
     */
    class CheckSendDataCdc : CheckData
    {
        enum STATE : byte { IDLE, DATA };

        public override string TestMode { get { return "Send Data(Arduino)"; } }
        public override string GoodCountString { get { return goodCount.ToString(); } }
        public override string ErrorCountString { get { return errorCount.ToString(); } }
        public override string TotalCountString { get { return totalCount.ToString(); } }
        STATE state = STATE.IDLE;
        volatile int goodCount = 0;
        volatile int errorCount = 0;
        volatile int totalCount = 0;

        string message;

        volatile bool threadContinueFlag;

        public override void ProcData(byte data)
        {
            switch (state)
            {
                case STATE.IDLE:
                    message = "";
                    message += (char)data;
                    state = STATE.DATA;
                    break;
                case STATE.DATA:
                    if (data == 10)
                    {
                        state = STATE.IDLE;
                        if (message.Substring(0, 1) == "G")
                        {
                            goodCount += 1;
                        }
                        else
                        {
                            errorCount += 1;
                        }
                    }
                    else
                    {
                        message += (char)data;
                    }
                    break;
            }
        }

        public CheckSendDataCdc(SerialConsoleActivity activity) { }

        public override void ResetProc()
        {
            totalCount = 0;
            goodCount = 0;
            errorCount = 0;
        }

        public override void StartProc()
        {
            var thread = new Thread(() =>
            {
                byte[] sendDataBuf = new byte[256];

                for (int i = 0; i < 256; i++)
                {
                    sendDataBuf[i] = (byte)i;
                }

                threadContinueFlag = true;
                while (threadContinueFlag)
                {
                    totalCount += 1;
                    SerialConsoleActivity.UseUsbSerialPort.Write(sendDataBuf, 1000);
                }
            });
            thread.Start();
        }

        public void StopSendData()
        {
            threadContinueFlag = false;
        }
    }
}

