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
using System.Threading;
using Android.Hardware.Usb;

namespace Aid.UsbSerial
{
    public abstract class UsbSerialPort
    {
        public const int DEFAULT_INTERNAL_READ_BUFFER_SIZE = 16 * 1024;
        public const int DEFAULT_TEMP_READ_BUFFER_SIZE = 16 * 1024;
        public const int DEFAULT_READ_BUFFER_SIZE = 16 * 1024;
        public const int DEFAULT_WRITE_BUFFER_SIZE = 16 * 1024;
		public const int DefaultBaudrate = 9600;
		public const int DefaultDataBits = 8;
		public const Parity DefaultParity = Parity.None;
		public const StopBits DefaultStopBits = StopBits.One;

        protected int mPortNumber;

        // non-null when open()
		protected UsbDeviceConnection Connection { get; set; }

        protected Object mInternalReadBufferLock = new Object();
        protected Object mReadBufferLock = new Object();
        protected Object mWriteBufferLock = new Object();

        /** Internal read buffer.  Guarded by {@link #mReadBufferLock}. */
        protected byte[] mInternalReadBuffer;
        protected byte[] mTempReadBuffer;
        protected byte[] mReadBuffer;
        protected int mReadBufferWriteCursor;
        protected int mReadBufferReadCursor;

        /** Internal write buffer.  Guarded by {@link #mWriteBufferLock}. */
        protected byte[] mWriteBuffer;

		private int mDataBits;

		private volatile bool _ContinueUpdating;
		public bool IsOpened { get; protected set; }
		public int Baudrate { get; set; }
		public int DataBits {
			get { return mDataBits; }
			set {
				if (value < 5 || 8 < value)
					throw new ArgumentOutOfRangeException ();
				mDataBits = value;
			}
		}
		public Parity Parity { get; set; }
		public StopBits StopBits { get; set; }

        public event EventHandler<DataReceivedEventArgs> DataReceived;


        public UsbSerialPort(UsbManager manager, UsbDevice device, int portNumber)
        {
			Baudrate = DefaultBaudrate;
			DataBits = DefaultDataBits;
			Parity = DefaultParity;
			StopBits = DefaultStopBits;

            UsbManager = manager;
			UsbDevice = device;
            mPortNumber = portNumber;

            mInternalReadBuffer = new byte[DEFAULT_INTERNAL_READ_BUFFER_SIZE];
            mTempReadBuffer = new byte[DEFAULT_TEMP_READ_BUFFER_SIZE];
            mReadBuffer = new byte[DEFAULT_READ_BUFFER_SIZE];
            mReadBufferReadCursor = 0;
            mReadBufferWriteCursor = 0;
            mWriteBuffer = new byte[DEFAULT_WRITE_BUFFER_SIZE];
        }

        public override string ToString()
        {
            return string.Format("<{0} device_name={1} device_id={2} port_number={3}>", this.GetType().Name, UsbDevice.DeviceName, UsbDevice.DeviceId, mPortNumber);
        }

        public UsbManager UsbManager
        {
            get; private set;
        }

        /**
         * Returns the currently-bound USB device.
         *
         * @return the device
         */
        public UsbDevice UsbDevice
        {
            get; private set;
        }

        /**
         * Sets the size of the internal buffer used to exchange data with the USB
         * stack for read operations.  Most users should not need to change this.
         *
         * @param bufferSize the size in bytes
         */
        public void SetReadBufferSize(int bufferSize)
        {
            if (bufferSize == mInternalReadBuffer.Length)
            {
                return;
            }
            lock (mInternalReadBufferLock)
            {
                mInternalReadBuffer = new byte[bufferSize];
            }
        }

        /**
         * Sets the size of the internal buffer used to exchange data with the USB
         * stack for write operations.  Most users should not need to change this.
         *
         * @param bufferSize the size in bytes
         */
        public void SetWriteBufferSize(int bufferSize)
        {
            lock (mWriteBufferLock)
            {
                if (bufferSize == mWriteBuffer.Length)
                {
                    return;
                }
                mWriteBuffer = new byte[bufferSize];
            }
        }

        // Members of IUsbSerialPort

        public int PortNumber
        {
            get { return mPortNumber; }
        }

        /**
         * Returns the device serial number
         *  @return serial number
         */
        public string Serial
        {
            get { return Connection.Serial; }
        }


		public abstract void Open ();

		public abstract void Close ();


		protected void CreateConnection()
		{
			if (UsbManager != null && UsbDevice != null) {
				lock (mReadBufferLock) {
					lock (mWriteBufferLock) {
						Connection = UsbManager.OpenDevice (UsbDevice);
					}
				}
			}
		}


		protected void CloseConnection()
		{
			if (Connection != null) {
				lock (mReadBufferLock) {
					lock (mWriteBufferLock) {
						Connection.Close();
						Connection = null;
					}
				}
			}
		}


		protected void StartUpdating()
		{
			ThreadPool.QueueUserWorkItem (o => DoTasks ());
		}


		protected void StopUpdating()
		{
			_ContinueUpdating = false;
		}


		private WaitCallback DoTasks()
        {
			_ContinueUpdating = true;
			while (_ContinueUpdating)
            {
                int rxlen = ReadInternal(mTempReadBuffer, 0);
                if (rxlen > 0)
                {
                    lock (mReadBufferLock)
                    {
                        for (int i = 0; i < rxlen; i++)
                        {
                            mReadBuffer[mReadBufferWriteCursor] = mTempReadBuffer[i];
                            mReadBufferWriteCursor = (mReadBufferWriteCursor + 1) % mReadBuffer.Length;
                            if (mReadBufferWriteCursor == mReadBufferReadCursor)
                            {
                                mReadBufferReadCursor = (mReadBufferReadCursor + 1) % mReadBuffer.Length;
                            }
                        }
                    }
                    if (DataReceived != null)
                    {
                        DataReceived(this, new DataReceivedEventArgs(this));
                    }
                }
            }
            return null;
        }

        public int Read(byte[] dest, int startIndex)
        {
            int len = 0;
            lock (mReadBufferLock)
            {
                int pos = startIndex;
                while ((mReadBufferReadCursor != mReadBufferWriteCursor) && (pos < dest.Length))
                {
                    dest[pos] = mReadBuffer[mReadBufferReadCursor];
                    len++;
                    pos++;
                    mReadBufferReadCursor = (mReadBufferReadCursor + 1) % mReadBuffer.Length;
                }
            }
            return len;
        }

		public void ResetParameters()
		{
			SetParameters(Baudrate, DataBits, StopBits, Parity);
		}

        protected abstract int ReadInternal(byte[] dest, int timeoutMillis);

        public abstract int Write(byte[] src, int timeoutMillis);

		protected abstract void SetParameters(int baudRate, int dataBits, StopBits stopBits, Parity parity);

        public abstract bool CD { get; }

        public abstract bool Cts { get; }

        public abstract bool Dsr { get; }

        public abstract bool Dtr { get; set; }

        public abstract bool RI { get; }

        public abstract bool Rts { get; set; }

        public virtual bool PurgeHwBuffers(bool flushReadBuffers, bool flushWriteBuffers)
        {
            return !flushReadBuffers && !flushWriteBuffers;
        }
    }
}
