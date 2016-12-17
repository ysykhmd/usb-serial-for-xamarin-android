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
using System.IO;

using Android.Hardware.Usb;
using Android.OS;
using Android.Util;

using Java.Nio;

namespace Aid.UsbSerial
{
    /**
     * USB CDC/ACM serial driver implementation.
     *
     * @author mike wakerly (opensource@hoho.com)
     * @see <a
     *      href="http://www.usb.org/developers/devclass_docs/usbcdc11.pdf">Universal
     *      Serial Bus Class Definitions for Communication Devices, v1.1</a>
     */
	internal class CdcAcmSerialPort : UsbSerialPort
    {
        private const string Tag = "CdcAcmSerialPort";

        private static int USB_RECIP_INTERFACE = 0x01;
        private static int USB_RT_ACM = UsbConstants.UsbTypeClass | USB_RECIP_INTERFACE;
        UsbAddressing a = UsbAddressing.DirMask;
        private static int SET_LINE_CODING = 0x20;  // USB CDC 1.1 section 6.2
        private static int GET_LINE_CODING = 0x21;
        private static int SET_CONTROL_LINE_STATE = 0x22;
        private static int SEND_BREAK = 0x23;

        private bool mEnableAsyncReads;
        private UsbInterface mControlInterface;
        private UsbInterface mDataInterface;

        private UsbEndpoint mControlEndpoint;
        private UsbEndpoint mReadEndpoint;
        private UsbEndpoint mWriteEndpoint;

        private bool mRts = false;
        private bool mDtr = false;

		public CdcAcmSerialPort(UsbManager usbManager, UsbDevice usbDevice, int portNumber)
            : base(usbManager, usbDevice, portNumber)
        {
            // Disabled because it is not work well under SmartThreadPool.
            //mEnableAsyncReads = (Build.VERSION.SdkInt >= BuildVersionCodes.JellyBeanMr1);
            mEnableAsyncReads = false;
        }

        public override void Open()
        {
			if (IsOpened) {
				return;
			}

			bool openedSuccessfully = false;
            try
            {
				CreateConnection();

                Log.Debug(Tag, "claiming interfaces, count=" + UsbDevice.InterfaceCount);
                mControlInterface = UsbDevice.GetInterface(0);
                Log.Debug(Tag, "Control iface=" + mControlInterface);
                // class should be USB_CLASS_COMM

                if (!Connection.ClaimInterface(mControlInterface, true))
                {
                    throw new IOException("Could not claim control interface.");
                }
                mControlEndpoint = mControlInterface.GetEndpoint(0);
                Log.Debug(Tag, "Control endpoint direction: " + mControlEndpoint.Direction);

                Log.Debug(Tag, "Claiming data interface.");
                mDataInterface = UsbDevice.GetInterface(1);
                Log.Debug(Tag, "data iface=" + mDataInterface);
                // class should be USB_CLASS_CDC_DATA

                if (!Connection.ClaimInterface(mDataInterface, true))
                {
                    throw new IOException("Could not claim data interface.");
                }
                mReadEndpoint = mDataInterface.GetEndpoint(1);
                Log.Debug(Tag, "Read endpoint direction: " + mReadEndpoint.Direction);
                mWriteEndpoint = mDataInterface.GetEndpoint(0);
                Log.Debug(Tag, "Write endpoint direction: " + mWriteEndpoint.Direction);
                if (mEnableAsyncReads)
                {
                    Log.Debug(Tag, "Async reads enabled");
                }
                else
                {
                    Log.Debug(Tag, "Async reads disabled.");
                }
				ResetParameters();
				openedSuccessfully = true;
            }
            finally {
				if (openedSuccessfully) {
					IsOpened = true;
					StartUpdating ();
				} else {
					CloseConnection();
				}
			}
        }

        private int SendAcmControlMessage(int request, int value, byte[] buf)
        {
            return Connection.ControlTransfer((UsbAddressing)USB_RT_ACM, request, value, 0, buf, buf != null ? buf.Length : 0, 5000);
        }

        public override void Close()
        {
			StopUpdating ();
			CloseConnection ();
			IsOpened = false;
        }

        protected override int ReadInternal()
        {
            if (mEnableAsyncReads)
            {
                UsbRequest request = new UsbRequest();
                try
                {
                    request.Initialize(Connection, mReadEndpoint);
                    ByteBuffer buf = ByteBuffer.Wrap(TempReadBuffer);
                    if (!request.Queue(buf, TempReadBuffer.Length))
                    {
                        throw new IOException("Error queueing request.");
                    }

                    UsbRequest response = Connection.RequestWait();
                    if (response == null)
                    {
                        throw new IOException("Null response");
                    }

                    int nread = buf.Position();
                    if (nread > 0)
                    {
                        //Log.Debug(Tag, HexDump.DumpHexString(TempReadBuffer, 0, Math.Min(32, TempReadBuffer.Length)));
                        return nread;
                    }
                    else
                    {
                        return 0;
                    }
                }
                finally
                {
                    request.Close();
                }
            }

            int numBytesRead;
            lock (mInternalReadBufferLock)
            {
                int readAmt = Math.Min(TempReadBuffer.Length, InternalReadBuffer.Length);
                numBytesRead = Connection.BulkTransfer(mReadEndpoint, InternalReadBuffer, readAmt, 0);
                if (numBytesRead < 0)
                {
                    // This sucks: we get -1 on timeout, not 0 as preferred.
                    // We *should* use UsbRequest, except it has a bug/api oversight
                    // where there is no way to determine the number of bytes read
                    // in response :\ -- http://b.android.com/28023
                    if (0 == int.MaxValue)
                    {
                        // Hack: Special case "~infinite timeout" as an error.
                        return -1;
                    }
                    return 0;
                }
                Array.Copy(InternalReadBuffer, 0, TempReadBuffer, 0, numBytesRead);
            }
            return numBytesRead;
        }

        public override int Write(byte[] src, int timeoutMillis)
        {
            // TODO(mikey): Nearly identical to FtdiSerial write. Refactor.
            int offset = 0;

            while (offset < src.Length)
            {
                int writeLength;
                int amtWritten;

                lock (mWriteBufferLock)
                {
                    byte[] writeBuffer;

                    writeLength = Math.Min(src.Length - offset, MainWriteBuffer.Length);
                    if (offset == 0)
                    {
                        writeBuffer = src;
                    }
                    else
                    {
                        // bulkTransfer does not support offsets, make a copy.
                        Array.Copy(src, offset, MainWriteBuffer, 0, writeLength);
                        writeBuffer = MainWriteBuffer;
                    }

                    amtWritten = Connection.BulkTransfer(mWriteEndpoint, writeBuffer, writeLength,
                            timeoutMillis);
                }
                if (amtWritten <= 0)
                {
                    throw new IOException("Error writing " + writeLength
                            + " bytes at offset " + offset + " length=" + src.Length);
                }

                Log.Debug(Tag, "Wrote amt=" + amtWritten + " attempted=" + writeLength);
                offset += amtWritten;
            }
            return offset;
        }

        protected override void SetParameters(int baudRate, int dataBits, StopBits stopBits, Parity parity)
        {
            byte stopBitsByte;
            switch (stopBits)
            {
                case StopBits.One:
                    stopBitsByte = 0;
                    break;
                case StopBits.OnePointFive:
                    stopBitsByte = 1;
                    break;
                case StopBits.Two:
                    stopBitsByte = 2;
                    break;
                default: throw new ArgumentException("Bad value for stopBits: " + stopBits);
            }

            byte parityBitesByte;
            switch (parity)
            {
                case Parity.None:
                    parityBitesByte = 0;
                    break;
                case Parity.Odd:
                    parityBitesByte = 1;
                    break;
                case Parity.Even:
                    parityBitesByte = 2;
                    break;
                case Parity.Mark:
                    parityBitesByte = 3;
                    break;
                case Parity.Space:
                    parityBitesByte = 4;
                    break;
                default: throw new ArgumentException("Bad value for parity: " + parity);
            }

            byte[] msg = {
                (byte) ( baudRate & 0xff),
                (byte) ((baudRate >> 8 ) & 0xff),
                (byte) ((baudRate >> 16) & 0xff),
                (byte) ((baudRate >> 24) & 0xff),
                stopBitsByte,
                parityBitesByte,
                (byte) dataBits};
            SendAcmControlMessage(SET_LINE_CODING, 0, msg);
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
                return mDtr;
            }
            set
            {
                mDtr = value;
                SetDtrRts();
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
                return mRts;
            }
            set
            {
                mRts = value;
                SetDtrRts();
            }
        }

        private void SetDtrRts()
        {
            int value = (mRts ? 0x2 : 0) | (mDtr ? 0x1 : 0);
            SendAcmControlMessage(SET_CONTROL_LINE_STATE, value, null);
        }
    }
}

