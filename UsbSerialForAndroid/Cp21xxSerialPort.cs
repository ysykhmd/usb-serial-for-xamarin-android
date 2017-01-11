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
using System.IO;

using Android.Hardware.Usb;
using Android.Util;

namespace Aid.UsbSerial
{
	class Cp21xxSerialPort : UsbSerialPort
    {
        const string TAG = "Cp21xxSerialPort";
        const int DEFAULT_BAUD_RATE = 9600;

        const int USB_WRITE_TIMEOUT_MILLIS = 5000;

        /*
         * Configuration Request Types
         */
        const int REQTYPE_HOST_TO_DEVICE = 0x41;

        /*
         * Configuration Request Codes
         */
        const int SILABSER_IFC_ENABLE_REQUEST_CODE = 0x00;
        const int SILABSER_SET_BAUDDIV_REQUEST_CODE = 0x01;
        const int SILABSER_SET_LINE_CTL_REQUEST_CODE = 0x03;
        const int SILABSER_SET_MHS_REQUEST_CODE = 0x07;
        const int SILABSER_SET_BAUDRATE = 0x1E;
        const int SILABSER_FLUSH_REQUEST_CODE = 0x12;

        const int FLUSH_READ_CODE = 0x0a;
        const int FLUSH_WRITE_CODE = 0x05;

        /*
         * SilabsER_IFC_ENABLE_REQUEST_CODE
         */
        const int UART_ENABLE = 0x0001;
        const int UART_DISABLE = 0x0000;

        /*
         * SilabsER_SET_BAUDDIV_REQUEST_CODE
         */
        const int BAUD_RATE_GEN_FREQ = 0x384000;

        /*
         * SilabsER_SET_MHS_REQUEST_CODE
         */
        const int MCR_DTR = 0x0001;
        const int MCR_RTS = 0x0002;
        const int MCR_ALL = 0x0003;

        const int CONTROL_WRITE_DTR = 0x0100;
        const int CONTROL_WRITE_RTS = 0x0200;

        UsbEndpoint ReadEndpoint;
        UsbEndpoint WriteEndpoint;

        public Cp21xxSerialPort(UsbManager manager, UsbDevice device, int portNumber)
            : base(manager, device, portNumber)
        {
        }

        int setConfigSingle(int request, int value)
        {
            return Connection.ControlTransfer((UsbAddressing)REQTYPE_HOST_TO_DEVICE, request, value, 0, null, 0, USB_WRITE_TIMEOUT_MILLIS);
        }

        public override void Open()
        {
			bool openedSuccessfully = false;
            try
            {
				CreateConnection();

				for (int i = 0; i < UsbDevice.InterfaceCount; i++)
                {
                    UsbInterface usbIface = UsbDevice.GetInterface(i);
                    if (Connection.ClaimInterface(usbIface, true))
                    {
                        Log.Debug(TAG, "ClaimInterface " + i + " SUCCESS");
                    }
                    else
                    {
                        Log.Debug(TAG, "ClaimInterface " + i + " FAIL");
                    }
                }

                UsbInterface dataIface = UsbDevice.GetInterface(UsbDevice.InterfaceCount - 1);
                for (int i = 0; i < dataIface.EndpointCount; i++)
                {
                    UsbEndpoint ep = dataIface.GetEndpoint(i);
                    if (ep.Type == UsbAddressing.XferBulk)
                    { // UsbConstants.USB_ENDPOINT_XFER_BULK
                        if (ep.Direction == UsbAddressing.In)
                        { // UsbConstants.USB_DIR_IN
                            ReadEndpoint = ep;
                        }
                        else
                        {
                            WriteEndpoint = ep;
                        }
                    }
                }

                setConfigSingle(SILABSER_IFC_ENABLE_REQUEST_CODE, UART_ENABLE);
                setConfigSingle(SILABSER_SET_MHS_REQUEST_CODE, MCR_ALL | CONTROL_WRITE_DTR | CONTROL_WRITE_RTS);
                setConfigSingle(SILABSER_SET_BAUDDIV_REQUEST_CODE, BAUD_RATE_GEN_FREQ / DEFAULT_BAUD_RATE);
				ResetParameters();
				openedSuccessfully = true;
            }
			finally
            {
				if (openedSuccessfully)
                {
					IsOpened = true;
					StartUpdating ();
				}
                else
                {
					CloseConnection();
				}
			}
        }

        public override void Close()
        {
			StopUpdating ();

			setConfigSingle(SILABSER_IFC_ENABLE_REQUEST_CODE, UART_DISABLE);

			CloseConnection ();
			IsOpened = false;
        }

        // ガベージを増やさないために関数内で変数の宣言はせず、すべて関数外で宣言する
        int numberOfByteRead;
        protected override int ReadInternal()
        {
            // 一つのスレッドからしか呼出されないので、このロックは不要
            //lock (InternalReadBufferLock)
            {
                numberOfByteRead = Connection.BulkTransfer(ReadEndpoint, TempReadBuffer, TempReadBuffer.Length, DEFAULT_READ_TIMEOUT_MILLISEC);
                if (numberOfByteRead < 0)
                {
                    // This sucks: we get -1 on timeout, not 0 as preferred.
                    // We *should* use UsbRequest, except it has a bug/api oversight
                    // where there is no way to determine the number of bytes read
                    // in response :\ -- http://b.android.com/28023
                    return 0;
                }
            }
            return numberOfByteRead;
        }

        // ガベージを増やさないために関数内で変数の宣言はせず、すべて関数外で宣言する
        int mainWriteBufferOffsetPointer;
        int writeLength;
        int amtWritten;
        byte[] writeBuffer;
        public override int Write(byte[] src, int timeoutMillis)
        {
            mainWriteBufferOffsetPointer = 0;

            lock (MainWriteBufferLock)
            {
                while (mainWriteBufferOffsetPointer < src.Length)
                {
                    writeLength = Math.Min(src.Length - mainWriteBufferOffsetPointer, MainWriteBuffer.Length);
                    if (mainWriteBufferOffsetPointer == 0)
                    {
                        writeBuffer = src;
                    }
                    else
                    {
                        // bulkTransfer does not support offsets, make a copy.
                        Array.Copy(src, mainWriteBufferOffsetPointer, MainWriteBuffer, 0, writeLength);
                        writeBuffer = MainWriteBuffer;
                    }

                    amtWritten = Connection.BulkTransfer(WriteEndpoint, writeBuffer, writeLength, timeoutMillis);
                    if (amtWritten <= 0)
                    {
                        throw new IOException("Error writing " + writeLength
                                + " bytes at offset " + mainWriteBufferOffsetPointer + " length=" + src.Length);
                    }
//                    Log.Debug(TAG, "Wrote amt=" + amtWritten + " attempted=" + writeLength);
                    mainWriteBufferOffsetPointer += amtWritten;
                }
            }
            return mainWriteBufferOffsetPointer;
        }

        void setBaudRate(int baudRate)
        {
            byte[] data = new byte[]
            {
                (byte) ( baudRate & 0xff),
                (byte) ((baudRate >> 8 ) & 0xff),
                (byte) ((baudRate >> 16) & 0xff),
                (byte) ((baudRate >> 24) & 0xff)
            };
            int ret = Connection.ControlTransfer((UsbAddressing)REQTYPE_HOST_TO_DEVICE, SILABSER_SET_BAUDRATE, 0, 0, data, 4, USB_WRITE_TIMEOUT_MILLIS);
            if (ret < 0)
            {
                throw new IOException("Error setting baud rate.");
            }
        }

        protected override void SetParameters(int baudRate, int dataBits, StopBits stopBits, Parity parity)
        {
            setBaudRate(baudRate);

            int configDataBits = 0;
            switch (dataBits)
            {
                case 5:
                    configDataBits |= 0x0500;
                    break;
                case 6:
                    configDataBits |= 0x0600;
                    break;
                case 7:
                    configDataBits |= 0x0700;
                    break;
                case 8:
                    configDataBits |= 0x0800;
                    break;
                default:
                    configDataBits |= 0x0800;
                    break;
            }

            switch (parity)
            {
                case Parity.Odd:
                    configDataBits |= 0x0010;
                    break;
                case Parity.Even:
                    configDataBits |= 0x0020;
                    break;
            }

            switch (stopBits)
            {
                case StopBits.One:
                    configDataBits |= 0;
                    break;
                case StopBits.Two:
                    configDataBits |= 2;
                    break;
            }
            setConfigSingle(SILABSER_SET_LINE_CTL_REQUEST_CODE, configDataBits);
        }

        public override bool CD
        {
            get
            {
                return false;  // TODO
            }
        }

        public override bool Cts
        {
            get
            {
                return false;  // TODO
            }
        }

        public override bool Dsr
        {
            get
            {
                return false;  // TODO
            }
        }

        public override bool Dtr
        {
            get
            {
                return true;
            }
            set
            {
            }
        }

        public override bool RI
        {
            get
            {
                return false;  // TODO
            }
        }

        public override bool Rts
        {
            get
            {
                return true;
            }
            set
            {
            }
        }

        public override bool PurgeHwBuffers(bool purgeReadBuffers, bool purgeWriteBuffers)
        {
            int value = (purgeReadBuffers ? FLUSH_READ_CODE : 0)
                    | (purgeWriteBuffers ? FLUSH_WRITE_CODE : 0);

            if (value != 0)
            {
                setConfigSingle(SILABSER_FLUSH_REQUEST_CODE, value);
            }

            return true;
        }
    }
}
