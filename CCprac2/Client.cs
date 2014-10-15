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
        public int RemotePort { get; private set; }
        TcpClient socket;
        StreamReader clientIn;
        StreamWriter clientOut;
        Queue<string> messageQueue;
        MessageRecievedHandler mHandler;
        ConnectionCLosedHandler cHandler;
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

            clientOut.WriteLine(Program.myPort);

            RemotePort = int.Parse(clientIn.ReadLine());

            Console.WriteLine("Verbonden: " + Program.ConvertToPort(RemotePort));

            Thread t = new Thread(ReadMessages);
            t.Start();
        }

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

        public void SendMessage()
        {
            if (messageQueue != null)
                lock (messageQueue)
                {
                    if (messageQueue.Count > 0)
                        SendMessage(messageQueue.Dequeue());
                }
        }

        void SendMessage(string message)
        {
            clientOut.WriteLine(message);
            //Console.WriteLine("//sent something to: " + RemotePort);
        }

        public enum DisconnectReason
        {
            Command,
            Message,
            Termination
        }

        public void Disconnect(DisconnectReason reason)
        {
            keepAlive = false;
            if (reason == DisconnectReason.Command) SendMessage("closing");
            socket.Close();
            clientOut.Dispose();
            clientIn.Dispose();
            Console.WriteLine("Verbroken: " + Program.ConvertToPort(RemotePort));
            Console.WriteLine("//Disconnected from: " + RemotePort + " | Reason: " + reason);
            cHandler(RemotePort);
        }

        void ReadMessages()
        {
            while (keepAlive)
            {
                string s = "null";
                try
                {
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
