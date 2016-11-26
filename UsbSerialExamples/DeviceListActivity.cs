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
using System.Collections.Generic;

using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;

using Aid.UsbSerial;

[assembly: UsesFeature("android.hardware.usb.host")]

namespace UsbSerialExamples
{
    /**
     * Shows a {@link ListView} of available USB devices.
     *
     * @author mike wakerly (opensource@hoho.com)
     */
    [Activity(Label = "@string/app_name", MainLauncher = true, Icon = "@drawable/ic_launcher")]
    [IntentFilter(new[] { UsbManager.ActionUsbDeviceAttached })]
    [MetaData(UsbManager.ActionUsbDeviceAttached, Resource = "@xml/device_filter")]
    public class DeviceListActivity : Activity
    {
        private class DeviceListItemHolder : Java.Lang.Object
        {
            public TextView deviceInfo { get; set; }
            public TextView productName { get; set; }
        }


        private class MyArrayAdapter : ArrayAdapter<UsbSerialPort>
        {
            private DeviceListActivity DeviceListActivity { get; set; }

			public MyArrayAdapter(DeviceListActivity activity, int textViewResourceId, List<UsbSerialPort> objects)
                : base(activity, textViewResourceId, objects)
            {
                DeviceListActivity = activity;
			}

			public override View GetView(int position, View convertView, ViewGroup parent)
			{
                DeviceListItemHolder holder; 
                View view;

                if (convertView == null)
                {
                    LayoutInflater inflater = (LayoutInflater)Context.GetSystemService(Context.LayoutInflaterService);
                    view = inflater.Inflate(Resource.Layout.device_list_item, null);
                }
                else
                {
                    view = convertView;
                }

                holder = view.Tag as DeviceListItemHolder;

                if (null == holder)
                {
                    holder = new DeviceListItemHolder();

                    holder.deviceInfo = (TextView) view.FindViewById(Resource.Id.device_info);
                    holder.productName = (TextView) view.FindViewById(Resource.Id.product_name);

                    view.Tag = holder;
                }

                UsbSerialPort port = GetItem (position);
				UsbDevice device = port.UsbDevice;

				string title = string.Format("Vendor {0:04x} Product {1:04x}",
                        HexDump.toHexString((short)device.VendorId),
                        HexDump.toHexString((short)device.ProductId));
                holder.deviceInfo.SetText(title, TextView.BufferType.Normal);

                // UsbDevice.ProductName is available from API Level 21 (Lollipop)
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
                {
                    string subtitle = device.ProductName;
                    holder.productName.SetText(subtitle, TextView.BufferType.Normal);
                }

                return view;
			}
        }


		//


        private const string TAG = "DeviceListActivity";
		private const int MESSAGE_REFRESH = 101;
		private const long REFRESH_TIMEOUT_MILLIS = 5000;

        private ListView mListView;


        private ArrayAdapter<UsbSerialPort> mAdapter;
		private List<UsbSerialPort> mUsbSerialPortList;
        private UsbSerialDeviceManager mUsbSerialDeviceManager;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.main);

			mUsbSerialDeviceManager = new UsbSerialDeviceManager(this, "Aid.UsbSerialExamples.Aid.UsbSerialExamples.USB_PERMISSION", true);
			mUsbSerialPortList = new List<UsbSerialPort> ();

            mListView = (ListView)FindViewById(Resource.Id.deviceList);

            mAdapter = new MyArrayAdapter(this, Android.Resource.Layout.SimpleExpandableListItem2, mUsbSerialPortList);
            mListView.Adapter = mAdapter;

			mUsbSerialDeviceManager.DeviceAttached += (object sender, UsbSerialDeviceEventArgs e) => { RefreshDeviceList(); };
			mUsbSerialDeviceManager.DeviceDetached += (object sender, UsbSerialDeviceEventArgs e) => { RefreshDeviceList(); };
			mUsbSerialDeviceManager.Start ();

            mListView.ItemClick += (sender, e) =>
            {
                int position = e.Position;
                Log.Debug(TAG, "Pressed item " + position);
				if (mAdapter.Count <= position)
                {
                    Log.Warn(TAG, "Illegal position.");
                    return;
                }

				UsbSerialPort port = mAdapter.GetItem(position);
                ShowConsoleActivity(port);
            };
        }

        public void RefreshDeviceList()
        {
			mAdapter.Clear ();
			foreach (var attachedDevice in mUsbSerialDeviceManager.AttachedDevices) {
				var ports = attachedDevice.Ports;
				foreach(var port in ports) {
					mAdapter.Add (port);
				}
			}
			mAdapter.NotifyDataSetChanged();
        }

		private void ShowConsoleActivity(UsbSerialPort port)
        {
            SerialConsoleActivity.Show(this, port);
        }
    }
}