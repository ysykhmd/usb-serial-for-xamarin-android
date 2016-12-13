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

#if UseSmartThreadPool
using Amib.Threading;
#endif

namespace Aid.UsbSerial
{
    public abstract class UsbSerialPort
    {
        // 256 の倍数以外では 57600bps 以上で FtdiSerailPort.cs の Connection.BulkTransfer() がエラーを返す。原因は不明
        // 115200bps で安定動作させるには、実測で 1024 以上必要
        public const int DEFAULT_INTERNAL_READ_BUFFER_SIZE = 8 * 1024;
        // 変更する場合は FtdiSerailPort.cs の ReadInternal() 内の Connection.BulkTransfer() 呼び出し部分を注意。
        public const int DEFAULT_TEMP_READ_BUFFER_SIZE = DEFAULT_INTERNAL_READ_BUFFER_SIZE;
        // この値が小さいと、FT-232R で転送速度が速いとき、最初のデータがフォアグラウンドから読みだせないことがある。
        public const int DEFAULT_READ_BUFFER_SIZE = 1 * DEFAULT_INTERNAL_READ_BUFFER_SIZE;
        public const int DEFAULT_WRITE_BUFFER_SIZE = 16 * 1024;
        public const int DefaultBaudrate = 9600;
        public const int DefaultDataBits = 8;
        public const Parity DefaultParity = Parity.None;
        public const StopBits DefaultStopBits = StopBits.One;

        public event EventHandler<DataReceivedEventArgs> DataReceivedEventLinser;

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
        public int DataBits
        {
            get { return mDataBits; }
            set
            {
                if (value < 5 || 8 < value)
                    throw new ArgumentOutOfRangeException();
                mDataBits = value;
            }
        }
        public Parity Parity { get; set; }
        public StopBits StopBits { get; set; }

#if UseSmartThreadPool
        public SmartThreadPool ThreadPool { get; set; }
#endif

#if UseSmartThreadPool
        public UsbSerialPort(UsbManager manager, UsbDevice device, int portNumber, SmartThreadPool threadPool)
#else
        public UsbSerialPort(UsbManager manager, UsbDevice device, int portNumber)
#endif
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

#if UseSmartThreadPool
            ThreadPool  = threadPool;
#endif
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


        public abstract void Open();

        public abstract void Close();


        protected void CreateConnection()
        {
            if (UsbManager != null && UsbDevice != null)
            {
                lock (mReadBufferLock)
                {
                    lock (mWriteBufferLock)
                    {
                        Connection = UsbManager.OpenDevice(UsbDevice);
                    }
                }
            }
        }


        protected void CloseConnection()
        {
            if (Connection != null)
            {
                lock (mReadBufferLock)
                {
                    lock (mWriteBufferLock)
                    {
                        Connection.Close();
                        Connection = null;
                    }
                }
            }
        }


        protected void StartUpdating()
        {
#if UseSmartThreadPool
            if (ThreadPool != null)
            {
                ThreadPool.QueueWorkItem(o => DoTasks());
            }
            else
            {
                System.Threading.ThreadPool.QueueUserWorkItem(o => DoTasks());
            }
#else
            ThreadPool.QueueUserWorkItem(o => DoTasks());
#endif
        }


        protected void StopUpdating()
        {
            _ContinueUpdating = false;
        }

        /*
         * ガベージを増やさないために DoTasks 内の自動変数は、すべて関数外で static 宣言する
         */
        static int doTaskRxLen;
        static int readRemainBufferSize;
#if UseSmartThreadPool
        private object DoTasks()
#else
        /*
         * mReadBuffer は mTempReadBuffer より大きいこと
         */
        private WaitCallback DoTasks()
#endif
        {
            _ContinueUpdating = true;
            while (_ContinueUpdating)
            {
                try
                {
                    doTaskRxLen = ReadInternalFtdi(0);
                    if (doTaskRxLen > 0)
                    {
                        lock (mReadBufferLock)
                        {
                            readRemainBufferSize = DEFAULT_READ_BUFFER_SIZE - mReadBufferWriteCursor;

                            if (doTaskRxLen > readRemainBufferSize)
                            {
                                Array.Copy(mTempReadBuffer, 0, mReadBuffer, mReadBufferWriteCursor, readRemainBufferSize);
                                mReadBufferWriteCursor = doTaskRxLen - readRemainBufferSize;
                                Array.Copy(mTempReadBuffer, readRemainBufferSize, mReadBuffer, 0, mReadBufferWriteCursor);
                            }
                            else
                            {
                                Array.Copy(mTempReadBuffer, 0, mReadBuffer, mReadBufferWriteCursor, doTaskRxLen);
                                mReadBufferWriteCursor += doTaskRxLen;
                                if (DEFAULT_READ_BUFFER_SIZE == mReadBufferWriteCursor)
                                {
                                    mReadBufferWriteCursor = 0;
                                }
                            }
                        }
                        
                        if (DataReceivedEventLinser != null)
                        {
                            DataReceivedEventLinser(this, new DataReceivedEventArgs(this));
                        }
                    }
                }
                catch (SystemException e)
                {
                    _ContinueUpdating = false;
                    Close();
                }

                Thread.Sleep(1);
            }
            return null;
        }

        /*
         * ガベージを増やさないために関数内の自動変数は、すべて関数外で static 宣言する
         */
        static int readFirstLength;
        static int readValidDataLength;
        public int Read(byte[] dest, int startIndex)
        {
            readValidDataLength = mReadBufferWriteCursor - mReadBufferReadCursor;
            lock (mReadBufferLock)
            {
                /*
                 * 以下は高速化のために意図的に関数分割していない
                 */
                if (mReadBufferWriteCursor < mReadBufferReadCursor)
                {
                    readValidDataLength += DEFAULT_READ_BUFFER_SIZE;
                    if (readValidDataLength > dest.Length)
                    {
                        readValidDataLength = dest.Length;
                    }

                    if (readValidDataLength + mReadBufferReadCursor > DEFAULT_READ_BUFFER_SIZE)
                    {
                        readFirstLength = DEFAULT_READ_BUFFER_SIZE - mReadBufferReadCursor;

                        Array.Copy(mReadBuffer, mReadBufferReadCursor, dest, startIndex, readFirstLength);
                        Array.Copy(mReadBuffer, 0, dest, startIndex + readFirstLength, mReadBufferWriteCursor);
                        mReadBufferReadCursor = mReadBufferWriteCursor;
                    }
                    else
                    {
                        Array.Copy(mReadBuffer, mReadBufferReadCursor, dest, startIndex, readValidDataLength);
                        mReadBufferReadCursor += readValidDataLength;
                        if (DEFAULT_READ_BUFFER_SIZE == mReadBufferReadCursor)
                        {
                            mReadBufferReadCursor = 0;
                        }
                    }
                }
                else
                {
                    if (readValidDataLength > dest.Length)
                    {
                        readValidDataLength = dest.Length;
                    }

                    Array.Copy(mReadBuffer, mReadBufferReadCursor, dest, startIndex, readValidDataLength);
                    mReadBufferReadCursor += readValidDataLength;
                    if (DEFAULT_READ_BUFFER_SIZE == mReadBufferReadCursor)
                    {
                        mReadBufferReadCursor = 0;
                    }
                }
            }
            return readValidDataLength;
        }

        public void ResetParameters()
        {
            SetParameters(Baudrate, DataBits, StopBits, Parity);
        }

        public void ResetReadBuffer()
        {
            lock(mReadBufferLock)
            {
                mReadBufferReadCursor = 0;
                mReadBufferWriteCursor = 0;
            }
        }

        protected abstract int ReadInternal();
        protected abstract int ReadInternalFtdi(int timeoutMillis);

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

