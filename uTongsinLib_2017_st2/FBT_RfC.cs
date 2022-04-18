using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using umhha;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace uTongsinLib_2017_st2
{
    /// <summary>Bluetooth Rfcomm Client</summary>
    public class FBT_RfC
    {
        /// <summary>recv type enum</summary>
        public enum eMODE
        {
            /// <summary>byte type</summary>
            TYPE_BY = 0,
            /// <summary>string type</summary>
            TYPE_ST = 1,
        }
        /// <summary>element not found</summary>
        public const uint ERROR_ELEMENT_NOT_FOUND = 0x80070490;
        /// <summary>only one usage of each socket address (protocol/network address/port) is normally permitted.</summary>
        public const uint WSAEADDRINUSE = 0x80072740;
        /// <summary>device connection fail</summary>
        public const uint CONNECT_FAIL = 0x80072745;
        /// <summary>the I/O operation has been aborted because of either a thread exit or an application request</summary>
        public const uint ERROR_OPERATION_ABORTED = 0x800703E3;
        /// <summary>
        /// The SDP Type of the Service Name SDP attribute.
        /// The first byte in the SDP Attribute encodes the SDP Attribute Type as follows :
        ///    -  the Attribute Type size in the least significant 3 bits,
        ///    -  the SDP Attribute Type value in the most significant 5 bits.        
        /// </summary>
        public const byte SdpServiceNameAttributeType = (4 << 3) | 5;
        /// <summary>The Id of the Service Name SDP attribute</summary>
        public const UInt16 SdpServiceNameAttributeId = 0x100;

        private eMODE _mode = eMODE.TYPE_BY;
        /// <summary>last exception error</summary>
        public string _lastErr = "";

        /// <summary>searching device</summary>
        public ObservableCollection<RfcommDeviceDisplay> ResultCollection
        {
            get;
            private set;
        }
        private DeviceWatcher _watcher = null;
        private StreamSocket _socket = null;
        private DataWriter _writer = null;
        private RfcommDeviceService _service = null;
        private BluetoothDevice _device;

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
        public FBT_RfC()
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
        ~FBT_RfC()
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

        /// <summary>device seraching</summary>
        public void StartWatching()
        {
            string[] requestedProperties = new string[] { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected" };

            _watcher = DeviceInformation.CreateWatcher("(System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\")",
                                                            requestedProperties,
                                                            DeviceInformationKind.AssociationEndpoint);

            _watcher.Added += new TypedEventHandler<DeviceWatcher, DeviceInformation>((watcher, deviceInfo) =>
            {
                if (deviceInfo.Name != "")
                {
                    ResultCollection.Add(new RfcommDeviceDisplay(deviceInfo));
                    Watch_Add();
                }
            });

            _watcher.Updated += new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>((watcher, deviceInfoUpdate) =>
            {
                foreach (RfcommDeviceDisplay rfcommInfoDisp in ResultCollection)
                {
                    if (rfcommInfoDisp.Id == deviceInfoUpdate.Id)
                    {
                        rfcommInfoDisp.Update(deviceInfoUpdate);
                        break;
                    }
                }
                Watch_Update();
            });

            _watcher.EnumerationCompleted += new TypedEventHandler<DeviceWatcher, Object>((watcher, obj) =>
            {
                Watch_End();
            });

            _watcher.Removed += new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>((watcher, deviceInfoUpdate) =>
            {
                foreach (RfcommDeviceDisplay rfcommInfoDisp in ResultCollection)
                {
                    if (rfcommInfoDisp.Id == deviceInfoUpdate.Id)
                    {
                        ResultCollection.Remove(rfcommInfoDisp);
                        break;
                    }
                }
                Watch_Remove();
            });

            _watcher.Stopped += new TypedEventHandler<DeviceWatcher, Object>((watcher, obj) =>
            {
                ResultCollection.Clear();
                Watch_Stop();
            });

            _watcher.Start();
        }
        /// <summary>device search stop</summary>
        public void StopWatching()
        {
            if (_watcher != null)
            {
                if ((DeviceWatcherStatus.Started == _watcher.Status ||
                     DeviceWatcherStatus.EnumerationCompleted == _watcher.Status))
                {
                    _watcher.Stop();
                }
                _watcher = null;
            }
        }
        /// <summary>search event</summary>
        public virtual void Watch_Add()
        {

        }
        /// <summary>search event</summary>
        public virtual void Watch_Update()
        {

        }
        /// <summary>search event</summary>
        public virtual void Watch_End()
        {

        }
        /// <summary>search event</summary>
        public virtual void Watch_Remove()
        {

        }
        /// <summary>search event</summary>
        public virtual void Watch_Stop()
        {

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
        /// <param name="device">connect device</param>
        /// <param name="guid">guid</param>
        /// <returns>true or false</returns>
        public async Task<bool> BTOpen(RfcommDeviceDisplay device, Guid guid)
        {
            DeviceAccessStatus accessStatus = DeviceAccessInformation.CreateFromId(device.Id).CurrentStatus;
            if (accessStatus == DeviceAccessStatus.DeniedByUser)
            {
                _lastErr = "This app does not have access to connect to the remote device (please grant access in Settings > Privacy > Other Devices";
                NotifyError();
                return false;
            }

            try
            {
                _device = await BluetoothDevice.FromIdAsync(device.Id);
            }
            catch (Exception ex)
            {
                _lastErr = ex.Message;
                NotifyError();
                return false;
            }

            if (_device == null)
            {
                _lastErr = ("Bluetooth Device returned null. Access Status = " + accessStatus.ToString());
                NotifyError();
            }

            var rfcommServices = await _device.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(guid), BluetoothCacheMode.Uncached);

            if (rfcommServices.Services.Count > 0)
            {
                _service = rfcommServices.Services[0];
            }
            else
            {
                _lastErr = "Could not discover the chat service on the remote device";
                NotifyError();
                return false;
            }

            var attributes = await _service.GetSdpRawAttributesAsync();
            if (!attributes.ContainsKey(SdpServiceNameAttributeId))
            {
                _lastErr =
                    "The Chat service is not advertising the Service Name attribute (attribute id=0x100). " +
                    "Please verify that you are running the BluetoothRfcommChat server.";
                NotifyError();
                return false;
            }
            var attributeReader = DataReader.FromBuffer(attributes[SdpServiceNameAttributeId]);
            var attributeType = attributeReader.ReadByte();
            if (attributeType != SdpServiceNameAttributeType)
            {
                _lastErr =
                    "The Chat service is using an unexpected format for the Service Name attribute. " +
                    "Please verify that you are running the BluetoothRfcommChat server.";
                NotifyError();
                return false;
            }
            var serviceNameLength = attributeReader.ReadByte();

            attributeReader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;

            StopWatching();

            lock (this)
            {
                _socket = new StreamSocket();
            }
            try
            {
                await _socket.ConnectAsync(_service.ConnectionHostName, _service.ConnectionServiceName);

                _writer = new DataWriter(_socket.OutputStream);
                DataReader chatReader = new DataReader(_socket.InputStream);
                chatReader.InputStreamOptions = InputStreamOptions.Partial;
                ReceiveStringLoop(chatReader);
            }
            catch (Exception ex) when ((uint)ex.HResult == ERROR_ELEMENT_NOT_FOUND)
            {
                _lastErr = "Please verify that you are running the BluetoothRfcommChat server.";
                NotifyError();
                return false;
            }
            catch (Exception ex) when ((uint)ex.HResult == WSAEADDRINUSE)
            {
                _lastErr = "Please verify that there is no other RFCOMM connection to the same device.";
                NotifyError();
                return false;
            }

            OnConnection();
            return true;
        }
        /// <summary>Bluetooth close</summary>
        public void BTClose()
        {
            if (_writer != null)
            {
                _writer.DetachStream();
                _writer = null;
            }


            if (_service != null)
            {
                _service.Dispose();
                _service = null;
            }
            lock (this)
            {
                if (_socket != null)
                {
                    _socket.Dispose();
                    _socket = null;
                }
            }

            OnDisConnection();
        }

        /// <summary>Bluetooth open state</summary>
        /// <returns>true or false</returns>
        public bool IsOpen()
        {
            if (_socket == null)
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

        private async void ReceiveStringLoop(DataReader reader)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            cts.CancelAfter(1000);
            try
            {
                uint size = await reader.LoadAsync(1);
                if (size < 1)
                {
                    BTClose();
                    _lastErr = "Remote device terminated connection - make sure only one instance of server is running on remote device";
                    NotifyError();
                    return;
                }
                
                if (_mode == eMODE.TYPE_BY)
                {
                    byte[] msg = new byte[1];
                    reader.ReadBytes(msg);
                    RecvMessage(msg);
                    if (cts.IsCancellationRequested == true)
                    {
                        cts = new CancellationTokenSource(1000);
                    }
                    size = await reader.LoadAsync(256).AsTask(cts.Token);
                    Array.Resize<byte>(ref msg, (int)size);
                    reader.ReadBytes(msg);
                    RecvMessage(msg);
                }
                else
                {
                    string msg = reader.ReadString(1);
                    RecvMessage(msg);
                    if (cts.IsCancellationRequested == true)
                    {
                        cts = new CancellationTokenSource(1000);
                    }
                    size = await reader.LoadAsync(256).AsTask(cts.Token);
                    msg = reader.ReadString(size);
                    RecvMessage(msg);
                }

                ReceiveStringLoop(reader);
            }
            catch (Exception ex)
            {
                lock (this)
                {
                    if (_socket == null)
                    {
                        if ((uint)ex.HResult == CONNECT_FAIL)
                            _lastErr = "Disconnect triggered by remote device";
                        else if ((uint)ex.HResult == ERROR_OPERATION_ABORTED)
                            _lastErr = "The I/O operation has been aborted because of either a thread exit or an application request.";

                        NotifyError();
                    }
                    else
                    {
                        _lastErr = ex.Message;
                        NotifyError();
                        BTClose();
                    }
                }
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
    /// <summary>Bluetooth Device List Class</summary>
    public class RfcommDeviceDisplay : INotifyPropertyChanged
    {
        private DeviceInformation deviceInfo;

        /// <summary>Bluetooth device list generator</summary>
        /// <param name="deviceInfoIn">bluetooth device info</param>
        public RfcommDeviceDisplay(DeviceInformation deviceInfoIn)
        {
            deviceInfo = deviceInfoIn;
            UpdateGlyphBitmapImage();
        }

        /// <summary>device info Instance</summary>
        public DeviceInformation DeviceInformation
        {
            get
            {
                return deviceInfo;
            }

            private set
            {
                deviceInfo = value;
            }
        }

        /// <summary>info id</summary>
        public string Id
        {
            get
            {
                return deviceInfo.Id;
            }
        }

        /// <summary>info name</summary>
        public string Name
        {
            get
            {
                return deviceInfo.Name;
            }
        }

        /// <summary>device image</summary>
        public BitmapImage GlyphBitmapImage
        {
            get;
            private set;
        }

        /// <summary>device update</summary>
        /// <param name="deviceInfoUpdate">update device</param>
        public void Update(DeviceInformationUpdate deviceInfoUpdate)
        {
            deviceInfo.Update(deviceInfoUpdate);
            UpdateGlyphBitmapImage();
        }

        private async void UpdateGlyphBitmapImage()
        {
            DeviceThumbnail deviceThumbnail = await deviceInfo.GetGlyphThumbnailAsync();
            BitmapImage glyphBitmapImage = new BitmapImage();
            await glyphBitmapImage.SetSourceAsync(deviceThumbnail);
            GlyphBitmapImage = glyphBitmapImage;
            OnPropertyChanged("GlyphBitmapImage");
        }

        /// <summary>property event handler</summary>
        public event PropertyChangedEventHandler PropertyChanged;
        /// <summary>property event</summary>
        /// <param name="name">property name</param>
        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
