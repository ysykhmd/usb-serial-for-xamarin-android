/* Copyright 2015 Yasuyuki Hamada <yasuyuki_hamada@agri-info-design.com>
 * 
 *  This library is free software; you can redistribute it and/or
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
 * 
 * +++
 * 
 * Original Java source code is ported to usb-serial-for-android
 * by Felix Hädicke <felixhaedicke@web.de>
 *
 * Based on the pyprolific driver written
 * by Emmanuel Blot <emmanuel.blot@free.fr>
 * See https://github.com/eblot/pyftdi
 */

using System;
using System.IO;
using System.Threading;

using Android.Hardware.Usb;
using Android.Util;

using Java.Nio;

namespace Aid.UsbSerial
{
    public class ProlificSerialPort : UsbSerialPort
    {
        const string TAG = "ProlificSerialPort";

        const int USB_READ_TIMEOUT_MILLIS = 1000;
        const int USB_WRITE_TIMEOUT_MILLIS = 5000;

        const int USB_RECIP_INTERFACE = 0x01;

        const int VENDOR_READ_REQUEST_TYPE = (int)UsbAddressing.In | UsbConstants.UsbTypeVendor;
        const int VENDOR_READ_REQUEST = 0x01;

        const int VENDOR_WRITE_REQUEST_TYPE = (int)UsbAddressing.Out | UsbConstants.UsbTypeVendor;
        const int VENDOR_WRITE_REQUEST = 0x01;

        const int ProlificCTRL_OUT_REQTYPE = (int)UsbAddressing.Out | UsbConstants.UsbTypeClass | USB_RECIP_INTERFACE;

        const int WRITE_ENDPOINT = 0x02;
        const int READ_ENDPOINT = 0x83;
        const int INTERRUPT_ENDPOINT = 0x81;

        const int FLUSH_RX_REQUEST = 0x08;
        const int FLUSH_TX_REQUEST = 0x09;

        const int SET_LINE_REQUEST = 0x20;
        const int SET_CONTROL_REQUEST = 0x22;

        const int CONTROL_DTR = 0x01;
        const int CONTROL_RTS = 0x02;

        const int STATUS_FLAG_CD = 0x01;
        const int STATUS_FLAG_DSR = 0x02;
        const int STATUS_FLAG_RI = 0x08;
        const int STATUS_FLAG_CTS = 0x80;

        const int STATUS_BUFFER_SIZE = 10;
        const int STATUS_BYTE_IDX = 8;

        const int DEVICE_TYPE_HX = 0;
        const int DEVICE_TYPE_0 = 1;
        const int DEVICE_TYPE_1 = 2;

        int DeviceType;

        UsbEndpoint ReadEndpoint;
        UsbEndpoint WriteEndpoint;
        UsbEndpoint InterruptEndpoint;

        volatile int ControlLinesValue = 0;
        volatile int StatusBuffer = 0;
        bool StopReadStatusThread = false;
        IOException ReadStatusException = null;

        int CurrentBaudRate;
        int CurrentDataBits;
        StopBits CurrentStopBits;
        Parity CurrentParity;

        public ProlificSerialPort(UsbManager manager, UsbDevice device, int portNumber)
            : base(manager, device, portNumber)
        {
        }

        byte[] InControlTransfer(int requestType, int request,
                int value, int index, int length)
        {
            byte[] buffer = new byte[length];
            int result = Connection.ControlTransfer((UsbAddressing)requestType, request, value, index, buffer, length, USB_READ_TIMEOUT_MILLIS);
            if (result != length)
            {
                throw new IOException(string.Format("ControlTransfer with value 0x{0:x} failed: {1}", value, result));
            }
            return buffer;
        }

        void OutControlTransfer(int requestType, int request,
                int value, int index, byte[] data)
        {
            int length = (data == null) ? 0 : data.Length;
            int result = Connection.ControlTransfer((UsbAddressing)requestType, request, value, index, data, length, USB_WRITE_TIMEOUT_MILLIS);
            if (result != length)
            {
                throw new IOException(string.Format("ControlTransfer with value 0x{0:x} failed: {1}", value, result));
            }
        }

        byte[] VendorIn(int value, int index, int length)
        {
            return InControlTransfer(VENDOR_READ_REQUEST_TYPE, VENDOR_READ_REQUEST, value, index, length);
        }

        void VendorOut(int value, int index, byte[] data)
        {
            OutControlTransfer(VENDOR_WRITE_REQUEST_TYPE, VENDOR_WRITE_REQUEST, value, index, data);
        }

        void ResetDevice()
        {
            PurgeHwBuffers(true, true);
        }

        void CtrlOut(int request, int value, int index, byte[] data)
        {
            OutControlTransfer(ProlificCTRL_OUT_REQTYPE, request, value, index, data);
        }

        void DoBlackMagic()
        {
            VendorIn(0x8484, 0, 1);
            VendorOut(0x0404, 0, null);
            VendorIn(0x8484, 0, 1);
            VendorIn(0x8383, 0, 1);
            VendorIn(0x8484, 0, 1);
            VendorOut(0x0404, 1, null);
            VendorIn(0x8484, 0, 1);
            VendorIn(0x8383, 0, 1);
            VendorOut(0, 1, null);
            VendorOut(1, 0, null);
            VendorOut(2, (DeviceType == DEVICE_TYPE_HX) ? 0x44 : 0x24, null);
        }

        void SetControlLines(int newControlLinesValue)
        {
            CtrlOut(SET_CONTROL_REQUEST, newControlLinesValue, 0, null);
            ControlLinesValue = newControlLinesValue;
        }


        object ReadStatusThreadFunction()
        {
            byte[] readStatusBuffer = new byte[STATUS_BUFFER_SIZE];
            int ReadStatusBytesCount;
            try
            {
                while (!StopReadStatusThread)
                {
                    ReadStatusBytesCount = Connection.BulkTransfer(InterruptEndpoint,
                            readStatusBuffer,
                            STATUS_BUFFER_SIZE,
                            500);
                    if (ReadStatusBytesCount == STATUS_BUFFER_SIZE)
                    {
                        StatusBuffer = readStatusBuffer[STATUS_BYTE_IDX] & 0xff;
                    }
                    else if (ReadStatusBytesCount > 0)
                    {
                        throw new IOException(string.Format("Invalid CTS / DSR / CD / RI status buffer received, expected {0} bytes, but received {1} bytes.", STATUS_BUFFER_SIZE, ReadStatusBytesCount));
                    }
                }
            }
            catch (IOException e)
            {
                ReadStatusException = e;
            }
            return null;
        }

        int Status
        {
            get
            {
                /* throw and clear an exception which occured in the status read thread */
                if (ReadStatusException != null)
                {
                    IOException readStatusException = ReadStatusException;
                    ReadStatusException = null;
                    throw readStatusException;
                }

                return StatusBuffer;
            }
        }

        bool TestStatusFlag(int flag)
        {
            return ((Status & flag) == flag);
        }

        public override void Open()
        {
            bool openedSuccessfully = false;
            try
            {
                CreateConnection();

                UsbInterface usbInterface = UsbDevice.GetInterface(0);
                if (!Connection.ClaimInterface(usbInterface, true))
                {
                    throw new IOException("Error claiming Prolific interface 0");
                }

                for (int i = 0; i < usbInterface.EndpointCount; ++i)
                {
                    UsbEndpoint currentEndpoint = usbInterface.GetEndpoint(i);

                    switch ((int)currentEndpoint.Address)
                    {
                        case READ_ENDPOINT:
                            ReadEndpoint = currentEndpoint;
                            break;

                        case WRITE_ENDPOINT:
                            WriteEndpoint = currentEndpoint;
                            break;

                        case INTERRUPT_ENDPOINT:
                            InterruptEndpoint = currentEndpoint;
                            break;
                    }
                }

                if (UsbDevice.DeviceClass == UsbClass.Comm)
                {
                    DeviceType = DEVICE_TYPE_0;
                }
                else
                {
                    try
                    {
                        byte[] rawDescriptors = Connection.GetRawDescriptors();
                        byte maxPacketSize0 = rawDescriptors[7];
                        if (maxPacketSize0 == 64)
                        {
                            DeviceType = DEVICE_TYPE_HX;
                        }
                        else if ((UsbDevice.DeviceClass == UsbClass.PerInterface) || (UsbDevice.DeviceClass == UsbClass.VendorSpec))
                        {
                            DeviceType = DEVICE_TYPE_1;
                        }
                        else
                        {
                            Log.Warn(TAG, "Could not detect PL2303 subtype, "
                                + "Assuming that it is a HX device");
                            DeviceType = DEVICE_TYPE_HX;
                        }
                    }
                    catch (Exception e)
                    {
                        DeviceType = DEVICE_TYPE_HX;
                        Log.Error(TAG, "An unexpected exception occured while trying "
                                + "to detect PL2303 subtype", e);
                    }
                }

                SetControlLines(ControlLinesValue);
                ResetDevice();
                DoBlackMagic();
                ResetParameters();

                //

                byte[] buffer = new byte[STATUS_BUFFER_SIZE];
                int readBytes = Connection.BulkTransfer(InterruptEndpoint, buffer, STATUS_BUFFER_SIZE, 100);
                if (readBytes != STATUS_BUFFER_SIZE)
                {
                    Log.Warn(TAG, "Could not read initial CTS / DSR / CD / RI status");
                }
                else
                {
                    StatusBuffer = buffer[STATUS_BYTE_IDX] & 0xff;
                }

#if UseSmartThreadPool
                if (ThreadPool != null)
                {
                    ThreadPool.QueueWorkItem(o => ReadStatusThreadFunction());
                }
                else
                {
                    System.Threading.ThreadPool.QueueUserWorkItem(o => ReadStatusThreadFunction());
                }
#else
                ThreadPool.QueueUserWorkItem(o => ReadStatusThreadFunction());
#endif
                Dtr = true;
                openedSuccessfully = true;
            }
            finally
            {
                if (openedSuccessfully)
                {
                    IsOpened = true;
                    StartUpdating();
                }
                else
                {
                    CloseConnection();
                }
            }
        }

        public override void Close()
        {
            StopUpdating();

            try
            {
                StopReadStatusThread = true;
                if (Connection != null)
                {
                    ResetDevice();
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                try
                {
                    if (Connection != null)
                    {
                        Connection.ReleaseInterface(UsbDevice.GetInterface(0));
                    }
                }
                finally
                {
                    CloseConnection();
                    IsOpened = false;
                    Dtr = false;
                }
            }
        }

        // ガベージを増やさないために関数内で変数の宣言はせず、すべて関数外で宣言する
        int ReadInternalNumBytesRead;
        protected override int ReadInternal()
        {
            if (Connection == null)
            {
                return 0;
            }
            ReadInternalNumBytesRead = Connection.BulkTransfer(ReadEndpoint, TempReadBuffer, TempReadBuffer.Length, DEFAULT_READ_TIMEOUT_MILLISEC);
            if (ReadInternalNumBytesRead < 0)
            {
                return 0;
            }
            return ReadInternalNumBytesRead;
        }

        // ガベージを増やさないために関数内で変数の宣言はせず、すべて関数外で宣言する
        int writeSrcBufferOffset;
        int writeLength;
        int amtWritten;
        byte[] writeBuffer;
        public override int Write(byte[] src, int timeoutMillis)
        {
            writeSrcBufferOffset = 0;

            while (writeSrcBufferOffset < src.Length)
            {
                lock (MainWriteBufferLock)
                {
                    writeLength = Math.Min(src.Length - writeSrcBufferOffset, MainWriteBuffer.Length);
                    if (writeSrcBufferOffset == 0)
                    {
                        writeBuffer = src;
                    }
                    else
                    {
                        // bulkTransfer does not support offsets, make a copy.
                        Array.Copy(src, writeSrcBufferOffset, MainWriteBuffer, 0, writeLength);
                        writeBuffer = MainWriteBuffer;
                    }

                    amtWritten = Connection.BulkTransfer(WriteEndpoint,
                            writeBuffer, writeLength, timeoutMillis);
                }

                if (amtWritten <= 0)
                {
                    throw new IOException("Error writing " + writeLength
                            + " bytes at offset " + writeSrcBufferOffset + " length="
                            + src.Length);
                }

                //Log.Debug(TAG, "Wrote amt=" + amtWritten + " attempted=" + writeLength);
                writeSrcBufferOffset += amtWritten;
            }
            return writeSrcBufferOffset;
        }

        protected override void SetParameters(int baudRate, int dataBits, StopBits stopBits, Parity parity)
        {
            if ((CurrentBaudRate == baudRate) && (CurrentDataBits == dataBits)
                    && (CurrentStopBits == stopBits) && (CurrentParity == parity))
            {
                // Make sure no action is performed if there is nothing to change
                return;
            }

            byte[] lineRequestData = new byte[7];

            lineRequestData[0] = (byte)(baudRate & 0xff);
            lineRequestData[1] = (byte)((baudRate >> 8) & 0xff);
            lineRequestData[2] = (byte)((baudRate >> 16) & 0xff);
            lineRequestData[3] = (byte)((baudRate >> 24) & 0xff);

            switch (stopBits)
            {
                case StopBits.One:
                    lineRequestData[4] = 0;
                    break;

                case StopBits.OnePointFive:
                    lineRequestData[4] = 1;
                    break;

                case StopBits.Two:
                    lineRequestData[4] = 2;
                    break;

                default:
                    throw new ArgumentException("Unknown stopBits value: " + stopBits);
            }

            switch (parity)
            {
                case Parity.None:
                    lineRequestData[5] = 0;
                    break;

                case Parity.Odd:
                    lineRequestData[5] = 1;
                    break;

                case Parity.Mark:
                    lineRequestData[5] = 3;
                    break;

                case Parity.Space:
                    lineRequestData[5] = 4;
                    break;

                default:
                    throw new ArgumentException("Unknown parity value: " + parity);
            }

            lineRequestData[6] = (byte)dataBits;

            CtrlOut(SET_LINE_REQUEST, 0, 0, lineRequestData);

            ResetDevice();

            CurrentBaudRate = baudRate;
            CurrentDataBits = dataBits;
            CurrentStopBits = stopBits;
            CurrentParity = parity;
        }

        public override bool CD
        {
            get
            {
                return TestStatusFlag(STATUS_FLAG_CD);
            }
        }

        public override bool Cts
        {
            get
            {
                return TestStatusFlag(STATUS_FLAG_CTS);
            }
        }

        public override bool Dsr
        {
            get
            {
                return TestStatusFlag(STATUS_FLAG_DSR);
            }
        }

        public override bool Dtr
        {
            get
            {
                return ((ControlLinesValue & CONTROL_DTR) == CONTROL_DTR);
            }
            set
            {
                int newControlLinesValue;
                if (value)
                {
                    newControlLinesValue = ControlLinesValue | CONTROL_DTR;
                }
                else
                {
                    newControlLinesValue = ControlLinesValue & ~CONTROL_DTR;
                }
                SetControlLines(newControlLinesValue);
            }
        }

        public override bool RI
        {
            get
            {
                return TestStatusFlag(STATUS_FLAG_RI);
            }
        }

        public override bool Rts
        {
            get
            {
                return ((ControlLinesValue & CONTROL_RTS) == CONTROL_RTS);
            }
            set
            {
                int newControlLinesValue;
                if (value)
                {
                    newControlLinesValue = ControlLinesValue | CONTROL_RTS;
                }
                else
                {
                    newControlLinesValue = ControlLinesValue & ~CONTROL_RTS;
                }
                SetControlLines(newControlLinesValue);
            }
        }

        public override bool PurgeHwBuffers(bool purgeReadBuffers, bool purgeWriteBuffers)
        {
            if (purgeReadBuffers)
            {
                VendorOut(FLUSH_RX_REQUEST, 0, null);
            }

            if (purgeWriteBuffers)
            {
                VendorOut(FLUSH_TX_REQUEST, 0, null);
            }

            return purgeReadBuffers || purgeWriteBuffers;
        }
    }
}
