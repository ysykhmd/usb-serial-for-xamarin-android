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
     * A {@link CommonUsbSerialPort} implementation for a variety of FTDI devices
     * <p>
     * This driver is based on <a
     * href="http://www.intra2net.com/en/developer/libftdi">libftdi</a>, and is
     * copyright and subject to the following terms:
     *
     * <pre>
     *   Copyright (C) 2003 by Intra2net AG
     *
     *   This program is free software; you can redistribute it and/or modify
     *   it under the terms of the GNU Lesser General Public License
     *   version 2.1 as published by the Free Software Foundation;
     *
     *   opensource@intra2net.com
     *   http://www.intra2net.com/en/developer/libftdi
     * </pre>
     *
     * </p>
     * <p>
     * Some FTDI devices have not been tested; see later listing of supported and
     * unsupported devices. Devices listed as "supported" support the following
     * features:
     * <ul>
     * <li>Read and write of serial data (see
     * {@link CommonUsbSerialPort#read(byte[], int)} and
     * {@link CommonUsbSerialPort#write(byte[], int)}.</li>
     * <li>Setting serial line parameters (see
     * {@link CommonUsbSerialPort#setParameters(int, int, int, int)}.</li>
     * </ul>
     * </p>
     * <p>
     * Supported and tested devices:
     * <ul>
     * <li>{@value DeviceType#TYPE_R}</li>
     * </ul>
     * </p>
     * <p>
     * Unsupported but possibly working devices (please contact the author with
     * feedback or patches):
     * <ul>
     * <li>{@value DeviceType#TYPE_2232C}</li>
     * <li>{@value DeviceType#TYPE_2232H}</li>
     * <li>{@value DeviceType#TYPE_4232H}</li>
     * <li>{@value DeviceType#TYPE_AM}</li>
     * <li>{@value DeviceType#TYPE_BM}</li>
     * </ul>
     * </p>
     *
     * @author mike wakerly (opensource@hoho.com)
     * @see <a href="https://github.com/mik3y/usb-serial-for-android">USB Serial
     *      for Android project page</a>
     * @see <a href="http://www.ftdichip.com/">FTDI Homepage</a>
     * @see <a href="http://www.intra2net.com/en/developer/libftdi">libftdi</a>
     */
	public class FtdiSerialPort : UsbSerialPort
    {
		/**
         * FTDI chip types.
         */
		private enum DeviceType
		{
			TYPE_BM,
			TYPE_AM,
			TYPE_2232C,
			TYPE_R,
			TYPE_2232H,
			TYPE_4232H
		}

        public static int USB_TYPE_STANDARD = 0x00 << 5;
        public static int USB_TYPE_CLASS = 0x00 << 5;
        public static int USB_TYPE_VENDOR = 0x00 << 5;
        public static int USB_TYPE_RESERVED = 0x00 << 5;

        public static int USB_RECIP_DEVICE = 0x00;
        public static int USB_RECIP_INTERFACE = 0x01;
        public static int USB_RECIP_ENDPOINT = 0x02;
        public static int USB_RECIP_OTHER = 0x03;

        public static int USB_ENDPOINT_IN = 0x80;
        public static int USB_ENDPOINT_OUT = 0x00;

        public static int USB_WRITE_TIMEOUT_MILLIS = 5000;
        public static int USB_READ_TIMEOUT_MILLIS = 5000;

        // From ftdi.h
        /**
         * Reset the port.
         */
        private static int SIO_RESET_REQUEST = 0;

        /**
         * Set the modem control register.
         */
        private static int SIO_MODEM_CTRL_REQUEST = 1;

        /**
         * Set flow control register.
         */
        private static int SIO_SET_FLOW_CTRL_REQUEST = 2;

        /**
         * Set baud rate.
         */
        private static int SIO_SET_BAUD_RATE_REQUEST = 3;

        /**
         * Set the data characteristics of the port.
         */
        private static int SIO_SET_DATA_REQUEST = 4;

        private static int SIO_RESET_SIO = 0;
        private static int SIO_RESET_PURGE_RX = 1;
        private static int SIO_RESET_PURGE_TX = 2;

        public static int FtdiDEVICE_OUT_REQTYPE =
                UsbConstants.UsbTypeVendor | USB_RECIP_DEVICE | USB_ENDPOINT_OUT;

        public static int FtdiDEVICE_IN_REQTYPE =
                UsbConstants.UsbTypeVendor | USB_RECIP_DEVICE | USB_ENDPOINT_IN;

        /**
         * Length of the modem status header, transmitted with every read.
         */
        private static int MODEM_STATUS_HEADER_LENGTH = 2;

        private string TAG = "FtdiSerialPort";

        private DeviceType mType;

        private int mInterface = 0; /* INTERFACE_ANY */

        private int mMaxPacketSize = 64; // TODO(mikey): detect

        /**
         * Due to http://b.android.com/28023 , we cannot use UsbRequest async reads
         * since it gives no indication of number of bytes read. Set this to
         * {@code true} on platforms where it is fixed.
         */
        private static bool ENABLE_ASYNC_READS = false;

        public FtdiSerialPort(UsbManager manager, UsbDevice device, int portNumber)
            : base(manager, device, portNumber)
        {
        }

        /**
         * Filter FTDI status bytes from buffer
         * @param src The source buffer (which contains status bytes)
         * @param dest The destination buffer to write the status bytes into (can be src)
         * @param totalBytesRead Number of bytes read to src
         * @param maxPacketSize The USB endpoint max packet size
         * @return The number of payload bytes
         */
        private int FilterStatusBytes(byte[] src, byte[] dest, int totalBytesRead, int maxPacketSize)
        {
            int packetsCount = totalBytesRead / maxPacketSize + (totalBytesRead % maxPacketSize == 0 ? 0 : 1);
            for (int packetIdx = 0; packetIdx < packetsCount; ++packetIdx)
            {
                int count = (packetIdx == (packetsCount - 1))
                        ? (totalBytesRead % maxPacketSize) - MODEM_STATUS_HEADER_LENGTH
                        : maxPacketSize - MODEM_STATUS_HEADER_LENGTH;
                if (count > 0)
                {
                    Array.Copy(src,
                            packetIdx * maxPacketSize + MODEM_STATUS_HEADER_LENGTH,
                            dest,
                            packetIdx * (maxPacketSize - MODEM_STATUS_HEADER_LENGTH),
                            count);
                }
            }

            return totalBytesRead - (packetsCount * 2);
        }

        public void Reset()
        {
            int result = Connection.ControlTransfer((UsbAddressing)FtdiDEVICE_OUT_REQTYPE, SIO_RESET_REQUEST, SIO_RESET_SIO, 0 /* index */, null, 0, USB_WRITE_TIMEOUT_MILLIS);
            if (result != 0)
            {
                throw new IOException("Reset failed: result=" + result);
            }

            // TODO(mikey): autodetect.
            mType = DeviceType.TYPE_R;
        }

        public override void Open()
        {
            bool openedSuccessfully = false;
            try
            {
				CreateConnection();

                for (int i = 0; i < UsbDevice.InterfaceCount; i++)
                {
                    if (Connection.ClaimInterface(UsbDevice.GetInterface(i), true))
                    {
                        Log.Debug(TAG, "claimInterface " + i + " SUCCESS");
                    }
                    else
                    {
                        throw new IOException("Error claiming interface " + i);
                    }
                }
                Reset();
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

        public override void Close()
        {
			StopUpdating ();
			CloseConnection ();
			IsOpened = false;
        }

        protected override int ReadInternal(byte[] dest, int timeoutMillis)
        {
            UsbEndpoint endpoint = UsbDevice.GetInterface(0).GetEndpoint(0);

            if (ENABLE_ASYNC_READS)
            {
                int readAmt;
                lock (mInternalReadBufferLock)
                {
                    // mReadBuffer is only used for maximum read size.
                    readAmt = Math.Min(dest.Length, mInternalReadBuffer.Length);
                }

                UsbRequest request = new UsbRequest();
                request.Initialize(Connection, endpoint);

                ByteBuffer buf = ByteBuffer.Wrap(dest);
                if (!request.Queue(buf, readAmt))
                {
                    throw new IOException("Error queueing request.");
                }

                UsbRequest response = Connection.RequestWait();
                if (response == null)
                {
                    throw new IOException("Null response");
                }

                int payloadBytesRead = buf.Position() - MODEM_STATUS_HEADER_LENGTH;
                if (payloadBytesRead > 0)
                {
                    //Log.Debug(TAG, HexDump.DumpHexString(dest, 0, Math.Min(32, dest.Length)));
                    return payloadBytesRead;
                }
                else
                {
                    return 0;
                }
            }
            else
            {
                int totalBytesRead;

                lock (mInternalReadBufferLock)
                {
                    int readAmt = Math.Min(dest.Length, mInternalReadBuffer.Length);
                    totalBytesRead = Connection.BulkTransfer(endpoint, mInternalReadBuffer,
                            readAmt, timeoutMillis);

                    if (totalBytesRead < MODEM_STATUS_HEADER_LENGTH)
                    {
                        throw new IOException("Expected at least " + MODEM_STATUS_HEADER_LENGTH + " bytes");
                    }

                    return FilterStatusBytes(mInternalReadBuffer, dest, totalBytesRead, endpoint.MaxPacketSize);
                }
            }
        }

        public override int Write(byte[] src, int timeoutMillis)
        {
            UsbEndpoint endpoint = UsbDevice.GetInterface(0).GetEndpoint(1);
            int offset = 0;

            while (offset < src.Length)
            {
                int writeLength;
                int amtWritten;

                lock (mWriteBufferLock)
                {
                    byte[] writeBuffer;

                    writeLength = Math.Min(src.Length - offset, mWriteBuffer.Length);
                    if (offset == 0)
                    {
                        writeBuffer = src;
                    }
                    else
                    {
                        // bulkTransfer does not support offsets, make a copy.
                        Array.Copy(src, offset, mWriteBuffer, 0, writeLength);
                        writeBuffer = mWriteBuffer;
                    }

                    amtWritten = Connection.BulkTransfer(endpoint, writeBuffer, writeLength,
                            timeoutMillis);
                }

                if (amtWritten <= 0)
                {
                    throw new IOException("Error writing " + writeLength
                            + " bytes at offset " + offset + " length=" + src.Length);
                }

                Log.Debug(TAG, "Wrote amtWritten=" + amtWritten + " attempted=" + writeLength);
                offset += amtWritten;
            }
            return offset;
        }

        private int SetBaudRate(int baudRate)
        {
            long[] vals = ConvertBaudrate(baudRate);
            long actualBaudrate = vals[0];
            long index = vals[1];
            long value = vals[2];
            int result = Connection.ControlTransfer((UsbAddressing)FtdiDEVICE_OUT_REQTYPE, SIO_SET_BAUD_RATE_REQUEST, (int)value, (int)index, null, 0, USB_WRITE_TIMEOUT_MILLIS);
            if (result != 0)
            {
                throw new IOException("Setting baudrate failed: result=" + result);
            }
            return (int)actualBaudrate;
        }

        protected override void SetParameters(int baudRate, int dataBits, StopBits stopBits, Parity parity)
        {
            SetBaudRate(baudRate);

            int config = dataBits;

            switch (parity)
            {
                case Parity.None:
                    config |= (0x00 << 8);
                    break;
                case Parity.Odd:
                    config |= (0x01 << 8);
                    break;
                case Parity.Even:
                    config |= (0x02 << 8);
                    break;
                case Parity.Mark:
                    config |= (0x03 << 8);
                    break;
                case Parity.Space:
                    config |= (0x04 << 8);
                    break;
                default:
                    throw new ArgumentException("Unknown parity value: " + parity);
            }

            switch (stopBits)
            {
                case StopBits.One:
                    config |= (0x00 << 11);
                    break;
                case StopBits.OnePointFive:
                    config |= (0x01 << 11);
                    break;
                case StopBits.Two:
                    config |= (0x02 << 11);
                    break;
                default:
                    throw new ArgumentException("Unknown stopBits value: " + stopBits);
            }

            int result = Connection.ControlTransfer((UsbAddressing)FtdiDEVICE_OUT_REQTYPE, SIO_SET_DATA_REQUEST, config, 0 /* index */, null, 0, USB_WRITE_TIMEOUT_MILLIS);
            if (result != 0)
            {
                throw new IOException("Setting parameters failed: result=" + result);
            }
        }

        private long[] ConvertBaudrate(int baudrate)
        {
            // TODO(mikey): Braindead transcription of libfti method.  Clean up,
            // using more idiomatic Java where possible.
            int divisor = 24000000 / baudrate;
            int bestDivisor = 0;
            int bestBaud = 0;
            int bestBaudDiff = 0;
            int[] fracCode = new int[] { 0, 3, 2, 4, 1, 5, 6, 7 };

            for (int i = 0; i < 2; i++)
            {
                int tryDivisor = divisor + i;
                int baudEstimate;
                int baudDiff;

                if (tryDivisor <= 8)
                {
                    // Round up to minimum supported divisor
                    tryDivisor = 8;
                }
                else if (mType != DeviceType.TYPE_AM && tryDivisor < 12)
                {
                    // BM doesn't support divisors 9 through 11 inclusive
                    tryDivisor = 12;
                }
                else if (divisor < 16)
                {
                    // AM doesn't support divisors 9 through 15 inclusive
                    tryDivisor = 16;
                }
                else
                {
                    if (mType == DeviceType.TYPE_AM)
                    {
                        // TODO
                    }
                    else
                    {
                        if (tryDivisor > 0x1FFFF)
                        {
                            // Round down to maximum supported divisor value (for
                            // BM)
                            tryDivisor = 0x1FFFF;
                        }
                    }
                }

                // Get estimated baud rate (to nearest integer)
                baudEstimate = (24000000 + (tryDivisor / 2)) / tryDivisor;

                // Get absolute difference from requested baud rate
                if (baudEstimate < baudrate)
                {
                    baudDiff = baudrate - baudEstimate;
                }
                else
                {
                    baudDiff = baudEstimate - baudrate;
                }

                if (i == 0 || baudDiff < bestBaudDiff)
                {
                    // Closest to requested baud rate so far
                    bestDivisor = tryDivisor;
                    bestBaud = baudEstimate;
                    bestBaudDiff = baudDiff;
                    if (baudDiff == 0)
                    {
                        // Spot on! No point trying
                        break;
                    }
                }
            }

            // Encode the best divisor value
            long encodedDivisor = (bestDivisor >> 3) | (fracCode[bestDivisor & 7] << 14);
            // Deal with special cases for encoded value
            if (encodedDivisor == 1)
            {
                encodedDivisor = 0; // 3000000 baud
            }
            else if (encodedDivisor == 0x4001)
            {
                encodedDivisor = 1; // 2000000 baud (BM only)
            }

            // Split into "value" and "index" values
            long value = encodedDivisor & 0xFFFF;
            long index;
            if (mType == DeviceType.TYPE_2232C || mType == DeviceType.TYPE_2232H
                    || mType == DeviceType.TYPE_4232H)
            {
                index = (encodedDivisor >> 8) & 0xffff;
                index &= 0xFF00;
                index |= 0 /* TODO mIndex */;
            }
            else
            {
                index = (encodedDivisor >> 16) & 0xffff;
            }

            // Return the nearest baud rate
            return new long[] {
                bestBaud, index, value
        };
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
                return false;
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
                return false;
            }
            set
            {
            }
        }

        public override bool PurgeHwBuffers(bool purgeReadBuffers, bool purgeWriteBuffers)
        {
            if (purgeReadBuffers)
            {
                int result = Connection.ControlTransfer((UsbAddressing)FtdiDEVICE_OUT_REQTYPE, SIO_RESET_REQUEST, SIO_RESET_PURGE_RX, 0 /* index */, null, 0, USB_WRITE_TIMEOUT_MILLIS);
                if (result != 0)
                {
                    throw new IOException("Flushing RX failed: result=" + result);
                }
            }

            if (purgeWriteBuffers)
            {
                int result = Connection.ControlTransfer((UsbAddressing)FtdiDEVICE_OUT_REQTYPE, SIO_RESET_REQUEST, SIO_RESET_PURGE_TX, 0 /* index */, null, 0, USB_WRITE_TIMEOUT_MILLIS);
                if (result != 0)
                {
                    throw new IOException("Flushing RX failed: result=" + result);
                }
            }
            return true;
        }
    }
}

