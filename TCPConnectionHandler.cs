using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TCP_Server
{
    /// <summary>
    /// Summary description for TCPSocketListener.
    /// </summary>
    public class TCPConnectionHandler
    {
        public static String DEFAULT_FILE_STORE_LOC = @"/Logs/";

        #region Variables
        #region Public_Variables
        public enum STATE { FILE_NAME_READ, DATA_READ, FILE_CLOSED };
        public Encoding _Stringencoding;
        #endregion

        #region Private_Variables
        /// <summary>
        /// Variables that are accessed by other classes indirectly.
        /// </summary>
        private Socket _clientSocket = null;
        private bool _stopClient = false;
        private Thread _clientListenerThread = null;
        private bool _markedForDeletion = false;  //If this is set to true, a purging thread can recognize this flag and purge the listener

        /// <summary>
        /// Working Variables.
        /// </summary>
        private DateTime _lastReceiveDateTime;
        private DateTime _currentReceiveDateTime;

        private bool _autodisconnectclient;  //If set to true, this will automatically disconnect idle clients. If false, clients will only diconnect on socket-errors
        private int _client_timeout;

        private int _buffersize = 1024; //Size (in Bytes) of the Send-/Receivebuffer.
        //private int buffersize = 3; //DEBUG

        private int ClientCycleWaitTime = 3; //We need to pause the client-loop () for a short time to avoid using up all CPU-Ressources. My recommendation: 3-20ms 

        private Queue<Byte> _sendqueue; //Queue with bytes which should be sent to the client

        private String _clientIP; //The clients IP-Address
        private UInt16 _clientPort; //The clients Port
        #endregion
        #endregion

        #region events
        /// <summary>
        /// Events.
        /// </summary>
        public event TCPSocketSendReceiveHandler OnDataReceived; //Data received from client
        public event TCPSocketSendReceiveHandler OnDataSent; //Data sent to a client
        public event TCPServerClientsEventHandler ClientConnectDisconnect; //Client Disconnected/Connected        
        #endregion

        #region Properties

        /// <summary>
        /// Returns the RemoteEndpoint that is connected to this ConnectionHandler-Thread or NULL in case of an error
        /// </summary>
        public EndPoint RemoteEndPoint
        {
            get
            {
                EndPoint myreturn = null;

                if (this._clientSocket != null)
                {
                    lock (this._clientSocket)
                    {
                        try
                        {//Try to set a return value
                            myreturn = this._clientSocket.RemoteEndPoint;
                        }
                        catch
                        {
                            //Setting return value not possible. Returning null
                        }
                    }
                }
                return myreturn;
            }
        }

        /// <summary>
        /// Gets the RemoteIP of the client that just connected/disconnected
        /// </summary>
        public String ClientIP
        {
            get
            {
                return this._clientIP;
            }
        }

        /// <summary>
        /// Gets the RemotePort of the client that just connected/disconnected
        /// </summary>
        public UInt16 RemotePort
        {
            get
            {
                return this._clientPort;
            }
        }

        #endregion

        #region Constructor_Destructor
        /// <summary>
        /// Client Socket Listener Constructor.
        /// </summary>
        /// <param name="clientSocket"></param>
        /// <param name="timeout">clienttimeout in seconds (DEFAULT: 15) only used if autodisconnectclient is true</param>
        /// <param name="autodisconnectclient">automatically disconnect the client socket after a certain time of inactivity (DEFAULT: true)</param>
        /// <param name="buffersize">DEFAULT=1024. Optionally define the size (64-4096) used for client-socket-buffer to tune network load</param>   
        public TCPConnectionHandler(Socket clientSocket, int timeout = 15, bool autodisconnectclient = true, int buffersize = 1024)
        {
            this._Stringencoding = Encoding.UTF8;

            this._client_timeout = timeout;
            this._autodisconnectclient = autodisconnectclient;
            if (buffersize >= 64 && buffersize <= 4096)
            {
                this._buffersize = buffersize;
            }
            _clientSocket = clientSocket;
            this._clientIP = ((IPEndPoint)clientSocket.RemoteEndPoint).Address.ToString();  //Save the clients IP to be used in disconnect situations when RemoteEndPoint get NULL
            this._clientPort = Convert.ToUInt16(((IPEndPoint)clientSocket.RemoteEndPoint).Port);  //Save the clients Port to be used in disconnect situations when RemoteEndPoint get NULL

            //Unfortunately we can't do this here, as noone is able to subscribe this before this object exists...Do it in TCP-Server!
            //this.On_ClientConnectDisconnect(new TCPServerClientsEventArgs(1, this._clientIP, this._clientPort)); //Raise Event, that indicates a new client connection
        }

        /// <summary>
        /// Client SocketListener Destructor.
        /// </summary>
        ~TCPConnectionHandler()
        {
            this.StopSocketListener(); //The hard way: we can't mark us for deletion here, as it will be too late. We just can try to free ressources one last time...
        }
        #endregion

        #region Methods
        #region Public_Methods

        public int timeout
        {
            get
            {
                return this._client_timeout;
            }
        }

        /// <summary>
        /// Method that starts SocketListener Thread.
        /// </summary>
        public void StartSocketListener()
        {
            if (this._clientSocket != null)
            {
                this._clientListenerThread =
                    new Thread(new ThreadStart(ClientConnectionHandlerThreadStart));

                this._clientListenerThread.Name = "Clientlistener " + this.RemoteEndPoint.ToString();

                this._clientListenerThread.Start();
            }
        }

        /// <summary>
        /// Method that returns the state of this object i.e. whether this
        /// object is marked for deletion or not.
        /// </summary>
        /// <returns></returns>
        public bool IsMarkedForDeletion()
        {
            return this._markedForDeletion;
        }

        /// <summary>
        /// Converts a String to a Bytearray and adds it to the sendqueue which causes them to be send asynchronously
        /// </summary>
        /// <param name="message">The String to be sent</param>
        /// <returns>FALSE if sendqueue NULL, otherwise TRUE</returns>
        public bool SendString(String message)
        {
            Byte[] byteBuffer = this._Stringencoding.GetBytes(message); //Get the Bytes from the given String using the Classwide-Encoding
            return this.SendData(byteBuffer); //Add the Bytes to the Sendqueue
        }

        /// <summary>
        /// Adds a Bytearray to the sendqueue which causes them to be send asynchronously
        /// </summary>
        /// <param name="byteBuffer">Bytes to sent</param>
        /// <returns>FALSE if sendqueue NULL, otherwise TRUE</returns>
        public bool SendData(Byte[] byteBuffer)
        {
            if (this._sendqueue != null)
            {
                lock (this._sendqueue)
                {
                    foreach (Byte b in byteBuffer) //Add each byte from the given byte-array to the sendqueue
                    {
                        this._sendqueue.Enqueue(b);
                    }
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Method that stops Client SocketListening Thread.
        /// You should avoid calling this from inside as you may end up cutting the branch you're sitting on.
        /// StopSocketListener will be called by the server' purging-thread if we're marked for deletion
        /// </summary>
        public void StopSocketListener()
        {
            if (this._clientSocket != null)
            {
                this._stopClient = true;  // Tell the "this._clientListenerThread" to stop
                if (this.ClientIsConnected())
                {
                    this._clientSocket.Close(); //Gracefully close the clientsocket
                }

                // Wait for two seconds for the the thread to stop if it still alive
                if (this._clientListenerThread.IsAlive)
                {
                    this._clientListenerThread.Join(2000);
                }

                // If still alive; Get rid of the thread.
                if (this._clientListenerThread.IsAlive)
                {
                    this._clientListenerThread.Abort();
                }
                this._clientListenerThread = null;
                this._clientSocket = null;

                //this.MarkForDeletion(); //this would be...
                //... the wrong hirarchie. MarkForDeletion will mark this object for deletion. The server's purging thread will then call "StopSocketListener" later.
                //... then calling it again doesn't make sense
            }

        }
        #endregion

        #region Private_Methods

        /// <summary>
        /// Checks if the clientsocket is still connected
        /// </summary>
        /// <returns>True if connected otherwise false</returns>
        private bool ClientIsConnected()
        {
            try
            {

                bool result = !(this._clientSocket.Poll(1, SelectMode.SelectRead) && this._clientSocket.Available == 0); //Will check if client is still connected
                /*
                if (!result)
                {
                    //Debug
                }
                */
                return result;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        /// <summary>
        /// Thread method that does the communication to the client. This 
        /// thread tries to receive from client and if client sends any data
        /// then parses it and again wait for the client data to come in a
        /// loop. The recieve is an indefinite time receive.
        /// </summary>
        private void ClientConnectionHandlerThreadStart()
        {
            int size = 0;

            Byte[] byteBuffer = new Byte[this._buffersize];

            this._lastReceiveDateTime = DateTime.Now;    // reset last receive time
            this._currentReceiveDateTime = DateTime.Now; // reset current receive time

            /*
             * Start a timer.
             * On each timer tick (every few seconds [defined by timeout]), a callback-method will be executed...
             * ...which will then check if the connection is idle for too long or not
             * ...and terminate this session handling in necessary
             * 
            */
            Timer t = new Timer(new TimerCallback(CheckClientCommInterval), null, this.timeout * 1000, this.timeout * 1000);

            this._sendqueue = new Queue<byte>(); //Initialize the sending queue

            while (!this._stopClient) //Endless loop until the listenerthread should be stopped
            {
                try
                {
                    #region Send-Receive-Handling

                    #region checkconnection                    
                    if (!this.ClientIsConnected())
                    {
                        //Connection lost

                        throw new SocketException(10064); //Raise a Socketexception that should be handled in the catch-block...

                        /*
                        From https://docs.microsoft.com/de-de/windows/win32/winsock/windows-sockets-error-codes-2?redirectedfrom=MSDN
                        WSAEHOSTDOWN
                        
                        10064
                                               
                        Host is down.
                        A socket operation failed because the destination host is down.
                        A socket operation encountered a dead host.
                        Networking activity on the local host has not been initiated.
                        These conditions are more likely to be indicated by the error WSAETIMEDOUT.
                        */

                    }
                    #endregion

                    #region Receive
                    //Receive data 
                    while (this._clientSocket.Available > 0) //As long as there is data available on the socket
                    {   /*
                    This will cause the available data to be splitted on portions of the given buffersize and prevent the buffer from exceeding.
                    If the available data is longer than the buffersize this will cause multiple events, as ParseReceiveBuffer will raise such an event whenever its called                
                    */
                        byteBuffer = new Byte[this._buffersize]; //First empty the old buffer to remove remains from previous content

                        size = this._clientSocket.Receive(byteBuffer, this._buffersize, SocketFlags.Partial); //Read from socket and put its contents to byteBuffer; count the size
                        this._currentReceiveDateTime = DateTime.Now; // save last receive as current receive time
                        this.ParseReceiveBuffer(byteBuffer/*, size*/); //Parses received bytes and raises an event if subscribed                                      
                    }
                    #endregion

                    #region Sending
                    //Send data
                    lock (this._sendqueue) //(Thread-)lock the sendqueue, to prevent it from beeing accessed by another thread
                    {
                        while (this._sendqueue.Count > 0) //As long as there is data available in the Sendqueue
                        {
                            Byte[] byteBufferOut = new Byte[this._buffersize]; //Create a new sendbuffer

                            for (int i = 0; i < this._buffersize; i++) //Iterate from 0 the the defined buffersize...
                            {
                                if (this._sendqueue.Count > 0) //If there is still an element in the sendqueue
                                {
                                    Byte Tempb = this._sendqueue.Dequeue(); //Fetch the first element of the sendqueue...
                                    byteBufferOut[i] = Tempb; //...and add it to the sendbuffer
                                }
                                else
                                {
                                    break; //If there is not element left in the sendqueue, break out of the for-loop
                                }
                            }
                            this._clientSocket.Send(byteBufferOut); //Send the data in the buffer
                            if (this.OnDataSent != null)
                            {
                                //Raise data-sent-event
                                this.OnDataSent(this, new TCPSocketSendReceiveEventArgs(
                                    byteBufferOut,
                                    true,
                                    this._Stringencoding,
                                    _clientSocket.RemoteEndPoint)
                                    );
                            }
                        }

                    } //unlock the sendqueue again
                    #endregion
                    #endregion
                }
                catch (SocketException se)
                {
                    /*
                     * https://docs.microsoft.com/de-de/windows/win32/winsock/windows-sockets-error-codes-2
                     * Socketerror (either send or receive) occured, meaning that something is wrong with the connection
                    */

                    this.NonExistify(); // We need to mark us-self for deletion, as calling StopSocketListener directly will cut the branch we're sitting on: corrupt the whole calling hirachie
                    //this.StopSocketListener();                    
                }
                catch (Exception ex)
                {
                    //Todo: Log general errors
                }
                /*
                 * We need to pause the loop here for a short time to avoid using up all CPU-Ressources
                 * Note, that the longer the sleep-time, the greater the delay while sending and receiving data
                 * My recommendation: 3-20ms                 
                 */
                Thread.Sleep(this.ClientCycleWaitTime);
            }

            //Stop timer, after exiting while(). - When client should be stopped.
            t.Change(Timeout.Infinite, Timeout.Infinite);
            t = null;
        }

        /// <summary>
        /// This method parses data that is sent by a client using TCP/IP.
        /// As per the "Protocol" between client and this Listener, client 
        /// sends each line of information by appending "CRLF" (Carriage Return
        /// and Line Feed). But since the data is transmitted from client to 
        /// here by TCP/IP protocol, it is not guarenteed that each line that
        /// arrives ends with a "CRLF". So the job of this method is to make a
        /// complete line of information that ends with "CRLF" from the data
        /// that comes from the client and get it processed.
        /// </summary>
        /// <param name="byteBuffer"></param>        
        private void ParseReceiveBuffer(Byte[] byteBuffer/*, int size*/)
        {
            if (this.OnDataReceived != null)
            {
                this.OnDataReceived(this, new TCPSocketSendReceiveEventArgs(byteBuffer, false, this._Stringencoding, this._clientSocket.RemoteEndPoint));
            }
        }

        /// <summary>
        /// This will mark this client listener for deletion in a case where a connection was lost e.g.
        /// The Server's purgingThread will later do rest of the job by calling "StopSocketListener"
        /// This will also raise an event which indicates this situation
        /// </summary>
        private void NonExistify()
        {
            //Raise connection lost event
            this.On_ClientConnectDisconnect(new TCPServerClientsEventArgs(2, this._clientIP, this._clientPort)); //Raise Event, that indicates a client disconnected

            //Mark for deletion
            this._markedForDeletion = true;
        }

        /// <summary>
        /// Method that checks whether there are any client calls for the
        /// timeout-period (defined in the constructor) or not. If not this client SocketListener will
        /// be closed if clients should be disconnected automatically (autodisconnectclient defined in the constructor).
        /// </summary>
        /// <param name="o"></param>
        private void CheckClientCommInterval(object o)
        {
            if (this._lastReceiveDateTime.Equals(this._currentReceiveDateTime))
            {
                // _currentReceiveDateTime has not been updated since _lastReceiveDateTime has been set. Therefore no data has been received since then.

                if (this._autodisconnectclient)
                {
                    //If inactive clients should be disconnected after a certain amount of time and the timeout period exceeded...
                    this.NonExistify();
                }
            }
            else
            {
                /*
                 * Update the last receive-timestamp if _lastReceiveDateTime differs from _currentReceiveDateTime
                 * This way both are set to equal values so we can check it again on the next callback.
                 */
                this._lastReceiveDateTime = this._currentReceiveDateTime;
            }
        }


        #endregion
        #region eventraisers

        /// <summary>
        /// Will raise the event that indicates, that a client has just (dis-)connected
        /// </summary>
        /// <param name="args"></param>
        /// <returns>TRUE if subscribed and risen, otherwise FALSE</returns>
        public bool On_ClientConnectDisconnect(TCPServerClientsEventArgs args)
        {
            if (this.ClientConnectDisconnect != null)
            {
                this.ClientConnectDisconnect(this, args);
                return true;
            }
            return false;
        }
        #endregion
        #endregion

    }
}

