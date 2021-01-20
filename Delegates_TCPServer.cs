
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Net;
using System.Text;


namespace TCP_Server
{
    #region Delegates    
    #region TCPServer
    public delegate void TCPServerErrorEventHandler(object sender, TCPServerErrorEventArgs e); //Servererror
    public delegate void TCPServerStateEventHandler(object sender, TCPServerStateEventArgs e); //Serverstate
    public delegate void TCPServerClientsEventHandler(object sender, TCPServerClientsEventArgs e); //Server-Clients
    public delegate void CleanUpEventHandler();
    #endregion

    #region SocketListener
    public delegate void TCPSocketSendReceiveHandler(object sender, TCPSocketSendReceiveEventArgs e); //Send- / Receive
    #endregion


    #endregion

    #region SocketListener
    public class TCPSocketSendReceiveEventArgs : EventArgs
    {
        private Encoding Stringencoding;

        #region properties

        /// <summary>
        /// The data as a byte-array
        /// </summary>
        public byte[] Data { get; }
        
        /// <summary>
        /// Returns a string from the date stored as a byte-array using the given encoding
        /// </summary>
        public String Message {
            /*
             * A String consists of a Array of Bytes which are represented as characters using a specific encoding and is usually terminated by a NULL-Byte
             * 
             * Example (using ASCII-Encoding):
             *      Bytearray[8]:
             *              [0] = 0x48 -> 'H'
             *              [1] = 0x65 -> 'e'
             *              [2] = 0x6c -> 'l'
             *              [3] = 0x6c -> 'l'
             *              [4] = 0x6f -> 'o'
             *              [5] = 0x00 -> NULL
             *              [6] = 0x00 -> NULL
             *              [7] = 0x00 -> NULL
             *      String: "Hello"
             */
            get
            {
                return Stringencoding.GetString(this.Data);
            }
        }

        /// <summary>
        /// The Endpoint from which the data has been received or to which it should be sent
        /// </summary>
        public EndPoint R_Endpoint { get;  }
        #endregion

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="_data">Data in Byte-Array-Format</param>
        /// <param name="_isSending">FALSE if data is received, otherwise TRUE</param>
        /// <param name="_Stringencoding">Encoding to be used to convert data to string</param>
        /// <param name="RemoteEndPoint">Remote-Endpoint to which we are communicating</param>
        #region constructor
        public TCPSocketSendReceiveEventArgs(byte[] _data, bool _isSending, Encoding _Stringencoding, EndPoint RemoteEndPoint)
        {
            this.Stringencoding = _Stringencoding;
            this.Data = _data; //Data is given as a byte-array - store it that way
            this.R_Endpoint = RemoteEndPoint;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="_message">Data in String-Format</param>
        /// <param name="_isSending">FALSE if data is received, otherwise TRUE</param>
        /// <param name="_Stringencoding">Encoding to be used to convert string to byte-array</param>
        /// <param name="RemoteEndPoint">Remote-Endpoint to which we are communicating</param>
        public TCPSocketSendReceiveEventArgs(String _message, bool _isSending, Encoding _Stringencoding, EndPoint RemoteEndPoint)
        {
            this.Stringencoding = _Stringencoding;
            this.Data = _Stringencoding.GetBytes(_message); //Data is given as a String. Store it as a byte-array
            this.R_Endpoint = RemoteEndPoint;
        }
        
        #endregion
    }
    #endregion

    #region TCPServer
    /// <summary>
    /// Eventargs for server's-client-states
    /// </summary>
    public class TCPServerClientsEventArgs : EventArgs
    {
        #region Dictionary
        //Dictionary that translates Clientscodes to Textmessages
        private static readonly Dictionary<uint, String> TCPServer_Clientstatecodes = new Dictionary<uint, string> {
             { 1, "Client connected" },
             { 2, "Client disconnected" }
        };
        #endregion

        #region Properties
        public uint _TCPServer_Clientstatecode { get; }

        private EndPoint _REndpoint;
        private String _EPIP;
        private UInt16 _EPPort;


        /// <summary>
        /// Gets the RemoteIP of the client that just connected/disconnected
        /// </summary>
        public String RemoteIP
        {
            get
            {
                return this._EPIP;
            }
        }

        /// <summary>
        /// Gets the RemotePort of the client that just connected/disconnected
        /// </summary>
        public UInt16 RemotePort
        {
            get
            {
                return this._EPPort;
            }
        }


        /// <summary>
        /// Gets the RemoteEndpoint that connected or disconnected. 
        /// NOTE that a disconnected client may result to a NULL-reference. You probably should use "RemoteIP" and "RemotePort" instead!
        /// </summary>
        public EndPoint RemoteEndpoint
        {
            get
            {
                return this._REndpoint;
            }
        }

        public String TCPServer_Clientstatemessage
        { //Returns the Statemessage to the corresponding Statecode from the Static Dictionary
            get
            {
                return TCPServerClientsEventArgs.TCPServer_Clientstatecodes[this._TCPServer_Clientstatecode];
            }
        }
        #endregion

        #region Constructor
        //public TCPServerClientsEventArgs(uint _statecode, EndPoint RemoteEndPoint)
        public TCPServerClientsEventArgs(uint _statecode, EndPoint RemoteEndpoint)
        {
            this._TCPServer_Clientstatecode = _statecode;
            this._REndpoint = RemoteEndpoint;

            //Convert the remote Endpoint to an IP-Address
            IPEndPoint ipcl = (IPEndPoint)RemoteEndpoint;
            this._EPIP = ipcl.Address.ToString();

            //Convert the remote Endpoint to an Port.
            this._EPPort = Convert.ToUInt16(ipcl.Port);
        }

        public TCPServerClientsEventArgs(uint _statecode, String clIPAddr, UInt16 clPort)
        {
            this._TCPServer_Clientstatecode = _statecode;
            this._REndpoint = null;

            this._EPIP = clIPAddr;

            this._EPPort = clPort;
        }
        #endregion
    }

    /// <summary>
    /// Eventargs for server's-status-messages
    /// </summary>
    public class TCPServerStateEventArgs : EventArgs
    {
        #region Dictionary
        //Dictionary that translates Statecodes to Textmessages
        private static readonly Dictionary<uint, String> TCPServer_Statecodes = new Dictionary<uint, string> {
             { 1, "Server is initializing." },
             { 2, "Server is starting up" },
             { 3, "Server is shutting down." },
             { 4, "Server is running." },
             { 5, "Server is stopped." },
             { 6, "Serverstate unknown." },
        };
        #endregion

        #region Properties
        public uint TCPServer_Statecode { get; }
        public String TCPServer_Statemessage
        { //Returns the Statemessage to the corresponding Statecode from the Static Dictionary
            get
            {
                return TCPServerStateEventArgs.TCPServer_Statecodes[this.TCPServer_Statecode];
            }
        }
        #endregion

        #region Constructor
        public TCPServerStateEventArgs(uint _statecode)
        {
            this.TCPServer_Statecode = _statecode;
        }
        #endregion
    }

    /// <summary>
    /// Eventargs for server's-error-messages
    /// </summary>
    public class TCPServerErrorEventArgs : EventArgs
    {
        #region Dictionary
        //Dictionary that translates Errorcodes to Textmessages
        private static readonly Dictionary<uint, String> TCPServer_Errorcodes =new Dictionary<uint, string> {
                { 1, "Error during intializing the server." },
                { 2, "Error during starting the server." },
                { 3, "Error during stopping the server." },
                { 4, "Socketerror." }
        };
        #endregion

        #region Properties
        public uint TCPServer_Errorcode { get; }
        public String TCPServer_Errormessage { //Returns the Errormessage to the corresponding Errorcode from the Static Dictionary
            get {
                return TCPServerErrorEventArgs.TCPServer_Errorcodes[this.TCPServer_Errorcode];
            }
        }
        public Exception TCPServer_Exception { get; }
        #endregion

        #region Constructor
        public TCPServerErrorEventArgs (uint _errorcode, Exception _e)
        {
            this.TCPServer_Errorcode = _errorcode;
            this.TCPServer_Exception = _e;
        }
        #endregion

    }
    #endregion

}
