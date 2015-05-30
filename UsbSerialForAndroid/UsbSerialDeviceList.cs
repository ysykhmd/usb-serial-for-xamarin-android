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

namespace Aid.UsbSerial
{
    public class UsbSerialDeviceList
    {
		public const int VendorFtdi = 0x0403;
		public const int FtdiFT232_245 = 0x6001;
		public const int FtdiFT2232 = 0x6010;
		public const int FtdiFT4232 = 0x6011;
		public const int FtdiFT232HL = 0x6014;

		public const int VendorRatoc = 0x0584;
		public const int RexUsb60F = 0xb020;
		public const int RexUsb60MI = 0xb02f;

        public const int VendorAtmel = 0x03EB;
        public const int AtmelLufaCdcDemoApp = 0x2044;

        public const int VendorArduino = 0x2341;
        public const int ArduinoUno = 0x0001;
        public const int ArduinoMega2560 = 0x0010;
        public const int ArduinoSerialAdapter = 0x003b;
        public const int ArduinoMegaAdk = 0x003f;
        public const int ArduinoMega2560R3 = 0x0042;
        public const int ArduinoUnoR3 = 0x0043;
        public const int ArduinoMegaAdkR3 = 0x0044;
        public const int ArduinoSerialAdapterR3 = 0x0044;
        public const int ArduinoLeonard = 0x8036;

        public const int VendorVanOoijenTech = 0x16c0;
        public const int VanOoijenTechTeensyduinoSerial = 0x0483;

        public const int VendorLeaflabs = 0x1eaf;
        public const int LeaflabsMaple = 0x0004;

        public const int VendorSilabs = 0x10c4;
        public const int SilabsCP2102 = 0xea60;
        public const int SilabsCP2105 = 0xea70;
        public const int SilabsCP2108 = 0xea71;
        public const int SilabsCP2110 = 0xea80;

        public const int VendorProlific = 0x067b;
        public const int ProlificPL2303 = 0x2303;


        //


        public static UsbSerialDeviceList Default { get; private set; }


        static UsbSerialDeviceList()
        {
            Default = new UsbSerialDeviceList();

            Default.AddAvailableDeviceInfo(VendorArduino, ArduinoUno, typeof(CdcAcmSerialPort), 1);
            Default.AddAvailableDeviceInfo(VendorArduino, ArduinoUnoR3, typeof(CdcAcmSerialPort), 1);
            Default.AddAvailableDeviceInfo(VendorArduino, ArduinoMega2560, typeof(CdcAcmSerialPort), 1);
            Default.AddAvailableDeviceInfo(VendorArduino, ArduinoMega2560R3, typeof(CdcAcmSerialPort), 1);
            Default.AddAvailableDeviceInfo(VendorArduino, ArduinoSerialAdapter, typeof(CdcAcmSerialPort), 1);
            Default.AddAvailableDeviceInfo(VendorArduino, ArduinoSerialAdapterR3, typeof(CdcAcmSerialPort), 1);
            Default.AddAvailableDeviceInfo(VendorArduino, ArduinoMegaAdk, typeof(CdcAcmSerialPort), 1);
            Default.AddAvailableDeviceInfo(VendorArduino, ArduinoMegaAdkR3, typeof(CdcAcmSerialPort), 1);
            Default.AddAvailableDeviceInfo(VendorArduino, ArduinoLeonard, typeof(CdcAcmSerialPort), 1);
            Default.AddAvailableDeviceInfo(VendorVanOoijenTech, VanOoijenTechTeensyduinoSerial, typeof(CdcAcmSerialPort), 1);
            Default.AddAvailableDeviceInfo(VendorAtmel, AtmelLufaCdcDemoApp, typeof(CdcAcmSerialPort), 1);
            Default.AddAvailableDeviceInfo(VendorLeaflabs, LeaflabsMaple, typeof(CdcAcmSerialPort), 1);

            Default.AddAvailableDeviceInfo(VendorSilabs, SilabsCP2102, typeof(Cp21xxSerialPort), 1);
            Default.AddAvailableDeviceInfo(VendorSilabs, SilabsCP2105, typeof(Cp21xxSerialPort), 1);
            Default.AddAvailableDeviceInfo(VendorSilabs, SilabsCP2108, typeof(Cp21xxSerialPort), 1);
            Default.AddAvailableDeviceInfo(VendorSilabs, SilabsCP2110, typeof(Cp21xxSerialPort), 1);

			Default.AddAvailableDeviceInfo(VendorFtdi, FtdiFT232_245, typeof(FtdiSerialPort), 1);
			Default.AddAvailableDeviceInfo(VendorFtdi, FtdiFT2232, typeof(FtdiSerialPort), 2);
			Default.AddAvailableDeviceInfo(VendorFtdi, FtdiFT4232, typeof(FtdiSerialPort), 4);
			Default.AddAvailableDeviceInfo(VendorFtdi, FtdiFT232HL, typeof(FtdiSerialPort), 1);

			Default.AddAvailableDeviceInfo(VendorRatoc, RexUsb60F, typeof(FtdiSerialPort), 1);
			Default.AddAvailableDeviceInfo(VendorRatoc, RexUsb60MI, typeof(FtdiSerialPort), 1);

            Default.AddAvailableDeviceInfo(VendorProlific, ProlificPL2303, typeof(ProlificSerialPort), 1);
        }


		//

        
		public Dictionary<UsbSerialDeviceID, UsbSerialDeviceInfo> AvailableDeviceInfo { get; private set; }


		public UsbSerialDeviceList()
		{
			AvailableDeviceInfo = new Dictionary<UsbSerialDeviceID, UsbSerialDeviceInfo> ();
		}


        public void AddAvailableDeviceInfo(int vendorId, int productId, Type deviceClass, int numOfPorts)
        {
            if (!deviceClass.IsSubclassOf(typeof(UsbSerialPort)))
            {
                throw new ArgumentException();
            }
            UsbSerialDeviceID id = new UsbSerialDeviceID(vendorId, productId);
            if (!AvailableDeviceInfo.ContainsKey(id))
            {
                AvailableDeviceInfo.Add(id, new UsbSerialDeviceInfo(deviceClass, numOfPorts));
            }
        }
    }
}