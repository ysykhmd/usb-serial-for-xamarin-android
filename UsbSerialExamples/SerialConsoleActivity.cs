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
using Android.Widget;

using Aid.UsbSerial;

namespace UsbSerialExamples
{
    [Activity(Label = "@string/app_name")]
    public class SerialConsoleActivity : Activity
    {
        private const string TAG = "SerialConsoleActivity";

		private static UsbSerialPort mUsbSerialPort = null;

        private TextView mTitleTextView;
        private TextView mDumpTextView;
        private ScrollView mScrollView;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.serial_console);

            mTitleTextView = (TextView)FindViewById(Resource.Id.demoTitle);
            mDumpTextView = (TextView)FindViewById(Resource.Id.consoleText);
            mScrollView = (ScrollView)FindViewById(Resource.Id.demoScroller);

			mUsbSerialPort.DataReceivedEventLinser += DataReceivedHandler;
        }

        protected override void OnPause()
        {
            base.OnPause();
            if (mUsbSerialPort != null)
            {
                try
                {
					mUsbSerialPort.DataReceivedEventLinser -= DataReceivedHandler;
                    mUsbSerialPort.Close();
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
				mTitleTextView.Text = "No serial device.";
			} else {
				try {
                    mUsbSerialPort.Baudrate = 19200;
					mUsbSerialPort.Open ();
				} catch (Exception e) {
					Log.Error (TAG, "Error setting up device: " + e.Message, e);
					mTitleTextView.Text = "Error opening device: " + e.Message;
					try {
						mUsbSerialPort.Close ();
					} catch (Exception) {
						// Ignore.
					}
					mUsbSerialPort = null;
					return;
				}
				mTitleTextView.Text = "Serial device: " + mUsbSerialPort.GetType ().Name;
			}
		}

        private void DataReceivedHandler(object sender, DataReceivedEventArgs e)
        {
            byte[] data = new byte[16384];
            int length = e.Port.Read(data, 0);
			RunOnUiThread(() => { UpdateReceivedData (data, length);});
        }

        public void UpdateReceivedData(byte[] data, int length)
        {
            //string message = "Read " + length + " bytes: \n" + HexDump.DumpHexString(data, 0, length) + "\n\n";
			//mDumpTextView.Append(message);
			mDumpTextView.Append(System.Text.Encoding.Default.GetString(data, 0, length));
            mScrollView.SmoothScrollTo(0, mDumpTextView.Bottom);
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
}