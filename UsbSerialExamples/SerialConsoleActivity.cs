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

namespace UsbSerialExamples
{
    [Activity(Label = "@string/app_name")]

    public class SerialConsoleActivity : Activity
    {
        const string TAG = "SerialConsoleActivity";

        enum TEST_STATUS { STANDBY, TESTING }
        enum TEST_PERIOD : Int16{ SEC30 = 30, MIN01 = 60, MIN03 = 180, MIN05 = 300, MIN10 = 600 }

        static UsbSerialPort mUsbSerialPort = null;

        TEST_STATUS ActivityStatus;
        TextView TitleTextView;
        TextView DumpTextView;
        ScrollView ScrollView;
        CheckNmeaCheckSum CheckInstance;

        Timer UpdateTestResultTimer;
        Timer TestMainTimer;
        Int32 TestTimePeriod = 10; // 5 * 60;
        Int32 TestTimeRemain;

        TextView ActivityStatusTextView;
        TextView TestTimeTextView;
        TextView RemainTimeTextView;
        TextView GoodCountTextView;
        TextView ErrorCountTextView;
        TextView TotalCountTextView;
        Button ModeChangeButton;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.serial_console);

            ActionBar.SetTitle(Resource.String.test_console_title);

            ActivityStatus = TEST_STATUS.STANDBY;

            TitleTextView = (TextView)FindViewById(Resource.Id.demoTitle);
            DumpTextView = (TextView)FindViewById(Resource.Id.consoleText);
            ScrollView = (ScrollView)FindViewById(Resource.Id.demoScroller);

            ActivityStatusTextView = (TextView)FindViewById(Resource.Id.activity_status);
            ActivityStatusTextView.SetText(Resource.String.activity_status_standby);

            TestTimeTextView = (TextView)FindViewById(Resource.Id.test_time);
            TestTimeTextView.SetText(string.Format("{0:0#}:{1:0#}", TestTimePeriod / 60, TestTimePeriod % 60), TextView.BufferType.Normal);

            RemainTimeTextView = (TextView)FindViewById(Resource.Id.remain_time);
            RemainTimeTextView.SetText(Resource.String.remain_time_initial);

            GoodCountTextView = (TextView)FindViewById(Resource.Id.good_count);
            ErrorCountTextView = (TextView)FindViewById(Resource.Id.error_count);
            TotalCountTextView = (TextView)FindViewById(Resource.Id.total_count);

            ModeChangeButton = (Button)FindViewById(Resource.Id.modeChange);
            ModeChangeButton.Click += ModeChangeButtonHandler;

            CheckInstance = new CheckNmeaCheckSum();

            mUsbSerialPort.DataReceivedEventLinser += DataReceivedHandler;
        }

        public override Boolean OnCreateOptionsMenu(IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.test_console_menu, menu);
            return base.OnPrepareOptionsMenu(menu);
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            TEST_PERIOD test_period;
            switch (item.ItemId)
            {
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
                default:
                    return base.OnOptionsItemSelected(item);
            }
            TestTimePeriod = (Int16)test_period;
            RunOnUiThread(() =>
            {
                TestTimeTextView.SetText(string.Format("{0:0#}:{1:0#}", TestTimePeriod / 60, TestTimePeriod % 60), TextView.BufferType.Normal);
            });
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
            CheckInstance.ResetProc();
            ActivityStatus = TEST_STATUS.TESTING;
            StartTestMainTimer();
            SetUpdateTestResultTimer();
            ActivityStatusTextView.SetText(Resource.String.activity_status_testing);
            ModeChangeButton.SetText(Resource.String.test_cancel);
            TestTimeRemain = TestTimePeriod;
        }

        void CancelTest()
        {
            ActivityStatus = TEST_STATUS.STANDBY;
            UpdateTestResultTimer.Dispose();
            TestMainTimer.Dispose();
            ActivityStatusTextView.SetText(Resource.String.activity_status_standby);
            ModeChangeButton.SetText(Resource.String.test_start);
        }

        void FinishTestHandler(Object sender)
        {
            Object thisLock = new Object();
            lock (thisLock)
            {
                ActivityStatus = TEST_STATUS.STANDBY;
                UpdateTestResultTimer.Dispose();
                TestMainTimer.Dispose();
                RunOnUiThread(() =>
                {
                    ActivityStatusTextView.SetText(Resource.String.activity_status_standby);
                    RemainTimeTextView.SetText(Resource.String.remain_time_normal_end);
                    ModeChangeButton.SetText(Resource.String.test_start);
                });
            }
        }

        void StartTestMainTimer()
        {
            TestMainTimer = new Timer(FinishTestHandler, this, TestTimePeriod * 1000, Timeout.Infinite);
        }

        void SetUpdateTestResultTimer()
        {
            UpdateTestResultTimer = new Timer(UpdateTestResultDisplay, this, 0, 1000);
        }

        void UpdateTestResultDisplay(Object sender)
        {
            Object thisLock = new Object();
            lock(thisLock)
            {
                RunOnUiThread(() => {
                    RemainTimeTextView.SetText(string.Format("{0:0#}:{1:0#}", TestTimeRemain / 60, TestTimeRemain % 60), TextView.BufferType.Normal);
                    GoodCountTextView. SetText(CheckInstance.GoodCountString,  TextView.BufferType.Normal);
                    ErrorCountTextView.SetText(CheckInstance.ErrorCountString, TextView.BufferType.Normal);
                    TotalCountTextView.SetText(CheckInstance.TotalCountString, TextView.BufferType.Normal);
                });
                TestTimeRemain -= 1;
                if (TestTimeRemain < 0)
                {
                    TestTimeRemain = 0;
                }

            }
        }

        protected override void OnPause()
        {
            base.OnPause();
            if (mUsbSerialPort != null)
            {
                try
                {
                    mUsbSerialPort.Close();
                    mUsbSerialPort.DataReceivedEventLinser -= DataReceivedHandler;
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
			Log.Debug (TAG, "Resumed, port=" + mUsbSerialPort);
			if (mUsbSerialPort == null) {
				TitleTextView.Text = "No serial device.";
			} else {
				try {
                    mUsbSerialPort.Baudrate = 19200;
					mUsbSerialPort.Open ();
				} catch (Exception e) {
					Log.Error (TAG, "Error setting up device: " + e.Message, e);
					TitleTextView.Text = "Error opening device: " + e.Message;
					try {
						mUsbSerialPort.Close ();
					} catch (Exception) {
						// Ignore.
					}
					mUsbSerialPort = null;
					return;
				}
				TitleTextView.Text = "Serial device: " + mUsbSerialPort.GetType ().Name;
			}
		}

        private void DataReceivedHandler(object sender, DataReceivedEventArgs e)
        {
            Object thisLock = new Object();
            lock (thisLock)
            {
                byte[] data = new byte[16384];
                int length = e.Port.Read(data, 0);
                RunOnUiThread(() => { UpdateReceivedData(data, length); });
                if (ActivityStatus == TEST_STATUS.TESTING)
                {
                    for (int i = 0; i < length; i++)
                    {
                        CheckInstance.ProcData(data[i]);
                    }
                }
            }
        }

        public void UpdateReceivedData(byte[] data, int length)
        {
            //string message = "Read " + length + " bytes: \n" + HexDump.DumpHexString(data, 0, length) + "\n\n";
			//mDumpTextView.Append(message);
			DumpTextView.Append(System.Text.Encoding.Default.GetString(data, 0, length));
            ScrollView.SmoothScrollTo(0, DumpTextView.Bottom);
        }

        /**
         * Starts the activity, using the supplied driver instance.
         *
         * @param context
         * @param driver
         */
		static public void Show(Context context, UsbSerialPort port)
        {
            mUsbSerialPort = port;
            Intent intent = new Intent(context, typeof(SerialConsoleActivity));
            intent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.NoHistory);
            context.StartActivity(intent);
        }
    }

    abstract class CheckData
    {
        abstract public string TestMode { get; }
        abstract public string GoodCountString { get; }
        abstract public string ErrorCountString { get; }
        abstract public string TotalCountString { get; }
        abstract public void ProcData(byte data);
        abstract public void ResetProc();
    }

    class CheckNmeaCheckSum : CheckData
    {
        enum STATE : byte { IDLE, DATA, SUM1, SUM2 };

        public override string TestMode { get { return "NMEA Check Sum"; } }
        public override string GoodCountString  { get { return goodCount. ToString(); } }
        public override string ErrorCountString { get { return errorCount.ToString(); } }
        public override string TotalCountString { get { return totalCount.ToString(); } }
        STATE state = STATE.IDLE;
        Int64 goodCount = 0;
        Int64 errorCount = 0;
        Int64 totalCount = 0;
        byte calcSum = 0;
        byte getSum = 0;
        byte firstCharValue;
        byte secondCharValue;
        Object thisLock = new Object();

        public override void ResetProc()
        {
            goodCount = 0;
            errorCount = 0;
            totalCount = 0;
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
}