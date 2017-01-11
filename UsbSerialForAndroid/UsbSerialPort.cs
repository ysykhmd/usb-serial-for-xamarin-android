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
using Android.Util;

#if UseSmartThreadPool
using Amib.Threading;
#endif

namespace Aid.UsbSerial
{
    public abstract class UsbSerialPort
    {
        private const string TAG = "UsbSerailPort";

        /**
         * バッファサイズについて
         *   Nexus5(Android 5.1.1/LMY48B)+FT-232RL の組み合わせで
         *     ・115200bps でそれなりに安定動作させるには、実測で InternalReadBuffer[] に 1024 以上必要
         *     ・FtdiSerialPort.cs で呼び出している Connection.BulkTransfer() は、バッファのサイズが 256 の倍数以外では、ボーレートが 57600bps 以上でエラーを返す。原因は不明
         *     ・InternalReadBuffer[] は 16384byte より大きくても使われることはない
         *     ・57600,115200bps では InternalReadBuffer[] 一杯にデータが詰め込まれてくることがある。このときInternalReadBuffer[16384]とすると 57,600bps で 2.84..秒、
         *       115200bps で 1.422..秒となる。データに時間情報を含む場合は要注意(DEFAULT_READ_TIMEOUT_MILLISEC = 0 の場合)
         *     ・InternalReadBuffer[4096] で 115200bps で 0x00-0xFF のデータを連続受信した場合、数分に一度エラーを起こす(30分に４回とか)。InternalReadBuffer[] のサイズ
         *     　を調整しても、状況が大きく変わることがなかった
         *   Nexus5(Android 5.1.1/LMY48B)+CP2102 の組み合わせで
         *     ・DEFAULT_INTERNAL_READ_BUFFER_SIZE = 16 * 1024 だと、57600, 115200bps でデータを受信できない(原因不明)
         *     ・DEFAULT_INTERNAL_READ_BUFFER_SIZE = 4 * 1024 だと、4,800bps、115200bps ともにデータを受信できる
         *     ・DEFAULT_INTERNAL_READ_BUFFER_SIZE = 4 * 1024 だと、19,200bps でデータ受信イベントの発生周期が 2.133..秒 となる(DEFAULT_READ_TIMEOUT_MILLISEC = 0 の場合)
         *     ・DEFAULT_INTERNAL_READ_BUFFER_SIZE = 4 * 1024 だと、38,400bps でデータ受信イベントの発生周期が 1.066..秒 となる(DEFAULT_READ_TIMEOUT_MILLISEC = 0 の場合)
         */
        public const int DEFAULT_INTERNAL_READ_BUFFER_SIZE = 4 * 1024;
        // 変更する場合は FtdiSerailPort.cs の ReadInternal() 内の Connection.BulkTransfer() 呼び出し部分を注意。
        public const int DEFAULT_TEMP_READ_BUFFER_SIZE = DEFAULT_INTERNAL_READ_BUFFER_SIZE;
        // この値が小さいと、FT-232R で転送速度が速いとき、最初のデータがフォアグラウンドから読みだせないことがある。
        public const int DEFAULT_READ_BUFFER_SIZE = 16 * 1024;
        public const int DEFAULT_WRITE_BUFFER_SIZE = 16 * 1024;
        public const int DefaultBaudrate = 9600;
        public const int DefaultDataBits = 8;
        public const Parity DefaultParity = Parity.None;
        public const StopBits DefaultStopBits = StopBits.One;

        // データ受信タイムアウトの指定 (ms)
        // Nexus5(Android 5.1.1/LMY48B)+FT232RL の組み合わせで
        //   500 だと 0x00-0xff/ 57600, 115200bps を受信できない
        //   0 だと 0x00-0xff/57600bps/innerBuffer 16384byte でデータ受信のイベント発生の間隔が３秒近く、115200 bps だと 1.5秒程度開くことがある
        // Nexus5(Android 5.1.1/LMY48B)+CP2102 の組み合わせで
        //   ・DEFAULT_INTERNAL_READ_BUFFER_SIZE = 4 * 1024 だと、19200bps では 300 を指定すると受信できない(9600bps では受信できた)
        public const int DEFAULT_READ_TIMEOUT_MILLISEC = 0;

        public event EventHandler<DataReceivedEventArgs> DataReceivedEventLinser;

        protected int CurrentPortNumber;

        // non-null when open()
        protected UsbDeviceConnection Connection { get; set; }

        protected Object InternalReadBufferLock = new Object();
        protected Object ReadBufferLock = new Object();
        protected Object WriteBufferLock = new Object();

        /** Internal read buffer.  Guarded by {@link #ReadBufferLock}. */
        protected byte[] InternalReadBuffer;
        protected byte[] TempReadBuffer;
        protected byte[] MainReadBuffer;
        protected int MainReadBufferWriteCursor;
        protected int MainReadBufferReadCursor;

        /** Internal write buffer.  Guarded by {@link #mWriteBufferLock}. */
        protected byte[] MainWriteBuffer;

        private int dataBits;

        private volatile bool _ContinueUpdating;
        public bool IsOpened { get; protected set; }
        public int Baudrate { get; set; }
        public int DataBits
        {
            get { return dataBits; }
            set
            {
                if (value < 5 || 8 < value) { throw new ArgumentOutOfRangeException(); }
                dataBits = value;
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
            CurrentPortNumber = portNumber;

            InternalReadBuffer = new byte[DEFAULT_INTERNAL_READ_BUFFER_SIZE];
            TempReadBuffer = new byte[DEFAULT_TEMP_READ_BUFFER_SIZE];
            MainReadBuffer = new byte[DEFAULT_READ_BUFFER_SIZE];
            MainReadBufferReadCursor = 0;
            MainReadBufferWriteCursor = 0;
            MainWriteBuffer = new byte[DEFAULT_WRITE_BUFFER_SIZE];

#if UseSmartThreadPool
            ThreadPool  = threadPool;
#endif
        }

        public override string ToString()
        {
            return string.Format("<{0} device_name={1} device_id={2} port_number={3}>", this.GetType().Name, UsbDevice.DeviceName, UsbDevice.DeviceId, CurrentPortNumber);
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
            if (bufferSize == InternalReadBuffer.Length)
            {
                return;
            }
            lock (InternalReadBufferLock)
            {
                InternalReadBuffer = new byte[bufferSize];
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
            lock (WriteBufferLock)
            {
                if (bufferSize == MainWriteBuffer.Length)
                {
                    return;
                }
                MainWriteBuffer = new byte[bufferSize];
            }
        }

        // Members of IUsbSerialPort

        public int PortNumber
        {
            get { return CurrentPortNumber; }
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
                lock (ReadBufferLock)
                {
                    lock (WriteBufferLock)
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
                lock (ReadBufferLock)
                {
                    lock (WriteBufferLock)
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

#if UseSmartThreadPool
        private object DoTasks()
#else
        /*
         * MainReadBuffer は TempReadBuffer より大きいこと
         */
        private WaitCallback DoTasks()
#endif
        {
            int doTaskRxLen;
            int readRemainBufferSize;

            _ContinueUpdating = true;
            try
            {
                while (_ContinueUpdating)
                {
                    doTaskRxLen = ReadInternal();

                    // デバッグ時のログ出力用コード:
                    //  ReadInternal() の読出しバイト数が 256byte以上になった場合に、それ以降20回、ReadInternal() が読みだしたデータをダンプする
                    //  while の外で next を宣言する必要がある
                    //  ProlificSerialPort.cs の受信エラー調査に使用した
                    //if (doTaskRxLen >= 256 || next > 0)
                    //{
                    //    string msg = "";
                    //    for (int i = 0; i < doTaskRxLen; i++)
                    //    {
                    //        msg += TempReadBuffer[i].ToString("x2") + " ";
                    //    }
                    //    Log.Info(TAG, "Read Data Length : " + doTaskRxLen.ToString() + "\n" + msg);
                    //    if (0 == next)
                    //    {
                    //        next = 20;
                    //    }
                    //    else
                    //    {
                    //        next -= 1;
                    //    }
                    //}

                    // デバッグ時のログ出力用コード:ReadInternal() が一回に読み出す byte数をログに出力
                    // Log.Info(TAG, "Read Data Length : " + DateTime.Now.ToString("HH:mm:ss.fff") + ":" + doTaskRxLen.ToString() + "\n");

                    if (doTaskRxLen > 0)
                    {
                        lock (ReadBufferLock)
                        {
                            readRemainBufferSize = DEFAULT_READ_BUFFER_SIZE - MainReadBufferWriteCursor;

                            if (doTaskRxLen > readRemainBufferSize)
                            {
                                Array.Copy(TempReadBuffer, 0, MainReadBuffer, MainReadBufferWriteCursor, readRemainBufferSize);
                                MainReadBufferWriteCursor = doTaskRxLen - readRemainBufferSize;
                                Array.Copy(TempReadBuffer, readRemainBufferSize, MainReadBuffer, 0, MainReadBufferWriteCursor);
                            }
                            else
                            {
                                Array.Copy(TempReadBuffer, 0, MainReadBuffer, MainReadBufferWriteCursor, doTaskRxLen);
                                MainReadBufferWriteCursor += doTaskRxLen;
                                if (DEFAULT_READ_BUFFER_SIZE == MainReadBufferWriteCursor)
                                {
                                    MainReadBufferWriteCursor = 0;
                                }
                            }
                            if (DataReceivedEventLinser != null)
                            {
                                DataReceivedEventLinser(this, new DataReceivedEventArgs(this));
                            }
                        }
                    }
                }
            }
            catch (SystemException e)
            {
                Log.Error(TAG, "Data read faild: " + e.Message, e);
                _ContinueUpdating = false;
                Close();
                throw new System.IO.IOException("XXXX");
            }
            return null;
        }

        /*
         * ガベージを増やさないために関数内の自動変数は、すべて関数外で宣言する
         */
                    int readFirstLength;
        int readValidDataLength;
        public int Read(byte[] dest, int startIndex)
        {
            // MainReadBuffer[] にアクセスするので、ここにロックは必要
            lock (ReadBufferLock)
            {
                readValidDataLength = MainReadBufferWriteCursor - MainReadBufferReadCursor;
                if (readValidDataLength != 0)
                {
                    /*
                     * 以下は高速化のために意図的に関数分割していない
                     */
                    if (MainReadBufferWriteCursor < MainReadBufferReadCursor)
                    {
                        readValidDataLength += DEFAULT_READ_BUFFER_SIZE;
                        if (readValidDataLength > dest.Length)
                        {
                            readValidDataLength = dest.Length;
                        }

                        if (readValidDataLength + MainReadBufferReadCursor > DEFAULT_READ_BUFFER_SIZE)
                        {
                            readFirstLength = DEFAULT_READ_BUFFER_SIZE - MainReadBufferReadCursor;

                            Array.Copy(MainReadBuffer, MainReadBufferReadCursor, dest, startIndex, readFirstLength);
                            MainReadBufferReadCursor = readValidDataLength - readFirstLength;
                            Array.Copy(MainReadBuffer, 0, dest, startIndex + readFirstLength, MainReadBufferReadCursor);
                        }
                        else
                        {
                            Array.Copy(MainReadBuffer, MainReadBufferReadCursor, dest, startIndex, readValidDataLength);
                            MainReadBufferReadCursor += readValidDataLength;
                            if (DEFAULT_READ_BUFFER_SIZE == MainReadBufferReadCursor)
                            {
                                MainReadBufferReadCursor = 0;
                            }
                        }
                    }
                    else
                    {
                        if (readValidDataLength > dest.Length)
                        {
                            readValidDataLength = dest.Length;
                        }

                        Array.Copy(MainReadBuffer, MainReadBufferReadCursor, dest, startIndex, readValidDataLength);
                        MainReadBufferReadCursor += readValidDataLength;
                        if (DEFAULT_READ_BUFFER_SIZE == MainReadBufferReadCursor)
                        {
                            MainReadBufferReadCursor = 0;
                        }
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
            lock(ReadBufferLock)
            {
                MainReadBufferReadCursor = 0;
                MainReadBufferWriteCursor = 0;
            }
        }

        protected abstract int ReadInternal();

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

