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
using System.Reflection;
using Android.Hardware.Usb;
using Android.App;
using Android.Content;

namespace Aid.UsbSerial
{
    /**
     *
     * @author Yasuyuki Hamada (yasuyuki_hamada@agri-info-design.com)
     */
	public class UsbSerialDeviceManager
    {
        private Context Context { get; set; }
		private bool IsWorking { get; set; }
		private UsbManager UsbManager { get; set; }
        private UsbSerialDeviceBroadcastReceiver Receiver { get; set; }
        private string ActionUsbPermission { get; set; }
        public bool AllowAnonymousCdcAcmDevices { get; private set; }

        public event EventHandler<UsbSerialDeviceEventArgs> DeviceAttached;
		public event EventHandler<UsbSerialDeviceEventArgs> DeviceDetached;

		private object AvailableDeviceInfoSyncRoot = new object();
        public Dictionary<UsbSerialDeviceID, UsbSerialDeviceInfo> AvailableDeviceInfo { get; private set; }
		private object AttachedDevicesSyncRoot = new object();
        public List<UsbSerialDevice> AttachedDevices { get; private set; }

        public UsbSerialDeviceManager(Context context, string actionUsbPermission, bool allowAnonymousCdcAmcDevices)
            : this(context, actionUsbPermission, allowAnonymousCdcAmcDevices, UsbSerialDeviceList.Default)
        {
        }

		public UsbSerialDeviceManager(Context context, string actionUsbPermission, bool allowAnonymousCdcAmcDevices, UsbSerialDeviceList availableDeviceList)
        {
            if (context == null)
                throw new ArgumentNullException();
            if (string.IsNullOrEmpty(actionUsbPermission))
                throw new ArgumentException();
            if (availableDeviceList == null)
                throw new ArgumentNullException();

            Context = context;
            ActionUsbPermission = actionUsbPermission;
            UsbManager = (UsbManager)context.GetSystemService(Context.UsbService);
            Receiver = new UsbSerialDeviceBroadcastReceiver(this, UsbManager, actionUsbPermission);
            AllowAnonymousCdcAcmDevices = allowAnonymousCdcAmcDevices;

            AvailableDeviceInfo = availableDeviceList.AvailableDeviceInfo;
            AttachedDevices = new List<UsbSerialDevice>();
        }

        public void Start()
        {
			if (IsWorking) {
				return;
			}
			IsWorking = true;
			// listen for new devices
            IntentFilter filter = new IntentFilter();
            filter.AddAction(UsbManager.ActionUsbDeviceAttached);
            filter.AddAction(UsbManager.ActionUsbDeviceDetached);
            filter.AddAction(ActionUsbPermission);
            Context.RegisterReceiver(Receiver, filter);
            Update();
        }

        //

        internal void AddDevice(UsbManager usbManager, UsbDevice usbDevice)
        {
            UsbSerialDevice serialDevice = GetDevice(usbManager, usbDevice, AllowAnonymousCdcAcmDevices);
            if (serialDevice != null)
            {
				lock (AttachedDevicesSyncRoot) {
					AttachedDevices.Add (serialDevice);
					if (DeviceAttached != null) {
						DeviceAttached (this, new UsbSerialDeviceEventArgs (serialDevice));
					}
				}
            }
        }


        internal void RemoveDevice(UsbDevice usbDevice)
        {
            UsbSerialDevice removedDevice = null;
			UsbSerialDevice[] attachedDevices = AttachedDevices.ToArray();
			foreach (UsbSerialDevice device in attachedDevices) {
				if (
					device.UsbDevice.VendorId == usbDevice.VendorId
					&& device.UsbDevice.ProductId == usbDevice.ProductId
					&& device.UsbDevice.SerialNumber == usbDevice.SerialNumber) {
					removedDevice = device;
					break;
				}
			}
			if (removedDevice != null) {
				RemoveDevice (removedDevice);
			}
        }


        internal void RemoveDevice(UsbSerialDevice serialDevice)
        {
            if (serialDevice != null)
            {
				lock (AttachedDevicesSyncRoot) {
					serialDevice.CloseAllPorts ();
					if (DeviceDetached != null) {
						DeviceDetached (this, new UsbSerialDeviceEventArgs (serialDevice));
					}
					AttachedDevices.Remove (serialDevice);
				}
            }
        }


        public void Stop()
		{
			if (!IsWorking) {
				return;
			}
			IsWorking = false;
			UsbSerialDevice[] attachedDevices = AttachedDevices.ToArray();
			foreach (UsbSerialDevice device in attachedDevices) {
				RemoveDevice (device);
			}
			Context.UnregisterReceiver (Receiver);
		}



        public void Update()
        {
            // Remove detached devices from AttachedDevices
			UsbSerialDevice[] attachedDevices = AttachedDevices.ToArray();
			foreach (UsbSerialDevice attachedDevice in attachedDevices)
            {
                bool exists = false;
				foreach (var usbDevice in UsbManager.DeviceList.Values) {
                    if ((usbDevice.VendorId == attachedDevice.ID.VendorID) && (usbDevice.ProductId == attachedDevice.ID.ProductID) && (usbDevice.SerialNumber == attachedDevice.UsbDevice.SerialNumber))
                    {
                        exists = true;
                        break;
                    }
                }
				if (!exists) {
					RemoveDevice(attachedDevice);
                }
            }

            // Add attached devices If not exists in AttachedDevices
            foreach (var usbDevice in UsbManager.DeviceList.Values) {
                bool exists = false;
				attachedDevices = AttachedDevices.ToArray();
                foreach (UsbSerialDevice attachedDevice in attachedDevices)
                {
                    if ((usbDevice.VendorId == attachedDevice.ID.VendorID) && (usbDevice.ProductId == attachedDevice.ID.ProductID) && (usbDevice.SerialNumber == attachedDevice.UsbDevice.SerialNumber)) {
                        exists = true;
                        break;
                    }
                }
                if (exists) {
                    break;
                } else {
                    if (!UsbManager.HasPermission(usbDevice))
                    {
                        PendingIntent permissionIntent;
                        permissionIntent = PendingIntent.GetBroadcast(Context.ApplicationContext, 0, new Intent(ActionUsbPermission), 0);
                        UsbManager.RequestPermission(usbDevice, permissionIntent);
                    }
                    else
                    {
                        AddDevice(UsbManager, usbDevice);
                    }
                }
            }
        }


		private UsbSerialDevice GetDevice(UsbManager usbManager, UsbDevice usbDevice, bool allowAnonymousCdcAcmDevices)
		{
            UsbSerialDeviceID id = new UsbSerialDeviceID(usbDevice.VendorId, usbDevice.ProductId);
			UsbSerialDeviceInfo info = FindDeviceInfo (id);
			if (info != null) {
				UsbSerialDevice device = new UsbSerialDevice(usbManager, usbDevice, id, info);
				return device;
            }
            else if (allowAnonymousCdcAcmDevices && usbDevice.DeviceClass == UsbClass.Comm)
            {
                UsbSerialDevice device = new UsbSerialDevice(usbManager, usbDevice, id, UsbSerialDeviceInfo.CdcAcm);
				return device;
			}
			return null;
		}


        private UsbSerialDeviceInfo FindDeviceInfo(UsbSerialDeviceID id)
        {
            if (AvailableDeviceInfo.ContainsKey(id))
            {
                return AvailableDeviceInfo[id];
            }
            else
            {
                return null;
            }
        }

        //

        private class UsbSerialDeviceBroadcastReceiver : BroadcastReceiver
        {
            private UsbManager UsbManager
            {
                get;
                set;
            }

            private UsbSerialDeviceManager DeviceManager
            {
                get;
                set;
            }

            private string ActionUsbPermission
            {
                get;
                set;
            }

            public UsbSerialDeviceBroadcastReceiver(UsbSerialDeviceManager manager, UsbManager usbManager, string actionUsbPermission)
            {
                DeviceManager = manager;
                UsbManager = usbManager;
                ActionUsbPermission = actionUsbPermission;
            }

            public override void OnReceive(Context context, Intent intent)
            {
                UsbDevice device = null;
                string action = intent.Action;
                if (action == UsbManager.ActionUsbDeviceAttached)
                {
					Java.Lang.Object exdevobj = intent.GetParcelableExtra(UsbManager.ExtraDevice);
					if (exdevobj is UsbDevice)
					{
						device = (UsbDevice)exdevobj;
					}
                    if (device != null && intent != null)
                    {
                        if (!UsbManager.HasPermission(device))
                        {
                            PendingIntent permissionIntent;
                            permissionIntent = PendingIntent.GetBroadcast(context, 0, new Intent(ActionUsbPermission), 0);
                            UsbManager.RequestPermission(device, permissionIntent);
                        }
                        else
                        {
                            DeviceManager.AddDevice(UsbManager, device);
                        }
                    }
                }
                else if (action == UsbManager.ActionUsbDeviceDetached)
                {
					Java.Lang.Object exdevobj = intent.GetParcelableExtra(UsbManager.ExtraDevice);
					if (exdevobj is UsbDevice)
					{
						device = (UsbDevice)exdevobj;
					}
					if (device != null)
					{
						DeviceManager.RemoveDevice(device);
					}
                }
                else if (action == ActionUsbPermission)
                {
                    Java.Lang.Object exdevobj = intent.GetParcelableExtra(UsbManager.ExtraDevice);
                    if (exdevobj is UsbDevice)
                    {
                        device = (UsbDevice)exdevobj;
                    }
                    if (device != null)
                    {
                        if (UsbManager.HasPermission(device))
                        {
                            DeviceManager.AddDevice(UsbManager, device);
                        }
                    }
                }
            }
        }
	}
}