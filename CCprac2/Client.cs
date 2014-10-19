using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetwProg
{
    class Client
    {
        /// <summary>
        /// Port identifying this connection
        /// </summary>
        public int RemotePort { get; private set; }

        //socket and streams
        TcpClient socket;
        StreamReader clientIn;
        StreamWriter clientOut;

        //buffer
        Queue<string> messageQueue;

        //handlers for handling messages and disconnects
        MessageRecievedHandler mHandler;
        ConnectionCLosedHandler cHandler;

        //flag telling if the connection needs to live
        //needed to terminate thread that handles incoming messages
        bool keepAlive = true;

        public delegate void MessageRecievedHandler(string message, int remotePort);

        public delegate void ConnectionCLosedHandler(int remotePort);

        public Client(TcpClient socket, MessageRecievedHandler mHandler, ConnectionCLosedHandler cHandler)
        {
            this.socket = socket;
            this.mHandler = mHandler;
            this.cHandler = cHandler;

            clientIn = new StreamReader(socket.GetStream());
            clientOut = new StreamWriter(socket.GetStream());

            clientOut.AutoFlush = true;

            //exchange port numbers with remote hosts;
            clientOut.WriteLine(Program.myPort);
            RemotePort = int.Parse(clientIn.ReadLine());

            Console.WriteLine("Verbonden: " + Program.ConvertToPort(RemotePort));

            //start the message handling thread
            Thread t = new Thread(ReadMessages);
            t.Start();
        }

        /// <summary>
        /// assigns the correct buffer to this client and flushed all messages currently in it
        /// </summary>
        /// <param name="queue">the buffer</param>
        public void SetQueue(Queue<string> queue)
        {
            this.messageQueue = queue;

            lock (messageQueue)
            {
                while (messageQueue.Count > 0)
                {
                    SendMessage(messageQueue.Dequeue());
                }
            }
        }

        /// <summary>
        /// sends the first message in the buffer, if assigned
        /// </summary>
        public void SendMessage()
        {
            if (messageQueue != null)
                lock (messageQueue)
                {
                    if (messageQueue.Count > 0)
                        SendMessage(messageQueue.Dequeue());
                }
        }

        /// <summary>
        /// sends a message into the outgoing stream
        /// </summary>
        /// <param name="message">the message</param>
        void SendMessage(string message)
        {
            try
            {
                clientOut.WriteLine(message);
            }
            catch { }
        }

        public enum DisconnectReason
        {
            Command,
            Message,
            Termination
        }
        
        /// <summary>
        /// terminates this connection
        /// </summary>
        /// <param name="reason">reason</param>
        public void Disconnect(DisconnectReason reason)
        {
            //clear buffer
            lock (messageQueue)
            {
                messageQueue.Clear();
            }

            //set flag so message reading thread terminates
            keepAlive = false;

            //if disconnect was commanded on this side: notify other end
            if (reason == DisconnectReason.Command) SendMessage("closing");

            //close the socket (this will unblock the message reading thread if its waiting on a new message with an exception)
            socket.Close();

            //dispose streams
            clientOut.Dispose();
            clientIn.Dispose();

            //notify
            Console.WriteLine("Verbroken: " + Program.ConvertToPort(RemotePort));
            Console.WriteLine("//Disconnected from: " + RemotePort + " | Reason: " + reason);

            //call the handler to handle the disconnect
            cHandler(RemotePort);
        }

        void ReadMessages()
        {
            while (keepAlive)
            {
                string s = "null";
                try
                {
                    //read incoming messages
                    s = clientIn.ReadLine();
                }
                catch 
                { 
                    Console.WriteLine("//Disconnect from " + Program.ConvertToPort(RemotePort)); 
                }
                    //Console.WriteLine("//got something from: " + RemotePort);
                if (s == "closing")
                    Disconnect(DisconnectReason.Message);
                else if (s != "null" && s != null)
                    mHandler(s, RemotePort);
            }
        }

    }
}
