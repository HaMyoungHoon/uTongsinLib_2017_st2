using umhha;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.Foundation;

namespace uTongsinLib_2017_st2
{
    /// <summary>Bluetooth Rfcomm Server</summary>
    public class FBT_RfS
    {
        /// <summary>recv type enum</summary>
        public enum eMODE
        {
            /// <summary>byte type</summary>
            TYPE_BY = 0,
            /// <summary>string type</summary>
            TYPE_ST = 1,
        }
        /// <summary> HRESULT_FROM_WIN32</summary>
        public const uint ERROR_DEVICE_NOT_AVAILABLE = 0x800710DF;
        /// <summary> HRESULT_FROM_WIN32</summary>
        public const uint ERROR_OPERATION_ABORTED = 0x800703E3;
        /// <summary>
        /// The SDP Type of the Service Name SDP attribute.
        /// The first byte in the SDP Attribute encodes the SDP Attribute Type as follows :
        ///    -  the Attribute Type size in the least significant 3 bits,
        ///    -  the SDP Attribute Type value in the most significant 5 bits.        
        /// </summary>
        public const byte SdpServiceNameAttributeType = (4 << 3) | 5;
        /// <summary>The value of the Service Name SDP attribute</summary>
        public const string SdpServiceName = "Bluetooth Rfcomm Service";
        /// <summary>The Id of the Service Name SDP attribute</summary>
        public const UInt16 SdpServiceNameAttributeId = 0x100;

        private eMODE _mode = eMODE.TYPE_ST;
        /// <summary>last exception error</summary>
        public string _lastErr = "";

        private StreamSocket _socket;
        private DataWriter _writer;
        private RfcommServiceProvider _provider;
        private StreamSocketListener _listener;

        /// <summary>Number and Order of Threads</summary>
        public enum eTHREAD
        {
            /// <summary>Thread 1</summary>
            TH1 = 0,
            /// <summary>Thread 2</summary>
            TH2,
            /// <summary>Thread 3</summary>
            TH3,
            /// <summary>Thread Count</summary>
            TH_COUNT,
        }

        /// <summary>FBT_RfS generator</summary>
        private struct stThread
        {
            public Thread _th;
            public int _interval;
            public ManualResetEvent _thRun;
        }
        private delegate void ThreadFunc();
        private stThread[] _thread;
        private ThreadFunc[] _threadFunc;

        /// <summary>FBT_RfS generator</summary>
        public FBT_RfS()
        {
            _thread = new stThread[(int)eTHREAD.TH_COUNT];
            _threadFunc = new ThreadFunc[(int)eTHREAD.TH_COUNT];
            _threadFunc[(int)eTHREAD.TH1] = ThreadFunc1;
            _threadFunc[(int)eTHREAD.TH2] = ThreadFunc2;
            _threadFunc[(int)eTHREAD.TH3] = ThreadFunc3;
            _thread[(int)eTHREAD.TH1]._interval = 10;
            _thread[(int)eTHREAD.TH2]._interval = 10;
            _thread[(int)eTHREAD.TH3]._interval = 10;
            _thread[(int)eTHREAD.TH1]._thRun = new ManualResetEvent(false);
            _thread[(int)eTHREAD.TH2]._thRun = new ManualResetEvent(false);
            _thread[(int)eTHREAD.TH3]._thRun = new ManualResetEvent(false);
        }
        /// <summary>FBT_RfS distructor</summary>
        ~FBT_RfS()
        {

        }

        /// <summary>set recv mode</summary>
        /// <param name="mode">ref ENUM</param>
        public void SetMode(eMODE mode)
        {
            _mode = mode;
        }
        /// <summary>called when an error occurs.</summary>
        /// <returns>last error</returns>
        public virtual string NotifyError()
        {
            return _lastErr;
        }

        /// <summary>called when Connection</summary>
        public virtual void OnConnection()
        {

        }
        /// <summary>called when disconnect</summary>
        public virtual void OnDisConnection()
        {

        }
        /// <summary>Bluetooth open</summary>
        /// <param name="guid">guid</param>
        /// <returns>true or false</returns>
        public async Task<bool> BTOpen(Guid guid)
        {
            try
            {
                _provider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromUuid(guid));
            }
            catch (Exception e)
            {
                _lastErr = e.Message;
                NotifyError();
                return false;
            }
            
            _listener = new StreamSocketListener();
            _listener.ConnectionReceived += _listener_ConnectionReceived;
            
            string rfComm = _provider.ServiceId.AsString();

            await _listener.BindServiceNameAsync(rfComm, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

            InitializeServiceSdpAttributes(_provider);

            try
            {
                _provider.StartAdvertising(_listener, true);
            }
            catch (Exception e)
            {
                _lastErr = e.Message;
                NotifyError();
                return false;
            }

            return true;
        }
        /// <summary>Bluetooth close</summary>
        public void BTClose()
        {
            if (_provider != null)
            {
                _provider.StopAdvertising();
                _provider = null;
            }

            if (_listener != null)
            {
                _listener.Dispose();
                _listener = null;
            }

            if (_writer != null)
            {
                _writer.DetachStream();
                _writer = null;
            }

            if (_socket != null)
            {
                _socket.Dispose();
                _socket = null;
            }
            OnDisConnection();
        }
        /// <summary>Bluetooth open state</summary>
        /// <returns>true or false</returns>
        public bool IsOpen()
        {
            if (_provider == null)
            {
                return false;
            }

            return true;
        }

        /// <summary>Send Data</summary>
        /// <param name="msg">literally</param>
        protected async void SendMessage(string msg)
        {
            if (_socket == null)
            {
                return;
            }
#if DEBUG
            if (Fmhha.Instance.LibraryPermit() == false)
            {
                return;
            }
#else
            return;
#endif
            _writer.WriteString(msg);

            await _writer.StoreAsync();
        }
        /// <summary>Send Data</summary>
        /// <param name="msg">literally</param>
        /// <param name="offset">literally</param>
        protected async void SendMessage(byte[] msg, int offset = 0)
        {
            if (_socket == null)
            {
                return;
            }
#if DEBUG
            if (Fmhha.Instance.LibraryPermit() == false)
            {
                return;
            }
#else
            return;
#endif
            _writer.WriteBytes(msg);

            await _writer.StoreAsync();
        }
        /// <summary>Recv Data</summary>
        /// <param name="msg">literally</param>
        public virtual void RecvMessage(string msg)
        {

        }
        /// <summary>Recv Data</summary>
        /// <param name="msg">literally</param>
        public virtual void RecvMessage(byte[] msg)
        {

        }

        private void InitializeServiceSdpAttributes(RfcommServiceProvider rfcommProvider)
        {
            var sdpWriter = new DataWriter();

            // Write the Service Name Attribute.
            sdpWriter.WriteByte(SdpServiceNameAttributeType);

            // The length of the UTF-8 encoded Service Name SDP Attribute.
            sdpWriter.WriteByte((byte)SdpServiceName.Length);

            // The UTF-8 encoded Service Name value.
            sdpWriter.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
            sdpWriter.WriteString(SdpServiceName);

            // Set the SDP Attribute on the RFCOMM Service Provider.
            rfcommProvider.SdpRawAttributes.Add(SdpServiceNameAttributeId, sdpWriter.DetachBuffer());
        }
        private async void _listener_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            try
            {
                _socket = args.Socket;
            }
            catch (Exception e)
            {
                _lastErr = e.Message;
                NotifyError();
                BTClose();
                return;
            }
            OnConnection();

            _writer = new DataWriter(_socket.OutputStream);
            DataReader reader = new DataReader(_socket.InputStream);
            reader.InputStreamOptions = InputStreamOptions.Partial;
            bool remoteDisconnection = false;

            CancellationTokenSource cts = new CancellationTokenSource();
            cts.CancelAfter(1);
            while (true)
            {
                try
                {
                    uint readLength = await reader.LoadAsync(1);
                    if (readLength < 1)
                    {
                        remoteDisconnection = true;
                        break;
                    }

                    if (_mode == eMODE.TYPE_BY)
                    {
                        byte[] msg = new byte[readLength];
                        reader.ReadBytes(msg);
                        RecvMessage(msg);
                        if (cts.IsCancellationRequested == true)
                        {
                            cts = new CancellationTokenSource(1000);
                        }
                        readLength = await reader.LoadAsync(256).AsTask(cts.Token);
                        Array.Resize<byte>(ref msg, (int)readLength);
                        reader.ReadBytes(msg);
                        RecvMessage(msg);
                    }
                    else
                    {
                        string msg = reader.ReadString(readLength);
                        RecvMessage(msg);
                        if (cts.IsCancellationRequested == true)
                        {
                            cts = new CancellationTokenSource(1000);
                        }
                        readLength = await reader.LoadAsync(256).AsTask(cts.Token);
                        msg = reader.ReadString(readLength);
                        RecvMessage(msg);
                    }
                }
                catch (Exception e)
                {
                    remoteDisconnection = true;
                    _lastErr = e.Message;
                    NotifyError();
                    break;
                }
            }

            reader.DetachStream();
            if (remoteDisconnection)
            {
                BTClose();
            }
        }

        private void ThreadFunc1()
        {
            PreThread1();
            while (_thread[(int)eTHREAD.TH1]._thRun.SafeWaitHandle.IsClosed == false)
            {
                Thread.Sleep(_thread[(int)eTHREAD.TH1]._interval);
                if (_thread[(int)eTHREAD.TH1]._thRun.SafeWaitHandle.IsClosed == false)
                {
                    _thread[(int)eTHREAD.TH1]._thRun.WaitOne();
                }
                if (ProcThread1() == false)
                {
                    break;
                }
            }
            PostThread1();
        }
        private void ThreadFunc2()
        {
            PreThread2();
            while (_thread[(int)eTHREAD.TH2]._thRun.SafeWaitHandle.IsClosed == false)
            {
                Thread.Sleep(_thread[(int)eTHREAD.TH2]._interval);
                if (_thread[(int)eTHREAD.TH2]._thRun.SafeWaitHandle.IsClosed == false)
                {
                    _thread[(int)eTHREAD.TH2]._thRun.WaitOne();
                }
                if (ProcThread2() == false)
                {
                    break;
                }
            }
            PostThread2();
        }
        private void ThreadFunc3()
        {
            PreThread3();
            while (_thread[(int)eTHREAD.TH3]._thRun.SafeWaitHandle.IsClosed == false)
            {
                Thread.Sleep(_thread[(int)eTHREAD.TH3]._interval);
                if (_thread[(int)eTHREAD.TH3]._thRun.SafeWaitHandle.IsClosed == false)
                {
                    _thread[(int)eTHREAD.TH3]._thRun.WaitOne();
                }
                if (ProcThread3() == false)
                {
                    break;
                }
            }
            PostThread3();
        }
        /// <remarks>The first run when the thread is created.</remarks>
        public virtual void PreThread1() { return; }
        /// <remarks>The first run when the thread is created.</remarks>
        public virtual void PreThread2() { return; }
        /// <remarks>The first run when the thread is created.</remarks>
        public virtual void PreThread3() { return; }
        /// <remarks>The last time the thread is closed.</remarks>
        public virtual void PostThread1() { return; }
        /// <remarks>The last time the thread is closed.</remarks>
        public virtual void PostThread2() { return; }
        /// <remarks>The last time the thread is closed.</remarks>
        public virtual void PostThread3() { return; }
        /// <returns>true : infinite, false : one time</returns>
        public virtual bool ProcThread1() { return false; }
        /// <returns>true : infinite, false : one time</returns>
        public virtual bool ProcThread2() { return false; }
        /// <returns>true : infinite, false : one time</returns>
        public virtual bool ProcThread3() { return false; }
        /// <summary>Changed the interval of the thread corresponding to the enum value.
        /// Default : 10ms
        /// </summary>
        /// <param name="threadEnum">refer to enum eTHREAD</param>
        /// <param name="interval">unit : ms</param>
        public void SetThreadInterval(eTHREAD threadEnum, int interval)
        {
#if DEBUG
            if (Fmhha.Instance.LibraryPermit() == false)
            {
                return;
            }
#else
            return;
#endif
            if (threadEnum == eTHREAD.TH_COUNT)
            {
                return;
            }
            bool result = Enum.IsDefined(typeof(eTHREAD), threadEnum);
            if (result)
            {
                _thread[(int)threadEnum]._interval = interval;
            }
        }
        /// <summary>Create and Run Thread</summary>
        /// <param name="threadEnum">refer to enum eTHREAD</param>
        public void CreateThread(eTHREAD threadEnum)
        {
#if DEBUG
            if (Fmhha.Instance.LibraryPermit() == false)
            {
                return;
            }
#else
            return;
#endif
            if (threadEnum == eTHREAD.TH_COUNT)
            {
                return;
            }
            bool result = Enum.IsDefined(typeof(eTHREAD), threadEnum);
            if (result)
            {
                if (_thread[(int)threadEnum]._th == null)
                {
                    _thread[(int)threadEnum]._th = new Thread(new ThreadStart(_threadFunc[(int)threadEnum]));
                }
                else if (_thread[(int)threadEnum]._th.IsAlive == false)
                {
                    _thread[(int)threadEnum]._th = new Thread(new ThreadStart(_threadFunc[(int)threadEnum]));
                }
                if (_thread[(int)threadEnum]._thRun.SafeWaitHandle.IsClosed)
                {
                    _thread[(int)threadEnum]._thRun = new ManualResetEvent(false);
                }
                if (_thread[(int)threadEnum]._th.IsAlive == true)
                {
                    return;
                }
                _thread[(int)threadEnum]._thRun.Set();
                _thread[(int)threadEnum]._th.Start();
            }
        }
        /// <summary>Close Thread</summary>
        /// <param name="threadEnum">refer to enum eTHREAD</param>
        public void CloseThread(eTHREAD threadEnum = eTHREAD.TH_COUNT)
        {
#if DEBUG
            if (Fmhha.Instance.LibraryPermit() == false)
            {
                return;
            }
#else
            return;
#endif
            bool result = Enum.IsDefined(typeof(eTHREAD), threadEnum);
            if (result)
            {
                if (threadEnum == eTHREAD.TH_COUNT)
                {
                    for (int i = 0; i < (int)eTHREAD.TH_COUNT; i++)
                    {
                        if (_thread[i]._thRun.SafeWaitHandle.IsClosed == false)
                        {
                            _thread[i]._thRun.Set();
                        }
                        if (_thread[i]._thRun.SafeWaitHandle.IsClosed == false)
                        {
                            _thread[i]._thRun.Close();
                        }
                    }
                }
                else
                {
                    if (_thread[(int)threadEnum]._thRun.SafeWaitHandle.IsClosed == false)
                    {
                        _thread[(int)threadEnum]._thRun.Set();
                    }
                    if (_thread[(int)threadEnum]._thRun.SafeWaitHandle.IsClosed == false)
                    {
                        _thread[(int)threadEnum]._thRun.Close();
                    }
                }
            }
        }
        /// <summary>Puase Thread</summary>
        /// <param name="threadEnum">refer to enum eTHREAD</param>
        public void PauseThread(eTHREAD threadEnum = eTHREAD.TH_COUNT)
        {
#if DEBUG
            if (Fmhha.Instance.LibraryPermit() == false)
            {
                return;
            }
#else
            return;
#endif
            bool result = Enum.IsDefined(typeof(eTHREAD), threadEnum);
            if (result)
            {
                if (threadEnum == eTHREAD.TH_COUNT)
                {
                    for (int i = 0; i < (int)eTHREAD.TH_COUNT; i++)
                    {
                        if (_thread[i]._thRun.SafeWaitHandle.IsClosed == false)
                        {
                            _thread[i]._thRun.Reset();
                        }
                    }
                }
                else
                {
                    if (_thread[(int)threadEnum]._thRun.SafeWaitHandle.IsClosed == false)
                    {
                        _thread[(int)threadEnum]._thRun.Reset();
                    }
                }
            }
        }
        /// <summary>Destroy Thread</summary>
        /// <param name="threadEnum">refer to enum eTHREAD</param>
        public void AbortThread(eTHREAD threadEnum = eTHREAD.TH_COUNT)
        {
#if DEBUG
            if (Fmhha.Instance.LibraryPermit() == false)
            {
                return;
            }
#else
            return;
#endif
            bool result = Enum.IsDefined(typeof(eTHREAD), threadEnum);
            if (result)
            {
                if (threadEnum == eTHREAD.TH_COUNT)
                {
                    for (int i = 0; i < (int)eTHREAD.TH_COUNT; i++)
                    {
                        if (_thread[i]._th == null)
                        {
                            continue;
                        }
                        _thread[i]._th.Abort();
                        if (_thread[i]._thRun.SafeWaitHandle.IsClosed == false)
                        {
                            _thread[i]._thRun.Set();
                        }
                        if (_thread[i]._thRun.SafeWaitHandle.IsClosed == false)
                        {
                            _thread[i]._thRun.Close();
                        }
                    }
                }
                else
                {
                    if (_thread[(int)threadEnum]._th != null)
                    {
                        _thread[(int)threadEnum]._th.Abort();
                    }
                    if (_thread[(int)threadEnum]._thRun.SafeWaitHandle.IsClosed == false)
                    {
                        _thread[(int)threadEnum]._thRun.Set();
                    }
                    if (_thread[(int)threadEnum]._thRun.SafeWaitHandle.IsClosed == false)
                    {
                        _thread[(int)threadEnum]._thRun.Close();
                    }
                }
            }
        }
        /// <summary>After closing the thread, wait for an end.</summary>
        /// <param name="threadEnum">refer to enum eTHREAD</param>
        public void WaitThreadTerminate(eTHREAD threadEnum = eTHREAD.TH_COUNT)
        {
#if DEBUG
            if (Fmhha.Instance.LibraryPermit() == false)
            {
                return;
            }
#else
            return;
#endif
            bool result = Enum.IsDefined(typeof(eTHREAD), threadEnum);
            if (result)
            {
                if (threadEnum == eTHREAD.TH_COUNT)
                {
                    for (int i = 0; i < (int)eTHREAD.TH_COUNT; i++)
                    {
                        if (_thread[i]._th == null)
                        {
                            continue;
                        }
                        if (_thread[i]._thRun.SafeWaitHandle.IsClosed == true)
                        {
                            _thread[i]._th.Join();
                        }
                    }
                }
                else
                {
                    if (_thread[(int)threadEnum]._th != null)
                    {
                        if (_thread[(int)threadEnum]._thRun.SafeWaitHandle.IsClosed == true)
                        {
                            _thread[(int)threadEnum]._th.Join();
                        }
                    }
                }
            }
        }
        /// <param name="threadEnum">refer to enum eTHREAD</param>
        /// <returns>return true if thread is alive.</returns>
        public bool IsAliveThread(eTHREAD threadEnum)
        {
#if DEBUG
            if (Fmhha.Instance.LibraryPermit() == false)
            {
                return false;
            }
#else
            return false;
#endif
            if (threadEnum == eTHREAD.TH_COUNT)
            {
                return false;
            }
            bool result = Enum.IsDefined(typeof(eTHREAD), threadEnum);
            if (result)
            {
                if (_thread[(int)threadEnum]._th == null)
                {
                    return false;
                }
                return _thread[(int)threadEnum]._th.IsAlive;
            }

            return false;
        }
    }
}
