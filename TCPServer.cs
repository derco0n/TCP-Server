using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;


namespace TCP_Server
{
    public class TCPServer
    {
        #region statics
        /// <summary>
        /// Default Constants.
        /// </summary>
        public static IPAddress DEFAULT_SERVERIP = IPAddress.Parse("127.0.0.1");
        public static int DEFAULT_PORT = 5555;
        public static float Version = 0.38f;
        public static String vDate = "20210512";

        #endregion

        #region Variables
        #region private
        /// <summary>
        /// Local Variables Declaration.
        /// </summary>
        private TcpListener _server = null;
        private bool _stopServer = false;
        private bool _stopPurging = false;
        private Thread _serverThread = null;
        private Thread _purgingThread = null;
        private ArrayList _socketListenersList = null;
        private ArrayList _currentClients = null;
        private bool _autodisconnectclients = false; //Don't Auto-disconnect inactive clients: Only disconnect clients on socket-errors
        private int _clientspurgeintervall = 3; //Intervall in senconds on which to check and remove inactive connections (suggestion: 3-10)
        private int _clientbufffersize = 1024;  //Default Buffersize for Clienthandlers
        private int _maximumclients = 200;  //Maximum number of active clients
        private bool _oneconnectionperclient;  //just one-connection per client
        private bool _dscp_prioritized;  //DSCP-Flags set to prioritize TCP-Packets on client sockets
        #endregion
        #region public
        public int _clientstimeoutseconds = 30; //90 //Client-idle-timeout in seconds. This is only needed if  _autodisconnectclients is set to true
        #endregion

        #endregion

        #region events
        /// <summary>
        /// TCP Events.
        /// </summary>
        //public event TCPServerMessageEventHandler TCPServerMessageEvent; //Networkmessages
        public event TCPServerErrorEventHandler TCPServerErrorEvent; //Servererrors
        public event TCPServerStateEventHandler TCPServerStateEvent; //ServerStates
        public event TCPServerClientsEventHandler TCPServerClientsEvent; //Server's Clientstates
        public event TCPSocketSendReceiveHandler ClientDataReceived; //Data Received from Client
        public event TCPSocketSendReceiveHandler ClientDataSent; //Data Sent Client
        //public event ErgebnisEventHandler ErgebnisÜbergabeEvent;
        #endregion

        #region properties
        /// <summary>
        /// Returns the number of active client connections
        /// </summary>
        public int Clientscount
        {
            get
            {
                if (this._socketListenersList != null)
                {//socketListeners is already initialized
                    return this._socketListenersList.Count;
                }
                else
                {
                    return 0; //socketListeners is not initialized. Therefore no client can be connected
                }
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of allowed clients
        /// </summary>
        public int maxClients
        {
            get
            {
                return this._maximumclients;
            }
            set
            {
                this.maxClients = value;
            }

        }

        /// <summary>
        /// Returns an ArrayList containing all connected Clients, reprensented as EndPoint
        /// </summary>
        public ArrayList connectedClients
        {
            get
            {
                ArrayList myreturn = null;

                lock (this._currentClients)
                {
                    myreturn = this._currentClients;
                }
                return myreturn;
            }
        }

        /// <summary>
        /// Returns all active client-sessions from a given IP-Adress
        /// </summary>
        /// <param name="ClientIP">The clients IP-Adress whose sessions we want</param>
        /// <returns>An Arraylist containing all TCPConnectionHandler's Sessions for the IP</returns>
        public ArrayList ClientSessions(String ClientIP)
        {
            ArrayList myreturn = new ArrayList();

            lock (this._socketListenersList)
            {
                foreach (TCPConnectionHandler socketListener in this._socketListenersList)
                {
                    if (socketListener.ClientIP.Equals(ClientIP))
                    {
                        myreturn.Add(socketListener);
                    }
                }
            }
            return myreturn;
        }

        /// <summary>
        /// Returns all IP-Adresses of all connected clients
        /// </summary>
        public ArrayList connectedIps
        {
            get
            {
                ArrayList myreturn = new ArrayList();

                ArrayList Endpoints = this.connectedClients;
                foreach (EndPoint ep in Endpoints)
                {
                    String sep = ep.ToString();
                    sep = sep.Substring(0, sep.IndexOf(':'));
                    myreturn.Add(IPAddress.Parse(sep));
                }
                return myreturn;
            }
        }

        #endregion

        #region constructor_destructor

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="ServerListenIP">IP on which the server should listen. (e.g. set this to TCPServer.DEFAULT_SERVERIP)</param>
        /// <param name="port">The TCP-port on which the server should listen. (e.g. set this to TCPServer.DEFAULT_PORT)</param>
        /// <param name="srverr_eventsubscriber">DEFAULT=null. Optionally define a handler for server-errors</param>
        /// <param name="srvstate_eventsubscriber">DEFAULT=null. Optionally define a handler for server-state-transitions</param>
        /// <param name="srvclient_eventsubscriber">DEFAULT=null. Optionally define a handler for server-clientstates</param>   
        /// <param name="clientbuffersize">DEFAULT=1024. Optionally define the size (64-4096) used for client-socket-buffer to tune network load</param>   
        /// <param name="oneconnectionperclient">limits the maximum connections per client to 1</param>   
        /// <param name="dscp_prioritized">Enables higher packet-priority for client-sockets by setting DSCP-Flags (https://de.wikipedia.org/wiki/DiffServ)</param>   
        public TCPServer(
            IPAddress ServerListenIP,
            int port,
            TCPServerErrorEventHandler srverr_eventsubscriber = null/*Optionally define a handler for server-errors*/,
            TCPServerStateEventHandler srvstate_eventsubscriber = null/*Optionally define a handler for server-state-transitions*/,
            TCPServerClientsEventHandler srvclient_eventsubscriber = null/*Optionally define a handler for server-clientstates*/,
            int clientbuffersize = 1024,
            bool oneconnectionperclient = false,
            bool dscp_prioritized=false
            )

        {

            this._oneconnectionperclient = oneconnectionperclient;
            this._dscp_prioritized = dscp_prioritized;

            //If a eventhandler is given, register ServerErrorEvent to that handler
            if (srverr_eventsubscriber != null)
            {
                this.TCPServerErrorEvent += srverr_eventsubscriber;
            }

            //If a eventhandler is given, register ServerStateEvent to that handler
            if (srvstate_eventsubscriber != null)
            {
                this.TCPServerStateEvent += srvstate_eventsubscriber;
            }

            //If a eventhandler is given, register ServerClientsEvent to that handler
            if (srvclient_eventsubscriber != null)
            {
                this.TCPServerClientsEvent += srvclient_eventsubscriber;
            }

            if (clientbuffersize >= 64 && clientbuffersize <= 4096)
            {
                this._clientbufffersize = clientbuffersize;
            }

            Init(new IPEndPoint(ServerListenIP, port));
            this.StartServer();
        }

        /// <summary>
        /// Destructor.
        /// </summary>
        ~TCPServer()
        {
            StopServer();
        }
        #endregion

        #region Methods

        #region Protected

        #endregion

        #region Private
        /// <summary>
        /// Init method that create a server (TCP Listener) Object based on the
        /// IP Address and Port information that is passed in.
        /// </summary>
        /// <param name="ipNport"></param>
        private void Init(IPEndPoint ipNport)
        {
            try
            {
                this._server = new TcpListener(ipNport);
                // Create a directory for storing client sent files.
                if (!Directory.Exists(TCPConnectionHandler.DEFAULT_FILE_STORE_LOC))
                {
                    Directory.CreateDirectory(
                        TCPConnectionHandler.DEFAULT_FILE_STORE_LOC);
                }
            }
            catch (Exception e)
            {
                if (this.TCPServerErrorEvent != null)
                {
                    this.TCPServerErrorEvent(this, new TCPServerErrorEventArgs(1, e)); //Errorcode 1. Initializationerror
                }
                this._server = null;
            }
        }

        /// <summary>
        /// Method that stops all clients and clears the list.
        /// </summary>
        private void StopAllSocketListeners()
        {
            foreach (TCPConnectionHandler socketListener
                         in this._socketListenersList)
            {
                //socketListener.TCPSocketListenerEvent -= MessageEventHandler;
                socketListener.OnDataReceived -= Handle_ClientConnectionDataReceived;
                socketListener.OnDataSent -= Handle_ClientConnectionDataSent;
                socketListener.ClientConnectDisconnect -= this.Handle_Client_Dis_Connected;

                socketListener.StopSocketListener();
            }
            // Remove all elements from the list.
            this._socketListenersList.Clear();
            this._socketListenersList = null;
        }

        /// <summary>
		/// TCP/IP Server Thread that is listening for clients.
		/// </summary>
		private void ServerThreadStart()
        {
            // Client Socket variable;
            Socket clientSocket = null;
            TCPConnectionHandler socketListener = null;

            while (!this._stopServer)
            {
                if (this.Clientscount < this._maximumclients)
                { // Only accept new connections if maximum clients is not exceeded    

                    try
                    {
                        // Wait for any client requests and if there is any 
                        // request from any client accept it (Wait indefinitely).
                        clientSocket = this._server.AcceptSocket();

                        if (this._dscp_prioritized)
                        {
                            try
                            {
                                //Try to set the socketoptions in order to QoS-prioritize packets vis DSCP
                                clientSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.BsdUrgent, 1);
                                clientSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.Expedited, 1);
                                clientSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, 1);

                                //Decimal 184 is DSCP-Value 46. .Net doesn't support the 8bit ToS-Field, but not to set the 6Bit-DSCP directly.
                                clientSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, 184);
                            }
                            catch
                            {

                            }
                        }
                        

                        if (this._oneconnectionperclient)
                        {//If client-connections should be limited to one per client

                            IPEndPoint newEndpoint = (IPEndPoint)clientSocket.RemoteEndPoint;
                            //bool shoulddenyconnect = false;
                            this.disconnectclients(newEndpoint);   // Disconnect all previous connections with the same IP                            
                        }

                        //Further process the new incoming connection

                        // Create a SocketListener object for the client.
                        socketListener = new TCPConnectionHandler(clientSocket, _clientstimeoutseconds, _autodisconnectclients, this._clientbufffersize);

                        //socketListener.TCPSocketListenerEvent += this.EventHandler;

                        // Add the socket listener to an array list in a thread 
                        // safe fashion.
                        //Monitor.Enter(m_socketListenersList);
                        lock (this._socketListenersList)
                        {
                            this._socketListenersList.Add(socketListener);
                            lock (this._currentClients) //Add entry to list of connected clients as well
                            {
                                this._currentClients.Add(socketListener.RemoteEndPoint);
                            }
                            //socketListener.TCPSocketListenerEvent += this.MessageEventHandler;
                            socketListener.OnDataReceived += this.Handle_ClientConnectionDataReceived;
                            socketListener.OnDataSent += this.Handle_ClientConnectionDataSent;
                            socketListener.ClientConnectDisconnect += this.Handle_Client_Dis_Connected;


                            if (this.TCPServerClientsEvent != null) //We have to do this here, as the socketListener-object already exists and would have raised the event before we subscribed it.
                            {
                                this.TCPServerClientsEvent(
                                    this,
                                    new TCPServerClientsEventArgs(
                                        1, socketListener.RemoteEndPoint
                                        )
                                    ); //Event: Client connected
                            }


                        }

                        //Monitor.Exit(m_socketListenersList);

                        // Start a communication with the client in a different
                        // thread.
                        socketListener.StartSocketListener();
                    }
                    catch (SocketException se)
                    {
                        if (se.ErrorCode != 10004/*Threadabort: WSA Abort blocking.*/)
                        {
                            //This is a socket error, that is not caused by Stopping the server.
                            if (this.TCPServerErrorEvent != null)
                            {
                                this.TCPServerErrorEvent(this, new TCPServerErrorEventArgs(4, se)); //Errorcode 4. Socketexception
                            }
                        }
                        this._stopServer = true;
                    }
                }
            }
        }

        /// <summary>
        /// Will close the connection of a given TCPClient gracefully
        /// </summary>
        /// <param name="connhandler">The connectionshandler</param>
        public void disconnectclient(TCPConnectionHandler connhandler)
        {
            connhandler.StopSocketListener();  //... stop the connectionhandler
        }

        /// <summary>
        /// Will close all Remoteendpoints (Clients) with a given IP-Endpoint
        /// </summary>
        /// <param name="remoteendpoint">The IP-Endpoint we want to disconnect</param>
        private void disconnectclients(IPEndPoint remoteendpoint)
        {
            lock (this._socketListenersList)
            {
                foreach (TCPConnectionHandler connhandler in this._socketListenersList)
                {
                    if (
                            connhandler.ClientIP.Equals(remoteendpoint.Address.ToString()) && //If the IP-Address of the current connectionhandler matches...
                            connhandler.RemotePort != remoteendpoint.Port //...but the Ports does not match... 
                        ) //... this means that this must be another connection from the same host, but not the same connection
                    {
                        connhandler.StopSocketListener(); //... stop the connectionhandler of the secondary/other connection                       
                    }
                }
            }
        }

        /// <summary>
		/// Thread method for purging Client Listeners that are marked for
		/// deletion (i.e. clients with socket connection closed). This thread
		/// is a low priority thread and sleeps for the time defined in this._clientspurgeintervall and then checks
		/// for any client SocketConnection objects which are obsolete and 
		/// marked for deletion.
		/// </summary>
		private void PurgingThreadStart()
        {
            while (!this._stopPurging)
            {
                ArrayList deleteList = new ArrayList();

                // Check for any client SocketListeners that are to be
                // deleted and put them in a separate list in a thread safe
                // fashion.
                //Monitor.Enter(m_socketListenersList);
                lock (this._socketListenersList)
                {
                    foreach (TCPConnectionHandler socketListener
                                 in this._socketListenersList)
                    {
                        if (socketListener.IsMarkedForDeletion())
                        {
                            deleteList.Add(socketListener);
                            socketListener.StopSocketListener();
                        }
                    }

                    lock (this._currentClients) //Remove entry from list of connected clients as well
                    {
                        for (int i = 0; i < deleteList.Count; ++i)
                        {
                            TCPConnectionHandler temp = (TCPConnectionHandler)deleteList[i];
                            this._currentClients.Remove(temp.RemoteEndPoint);
                        }
                    }

                    // Delete all the client SocketConnection ojects which are
                    // in marked for deletion and are in the delete list.
                    for (int i = 0; i < deleteList.Count; ++i)
                    {
                        /*
                         // Commented out, as we are now subscribing the corresponding event from socketListener
                        EndPoint REndpoint;
                        var obj = deleteList[i]; //).RemoteEndPoint;                        
                        TCPConnectionHandler obj2 = (TCPConnectionHandler)obj;
                        REndpoint = obj2.RemoteEndPoint;
                        if (this.TCPServerClientsEvent != null)
                        {
                            lock (deleteList)
                            {
                                lock (REndpoint)
                                {
                                    this.TCPServerClientsEvent(this, new TCPServerClientsEventArgs(2, REndpoint)); //Event: Client disconnected
                                }
                            }
                        }
                        */
                        this._socketListenersList.Remove(deleteList[i]);
                    }
                }

                //Monitor.Exit(m_socketListenersList);

                deleteList = null;
                Thread.Sleep(this._clientspurgeintervall);
            }
        }
        #endregion

        /// <summary>
        /// Starts the Server
        /// </summary>
        #region Public
        public void StartServer()
        {
            if (this._server != null)
            {
                try
                {
                    // Create an ArrayList for storing SocketListeners before
                    // starting the server.
                    this._socketListenersList = new ArrayList();

                    // Create an ArrayList for storing currently connected Endpoint
                    this._currentClients = new ArrayList();

                    // Start the Server and start the thread to listen client 
                    // requests.
                    this._server.Start();
                    this._serverThread = new Thread(new ThreadStart(ServerThreadStart));
                    this._serverThread.Name = "TCP-Server-Main";
                    this._serverThread.Start();

                    // Create a low priority thread that checks and deletes client
                    // SocketConnection objects that are marked for deletion.
                    this._purgingThread = new Thread(new ThreadStart(PurgingThreadStart));
                    this._purgingThread.Priority = ThreadPriority.Lowest;
                    this._purgingThread.Name = "TCP-Server-Purging";
                    this._purgingThread.Start();

                    if (this.TCPServerStateEvent != null)
                    {
                        this.TCPServerStateEvent(this, new TCPServerStateEventArgs(4)); //Event: Server is Running
                    }

                }
                catch (Exception e)
                {//Error while starting server
                    if (this.TCPServerErrorEvent != null)
                    {
                        this.TCPServerErrorEvent(this, new TCPServerErrorEventArgs(2, e)); //Errorcode 2. Server-Start-Error
                    }

                    if (this.TCPServerStateEvent != null)
                    {
                        this.TCPServerStateEvent(this, new TCPServerStateEventArgs(5)); //Event: Server is stopped
                    }
                }
            }
        }

        /// <summary>
        /// Adds data to send to correct sessionhandler that corresponds to a given endpoint
        /// This will let you send data to a specific active client connection
        /// </summary>
        /// <param name="useful_info">TCPSocketSendReceiveEventArgs are perfect for this, as they contain everything we need</param>
        /// <returns>TRUE on success, otherwise FALSE</returns>
        public bool sendData(TCPSocketSendReceiveEventArgs useful_info/*TCPSocketSendReceiveEventArgs are perfect for this, as they contain everything we need*/)
        {
            bool myreturn = false;

            //First find the correct ConnectionHandler for out Remote-Endpoint
            if (this._socketListenersList != null)
            {
                lock (this._socketListenersList) //(Thread-)lock the list of socketlistners
                {
                    if (this._socketListenersList.Count > 0)
                    {
                        foreach (TCPConnectionHandler elem in this._socketListenersList) //Iterate through the list of active socketlisteners
                        {
                            if (elem != null && elem.RemoteEndPoint != null && elem.RemoteEndPoint.Equals(useful_info.R_Endpoint))
                            {//If the current socketlistener-object is not null and its endpoint is not null as well and the information matches the one we are searching...
                                elem.SendData(useful_info.Data); //Call SendData on the current socketlistener
                                myreturn = true; //set return value
                                break; //break loop
                            }
                        }
                    }
                }
            }
            return myreturn;
        }

        /// <summary>
		/// Method that stops the TCP/IP Server.
		/// </summary>
		public void StopServer()
        {
            if (_server != null)
            {
                try
                {
                    if (this.TCPServerStateEvent != null)
                    {
                        this.TCPServerStateEvent(this, new TCPServerStateEventArgs(3)); //Event: Server is stopping...
                    }

                    // It is important to Stop the server first before doing
                    // any cleanup. If not so, clients might being added as
                    // server is running, but supporting data structures
                    // (such as _socketListenersList) are cleared. This might
                    // cause exceptions.

                    // Stop the TCP/IP Server.
                    this._stopServer = true;
                    this._server.Stop();

                    // Wait for two seconds for the the thread to stop if it is still alive.
                    if (this._serverThread.IsAlive)
                    {
                        this._serverThread.Join(2000);
                    }

                    // If still alive; Get rid of the thread.
                    if (this._serverThread.IsAlive)
                    {
                        this._serverThread.Abort();
                    }
                    this._serverThread = null;

                    this._stopPurging = true;
                    this._purgingThread.Join(2000);
                    if (this._purgingThread.IsAlive)
                    {
                        this._purgingThread.Abort();
                    }
                    this._purgingThread = null;

                    // Free Server Object.
                    this._server = null;

                    // Stop All clients.
                    this.StopAllSocketListeners();


                    if (this.TCPServerStateEvent != null)
                    {
                        this.TCPServerStateEvent(this, new TCPServerStateEventArgs(5)); //Event: Server is stopped
                    }
                }
                catch (Exception e)
                {//Error while stopping the server
                    if (this.TCPServerErrorEvent != null)
                    {
                        this.TCPServerErrorEvent(this, new TCPServerErrorEventArgs(3, e)); //Errorcode 3. Error on stop
                    }
                    //Serverstate is unknown here, as an error occured on stop
                    if (this.TCPServerStateEvent != null)
                    {
                        this.TCPServerStateEvent(this, new TCPServerStateEventArgs(6)); //Event: Serverstate is unknown
                    }
                }
            }
        }

        /// <summary>
        /// Handles the event, that data is received from a client
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        public void Handle_ClientConnectionDataReceived(object sender, TCPSocketSendReceiveEventArgs args)
        {
            if (this.ClientDataReceived != null)
            {
                this.ClientDataReceived(this, args);
            }
        }

        /// <summary>
        /// Handles the event, that data is sent to a client
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        public void Handle_ClientConnectionDataSent(object sender, TCPSocketSendReceiveEventArgs args)
        {
            if (this.ClientDataSent != null)
            {
                this.ClientDataSent(this, args);
            }
        }

        /// <summary>
        /// Handles the event, that a client has connected/disconnected
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        public void Handle_Client_Dis_Connected(object sender, TCPServerClientsEventArgs args)
        {
            if (this.TCPServerClientsEvent != null)
            {
                this.TCPServerClientsEvent(this, args);
            }
        }

        #endregion

        #endregion
    }
}