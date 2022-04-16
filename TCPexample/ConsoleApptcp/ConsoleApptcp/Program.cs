using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Net.Sockets;
using System.Globalization;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ConsoleApptcp
{
    internal class Program
    {
        public static TCPClient tcpClient;
        static void Main(string[] args)
        {
            #region Server
            Console.WriteLine("TCP Server Start...");
            TCPSERVER.Instance.Port = 7575;
            TCPSERVER.Instance.IsOpen = true;
            #endregion
            Thread.Sleep(1000);
            #region Client
            Console.WriteLine("TCP Client Start...");
            IPAddress IPAddrClient = IPAddress.Parse("127.0.0.1");
            tcpClient = new TCPClient(IPAddrClient, 7575);
            #endregion
            Thread TestSendataClientThread = new Thread(TestSendataClient);
            TestSendataClientThread.Start();

            Thread TestReciveServerThread = new Thread(TestReciveServer);
            TestReciveServerThread.Start();
        }
        public static void TestSendataClient()
        {
            while (true)
            {
                byte[] temp = new byte[] { 100, 200 };
                TCPClient.QSendPacket.Data = temp;
                string temp_output = "";
                for (int i = 0; i < temp.Length; i++)
                {
                    temp_output += temp[i];
                    temp_output += " ";
                }

                Thread.Sleep(1000);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Client Send: " + temp_output + " - " + DateTime.Now.ToString("HH:MM:ss") + "\n");
            }
        }
        public static void TestReciveServer()
        {
            byte[] temparr;

            while (TCPSERVER.Instance.m_isOpen && TCPSERVER.Instance.m_port >= 0)
            {
                if (TCPSERVER.Instance.QReceivePacket.DataCount > 0)
                {
                    //Parse Data
                    temparr = TCPSERVER.Instance.QReceivePacket.Data;

                    string temp_outPut = "";
                    for (int i = 0; i < temparr.Length; i++)
                    {
                        temp_outPut += temparr[i];
                        temp_outPut += " ";
                    }
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.WriteLine("Server Rec.: " + temp_outPut + " - " + DateTime.Now.ToString("HH:MM:ss") + "\n");
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        }
    }
    #region Server
    #region StorageOfArrayByte
    public class StorageOfArrayByte
    {
        volatile Queue<byte[]> StorageArrayByte;
        public StorageOfArrayByte()
        {
            StorageArrayByte = new Queue<byte[]>();
        }
        public byte[] Data
        {
            get
            {
                lock (this)
                {
                    while (StorageArrayByte.Count == 0) Monitor.Wait(this);
                    return StorageArrayByte.Dequeue();
                }
            }
            set
            {
                lock (this)
                {
                    StorageArrayByte.Enqueue(value);
                    Monitor.PulseAll(this);
                }
            }
        }
        public int DataCount { get { return StorageArrayByte.Count; } }
        public void DataReallocate() { StorageArrayByte.TrimExcess(); }
        public void Clear() { StorageArrayByte.Clear(); }
    }
    #endregion
    #region Singelton
    public class TCPSERVER
    {
        private static TCPSERVER instance;
        public static TCPSERVER Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new TCPSERVER();
                }

                return instance;
            }
        }
        #endregion
        public delegate void tcpServerError(TCPSERVER server, Exception e);
        public delegate void tcpServerConnectionChanged(TCPServerConnection connection);
        public StorageOfArrayByte QReceivePacket = new StorageOfArrayByte();
        public StorageOfArrayByte QSendPacket = new StorageOfArrayByte();
        private List<TCPServerConnection> connections;
        //private int NumberofDataFlow = 1;//6;//board status, signal data, target data, server status, On/Off commands in multiple connections form or 1 for whole data
        private IPAddress _ipAddress = IPAddress.Any;//yo can set ip here
        private TcpListener listener;
        private Thread listenThread;
        private Thread sendThread;
        private Thread manageThread;
        private Thread forwardThread;
        public bool m_isOpen;
        public int m_port;
        private int LimitClients = 5;//Limit Number of Clients Can Connect to server 
        private int m_maxSendAttempts;
        private int m_idleTime;
        private int m_maxCallbackThreads;
        private int m_verifyConnectionInterval;
        private Encoding m_encoding;
        private SemaphoreSlim sem;
        private bool waiting;
        private int activeThreads;
        private object activeThreadsLock = new object();
        public event tcpServerConnectionChanged OnConnect = null;
        public event tcpServerConnectionChanged OnDataAvailable = null;
        public event tcpServerError OnError = null;
        // number of cliens is connected
        public static int newconncounter = 0;
        public List<TCPServerConnection> connectionsalias;
        //recive queue
        public static List<byte> LastBytes = new List<byte>();
        public static int ProcState = 0;
        /*public static List<ByteStorageQ> TCPQs ;//= new ByteStorageQ();
        public static List<int> ConnectionsIndex;
        public static List<int> ProcState;
        public static List<TCPMainPacket> LastPackets;
        public static List<List<byte>> LastBytes;*/
        public static string clientmsg = "";
        //send queue
        public static int ErrHdr = 0;
        public static string msg = "";
        public int calibdatacounter = 0;
        private TCPSERVER()
        {
            // 
            // TODO: Add constructor logic here
            //
            Initialise();
        }
        private void Initialise()
        {
            connections = new List<TCPServerConnection>();

            listener = null;

            connectionsalias = connections;

            listenThread = null;
            sendThread = null;
            manageThread = null;
            forwardThread = null;

            m_port = -1;
            m_maxSendAttempts = 3;
            m_isOpen = false;
            m_idleTime = 100; //100 ms sleep time when no data to send and no request to accept
            m_maxCallbackThreads = 100;
            m_verifyConnectionInterval = 100;
            m_encoding = Encoding.ASCII;

            sem = new SemaphoreSlim(0);
            waiting = false;

            activeThreads = 0;
            newconncounter = 0;

            OnDataAvailable = new tcpServerConnectionChanged(TcpServer1_OnDataAvailable);

            //clientmsg = "OnDataAvailable initialized";
        }
        public int Port
        {
            get
            {
                return m_port;
            }
            set
            {
                if (value < 0)
                {
                    return;
                }

                if (m_port == value)
                {
                    return;
                }

                if (m_isOpen)
                {
                    throw new Exception("Invalid attempt to change port while still open.\nPlease close port before changing.");
                }

                m_port = value;
                if (listener == null)
                {
                    //this should only be called the first time.
                    listener = new TcpListener(_ipAddress, m_port);
                }
                else
                {
                    listener.Server.Bind(new IPEndPoint(_ipAddress, m_port));
                }
            }
        }
        public int MaxSendAttempts
        {
            get
            {
                return m_maxSendAttempts;
            }
            set
            {
                m_maxSendAttempts = value;
            }
        }
        [Browsable(false)]
        public bool IsOpen
        {
            get
            {
                return m_isOpen;
            }
            set
            {
                if (m_isOpen == value)
                {
                    return;
                }

                if (value)
                {
                    Open();
                }
                else
                {
                    Close();
                }
            }
        }
        public List<TCPServerConnection> Connections
        {
            get
            {
                List<TCPServerConnection> rv = new List<TCPServerConnection>();
                rv.AddRange(connections);
                return rv;
            }
        }
        public int IdleTime
        {
            get
            {
                return m_idleTime;
            }
            set
            {
                m_idleTime = value;
            }
        }
        public int MaxCallbackThreads
        {
            get
            {
                return m_maxCallbackThreads;
            }
            set
            {
                m_maxCallbackThreads = value;
            }
        }
        public int VerifyConnectionInterval
        {
            get
            {
                return m_verifyConnectionInterval;
            }
            set
            {
                m_verifyConnectionInterval = value;
            }
        }
        public Encoding Encoding
        {
            get
            {
                return m_encoding;
            }
            set
            {
                Encoding oldEncoding = m_encoding;
                m_encoding = value;
                foreach (TCPServerConnection client in connections)
                {
                    if (client.Encoding == oldEncoding)
                    {
                        client.Encoding = m_encoding;
                    }
                }
                /*foreach (TCPServerConnection client in newconnections)
                {
                    if (client.Encoding == oldEncoding)
                    {
                        client.Encoding = m_encoding;
                    }
                }*/
            }
        }
        public void SetEncoding(Encoding encoding, bool changeAllClients)
        {
            Encoding oldEncoding = m_encoding;
            m_encoding = encoding;
            if (changeAllClients)
            {
                foreach (TCPServerConnection client in connections)
                {
                    client.Encoding = m_encoding;
                }
            }
        }
        private void RunListener()
        {
            while (m_isOpen && m_port >= 0)
            {
                try
                {
                    if (listener.Pending())
                    {
                        TcpClient socket = listener.AcceptTcpClient();
                        TCPServerConnection conn = new TCPServerConnection(socket, m_encoding);

                        if (OnConnect != null)
                        {
                            lock (activeThreadsLock)
                            {
                                activeThreads++;
                            }
                            conn.CallbackThread = new Thread(() =>
                            {
                                OnConnect(conn);
                            });
                            conn.CallbackThread.Start();
                        }

                        lock (connections)
                        {
                            conn.Socket.Client.Blocking = true;
                            conn.Socket.Client.SendBufferSize = 1024 * 1024 * 2;
                            connections.Add(conn);
                            newconncounter++;
                        }
                    }
                    else
                    {
                        System.Threading.Thread.Sleep(m_idleTime);
                    }
                }
                catch (ThreadInterruptedException) { } //thread is interrupted when we quit
                catch (Exception e)
                {
                    if (m_isOpen && OnError != null)
                    {
                        OnError(this, e);
                    }
                }
            }
        }
        private void RunSender()
        {
            while (m_isOpen && m_port >= 0)
            {
                try
                {
                    bool moreWork = false;
                    for (int i = 0; i < connections.Count; i++)
                    {
                        if (connections[i].CallbackThread != null)
                        {
                            try
                            {
                                connections[i].CallbackThread = null;
                                lock (activeThreadsLock)
                                {
                                    activeThreads--;
                                }
                            }
                            catch (Exception)
                            {
                                //an exception is thrown when setting thread and old thread hasn't terminated
                                //we don't need to handle the exception, it just prevents decrementing activeThreads
                            }
                        }

                        if (connections[i].CallbackThread != null)
                        {
                            //clientmsg = "there is no callback";
                        }
                        else if (connections[i].Connected() &&
                            (connections[i].LastVerifyTime.AddMilliseconds(m_verifyConnectionInterval) > DateTime.UtcNow ||
                             connections[i].VerifyConnected()))
                        {
                            moreWork = moreWork || ProcessConnection(connections[i]);
                            //clientmsg = "processed";
                        }
                        else
                        {
                            //clientmsg = "connection is removed";
                            lock (connections)
                            {
                                connections.RemoveAt(i);
                                /*TCPQs.RemoveAt(i);
                                if(ConnectionsIndex[i] >= 0)
                                {
                                    ConnectionsStat[ConnectionsIndex[i]] = false;
                                }
                                ConnectionsIndex.RemoveAt(i);
                                ProcState.RemoveAt(i);
                                LastPackets.RemoveAt(i);
                                LastBytes.RemoveAt(i);*/
                                newconncounter--;
                                i--;
                            }
                        }
                    }

                    if (!moreWork)
                    {
                        System.Threading.Thread.Yield();
                        lock (sem)
                        {
                            foreach (TCPServerConnection conn in connections)
                            {
                                if (conn.HasMoreWork())
                                {
                                    moreWork = true;
                                    break;
                                }
                            }
                        }
                        if (!moreWork)
                        {
                            waiting = true;
                            sem.Wait(m_idleTime);
                            waiting = false;
                        }
                    }
                }
                catch (ThreadInterruptedException) { } //thread is interrupted when we quit
                catch (Exception e)
                {
                    if (m_isOpen && OnError != null)
                    {
                        OnError(this, e);
                    }
                }
            }
        }
        public static bool endOfBuffer = false;
        public string GreToPer(DateTime gre)
        {
            PersianCalendar per = new PersianCalendar();
            string result = "";
            try
            {
                int year = per.GetYear(gre);
                int month = per.GetMonth(gre);
                int day = per.GetDayOfMonth(gre);

                int hour = per.GetHour(gre);
                int min = per.GetMinute(gre);
                int sec = per.GetSecond(gre);

                string dayOfWeek = per.GetDayOfWeek(gre).ToString();

                result = year + "_" + month + "_" + day + "_" + dayOfWeek + "_" + hour + "_" + min + "_" + sec;
            }
            catch
            {
                int a = 0;
            }
            return result;
        }
        public static int calibDefault = -1;
        //send
        private void SendData()
        {
            byte[] data;
            int DataCount;
            List<byte> sendlist = new List<byte>();
            while (m_isOpen && m_port >= 0)
            {
                try
                {
                    DataCount = QSendPacket.DataCount;
                    if (DataCount != 0)
                    {
                        for (int i = 0; i < DataCount; i++)
                        {
                            data = QSendPacket.Data;
                            for (int j = 0; j < connections.Count; j++)//if(connections.Count != 0)//(ConnectionsStat[0])
                            {
                                SendPacket(data, connections[j]);//Clients[0]);                
                            }
                        }
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }
                catch
                {
                    //do nothing, a client is disconnected and a connection is removed from the list                    
                }
            }
        }
        // detected clients
        private bool ProcessConnection(TCPServerConnection conn)
        {
            bool moreWork = false;
            if (conn.ProcessOutgoing(m_maxSendAttempts))
            {
                moreWork = true;
            }

            if ((OnDataAvailable != null) && (activeThreads < m_maxCallbackThreads) && (conn.Socket.Available > 0))
            {
                lock (activeThreadsLock)
                {
                    activeThreads++;
                }
                conn.CallbackThread = new Thread(() =>
                {
                    OnDataAvailable(conn);

                });
                conn.CallbackThread.Start();
                Thread.Yield();

                //clientmsg = "data handler set up ok";
            }
            /*else
            {
                clientmsg = "handler is not ok " + activeThreads.ToString() + " " + m_maxCallbackThreads.ToString() + " " + conn.Socket.Available.ToString() + " " + OnDataAvailable.ToString();
            }*/
            return moreWork;
        }
        public void Open()
        {
            lock (this)
            {
                if (m_isOpen)
                {
                    //already open, no work to do
                    return;
                }
                if (m_port < 0)
                {
                    throw new Exception("Invalid port");
                }

                try
                {
                    listener.Start(5);
                }
                catch (Exception)
                {
                    listener.Stop();
                    listener = new TcpListener(IPAddress.Any, m_port);
                    listener.Start(5);
                }

                m_isOpen = true;

                listenThread = new Thread(new ThreadStart(RunListener));
                listenThread.Start();

                sendThread = new Thread(new ThreadStart(RunSender));
                sendThread.Start();

                //manageThread = new Thread(new ThreadStart(ReciveData));
                //manageThread.Start();

                forwardThread = new Thread(new ThreadStart(SendData));
                forwardThread.Start();

            }
        }
        private void TcpServer1_OnDataAvailable(TCPServerConnection connection)
        {
            byte[] data = ReadStream(connection.Socket);

            if (data != null)
            {
                //string dataStr = Encoding.ASCII.GetString(data);
                //receive controlling data from the clients******************************************
                //SendPacket(data, connection);

                int clientnum = connections.IndexOf(connection);

                QReceivePacket.Data = data;
                //for (int i = 0; i < data.Length; i++)
                //{
                //    TCPQ.Data = data[i];
                //    ////TCPQs[clientnum].Data = data[i];
                //    ////TCPQs[0].Data = data[i];
                //}

                //clientmsg = "data is received from " + clientnum.ToString();

                data = null;
            }
            //else            
            //clientmsg = "data is received!";
        }
        protected byte[] ReadStream(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            if (stream.DataAvailable)
            {
                byte[] data = new byte[client.Available];

                int bytesRead = 0;
                try
                {
                    bytesRead = stream.Read(data, 0, data.Length);
                }
                catch (IOException)
                {
                }

                if (bytesRead < data.Length)
                {
                    byte[] lastData = data;
                    data = new byte[bytesRead];
                    Array.ConstrainedCopy(lastData, 0, data, 0, bytesRead);
                }
                return data;
            }
            return null;
        }
        public void Close()
        {
            if (!m_isOpen)
            {
                return;
            }

            lock (this)
            {
                m_isOpen = false;
                foreach (TCPServerConnection conn in connections)
                {
                    conn.ForceDisconnect();
                }
                try
                {
                    if (listenThread.IsAlive)
                    {
                        listenThread.Interrupt();

                        Thread.Yield();
                        if (listenThread.IsAlive)
                        {
                            listenThread.Abort();
                        }
                    }
                }
                catch (System.Security.SecurityException) { }
                try
                {
                    if (sendThread.IsAlive)
                    {
                        sendThread.Interrupt();

                        Thread.Yield();
                        if (sendThread.IsAlive)
                        {
                            sendThread.Abort();
                        }
                    }
                }
                catch (System.Security.SecurityException) { }
                try
                {
                    if (manageThread.IsAlive)
                    {
                        manageThread.Interrupt();

                        Thread.Yield();
                        if (manageThread.IsAlive)
                        {
                            manageThread.Abort();
                        }
                    }
                }
                catch (System.Security.SecurityException) { }
                try
                {
                    if (forwardThread.IsAlive)
                    {
                        forwardThread.Interrupt();

                        Thread.Yield();
                        if (forwardThread.IsAlive)
                        {
                            forwardThread.Abort();
                        }
                    }
                }
                catch (System.Security.SecurityException) { }
            }
            listener.Stop();

            lock (connections)
            {
                connections.Clear();
                //TCPQs.Clear();
                //ConnectionsIndex.Clear();
                //ProcState.Clear();
                //LastPackets.Clear();
                LastBytes.Clear();
            }

            /*lock (newconnections)
            {                    
                newconnections.Clear();
                newconncounter = 0; 
            }*/

            listenThread = null;
            sendThread = null;
            manageThread = null;
            forwardThread = null;
            GC.Collect();
        }
        public void SendPacket(byte[] data, TCPServerConnection conn)
        {
            try
            {
                conn.SendDataBytes(data);
            }
            catch
            {
            }
        }
    }
    public class TCPServerConnection
    {
        private TcpClient m_socket;
        private List<byte[]> messagesToSend;
        private int attemptCount;

        private Thread m_thread = null;

        private DateTime m_lastVerifyTime;

        private Encoding m_encoding;

        private int offsetind = 0;

        public static int totalreset = 0, errorwhilesending = 0;
        public static int totalpacketsent = 0;

        public TCPServerConnection(TcpClient sock, Encoding encoding)
        {
            m_socket = sock;
            messagesToSend = new List<byte[]>();
            attemptCount = 0;

            m_lastVerifyTime = DateTime.UtcNow;
            m_encoding = encoding;
        }

        public bool Connected()
        {
            try
            {
                return m_socket.Connected;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool VerifyConnected()
        {
            //note: `Available` is checked before because it's faster,
            //`Available` is also checked after to prevent a race condition.
            bool connected = m_socket.Client.Available != 0 ||
                !m_socket.Client.Poll(1, SelectMode.SelectRead) ||
                m_socket.Client.Available != 0;
            m_lastVerifyTime = DateTime.UtcNow;
            return connected;
        }
        public static int MaxTCPTXPackSize = 10;
        public bool ProcessOutgoing(int maxSendAttempts)
        {
            lock (m_socket)
            {
                if (!m_socket.Connected)
                {
                    messagesToSend.Clear();
                    totalreset++;
                    return false;
                }

                if (messagesToSend.Count == 0)
                {
                    return false;
                }

                NetworkStream stream = m_socket.GetStream();
                try
                {
                    //byte [] tempb = messagesToSend[0].Select((s, i) => (i < 9)? s : (byte)totalpacketsent).ToArray();

                    //StreamWriter wo = new StreamWriter("/home/nafagh/serverout2.txt", true);
                    /*for(int i = 0; i < messagesToSend[0].Length; i++)
                    {
                        wo.WriteLine(messagesToSend[0][i].ToString());
                    }*/
                    /*for(int i = 0; i < tempb.Length; i++)
                    {
                        wo.WriteLine(tempb[i].ToString());
                    }
                    wo.Close();*/


                    //stream.Write(messagesToSend[0], 0, messagesToSend[0].Length); 
                    //stream.Write(messagesToSend[0], offsetind, messagesToSend[0].Length); 

                    if (messagesToSend[0].Length > MaxTCPTXPackSize)
                    {
                        if (offsetind == 0)
                        {
                            stream.Write(messagesToSend[0], offsetind, messagesToSend[0].Length / 2);
                            offsetind = messagesToSend[0].Length / 2;
                        }
                        else
                        {
                            stream.Write(messagesToSend[0], offsetind, messagesToSend[0].Length - offsetind);
                            offsetind = 0;
                        }

                        Thread.Sleep(1);
                    }
                    else
                    {
                        offsetind = 0;
                        stream.Write(messagesToSend[0], offsetind, messagesToSend[0].Length);
                    }


                    //int numoffrags = (tempb.Length / Parameters.MaxTCPTXPackSize) + 1; 
                    //int len = (int)Math.Min(tempb.Length / numoffrags, tempb.Length - offsetind); 

                    //stream.Write(tempb, offsetind, tempb.Length); 
                    //stream.Write(tempb, offsetind, len);
                    //offsetind = offsetind + len;
                    //offsetind = (offsetind >= tempb.Length)? 0 : offsetind;

                    //totalpacketsent++;               

                    lock (messagesToSend)
                    {
                        if (offsetind == 0)
                        {
                            messagesToSend.RemoveAt(0);
                            totalpacketsent++;
                        }
                    }
                    attemptCount = 0;
                }
                catch (System.IO.IOException)
                {
                    //occurs when there's an error writing to network
                    attemptCount++;
                    if (attemptCount >= maxSendAttempts)
                    {
                        //TODO log error

                        lock (messagesToSend)
                        {
                            messagesToSend.RemoveAt(0);
                            errorwhilesending++;
                        }
                        attemptCount = 0;
                    }
                }
                catch (ObjectDisposedException)
                {
                    //occurs when stream is closed
                    m_socket.Close();
                    return false;
                }
            }
            return messagesToSend.Count != 0;
        }

        public void SendData(string data)
        {
            byte[] array = m_encoding.GetBytes(data);
            lock (messagesToSend)
            {
                messagesToSend.Add(array);
            }
        }

        public void SendDataBytes(byte[] data)
        {
            //byte[] array = m_encoding.GetBytes(data);
            lock (messagesToSend)
            {
                messagesToSend.Add(data);
            }
        }

        public void ForceDisconnect()
        {
            lock (m_socket)
            {
                m_socket.Close();
            }
        }

        public bool HasMoreWork()
        {
            return messagesToSend.Count > 0 || (Socket.Available > 0 && CanStartNewThread());
        }

        private bool CanStartNewThread()
        {
            if (m_thread == null)
            {
                return true;
            }
            return (m_thread.ThreadState & (System.Threading.ThreadState.Aborted | System.Threading.ThreadState.Stopped)) != 0 &&
                   (m_thread.ThreadState & System.Threading.ThreadState.Unstarted) == 0;
        }

        public TcpClient Socket
        {
            get
            {
                return m_socket;
            }
            set
            {
                m_socket = value;
            }
        }

        public Thread CallbackThread
        {
            get
            {
                return m_thread;
            }
            set
            {
                if (!CanStartNewThread())
                {
                    throw new Exception("Cannot override TcpServerConnection Callback Thread. The old thread is still running.");
                }
                m_thread = value;
            }
        }

        public DateTime LastVerifyTime
        {
            get
            {
                return m_lastVerifyTime;
            }
        }

        public Encoding Encoding
        {
            get
            {
                return m_encoding;
            }
            set
            {
                m_encoding = value;
            }
        }
    }
    #endregion
    #region Client
    public class TCPClient
    {
        public TCPClient(IPAddress iIPAddr, int iPortNum)
        {
            QReceivePacket = new StorageOfArrayByte();
            QSendPacket = new StorageOfArrayByte();
            this._portNum = iPortNum;
            this._ipAddress = iIPAddr;

            stopThread = false;

            Thread receiveThread = new Thread(ReceiveFunc);
            receiveThread.Start();

            Thread sendThread = new Thread(SendFunc);
            sendThread.Start();
        }
        private static TcpClient tcpClient = new TcpClient();
        private IPAddress _ipAddress = IPAddress.Any;
        private int _portNum = 0;
        IPEndPoint remoteEP;
        public static StorageOfArrayByte QReceivePacket;
        public static StorageOfArrayByte QSendPacket;
        private static byte[] receivedbytes;
        private bool m_isOpen;
        private bool stopThread = false;
        public bool IsConnected = false;
        public bool IsOpen
        {
            get
            {
                return m_isOpen;
            }
            set
            {
                //m_isOpen = value;
            }
        }
        StreamWriter sw = new StreamWriter("TCPClient_log.txt");
        private void ReceiveFunc()
        {
            Stream stm;
            int counter = 0;
            //int PackSize = 0;
            while (!stopThread)
            {
                //m_isOpen = tcpClient.Connected;
                if (m_isOpen)
                {
                    IsConnected = true;
                    //read incoming data
                    receivedbytes = new byte[tcpClient.Available];

                    if (tcpClient.Available != 0)
                    {
                        counter = 0;

                        stm = tcpClient.GetStream();
                        int numofbytes = stm.Read(receivedbytes, 0, tcpClient.Available);
                        QReceivePacket.Data = receivedbytes;
                    }
                    else
                    {
                        counter++;
                        if (counter > 10000)
                        {
                            //m_isOpen = false;
                        }
                    }

                    Thread.Sleep(10);
                }
                else
                {
                    Thread.Sleep(1000);//
                    counter = 0;
                    tcpClient = null;
                    tcpClient = new TcpClient();
                    //m_isOpen = CheckAndConnect(ipaddr, port); 

                    try
                    {
                        remoteEP = new IPEndPoint(_ipAddress, _portNum);
                        tcpClient.Connect(remoteEP);
                        m_isOpen = true;
                    }
                    catch
                    {
                        //connection refused by server
                        m_isOpen = false;
                        //port++;
                    }
                }
            }
        }
        private void SendFunc()
        {
            byte[] data;
            while (!stopThread)
            {
                if (QSendPacket.DataCount > 0)
                {
                    data = QSendPacket.Data;
                    if (m_isOpen)
                    {
                        tcpClient.Client.Send(data);
                    }
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
        }
        public void Stop()
        {
            //stop active threads
            stopThread = true;

            //close the port
            Thread.Sleep(100);
            if (m_isOpen)
            {
                tcpClient.Close();
                m_isOpen = false;
            }
        }
    }
    #endregion
}
