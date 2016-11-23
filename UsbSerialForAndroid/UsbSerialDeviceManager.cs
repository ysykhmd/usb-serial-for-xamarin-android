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

#if UseSmartThreadPool
using Amib.Threading;
#endif

namespace Aid.UsbSerial
{
    /**
     *
     * @author Yasuyuki Hamada (yasuyuki_hamada@agri-info-design.com)
     */
    public class UsbSerialDeviceManager
    {
        private Context Context { get; }
        private bool IsWorking { get; set; }
        private UsbManager UsbManager { get; }
        private UsbSerialDeviceBroadcastReceiver Receiver { get; }
        private string ActionUsbPermission { get; }
        public bool AllowAnonymousCdcAcmDevices { get; }
#if UseSmartThreadPool
        public SmartThreadPool ThreadPool { get; private set; }
#endif

        public event EventHandler<UsbSerialDeviceEventArgs> DeviceAttached;
        public event EventHandler<UsbSerialDeviceEventArgs> DeviceDetached;

        public Dictionary<UsbSerialDeviceID, UsbSerialDeviceInfo> AvailableDeviceInfo { get; }
        private readonly object _attachedDevicesSyncRoot = new object();
        public List<UsbSerialDevice> AttachedDevices { get; }

#if UseSmartThreadPool
        public UsbSerialDeviceManager(Context context, string actionUsbPermission, bool allowAnonymousCdcAmcDevices)
            : this(context, actionUsbPermission, allowAnonymousCdcAmcDevices, UsbSerialDeviceList.Default, null)
        {
        }

        public UsbSerialDeviceManager(Context context, string actionUsbPermission, bool allowAnonymousCdcAmcDevices, SmartThreadPool threadPool)
            : this(context, actionUsbPermission, allowAnonymousCdcAmcDevices, UsbSerialDeviceList.Default, threadPool)
        {
        }
#else
        public UsbSerialDeviceManager(Context context, string actionUsbPermission, bool allowAnonymousCdcAmcDevices)
            : this(context, actionUsbPermission, allowAnonymousCdcAmcDevices, UsbSerialDeviceList.Default)
        {
        }
#endif

#if UseSmartThreadPool
        public UsbSerialDeviceManager(Context context, string actionUsbPermission, bool allowAnonymousCdcAmcDevices, UsbSerialDeviceList availableDeviceList, SmartThreadPool threadPool)
#else
        public UsbSerialDeviceManager(Context context, string actionUsbPermission, bool allowAnonymousCdcAmcDevices, UsbSerialDeviceList availableDeviceList)
#endif
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

#if UseSmartThreadPool
            ThreadPool = threadPool;
#endif
        }

        public void Start()
        {
            if (IsWorking)
            {
                return;
            }
            IsWorking = true;
            // listen for new devices
            var filter = new IntentFilter();
            filter.AddAction(UsbManager.ActionUsbDeviceAttached);
            filter.AddAction(UsbManager.ActionUsbDeviceDetached);
            filter.AddAction(ActionUsbPermission);
            Context.RegisterReceiver(Receiver, filter);
            Update();
        }

        internal void AddDevice(UsbManager usbManager, UsbDevice usbDevice)
        {
            var serialDevice = GetDevice(usbManager, usbDevice, AllowAnonymousCdcAcmDevices);
            if (serialDevice != null)
            {
                lock (_attachedDevicesSyncRoot)
                {
                    AttachedDevices.Add(serialDevice);
                    DeviceAttached?.Invoke(this, new UsbSerialDeviceEventArgs(serialDevice));
                }
            }
        }

        internal void RemoveDevice(UsbDevice usbDevice)
        {
            UsbSerialDevice removedDevice = null;
            var attachedDevices = AttachedDevices.ToArray();
            foreach (var device in attachedDevices)
            {
                bool serialEquals;
                try
                {
                    // TODO Fix this workaround of https://github.com/ysykhmd/usb-serial-for-xamarin-android/issues/1
                    serialEquals = device.UsbDevice.SerialNumber == usbDevice.SerialNumber;
                }
                catch (Exception)
                {
                    serialEquals = true;
                }

                if (device.UsbDevice.VendorId == usbDevice.VendorId
                    && device.UsbDevice.ProductId == usbDevice.ProductId
                    && serialEquals)
                {
                    removedDevice = device;
                    break;
                }
            }
            if (removedDevice != null)
            {
                RemoveDevice(removedDevice);
            }
        }

        internal void RemoveDevice(UsbSerialDevice serialDevice)
        {
            if (serialDevice != null)
            {
                lock (_attachedDevicesSyncRoot)
                {
                    serialDevice.CloseAllPorts();
                    DeviceDetached?.Invoke(this, new UsbSerialDeviceEventArgs(serialDevice));
                    AttachedDevices.Remove(serialDevice);
                }
            }
        }

        public void Stop()
        {
            if (!IsWorking)
            {
                return;
            }
            IsWorking = false;
            var attachedDevices = AttachedDevices.ToArray();
            foreach (var device in attachedDevices)
            {
                RemoveDevice(device);
            }
            Context.UnregisterReceiver(Receiver);
        }

        public void Update()
        {
            // Remove detached devices from AttachedDevices
            var attachedDevices = AttachedDevices.ToArray();
            foreach (var attachedDevice in attachedDevices)
            {
                var exists = false;
                foreach (var usbDevice in UsbManager.DeviceList.Values)
                {
                    if ((usbDevice.VendorId == attachedDevice.ID.VendorID) && (usbDevice.ProductId == attachedDevice.ID.ProductID) && (usbDevice.SerialNumber == attachedDevice.UsbDevice.SerialNumber))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    RemoveDevice(attachedDevice);
                }
            }

            // Add attached devices If not exists in AttachedDevices
            foreach (var usbDevice in UsbManager.DeviceList.Values)
            {
                var exists = false;
                attachedDevices = AttachedDevices.ToArray();
                foreach (var attachedDevice in attachedDevices)
                {
                    if ((usbDevice.VendorId == attachedDevice.ID.VendorID) && (usbDevice.ProductId == attachedDevice.ID.ProductID) && (usbDevice.SerialNumber == attachedDevice.UsbDevice.SerialNumber))
                    {
                        exists = true;
                        break;
                    }
                }
                if (exists)
                {
                    break;
                }
                if (!UsbManager.HasPermission(usbDevice))
                {
                    var permissionIntent = PendingIntent.GetBroadcast(Context.ApplicationContext, 0, new Intent(ActionUsbPermission), 0);
                    UsbManager.RequestPermission(usbDevice, permissionIntent);
                }
                else
                {
                    AddDevice(UsbManager, usbDevice);
                }
            }
        }

        private UsbSerialDevice GetDevice(UsbManager usbManager, UsbDevice usbDevice, bool allowAnonymousCdcAcmDevices)
        {
            var id = new UsbSerialDeviceID(usbDevice.VendorId, usbDevice.ProductId);
            var info = FindDeviceInfo(id, usbDevice.DeviceClass, allowAnonymousCdcAcmDevices);
            if (info != null)
            {
#if UseSmartThreadPool
                var device = new UsbSerialDevice(usbManager, usbDevice, id, info, ThreadPool);
#else
                var device = new UsbSerialDevice(usbManager, usbDevice, id, info);
#endif
                return device;
            }
            return null;
        }

        private UsbSerialDeviceInfo FindDeviceInfo(UsbSerialDeviceID id, UsbClass usbClass, bool allowAnonymousCdcAcmDevices)
        {
            if (AvailableDeviceInfo.ContainsKey(id))
            {
                return AvailableDeviceInfo[id];
            }
            if (allowAnonymousCdcAcmDevices && usbClass == UsbClass.Comm)
            {
                return UsbSerialDeviceInfo.CdcAcm;
            }

            return null;
        }

        private class UsbSerialDeviceBroadcastReceiver : BroadcastReceiver
        {
            private UsbManager UsbManager { get; }

            private UsbSerialDeviceManager DeviceManager { get; }

            private string ActionUsbPermission { get; }

            public UsbSerialDeviceBroadcastReceiver(UsbSerialDeviceManager manager, UsbManager usbManager, string actionUsbPermission)
            {
                DeviceManager = manager;
                UsbManager = usbManager;
                ActionUsbPermission = actionUsbPermission;
            }

            public override void OnReceive(Context context, Intent intent)
            {
                var device = intent.GetParcelableExtra(UsbManager.ExtraDevice) as UsbDevice;
                if (device == null)
                    return;

                var id = new UsbSerialDeviceID(device.VendorId, device.ProductId);
                var info = DeviceManager.FindDeviceInfo(id, device.DeviceClass, DeviceManager.AllowAnonymousCdcAcmDevices);
                if (info == null)
                    return;

                var action = intent.Action;
                if (action == UsbManager.ActionUsbDeviceAttached)
                {
                    if (!UsbManager.HasPermission(device))
                    {
                        var permissionIntent = PendingIntent.GetBroadcast(context, 0, new Intent(ActionUsbPermission), 0);
                        UsbManager.RequestPermission(device, permissionIntent);
                    }
                    else
                    {
                        DeviceManager.AddDevice(UsbManager, device);
                    }
                }
                else if (action == UsbManager.ActionUsbDeviceDetached)
                {
                    DeviceManager.RemoveDevice(device);
                }
                else if (action == ActionUsbPermission)
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